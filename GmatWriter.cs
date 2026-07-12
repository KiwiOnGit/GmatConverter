using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GmatConverter
{
    
    public static class GmatWriter
    {
        public static void Write(SimpleMaterial mat, string outPath)
        {
            if (File.Exists(outPath)) File.Delete(outPath);
            using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);

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

            var m = new JObject
            {
                ["format"] = "simple",
                ["color"] = new JArray(mat.ColorR, mat.ColorG, mat.ColorB),
                ["customColors"] = mat.CustomColors,
                ["tiling"] = new JArray(mat.TilingX, mat.TilingY),
                ["offset"] = new JArray(mat.OffsetX, mat.OffsetY),
                ["texture"] = "albedo.png",
                ["shader"] = mat.ShaderName,
                ["animation"] = new JObject
                {
                    ["type"] = mat.Animation,
                    ["cols"] = mat.FlipbookCols,
                    ["rows"] = mat.FlipbookRows,
                    ["speed"] = mat.AnimSpeed
                }
            };
            if (mat.TaggedTexture != null)
                m["taggedTexture"] = "tagged.png";
            WriteText(zip, "material.json", m.ToString(Formatting.Indented));

            if (mat.Texture != null)
            {
                var entry = zip.CreateEntry("albedo.png", CompressionLevel.Optimal);
                using var es = entry.Open();
                mat.Texture.Save(es, ImageFormat.Png);
            }
            if (mat.TaggedTexture != null)
            {
                var entry = zip.CreateEntry("tagged.png", CompressionLevel.Optimal);
                using var es = entry.Open();
                mat.TaggedTexture.Save(es, ImageFormat.Png);
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
