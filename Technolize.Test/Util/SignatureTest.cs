using System.Diagnostics;
using Technolize.Utils;
using Technolize.World.Block;
namespace Technolize.Test.Util;

[TestFixture]
public class SignatureTest {

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
        uint signature1 = SignatureProcessor.ComputeSignature(source);
        uint signature2 = SignatureProcessor.ComputeSignature(source);

        // Assert
        Assert.That(signature1, Is.EqualTo(signature2), "Signatures should be equal for identical inputs.");
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
        uint signature1 = SignatureProcessor.ComputeSignature( source, 12345);
        uint signature2 = SignatureProcessor.ComputeSignature( source, 54321);

        // Assert
        Assert.That(signature1, Is.Not.EqualTo(signature2), "Signatures should differ with different seeds.");
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

        // Create a second source with a minimal change (9 -> 8, which is a single bit flip)
        uint[,] source2 = new uint[,]
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 8 }
        };

        // Act
        uint signature1 = SignatureProcessor.ComputeSignature(source1);
        uint signature2 = SignatureProcessor.ComputeSignature(source2);

        // Assert
        Assert.That(signature1, Is.Not.EqualTo(signature2), "Signatures should differ with a single bit change in source.");
    }

    [Test]
    public void ComputeSignature_WithRandomInputs_ReturnsDifferentSignatures()
    {
        // Create 100 random 3x3 matrices
        Random random = new Random();

        // Create a second source with a minimal change (9 -> 8, which is a single bit flip)
        uint[,,] sources = new uint[100, 3, 3];
        for (int i = 0; i < 100; i++) {
            for (int y = 0; y < 3; y++) {
                for (int x = 0; x < 3; x++) {
                    sources[i, y, x] = (uint)random.Next(0, 256); // Random value between 0 and 255
                }
            }
        }

        uint[] signatures = new uint[100];

        // Act
        for (int i = 0; i < 100; i++) {
            uint[,] source = new uint[3, 3];
            for (int y = 0; y < 3; y++) {
                for (int x = 0; x < 3; x++) {
                    source[y, x] = sources[i, y, x];
                }
            }
            signatures[i] = SignatureProcessor.ComputeSignature(source);
        }

        // Assert
        for (int i = 0; i < signatures.Length - 1; i++) {
            for (int j = i + 1; j < signatures.Length; j++) {
                Assert.That(signatures[i], Is.Not.EqualTo(signatures[j]),
                    $"Signatures at index {i} and {j} should differ for random inputs.");
            }
        }
    }

    [Test]
    public void ComputeSignature_UsingAllBlocks_ReturnsDifferentSignatures()
    {
        Assert.That(TestUniqueSeed(0), Is.True, "All signatures should be unique for all possible block matrices with seed 0.");
        Assert.That(TestUniqueSeed(SignatureProcessor.DefaultSeed), Is.True, "All signatures should be unique for all possible block matrices with seed 123456789.");
        Assert.That(TestUniqueSeed(123456789), Is.True, "All signatures should be unique for all possible block matrices with seed 123456789.");
    }

    private bool TestUniqueSeed(ulong seed) {
        // Create 100 random 3x3 matrices
        int maxBlockId = Blocks.AllBlocks().Max(b => b.Id);
        int totalCombinations = (int)Math.Pow(maxBlockId + 1, 9); // 3x3 matrix with maxBlockId + 1 options per cell
        Console.WriteLine($"Total block combinations: {totalCombinations}");

        // for each possible block matrix, test unique signature
        ISet<uint> uniqueSignatures = new HashSet<uint>();

        foreach (uint[,] matrix in GenerateBlockMatrices(new uint[3 * 3], maxBlockId)) {
            uint signature = SignatureProcessor.ComputeSignature(matrix, seed);

            if (!uniqueSignatures.Add(signature)) {
                return false;
            }
        }
        return true;
    }

    private IEnumerable<uint[,]> GenerateBlockMatrices(uint[] matrix, int maxBlockId, int filled = 0)
    {
        if (filled == 9) {
            // create matrix
            uint[,] result = new uint[3, 3];
            for (int i = 0; i < 3; i++) {
                for (int j = 0; j < 3; j++) {
                    result[i, j] = matrix[i * 3 + j];
                }
            }
            yield return result;
            yield break;
        }

        for (uint blockId = 0; blockId <= maxBlockId; blockId++) {
            matrix[filled] = blockId;
            foreach (uint[,] nextMatrix in GenerateBlockMatrices(matrix, maxBlockId, filled + 1)) {
                yield return nextMatrix;
            }
        }
    }
}
