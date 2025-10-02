using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using System.Numerics;
using Technolize.Utils;

namespace Technolize.Test.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class SignatureProcessorBenchmarks
{
    private uint[,] _small3x3Data = null!;
    private uint[,] _medium16x16Data = null!;
    private uint[,] _large64x64Data = null!;
    private uint[,] _regionSizeData = null!; // 32x32 based on TickableWorld.RegionSize + 2 padding
    
    // Pre-allocated destination arrays to test in-place performance
    private ulong[,] _small3x3Dest = null!;
    private ulong[,] _medium16x16Dest = null!;
    private ulong[,] _large64x64Dest = null!;
    private ulong[,] _regionSizeDest = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create test data with varied patterns to stress the SIMD implementation
        _small3x3Data = GenerateTestData(3, 3);
        _medium16x16Data = GenerateTestData(16, 16);
        _large64x64Data = GenerateTestData(64, 64);
        _regionSizeData = GenerateTestData(34, 34); // RegionSize (32) + 2 padding
        
        // Pre-allocate destination arrays
        _small3x3Dest = new ulong[3, 3];
        _medium16x16Dest = new ulong[16, 16];
        _large64x64Dest = new ulong[64, 64];
        _regionSizeDest = new ulong[34, 34];
    }

    private static uint[,] GenerateTestData(int width, int height)
    {
        var data = new uint[height, width];
        var random = new Random(42); // Fixed seed for consistent benchmarks
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Create varied patterns: some clusters, some noise, some gradients
                if ((x + y) % 4 == 0)
                {
                    data[y, x] = (uint)(1 + (x * y) % 10); // Pattern based data
                }
                else if ((x * y) % 7 == 0) 
                {
                    data[y, x] = (uint)random.Next(1, 100); // Random data
                }
                else
                {
                    data[y, x] = (uint)((x + y * 2) % 50); // Gradient-like data
                }
            }
        }
        return data;
    }

    [Benchmark(Baseline = true)]
    public ulong SmallData_SingleSignature_3x3()
    {
        return SignatureProcessor.ComputeSignature(_small3x3Data);
    }

    [Benchmark]
    public ulong[,] SmallData_AllSignatures_3x3()
    {
        return SignatureProcessor.ComputeSignatures(_small3x3Data);
    }

    [Benchmark]
    public ulong[,] MediumData_AllSignatures_16x16()
    {
        return SignatureProcessor.ComputeSignatures(_medium16x16Data);
    }

    [Benchmark]
    public ulong[,] LargeData_AllSignatures_64x64()
    {
        return SignatureProcessor.ComputeSignatures(_large64x64Data);
    }

    [Benchmark]
    public ulong[,] RegionSizeData_AllSignatures_34x34()
    {
        return SignatureProcessor.ComputeSignatures(_regionSizeData);
    }

    [Benchmark]
    public void RegionSizeData_InPlace_Span()
    {
        ReadOnlySpan<uint> inputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateReadOnlySpan(ref _regionSizeData[0, 0], 34 * 34);
        Span<ulong> outputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateSpan(ref _regionSizeDest[0, 0], 34 * 34);
        SignatureProcessor.ComputeSignature(inputSpan, outputSpan, 34, 34);
    }

    [Benchmark]
    public void LargeData_InPlace_Span()
    {
        ReadOnlySpan<uint> inputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateReadOnlySpan(ref _large64x64Data[0, 0], 64 * 64);
        Span<ulong> outputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateSpan(ref _large64x64Dest[0, 0], 64 * 64);
        SignatureProcessor.ComputeSignature(inputSpan, outputSpan, 64, 64);
    }

    [Benchmark]
    public void MultiSeed_RegionSize()
    {
        ReadOnlySpan<uint> inputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateReadOnlySpan(ref _regionSizeData[0, 0], 34 * 34);
        Span<ulong> outputSpan = System.Runtime.InteropServices.MemoryMarshal
            .CreateSpan(ref _regionSizeDest[0, 0], 34 * 34);
        
        // Test with different seeds to see if it affects performance
        SignatureProcessor.ComputeSignature(inputSpan, outputSpan, 34, 34, 12345);
        SignatureProcessor.ComputeSignature(inputSpan, outputSpan, 34, 34, 67890);
        SignatureProcessor.ComputeSignature(inputSpan, outputSpan, 34, 34, 999999);
    }

    [Benchmark]
    public int VectorWidth_Info()
    {
        // This helps us understand the SIMD capabilities on the current hardware
        return Vector<uint>.Count;
    }

    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            // Use in-process execution to avoid issues with headless environment
            AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }
}