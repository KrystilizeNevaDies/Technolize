using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A 2D RGBA8 texture with nearest-neighbour filtering and clamp-to-edge wrapping — the exact sampling
/// the world shader relies on for the dense colour texture and the two packed quadtree textures.
///
/// Pixel data is laid out row-major, top-to-bottom, 4 bytes per pixel (R, G, B, A).
/// </summary>
public sealed class GlTexture : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    public int Width { get; }
    public int Height { get; }

    private GlTexture(GL gl, uint handle, int width, int height)
    {
        _gl = gl;
        _handle = handle;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Creates an RGBA8 texture from tightly-packed pixel data. <paramref name="pixels"/> must contain
    /// at least <c>width * height * 4</c> bytes. When <paramref name="linear"/> is set the texture uses
    /// bilinear min/mag filtering (smooth upsampling, e.g. the lighting texture); otherwise it stays
    /// nearest-neighbour for crisp, texel-exact sampling (e.g. the dense colour texture).
    /// </summary>
    public static unsafe GlTexture CreateRgba8(GL gl, int width, int height, ReadOnlySpan<byte> pixels, bool linear = false)
    {
        if (pixels.Length < width * height * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(pixels));
        }

        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        fixed (byte* data = pixels)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)InternalFormat.Rgba8,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                data);
        }

        int filter = linear ? (int)GLEnum.Linear : (int)GLEnum.Nearest;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        return new GlTexture(gl, handle, width, height);
    }

    /// <summary>
    /// Creates a single-channel R8 texture from tightly-packed one-byte-per-pixel data. The shader reads
    /// the value in the red channel (e.g. the distance field for shadow sphere-tracing). <paramref name="pixels"/>
    /// must contain at least <c>width * height</c> bytes.
    /// </summary>
    public static unsafe GlTexture CreateR8(GL gl, int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length < width * height)
        {
            throw new ArgumentException("Pixel buffer is smaller than width * height.", nameof(pixels));
        }

        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        // R8 rows are one byte wide and may not be 4-byte aligned, so relax the unpack alignment.
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        fixed (byte* data = pixels)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                (int)InternalFormat.R8,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Red,
                PixelType.UnsignedByte,
                data);
        }

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        return new GlTexture(gl, handle, width, height);
    }

    /// <summary>Binds this texture to the currently-active texture unit.</summary>
    public void Bind() => _gl.BindTexture(TextureTarget.Texture2D, _handle);

    public void Dispose() => _gl.DeleteTexture(_handle);
}
