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

/// <summary>
/// Benchmarks comparing WorldRenderer (CPU-based) vs WorldShaderRenderer (GPU-based) performance.
/// Tests various scenarios to understand performance characteristics of each approach.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class WorldShaderRendererBenchmarks
{
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1080;

    private TickableWorld _smallWorld = null!;
    private TickableWorld _mediumWorld = null!;
    private TickableWorld _largeWorld = null!;

    private WorldRenderer _smallCpuRenderer = null!;
    private WorldRenderer _mediumCpuRenderer = null!;
    private WorldRenderer _largeCpuRenderer = null!;

    private WorldShaderRenderer _smallShaderRenderer = null!;
    private WorldShaderRenderer _mediumShaderRenderer = null!;
    private WorldShaderRenderer _largeShaderRenderer = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize Raylib for benchmarking
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Benchmark");

        // Create test worlds with different sizes and complexities
        _smallWorld = CreateTestWorld(WorldSize.Small);
        _mediumWorld = CreateTestWorld(WorldSize.Medium);
        _largeWorld = CreateTestWorld(WorldSize.Large);

        // Create CPU renderers
        _smallCpuRenderer = new WorldRenderer(_smallWorld, ScreenWidth, ScreenHeight);
        _mediumCpuRenderer = new WorldRenderer(_mediumWorld, ScreenWidth, ScreenHeight);
        _largeCpuRenderer = new WorldRenderer(_largeWorld, ScreenWidth, ScreenHeight);

        // Create shader renderers
        _smallShaderRenderer = new WorldShaderRenderer(_smallWorld, ScreenWidth, ScreenHeight);
        _mediumShaderRenderer = new WorldShaderRenderer(_mediumWorld, ScreenWidth, ScreenHeight);
        _largeShaderRenderer = new WorldShaderRenderer(_largeWorld, ScreenWidth, ScreenHeight);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _smallShaderRenderer.Dispose();
        _mediumShaderRenderer.Dispose();
        _largeShaderRenderer.Dispose();
        
        Raylib.CloseWindow();
    }

    // Small World Benchmarks
    [Benchmark(Baseline = true)]
    public void SmallWorld_CPU_SingleFrame()
    {
        _smallCpuRenderer.Draw();
    }

    [Benchmark]
    public void SmallWorld_Shader_SingleFrame()
    {
        _smallShaderRenderer.Draw();
    }

    // Medium World Benchmarks
    [Benchmark]
    public void MediumWorld_CPU_SingleFrame()
    {
        _mediumCpuRenderer.Draw();
    }

    [Benchmark]
    public void MediumWorld_Shader_SingleFrame()
    {
        _mediumShaderRenderer.Draw();
    }

    // Large World Benchmarks
    [Benchmark]
    public void LargeWorld_CPU_SingleFrame()
    {
        _largeCpuRenderer.Draw();
    }

    [Benchmark]
    public void LargeWorld_Shader_SingleFrame()
    {
        _largeShaderRenderer.Draw();
    }

    // Active vs Inactive Region Benchmarks
    [Benchmark]
    public void ActiveRegions_CPU_Rendering()
    {
        // Make sure blocks are recently updated to keep regions active
        _mediumWorld.SetBlock(new Vector2(5, 5), Blocks.Stone.id);
        _mediumCpuRenderer.Draw();
    }

    [Benchmark]
    public void ActiveRegions_Shader_Rendering()
    {
        // Make sure blocks are recently updated to keep regions active
        _mediumWorld.SetBlock(new Vector2(5, 5), Blocks.Stone.id);
        _mediumShaderRenderer.Draw();
    }

    [Benchmark]
    public void InactiveRegions_CPU_Rendering()
    {
        // Let regions become inactive by simulating time passage
        System.Threading.Thread.Sleep(1100); // Wait longer than SecondsUntilCachedTexture
        
        _mediumCpuRenderer.Draw(); // First call creates textures
        _mediumCpuRenderer.Draw(); // Second call uses cached textures
    }

    [Benchmark]
    public void InactiveRegions_Shader_Rendering()
    {
        // Let regions become inactive by simulating time passage
        System.Threading.Thread.Sleep(1100); // Wait longer than SecondsUntilCachedTexture
        
        _mediumShaderRenderer.Draw(); // First call creates textures with shaders
        _mediumShaderRenderer.Draw(); // Second call uses cached textures
    }

    // Multiple Frame Stress Tests
    [Benchmark]
    public void MultipleFrames_CPU_SmallWorld()
    {
        for (int i = 0; i < 10; i++)
        {
            _smallCpuRenderer.Draw();
        }
    }

    [Benchmark]
    public void MultipleFrames_Shader_SmallWorld()
    {
        for (int i = 0; i < 10; i++)
        {
            _smallShaderRenderer.Draw();
        }
    }

    [Benchmark]
    public void MultipleFrames_CPU_MediumWorld()
    {
        for (int i = 0; i < 5; i++)
        {
            _mediumCpuRenderer.Draw();
        }
    }

    [Benchmark]
    public void MultipleFrames_Shader_MediumWorld()
    {
        for (int i = 0; i < 5; i++)
        {
            _mediumShaderRenderer.Draw();
        }
    }

    // Camera Operations Benchmarks
    [Benchmark]
    public void Camera_Update_CPU()
    {
        _mediumCpuRenderer.UpdateCamera();
    }

    [Benchmark]
    public void Camera_Update_Shader()
    {
        _mediumShaderRenderer.UpdateCamera();
    }

    // Bounds Calculation Benchmarks
    [Benchmark]
    public void WorldBounds_Calculation_CPU()
    {
        var bounds = _mediumCpuRenderer.GetVisibleWorldBounds();
    }

    [Benchmark]
    public void WorldBounds_Calculation_Shader()
    {
        var bounds = _mediumShaderRenderer.GetVisibleWorldBounds();
    }

    // Block Density Tests
    [Benchmark]
    public void HighDensity_CPU_BlockRendering()
    {
        var denseWorld = CreateDenseWorld();
        var denseRenderer = new WorldRenderer(denseWorld, ScreenWidth, ScreenHeight);
        denseRenderer.Draw();
    }

    [Benchmark]
    public void HighDensity_Shader_BlockRendering()
    {
        var denseWorld = CreateDenseWorld();
        var denseRenderer = new WorldShaderRenderer(denseWorld, ScreenWidth, ScreenHeight);
        denseRenderer.Draw();
        denseRenderer.Dispose();
    }

    [Benchmark]
    public void SparseBlocks_CPU_Rendering()
    {
        var sparseWorld = CreateSparseWorld();
        var sparseRenderer = new WorldRenderer(sparseWorld, ScreenWidth, ScreenHeight);
        sparseRenderer.Draw();
    }

    [Benchmark]
    public void SparseBlocks_Shader_Rendering()
    {
        var sparseWorld = CreateSparseWorld();
        var sparseRenderer = new WorldShaderRenderer(sparseWorld, ScreenWidth, ScreenHeight);
        sparseRenderer.Draw();
        sparseRenderer.Dispose();
    }

    private enum WorldSize
    {
        Small,
        Medium,
        Large
    }

    private TickableWorld CreateTestWorld(WorldSize size)
    {
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        int blockCount = size switch
        {
            WorldSize.Small => 100,
            WorldSize.Medium => 1000,
            WorldSize.Large => 5000,
            _ => 100
        };

        int maxCoord = size switch
        {
            WorldSize.Small => 50,
            WorldSize.Medium => 100,
            WorldSize.Large => 200,
            _ => 50
        };

        var random = new Random(42); // Fixed seed for consistent benchmarks

        for (int i = 0; i < blockCount; i++)
        {
            int x = random.Next(-maxCoord, maxCoord);
            int y = random.Next(-maxCoord, maxCoord);
            var blockTypes = Blocks.AllBlocks().ToArray();
            var blockType = blockTypes[random.Next(blockTypes.Length)];
            
            world.SetBlock(new Vector2(x, y), blockType.id);
        }

        // Force region creation
        for (int x = -maxCoord; x <= maxCoord; x += TickableWorld.RegionSize)
        {
            for (int y = -maxCoord; y <= maxCoord; y += TickableWorld.RegionSize)
            {
                world.GetBlock(new Vector2(x, y));
            }
        }

        return world;
    }

    private TickableWorld CreateDenseWorld()
    {
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        // Fill a region densely with blocks
        for (int x = 0; x < TickableWorld.RegionSize; x++)
        {
            for (int y = 0; y < TickableWorld.RegionSize; y++)
            {
                if ((x + y) % 2 == 0) // Checkerboard pattern
                {
                    world.SetBlock(new Vector2(x, y), Blocks.Stone.id);
                }
            }
        }

        world.GetBlock(new Vector2(0, 0)); // Force region creation
        return world;
    }

    private TickableWorld CreateSparseWorld()
    {
        var world = new TickableWorld();
        world.Generator = new TestPatternGenerator();

        // Add only a few blocks
        world.SetBlock(new Vector2(5, 5), Blocks.Stone.id);
        world.SetBlock(new Vector2(25, 25), Blocks.Water.id);
        world.SetBlock(new Vector2(45, 45), Blocks.Sand.id);

        world.GetBlock(new Vector2(0, 0)); // Force region creation
        return world;
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