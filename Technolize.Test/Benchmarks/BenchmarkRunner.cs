using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Numerics;
using Technolize.Test.Benchmarks;
using Technolize.Rendering;
using Technolize.Rendering.Graphics;
using Technolize.Runtime;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Ticking;
using Silk.NET.Windowing;

namespace Technolize.Test;

/// <summary>
/// Main entry point for Technolize.Test project.
/// Can run unit tests (default) or performance benchmarks.
/// </summary>
public class Program
{
    private const int WaterBodyWidth = 1000;
    private const int WaterBodyHeight = 1000;
    private const int WaterBodyStartY = 100000;

    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "benchmark")
        {
            Console.WriteLine("=== Technolize SIMD Performance Benchmarks ===");
            Console.WriteLine($"Running on .NET {Environment.Version}");
            Console.WriteLine($"Processor Count: {Environment.ProcessorCount}");
            Console.WriteLine($"Is 64-bit: {Environment.Is64BitProcess}");
            Console.WriteLine();

            string filter = args.Length > 2 && args[1] == "--filter" ? args[2] : "*";

            if (filter.Contains("SignatureProcessor") || filter == "*")
            {
                Console.WriteLine("Starting SignatureProcessor benchmarks...");
                BenchmarkDotNet.Running.BenchmarkRunner.Run<SignatureProcessorBenchmarks>();
            }

            if (filter.Contains("SignatureWorldTicker") || filter == "*")
            {
                Console.WriteLine("\nStarting SignatureWorldTicker benchmarks...");
                BenchmarkDotNet.Running.BenchmarkRunner.Run<SignatureWorldTickerBenchmarks>();
            }

            if (filter.Contains("WaterfallWorldTicker") || filter == "*")
            {
                Console.WriteLine("\nStarting WaterfallWorldTicker benchmarks...");
                Console.WriteLine("WaterfallWorldTicker benchmark class is not available in this repo state; use 'profile-waterfall' for manual timing analysis.");
            }

            if (filter.Contains("SimdVsScalar") || filter == "*")
            {
                Console.WriteLine("\nStarting SIMD vs Scalar comparison benchmarks...");
                BenchmarkDotNet.Running.BenchmarkRunner.Run<SimdVsScalarBenchmarks>();
            }

            if (filter.Contains("QuickSimdVsScalar") || filter == "*")
            {
                Console.WriteLine("\nStarting Quick SIMD vs Scalar benchmarks (30 seconds)...");
                BenchmarkDotNet.Running.BenchmarkRunner.Run<QuickSimdVsScalarBenchmarks>();
            }

            Console.WriteLine();
            Console.WriteLine("=== Benchmark Summary ===");
            Console.WriteLine("Check BenchmarkDotNet.Artifacts folder for detailed results.");
            Console.WriteLine("Key metrics to analyze:");
            Console.WriteLine("- Mean execution time");
            Console.WriteLine("- Memory allocations");
            Console.WriteLine("- Scalability with data size");
            Console.WriteLine("- SIMD vector utilization");
            Console.WriteLine("- SIMD vs Scalar performance ratio");
        }
        else if (args.Length > 0 && args[0] == "profile-waterfall")
        {
            TickableWorld world = CreateWaterfallProfileWorld();
            using SignatureWorldTicker ticker = new(world);
            int tickCount = args.Length > 1 && int.TryParse(args[1], out int parsedTickCount) ? parsedTickCount : 200;
            bool fullScan = args.Any(arg => string.Equals(arg, "full-scan", StringComparison.OrdinalIgnoreCase));
            bool saturateCpu = args.Any(arg => string.Equals(arg, "saturate", StringComparison.OrdinalIgnoreCase));
            using CpuSaturator cpuSaturator = new(saturateCpu ? Environment.ProcessorCount : 0);
            cpuSaturator.SetEnabled(saturateCpu);

            Console.WriteLine($"Profiling waterfall workload for {tickCount} ticks...");
            Console.WriteLine($"Water body: {WaterBodyWidth}x{WaterBodyHeight} at y={WaterBodyStartY}");
            Console.WriteLine($"FullScan={fullScan}");
            Console.WriteLine($"SaturateCpu={saturateCpu}");
            ticker.ManualTimingsEnabled = true;

            Stopwatch stopwatch = Stopwatch.StartNew();
            int lastActiveRegionCount = 0;
            TickCycleTimingTotals timingTotals = new();
            for (int i = 0; i < tickCount; i++)
            {
                lastActiveRegionCount = ticker.Tick(fullScan);
                if (ticker.LastTickTimings is TickCycleTimings timings)
                {
                    timingTotals.Add(timings);
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"ElapsedMs={stopwatch.Elapsed.TotalMilliseconds:0.00}");
            Console.WriteLine($"Ticks={tickCount}");
            Console.WriteLine($"LastActiveRegions={lastActiveRegionCount}");
            Console.WriteLine($"AverageTickMs={timingTotals.GetAverage(t => t.TotalMs):0.00}");
            Console.WriteLine($"AverageRegionSelectionMs={timingTotals.GetAverage(t => t.RegionSelectionMs):0.00}");
            Console.WriteLine($"AverageParallelPhaseMs={timingTotals.GetAverage(t => t.ParallelPhaseMs):0.00}");
            Console.WriteLine($"AverageRegionPaddingPerActiveRegionMs={timingTotals.GetAverage(t => t.RegionPaddingPerActiveRegionMs):0.000}");
            Console.WriteLine($"AverageSignaturePerActiveRegionMs={timingTotals.GetAverage(t => t.SignatureComputationPerActiveRegionMs):0.000}");
            Console.WriteLine($"AverageRuleMatchingPerActiveRegionMs={timingTotals.GetAverage(t => t.RuleMatchingPerActiveRegionMs):0.000}");
        }
        else if (args.Length > 0 && args[0] == "render-snapshot")
        {
            RunRenderSnapshot(args);
        }
        else if (args.Length > 0 && args[0] == "view")
        {
            RunWorldView(args);
        }
        else
        {
            Console.WriteLine("Technolize.Test - Use 'benchmark' argument to run performance benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- profile-waterfall [tickCount]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- render-snapshot [frameCount] [--update]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- view [seconds]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*QuickSimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SignatureProcessor*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*WorldRenderer*\"");
        }
    }

    private const int RenderProfileScreenWidth = 1280;
    private const int RenderProfileScreenHeight = 720;
    private const int RenderProfileSceneRadius = 200;

    // A fixed time value for the shader's animated water/sun so snapshots are deterministic. Chosen
    // to land mid-animation so waves/sway are visibly exercised rather than at a trivial t=0 state.
    private const float SnapshotFixedTime = 7.5f;


    /// <summary>
    /// The Silk.NET OpenGL 4.6 deterministic render snapshot. Renders a fixed scene through
    /// <see cref="WorldShaderRenderer.RenderToScreen"/> into an offscreen framebuffer, exports the
    /// result with the dependency-free <see cref="PngWriter"/>, and compares it to a committed Silk
    /// baseline pixel-for-pixel. This is the regression gate for the render path.
    /// </summary>
    private static void RunRenderSnapshot(string[] args)
    {
        int frameCount = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 60;
        bool update = args.Any(a => string.Equals(a, "--update", StringComparison.OrdinalIgnoreCase));
        const int warmupFrames = 10;

        TickableWorld world = CreateRenderProfileWorld();
        WorldRenderFrame frame = WorldRenderFrameBuilder.FromWorld(world);
        (Vector2 regionStart, Vector2 regionEnd) = ComputeWorldRegionBounds(frame);
        Camera2D camera = Camera2D.Centered(RenderProfileScreenWidth, RenderProfileScreenHeight);

        string snapshotDir = Path.Combine(FindRepoRoot(), "render-snapshots");
        Directory.CreateDirectory(snapshotDir);
        string baselinePath = Path.Combine(snapshotDir, "baseline.png");
        string latestPath = Path.Combine(snapshotDir, "latest.png");
        string diffPath = Path.Combine(snapshotDir, "diff.png");

        Console.WriteLine("=== Technolize Silk Render Snapshot ===");
        Console.WriteLine($"Screen: {RenderProfileScreenWidth}x{RenderProfileScreenHeight}, FixedTime={SnapshotFixedTime}");
        Console.WriteLine($"Baseline: {baselinePath}");

        using GlContext context = GlContext.CreateOffscreen(RenderProfileScreenWidth, RenderProfileScreenHeight);
        using WorldShaderRenderer renderer = new(context.Gl);

        // Timing loop: render the same path repeatedly so the per-frame GPU cost is reported.
        List<double> frameMs = new(frameCount);
        Stopwatch frameTimer = new();
        byte[] captured = Array.Empty<byte>();
        for (int frame_i = 0; frame_i < warmupFrames + frameCount; frame_i++)
        {
            frameTimer.Restart();
            captured = renderer.RenderToScreen(
                frame, regionStart, regionEnd, WorldLighting.Default, SnapshotFixedTime,
                camera, RenderProfileScreenWidth, RenderProfileScreenHeight, drawGrid: true);
            double ms = frameTimer.Elapsed.TotalMilliseconds;
            if (frame_i >= warmupFrames)
            {
                frameMs.Add(ms);
            }
        }

        PngWriter.WriteFile(latestPath, captured, RenderProfileScreenWidth, RenderProfileScreenHeight);

        long totalPixels = (long)RenderProfileScreenWidth * RenderProfileScreenHeight;
        long mismatchedPixels = 0;
        int maxChannelDiff = 0;
        string result;

        if (update || !File.Exists(baselinePath))
        {
            PngWriter.WriteFile(baselinePath, captured, RenderProfileScreenWidth, RenderProfileScreenHeight);
            result = update ? "UPDATED" : "CREATED";
        }
        else
        {
            byte[] baseline = File.ReadAllBytes(baselinePath);
            byte[] latest = File.ReadAllBytes(latestPath);
            if (baseline.AsSpan().SequenceEqual(latest))
            {
                // Identical encoded files ⇒ identical pixels; skip the per-pixel decode.
                result = "MATCH";
            }
            else
            {
                // Files differ: re-render the committed baseline scene and diff in pixel space. Because
                // the baseline was produced by this same deterministic path, any difference is a real
                // regression, captured into a red-on-black diff image.
                byte[] baselinePixels = BaselineComparePixels(baselinePath, captured.Length, out bool sizeOk);
                if (!sizeOk)
                {
                    result = "SIZE_MISMATCH";
                }
                else
                {
                    byte[] diff = new byte[captured.Length];
                    for (long i = 0; i < totalPixels; i++)
                    {
                        long b = i * 4;
                        int dr = Math.Abs(captured[b + 0] - baselinePixels[b + 0]);
                        int dg = Math.Abs(captured[b + 1] - baselinePixels[b + 1]);
                        int db = Math.Abs(captured[b + 2] - baselinePixels[b + 2]);
                        int da = Math.Abs(captured[b + 3] - baselinePixels[b + 3]);
                        int d = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                        if (d != 0)
                        {
                            mismatchedPixels++;
                            if (d > maxChannelDiff) maxChannelDiff = d;
                            diff[b + 0] = 255;
                            diff[b + 3] = 255;
                        }
                        else
                        {
                            diff[b + 3] = 255;
                        }
                    }

                    if (mismatchedPixels > 0)
                    {
                        PngWriter.WriteFile(diffPath, diff, RenderProfileScreenWidth, RenderProfileScreenHeight);
                    }

                    result = mismatchedPixels == 0 ? "MATCH" : "MISMATCH";
                }
            }
        }

        frameMs.Sort();
        double mean = frameMs.Count > 0 ? frameMs.Average() : 0.0;
        double p95 = frameMs.Count > 0 ? Percentile(frameMs, 0.95) : 0.0;

        Console.WriteLine();
        Console.WriteLine("=== Snapshot Result ===");
        Console.WriteLine($"SnapshotResult={result}");
        Console.WriteLine($"TotalPixels={totalPixels}");
        Console.WriteLine($"MismatchedPixels={mismatchedPixels}");
        Console.WriteLine($"MaxChannelDiff={maxChannelDiff}");
        Console.WriteLine($"PixelPerfect={(result == "MATCH" ? "YES" : (result is "CREATED" or "UPDATED" ? "BASELINE" : "NO"))}");
        Console.WriteLine($"LatestImage={latestPath}");
        Console.WriteLine();
        Console.WriteLine("=== Timing (per frame, lower is better) ===");
        Console.WriteLine($"FramesMeasured={frameMs.Count}");
        Console.WriteLine($"MeanMs={mean:0.000}");
        Console.WriteLine($"P95Ms={p95:0.000}");
        Console.WriteLine($"MeanFps={(mean > 0 ? 1000.0 / mean : 0):0.0}");
    }

    /// <summary>
    /// Opens a live, interactive Silk.NET OpenGL 4.6 window showing the profile world via
    /// <see cref="WorldView"/> (left-drag pan, wheel zoom, ImGui HUD). Closes on window-close or
    /// after the optional time limit (default: runs until closed).
    /// </summary>
    private static void RunWorldView(string[] args)
    {
        double? maxSeconds = args.Length > 1 && double.TryParse(args[1], out double s) ? s : null;

        TickableWorld world = CreateRenderProfileWorld();

        using GlContext context = GlContext.CreateWindowed(RenderProfileScreenWidth, RenderProfileScreenHeight, "Technolize — Silk World View");
        using WorldView view = new(context, new TickableWorldRenderSource(world));

        Console.WriteLine("=== Technolize Silk World View ===");
        Console.WriteLine("Left-drag to pan, mouse wheel to zoom. Close the window to exit.");

        // Drive the loop manually rather than IWindow.Run(): GlContext already called Initialize()
        // and made the context current on this thread, so a manual DoEvents/render/swap loop avoids the
        // double-initialisation that Run() would trigger.
        Stopwatch clock = Stopwatch.StartNew();
        double lastTime = 0.0;
        while (!context.Window.IsClosing)
        {
            context.Window.DoEvents();
            if (context.Window.IsClosing)
            {
                break;
            }

            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)(now - lastTime);
            lastTime = now;

            view.RenderFrame(delta);
            context.Window.GLContext?.SwapBuffers();

            if (maxSeconds is double limit && now >= limit)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Re-renders the committed Silk baseline image's pixels by decoding it, so a mismatch can be diffed
    /// in pixel space. Returns the decoded RGBA8 buffer; <paramref name="sizeOk"/> is false if it does
    /// not match the expected length.
    /// </summary>
    private static byte[] BaselineComparePixels(string baselinePath, int expectedLength, out bool sizeOk)
    {
        byte[] pixels = PngReader.DecodeRgba8(File.ReadAllBytes(baselinePath), out _, out _);
        sizeOk = pixels.Length == expectedLength;
        return pixels;
    }

    /// <summary>
    /// Returns the half-open region-space bounding box covering every loaded region in the frame, so
    /// the whole world is rendered (matching <see cref="WorldShaderRenderer"/>). Falls back to a unit
    /// box when no regions are loaded.
    /// </summary>
    private static (Vector2 start, Vector2 end) ComputeWorldRegionBounds(WorldRenderFrame frame)
    {
        if (frame.Regions.Count == 0)
        {
            return (new Vector2(0, 0), new Vector2(1, 1));
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (WorldRenderRegion region in frame.Regions)
        {
            minX = Math.Min(minX, region.Position.X);
            minY = Math.Min(minY, region.Position.Y);
            maxX = Math.Max(maxX, region.Position.X);
            maxY = Math.Max(maxY, region.Position.Y);
        }

        return (new Vector2(minX, minY), new Vector2(maxX + 1, maxY + 1));
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Technolize.sln")))
            {
                return dir;
            }

            DirectoryInfo? parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }
            dir = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static double Percentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        double rank = percentile * (sortedValues.Count - 1);
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        double weight = rank - low;
        return sortedValues[low] * (1.0 - weight) + sortedValues[high] * weight;
    }

    private static TickableWorld CreateRenderProfileWorld()
    {
        TickableWorld world = new() { Generator = new BlankGenerator() };
        const int radius = RenderProfileSceneRadius;

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

    private static TickableWorld CreateWaterfallProfileWorld()
    {
        TickableWorld world = new();
        world.Generator = new BlankGenerator();

        for (int y = 0; y < WaterBodyHeight; y++)
        {
            int worldY = WaterBodyStartY + y;
            for (int x = 0; x < WaterBodyWidth; x++)
            {
                world.SetBlock(new Vector2(x, worldY), Blocks.Water.Id);
            }
        }

        return world;
    }

    private sealed class TickCycleTimingTotals
    {
        private readonly List<TickCycleTimings> _samples = [];

        public void Add(TickCycleTimings timings)
        {
            _samples.Add(timings);
        }

        public double GetAverage(Func<TickCycleTimings, double> selector)
        {
            return _samples.Count == 0 ? 0.0 : _samples.Average(selector);
        }
    }

    private sealed class CpuSaturator : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Thread[] _workers;
        private volatile bool _enabled;

        public CpuSaturator(int workerCount)
        {
            _workers = Enumerable.Range(0, Math.Max(0, workerCount))
                .Select(index =>
                {
                    Thread worker = new(() => RunWorkerLoop(_cancellationTokenSource.Token))
                    {
                        IsBackground = true,
                        Name = $"CpuSaturator-{index}"
                    };
                    worker.Start();
                    return worker;
                })
                .ToArray();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            foreach (Thread worker in _workers)
            {
                worker.Join();
            }
            _cancellationTokenSource.Dispose();
        }

        private void RunWorkerLoop(CancellationToken cancellationToken)
        {
            SpinWait spinner = new();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_enabled)
                {
                    spinner.SpinOnce();
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}
