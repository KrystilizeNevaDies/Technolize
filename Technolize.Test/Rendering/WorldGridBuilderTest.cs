using System.Numerics;
using Technolize.Rendering;

namespace Technolize.Test.Rendering;

/// <summary>
/// Pure-CPU tests for <see cref="WorldGridBuilder"/>, which generates the world grid overlay geometry
/// for the Silk.NET path. They validate the adaptive spacing and the emitted triangle vertices without
/// any GPU.
/// </summary>
[TestFixture]
public class WorldGridBuilderTest
{
    private const int FloatsPerVertex = 6;
    private const int VerticesPerLine = 6;

    [Test]
    public void ComputeGridSize_IsPowerOfTwo_AndGrowsWithArea()
    {
        int small = WorldGridBuilder.ComputeGridSize(new Vector2(0, 0), new Vector2(10, 10));
        int large = WorldGridBuilder.ComputeGridSize(new Vector2(0, 0), new Vector2(4000, 4000));

        Assert.Multiple(() =>
        {
            Assert.That(IsPowerOfTwo(small), Is.True, $"small grid size {small} must be a power of two");
            Assert.That(IsPowerOfTwo(large), Is.True, $"large grid size {large} must be a power of two");
            Assert.That(large, Is.GreaterThan(small), "a larger visible area must use a coarser grid");
        });
    }

    [Test]
    public void BuildGridVertices_EmitsWholeLineQuads()
    {
        // 0..32 blocks with grid size derived from the area; every emitted vertex group is a 6-vertex quad.
        float[] vertices = WorldGridBuilder.BuildGridVertices(new Vector2(0, 0), new Vector2(32, 32), 1.0f, out int vertexCount);

        Assert.Multiple(() =>
        {
            Assert.That(vertices, Has.Length.EqualTo(vertexCount * FloatsPerVertex));
            Assert.That(vertexCount % VerticesPerLine, Is.EqualTo(0), "vertex count must be whole quads");
            Assert.That(vertexCount, Is.GreaterThan(0), "a visible range must produce grid lines");
        });
    }

    [Test]
    public void BuildGridVertices_LineCount_MatchesGridDivisions()
    {
        Vector2 start = new(0, 0);
        Vector2 end = new(64, 64);
        int gridSize = WorldGridBuilder.ComputeGridSize(start, end);

        float[] _ = WorldGridBuilder.BuildGridVertices(start, end, 1.0f, out int vertexCount);

        // Count grid-aligned columns in [0, 64] and rows in [0, 64].
        int verticalLines = CountMultiples(0, 64, gridSize);
        int horizontalLines = CountMultiples(0, 64, gridSize);
        int expectedQuads = verticalLines + horizontalLines;

        Assert.That(vertexCount, Is.EqualTo(expectedQuads * VerticesPerLine));
    }

    [Test]
    public void BuildGridVertices_UsesGridColor_InAllVertices()
    {
        float[] vertices = WorldGridBuilder.BuildGridVertices(new Vector2(0, 0), new Vector2(32, 32), 1.0f, out int vertexCount);

        // GridColor is (255, 255, 255, 64) → (1, 1, 1, 0.2509...). Every vertex must carry it.
        for (int v = 0; v < vertexCount; v++)
        {
            int baseIndex = v * FloatsPerVertex;
            Assert.Multiple(() =>
            {
                Assert.That(vertices[baseIndex + 2], Is.EqualTo(1f).Within(1e-6), "r");
                Assert.That(vertices[baseIndex + 3], Is.EqualTo(1f).Within(1e-6), "g");
                Assert.That(vertices[baseIndex + 4], Is.EqualTo(1f).Within(1e-6), "b");
                Assert.That(vertices[baseIndex + 5], Is.EqualTo(64f / 255f).Within(1e-6), "a");
            });
        }
    }

    [Test]
    public void BuildGridVertices_HigherZoom_ProducesThinnerLinesInWorldSpace()
    {
        // Line half-width is 1/zoom in world units; a vertical line's quad spans 2*halfWidth in X.
        float[] zoom1 = WorldGridBuilder.BuildGridVertices(new Vector2(0, 0), new Vector2(32, 32), 1.0f, out _);
        float[] zoom2 = WorldGridBuilder.BuildGridVertices(new Vector2(0, 0), new Vector2(32, 32), 2.0f, out _);

        // First quad is a vertical line; its first two vertices differ in X by 2*halfWidth.
        float width1 = zoom1[FloatsPerVertex] - zoom1[0];           // vertex1.x - vertex0.x
        float width2 = zoom2[FloatsPerVertex] - zoom2[0];

        Assert.That(width2, Is.EqualTo(width1 / 2f).Within(1e-4), "doubling zoom halves the world-space line width");
    }

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    private static int CountMultiples(int from, int to, int step)
    {
        int count = 0;
        for (int v = from; v <= to; v++)
        {
            if (v % step == 0)
            {
                count++;
            }
        }

        return count;
    }
}
