using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Generation;
using Technolize.World.Block;

namespace Technolize.Test.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class WorldRendererBenchmarks
{
    private TickableWorld _smallWorld = null!;
    private TickableWorld _mediumWorld = null!;
    private TickableWorld _largeWorld = null!;
    private WorldRenderer _smallRenderer = null!;
    private WorldRenderer _mediumRenderer = null!;
    private WorldRenderer _largeRenderer = null!;
    
    private const int ScreenWidth = 800;
    private const int ScreenHeight = 600;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize Raylib in headless mode for benchmarking
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Benchmark");
        Raylib.SetTargetFPS(60);

        SetupSmallWorld();
        SetupMediumWorld();
        SetupLargeWorld();
    }

    private void SetupSmallWorld()
    {
        // Small world: 1x1 regions (32x32 blocks)
        _smallWorld = new TickableWorld();
        _smallWorld.Generator = new TestPatternGenerator();
        _smallRenderer = new WorldRenderer(_smallWorld, ScreenWidth, ScreenHeight);
        
        // Generate and populate one region
        _smallWorld.SetBlock(new Vector2(5, 5), Blocks.Stone.id);
        _smallWorld.SetBlock(new Vector2(10, 10), Blocks.Water.id);
        _smallWorld.SetBlock(new Vector2(15, 15), Blocks.Sand.id);
    }

    private void SetupMediumWorld()
    {
        // Medium world: 2x2 regions (64x64 blocks)
        _mediumWorld = new TickableWorld();
        _mediumWorld.Generator = new TestPatternGenerator();
        _mediumRenderer = new WorldRenderer(_mediumWorld, ScreenWidth, ScreenHeight);
        
        // Populate multiple regions
        for (int regionX = 0; regionX < 2; regionX++)
        {
            for (int regionY = 0; regionY < 2; regionY++)
            {
                Vector2 regionPos = new Vector2(regionX, regionY);
                Vector2 basePos = regionPos * TickableWorld.RegionSize;
                
                // Add varied blocks to each region
                _mediumWorld.SetBlock(basePos + new Vector2(5, 5), Blocks.Stone.id);
                _mediumWorld.SetBlock(basePos + new Vector2(10, 10), Blocks.Water.id);
                _mediumWorld.SetBlock(basePos + new Vector2(20, 20), Blocks.Sand.id);
            }
        }
    }

    private void SetupLargeWorld()
    {
        // Large world: 4x4 regions (128x128 blocks)
        _largeWorld = new TickableWorld();
        _largeWorld.Generator = new TestPatternGenerator();
        _largeRenderer = new WorldRenderer(_largeWorld, ScreenWidth, ScreenHeight);
        
        // Populate multiple regions with varied patterns
        for (int regionX = 0; regionX < 4; regionX++)
        {
            for (int regionY = 0; regionY < 4; regionY++)
            {
                Vector2 regionPos = new Vector2(regionX, regionY);
                Vector2 basePos = regionPos * TickableWorld.RegionSize;
                
                // Create a pattern with different densities
                int blockCount = (regionX + regionY) * 3 + 5;
                for (int i = 0; i < blockCount; i++)
                {
                    Vector2 pos = basePos + new Vector2(i % 16, i / 16);
                    uint blockType = (uint)((i % 3) + 1); // Cycle through block types
                    _largeWorld.SetBlock(pos, blockType);
                }
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Raylib.CloseWindow();
    }

    // Benchmark single frame rendering for different world sizes
    [Benchmark(Baseline = true)]
    public void SmallWorld_SingleFrame()
    {
        _smallRenderer.Draw();
    }

    [Benchmark]
    public void MediumWorld_SingleFrame()
    {
        _mediumRenderer.Draw();
    }

    [Benchmark]
    public void LargeWorld_SingleFrame()
    {
        _largeRenderer.Draw();
    }

    // Benchmark active region rendering (no texture caching)
    [Benchmark]
    public void ActiveRegions_Rendering()
    {
        // Make sure blocks are recently updated to keep regions active
        _mediumWorld.SetBlock(new Vector2(5, 5), Blocks.Stone.id);
        
        _mediumRenderer.Draw();
    }

    // Benchmark inactive region rendering (with texture caching)
    [Benchmark]
    public void InactiveRegions_Rendering()
    {
        // Let regions become inactive by simulating time passage
        System.Threading.Thread.Sleep(1100); // Wait longer than SecondsUntilCachedTexture
        
        _mediumRenderer.Draw(); // First call creates textures
        _mediumRenderer.Draw(); // Second call uses cached textures
    }

    // Benchmark camera update operations
    [Benchmark]
    public void Camera_Update()
    {
        _mediumRenderer.UpdateCamera();
    }

    // Benchmark world bounds calculation
    [Benchmark]
    public void WorldBounds_Calculation()
    {
        var bounds = _mediumRenderer.GetVisibleWorldBounds();
    }

    // Benchmark multiple consecutive frames (stress test)
    [Benchmark]
    public void MultipleFrames_SmallWorld()
    {
        for (int i = 0; i < 10; i++)
        {
            _smallRenderer.Draw();
        }
    }

    [Benchmark]
    public void MultipleFrames_MediumWorld()
    {
        for (int i = 0; i < 5; i++)
        {
            _mediumRenderer.Draw();
        }
    }

    // Test different block densities
    [Benchmark]
    public void HighDensity_BlockRendering()
    {
        // Create a world with high block density
        var denseWorld = new TickableWorld();
        denseWorld.Generator = new TestPatternGenerator();
        var denseRenderer = new WorldRenderer(denseWorld, ScreenWidth, ScreenHeight);
        
        // Fill a region densely with blocks
        for (int x = 0; x < TickableWorld.RegionSize; x++)
        {
            for (int y = 0; y < TickableWorld.RegionSize; y++)
            {
                if ((x + y) % 2 == 0) // Checkerboard pattern
                {
                    denseWorld.SetBlock(new Vector2(x, y), Blocks.Stone.id);
                }
            }
        }
        
        denseRenderer.Draw();
    }

    // Test sparse block rendering
    [Benchmark]
    public void SparseBlocks_Rendering()
    {
        // Create a world with sparse blocks
        var sparseWorld = new TickableWorld();
        sparseWorld.Generator = new TestPatternGenerator();
        var sparseRenderer = new WorldRenderer(sparseWorld, ScreenWidth, ScreenHeight);
        
        // Add only a few blocks
        sparseWorld.SetBlock(new Vector2(5, 5), Blocks.Stone.id);
        sparseWorld.SetBlock(new Vector2(25, 25), Blocks.Water.id);
        
        sparseRenderer.Draw();
    }

    private class TestPatternGenerator : IGenerator
    {
        public override void Generate(IUnit unit)
        {
            // Generate a simple test pattern
            Vector2 basePos = unit.MinPos;
            
            // Add some base terrain
            for (int x = (int)unit.MinPos.X; x < unit.MaxPos.X; x++)
            {
                if (x % 8 == 0) // Sparse pattern
                {
                    unit.Set(new Vector2(x, unit.MinPos.Y), Blocks.Sand.id);
                }
            }
        }
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