using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Numerics;
using Technolize.Test.Benchmarks;
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
            Console.WriteLine($"AverageActionPreparationMs={timingTotals.GetAverage(t => t.ActionPreparationMs):0.00}");
            Console.WriteLine($"AverageActionExecutionMs={timingTotals.GetAverage(t => t.ActionExecutionMs):0.00}");
            Console.WriteLine($"AverageWorkerRegionPaddingMs={timingTotals.GetAverage(t => t.WorkerRegionPaddingMs):0.00}");
            Console.WriteLine($"AverageWorkerSignatureMs={timingTotals.GetAverage(t => t.WorkerSignatureComputationMs):0.00}");
            Console.WriteLine($"AverageWorkerRuleMatchingMs={timingTotals.GetAverage(t => t.WorkerRuleMatchingMs):0.00}");
            Console.WriteLine($"AverageWorkerActionMergeMs={timingTotals.GetAverage(t => t.WorkerActionMergeMs):0.00}");
            Console.WriteLine($"AverageWorkerAccumulatedMs={timingTotals.GetAverage(t => t.WorkerAccumulatedMs):0.00}");
            Console.WriteLine($"AverageEstimatedParallelism={timingTotals.GetAverage(t => t.EstimatedParallelism):0.00}");
            Console.WriteLine($"AverageActionCount={timingTotals.GetAverage(t => t.ActionCount):0.00}");
        }
        else
        {
            Console.WriteLine("Technolize.Test - Use 'benchmark' argument to run performance benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- profile-waterfall [tickCount]");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*QuickSimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SignatureProcessor*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*WorldRenderer*\"");
        }
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
                world.SetBlock(new Vector2(x, worldY), Blocks.Water.id);
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