using System.Buffers.Binary;
using System.IO.Compression;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A minimal PNG decoder that is the inverse of <see cref="PngWriter"/>: it reads 8-bit truecolour
/// + alpha (colour type 6, non-interlaced) PNGs into a tightly-packed RGBA8 buffer. All five PNG
/// scanline filter types are reversed so it can also decode files not produced by this project, though
/// the snapshot path only ever decodes its own baselines.
/// </summary>
public static class PngReader
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Decodes an RGBA8 PNG into a tightly-packed RGBA8 byte buffer.</summary>
    public static byte[] DecodeRgba8(byte[] png, out int width, out int height)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new ArgumentException("Not a PNG (bad signature).", nameof(png));
        }

        width = 0;
        height = 0;
        using var idat = new MemoryStream();
        bool seenHeader = false;

        int offset = 8;
        while (offset + 8 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4, 4));
                    if (data[8] != 8 || data[9] != 6 || data[12] != 0)
                    {
                        throw new NotSupportedException("Only 8-bit RGBA, non-interlaced PNG is supported.");
                    }
                    seenHeader = true;
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    offset = png.Length;
                    break;
            }

            offset += 12 + length; // length(4) + type(4) + data + crc(4)
        }

        if (!seenHeader)
        {
            throw new ArgumentException("PNG missing IHDR.", nameof(png));
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        return Unfilter(raw.ToArray(), width, height);
    }

    private static byte[] Unfilter(byte[] filtered, int width, int height)
    {
        const int bpp = 4; // bytes per pixel (RGBA8)
        int stride = width * bpp;
        byte[] pixels = new byte[height * stride];

        for (int row = 0; row < height; row++)
        {
            int filterType = filtered[row * (stride + 1)];
            int src = row * (stride + 1) + 1;
            int dst = row * stride;

            for (int i = 0; i < stride; i++)
            {
                int x = filtered[src + i];
                int a = i >= bpp ? pixels[dst + i - bpp] : 0;          // left
                int b = row > 0 ? pixels[dst - stride + i] : 0;        // up
                int c = (row > 0 && i >= bpp) ? pixels[dst - stride + i - bpp] : 0; // up-left

                int value = filterType switch
                {
                    0 => x,
                    1 => x + a,
                    2 => x + b,
                    3 => x + ((a + b) >> 1),
                    4 => x + Paeth(a, b, c),
                    _ => throw new NotSupportedException($"Unsupported PNG filter type {filterType}."),
                };

                pixels[dst + i] = (byte)value;
            }
        }

        return pixels;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
