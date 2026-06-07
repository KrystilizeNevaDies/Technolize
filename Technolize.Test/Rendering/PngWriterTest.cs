using System.Buffers.Binary;
using System.IO.Compression;
using Technolize.Rendering.Graphics;

namespace Technolize.Test.Rendering;

/// <summary>
/// Pure-CPU tests for <see cref="PngWriter"/>, the dependency-free RGBA8 PNG encoder used by the
/// Silk.NET snapshot path. They parse the encoded bytes back (signature, IHDR, chunk CRCs, and the
/// zlib-compressed scanlines) to prove the output is a valid PNG that round-trips the input pixels.
/// </summary>
[TestFixture]
public class PngWriterTest
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    [Test]
    public void Encode_StartsWithPngSignatureAndIhdr()
    {
        byte[] pixels = MakePixels(3, 2);

        byte[] png = PngWriter.Encode(pixels, 3, 2);

        Assert.That(png.AsSpan(0, 8).SequenceEqual(Signature), Is.True, "PNG signature");

        // First chunk after the signature must be IHDR with the right dimensions and RGBA settings.
        (string type, byte[] data) = ReadChunkAt(png, 8, out _);
        Assert.Multiple(() =>
        {
            Assert.That(type, Is.EqualTo("IHDR"));
            Assert.That(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0, 4)), Is.EqualTo(3), "width");
            Assert.That(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4)), Is.EqualTo(2), "height");
            Assert.That(data[8], Is.EqualTo(8), "bit depth");
            Assert.That(data[9], Is.EqualTo(6), "colour type RGBA");
        });
    }

    [Test]
    public void Encode_ChunkCrcsAreValid_AndEndWithIend()
    {
        byte[] png = PngWriter.Encode(MakePixels(4, 4), 4, 4);

        int offset = 8;
        string lastType = string.Empty;
        while (offset < png.Length)
        {
            (string type, byte[] data) = ReadChunkAt(png, offset, out int next);

            // Recompute the CRC over type + data and compare to the stored CRC.
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(next - 4, 4));
            uint actualCrc = ComputeCrc(type, data);
            Assert.That(actualCrc, Is.EqualTo(storedCrc), $"CRC mismatch in chunk {type}");

            lastType = type;
            offset = next;
        }

        Assert.That(lastType, Is.EqualTo("IEND"), "PNG must end with IEND");
    }

    [Test]
    public void Encode_IdatDecompresses_ToFilteredScanlinesMatchingInput()
    {
        const int width = 5;
        const int height = 3;
        byte[] pixels = MakePixels(width, height);

        byte[] png = PngWriter.Encode(pixels, width, height);
        byte[] idat = ConcatIdat(png);

        using var compressed = new MemoryStream(idat);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        byte[] filtered = raw.ToArray();

        int stride = width * 4;
        Assert.That(filtered, Has.Length.EqualTo(height * (stride + 1)));

        for (int row = 0; row < height; row++)
        {
            int dst = row * (stride + 1);
            Assert.That(filtered[dst], Is.EqualTo(0), $"row {row} filter byte must be None(0)");
            for (int i = 0; i < stride; i++)
            {
                Assert.That(filtered[dst + 1 + i], Is.EqualTo(pixels[(row * stride) + i]), $"pixel byte mismatch row {row} col {i}");
            }
        }
    }

    [Test]
    public void Encode_RejectsUndersizedBuffer()
    {
        byte[] tooSmall = new byte[3 * 2 * 4 - 1];
        Assert.Throws<ArgumentException>(() => PngWriter.Encode(tooSmall, 3, 2));
    }

    [Test]
    public void EncodeThenDecode_RoundTripsPixels()
    {
        const int width = 7;
        const int height = 5;
        byte[] pixels = MakePixels(width, height);

        byte[] png = PngWriter.Encode(pixels, width, height);
        byte[] decoded = PngReader.DecodeRgba8(png, out int decodedWidth, out int decodedHeight);

        Assert.Multiple(() =>
        {
            Assert.That(decodedWidth, Is.EqualTo(width));
            Assert.That(decodedHeight, Is.EqualTo(height));
            Assert.That(decoded, Is.EqualTo(pixels));
        });
    }

    private static byte[] MakePixels(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            pixels[(i * 4) + 0] = (byte)(i * 7);
            pixels[(i * 4) + 1] = (byte)(i * 13);
            pixels[(i * 4) + 2] = (byte)(i * 29);
            pixels[(i * 4) + 3] = 255;
        }

        return pixels;
    }

    private static (string type, byte[] data) ReadChunkAt(byte[] png, int offset, out int nextOffset)
    {
        int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
        string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
        byte[] data = png.AsSpan(offset + 8, length).ToArray();
        nextOffset = offset + 12 + length; // length(4) + type(4) + data + crc(4)
        return (type, data);
    }

    private static byte[] ConcatIdat(byte[] png)
    {
        using var idat = new MemoryStream();
        int offset = 8;
        while (offset < png.Length)
        {
            (string type, byte[] data) = ReadChunkAt(png, offset, out int next);
            if (type == "IDAT")
            {
                idat.Write(data);
            }
            offset = next;
        }

        return idat.ToArray();
    }

    private static uint ComputeCrc(string type, byte[] data)
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

        uint crc = 0xFFFFFFFFu;
        foreach (char ch in type)
        {
            crc = table[(crc ^ (byte)ch) & 0xFF] ^ (crc >> 8);
        }
        foreach (byte b in data)
        {
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
