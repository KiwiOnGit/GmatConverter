using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Windows.Forms;

namespace GmatConverter
{
    public class MainForm : Form
    {
        SimpleMaterial mat;
        string loadedPath;

        TextBox nameBox, authorBox, descBox, tileXBox, tileYBox, offXBox, offYBox, colsBox, rowsBox, speedBox;
        CheckBox customColorsBox, previewTaggedBox;
        Button colorButton;
        Panel colorSwatch;
        ComboBox animBox, shaderBox;
        Label texInfoLabel, taggedInfoLabel;

        PreviewPanel preview;
        Gorilla3DPreviewPanel preview3D;
        System.Windows.Forms.Timer timer;
        float animT;

        public MainForm(string initialFile)
        {
            Text = "GMAT -> GMATPLUS Converter";
            Width = 980;
            Height = 660;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(30, 30, 34);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            BuildUi();

            timer = new System.Windows.Forms.Timer { Interval = 33 };
            timer.Tick += (s, e) => { animT += 0.033f; preview.Invalidate(); preview3D.Invalidate(); };
            timer.Start();

            if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
                LoadFile(initialFile);
            else
                NewSkin(silent: true);
        }

        void BuildUi()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(22, 22, 26), Padding = new Padding(8, 8, 8, 8) };
            var openBtn = Btn("Open .gmat", 110);
            openBtn.Click += (s, e) => OpenDialog();
            var newBtn = Btn("New skin", 110);
            newBtn.Click += (s, e) => NewSkin(silent: false);
            var exportBtn = Btn("Export .gmatplus", 140);
            exportBtn.Click += (s, e) => ExportDialog();
            top.Controls.Add(openBtn);
            top.Controls.Add(newBtn);
            top.Controls.Add(exportBtn);
            Controls.Add(top);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 400, BackColor = Color.FromArgb(30, 30, 34) };
            Controls.Add(split);
            split.Panel1.AutoScroll = true;
            split.Panel1.Padding = new Padding(10);

            int y = 6;
            void Row(string label, Control c, int h = 24, int w = 250)
            {
                var l = new Label { Text = label, Left = 6, Top = y + 3, Width = 110, ForeColor = Color.Silver };
                c.Left = 122; c.Top = y; c.Width = w; c.Height = h;
                split.Panel1.Controls.Add(l);
                split.Panel1.Controls.Add(c);
                y += h + 8;
            }

            nameBox = Tb(); nameBox.TextChanged += (s, e) => { if (mat != null) mat.Name = nameBox.Text; }; Row("Name", nameBox);
            authorBox = Tb(); authorBox.TextChanged += (s, e) => { if (mat != null) mat.Author = authorBox.Text; }; Row("Author", authorBox);
            descBox = Tb(); descBox.TextChanged += (s, e) => { if (mat != null) mat.Description = descBox.Text; }; Row("Description", descBox);

            var importBtn = Btn("Import PNG...", 120);
            importBtn.Click += (s, e) => ImportTexture(tagged: false);
            Row("Main texture", importBtn);

            var importTaggedBtn = Btn("Import PNG...", 120);
            importTaggedBtn.Click += (s, e) => ImportTexture(tagged: true);
            Row("Tagged texture", importTaggedBtn, w: 150);
            var clearTaggedBtn = Btn("Clear", 70);
            clearTaggedBtn.Left = importTaggedBtn.Left + importTaggedBtn.Width + 6;
            clearTaggedBtn.Top = importTaggedBtn.Top;
            clearTaggedBtn.Click += (s, e) => { if (mat != null) { mat.TaggedTexture = null; UpdateTexLabels(); } };
            split.Panel1.Controls.Add(clearTaggedBtn);

            taggedInfoLabel = new Label { Text = "No tagged variant (uses main texture while tagged)", ForeColor = Color.Gray, AutoSize = true };
            Row("", taggedInfoLabel);

            previewTaggedBox = new CheckBox { Text = "Preview tagged look", ForeColor = Color.Silver, AutoSize = true };
            previewTaggedBox.CheckedChanged += (s, e) => { preview.Invalidate(); preview3D.Invalidate(); };
            Row("", previewTaggedBox);

            shaderBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown };
            shaderBox.Items.AddRange(new object[] { "GorillaTag/UberShader", "Universal Render Pipeline/Lit", "Standard" });
            shaderBox.SelectedIndexChanged += (s, e) => { if (mat != null) mat.ShaderName = shaderBox.Text; };
            shaderBox.Leave += (s, e) => { if (mat != null) mat.ShaderName = shaderBox.Text; };
            Row("Shader", shaderBox);
            var shaderNote = new Label
            {
                Text = "Metadata only -- the mod always builds on the game's real shader for safety.",
                ForeColor = Color.Gray, AutoSize = true, MaximumSize = new Size(260, 0)
            };
            Row("", shaderNote, 32);

            customColorsBox = new CheckBox { Text = "Custom colors (tint by player color in-game)", ForeColor = Color.Silver, AutoSize = true };
            customColorsBox.CheckedChanged += (s, e) => { if (mat != null) mat.CustomColors = customColorsBox.Checked; };
            Row("", customColorsBox);

            colorButton = Btn("Pick color", 100);
            colorButton.Click += (s, e) => PickColor();
            Row("Tint", colorButton, w: 100);
            colorSwatch = new Panel
            {
                Left = colorButton.Left + colorButton.Width + 6,
                Top = colorButton.Top,
                Width = 40,
                Height = 24,
                BorderStyle = BorderStyle.FixedSingle
            };
            split.Panel1.Controls.Add(colorSwatch);

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

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabSwatch = new TabPage("Material swatch");
            var tab3D = new TabPage("3D preview");
            preview = new PreviewPanel(this) { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 20) };
            preview3D = new Gorilla3DPreviewPanel(this) { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 20) };
            tabSwatch.Controls.Add(preview);
            tab3D.Controls.Add(preview3D);

            var faceChestBox = new PictureBox
            {
                Image = GorillaModel.FaceChestTexture,
                SizeMode = PictureBoxSizeMode.Zoom,
                Width = 96,
                Height = 96,
                BackColor = Color.FromArgb(30, 30, 34),
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            var faceChestLabel = new Label
            {
                Text = "Face & belly (fixed)",
                ForeColor = Color.Gray,
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            tab3D.Controls.Add(faceChestBox);
            tab3D.Controls.Add(faceChestLabel);
            tab3D.Resize += (s, e) =>
            {
                faceChestBox.Left = tab3D.ClientSize.Width - faceChestBox.Width - 12;
                faceChestBox.Top = tab3D.ClientSize.Height - faceChestBox.Height - 30;
                faceChestLabel.Left = faceChestBox.Left;
                faceChestLabel.Top = faceChestBox.Top + faceChestBox.Height + 2;
            };

            tabs.TabPages.Add(tab3D);
            tabs.TabPages.Add(tabSwatch);
            split.Panel2.Controls.Add(tabs);

            nameBox.Focus();
            split.Panel1.AutoScrollPosition = new Point(0, 0);
        }

        TextBox Tb() => new TextBox { BackColor = Color.FromArgb(45, 45, 50), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        Button Btn(string t, int w) => new Button { Text = t, Width = w, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(70, 100, 200), ForeColor = Color.White };

        void OpenDialog()
        {
            using var ofd = new OpenFileDialog { Filter = "GMAT material (*.gmat)|*.gmat|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == DialogResult.OK) LoadFile(ofd.FileName);
        }

        void NewSkin(bool silent)
        {
            mat = new SimpleMaterial { Name = "My Skin" };
            loadedPath = null;
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
            shaderBox.Text = mat.ShaderName;
            UpdateSwatch();
            UpdateTexLabels();
            preview.Invalidate();
            preview3D.Invalidate();
            if (!silent)
                MessageBox.Show("New blank skin ready. Import a PNG for the main texture, then Export.", "New skin");
        }

        void ImportTexture(bool tagged)
        {
            if (mat == null) NewSkin(silent: true);
            using var ofd = new OpenFileDialog { Filter = "PNG image (*.png)|*.png|All files (*.*)|*.*" };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            try
            {
                var bmp = new Bitmap(ofd.FileName);
                
                var converted = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(converted)) g.DrawImageUnscaled(bmp, 0, 0);
                bmp.Dispose();

                if (tagged) mat.TaggedTexture = converted;
                else mat.Texture = converted;

                UpdateTexLabels();
                preview.Invalidate();
                preview3D.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load PNG:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void UpdateTexLabels()
        {
            if (mat == null) return;
            if (mat.Texture != null)
            {
                string hint = string.IsNullOrEmpty(mat.ShaderHint) ? "" : $"  ({mat.ShaderHint})";
                texInfoLabel.Text = $"Texture {mat.Texture.Width}×{mat.Texture.Height}{hint}";
                texInfoLabel.ForeColor = Color.Silver;
            }
            else if (mat.Animation == "scroll")
            {
                texInfoLabel.Text = "No texture (procedural scroll effect, expected)";
                texInfoLabel.ForeColor = Color.Silver;
            }
            else
            {
                texInfoLabel.Text = "No texture found -- will export white!";
                texInfoLabel.ForeColor = Color.OrangeRed;
            }

            taggedInfoLabel.Text = mat.TaggedTexture != null
                ? $"Tagged texture {mat.TaggedTexture.Width}×{mat.TaggedTexture.Height}"
                : "No tagged variant (uses main texture while tagged)";
            taggedInfoLabel.ForeColor = mat.TaggedTexture != null ? Color.Silver : Color.Gray;
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
                shaderBox.Text = mat.ShaderName;
                UpdateSwatch();
                UpdateTexLabels();
                preview.Invalidate();
                preview3D.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to read .gmat:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ExportDialog()
        {
            if (mat == null) { MessageBox.Show("Open a .gmat or start a New skin first."); return; }
            if (mat.Texture == null && mat.Animation != "scroll")
            {
                var choice = MessageBox.Show(
                    "This material has no main texture. Exporting now will produce a .gmatplus " +
                    "that renders as a plain white skin in-game.\n\n" +
                    "Export anyway?",
                    "No texture found", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice != DialogResult.Yes) return;
            }
            Commit();
            mat.Name = nameBox.Text; mat.Author = authorBox.Text; mat.Description = descBox.Text;
            mat.ShaderName = shaderBox.Text;
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

        public (Bitmap texture, Vector2 scale, Vector2 offset, Color tint) GetPreviewFrame()
        {
            if (mat == null) return (null, Vector2.One, Vector2.Zero, Color.White);

            Bitmap tex = (previewTaggedBox.Checked && mat.TaggedTexture != null) ? mat.TaggedTexture : mat.Texture;
            Color tint = Color.FromArgb(Clamp(mat.ColorR), Clamp(mat.ColorG), Clamp(mat.ColorB));
            float speed = Math.Max(0.01f, mat.AnimSpeed);

            if (mat.Animation == "flipbook" && tex != null && mat.FlipbookCols >= 1 && mat.FlipbookRows >= 1)
            {
                int frames = mat.FlipbookCols * mat.FlipbookRows;
                int idx = frames > 0 ? (int)(animT * speed * frames) % frames : 0;
                int col = idx % mat.FlipbookCols, row = idx / mat.FlipbookCols;
                var scale = new Vector2(1f / mat.FlipbookCols, 1f / mat.FlipbookRows);
                var offset = new Vector2((float)col / mat.FlipbookCols, (float)row / mat.FlipbookRows);
                return (tex, scale, offset, mat.CustomColors ? tint : Color.White);
            }
            if (mat.Animation == "hue")
            {
                float hue = (animT * speed * 0.25f) % 1f;
                return (tex, Vector2.One, Vector2.Zero, HsvToColor(hue));
            }
            if (mat.Animation == "scroll")
            {
                float off = (animT * speed) % 1f;
                return (tex, new Vector2(mat.TilingX, mat.TilingY), new Vector2(mat.OffsetX, mat.OffsetY + off), tint);
            }
            return (tex, new Vector2(mat.TilingX, mat.TilingY), new Vector2(mat.OffsetX, mat.OffsetY), tint);
        }

        public void DrawPreview(Graphics g, Rectangle bounds)
        {
            g.Clear(Color.FromArgb(18, 18, 20));
            if (mat == null)
            {
                TextRenderer.DrawText(g, "Open a .gmat or start a New skin", Font, bounds, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            var (tex, scale, offset, tint) = GetPreviewFrame();
            if (tex == null)
            {
                string msg = mat.Animation == "scroll"
                    ? "No texture -- procedural scroll effect\n(rendered by the mod at runtime)"
                    : "No texture yet\nImport a PNG for the main texture";
                TextRenderer.DrawText(g, msg, Font, bounds, mat.Animation == "scroll" ? Color.Gray : Color.OrangeRed,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int size = Math.Min(bounds.Width, bounds.Height) - 40;
            if (size < 32) return;
            var dest = new Rectangle(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size);

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using var ia = TintAttributes(tint);
            int repX = Math.Max(1, Math.Min(16, (int)Math.Round(Math.Abs(scale.X))));
            int repY = Math.Max(1, Math.Min(16, (int)Math.Round(Math.Abs(scale.Y))));
            if (mat.Animation == "flipbook") { repX = 1; repY = 1; }
            int cw = dest.Width / repX, ch = dest.Height / repY;
            var srcRect = new Rectangle(
                (int)(offset.X * tex.Width) % tex.Width, (int)(offset.Y * tex.Height) % tex.Height,
                mat.Animation == "flipbook" ? (int)(scale.X * tex.Width) : tex.Width,
                mat.Animation == "flipbook" ? (int)(scale.Y * tex.Height) : tex.Height);
            if (srcRect.Width <= 0) srcRect.Width = tex.Width;
            if (srcRect.Height <= 0) srcRect.Height = tex.Height;

            for (int ix = 0; ix < repX; ix++)
                for (int iy = 0; iy < repY; iy++)
                {
                    var cell = new Rectangle(dest.X + ix * cw, dest.Y + iy * ch, cw, ch);
                    g.DrawImage(tex, cell, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, ia);
                }

            using var border = new Pen(Color.FromArgb(70, 70, 80));
            g.DrawRectangle(border, dest);
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
