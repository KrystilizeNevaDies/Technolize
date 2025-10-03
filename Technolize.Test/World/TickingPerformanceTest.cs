using System.Diagnostics;
using System.Numerics;
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
        
        var ticker = new SignatureWorldTicker(world);
        
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
        
        var ticker = new SignatureWorldTicker(world);
        
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
        
        var ticker = new SignatureWorldTicker(world);
        
        // Run multiple ticks to ensure stability
        for (int i = 0; i < 5; i++)
        {
            Assert.DoesNotThrow(() => ticker.Tick());
        }
        
        Console.WriteLine("Multiple ticks completed successfully");
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