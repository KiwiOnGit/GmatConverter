using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GmatConverter
{
    // Writes the "simple" .gmatplus format: a zip of { package.json, material.json, albedo.png }.
    // The presence of material.json is what the mod uses to tell this apart from a normal
    // AssetBundle-based .gmatplus. Because it's a plain PNG + JSON, the mod can build the
    // material directly on the game's live shader with zero Unity-version/AssetBundle risk.
    public static class GmatWriter
    {
        public static void Write(SimpleMaterial mat, string outPath)
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);

            // package.json -- same schema the mod already reads, so it lists in the picker.
            var pkg = new JObject
            {
                ["androidFileName"] = "",
                ["pcFileName"] = "",
                ["descriptor"] = new JObject
                {
                    ["objectName"] = mat.Name,
                    ["author"] = mat.Author,
                    ["description"] = mat.Description
                },
                ["config"] = new JObject
                {
                    ["customColors"] = mat.CustomColors,
                    ["disableInPublicLobbies"] = false
                }
            };
            WriteText(zip, "package.json", pkg.ToString(Formatting.Indented));

            // material.json -- the simple-format payload.
            var m = new JObject
            {
                ["format"] = "simple",
                ["color"] = new JArray(mat.ColorR, mat.ColorG, mat.ColorB),
                ["customColors"] = mat.CustomColors,
                ["tiling"] = new JArray(mat.TilingX, mat.TilingY),
                ["offset"] = new JArray(mat.OffsetX, mat.OffsetY),
                ["texture"] = "albedo.png",
                ["animation"] = new JObject
                {
                    ["type"] = mat.Animation,
                    ["cols"] = mat.FlipbookCols,
                    ["rows"] = mat.FlipbookRows,
                    ["speed"] = mat.AnimSpeed
                }
            };
            WriteText(zip, "material.json", m.ToString(Formatting.Indented));

            if (mat.Texture != null)
            {
                var entry = zip.CreateEntry("albedo.png", CompressionLevel.Optimal);
                using var es = entry.Open();
                mat.Texture.Save(es, ImageFormat.Png);
            }
        }

        static void WriteText(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var sw = new StreamWriter(entry.Open());
            sw.Write(content);
        }
    }
}
