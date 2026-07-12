using System;
using System.Windows.Forms;

namespace GmatConverter
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Headless test/CLI mode: `GmatConverter --extract <file.gmat>` prints what it
            // parsed and (optionally) writes a .gmatplus next to it. Used for verification;
            // double-clicking the exe launches the GUI.
            if (args.Length >= 2 && args[0] == "--extract")
            {
                try
                {
                    var mat = GmatReader.Read(args[1]);
                    Console.WriteLine($"Name: {mat.Name}");
                    Console.WriteLine($"Author: {mat.Author}");
                    Console.WriteLine($"CustomColors: {mat.CustomColors}");
                    Console.WriteLine($"Color: {mat.ColorR:0.###},{mat.ColorG:0.###},{mat.ColorB:0.###}");
                    Console.WriteLine($"Tiling: {mat.TilingX}x{mat.TilingY}  Offset: {mat.OffsetX},{mat.OffsetY}");
                    Console.WriteLine($"Animation: {mat.Animation} cols={mat.FlipbookCols} rows={mat.FlipbookRows} speed={mat.AnimSpeed}");
                    Console.WriteLine($"Texture: {(mat.Texture != null ? $"{mat.Texture.Width}x{mat.Texture.Height}" : "NONE")}");
                    if (args.Length >= 3)
                    {
                        GmatWriter.Write(mat, args[2]);
                        Console.WriteLine($"Wrote {args[2]}");
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: " + ex);
                    return 1;
                }
            }

            // Headless sanity check for the bundled gorilla preview meshes: `GmatConverter
            // --testmesh` parses all three embedded .asset files and reports vertex/triangle
            // counts, so a bad parse fails fast without opening the GUI.
            if (args.Length >= 1 && args[0] == "--testmesh")
            {
                try
                {
                    foreach (var (label, mesh) in new (string, SimpleMesh)[]
                    {
                        ("Body", GorillaModel.Body),
                        ("Face", GorillaModel.Face),
                        ("Chest", GorillaModel.Chest),
                    })
                    {
                        if (mesh == null)
                        {
                            Console.WriteLine($"{label}: not found locally (gitignored -- see README.md)");
                            continue;
                        }
                        System.Numerics.Vector3 min = mesh.Positions[0], max = mesh.Positions[0];
                        foreach (var p in mesh.Positions)
                        {
                            min = System.Numerics.Vector3.Min(min, p);
                            max = System.Numerics.Vector3.Max(max, p);
                        }
                        Console.WriteLine($"{label}: {mesh.Positions.Length} verts, {mesh.Indices.Length / 3} tris, bounds min={min} max={max}");
                    }
                    Console.WriteLine($"FaceChestTexture: {(GorillaModel.FaceChestTexture != null ? $"{GorillaModel.FaceChestTexture.Width}x{GorillaModel.FaceChestTexture.Height}" : "NONE")}");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: " + ex);
                    return 1;
                }
            }

            // Headless render check: `GmatConverter --renderpreview <out.png> [file.gmat]`
            // renders one frame of the 3D preview (optionally with a real material loaded) to
            // a PNG using the exact same GorillaPreviewRenderer the GUI's preview panel calls,
            // with no window/screen capture involved -- lets the render be eyeballed from a
            // file instead of a live screenshot.
            if (args.Length >= 2 && args[0] == "--renderpreview")
            {
                try
                {
                    System.Drawing.Bitmap tex = null;
                    System.Drawing.Color tint = System.Drawing.Color.White;
                    if (args.Length >= 3)
                    {
                        var mat = GmatReader.Read(args[2]);
                        tex = mat.Texture;
                        tint = System.Drawing.Color.FromArgb(
                            (int)(mat.ColorR * 255), (int)(mat.ColorG * 255), (int)(mat.ColorB * 255));
                    }
                    float yaw = args.Length >= 4 ? float.Parse(args[3]) : 0.5f;
                    float pitch = args.Length >= 5 ? float.Parse(args[4]) : -0.15f;
                    using var bmp = GorillaPreviewRenderer.Render(tex, System.Numerics.Vector2.One, System.Numerics.Vector2.Zero,
                        tint, yaw, pitch, distance: 2.6f, w: 500, h: 500);
                    bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine($"Wrote {args[1]}");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR: " + ex);
                    return 1;
                }
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args.Length >= 1 ? args[0] : null));
            return 0;
        }
    }
}
