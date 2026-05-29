using System.Diagnostics;
using System.Numerics;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Generation;
using Technolize.World.Ticking;

namespace Technolize.Test.World;

[TestFixture]
public class TickingPerformanceTest
{
    [Test]
    public void SignatureWorldTicker_OptimizationsWork()
    {
        // Simple test to verify optimizations don't break functionality
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        // Create a single region
        var regionPos = Vector2.Zero;
        world.GetBlock(regionPos * TickableWorld.RegionSize); // Forces generation
        world.ProcessUpdate(regionPos);

        using var ticker = new SignatureWorldTicker(world);

        // This should complete without errors
        Assert.DoesNotThrow(() => ticker.Tick());
        Console.WriteLine("SignatureWorldTicker optimizations work correctly");
    }

    [Test]
    public void SignatureWorldTicker_PerformsBasicTicking()
    {
        // Basic functionality test
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        // Set up a simple scenario
        world.SetBlock(new Vector2(5, 5), 1);
        world.ProcessUpdate(Vector2.Zero);

        using var ticker = new SignatureWorldTicker(world);

        var stopwatch = Stopwatch.StartNew();
        ticker.Tick();
        stopwatch.Stop();

        Console.WriteLine($"Basic tick completed in {stopwatch.ElapsedMilliseconds}ms");

        // Should complete quickly
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000),
            "Basic tick should complete reasonably quickly");
    }

    [Test]
    public void SignatureWorldTicker_MultipleTicksStable()
    {
        // Test stability over multiple ticks
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        var regionPos = Vector2.Zero;
        world.GetBlock(regionPos * TickableWorld.RegionSize);
        world.ProcessUpdate(regionPos);

        using var ticker = new SignatureWorldTicker(world);

        // Run multiple ticks to ensure stability
        for (int i = 0; i < 5; i++)
        {
            Assert.DoesNotThrow(() => ticker.Tick());
        }

        Console.WriteLine("Multiple ticks completed successfully");
    }

    [Test]
    public void SignatureWorldTicker_ProducesManualTickTimings()
    {
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        var regionPos = Vector2.Zero;
        world.GetBlock(regionPos * TickableWorld.RegionSize);
        world.ProcessUpdate(regionPos);

        using var ticker = new SignatureWorldTicker(world)
        {
            ManualTimingsEnabled = true
        };

        int activeRegionCount = ticker.Tick();
        TickCycleTimings? timings = ticker.LastTickTimings;

        Assert.That(timings, Is.Not.Null, "Manual timing mode should capture the last tick timings.");
        Assert.Multiple(() =>
        {
            Assert.That(timings!.ActiveRegionCount, Is.EqualTo(activeRegionCount));
            Assert.That(timings.TotalMs, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(timings.WorkerAccumulatedMs, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(timings.EstimatedParallelism, Is.GreaterThanOrEqualTo(0.0));
        });
    }

    [Test]
    public void WorldRenderFrame_IncludesScheduledRegionsWithinVisibleBounds()
    {
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        world.GetBlock(Vector2.Zero);
        world.GetBlock(new Vector2(TickableWorld.RegionSize, 0));
        world.ProcessUpdate(Vector2.Zero, localOnly: true);
        world.ProcessUpdate(new Vector2(1, 0), localOnly: true);

        WorldRenderFrame frame = WorldRenderFrameBuilder.FromWorld(world, new Vector2(0, 0), new Vector2(1, 1));

        Assert.That(frame.ScheduledRegions, Has.Count.EqualTo(1));
        Assert.That(frame.ScheduledRegions, Contains.Item(Vector2.Zero));
    }

    private class TestPatternGenerator : IGenerator
    {
        public override void Generate(IUnit unit)
        {
            // Create simple patterns for testing
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
            int x = (int)position.X;
            int y = (int)position.Y;

            // Simple pattern
            if ((x + y) % 10 == 0) return 1;
            return 0; // Air/empty
        }
    }
}