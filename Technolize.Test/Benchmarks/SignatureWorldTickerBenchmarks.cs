using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using System.Numerics;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Ticking;

namespace Technolize.Test.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class SignatureWorldTickerBenchmarks
{
    private const int RepeatedTickCount = 10;

    private TickableWorld _world = null!;
    private SignatureWorldTicker _ticker = null!;

    [IterationSetup(Target = nameof(SmallWorld_SingleTick))]
    public void SetupSmallSingleTick()
    {
        ResetBenchmarkState(1, 1);
    }

    [IterationSetup(Target = nameof(MediumWorld_SingleTick))]
    public void SetupMediumSingleTick()
    {
        ResetBenchmarkState(2, 2);
    }

    [IterationSetup(Target = nameof(LargeWorld_SingleTick))]
    public void SetupLargeSingleTick()
    {
        ResetBenchmarkState(4, 4);
    }

    [IterationSetup(Target = nameof(SmallWorld_MultipleTicks))]
    public void SetupSmallMultipleTicks()
    {
        ResetBenchmarkState(1, 1);
    }

    [IterationSetup(Target = nameof(MediumWorld_MultipleTicks))]
    public void SetupMediumMultipleTicks()
    {
        ResetBenchmarkState(2, 2);
    }

    [IterationSetup(Target = nameof(SmallWorld_MemoryStress))]
    public void SetupSmallMemoryStress()
    {
        ResetBenchmarkState(1, 1);
    }

    [IterationCleanup(Targets = [
        nameof(SmallWorld_SingleTick),
        nameof(MediumWorld_SingleTick),
        nameof(LargeWorld_SingleTick),
        nameof(SmallWorld_MultipleTicks),
        nameof(MediumWorld_MultipleTicks),
        nameof(SmallWorld_MemoryStress)])]
    public void CleanupIteration()
    {
        _ticker?.Dispose();
        _world?.Unload();
        _ticker = null!;
        _world = null!;
    }

    private void ResetBenchmarkState(int regionsX, int regionsY)
    {
        CleanupIteration();
        _world = CreateTestWorld(regionsX, regionsY);
        _ticker = new SignatureWorldTicker(_world);
        ScheduleActiveRegions(regionsX, regionsY);
    }

    private static TickableWorld CreateTestWorld(int regionsX, int regionsY)
    {
        var world = new TickableWorld
        {
            Generator = new TestGenerator()
        };

        // Preload a one-region halo so padded neighbor reads do not generate new regions during the measured tick.
        for (int regionY = -1; regionY <= regionsY; regionY++)
        {
            for (int regionX = -1; regionX <= regionsX; regionX++)
            {
                world.GetRegion(new Vector2(regionX, regionY));
            }
        }

        return world;
    }

    private void ScheduleActiveRegions(int regionsX, int regionsY)
    {
        for (int regionY = 0; regionY < regionsY; regionY++)
        {
            for (int regionX = 0; regionX < regionsX; regionX++)
            {
                _world.ProcessUpdate(new Vector2(regionX, regionY), localOnly: true);
            }
        }
    }

    [Benchmark(Baseline = true)]
    public void SmallWorld_SingleTick()
    {
        _ticker.Tick();
    }

    [Benchmark]
    public void MediumWorld_SingleTick()
    {
        _ticker.Tick();
    }

    [Benchmark]
    public void LargeWorld_SingleTick()
    {
        _ticker.Tick();
    }

    [Benchmark]
    public void SmallWorld_MultipleTicks()
    {
        for (int i = 0; i < RepeatedTickCount; i++)
        {
            ScheduleActiveRegions(1, 1);
            _ticker.Tick();
        }
    }

    [Benchmark]
    public void MediumWorld_MultipleTicks()
    {
        for (int i = 0; i < RepeatedTickCount; i++)
        {
            ScheduleActiveRegions(2, 2);
            _ticker.Tick();
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

        _ticker.Tick();
    }

    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddJob(Job.ShortRun.WithRuntime(CoreRuntime.Core80));
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
            int x = PositiveMod((int)position.X, TickableWorld.RegionSize);
            int y = PositiveMod((int)position.Y, TickableWorld.RegionSize);

            if (y == TickableWorld.RegionSize - 1)
            {
                return Blocks.Bedrock.Id;
            }

            if (y >= TickableWorld.RegionSize - 6)
            {
                return x % 5 == 0 ? Blocks.Dirt.Id : Blocks.Stone.Id;
            }

            if (y is >= 6 and <= 9 && x >= 6 && x <= TickableWorld.RegionSize - 7)
            {
                return Blocks.Water.Id;
            }

            if (y is >= 10 and <= 13 && x % 4 != 0)
            {
                return Blocks.Sand.Id;
            }

            if (y is >= 14 and <= 16 && (x + y) % 3 == 0)
            {
                return Blocks.Dirt.Id;
            }

            if (y == 17 && x % 6 == 0)
            {
                return Blocks.Wood.Id;
            }

            return Blocks.Air.Id;
        }

        private static int PositiveMod(int value, int divisor)
        {
            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }
    }
}
