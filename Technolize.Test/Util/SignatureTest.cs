using System.Diagnostics;
using System.Numerics;
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
        ulong signature1 = SignatureProcessor.ComputeSignature(source);
        ulong signature2 = SignatureProcessor.ComputeSignature(source);

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
        ulong signature1 = SignatureProcessor.ComputeSignature( source, 12345);
        ulong signature2 = SignatureProcessor.ComputeSignature( source, 54321);

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
        ulong signature1 = SignatureProcessor.ComputeSignature(source1);
        ulong signature2 = SignatureProcessor.ComputeSignature(source2);

        // Assert
        Assert.That(signature1, Is.Not.EqualTo(signature2), "Signatures should differ with a single bit change in source.");
    }

    [Test]
    public void ComputeSignature_WithRandomInputs_ReturnsDifferentSignatures()
    {
        // Create 100 random 3x3 matrices
        Random random = new ();

        // Create a second source with a minimal change (9 -> 8, which is a single bit flip)
        uint[,,] sources = new uint[100, 3, 3];
        for (int i = 0; i < 100; i++) {
            for (int y = 0; y < 3; y++) {
                for (int x = 0; x < 3; x++) {
                    sources[i, y, x] = (uint)random.Next(0, 256); // Random value between 0 and 255
                }
            }
        }

        ulong[] signatures = new ulong[100];

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
    public void ComputeSignature_WithDifferingLengths_ReturnsSameSignatures() {

        // ensure that not matter how wide the source array is, the signature computation is consistent
        // this is mostly testing the vector vs scalar processing logic

        // use a 7x3 matrix, and a vecX3 matrix to compare

        uint[,] source1 = CreateNx3Matrix(7);
        uint[,] source2 = CreateNx3Matrix(Vector<uint>.Count + 2 /* 2 is padding */);

        ulong[,] signatures1 = SignatureProcessor.ComputeSignatures(source1);
        ulong[,] signatures2 = SignatureProcessor.ComputeSignatures(source2);

        // Assert that the signatures are the same for both matrices
        int width = Math.Min(signatures1.GetLength(0), signatures2.GetLength(0));
        int height = Math.Min(signatures1.GetLength(1) - 2, signatures2.GetLength(1));
        for (int x = 0; x < width; x++) {
            for (int y = 0; y < height; y++) {
                Assert.That(signatures1[x, y], Is.EqualTo(signatures2[x, y]),
                    $"Signatures at ({x}, {y}) should be equal for both matrices.");
            }
        }
    }

    private uint[,] CreateNx3Matrix(int n)
    {
        uint[,] matrix = new uint[n, 3];
        for (int x = 0; x < n; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                matrix[x, y] = (uint)(x + y); // Simple pattern for testing
            }
        }
        return matrix;
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
        uint maxBlockId = Blocks.AllBlocks().Max(b => b.id);
        int totalCombinations = (int)Math.Pow(maxBlockId + 1, 9); // 3x3 matrix with maxBlockId + 1 options per cell
        Console.WriteLine($"Total block combinations: {totalCombinations}");

        // for each possible block matrix, test unique signature
        ISet<ulong> uniqueSignatures = new HashSet<ulong>();

        foreach (uint[,] matrix in GenerateBlockMatrices(new uint[3 * 3], maxBlockId)) {
            ulong signature = SignatureProcessor.ComputeSignature(matrix, seed);

            if (!uniqueSignatures.Add(signature)) {
                return false;
            }
        }
        return true;
    }

    private IEnumerable<uint[,]> GenerateBlockMatrices(uint[] matrix, uint maxBlockId, int filled = 0)
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

    [Test]
    public void RandomSignatureTest()
    {
        // Test the random signature generation


        for (int i = 4; i < 100; i++) {
            uint[,] data = CreateRandom(i, i, i);
            ulong[,] signatures = SignatureProcessor.ComputeSignatures(data);

            // Check that the center pixel signature is not zero
            ulong centerSignature = signatures[i / 2, i / 2];
            Assert.That(centerSignature, Is.Not.EqualTo(0), $"Center signature for seed {i} should not be zero.");
        }
    }

    private uint[,] CreateRandom(int seed, int width, int height) {
        uint[,] matrix = new uint[height, width];
        Random random = new (seed);
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                matrix[y, x] = (uint)random.NextInt64(0, uint.MaxValue);
            }
        }
        return matrix;
    }
}
