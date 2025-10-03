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
/// Fast comparative benchmark for WorldRenderer vs WorldShaderRenderer performance.
/// Focuses on the most critical rendering scenarios with reduced setup time.
/// </summary>
[Config(typeof(FastBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class FastWorldRendererComparison
{
    private const int ScreenWidth = 800;
    private const int ScreenHeight = 600;

    private TickableWorld _testWorld = null!;
    private WorldRenderer _cpuRenderer = null!;
    private WorldShaderRenderer _shaderRenderer = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Initialize Raylib for benchmarking
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Fast Benchmark");

        // Create a focused test world
        _testWorld = CreateFocusedTestWorld();

        // Create renderers
        _cpuRenderer = new WorldRenderer(_testWorld, ScreenWidth, ScreenHeight);
        _shaderRenderer = new WorldShaderRenderer(_testWorld, ScreenWidth, ScreenHeight);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shaderRenderer.Dispose();
        Raylib.CloseWindow();
    }

    // Single Frame Rendering Benchmarks
    [Benchmark(Baseline = true)]
    public void CPU_SingleFrame()
    {
        _cpuRenderer.Draw();
    }

    [Benchmark]
    public void Shader_SingleFrame()
    {
        _shaderRenderer.Draw();
    }

    // Multiple Frame Rendering for Throughput Testing
    [Benchmark]
    public void CPU_MultipleFrames()
    {
        for (int i = 0; i < 5; i++)
        {
            _cpuRenderer.Draw();
        }
    }

    [Benchmark]
    public void Shader_MultipleFrames()
    {
        for (int i = 0; i < 5; i++)
        {
            _shaderRenderer.Draw();
        }
    }

    // Camera Update Performance
    [Benchmark]
    public void CPU_CameraUpdate()
    {
        _cpuRenderer.UpdateCamera();
    }

    [Benchmark]
    public void Shader_CameraUpdate()
    {
        _shaderRenderer.UpdateCamera();
    }

    // Bounds Calculation Performance
    [Benchmark]
    public void CPU_BoundsCalculation()
    {
        var bounds = _cpuRenderer.GetVisibleWorldBounds();
    }

    [Benchmark]
    public void Shader_BoundsCalculation()
    {
        var bounds = _shaderRenderer.GetVisibleWorldBounds();
    }

    // Block-dense rendering test
    [Benchmark]
    public void CPU_DenseBlocks()
    {
        var denseWorld = CreateDenseBlockWorld();
        var denseRenderer = new WorldRenderer(denseWorld, ScreenWidth, ScreenHeight);
        denseRenderer.Draw();
    }

    [Benchmark]
    public void Shader_DenseBlocks()
    {
        var denseWorld = CreateDenseBlockWorld();
        var denseRenderer = new WorldShaderRenderer(denseWorld, ScreenWidth, ScreenHeight);
        denseRenderer.Draw();
        denseRenderer.Dispose();
    }

    private TickableWorld CreateFocusedTestWorld()
    {
        var world = new TickableWorld();
        world.Generator = new FastTestGenerator();

        // Create a medium-sized test scenario that covers common use cases
        var random = new Random(42); // Fixed seed for consistent benchmarks
        var blockTypes = Blocks.AllBlocks().ToArray();

        // Add blocks in a pattern that exercises both active and inactive regions
        for (int x = -32; x <= 32; x += 4)
        {
            for (int y = -32; y <= 32; y += 4)
            {
                if (random.NextDouble() < 0.3) // 30% block density
                {
                    var blockType = blockTypes[random.Next(blockTypes.Length)];
                    world.SetBlock(new Vector2(x, y), blockType.id);
                }
            }
        }

        // Force region creation for consistent benchmarking
        for (int x = -32; x <= 32; x += TickableWorld.RegionSize)
        {
            for (int y = -32; y <= 32; y += TickableWorld.RegionSize)
            {
                world.GetBlock(new Vector2(x, y));
            }
        }

        return world;
    }

    private TickableWorld CreateDenseBlockWorld()
    {
        var world = new TickableWorld();
        world.Generator = new FastTestGenerator();

        // Create a single region with high block density
        for (int x = 0; x < TickableWorld.RegionSize; x += 2)
        {
            for (int y = 0; y < TickableWorld.RegionSize; y += 2)
            {
                world.SetBlock(new Vector2(x, y), Blocks.Stone.id);
            }
        }

        world.GetBlock(new Vector2(0, 0)); // Force region creation
        return world;
    }

    private class FastTestGenerator : IGenerator
    {
        public override void Generate(IUnit unit)
        {
            // Minimal generation for focused benchmarking
            Vector2 basePos = unit.MinPos;
            
            // Add sparse base terrain
            for (int x = (int)unit.MinPos.X; x < unit.MaxPos.X; x += 16)
            {
                unit.Set(new Vector2(x, unit.MinPos.Y), Blocks.Sand.id);
            }
        }
    }

    public class FastBenchmarkConfig : ManualConfig
    {
        public FastBenchmarkConfig()
        {
            // Use in-process execution with minimal iterations for fast results
            AddJob(Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithIterationCount(3)  // Reduced for faster execution
                .WithWarmupCount(1));   // Minimal warmup
        }
    }
}