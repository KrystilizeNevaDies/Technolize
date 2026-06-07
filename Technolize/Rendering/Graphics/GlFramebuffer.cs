using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// An offscreen colour render target (a framebuffer object backed by an RGBA8 texture) with CPU pixel
/// readback, used by the snapshot/benchmark harness.
/// </summary>
public sealed class GlFramebuffer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _fbo;
    private readonly uint _colorTexture;

    public int Width { get; }
    public int Height { get; }

    public unsafe GlFramebuffer(GL gl, int width, int height, bool linearFilter = false, bool floatColor = false)
    {
        _gl = gl;
        Width = width;
        Height = height;

        _colorTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            (int)(floatColor ? InternalFormat.Rgba16f : InternalFormat.Rgba8),
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            floatColor ? PixelType.HalfFloat : PixelType.UnsignedByte,
            null);
        int filter = linearFilter ? (int)GLEnum.Linear : (int)GLEnum.Nearest;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            _colorTexture,
            0);

        GLEnum status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer incomplete: {status}");
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Binds this framebuffer and sets the viewport to its full extent for rendering.</summary>
    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Restores the default (window) framebuffer.</summary>
    public void Unbind() => _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

    /// <summary>Binds this framebuffer's colour texture to <paramref name="unit"/> for sampling.</summary>
    public void BindColorTexture(uint unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)unit);
        _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
    }

    /// <summary>
    /// Reads the colour attachment back into a tightly-packed RGBA8 byte buffer. By default the rows are
    /// flipped to top-down order (OpenGL stores them bottom-up), so the output matches the snapshot images.
    /// </summary>
    public unsafe byte[] ReadPixels(bool flipVertically = true)
    {
        byte[] pixels = new byte[Width * Height * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);

        fixed (byte* data = pixels)
        {
            _gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (!flipVertically)
        {
            return pixels;
        }

        int stride = Width * 4;
        byte[] flipped = new byte[pixels.Length];
        for (int row = 0; row < Height; row++)
        {
            Array.Copy(pixels, row * stride, flipped, (Height - 1 - row) * stride, stride);
        }

        return flipped;
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_fbo);
        _gl.DeleteTexture(_colorTexture);
    }
}
