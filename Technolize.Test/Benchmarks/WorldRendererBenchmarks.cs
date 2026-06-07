using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using Technolize.Rendering;
using Technolize.Rendering.Graphics;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;

namespace Technolize.Test.Benchmarks;

/// <summary>
/// Explicit graphics-rendering benchmarks for the Silk.NET OpenGL 4.6 world shader path. They run the
/// exact deterministic scene the render snapshot uses (<c>render-snapshot</c>), so the measured work is
/// the real frame: serialise the world quadtree to the SSBO + colour texture and run
/// <c>world_renderer.frag</c>.
///
/// Two phases are measured separately so a regression can be attributed to the CPU resource build (the
/// SSBO/colour packing) or the GPU render itself:
/// <list type="bullet">
///   <item><description><see cref="BuildResources_Cpu"/> — pure-CPU <see cref="WorldShaderResourceBuilder"/>.</description></item>
///   <item><description><see cref="RenderFrame_Gpu"/> — per-frame upload + shader draw into an offscreen
///     framebuffer (no GPU→CPU pixel readback).</description></item>
/// </list>
///
/// The GPU benchmark renders <see cref="FrameCount"/> frames per invocation and reports the cost
/// <em>per frame</em> via <c>OperationsPerInvoke</c>. It deliberately avoids the framebuffer readback
/// (the synchronous <c>glReadPixels</c> stall the snapshot path pays) so the number reflects the render
/// cost rather than the pixel transfer; a single <c>glFinish</c> after the batch ensures all queued
/// frames actually complete before timing stops.
///
/// The GPU benchmark needs a real offscreen context, so it only runs where a display/GL is available
/// (the same machines the render snapshot runs on); it is created once in <see cref="GlobalSetup"/>.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class WorldRendererBenchmarks
{
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const int SceneRadius = 200;

    // Frames rendered per GPU benchmark invocation. OperationsPerInvoke divides the measured time by
    // this so the reported Mean is the per-frame render cost.
    private const int FrameCount = 30;

    // Matches the snapshot's fixed time so the animated water/sun is exercised mid-animation.
    private const float FixedTime = 7.5f;

    private GlContext _context = null!;
    private WorldShaderRenderer _renderer = null!;
    private GlFramebuffer _framebuffer = null!;
    private WorldRenderFrame _frame = null!;
    private Vector2 _regionStart;
    private Vector2 _regionEnd;
    private Camera2D _camera;

    [GlobalSetup]
    public void GlobalSetup()
    {
        TickableWorld world = CreateRenderProfileWorld();
        _frame = WorldRenderFrameBuilder.FromWorld(world);
        (_regionStart, _regionEnd) = ComputeWorldRegionBounds(_frame);
        _camera = Camera2D.Centered(ScreenWidth, ScreenHeight);

        _context = GlContext.CreateOffscreen(ScreenWidth, ScreenHeight);

        // BenchmarkDotNet runs in a generated child process whose working directory is not the test
        // bin folder, so resolve the shaders relative to this assembly (whose output does contain them)
        // rather than AppContext.BaseDirectory.
        string assemblyDir = Path.GetDirectoryName(typeof(WorldRendererBenchmarks).Assembly.Location)!;
        string shaderDir = Path.Combine(assemblyDir, "shaders");
        _renderer = new WorldShaderRenderer(_context.Gl, shaderDir);

        // A persistent offscreen colour target the per-frame render draws into; never read back.
        _framebuffer = new GlFramebuffer(_context.Gl, ScreenWidth, ScreenHeight);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _framebuffer?.Dispose();
        _renderer?.Dispose();
        _context?.Dispose();
    }

    [Benchmark(Description = "CPU: build SSBO + colour resources")]
    public WorldShaderResourceData BuildResources_Cpu()
    {
        return WorldShaderResourceBuilder.Build(_frame, _regionStart, _regionEnd);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = FrameCount, Description = "GPU: render per frame (1280x720, no readback)")]
    public void RenderFrame_Gpu()
    {
        _framebuffer.Bind();
        for (int i = 0; i < FrameCount; i++)
        {
            _renderer.RenderToTarget(
                _frame, _regionStart, _regionEnd, WorldLighting.Default, FixedTime,
                _camera, ScreenWidth, ScreenHeight, drawGrid: true);
        }

        // Force all queued frames to finish so the timing reflects real GPU work, without paying the
        // GPU→CPU pixel readback the snapshot path uses.
        _context.Gl.Finish();
        _framebuffer.Unbind();
    }

    /// <summary>
    /// The deterministic render scene shared with the snapshot gate: a water body with a stone floor,
    /// regular stone pillars, and sparse air pockets, so the shader's reflection/refraction/escape
    /// paths are all exercised.
    /// </summary>
    private static TickableWorld CreateRenderProfileWorld()
    {
        TickableWorld world = new() { Generator = new BlankGenerator() };
        const int radius = SceneRadius;

        world.BatchSetBlocks(placer =>
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    uint block = Blocks.Water.Id;
                    if (y <= -radius + 10)
                    {
                        block = Blocks.Stone.Id;         // solid floor: water/solid interfaces (reflection path)
                    }
                    else if (x % 24 == 0 && y < radius - 40)
                    {
                        block = Blocks.Stone.Id;         // pillars for more interfaces
                    }
                    else if ((x + y) % 31 == 0)
                    {
                        block = Blocks.Air.Id;           // sparse air pockets so rays escape/refract
                    }

                    placer.Set(new Vector2(x, y), block);
                }
            }
        });

        return world;
    }

    /// <summary>
    /// Half-open region-space bounding box covering every loaded region in the frame, so the whole
    /// world is rendered. Falls back to a unit box when no regions are loaded.
    /// </summary>
    private static (Vector2 start, Vector2 end) ComputeWorldRegionBounds(WorldRenderFrame frame)
    {
        if (frame.Regions.Count == 0)
        {
            return (Vector2.Zero, Vector2.One);
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (WorldRenderRegion region in frame.Regions)
        {
            minX = Math.Min(minX, region.Position.X);
            minY = Math.Min(minY, region.Position.Y);
            maxX = Math.Max(maxX, region.Position.X);
            maxY = Math.Max(maxY, region.Position.Y);
        }

        return (new Vector2(minX, minY), new Vector2(maxX + 1, maxY + 1));
    }

    public class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddJob(Job.ShortRun.WithRuntime(CoreRuntime.Core80));
        }
    }
}
