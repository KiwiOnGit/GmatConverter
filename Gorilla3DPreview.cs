using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GmatConverter
{
    // Loads and caches the three ripped gorilla mesh assets (body, face, chest/belly) bundled
    // as embedded resources, plus the shared legacy face/chest texture used to render the
    // parts of the model this tool doesn't edit.
    public static class GorillaModel
    {
        private static SimpleMesh body, face, chest;
        private static Bitmap faceChestTexture;

        public static SimpleMesh Body => body ??= Load("GmatConverter.Mesh.Gorilla.asset");
        public static SimpleMesh Face => face ??= Load("GmatConverter.Mesh.gorillaface.asset");
        public static SimpleMesh Chest => chest ??= Load("GmatConverter.Mesh.gorillachest.asset");

        public static Bitmap FaceChestTexture
        {
            get
            {
                if (faceChestTexture == null)
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GmatConverter.Mesh.gorillachestface.png");
                    if (stream != null) faceChestTexture = new Bitmap(stream);
                }
                return faceChestTexture;
            }
        }

        // Null when the local, gitignored Resources/Mesh/*.asset files aren't present (e.g. a
        // fresh clone that hasn't supplied its own ripped meshes yet -- see README.md).
        private static SimpleMesh Load(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var reader = new System.IO.StreamReader(stream);
            return UnityMeshAsset.Parse(reader.ReadToEnd());
        }
    }

    // A tiny software rasterizer: perspective projection, a Z-buffer, perspective-correct
    // texture sampling and simple headlight Gouraud shading. No GPU/3D-API dependency, so the
    // converter stays a single self-contained exe with no extra runtime requirement.
    public static class SoftwareRasterizer
    {
        private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(0.35f, 0.55f, -1f));

        public static void RenderMesh(SimpleMesh mesh, Bitmap texture, Color tint, Vector2 uvScale, Vector2 uvOffset,
            Matrix4x4 rotation, Vector3 modelCenter, float cameraDistance, float meshScale,
            int[] framebuffer, float[] depth, int width, int height)
        {
            if (mesh == null || mesh.Indices == null) return;
            (int[] texPixels, int texW, int texH) = GetTexturePixels(texture);

            float focal = height * 1.1f;
            float tintR = tint.R / 255f, tintG = tint.G / 255f, tintB = tint.B / 255f;

            int triCount = mesh.Indices.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = mesh.Indices[t * 3], i1 = mesh.Indices[t * 3 + 1], i2 = mesh.Indices[t * 3 + 2];

                Vector3 v0 = Vector3.Transform(mesh.Positions[i0] * meshScale - modelCenter, rotation);
                Vector3 v1 = Vector3.Transform(mesh.Positions[i1] * meshScale - modelCenter, rotation);
                Vector3 v2 = Vector3.Transform(mesh.Positions[i2] * meshScale - modelCenter, rotation);
                float z0 = v0.Z + cameraDistance, z1 = v1.Z + cameraDistance, z2 = v2.Z + cameraDistance;
                if (z0 < 0.05f || z1 < 0.05f || z2 < 0.05f) continue;

                float sx0 = width / 2f + v0.X * focal / z0, sy0 = height / 2f - v0.Y * focal / z0;
                float sx1 = width / 2f + v1.X * focal / z1, sy1 = height / 2f - v1.Y * focal / z1;
                float sx2 = width / 2f + v2.X * focal / z2, sy2 = height / 2f - v2.Y * focal / z2;

                // Backface cull (screen-space winding).
                float area = (sx1 - sx0) * (sy2 - sy0) - (sx2 - sx0) * (sy1 - sy0);
                if (area >= 0) continue;

                Vector3 n0 = Vector3.TransformNormal(mesh.Normals[i0], rotation);
                Vector3 n1 = Vector3.TransformNormal(mesh.Normals[i1], rotation);
                Vector3 n2 = Vector3.TransformNormal(mesh.Normals[i2], rotation);
                float sh0 = Shade(n0), sh1 = Shade(n1), sh2 = Shade(n2);

                float invZ0 = 1f / z0, invZ1 = 1f / z1, invZ2 = 1f / z2;
                Vector2 uv0 = mesh.UVs[i0] * invZ0, uv1 = mesh.UVs[i1] * invZ1, uv2 = mesh.UVs[i2] * invZ2;
                float sha0 = sh0 * invZ0, sha1 = sh1 * invZ1, sha2 = sh2 * invZ2;

                int minX = Math.Max(0, (int)Math.Floor(Math.Min(sx0, Math.Min(sx1, sx2))));
                int maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(sx0, Math.Max(sx1, sx2))));
                int minY = Math.Max(0, (int)Math.Floor(Math.Min(sy0, Math.Min(sy1, sy2))));
                int maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(sy0, Math.Max(sy1, sy2))));
                if (minX > maxX || minY > maxY) continue;

                float invArea = 1f / area;
                for (int py = minY; py <= maxY; py++)
                {
                    for (int px = minX; px <= maxX; px++)
                    {
                        float fx = px + 0.5f, fy = py + 0.5f;
                        float w0 = ((sx1 - fx) * (sy2 - fy) - (sx2 - fx) * (sy1 - fy)) * invArea;
                        float w1 = ((sx2 - fx) * (sy0 - fy) - (sx0 - fx) * (sy2 - fy)) * invArea;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;

                        float invZ = w0 * invZ0 + w1 * invZ1 + w2 * invZ2;
                        float z = 1f / invZ;
                        int pi = py * width + px;
                        if (z >= depth[pi]) continue;

                        float u = (w0 * uv0.X + w1 * uv1.X + w2 * uv2.X) * z;
                        float v = (w0 * uv0.Y + w1 * uv1.Y + w2 * uv2.Y) * z;
                        float shade = (w0 * sha0 + w1 * sha1 + w2 * sha2) * z;

                        u = u * uvScale.X + uvOffset.X;
                        v = v * uvScale.Y + uvOffset.Y;

                        float r, g, b;
                        if (texPixels != null)
                        {
                            float tu = Wrap01(u), tv = 1f - Wrap01(v);
                            int tx = Math.Min(texW - 1, (int)(tu * texW));
                            int ty = Math.Min(texH - 1, (int)(tv * texH));
                            int c = texPixels[ty * texW + tx];
                            r = ((c >> 16) & 0xFF) / 255f;
                            g = ((c >> 8) & 0xFF) / 255f;
                            b = (c & 0xFF) / 255f;
                        }
                        else
                        {
                            r = g = b = 0.75f;
                        }

                        r *= tintR * shade; g *= tintG * shade; b *= tintB * shade;
                        depth[pi] = z;
                        framebuffer[pi] = unchecked((int)0xFF000000 |
                            (Clamp255(r) << 16) | (Clamp255(g) << 8) | Clamp255(b));
                    }
                }
            }
        }

        private static float Shade(Vector3 normal)
        {
            float d = Vector3.Dot(Vector3.Normalize(normal), LightDir);
            // Generous ambient floor -- this is a shape/color preview, not a lighting demo, so
            // it should never read as "solid black" no matter which way it's rotated.
            return 0.45f + 0.55f * Math.Max(0f, d);
        }

        private static float Wrap01(float x)
        {
            x %= 1f;
            return x < 0 ? x + 1f : x;
        }

        private static int Clamp255(float v) => Math.Max(0, Math.Min(255, (int)(v * 255f)));

        // Small cache so we don't re-lock the same Bitmap's bits every triangle/frame.
        private static Bitmap cachedBmp;
        private static int[] cachedPixels;
        private static int cachedW, cachedH;

        private static (int[], int, int) GetTexturePixels(Bitmap bmp)
        {
            if (bmp == null) return (null, 0, 0);
            if (ReferenceEquals(bmp, cachedBmp)) return (cachedPixels, cachedW, cachedH);

            int w = bmp.Width, h = bmp.Height;
            var pixels = new int[w * h];
            var rect = new Rectangle(0, 0, w, h);
            BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(bd);

            cachedBmp = bmp; cachedPixels = pixels; cachedW = w; cachedH = h;
            return (pixels, w, h);
        }
    }

    // Rotatable 3D preview: drag with the left mouse button to orbit, wheel to zoom.
    public class Gorilla3DPreviewPanel : Panel
    {
        private readonly MainForm owner;
        private float yaw = 0.4f, pitch = -0.15f, distance = 3.2f;
        private System.Drawing.Point lastMouse;
        private bool dragging;

        public Gorilla3DPreviewPanel(MainForm owner)
        {
            this.owner = owner;
            DoubleBuffered = true;
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragging = true; lastMouse = e.Location; } };
            MouseUp += (s, e) => dragging = false;
            MouseMove += (s, e) =>
            {
                if (!dragging) return;
                yaw += (e.X - lastMouse.X) * 0.01f;
                pitch = Math.Max(-1.3f, Math.Min(1.3f, pitch + (e.Y - lastMouse.Y) * 0.01f));
                lastMouse = e.Location;
                Invalidate();
            };
            MouseWheel += (s, e) =>
            {
                distance = Math.Max(1.4f, Math.Min(8f, distance - e.Delta * 0.002f));
                Invalidate();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
            var (bodyTex, uvScale, uvOffset, tint) = owner.GetPreviewFrame();
            using var bmp = GorillaPreviewRenderer.Render(bodyTex, uvScale, uvOffset, tint, yaw, pitch, distance, w, h);
            e.Graphics.DrawImageUnscaled(bmp, 0, 0);

            if (GorillaModel.Body == null)
            {
                TextRenderer.DrawText(e.Graphics, "3D preview unavailable -- local mesh assets not found.\nSee README.md to supply your own from AssetRipper.",
                    owner.Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            TextRenderer.DrawText(e.Graphics, "Drag to rotate - wheel to zoom", owner.Font,
                new Rectangle(6, h - 22, w - 12, 20), Color.Gray);
        }
    }

    // Renders one frame of the gorilla body + face + chest to an offscreen bitmap. Shared by
    // the interactive preview panel and the headless `--renderpreview` CLI check, so what gets
    // eyeballed in a screenshot is exactly the code path the GUI actually uses.
    public static class GorillaPreviewRenderer
    {
        // The ripped body mesh (a skinned mesh, bind-pose-local) was authored at a far smaller
        // scale than the static face/chest meshes -- its own declared submesh AABBs confirm
        // this isn't a parsing bug, the raw data really is this small; in the actual game a
        // Transform scale (not present in a bare Mesh asset) brings it up to size. Empirically
        // derived so the body's bounding box lands in the same rough units as face/chest: the
        // body's largest raw extent (~0.0154) times this constant is ~1.4, matching the scale
        // face/chest already sit at.
        public const float BodyScale = 91f;

        public static Bitmap Render(Bitmap bodyTex, Vector2 uvScale, Vector2 uvOffset, Color tint,
            float yaw, float pitch, float distance, int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var framebuffer = new int[w * h];
            var depth = new float[w * h];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;
            int bgColor = unchecked((int)0xFF121214);
            for (int i = 0; i < framebuffer.Length; i++) framebuffer[i] = bgColor;

            // The ripped mesh's own local axes don't have Y as "up" (its tallest extent is on
            // Z) -- this fixed correction is applied before the user's orbit so the model
            // starts right-side-up instead of lying on its side.
            var baseCorrection = Matrix4x4.CreateRotationX(-MathF.PI / 2f);
            var rotation = baseCorrection * (Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw));
            SimpleMesh bodyMesh = GorillaModel.Body;
            if (bodyMesh != null)
            {
                Vector3 center = BoundsCenter(bodyMesh, BodyScale);
                SoftwareRasterizer.RenderMesh(bodyMesh, bodyTex, tint, uvScale, uvOffset, rotation, center, distance, BodyScale, framebuffer, depth, w, h);
            }

            // The separate face-plate/chest-badge meshes live in a different, unrelated local
            // origin (no ripped scene/prefab transform survives in a bare Mesh asset to place
            // them correctly), so they're not composited into the 3D view -- the face & chest
            // texture is shown as a fixed 2D reference swatch next to the 3D view instead,
            // rather than risk a visibly-misplaced floating decal.

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, w, h);
            BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(framebuffer, 0, bd.Scan0, framebuffer.Length);
            bmp.UnlockBits(bd);
            return bmp;
        }

        private static Vector3 BoundsCenter(SimpleMesh mesh, float scale)
        {
            Vector3 min = mesh.Positions[0], max = mesh.Positions[0];
            foreach (var p in mesh.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return (min + max) * 0.5f * scale;
        }
    }
}
