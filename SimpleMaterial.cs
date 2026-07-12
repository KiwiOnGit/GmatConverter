using System.Drawing;

namespace GmatConverter
{
    
    public class SimpleMaterial
    {
        public string Name = "Cosmetic";
        public string Author = "Author";
        public string Description = "";
        public bool CustomColors = false;

        public float ColorR = 1f, ColorG = 1f, ColorB = 1f;

        public float TilingX = 1f, TilingY = 1f;
        public float OffsetX = 0f, OffsetY = 0f;

        public string Animation = "none";
        public int FlipbookCols = 1;
        public int FlipbookRows = 1;
        public float AnimSpeed = 0.5f;

        public Bitmap Texture;

        public Bitmap TaggedTexture;

        public string ShaderHint = "";

        public string ShaderName = "GorillaTag/UberShader";
    }
}
