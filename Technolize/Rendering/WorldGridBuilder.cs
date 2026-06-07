using System.Numerics;
using Technolize.Utils;

namespace Technolize.Rendering;

/// <summary>
/// Backend-agnostic generator for the world grid overlay (adaptive power-of-two spacing, zoom-scaled
/// line width, world-pixel coordinates with Y flipped). It emits plain geometry instead of issuing
/// graphics calls, so it is unit-testable on the CPU and uploadable by the GPU.
///
/// Each grid line is emitted as a thin rectangle (two triangles) centred on the line. Vertices are
/// interleaved as <c>(posX, posY, r, g, b, a)</c> in world-pixel space; the camera transform is applied
/// later in the vertex shader.
/// </summary>
public static class WorldGridBuilder
{
    private const float BlockSize = 16f;
    private const int FloatsPerVertex = 6;
    private const int VerticesPerLine = 6; // two triangles
    private const double TargetGridCount = 256;

    private static readonly Color GridColor = new(255, 255, 255, 64);

    /// <summary>
    /// Computes the adaptive grid spacing (in world blocks) for the given visible block bounds — the
    /// smallest power of two whose on-screen density stays near <c>TargetGridCount</c>.
    /// </summary>
    public static int ComputeGridSize(Vector2 worldStart, Vector2 worldEnd)
    {
        double worldWidth = (worldEnd.X - worldStart.X) * BlockSize;
        double worldHeight = (worldEnd.Y - worldStart.Y) * BlockSize;
        double variableGridSize = Math.Max(1, Math.Max(worldWidth, worldHeight) / TargetGridCount);

        int gridSize = 1;
        while (gridSize < variableGridSize)
        {
            gridSize *= 2;
        }

        return gridSize;
    }

    /// <summary>
    /// Builds the interleaved triangle vertices (<c>posX, posY, r, g, b, a</c>) for the grid overlay
    /// across the visible block bounds at the given camera zoom. <paramref name="vertexCount"/> is the
    /// number of vertices (always a multiple of 6). Returns an empty array when no lines are visible.
    /// </summary>
    public static float[] BuildGridVertices(Vector2 worldStart, Vector2 worldEnd, float zoom, out int vertexCount)
    {
        int gridSize = ComputeGridSize(worldStart, worldEnd);

        // World-unit half width so the projected line stays ~2px on screen (2.0 / zoom).
        float halfWidth = zoom > 0f ? 1.0f / zoom : 1.0f;

        int startX = (int)worldStart.X;
        int endX = (int)worldEnd.X;
        int startY = (int)worldStart.Y;
        int endY = (int)worldEnd.Y;

        List<float> vertices = new();

        // Vertical lines span the full visible Y range at each grid column.
        float topY = -worldStart.Y * BlockSize;
        float bottomY = -worldEnd.Y * BlockSize;
        for (int x = startX; x <= endX; x++)
        {
            if (x % gridSize != 0)
            {
                continue;
            }

            float xPos = x * BlockSize;
            AddQuad(vertices, xPos - halfWidth, topY, xPos + halfWidth, bottomY);
        }

        // Horizontal lines span the full visible X range at each grid row.
        float leftX = worldStart.X * BlockSize;
        float rightX = worldEnd.X * BlockSize;
        for (int y = startY; y <= endY; y++)
        {
            if (y % gridSize != 0)
            {
                continue;
            }

            float yPos = -y * BlockSize;
            AddQuad(vertices, leftX, yPos - halfWidth, rightX, yPos + halfWidth);
        }

        vertexCount = vertices.Count / FloatsPerVertex;
        return vertices.ToArray();
    }

    /// <summary>Appends a coloured rectangle (two triangles) spanning the given world-space corners.</summary>
    private static void AddQuad(List<float> vertices, float x0, float y0, float x1, float y1)
    {
        float r = GridColor.R / 255f;
        float g = GridColor.G / 255f;
        float b = GridColor.B / 255f;
        float a = GridColor.A / 255f;

        void Vertex(float x, float y)
        {
            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(r);
            vertices.Add(g);
            vertices.Add(b);
            vertices.Add(a);
        }

        Vertex(x0, y0);
        Vertex(x1, y0);
        Vertex(x1, y1);

        Vertex(x0, y0);
        Vertex(x1, y1);
        Vertex(x0, y1);
    }
}
