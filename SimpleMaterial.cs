using System.Drawing;

namespace GmatConverter
{
    // The extracted, editable representation of a material. Everything the preview shows and
    // the .gmatplus writer emits lives here.
    public class SimpleMaterial
    {
        public string Name = "Cosmetic";
        public string Author = "Author";
        public string Description = "";
        public bool CustomColors = false;

        // Tint (RGB, 0..1). Applied as a multiply over the texture in preview and in-game.
        public float ColorR = 1f, ColorG = 1f, ColorB = 1f;

        // Texture tiling / offset (fur repeats, so scale can be > 1).
        public float TilingX = 1f, TilingY = 1f;
        public float OffsetX = 0f, OffsetY = 0f;

        // Animation reconstructed CPU-side by the mod, same vocabulary as the mod's
        // CosmeticAnimator: none | flipbook | hue | scroll.
        public string Animation = "none";
        public int FlipbookCols = 1;
        public int FlipbookRows = 1;
        public float AnimSpeed = 0.5f;

        // The decoded main texture (top-left origin, standard Bitmap orientation).
        public Bitmap Texture;
    }
}
