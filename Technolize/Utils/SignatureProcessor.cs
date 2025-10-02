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
            for (int y = 1; y < height - 1; y++) processor.Process(y);
        }
    }

    private unsafe class Processor (uint* pSource, ulong* pDestination, int width, int vectorWidth, Vector<ulong> seed) {
        private readonly ulong _seed = seed[0];
        
        public void Process(int y) {
            int rowOffset = y * width;
            
            // Optimized processing using better memory access patterns and loop unrolling
            // Focus on algorithmic optimizations that provide real performance benefits
            
            int x = 1;
            int end = width - 1;
            
            // Process pixels with optimized memory access patterns
            // Unroll loop for better instruction-level parallelism
            for (; x + 3 < end; x += 4)
            {
                // Process 4 pixels at once with optimized memory access
                ProcessFourPixelsOptimized(y, x, rowOffset);
            }
            
            // Process remaining pixels
            for (; x < end; x++)
            {
                ProcessSinglePixelOptimized(y, x, rowOffset);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessFourPixelsOptimized(int y, int x, int rowOffset)
        {
            // Process 4 pixels with improved memory locality
            // This provides better cache utilization than SIMD overhead for small kernels
            
            uint* topRow = pSource + (y - 1) * width;
            uint* midRow = pSource + y * width;
            uint* botRow = pSource + (y + 1) * width;
            
            // Process first pixel
            ProcessPixelAtPosition(x, rowOffset, topRow, midRow, botRow);
            
            // Process second pixel (if within bounds)
            if (x + 1 < width - 1)
                ProcessPixelAtPosition(x + 1, rowOffset, topRow, midRow, botRow);
                
            // Process third pixel (if within bounds)
            if (x + 2 < width - 1)
                ProcessPixelAtPosition(x + 2, rowOffset, topRow, midRow, botRow);
                
            // Process fourth pixel (if within bounds)
            if (x + 3 < width - 1)
                ProcessPixelAtPosition(x + 3, rowOffset, topRow, midRow, botRow);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessPixelAtPosition(int x, int rowOffset, uint* topRow, uint* midRow, uint* botRow)
        {
            // Load 3x3 neighborhood with optimized pointer access
            uint tl = topRow[x - 1], tc = topRow[x], tr = topRow[x + 1];
            uint ml = midRow[x - 1], mc = midRow[x], mr = midRow[x + 1];
            uint bl = botRow[x - 1], bc = botRow[x], br = botRow[x + 1];

            // Compute hash with optimized arithmetic
            ulong result = 0x9E3779B97F4A7C15UL; // P1
            result ^= _seed;
            result *= 0xC4CEB9FE1A85EC53UL; // P2

            // Unrolled hash computation for better instruction pipelining
            result ^= tl; result *= 0x165667B19E3779F1UL;    // P3
            result ^= tc; result *= 0x1F79A7AECA2324A5UL;    // P4
            result ^= tr; result *= 0x9616EF3348634979UL;    // P5
            result ^= ml; result *= 0xB8F65595A4934737UL;    // P6
            result ^= mc; result *= 0x0BEB655452634B2BUL;    // P7
            result ^= mr; result *= 0x6295C58D548264A9UL;    // P8
            result ^= bl; result *= 0x11A2968551968C31UL;    // P9
            result ^= bc; result *= 0xEEEF07997F4A7C5BUL;    // P10
            result ^= br; result *= 0x0CF6FD4E4863490BUL;    // P11

            pDestination[rowOffset + x] = result;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessSinglePixelOptimized(int y, int x, int rowOffset)
        {
            int currentPixelIndex = rowOffset + x;
            int baseIndex = (y - 1) * width + (x - 1);
            uint* basePtr = pSource + baseIndex;
            
            // Optimized memory access - load entire rows efficiently
            uint tl = basePtr[0], tc = basePtr[1], tr = basePtr[2];
            basePtr += width;
            uint ml = basePtr[0], mc = basePtr[1], mr = basePtr[2];
            basePtr += width;
            uint bl = basePtr[0], bc = basePtr[1], br = basePtr[2];

            // Highly optimized hash computation
            ulong result = 0x9E3779B97F4A7C15UL; // P1
            result ^= _seed;
            result *= 0xC4CEB9FE1A85EC53UL; // P2
            result ^= tl; result *= 0x165667B19E3779F1UL;    // P3
            result ^= tc; result *= 0x1F79A7AECA2324A5UL;    // P4
            result ^= tr; result *= 0x9616EF3348634979UL;    // P5
            result ^= ml; result *= 0xB8F65595A4934737UL;    // P6
            result ^= mc; result *= 0x0BEB655452634B2BUL;    // P7
            result ^= mr; result *= 0x6295C58D548264A9UL;    // P8
            result ^= bl; result *= 0x11A2968551968C31UL;    // P9
            result ^= bc; result *= 0xEEEF07997F4A7C5BUL;    // P10
            result ^= br; result *= 0x0CF6FD4E4863490BUL;    // P11

            pDestination[currentPixelIndex] = result;
        }
    }
}
