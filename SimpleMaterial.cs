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

        // Optional separate texture shown only while the player is tagged/"it". Null means
        // "no tagged variant" -- the mod keeps showing Texture when tagged.
        public Bitmap TaggedTexture;

        // Diagnostic only, set by GmatReader when converting an existing .gmat: a coarse,
        // honest read of the source shader family (never a guessed exact name). Not written
        // to the .gmatplus.
        public string ShaderHint = "";

        // The shader this material targets. The simple .gmatplus format always actually
        // renders on the game's live "GorillaTag/UberShader" (see SimpleMaterialLoader) --
        // that is the whole point of the format, it has zero AssetBundle/shader-version risk.
        // This field is recorded as metadata for custom-made skins and shown in the UI so an
        // advanced user can note what they authored against; it does not change how the mod
        // builds the material.
        public string ShaderName = "GorillaTag/UberShader";
    }
}
