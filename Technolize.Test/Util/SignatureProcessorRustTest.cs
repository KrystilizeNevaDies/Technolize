using Technolize.Utils;

namespace Technolize.Test.Util;

[TestFixture]
public class SignatureProcessorRustTest
{
    [Test]
    public void ComputeSignature_WithIdenticalInputs_ReturnsSameSignature()
    {
        // Arrange
        uint[,] source = new uint[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 9 }
        };

        // Act
        ulong signature1 = SignatureProcessorRust.ComputeSignature(source);
        ulong signature2 = SignatureProcessorRust.ComputeSignature(source);

        // Assert
        Assert.That(signature1, Is.EqualTo(signature2), "Rust signatures should be equal for identical inputs.");
    }

    [Test]
    public void ComputeSignature_WithDifferentSeeds_ReturnsDifferentSignatures()
    {
        // Arrange
        uint[,] source = new uint[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 9 }
        };

        // Act
        ulong signature1 = SignatureProcessorRust.ComputeSignature(source, 12345);
        ulong signature2 = SignatureProcessorRust.ComputeSignature(source, 54321);

        // Assert
        Assert.That(signature1, Is.Not.EqualTo(signature2), "Rust signatures should differ with different seeds.");
    }

    [Test]
    public void ComputeSignature_MatchesOriginalImplementation()
    {
        // Arrange
        uint[,] source = new uint[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 9 }
        };

        // Act
        ulong originalSignature = SignatureProcessor.ComputeSignature(source);
        ulong rustSignature = SignatureProcessorRust.ComputeSignature(source);

        // Assert
        Assert.That(rustSignature, Is.EqualTo(originalSignature), 
            "Rust implementation should match original C# implementation.");
    }

    [Test]
    public void ComputeSignatures_MatchesOriginalImplementation()
    {
        // Arrange
        uint[,] source = new uint[,]
        {
            { 1, 2, 3, 4 },
            { 5, 6, 7, 8 },
            { 9, 10, 11, 12 },
            { 13, 14, 15, 16 }
        };

        // Act
        ulong[,] originalSignatures = SignatureProcessor.ComputeSignatures(source);
        ulong[,] rustSignatures = SignatureProcessorRust.ComputeSignatures(source);

        // Assert
        Assert.That(rustSignatures.GetLength(0), Is.EqualTo(originalSignatures.GetLength(0)));
        Assert.That(rustSignatures.GetLength(1), Is.EqualTo(originalSignatures.GetLength(1)));

        for (int y = 0; y < originalSignatures.GetLength(0); y++)
        {
            for (int x = 0; x < originalSignatures.GetLength(1); x++)
            {
                Assert.That(rustSignatures[y, x], Is.EqualTo(originalSignatures[y, x]),
                    $"Rust signature at ({y}, {x}) should match original implementation.");
            }
        }
    }

    [Test]
    public void ComputeSignature_WithSingleBitChangeInSource_ReturnsDifferentSignatures()
    {
        // Arrange
        uint[,] source1 = new uint[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 9 }
        };

        uint[,] source2 = new uint[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 8 }
        };

        // Act
        ulong signature1 = SignatureProcessorRust.ComputeSignature(source1);
        ulong signature2 = SignatureProcessorRust.ComputeSignature(source2);

        // Assert
        Assert.That(signature1, Is.Not.EqualTo(signature2), 
            "Rust signatures should differ with a single bit change in source.");
    }
}