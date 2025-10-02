using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Technolize.Utils;

namespace Technolize.Test.Benchmarks;

/// <summary>
/// Quick 30-second SIMD vs Scalar benchmark for fast performance comparison
/// </summary>
[Config(typeof(QuickBenchmarkConfig))]
[MemoryDiagnoser]
public class QuickSimdVsScalarBenchmarks
{
    private uint[,] _testData = null!;
    private ulong[,] _destination = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Use region size + padding for realistic test
        _testData = GenerateTestData(34, 34);
        _destination = new ulong[34, 34];
    }

    private static uint[,] GenerateTestData(int width, int height)
    {
        var data = new uint[height, width];
        var random = new Random(42);
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                data[y, x] = (uint)random.Next(0, 100);
            }
        }
        return data;
    }

    [Benchmark(Baseline = true)]
    public void SIMD_InPlace()
    {
        ReadOnlySpan<uint> inputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateReadOnlySpan(ref _testData[0, 0], 34 * 34);
        Span<ulong> outputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateSpan(ref _destination[0, 0], 34 * 34);
        SignatureProcessor.ComputeSignature(inputSpan, outputSpan, 34, 34);
    }

    [Benchmark]
    public void Scalar_InPlace()
    {
        ComputeSignatureScalar(_testData, _destination);
    }

    private static void ComputeSignatureScalar(uint[,] source, ulong[,] destination, ulong seed = SignatureProcessor.DefaultSeed)
    {
        int width = source.GetLength(1);
        int height = source.GetLength(0);

        // Process each pixel (excluding border)
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                ulong hash = 0x9E3779B97F4A7C15UL; // P1

                // Apply seed
                hash ^= seed;
                hash *= 0xC4CEB9FE1A85EC53UL; // P2

                // Mix in the 3x3 neighborhood
                hash ^= source[y - 1, x - 1]; hash *= 0x165667B19E3779F1UL; // TL, P3
                hash ^= source[y - 1, x];     hash *= 0x1F79A7AECA2324A5UL; // TC, P4
                hash ^= source[y - 1, x + 1]; hash *= 0x9616EF3348634979UL; // TR, P5
                hash ^= source[y, x - 1];     hash *= 0xB8F65595A4934737UL; // ML, P6
                hash ^= source[y, x];         hash *= 0x0BEB655452634B2BUL; // MC, P7
                hash ^= source[y, x + 1];     hash *= 0x6295C58D548264A9UL; // MR, P8
                hash ^= source[y + 1, x - 1]; hash *= 0x11A2968551968C31UL; // BL, P9
                hash ^= source[y + 1, x];     hash *= 0xEEEF07997F4A7C5BUL; // BC, P10
                hash ^= source[y + 1, x + 1]; hash *= 0x0CF6FD4E4863490BUL; // BR, P11

                destination[y, x] = hash;
            }
        }
    }

    public class QuickBenchmarkConfig : ManualConfig
    {
        public QuickBenchmarkConfig()
        {
            // Quick configuration for 30-second runs
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithWarmupCount(1)     // Minimal warmup
                .WithIterationCount(3)  // Quick iterations
                .WithInvocationCount(1000)); // Enough for statistical significance
        }
    }
}