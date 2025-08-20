using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Technolize.Utils;

public static class SignatureProcessor
{

    public const ulong DefaultSeed = 67890;

    private static readonly Vector<uint> P1 = new (0xC9340C13);
    private static readonly Vector<uint> P2 = new (0xCE945C22);
    private static readonly Vector<uint> P3 = new (0xBE111638);
    private static readonly Vector<uint> P4 = new (0xE00FACD5);
    private static readonly Vector<uint> P5 = new (0xCB1631D7);
    private static readonly Vector<uint> P6 = new (0xAD1DA259);
    private static readonly Vector<uint> P7 = new (0xD654964C);
    private static readonly Vector<uint> P8 = new (0xE44F04FD);
    private static readonly Vector<uint> P9 = new (0xEBAB1961);
    private static readonly Vector<uint> P10 = new (0xCBBEB4C3);
    private static readonly Vector<uint> P11 = new (0x93441AB3);
    private static readonly Vector<uint> P12 = new (0xAD57E063);


    /// <summary>
    /// Computes a signature for the given source context using a seed.
    /// </summary>
    /// <param name="source">The source data to compute the signature from. The array must be a 3x3.</param>
    /// <param name="seed">The seed to use for the signature computation.</param>
    /// <returns>The computed signature as a 32-bit unsigned integer.</returns>
    /// <exception cref="ArgumentException"></exception>
    public static uint ComputeSignature(uint[,] source, ulong seed = DefaultSeed)
    {
        if (source.GetLength(0) < 3 || source.GetLength(1) < 3)
            throw new ArgumentException("Source array must be at least 3x3 pixels.");

        int width = source.GetLength(1);
        int height = source.GetLength(0);
        Span<uint> destination = stackalloc uint[width * height];

        ComputeSignature(MemoryMarshal.CreateReadOnlySpan(ref source[0, 0], width * height), destination, width, height, seed);

        return destination[width + 1]; // Return the center pixel signature
    }

    public unsafe static void ComputeSignature(ReadOnlySpan<uint> source, Span<uint> destination, int width, int height, ulong seed = DefaultSeed)
    {
        int vectorWidth = Vector<uint>.Count;

        Vector<uint> lowSeed = new ((uint)(seed & 0xFFFFFFFF));
        Vector<uint> highSeed = new ((uint)(seed >> 32));

        // Pin memory to get stable pouinters
        fixed (uint* pSource = source, pDestination = destination)
        {
            Processor processor = new (pSource, pDestination, width, vectorWidth, lowSeed, highSeed);

            // We skip the 1-pixel border
            Parallel.For(1, height - 1, y => {
                processor.Process(y);
            });
        }
    }

    private unsafe class Processor (uint* pSource, uint* pDestination, int width, int vectorWidth, Vector<uint> lowSeed, Vector<uint> highSeed) {
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
                Vector<uint> v_tl = Unsafe.ReadUnaligned<Vector<uint>>(pSource + topRowIndex);    // Top-Left
                Vector<uint> v_tc = Unsafe.ReadUnaligned<Vector<uint>>(pSource + topRowIndex + 1);// Top-Center
                Vector<uint> v_tr = Unsafe.ReadUnaligned<Vector<uint>>(pSource + topRowIndex + 2);// Top-Right

                Vector<uint> v_ml = Unsafe.ReadUnaligned<Vector<uint>>(pSource + midRowIndex);    // Mid-Left
                Vector<uint> v_mc = Unsafe.ReadUnaligned<Vector<uint>>(pSource + midRowIndex + 1);// Mid-Center
                Vector<uint> v_mr = Unsafe.ReadUnaligned<Vector<uint>>(pSource + midRowIndex + 2);// Mid-Right

                Vector<uint> v_bl = Unsafe.ReadUnaligned<Vector<uint>>(pSource + botRowIndex);    // Bot-Left
                Vector<uint> v_bc = Unsafe.ReadUnaligned<Vector<uint>>(pSource + botRowIndex + 1);// Bot-Center
                Vector<uint> v_br = Unsafe.ReadUnaligned<Vector<uint>>(pSource + botRowIndex + 2);// Bot-Right

                // start with the least significant seed bits
                Vector<uint> resultVector = P1;
                resultVector ^= lowSeed;
                resultVector *= P2;
                resultVector ^= v_tl;
                resultVector *= P3;
                resultVector ^= v_tc;
                resultVector *= P4;
                resultVector ^= v_tr;
                resultVector *= P5;
                resultVector ^= v_ml;
                resultVector *= P6;
                resultVector ^= v_mc;
                resultVector *= P7;
                resultVector ^= v_mr;
                resultVector *= P8;
                resultVector ^= v_bl;
                resultVector *= P9;
                resultVector ^= v_bc;
                resultVector *= P10;
                resultVector ^= v_br;
                resultVector *= P11;

                // apply the final seed bits
                resultVector ^= highSeed;
                resultVector *= P12; // Mix in the high seed bits

                Unsafe.WriteUnaligned(pDestination + currentPixelIndex, resultVector);
            }

            // Process any remaining pixels in the row that didn't fit in a full vector.
            for (; x < width - 1; x++) {
                int baseIndex = (y - 1) * width + (x - 1);
                uint result = P1[0];
                result ^= lowSeed[0];
                result *= P2[0];
                result ^= pSource[baseIndex];
                result *= P3[0];
                result ^= pSource[baseIndex + 1];
                result *= P4[0];
                result ^= pSource[baseIndex + 2];
                result *= P5[0];
                result ^= pSource[baseIndex + width];
                result *= P6[0];
                result ^= pSource[baseIndex + width + 1];
                result *= P7[0];
                result ^= pSource[baseIndex + width + 2];
                result *= P8[0];
                result ^= pSource[baseIndex + width * 2];
                result *= P9[0];
                result ^= pSource[baseIndex + width * 2 + 1];
                result *= P10[0];
                result ^= pSource[baseIndex + width * 2 + 2];
                result *= P11[0];

                // apply the final seed bits
                result ^= highSeed[0];
                result *= P12[0]; // Mix in the high seed bits
                pDestination[y * width + x] = result;
            }
        }
    }
}
