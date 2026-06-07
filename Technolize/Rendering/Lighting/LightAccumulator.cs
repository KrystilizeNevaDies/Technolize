using System.Numerics;

namespace Technolize.Rendering.Lighting;

/// <summary>
/// The shared, HDR per-cell light buffer that every <see cref="ILightEmitter"/> writes into during the
/// CPU lighting stage. It is the "shared lighting texture" the ray casts deposit into, kept in linear
/// floating point (per cell, per channel) so multiple light sources accumulate additively without
/// clipping until the final tone-clamp at upload time.
///
/// Layout matches <see cref="CpuLightingField"/> and the colour texture: row-major, top-to-bottom, one
/// <see cref="Vector3"/> per cell (index <c>y * Width + x</c>).
/// </summary>
public sealed class LightAccumulator
{
    private Vector3[] _light;

    public LightAccumulator(int width, int height)
    {
        Width = width;
        Height = height;
        _light = new Vector3[width * height];
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// Resizes (if needed) and resets every cell to the ambient base light, the lighting-agnostic floor
    /// added before any emitter contributes.
    /// </summary>
    public void Reset(int width, int height, Vector3 ambient)
    {
        if (width != Width || height != Height || _light.Length != width * height)
        {
            Width = width;
            Height = height;
            _light = new Vector3[width * height];
        }

        Array.Fill(_light, ambient);
    }

    /// <summary>Adds <paramref name="light"/> to the cell, ignoring out-of-bounds coordinates.</summary>
    public void Deposit(int x, int y, Vector3 light)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        _light[(y * Width) + x] += light;
    }

    /// <summary>
    /// Packs the accumulated light into a tightly-packed RGBA8 buffer (per-channel clamped to [0, 1]),
    /// ready to upload as the lighting texture. Alpha is fixed at 255.
    /// </summary>
    public byte[] ToRgba8()
    {
        byte[] pixels = new byte[Width * Height * 4];
        for (int i = 0; i < _light.Length; i++)
        {
            Vector3 c = Vector3.Clamp(_light[i], Vector3.Zero, Vector3.One);
            int index = i * 4;
            pixels[index + 0] = (byte)(c.X * 255f + 0.5f);
            pixels[index + 1] = (byte)(c.Y * 255f + 0.5f);
            pixels[index + 2] = (byte)(c.Z * 255f + 0.5f);
            pixels[index + 3] = 255;
        }

        return pixels;
    }
}
