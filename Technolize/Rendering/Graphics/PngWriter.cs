using System.Buffers.Binary;
using System.IO.Compression;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A minimal, dependency-free PNG encoder/decoder for tightly-packed RGBA8 buffers (row-major,
/// top-to-bottom, 4 bytes per pixel), used by the snapshot path. It relies only on the BCL
/// (<see cref="ZLibStream"/> for the zlib/DEFLATE stream that PNG's IDAT requires).
///
/// Only the single configuration the renderer needs is supported: 8-bit colour type 6 (truecolour with
/// alpha), no interlacing, filter type 0 (None) on every scanline.
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Encodes an RGBA8 pixel buffer to PNG bytes.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgbaPixels, int width, int height)
    {
        if (rgbaPixels.Length < width * height * 4)
        {
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(rgbaPixels));
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: truecolour + alpha
        ihdr[10] = 0; // compression: DEFLATE
        ihdr[11] = 0; // filter: adaptive (we only use None per scanline)
        ihdr[12] = 0; // interlace: none
        WriteChunk(output, "IHDR", ihdr);

        byte[] compressed = CompressScanlines(rgbaPixels, width, height);
        WriteChunk(output, "IDAT", compressed);

        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    /// <summary>Writes an RGBA8 pixel buffer to a PNG file.</summary>
    public static void WriteFile(string path, ReadOnlySpan<byte> rgbaPixels, int width, int height)
    {
        File.WriteAllBytes(path, Encode(rgbaPixels, width, height));
    }

    private static byte[] CompressScanlines(ReadOnlySpan<byte> rgbaPixels, int width, int height)
    {
        int stride = width * 4;

        // Prefix every scanline with filter byte 0 (None).
        byte[] filtered = new byte[height * (stride + 1)];
        for (int row = 0; row < height; row++)
        {
            int src = row * stride;
            int dst = row * (stride + 1);
            filtered[dst] = 0;
            rgbaPixels.Slice(src, stride).CopyTo(filtered.AsSpan(dst + 1, stride));
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(filtered, 0, filtered.Length);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }
        output.Write(typeBytes);
        output.Write(data);

        uint crc = Crc32.Compute(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }
}

/// <summary>Standard CRC-32 (PNG polynomial 0xEDB88320) over the chunk type + data.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }

        return table;
    }

    public static uint Compute(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in first)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in second)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
