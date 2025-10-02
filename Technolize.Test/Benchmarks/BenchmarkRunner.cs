using BenchmarkDotNet.Running;
using Technolize.Test.Benchmarks;

namespace Technolize.Test;

/// <summary>
/// Main entry point for Technolize.Test project.
/// Can run unit tests (default) or performance benchmarks.
/// </summary>
public class Program
{
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
        else
        {
            Console.WriteLine("Technolize.Test - Use 'benchmark' argument to run performance benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*QuickSimdVsScalar*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SignatureProcessor*\"");
            Console.WriteLine("  dotnet run --project Technolize.Test -c Release -- benchmark --filter \"*SimdVsScalar*\"");
        }
    }
}