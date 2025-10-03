using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Technolize.Utils;

/// <summary>
/// Rust-based signature processor using P/Invoke for cross-platform performance
/// </summary>
public static partial class SignatureProcessorRust
{
    public const ulong DefaultSeed = 67890;

    private const string LibName = "signature_rs.dll";

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial ulong compute_signature_3x3(IntPtr source, ulong seed);

    [LibraryImport(LibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void compute_signatures(IntPtr source, IntPtr destination, int width, int height, ulong seed);

    /// <summary>
    /// Computes a signature for the given source context using a seed.
    /// </summary>
    /// <param name="source">The source data to compute the signature from. The array must be a 3x3.</param>
    /// <param name="seed">The seed to use for the signature computation.</param>
    /// <returns>The computed signature as a 64-bit unsigned integer.</returns>
    /// <exception cref="ArgumentException"></exception>
    public static ulong ComputeSignature(uint[,] source, ulong seed = DefaultSeed)
    {
        if (source.GetLength(0) != 3 || source.GetLength(1) != 3)
            throw new ArgumentException("Source array must be exactly 3x3 pixels for single signature computation.");

        unsafe
        {
            fixed (uint* pSource = source)
            {
                return compute_signature_3x3((IntPtr)pSource, seed);
            }
        }
    }

    /// <summary>
    /// Based on the source data, computes a signature for each pixel in the 2D array.
    /// The signature is computed using a 3x3 neighborhood around each pixel.
    /// </summary>
    public static ulong[,] ComputeSignatures(uint[,] source, ulong seed = DefaultSeed)
    {
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

    public static void ComputeSignature(ReadOnlySpan<uint> source, Span<ulong> destination, int width, int height, ulong seed = DefaultSeed)
    {
        if (width < 3 || height < 3)
            throw new ArgumentException("Source array must be at least 3x3 pixels.");

        unsafe
        {
            fixed (uint* pSource = source)
            fixed (ulong* pDestination = destination)
            {
                compute_signatures((IntPtr)pSource, (IntPtr)pDestination, width, height, seed);
            }
        }
    }
}
