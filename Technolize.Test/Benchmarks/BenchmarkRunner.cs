using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using Technolize.Test.Benchmarks;
using Technolize.Rendering;
using Technolize.Runtime;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Ticking;

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

            if (filter.Contains("WorldRenderer") || filter == "*")
            {
                Console.WriteLine("\nStarting World Rendering benchmarks...");
                BenchmarkDotNet.Running.BenchmarkRunner.Run<WorldRendererBenchmarks>();
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
        else if (args.Length > 0 && args[0] == "profile-render")
        {
            RunRenderProfile(args);
        }
        else if (args.Length > 0 && args[0] == "render-snapshot")
        {
            RunRenderSnapshot(args);
        }
        else
        {
            Console.WriteLine("Technolize.Test - Use 'benchmark' argument to run performance benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- profile-waterfall [tickCount]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- profile-render [frameCount]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- render-snapshot [frameCount] [--update]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*QuickSimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SignatureProcessor*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*WorldRenderer*\"");
        }
    }

    private const int RenderProfileScreenWidth = 1280;
    private const int RenderProfileScreenHeight = 720;
    private const int RenderProfileSceneRadius = 200;

    /// <summary>
    /// Renders a fixed, water-heavy scene with the live <see cref="WorldShaderRenderer"/> for a fixed
    /// number of frames and reports per-frame timing. Designed to run on a virtual Xvfb screen with
    /// Mesa software rendering so the GPU fragment cost (the water lighting hot path) can be measured
    /// headlessly. The scene fills the view with water plus solids/air so the shader's per-fragment
    /// ray-marching is exercised the same way it is in-game.
    /// </summary>
    private static void RunRenderProfile(string[] args)
    {
        int frameCount = args.Length > 1 && int.TryParse(args[1], out int parsedFrames) ? parsedFrames : 600;
        const int warmupFrames = 30;

        // Info level so raylib logs the GL vendor/renderer/version, making it obvious whether the
        // run used a real GPU (e.g. WSLg d3d12) or a software rasteriser (llvmpipe).
        Raylib.SetTraceLogLevel(TraceLogLevel.Info);
        Raylib.InitWindow(RenderProfileScreenWidth, RenderProfileScreenHeight, "Technolize Render Profile");
        Raylib.SetTargetFPS(0); // uncapped: measure the true per-frame cost, not the vsync/target cap.

        TickableWorld world = CreateRenderProfileWorld();
        using WorldShaderRenderer renderer = new(world, RenderProfileScreenWidth, RenderProfileScreenHeight);

        Console.WriteLine("=== Technolize Live Render Profile ===");
        Console.WriteLine($"Screen: {RenderProfileScreenWidth}x{RenderProfileScreenHeight}");
        Console.WriteLine($"Scene: water-filled {2 * RenderProfileSceneRadius}x{2 * RenderProfileSceneRadius} area with solids/air");
        Console.WriteLine($"Warmup frames: {warmupFrames}, measured frames: {frameCount}");

        List<double> frameMs = new(frameCount);
        Stopwatch frameTimer = new();

        for (int frame = 0; frame < warmupFrames + frameCount; frame++)
        {
            if (Raylib.WindowShouldClose())
            {
                break;
            }

            frameTimer.Restart();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            renderer.Draw();
            Raylib.EndDrawing();
            double elapsedMs = frameTimer.Elapsed.TotalMilliseconds;

            if (frame >= warmupFrames)
            {
                frameMs.Add(elapsedMs);
            }
        }

        Raylib.CloseWindow();

        if (frameMs.Count == 0)
        {
            Console.WriteLine("No frames were measured (window closed early).");
            return;
        }

        frameMs.Sort();
        double mean = frameMs.Average();
        double min = frameMs[0];
        double max = frameMs[^1];
        double p50 = Percentile(frameMs, 0.50);
        double p95 = Percentile(frameMs, 0.95);
        double p99 = Percentile(frameMs, 0.99);

        Console.WriteLine();
        Console.WriteLine("=== Results (per frame, lower is better) ===");
        Console.WriteLine($"FramesMeasured={frameMs.Count}");
        Console.WriteLine($"MeanMs={mean:0.000}");
        Console.WriteLine($"MinMs={min:0.000}");
        Console.WriteLine($"P50Ms={p50:0.000}");
        Console.WriteLine($"P95Ms={p95:0.000}");
        Console.WriteLine($"P99Ms={p99:0.000}");
        Console.WriteLine($"MaxMs={max:0.000}");
        Console.WriteLine($"MeanFps={1000.0 / mean:0.0}");
    }

    // A fixed time value for the shader's animated water/sun so snapshots are deterministic. Chosen
    // to land mid-animation so waves/sway are visibly exercised rather than at a trivial t=0 state.
    private const float SnapshotFixedTime = 7.5f;

    /// <summary>
    /// Renders the fixed scene deterministically (fixed shader time, no debug overlay) and compares
    /// the captured frame to a committed known-good baseline image, pixel for pixel. The first run
    /// (or with --update) writes the baseline. Also reports per-frame timing so an optimization can be
    /// judged on BOTH a pixel-perfect match and a speed improvement against the same scene.
    /// </summary>
    private static unsafe void RunRenderSnapshot(string[] args)
    {
        int frameCount = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 200;
        bool update = args.Any(a => string.Equals(a, "--update", StringComparison.OrdinalIgnoreCase));
        const int warmupFrames = 20;

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(RenderProfileScreenWidth, RenderProfileScreenHeight, "Technolize Render Snapshot");
        Raylib.SetTargetFPS(0);

        TickableWorld world = CreateRenderProfileWorld();
        using WorldShaderRenderer renderer = new(world, RenderProfileScreenWidth, RenderProfileScreenHeight)
        {
            FixedTime = SnapshotFixedTime,
            ShowDebugOverlay = false,
        };

        string snapshotDir = Path.Combine(FindRepoRoot(), "render-snapshots");
        Directory.CreateDirectory(snapshotDir);
        string baselinePath = Path.Combine(snapshotDir, "baseline.png");
        string latestPath = Path.Combine(snapshotDir, "latest.png");
        string diffPath = Path.Combine(snapshotDir, "diff.png");

        Console.WriteLine("=== Technolize Render Snapshot ===");
        Console.WriteLine($"Screen: {RenderProfileScreenWidth}x{RenderProfileScreenHeight}, FixedTime={SnapshotFixedTime}");
        Console.WriteLine($"Baseline: {baselinePath}");

        // --- Timing loop: render to screen, same path/cost as profile-render. ---
        List<double> frameMs = new(frameCount);
        Stopwatch frameTimer = new();
        for (int frame = 0; frame < warmupFrames + frameCount; frame++)
        {
            if (Raylib.WindowShouldClose())
            {
                break;
            }

            frameTimer.Restart();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            renderer.Draw();
            Raylib.EndDrawing();
            double ms = frameTimer.Elapsed.TotalMilliseconds;
            if (frame >= warmupFrames)
            {
                frameMs.Add(ms);
            }
        }

        // --- Capture one deterministic frame into an offscreen render texture. ---
        RenderTexture2D target = Raylib.LoadRenderTexture(RenderProfileScreenWidth, RenderProfileScreenHeight);
        Raylib.BeginDrawing();
        Raylib.BeginTextureMode(target);
        renderer.Draw();
        Raylib.EndTextureMode();
        Raylib.EndDrawing();

        Image captured = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref captured); // render textures are stored bottom-up
        Raylib.ExportImage(captured, latestPath);

        string result;
        long mismatchedPixels = 0;
        long totalPixels = (long)RenderProfileScreenWidth * RenderProfileScreenHeight;
        int maxChannelDiff = 0;

        if (update || !File.Exists(baselinePath))
        {
            Raylib.ExportImage(captured, baselinePath);
            result = update ? "UPDATED" : "CREATED";
        }
        else
        {
            Image baseline = Raylib.LoadImage(baselinePath);
            if (baseline.Width != captured.Width || baseline.Height != captured.Height)
            {
                result = "SIZE_MISMATCH";
            }
            else
            {
                Color* cap = Raylib.LoadImageColors(captured);
                Color* bas = Raylib.LoadImageColors(baseline);
                Image diff = Raylib.GenImageColor(captured.Width, captured.Height, Color.Black);

                for (long i = 0; i < totalPixels; i++)
                {
                    int dr = Math.Abs(cap[i].R - bas[i].R);
                    int dg = Math.Abs(cap[i].G - bas[i].G);
                    int db = Math.Abs(cap[i].B - bas[i].B);
                    int da = Math.Abs(cap[i].A - bas[i].A);
                    int d = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                    if (d != 0)
                    {
                        mismatchedPixels++;
                        if (d > maxChannelDiff) maxChannelDiff = d;
                        int px = (int)(i % captured.Width);
                        int py = (int)(i / captured.Width);
                        Raylib.ImageDrawPixel(ref diff, px, py, new Color(255, 0, 0, 255));
                    }
                }

                Raylib.UnloadImageColors(cap);
                Raylib.UnloadImageColors(bas);
                if (mismatchedPixels > 0)
                {
                    Raylib.ExportImage(diff, diffPath);
                }
                Raylib.UnloadImage(diff);

                result = mismatchedPixels == 0 ? "MATCH" : "MISMATCH";
            }

            Raylib.UnloadImage(baseline);
        }

        Raylib.UnloadImage(captured);
        Raylib.UnloadRenderTexture(target);
        Raylib.CloseWindow();

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
