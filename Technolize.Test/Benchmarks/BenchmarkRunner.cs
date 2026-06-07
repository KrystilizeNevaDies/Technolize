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

            if (filter.Contains("WorldRenderer") || filter == "*")
            {
                Console.WriteLine("\nStarting WorldRenderer graphics benchmarks (offscreen GPU)...");
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
        else if (args.Length > 0 && args[0] == "render-snapshot")
        {
            RunRenderSnapshot(args);
        }
        else if (args.Length > 0 && args[0] == "render-sun-sweep")
        {
            RunSunRaySweep(args);
        }
        else if (args.Length > 0 && args[0] == "render-scale-scan")
        {
            RunScaleScan(args);
        }
        else if (args.Length > 0 && args[0] == "render-pixel-sweep")
        {
            RunPixelSweep(args);
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
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- render-sun-sweep [maxRays]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- render-scale-scan [sunRays]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- render-pixel-sweep");
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
    /// Sweeps the sparse temporal <see cref="WorldShaderRenderer.PixelUpdateFraction"/> at full output
    /// resolution and full ray count, reporting the steady-state GPU time / FPS and the converged pixel
    /// difference from the fraction=1.0 (relight-everything) reference. Because each refreshed pixel
    /// gets full-ray lighting, a static view converges to the exact reference, so the diff should stay
    /// ~0 while GPU time drops with the fraction. Saves a converged image per fraction. Diagnostic only.
    /// </summary>
    private static void RunPixelSweep(string[] args)
    {
        const int timedFrames = 20;

        TickableWorld world = CreateRenderProfileWorld();
        WorldRenderFrame frame = WorldRenderFrameBuilder.FromWorld(world);
        (Vector2 regionStart, Vector2 regionEnd) = ComputeWorldRegionBounds(frame);
        Camera2D camera = Camera2D.Centered(RenderProfileScreenWidth, RenderProfileScreenHeight);

        double[] fractions = { 1.0, 0.5, 0.25, 0.125, 0.0625, 0.03125, 0.02, 0.01 };

        string sweepDir = Path.Combine(FindRepoRoot(), "render-snapshots", "pixel-sweep");
        Directory.CreateDirectory(sweepDir);

        Console.WriteLine("=== Technolize Pixel-Update-Fraction Sweep ===");
        Console.WriteLine($"Output: {RenderProfileScreenWidth}x{RenderProfileScreenHeight}, FixedTime={SnapshotFixedTime}, sunRays={WorldLighting.Default.SunRayCount}");
        Console.WriteLine($"Images: {sweepDir}");

        using GlContext context = GlContext.CreateOffscreen(RenderProfileScreenWidth, RenderProfileScreenHeight);
        using WorldShaderRenderer renderer = new(context.Gl);
        renderer.EnableGpuTiming = true;
        Console.WriteLine($"GL_RENDERER: {context.Renderer}");
        Console.WriteLine();

        long totalPixels = (long)RenderProfileScreenWidth * RenderProfileScreenHeight;

        (byte[] pixels, double gpuMs) RenderAt(double fraction)
        {
            renderer.PixelUpdateFraction = fraction;
            // Converge: render at least one full refresh sweep before timing the steady state.
            int warmup = (int)Math.Ceiling(1.0 / fraction) + 4;
            byte[] captured = Array.Empty<byte>();
            for (int f = 0; f < warmup; f++)
            {
                captured = renderer.RenderToScreen(
                    frame, regionStart, regionEnd, WorldLighting.Default, SnapshotFixedTime,
                    camera, RenderProfileScreenWidth, RenderProfileScreenHeight, drawGrid: true);
            }

            List<double> gpu = new(timedFrames);
            for (int f = 0; f < timedFrames; f++)
            {
                captured = renderer.RenderToScreen(
                    frame, regionStart, regionEnd, WorldLighting.Default, SnapshotFixedTime,
                    camera, RenderProfileScreenWidth, RenderProfileScreenHeight, drawGrid: true);
                if (renderer.LastGpuDrawMs is double ms)
                {
                    gpu.Add(ms);
                }
            }

            PngWriter.WriteFile(Path.Combine(sweepDir, $"fraction-{fraction:0.0000}.png"), captured, RenderProfileScreenWidth, RenderProfileScreenHeight);
            return (captured, gpu.Count > 0 ? gpu.Average() : 0.0);
        }

        (byte[] referencePixels, double referenceGpuMs) = RenderAt(1.0);

        Console.WriteLine("Fraction | RefreshFrames | GpuMs   | Est.FPS | MismatchPct | MaxChanDiff | MeanChanDiff");
        Console.WriteLine("---------+---------------+---------+---------+-------------+-------------+-------------");

        foreach (double fraction in fractions)
        {
            (byte[] pixels, double gpuMs) = fraction >= 1.0 ? (referencePixels, referenceGpuMs) : RenderAt(fraction);

            long mismatched = 0;
            long channelDiffSum = 0;
            int maxChannelDiff = 0;
            for (long i = 0; i < totalPixels; i++)
            {
                long b = i * 4;
                int dr = Math.Abs(pixels[b + 0] - referencePixels[b + 0]);
                int dg = Math.Abs(pixels[b + 1] - referencePixels[b + 1]);
                int db = Math.Abs(pixels[b + 2] - referencePixels[b + 2]);
                int d = Math.Max(dr, Math.Max(dg, db));
                if (d != 0)
                {
                    mismatched++;
                    if (d > maxChannelDiff) maxChannelDiff = d;
                }

                channelDiffSum += dr + dg + db;
            }

            double estFps = gpuMs > 0 ? 1000.0 / gpuMs : 0.0;
            double mismatchPct = 100.0 * mismatched / totalPixels;
            double meanChannelDiff = channelDiffSum / (double)(totalPixels * 3);
            int refreshFrames = (int)Math.Ceiling(1.0 / fraction);
            Console.WriteLine(
                $"{fraction,8:0.0000} | {refreshFrames,13} | {gpuMs,7:0.00} | {estFps,7:0.0} | {mismatchPct,10:0.000}% | {maxChannelDiff,11} | {meanChannelDiff,12:0.0000}");
        }

        Console.WriteLine();
        Console.WriteLine("GpuMs = steady-state both passes (no readback). Converged diff vs fraction=1.0 should be ~0.");
        Console.WriteLine($"Images saved in {sweepDir}");
    }


    /// <summary>
    /// Sweeps the decoupled lighting-resolution scale (the two-pass low-res lighting path) at a fixed
    /// full output resolution, reporting GPU time / FPS and the pixel difference from the exact
    /// single-pass (full-resolution lighting) reference. Saves an image per scale so the perf/quality
    /// tradeoff toward a target frame rate can be chosen by eye. Diagnostic only.
    /// </summary>
    private static void RunScaleScan(string[] args)
    {
        int sunRays = args.Length > 1 && int.TryParse(args[1], out int parsedRays)
            ? Math.Clamp(parsedRays, 1, 64)
            : WorldLighting.Default.SunRayCount;
        WorldLighting lighting = WorldLighting.Default with { SunRayCount = sunRays };

        const int warmupFrames = 4;
        const int timedFrames = 10;

        TickableWorld world = CreateRenderProfileWorld();
        WorldRenderFrame frame = WorldRenderFrameBuilder.FromWorld(world);
        (Vector2 regionStart, Vector2 regionEnd) = ComputeWorldRegionBounds(frame);
        Camera2D camera = Camera2D.Centered(RenderProfileScreenWidth, RenderProfileScreenHeight);

        // 1.0 = exact single-pass reference; lower = two-pass lighting at that fraction of the resolution.
        double[] scales = { 1.0, 0.5, 0.35, 0.25, 0.2, 0.167, 0.125, 0.0833 };

        string scanDir = Path.Combine(FindRepoRoot(), "render-snapshots", "lighting-scan");
        Directory.CreateDirectory(scanDir);

        Console.WriteLine("=== Technolize Lighting-Resolution Scan ===");
        Console.WriteLine($"Output: {RenderProfileScreenWidth}x{RenderProfileScreenHeight}, FixedTime={SnapshotFixedTime}, sunRays={sunRays}");
        Console.WriteLine($"Images: {scanDir}");

        using GlContext context = GlContext.CreateOffscreen(RenderProfileScreenWidth, RenderProfileScreenHeight);
        using WorldShaderRenderer renderer = new(context.Gl);
        renderer.EnableGpuTiming = true;
        // The sweep varies SunRayCount, which only affects the non-temporal single-frame lighting path.
        renderer.EnableTemporalAccumulation = false;
        Console.WriteLine($"GL_RENDERER: {context.Renderer}");
        Console.WriteLine();

        long totalPixels = (long)RenderProfileScreenWidth * RenderProfileScreenHeight;

        (byte[] pixels, double gpuMs) RenderAt(double scale)
        {
            renderer.LightingResolutionScale = scale;
            byte[] captured = Array.Empty<byte>();
            List<double> gpu = new(timedFrames);
            for (int f = 0; f < warmupFrames + timedFrames; f++)
            {
                captured = renderer.RenderToScreen(
                    frame, regionStart, regionEnd, lighting, SnapshotFixedTime,
                    camera, RenderProfileScreenWidth, RenderProfileScreenHeight, drawGrid: true);
                if (f >= warmupFrames && renderer.LastGpuDrawMs is double ms)
                {
                    gpu.Add(ms);
                }
            }

            int lightW = (int)Math.Round(RenderProfileScreenWidth * scale);
            string tag = scale >= 1.0 ? "full" : $"{lightW}px";
            PngWriter.WriteFile(Path.Combine(scanDir, $"scale-{scale:0.0000}-{tag}.png"), captured, RenderProfileScreenWidth, RenderProfileScreenHeight);
            return (captured, gpu.Count > 0 ? gpu.Average() : 0.0);
        }

        // Single-pass full-resolution lighting is the visual reference.
        (byte[] referencePixels, double referenceGpuMs) = RenderAt(1.0);

        Console.WriteLine("Scale  | LightRes   | GpuMs  | Est.FPS | MismatchPct | MaxChanDiff | MeanChanDiff");
        Console.WriteLine("-------+------------+--------+---------+-------------+-------------+-------------");

        foreach (double scale in scales)
        {
            (byte[] pixels, double gpuMs) = scale >= 1.0 ? (referencePixels, referenceGpuMs) : RenderAt(scale);

            long mismatched = 0;
            long channelDiffSum = 0;
            int maxChannelDiff = 0;
            for (long i = 0; i < totalPixels; i++)
            {
                long b = i * 4;
                int dr = Math.Abs(pixels[b + 0] - referencePixels[b + 0]);
                int dg = Math.Abs(pixels[b + 1] - referencePixels[b + 1]);
                int db = Math.Abs(pixels[b + 2] - referencePixels[b + 2]);
                int d = Math.Max(dr, Math.Max(dg, db));
                if (d != 0)
                {
                    mismatched++;
                    if (d > maxChannelDiff) maxChannelDiff = d;
                }

                channelDiffSum += dr + dg + db;
            }

            int lightW = (int)Math.Round(RenderProfileScreenWidth * scale);
            int lightH = (int)Math.Round(RenderProfileScreenHeight * scale);
            double estFps = gpuMs > 0 ? 1000.0 / gpuMs : 0.0;
            double mismatchPct = 100.0 * mismatched / totalPixels;
            double meanChannelDiff = channelDiffSum / (double)(totalPixels * 3);
            string res = scale >= 1.0 ? "full (ref)" : $"{lightW}x{lightH}";
            Console.WriteLine(
                $"{scale,6:0.0000} | {res,-10} | {gpuMs,6:0.00} | {estFps,7:0.0} | {mismatchPct,10:0.000}% | {maxChannelDiff,11} | {meanChannelDiff,12:0.0000}");
        }

        Console.WriteLine();
        Console.WriteLine("GpuMs = both passes (no readback). Pick the smallest scale whose diff is acceptable for >=240 FPS.");
        Console.WriteLine($"Images saved in {scanDir}");
    }


    /// <summary>
    /// Renders the deterministic profile scene at every sun ray count from 1 to <c>maxRays</c> (default
    /// 64), saves each frame as <c>render-snapshots/sun-sweep/rays-NN.png</c>, and reports each one's
    /// difference from the highest-quality (<c>maxRays</c>) render plus its GPU shader time. This lets
    /// you pick the smallest ray count whose output is visually indistinguishable from the maximum.
    /// </summary>
    private static void RunSunRaySweep(string[] args)
    {
        int maxRays = args.Length > 1 && int.TryParse(args[1], out int parsed) ? Math.Clamp(parsed, 1, 64) : 64;
        const int warmupFrames = 4;
        const int timedFrames = 8;

        TickableWorld world = CreateRenderProfileWorld();
        WorldRenderFrame frame = WorldRenderFrameBuilder.FromWorld(world);
        (Vector2 regionStart, Vector2 regionEnd) = ComputeWorldRegionBounds(frame);
        Camera2D camera = Camera2D.Centered(RenderProfileScreenWidth, RenderProfileScreenHeight);

        string sweepDir = Path.Combine(FindRepoRoot(), "render-snapshots", "sun-sweep");
        Directory.CreateDirectory(sweepDir);

        Console.WriteLine("=== Technolize Sun Ray Sweep ===");
        Console.WriteLine($"Screen: {RenderProfileScreenWidth}x{RenderProfileScreenHeight}, FixedTime={SnapshotFixedTime}, maxRays={maxRays}");
        Console.WriteLine($"Output: {sweepDir}");

        using GlContext context = GlContext.CreateOffscreen(RenderProfileScreenWidth, RenderProfileScreenHeight);
        using WorldShaderRenderer renderer = new(context.Gl);
        renderer.EnableGpuTiming = true;
        // The sweep varies SunRayCount, which only affects the non-temporal single-frame lighting path.
        renderer.EnableTemporalAccumulation = false;

        Console.WriteLine($"GL_RENDERER: {context.Renderer}");
        Console.WriteLine();

        long totalPixels = (long)RenderProfileScreenWidth * RenderProfileScreenHeight;

        // Renders one ray count, saves its PNG, and returns the captured pixels + mean GPU time.
        (byte[] pixels, double gpuMs) RenderAt(int rays)
        {
            WorldLighting lighting = WorldLighting.Default with { SunRayCount = rays };
            byte[] captured = Array.Empty<byte>();
            List<double> gpu = new(timedFrames);
            for (int f = 0; f < warmupFrames + timedFrames; f++)
            {
                captured = renderer.RenderToScreen(
                    frame, regionStart, regionEnd, lighting, SnapshotFixedTime,
                    camera, RenderProfileScreenWidth, RenderProfileScreenHeight, drawGrid: true);
                if (f >= warmupFrames && renderer.LastGpuDrawMs is double ms)
                {
                    gpu.Add(ms);
                }
            }

            string path = Path.Combine(sweepDir, $"rays-{rays:00}.png");
            PngWriter.WriteFile(path, captured, RenderProfileScreenWidth, RenderProfileScreenHeight);
            return (captured, gpu.Count > 0 ? gpu.Average() : 0.0);
        }

        // The max-ray render is the visual reference every lower count is compared against.
        (byte[] referencePixels, double referenceGpuMs) = RenderAt(maxRays);

        Console.WriteLine("Rays | GpuMs   | MismatchedPx | MismatchPct | MaxChanDiff | MeanChanDiff | vs maxRays");
        Console.WriteLine("-----+---------+--------------+-------------+-------------+--------------+-----------");

        for (int rays = 1; rays <= maxRays; rays++)
        {
            (byte[] pixels, double gpuMs) = rays == maxRays ? (referencePixels, referenceGpuMs) : RenderAt(rays);

            long mismatched = 0;
            long channelDiffSum = 0;
            int maxChannelDiff = 0;
            for (long i = 0; i < totalPixels; i++)
            {
                long b = i * 4;
                int dr = Math.Abs(pixels[b + 0] - referencePixels[b + 0]);
                int dg = Math.Abs(pixels[b + 1] - referencePixels[b + 1]);
                int db = Math.Abs(pixels[b + 2] - referencePixels[b + 2]);
                int d = Math.Max(dr, Math.Max(dg, db));
                if (d != 0)
                {
                    mismatched++;
                    if (d > maxChannelDiff) maxChannelDiff = d;
                }

                channelDiffSum += dr + dg + db;
            }

            double mismatchPct = 100.0 * mismatched / totalPixels;
            double meanChannelDiff = channelDiffSum / (double)(totalPixels * 3);
            string marker = rays == maxRays ? "(reference)" : "";
            Console.WriteLine(
                $"{rays,4} | {gpuMs,7:0.00} | {mismatched,12} | {mismatchPct,10:0.000}% | {maxChannelDiff,11} | {meanChannelDiff,12:0.0000} | {marker}");
        }

        Console.WriteLine();
        Console.WriteLine("Pick the smallest ray count whose MaxChanDiff/MeanChanDiff is acceptably small.");
        Console.WriteLine($"Images saved as rays-01.png .. rays-{maxRays:00}.png in {sweepDir}");
    }


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

        // Optional --sun-rays N override (diagnostic): measures the lighting perf/quality tradeoff
        // against the committed baseline without changing the default look.
        WorldLighting lighting = WorldLighting.Default;
        int sunRaysArgIndex = Array.FindIndex(args, a => string.Equals(a, "--sun-rays", StringComparison.OrdinalIgnoreCase));
        if (sunRaysArgIndex >= 0 && sunRaysArgIndex + 1 < args.Length && int.TryParse(args[sunRaysArgIndex + 1], out int sunRays))
        {
            lighting = lighting with { SunRayCount = sunRays };
        }

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
        renderer.EnableGpuTiming = true;

        Console.WriteLine($"GL_RENDERER: {context.Renderer}");
        Console.WriteLine($"GL_VENDOR:   {context.Vendor}");
        Console.WriteLine($"GL_VERSION:  {context.GlVersion}");

        // Timing loop: render the same path repeatedly so the per-frame GPU cost is reported.
        List<double> frameMs = new(frameCount);
        List<double> gpuDrawMs = new(frameCount);
        Stopwatch frameTimer = new();
        byte[] captured = Array.Empty<byte>();
        for (int frame_i = 0; frame_i < warmupFrames + frameCount; frame_i++)
        {
            frameTimer.Restart();
            captured = renderer.RenderToScreen(
                frame, regionStart, regionEnd, lighting, SnapshotFixedTime,
                camera, RenderProfileScreenWidth, RenderProfileScreenHeight, drawGrid: true);
            double ms = frameTimer.Elapsed.TotalMilliseconds;
            if (frame_i >= warmupFrames)
            {
                frameMs.Add(ms);
                if (renderer.LastGpuDrawMs is double gpuMs)
                {
                    gpuDrawMs.Add(gpuMs);
                }
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
        gpuDrawMs.Sort();
        double gpuMean = gpuDrawMs.Count > 0 ? gpuDrawMs.Average() : 0.0;
        double gpuP95 = gpuDrawMs.Count > 0 ? Percentile(gpuDrawMs, 0.95) : 0.0;

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
        Console.WriteLine($"GpuDrawMeanMs={gpuMean:0.000}  (shader execution only, no readback)");
        Console.WriteLine($"GpuDrawP95Ms={gpuP95:0.000}");
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
