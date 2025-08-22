using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Technolize.Utils;

public static class SignatureProcessor
{

    public const ulong DefaultSeed = 67890;

    private static readonly Vector<ulong> P1 = new (0x9E3779B97F4A7C15UL);
    private static readonly Vector<ulong> P2 = new (0xC4CEB9FE1A85EC53UL);
    private static readonly Vector<ulong> P3 = new (0x165667B19E3779F1UL);
    private static readonly Vector<ulong> P4 = new (0x1F79A7AECA2324A5UL);
    private static readonly Vector<ulong> P5 = new (0x9616EF3348634979UL);
    private static readonly Vector<ulong> P6 = new (0xB8F65595A4934737UL);
    private static readonly Vector<ulong> P7 = new (0x0BEB655452634B2BUL);
    private static readonly Vector<ulong> P8 = new (0x6295C58D548264A9UL);
    private static readonly Vector<ulong> P9 = new (0x11A2968551968C31UL);
    private static readonly Vector<ulong> P10 = new (0xEEEF07997F4A7C5BUL);
    private static readonly Vector<ulong> P11 = new (0x0CF6FD4E4863490BUL);


    /// <summary>
    /// Computes a signature for the given source context using a seed.
    /// </summary>
    /// <param name="source">The source data to compute the signature from. The array must be a 3x3.</param>
    /// <param name="seed">The seed to use for the signature computation.</param>
    /// <returns>The computed signature as a 32-bit unsigned integer.</returns>
    /// <exception cref="ArgumentException"></exception>
    public static ulong ComputeSignature(uint[,] source, ulong seed = DefaultSeed)
    {
        if (source.GetLength(0) < 3 || source.GetLength(1) < 3)
            throw new ArgumentException("Source array must be at least 3x3 pixels.");

        int width = source.GetLength(1);
        int height = source.GetLength(0);
        Span<ulong> destination = stackalloc ulong[width * height];

        ComputeSignature(MemoryMarshal.CreateReadOnlySpan(ref source[0, 0], width * height), destination, width, height, seed);

        return destination[width + 1]; // Return the center pixel signature
    }

    /// <summary>
    /// Based on the source data, computes a signature for each pixel in the 2D array.
    /// The signature is computed using a 3x3 neighborhood around each pixel.
    /// </summary>
    public static ulong[,] ComputeSignatures(uint[,] source, ulong seed = DefaultSeed) {
        int width = source.GetLength(1);
        int height = source.GetLength(0);
        if (width < 3 || height < 3)
            throw new ArgumentException("Source array must be at least 3x3 pixels.");

        ulong[,] signatures = new ulong[height, width];

        ReadOnlySpan<uint> inputSpan = MemoryMarshal.CreateReadOnlySpan(ref source[0, 0], width * height);
        Span<ulong> outputSpan = MemoryMarshal.CreateSpan(ref signatures[0, 0], width * height);
        ComputeSignature(inputSpan, outputSpan, width, height, seed);
        return signatures;
    }
    public unsafe static void ComputeSignature(ReadOnlySpan<uint> source, Span<ulong> destination, int width, int height, ulong seed = DefaultSeed)
    {
        int vectorWidth = Vector<uint>.Count;

        Vector<ulong> seedVec = new (seed);

        // Pin memory to get stable pointers
        fixed (uint* pSource = source)
        fixed(ulong* pDestination = destination) {
            Processor processor = new (pSource, pDestination, width, vectorWidth, seedVec);

            // We skip the 1-pixel border
            // Parallel.For(1, height - 1, y => processor.Process(y));
            for (int y = 1; y < height - 1; y++)
            {
                processor.Process(y);
            }
        }
    }

