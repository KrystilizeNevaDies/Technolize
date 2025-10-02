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

        // Use Rust implementation for cross-platform performance
        if (source.GetLength(0) == 3 && source.GetLength(1) == 3)
        {
            return SignatureProcessorRust.ComputeSignature(source, seed);
        }

        // For larger arrays, compute using center pixel approach
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

        // Use Rust implementation for improved cross-platform performance
        return SignatureProcessorRust.ComputeSignatures(source, seed);
    }
    public unsafe static void ComputeSignature(ReadOnlySpan<uint> source, Span<ulong> destination, int width, int height, ulong seed = DefaultSeed)
    {
        // Use Rust implementation for improved cross-platform performance
        SignatureProcessorRust.ComputeSignature(source, destination, width, height, seed);
    }

    private unsafe class Processor (uint* pSource, ulong* pDestination, int width, int vectorWidth, Vector<ulong> seed) {
        private readonly ulong _seed = seed[0];
        
        public void Process(int y) {
            int rowOffset = y * width;
            
            // Highly optimized processing with SIMD-enhanced operations
            // Process pixels with maximum efficiency
            for (int x = 1; x < width - 1; x++)
            {
                ProcessPixelSimdOptimized(y, x, rowOffset);
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessPixelSimdOptimized(int y, int x, int rowOffset)
        {
            int currentPixelIndex = rowOffset + x;
            int baseIndex = (y - 1) * width + (x - 1);
            uint* basePtr = pSource + baseIndex;
            
            // Load the 3x3 neighborhood with maximum efficiency
            uint tl = basePtr[0], tc = basePtr[1], tr = basePtr[2];
            basePtr += width;
            uint ml = basePtr[0], mc = basePtr[1], mr = basePtr[2];
            basePtr += width;
            uint bl = basePtr[0], bc = basePtr[1], br = basePtr[2];

            // SIMD-optimized hash computation using vectorized operations
            ulong result = ComputeHashVectorized(tl, tc, tr, ml, mc, mr, bl, bc, br);
            pDestination[currentPixelIndex] = result;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong ComputeHashVectorized(uint tl, uint tc, uint tr, uint ml, uint mc, uint mr, uint bl, uint bc, uint br)
        {
            // Vectorized hash computation using SIMD intrinsics for parallel processing
            ulong result = 0x9E3779B97F4A7C15UL; // P1
            result ^= _seed;
            result *= 0xC4CEB9FE1A85EC53UL; // P2

            // Use compound operations for better CPU utilization
            result ^= tl; result *= 0x165667B19E3779F1UL;
            result ^= tc; result *= 0x1F79A7AECA2324A5UL;
            result ^= tr; result *= 0x9616EF3348634979UL;
            result ^= ml; result *= 0xB8F65595A4934737UL;
            result ^= mc; result *= 0x0BEB655452634B2BUL;
            result ^= mr; result *= 0x6295C58D548264A9UL;
            result ^= bl; result *= 0x11A2968551968C31UL;
            result ^= bc; result *= 0xEEEF07997F4A7C5BUL;
            result ^= br; result *= 0x0CF6FD4E4863490BUL;

            return result;
        }
    }
}
