using System.Diagnostics;
using Technolize.Utils;

namespace Technolize.Test.Util;

[TestFixture]
public class SignaturePerformanceTest
{
    [Test]
    public void Performance_CompareRustVsOriginal()
    {
        // Create a larger test case for performance comparison
        const int size = 100;
        uint[,] largeSource = new uint[size, size];
        Random random = new(42); // Fixed seed for reproducible results
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                largeSource[y, x] = (uint)random.Next(0, 1000);
            }
        }

        // Warm up
        SignatureProcessor.ComputeSignatures(largeSource);
        SignatureProcessorRust.ComputeSignatures(largeSource);

        // Time the original vs Rust implementation
        Stopwatch sw = new();
        
        sw.Start();
        var rustResults = SignatureProcessorRust.ComputeSignatures(largeSource);
        sw.Stop();
        long rustTime = sw.ElapsedMilliseconds;

        sw.Restart();
        var originalResults = SignatureProcessor.ComputeSignatures(largeSource);
        sw.Stop();
        long originalTime = sw.ElapsedMilliseconds;

        // Verify results are identical
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Assert.That(rustResults[y, x], Is.EqualTo(originalResults[y, x]),
                    $"Results should be identical at position ({y}, {x})");
            }
        }

        Console.WriteLine($"Rust implementation: {rustTime}ms");
        Console.WriteLine($"Original (now Rust-backed): {originalTime}ms");
        Console.WriteLine($"Results are identical: {rustResults.Cast<ulong>().SequenceEqual(originalResults.Cast<ulong>())}");
        
        // Both should be fast since they both use Rust now
        Assert.That(rustTime, Is.LessThan(1000), "Rust implementation should be fast");
        Assert.That(originalTime, Is.LessThan(1000), "Original implementation (now Rust-backed) should be fast");
    }
}