    private unsafe class Processor (uint* pSource, ulong* pDestination, int width, int vectorWidth, Vector<ulong> seed) {
        public  void Process(int y) {
            int rowOffset = y * width;
            int x = 1;

            // Process the row in chunks of 'vectorWidth' pixels.
            for (; x <= width - 1 - vectorWidth; x += vectorWidth) {
                int currentPixelIndex = rowOffset + x;

                // Define the indices for the 3x3 neighborhood's top-left corner
                int topRowIndex = currentPixelIndex - width - 1;
                int midRowIndex = currentPixelIndex - 1;
                int botRowIndex = currentPixelIndex + width - 1;

                // Load vectors of source pixels from the 3 rows
                Vector<uint> vTl = Unsafe.ReadUnaligned<Vector<uint>>(pSource + topRowIndex);    // Top-Left
                Vector<uint> vTc = Unsafe.ReadUnaligned<Vector<uint>>(pSource + topRowIndex + 1);// Top-Center
                Vector<uint> vTr = Unsafe.ReadUnaligned<Vector<uint>>(pSource + topRowIndex + 2);// Top-Right

                Vector<uint> vMl = Unsafe.ReadUnaligned<Vector<uint>>(pSource + midRowIndex);    // Mid-Left
                Vector<uint> vMc = Unsafe.ReadUnaligned<Vector<uint>>(pSource + midRowIndex + 1);// Mid-Center
                Vector<uint> vMr = Unsafe.ReadUnaligned<Vector<uint>>(pSource + midRowIndex + 2);// Mid-Right

                Vector<uint> vBl = Unsafe.ReadUnaligned<Vector<uint>>(pSource + botRowIndex);    // Bot-Left
                Vector<uint> vBc = Unsafe.ReadUnaligned<Vector<uint>>(pSource + botRowIndex + 1);// Bot-Center
                Vector<uint> vBr = Unsafe.ReadUnaligned<Vector<uint>>(pSource + botRowIndex + 2);// Bot-Right

                // Widen each uint vector into two ulong vectors (lower and upper halves)
                Vector.Widen(vTl, out Vector<ulong> vTlLo, out Vector<ulong> vTlHi);
                Vector.Widen(vTc, out Vector<ulong> vTcLo, out Vector<ulong> vTcHi);
                Vector.Widen(vTr, out Vector<ulong> vTrLo, out Vector<ulong> vTrHi);

                Vector.Widen(vMl, out Vector<ulong> vMlLo, out Vector<ulong> vMlHi);
                Vector.Widen(vMc, out Vector<ulong> vMcLo, out Vector<ulong> vMcHi);
                Vector.Widen(vMr, out Vector<ulong> vMrLo, out Vector<ulong> vMrHi);

                Vector.Widen(vBl, out Vector<ulong> vBlLo, out Vector<ulong> vBlHi);
                Vector.Widen(vBc, out Vector<ulong> vBcLo, out Vector<ulong> vBcHi);
                Vector.Widen(vBr, out Vector<ulong> vBrLo, out Vector<ulong> vBrHi);

                Vector<ulong> hashLo = P1;
                Vector<ulong> hashHi = P1;

                // Apply the seed
                hashLo ^= seed;
                hashHi ^= seed;
                hashLo *= P2;
                hashHi *= P2;

                hashLo ^= vTlLo;
                hashHi ^= vTlHi;
                hashLo *= P3;
                hashHi *= P3;

                hashLo ^= vTcLo;
                hashHi ^= vTcHi;
                hashLo *= P4;
                hashHi *= P4;

                hashLo ^= vTrLo;
                hashHi ^= vTrHi;
                hashLo *= P5;
                hashHi *= P5;

                hashLo ^= vMlLo;
                hashHi ^= vMlHi;
                hashLo *= P6;
                hashHi *= P6;

                hashLo ^= vMcLo;
                hashHi ^= vMcHi;
                hashLo *= P7;
                hashHi *= P7;

                hashLo ^= vMrLo;
                hashHi ^= vMrHi;
                hashLo *= P8;
                hashHi *= P8;

                hashLo ^= vBlLo;
                hashHi ^= vBlHi;
                hashLo *= P9;
                hashHi *= P9;

                hashLo ^= vBcLo;
                hashHi ^= vBcHi;
                hashLo *= P10;
                hashHi *= P10;

                hashLo ^= vBrLo;
                hashHi ^= vBrHi;
                hashLo *= P11;
                hashHi *= P11;

                // Write low
                Unsafe.WriteUnaligned(pDestination + currentPixelIndex, hashLo);
                // Write high
                Unsafe.WriteUnaligned(pDestination + currentPixelIndex + vectorWidth / 2, hashHi);
            }

            // Process any remaining pixels in the row that didn't fit in a full vector.
            for (; x < width - 1; x++)
            {
                // Calculate base index for the top-left of the 3x3 grid
                int baseIndex = (y - 1) * width + (x - 1);

                // Initialize the 64-bit accumulator
                ulong result = P1[0];

                // Apply the seed
                result ^= seed[0];
                result *= P2[0];

                // Mix in each of the 9 source pixels from the neighborhood sequentially
                result ^= pSource[baseIndex];                 // Top-Left
                result *= P3[0];
                result ^= pSource[baseIndex + 1];             // Top-Center
                result *= P4[0];
                result ^= pSource[baseIndex + 2];             // Top-Right
                result *= P5[0];
                result ^= pSource[baseIndex + width];         // Mid-Left
                result *= P6[0];
                result ^= pSource[baseIndex + width + 1];     // Mid-Center
                result *= P7[0];
                result ^= pSource[baseIndex + width + 2];     // Mid-Right
                result *= P8[0];
                result ^= pSource[baseIndex + width * 2];     // Bot-Left
                result *= P9[0];
                result ^= pSource[baseIndex + width * 2 + 1]; // Bot-Center
                result *= P10[0];
                result ^= pSource[baseIndex + width * 2 + 2]; // Bot-Right
                result *= P11[0];

                // Write the final 64-bit hash to the destination
                pDestination[y * width + x] = result;
            }
        }
    }
}
