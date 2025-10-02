using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using System.Numerics;
using Technolize.World;
using Technolize.World.Generation;
using Technolize.World.Ticking;

namespace Technolize.Test.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class SignatureWorldTickerBenchmarks
{
    private TickableWorld _smallWorld = null!;
    private TickableWorld _mediumWorld = null!;
    private TickableWorld _largeWorld = null!;
    
    private SignatureWorldTicker _smallTicker = null!;
    private SignatureWorldTicker _mediumTicker = null!;
    private SignatureWorldTicker _largeTicker = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create worlds of different sizes to benchmark ticker performance
        _smallWorld = CreateTestWorld(1, 1); // 1x1 regions (32x32 blocks each)
        _mediumWorld = CreateTestWorld(2, 2); // 2x2 regions (64x64 blocks total)
        _largeWorld = CreateTestWorld(4, 4); // 4x4 regions (128x128 blocks total)
        
        _smallTicker = new SignatureWorldTicker(_smallWorld);
        _mediumTicker = new SignatureWorldTicker(_mediumWorld);
        _largeTicker = new SignatureWorldTicker(_largeWorld);
    }

    private static TickableWorld CreateTestWorld(int regionsX, int regionsY)
    {
        var world = new TickableWorld();
        world.Generator = new TestGenerator();
        
        // Pre-load regions with test data
        for (int regionY = 0; regionY < regionsY; regionY++)
        {
            for (int regionX = 0; regionX < regionsX; regionX++)
            {
                var regionPos = new Vector2(regionX, regionY);
                world.GetBlock(regionPos * TickableWorld.RegionSize); // This forces region generation
                
                // Mark some regions as needing ticks to simulate real-world conditions
                if ((regionX + regionY) % 2 == 0)
                {
                    world.ProcessUpdate(regionPos * TickableWorld.RegionSize);
                }
            }
        }
        
        return world;
    }

    [Benchmark(Baseline = true)]
    public void SmallWorld_SingleTick()
    {
        _smallTicker.Tick();
    }

    [Benchmark]
    public void MediumWorld_SingleTick()
    {
        _mediumTicker.Tick();
    }

    [Benchmark]
    public void LargeWorld_SingleTick()
    {
        _largeTicker.Tick();
    }

    [Benchmark]
    public void SmallWorld_MultipleTicks()
    {
        // Simulate multiple consecutive ticks to test caching and ThreadLocal efficiency
        for (int i = 0; i < 10; i++)
        {
            _smallTicker.Tick();
        }
    }

    [Benchmark]
    public void MediumWorld_MultipleTicks()
    {
        for (int i = 0; i < 5; i++)
        {
            _mediumTicker.Tick();
        }
    }

    // Memory allocation patterns benchmark
    [Benchmark]
    public void SmallWorld_MemoryStress()
    {
        // Force GC to see memory allocation patterns
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        _smallTicker.Tick();
    }

    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            // Use in-process execution to avoid issues with headless environment
            AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
        }
    }

    /// <summary>
    /// Simple test generator that creates predictable patterns for benchmarking
    /// </summary>
    private class TestGenerator : IGenerator
    {
        public override void Generate(IUnit unit)
        {
            for (int x = (int)unit.MinPos.X; x < unit.MaxPos.X; x++)
            {
                for (int y = (int)unit.MinPos.Y; y < unit.MaxPos.Y; y++)
                {
                    Vector2 pos = new Vector2(x, y);
                    uint blockId = GenerateBlock(pos);
                    unit.Set(pos, blockId);
                }
            }
        }

        private uint GenerateBlock(Vector2 position)
        {
            // Create varied patterns to trigger different signature computations
            int x = (int)position.X;
            int y = (int)position.Y;
            
            if ((x + y) % 10 == 0) return 1; // Stone-like pattern
            if ((x * y) % 7 == 0) return 2; // Ore-like pattern
            if (Math.Abs(x) + Math.Abs(y) < 5) return 3; // Central area
            if ((x % 4 == 0) && (y % 4 == 0)) return 4; // Grid pattern
            
            return 0; // Air/empty
        }
    }
}