using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace GmatConverter
{
    public class MainForm : Form
    {
        SimpleMaterial mat;
        string loadedPath;

        // Left-column editors
        TextBox nameBox, authorBox, descBox, tileXBox, tileYBox, offXBox, offYBox, colsBox, rowsBox, speedBox;
        CheckBox customColorsBox;
        Button colorButton;
        Panel colorSwatch;
        ComboBox animBox;
        Label texInfoLabel;

        // Right preview
        PreviewPanel preview;
        System.Windows.Forms.Timer timer;
        float animT;

        public MainForm(string initialFile)
        {
            Text = "GMAT → GMATPLUS Converter";
            Width = 940;
            Height = 620;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(30, 30, 34);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            BuildUi();

            timer = new System.Windows.Forms.Timer { Interval = 33 };
            timer.Tick += (s, e) => { animT += 0.033f; preview.Invalidate(); };
            timer.Start();

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
                LoadFile(initialFile);
        }

        void BuildUi()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(22, 22, 26), Padding = new Padding(8, 8, 8, 8) };
            var openBtn = Btn("Open .gmat", 110);
            openBtn.Click += (s, e) => OpenDialog();
            var exportBtn = Btn("Export .gmatplus", 140);
            exportBtn.Click += (s, e) => ExportDialog();
            top.Controls.Add(openBtn);
            top.Controls.Add(exportBtn);
            Controls.Add(top);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 380, BackColor = Color.FromArgb(30, 30, 34) };
            Controls.Add(split);
            split.Panel1.AutoScroll = true;
            split.Panel1.Padding = new Padding(10);

            int y = 6;
            void Row(string label, Control c, int h = 24)
            {
                var l = new Label { Text = label, Left = 6, Top = y + 3, Width = 110, ForeColor = Color.Silver };
                c.Left = 122; c.Top = y; c.Width = 230; c.Height = h;
                split.Panel1.Controls.Add(l);
                split.Panel1.Controls.Add(c);
                y += h + 8;
            }

            nameBox = Tb(); Row("Name", nameBox);
            authorBox = Tb(); Row("Author", authorBox);
            descBox = Tb(); Row("Description", descBox);

            customColorsBox = new CheckBox { Text = "Custom colors (tint by player color in-game)", ForeColor = Color.Silver, AutoSize = true };
            customColorsBox.CheckedChanged += (s, e) => { if (mat != null) mat.CustomColors = customColorsBox.Checked; };
            Row("", customColorsBox);

            colorButton = Btn("Pick color", 100);
            colorButton.Click += (s, e) => PickColor();
            colorSwatch = new Panel { Left = 232, Top = 0, Width = 40, Height = 24, BorderStyle = BorderStyle.FixedSingle };
            Row("Tint", colorButton);
            split.Panel1.Controls.Add(colorSwatch);
            colorSwatch.Top = colorButton.Top; colorSwatch.Left = 232;

            tileXBox = Tb(); tileXBox.TextChanged += (s, e) => Commit(); Row("Tiling X", tileXBox);
            tileYBox = Tb(); tileYBox.TextChanged += (s, e) => Commit(); Row("Tiling Y", tileYBox);
            offXBox = Tb(); offXBox.TextChanged += (s, e) => Commit(); Row("Offset X", offXBox);
            offYBox = Tb(); offYBox.TextChanged += (s, e) => Commit(); Row("Offset Y", offYBox);

            animBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            animBox.Items.AddRange(new object[] { "none", "flipbook", "hue", "scroll" });
            animBox.SelectedIndexChanged += (s, e) => { if (mat != null) mat.Animation = (string)animBox.SelectedItem; };
            Row("Animation", animBox);

            colsBox = Tb(); colsBox.TextChanged += (s, e) => Commit(); Row("Flipbook cols", colsBox);
            rowsBox = Tb(); rowsBox.TextChanged += (s, e) => Commit(); Row("Flipbook rows", rowsBox);
            speedBox = Tb(); speedBox.TextChanged += (s, e) => Commit(); Row("Anim speed", speedBox);

            texInfoLabel = new Label { Text = "No texture", ForeColor = Color.Gray, AutoSize = true };
            Row("", texInfoLabel);

            preview = new PreviewPanel(this) { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 20) };
            split.Panel2.Controls.Add(preview);
        }

        TextBox Tb() => new TextBox { BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        Button Btn(string t, int w) => new Button { Text = t, Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 100, 200), ForeColor = Color.White };

        void OpenDialog()
        {
            using var ofd = new OpenFileDialog { Filter = "GMAT material (*.gmat)|*.gmat|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == DialogResult.OK) LoadFile(ofd.FileName);
        }

        void LoadFile(string path)
        {
            try
            {
                mat = GmatReader.Read(path);
                loadedPath = path;
                nameBox.Text = mat.Name;
                authorBox.Text = mat.Author;
                descBox.Text = mat.Description;
                customColorsBox.Checked = mat.CustomColors;
                tileXBox.Text = mat.TilingX.ToString("0.###");
                tileYBox.Text = mat.TilingY.ToString("0.###");
                offXBox.Text = mat.OffsetX.ToString("0.###");
                offYBox.Text = mat.OffsetY.ToString("0.###");
                animBox.SelectedItem = mat.Animation;
                colsBox.Text = mat.FlipbookCols.ToString();
                rowsBox.Text = mat.FlipbookRows.ToString();
                speedBox.Text = mat.AnimSpeed.ToString("0.###");
                UpdateSwatch();
                texInfoLabel.Text = mat.Texture != null ? $"Texture {mat.Texture.Width}×{mat.Texture.Height}" : "No texture found";
                texInfoLabel.ForeColor = mat.Texture != null ? Color.Silver : Color.OrangeRed;
                preview.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read .gmat:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ExportDialog()
        {
            if (mat == null) { MessageBox.Show("Open a .gmat first."); return; }
            Commit();
            mat.Name = nameBox.Text; mat.Author = authorBox.Text; mat.Description = descBox.Text;
            using var sfd = new SaveFileDialog
            {
                Filter = "GMATPLUS material (*.gmatplus)|*.gmatplus",
                FileName = (mat.Name ?? "material") + ".gmatplus"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try { GmatWriter.Write(mat, sfd.FileName); MessageBox.Show("Saved:\n" + sfd.FileName, "Done"); }
                catch (Exception ex) { MessageBox.Show("Export failed:\n" + ex.Message, "Error"); }
            }
        }

        void PickColor()
        {
            if (mat == null) return;
            using var cd = new ColorDialog { Color = Color.FromArgb(Clamp(mat.ColorR), Clamp(mat.ColorG), Clamp(mat.ColorB)) };
            if (cd.ShowDialog() == DialogResult.OK)
            {
                mat.ColorR = cd.Color.R / 255f; mat.ColorG = cd.Color.G / 255f; mat.ColorB = cd.Color.B / 255f;
                UpdateSwatch();
            }
        }

        void UpdateSwatch()
        {
            if (mat == null) return;
            colorSwatch.BackColor = Color.FromArgb(Clamp(mat.ColorR), Clamp(mat.ColorG), Clamp(mat.ColorB));
        }

        static int Clamp(float v) => Math.Max(0, Math.Min(255, (int)(v * 255)));

        void Commit()
        {
            if (mat == null) return;
            mat.TilingX = ParseF(tileXBox.Text, mat.TilingX);
            mat.TilingY = ParseF(tileYBox.Text, mat.TilingY);
            mat.OffsetX = ParseF(offXBox.Text, mat.OffsetX);
            mat.OffsetY = ParseF(offYBox.Text, mat.OffsetY);
            mat.FlipbookCols = Math.Max(1, ParseI(colsBox.Text, mat.FlipbookCols));
            mat.FlipbookRows = Math.Max(1, ParseI(rowsBox.Text, mat.FlipbookRows));
            mat.AnimSpeed = ParseF(speedBox.Text, mat.AnimSpeed);
        }

        static float ParseF(string s, float d) => float.TryParse(s, out var v) ? v : d;
        static int ParseI(string s, int d) => int.TryParse(s, out var v) ? v : d;

        // Draws the current material state into the preview panel, animating with animT.
        public void DrawPreview(Graphics g, Rectangle bounds)
        {
            g.Clear(Color.FromArgb(18, 18, 20));
            if (mat == null || mat.Texture == null)
            {
                TextRenderer.DrawText(g, "Open a .gmat to preview", Font, bounds, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int size = Math.Min(bounds.Width, bounds.Height) - 40;
            if (size < 32) return;
            var dest = new Rectangle(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size);

            var tint = Color.FromArgb(Clamp(mat.ColorR), Clamp(mat.ColorG), Clamp(mat.ColorB));
            float speed = Math.Max(0.01f, mat.AnimSpeed);

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            if (mat.Animation == "flipbook" && mat.FlipbookCols >= 1 && mat.FlipbookRows >= 1)
            {
                int frames = mat.FlipbookCols * mat.FlipbookRows;
                int idx = frames > 0 ? (int)(animT * speed * frames) % frames : 0;
                int col = idx % mat.FlipbookCols, row = idx / mat.FlipbookCols;
                int fw = mat.Texture.Width / mat.FlipbookCols, fh = mat.Texture.Height / mat.FlipbookRows;
                var src = new Rectangle(col * fw, row * fh, fw, fh);
                using var ia = TintAttributes(mat.Animation == "flipbook" && mat.CustomColors ? tint : Color.White);
                g.DrawImage(mat.Texture, dest, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            else if (mat.Animation == "hue")
            {
                float hue = (animT * speed * 0.25f) % 1f;
                var c = HsvToColor(hue);
                using var ia = TintAttributes(c);
                g.DrawImage(mat.Texture, dest, 0, 0, mat.Texture.Width, mat.Texture.Height, GraphicsUnit.Pixel, ia);
            }
            else if (mat.Animation == "scroll")
            {
                float off = (animT * speed) % 1f;
                DrawTiledTinted(g, dest, tint, mat.TilingX, mat.TilingY, mat.OffsetX, mat.OffsetY + off);
            }
            else
            {
                DrawTiledTinted(g, dest, tint, mat.TilingX, mat.TilingY, mat.OffsetX, mat.OffsetY);
            }

            using var border = new Pen(Color.FromArgb(70, 70, 80));
            g.DrawRectangle(border, dest);
        }

        void DrawTiledTinted(Graphics g, Rectangle dest, Color tint, float tileX, float tileY, float offX, float offY)
        {
            using var ia = TintAttributes(tint);
            int repX = Math.Max(1, (int)Math.Round(Math.Abs(tileX)));
            int repY = Math.Max(1, (int)Math.Round(Math.Abs(tileY)));
            repX = Math.Min(repX, 16); repY = Math.Min(repY, 16);
            int cw = dest.Width / repX, ch = dest.Height / repY;
            for (int ix = 0; ix < repX; ix++)
                for (int iy = 0; iy < repY; iy++)
                {
                    var cell = new Rectangle(dest.X + ix * cw, dest.Y + iy * ch, cw, ch);
                    g.DrawImage(mat.Texture, cell, 0, 0, mat.Texture.Width, mat.Texture.Height, GraphicsUnit.Pixel, ia);
                }
        }

        static ImageAttributes TintAttributes(Color tint)
        {
            var ia = new ImageAttributes();
            var cm = new ColorMatrix(new float[][]
            {
                new float[] { tint.R / 255f, 0, 0, 0, 0 },
                new float[] { 0, tint.G / 255f, 0, 0, 0 },
                new float[] { 0, 0, tint.B / 255f, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 }
            });
            ia.SetColorMatrix(cm);
            ia.SetWrapMode(WrapMode.Tile);
            return ia;
        }

        static Color HsvToColor(float h)
        {
            float r, g, b;
            int i = (int)(h * 6);
            float f = h * 6 - i;
            float p = 0, q = 1 - f, t = f;
            switch (i % 6)
            {
                case 0: r = 1; g = t; b = p; break;
                case 1: r = q; g = 1; b = p; break;
                case 2: r = p; g = 1; b = t; break;
                case 3: r = p; g = q; b = 1; break;
                case 4: r = t; g = p; b = 1; break;
                default: r = 1; g = p; b = q; break;
            }
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
    }

    // Simple double-buffered panel that delegates painting to the form.
    public class PreviewPanel : Panel
    {
        readonly MainForm owner;
        public PreviewPanel(MainForm owner)
        {
            this.owner = owner;
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            owner.DrawPreview(e.Graphics, ClientRectangle);
        }
    }
}
