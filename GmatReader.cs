using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Newtonsoft.Json.Linq;

namespace GmatConverter
{
    // Reads an old .gmat (zip: package.json + Unity AssetBundle), pulls out the material's
    // texture, tint, tiling and any animation hint, entirely in managed code (AssetsTools.NET
    // + its texture decoder) -- no Unity Editor, no external tools.
    public static class GmatReader
    {
        public static SimpleMaterial Read(string gmatPath)
        {
            var result = new SimpleMaterial();

            byte[] bundleBytes = null;
            string bundleEntryName = null;

            using (var zip = ZipFile.OpenRead(gmatPath))
            {
                var jsonEntry = zip.Entries.FirstOrDefault(e => e.Name == "package.json");
                if (jsonEntry != null)
                {
                    using var sr = new StreamReader(jsonEntry.Open());
                    JObject json = JObject.Parse(sr.ReadToEnd());
                    result.Name = (string)json["descriptor"]?["objectName"] ?? Path.GetFileNameWithoutExtension(gmatPath);
                    result.Author = (string)json["descriptor"]?["author"] ?? "Author";
                    result.Description = (string)json["descriptor"]?["description"] ?? "";
                    result.CustomColors = (bool?)json["config"]?["customColors"] ?? false;
                    bundleEntryName = (string)json["pcFileName"];
                }

                ZipArchiveEntry bundleEntry = null;
                if (!string.IsNullOrEmpty(bundleEntryName))
                    bundleEntry = zip.Entries.FirstOrDefault(e => e.Name == bundleEntryName);
                // Fall back to the first non-json entry.
                bundleEntry ??= zip.Entries.FirstOrDefault(e => e.Name != "package.json");
                if (bundleEntry == null)
                    throw new Exception("No AssetBundle found inside the .gmat package.");

                using var ms = new MemoryStream();
                bundleEntry.Open().CopyTo(ms);
                bundleBytes = ms.ToArray();
            }

            ExtractFromBundle(bundleBytes, result);
            return result;
        }

