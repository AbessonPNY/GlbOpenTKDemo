using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using System.IO;

namespace GlbOpenTKDemo
{
    public static class TextureLoader
    {
        public static int LoadTexture2D(string path, out int width, out int height)
        {
            using var fs = File.OpenRead(path);
            var img = ImageResult.FromStream(fs, ColorComponents.RedGreenBlueAlpha);
            width = img.Width; height = img.Height;

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, img.Data);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return tex;
        }
    }
}
