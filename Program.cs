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

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(args.Length >= 1 ? args[0] : null));
            return 0;
        }
    }
}