        static void ExtractFromBundle(byte[] bundleBytes, SimpleMaterial result)
        {
            var manager = new AssetsManager();
            var bunInst = manager.LoadBundleFile(new MemoryStream(bundleBytes), "bundle");
            // Decompress if needed (LZ4/LZMA) so the assets file can be read.
            var afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
            var afile = afileInst.file;

            // --- Material ---
            AssetTypeValueField matField = null;
            foreach (var info in afile.GetAssetsOfType(AssetClassID.Material))
            {
                matField = manager.GetBaseField(afileInst, info);
                break;
            }

            long mainTexPathId = 0;
            if (matField != null)
            {
                var props = matField["m_SavedProperties"];

                // Colors. Prefer the conventional tint names; if the shader doesn't use them
                // (Shader Forge and other hand-rolled shaders invent their own names), fall
                // back to the first plausible tint rather than silently defaulting to white --
                // a defaulted-white tint over a texture that failed to bind elsewhere is the
                // classic "conversion looks white in-game" symptom.
                var colors = props["m_Colors"]["Array"];
                AssetTypeValueField tintField = null;
                foreach (var c in colors)
                    if (PropName(c) is "_Color" or "_BaseColor") { tintField = c; break; }
                if (tintField == null)
                {
                    foreach (var c in colors)
                    {
                        string lk = PropName(c).ToLowerInvariant();
                        if (lk.Contains("emission") || lk.Contains("outline") || lk.Contains("specular") ||
                            lk.Contains("rim") || lk.Contains("hsv") || lk.Contains("stencil")) continue;
                        tintField = c;
                        break;
                    }
                }
                if (tintField != null)
                {
                    var col = tintField["second"];
                    result.ColorR = col["r"].AsFloat;
                    result.ColorG = col["g"].AsFloat;
                    result.ColorB = col["b"].AsFloat;
                }

                // Texture envs (texture ref + tiling/offset)
                var texEnvs = props["m_TexEnvs"]["Array"];
                var texByName = new Dictionary<string, AssetTypeValueField>();
                foreach (var te in texEnvs)
                    texByName[PropName(te)] = te;

                AssetTypeValueField chosen = null;
                foreach (var key in new[] { "_MainTex", "_BaseMap" })
                    if (texByName.TryGetValue(key, out chosen) && chosen["second"]["m_Texture"]["m_PathID"].AsLong != 0)
                        break;
                    else chosen = null;
                if (chosen == null)
                {
                    // First bound texture that isn't a normal/mask/etc map.
                    foreach (var kv in texByName)
                    {
                        string lk = kv.Key.ToLowerInvariant();
                        if (lk.Contains("bump") || lk.Contains("normal") || lk.Contains("mask") ||
                            lk.Contains("occlusion") || lk.Contains("detail") || lk.Contains("metallic") ||
                            lk.Contains("gloss") || lk.Contains("specular") || lk.Contains("emission")) continue;
                        if (kv.Value["second"]["m_Texture"]["m_PathID"].AsLong != 0) { chosen = kv.Value; break; }
                    }
                }
                if (chosen != null)
                {
                    mainTexPathId = chosen["second"]["m_Texture"]["m_PathID"].AsLong;
                    result.TilingX = chosen["second"]["m_Scale"]["x"].AsFloat;
                    result.TilingY = chosen["second"]["m_Scale"]["y"].AsFloat;
                    result.OffsetX = chosen["second"]["m_Offset"]["x"].AsFloat;
                    result.OffsetY = chosen["second"]["m_Offset"]["y"].AsFloat;
                }

                // Shader family hint: a coarse, honest diagnostic (never a guessed exact shader
                // name -- resolving the real name would mean loading the referenced external
                // asset file, which usually isn't available offline). If the bundle depends on
                // Unity's built-in shader library, the source material almost certainly used
                // Standard/Legacy -- the shader family the Gorilla Tag engine upgrade broke,
                // and the most common reason a property ends up missing here.
                bool usesBuiltinShaderLib = false;
                foreach (var ext in afile.Metadata.Externals)
                {
                    string p = ext.PathName ?? "";
                    if (p.IndexOf("unity_builtin_extra", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.IndexOf("unity default resources", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        usesBuiltinShaderLib = true;
                        break;
                    }
                }
                result.ShaderHint = usesBuiltinShaderLib
                    ? "Legacy Built-in shader (Standard/Legacy family)"
                    : "Custom/bundled shader";

                if (Environment.GetEnvironmentVariable("GMAT_DUMP") == "1")
                {
                    Console.Error.WriteLine("-- SHADER --");
                    Console.Error.WriteLine($"   hint={result.ShaderHint}");
                    for (int i = 0; i < afile.Metadata.Externals.Count; i++)
                        Console.Error.WriteLine($"   external[{i + 1}] = {afile.Metadata.Externals[i].PathName} guid={afile.Metadata.Externals[i].Guid}");
                    Console.Error.WriteLine("-- COLORS --");
                    foreach (var c in props["m_Colors"]["Array"])
                        Console.Error.WriteLine($"   {PropName(c)} = {c["second"]["r"].AsFloat},{c["second"]["g"].AsFloat},{c["second"]["b"].AsFloat},{c["second"]["a"].AsFloat}");
                    Console.Error.WriteLine("-- FLOATS --");
                    foreach (var f in props["m_Floats"]["Array"])
                        Console.Error.WriteLine($"   {PropName(f)} = {f["second"].AsFloat}");
                    Console.Error.WriteLine("-- TEXENVS --");
                    foreach (var te in props["m_TexEnvs"]["Array"])
                        Console.Error.WriteLine($"   {PropName(te)} scale={te["second"]["m_Scale"]["x"].AsFloat},{te["second"]["m_Scale"]["y"].AsFloat} texPath={te["second"]["m_Texture"]["m_PathID"].AsLong}");
                }

                DetectAnimation(props, result);
            }

            // --- Texture ---
            AssetTypeValueField texField = null;
            if (mainTexPathId != 0)
            {
                var info = afile.GetAssetInfo(mainTexPathId);
                if (info != null) texField = manager.GetBaseField(afileInst, info);
            }
            if (texField == null)
            {
                foreach (var info in afile.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    texField = manager.GetBaseField(afileInst, info);
                    break;
                }
            }
            if (texField != null)
                result.Texture = DecodeTexture(texField, afileInst);

            RefineFlipbook(result);
        }

        // Property key in a SavedProperties pair; layout differs slightly across versions
        // (a plain string, or a struct with a "name" field).
        static string PropName(AssetTypeValueField pair)
        {
            var first = pair["first"];
            if (first.Children != null && first.Children.Count > 0 && first["name"] != null && !first["name"].IsDummy)
                return first["name"].AsString;
            try { return first.AsString; } catch { return ""; }
        }

        static void DetectAnimation(AssetTypeValueField props, SimpleMaterial result)
        {
            var floatNames = new HashSet<string>();
            foreach (var f in props["m_Floats"]["Array"]) floatNames.Add(PropName(f));
            float GetFloat(string n)
            {
                foreach (var f in props["m_Floats"]["Array"])
                    if (PropName(f) == n) return f["second"].AsFloat;
                return 0f;
            }
            // Vector props live in m_Colors for shaders that pack them there, or a separate
            // block; check both color-typed vectors and floats.
            if (floatNames.Contains("_Grid") && floatNames.Contains("_Density"))
            {
                result.Animation = "scroll";
                result.AnimSpeed = 0.4f;
            }
            else if (floatNames.Contains("_HSVRangeMin") || floatNames.Contains("_HSVRangeMax"))
            {
                result.Animation = "hue";
                result.AnimSpeed = floatNames.Contains("_Speed") ? Math.Max(0.05f, GetFloat("_Speed")) : 1f;
            }
            else
            {
                // A flipbook/scroll sprite-sheet shader. _XColsYRowsZSpeed nominally packs
                // (cols, rows, speed), but Shader-Forge variants (e.g. birds2chainz) stuff
                // scroll parameters in there instead, so the raw numbers can't be trusted as a
                // literal grid. Just flag it here; RefineFlipbook() derives the real grid from
                // the decoded texture's aspect ratio after it's loaded.
                foreach (var c in props["m_Colors"]["Array"])
                {
                    if (PropName(c) == "_XColsYRowsZSpeed")
                    {
                        result.Animation = "flipbook";
                        float sp = Math.Abs(c["second"]["b"].AsFloat);
                        result.AnimSpeed = sp > 0.001f ? sp : 0.5f;
                    }
                }
            }
        }

        // A sprite-sheet is stored as one tall (or wide) strip; the frame grid is whatever
        // makes each frame roughly square. Trust an explicit small grid only if it actually
        // matches the texture; otherwise infer from the aspect ratio.
        static void RefineFlipbook(SimpleMaterial result)
        {
            if (result.Animation != "flipbook" || result.Texture == null)
                return;
            int w = result.Texture.Width, h = result.Texture.Height;
            if (w <= 0 || h <= 0) return;

            int cols = 1, rows = 1;
            if (h >= w * 3 / 2)
            {
                cols = 1;
                rows = Math.Max(2, (int)Math.Round((double)h / w));
            }
            else if (w >= h * 3 / 2)
            {
                rows = 1;
                cols = Math.Max(2, (int)Math.Round((double)w / h));
            }
            else
            {
                // Near-square sheet: assume a square grid of frames.
                int n = Math.Max(2, (int)Math.Round(Math.Sqrt(Math.Max(2, (w * h) / (128 * 128)))));
                cols = n; rows = n;
            }
            result.FlipbookCols = Math.Min(cols, 128);
            result.FlipbookRows = Math.Min(rows, 128);
        }

        static Bitmap DecodeTexture(AssetTypeValueField texField, AssetsFileInstance afileInst)
        {
            var texFile = TextureFile.ReadTextureFile(texField);
            // FillPictureData pulls the encoded bytes (including from a resS stream if the
            // bundle stored them separately); DecodeTextureRaw turns them into BGRA32.
            byte[] raw = texFile.FillPictureData(afileInst);
            byte[] bgra = texFile.DecodeTextureRaw(raw, true);
            int w = texFile.m_Width, h = texFile.m_Height;
            if (bgra == null || w <= 0 || h <= 0) return null;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            // Unity texture data is bottom-up; flip rows into the top-down Bitmap.
            int stride = w * 4;
            byte[] flipped = new byte[bgra.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(bgra, y * stride, flipped, (h - 1 - y) * stride, stride);
            Marshal.Copy(flipped, 0, data.Scan0, flipped.Length);
            bmp.UnlockBits(data);
            return bmp;
        }
    }
}
