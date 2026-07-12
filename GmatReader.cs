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
            
            var afileInst = manager.LoadAssetsFileFromBundle(bunInst, 0, false);
            var afile = afileInst.file;

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
                
                int n = Math.Max(2, (int)Math.Round(Math.Sqrt(Math.Max(2, (w * h) / (128 * 128)))));
                cols = n; rows = n;
            }
            result.FlipbookCols = Math.Min(cols, 128);
            result.FlipbookRows = Math.Min(rows, 128);
        }

        static Bitmap DecodeTexture(AssetTypeValueField texField, AssetsFileInstance afileInst)
        {
            var texFile = TextureFile.ReadTextureFile(texField);
            
            byte[] raw = texFile.FillPictureData(afileInst);
            byte[] bgra = texFile.DecodeTextureRaw(raw, true);
            int w = texFile.m_Width, h = texFile.m_Height;
            if (bgra == null || w <= 0 || h <= 0) return null;

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            
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
