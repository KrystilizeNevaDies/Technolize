using System.Numerics;
using System.Runtime.InteropServices;
using Technolize.Utils;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// One quadtree node as the fragment shader consumes it from the <c>std430</c> SSBO. The CPU resolves
/// each leaf's block id to its <see cref="Material"/> and <see cref="RefractionIndex"/> here, so the
/// shader does a single buffer index with no bit-unpacking. Internal nodes use material 255 (the
/// shader's INTERNAL sentinel) and point at their four contiguous children via <see cref="FirstChild"/>.
///
/// Field order and the 4-byte scalar layout match the GLSL <c>struct { int firstChild; int material;
/// float refractionIndex; }</c> under std430 (12-byte stride).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct GpuQuadtreeNode(int FirstChild, int Material, float RefractionIndex);

/// <summary>
/// Backend-agnostic CPU representation of the GPU resources the world shader needs for one frame:
/// the dense colour texture plus the world quadtree as a flat node array, and the coordinate metadata
/// the shader reads as uniforms.
///
/// Pixel buffers are tightly-packed RGBA8, row-major, top-to-bottom (4 bytes per pixel) — the layout
/// expected by <c>GlTexture.CreateRgba8</c>. The quadtree is a flat <see cref="GpuQuadtreeNode"/> array
/// uploaded verbatim to a shader storage buffer.
/// </summary>
public sealed record WorldShaderResourceData(
    byte[] WorldColorPixels,
    int WorldColorWidth,
    int WorldColorHeight,
    GpuQuadtreeNode[] QuadtreeNodes,
    Vector2 WorldOrigin,
    Vector2 WorldSize,
    Vector2 QuadtreeSize,
    Vector2 QuadtreeOrigin);

/// <summary>
/// Builds <see cref="WorldShaderResourceData"/> from a captured <see cref="WorldRenderFrame"/>. This
/// holds the GPU-API-independent half of <c>WorldShaderRenderer</c>: cell rasterisation, colour
/// packing, and the quadtree texture encoding. No graphics calls are made here, so the result can be
/// unit-tested on the CPU and uploaded by any backend.
/// </summary>
public static class WorldShaderResourceBuilder
{
    private const byte AirMaterialCode = 0;
    private const byte WaterMaterialCode = 1;
    private const byte SolidMaterialCode = 2;
    private const int InternalMaterialCode = 255;
    private const double RefractionEncodingScale = 4096.0;

    private static readonly Vector2 RegionSizeVector = new(TickableWorld.RegionSize);

    private readonly record struct WorldCell(Color Color, byte MaterialCode);

    /// <summary>
    /// Builds the colour + quadtree pixel buffers and coordinate metadata for the loaded world region
    /// box <c>[worldRegionStart, worldRegionEnd)</c> (in region space).
    /// </summary>
    public static WorldShaderResourceData Build(WorldRenderFrame frame, Vector2 worldRegionStart, Vector2 worldRegionEnd)
    {
        int worldWidth = Math.Max(1, (int)((worldRegionEnd.X - worldRegionStart.X) * TickableWorld.RegionSize));
        int worldHeight = Math.Max(1, (int)((worldRegionEnd.Y - worldRegionStart.Y) * TickableWorld.RegionSize));
        int quadtreeSide = NextPowerOfTwo(Math.Max(worldWidth, worldHeight));

        WorldCell[,] cells = CreateWorldCells(frame, worldRegionStart, worldWidth, worldHeight, quadtreeSide);
        byte[] colorPixels = BuildWorldColorPixels(cells, worldWidth, worldHeight);

        GpuQuadtreeNode[] quadtreeNodes = BuildQuadtreeNodes(frame.WorldQuadtree);

        Vector2 worldOrigin = worldRegionStart * RegionSizeVector;

        // The quadtree array is a window of the global tree. QuadtreeWindowSize == 0 is the legacy
        // "whole world" sentinel (the array is the entire [0, WorldSize) tree); otherwise the array
        // covers the aligned window [windowOrigin, windowOrigin + windowSize) in absolute tree coords.
        bool windowed = frame.QuadtreeWindowSize > 0;
        int windowSize = windowed ? frame.QuadtreeWindowSize : TickableWorld.WorldSize;
        int windowOriginX = windowed ? frame.QuadtreeWindowOriginX : 0;
        int windowOriginY = windowed ? frame.QuadtreeWindowOriginY : 0;

        // Tree coordinate that local draw-space position (0, 0) maps to, expressed relative to the
        // window origin so the shader descends the shallow windowed tree ([0, windowSize)). Local Y is
        // flipped relative to world Y (top row of the colour texture is the highest world row), so the
        // shader maps a local position p to tree coords as quadtreeOrigin + (p.x, -p.y).
        Vector2 quadtreeOrigin = new(
            worldOrigin.X + TickableWorld.WorldOffset - windowOriginX,
            worldOrigin.Y + worldHeight + TickableWorld.WorldOffset - windowOriginY);

        return new WorldShaderResourceData(
            colorPixels,
            worldWidth,
            worldHeight,
            quadtreeNodes,
            worldOrigin,
            new Vector2(worldWidth, worldHeight),
            new Vector2(windowSize, windowSize),
            quadtreeOrigin);
    }

    private static WorldCell[,] CreateWorldCells(WorldRenderFrame frame, Vector2 worldRegionStart, int worldWidth, int worldHeight, int quadtreeSide)
    {
        WorldCell[,] cells = new WorldCell[quadtreeSide, quadtreeSide];
        WorldCell airCell = CreateCell(Blocks.Air);

        for (int y = 0; y < quadtreeSide; y++)
        {
            for (int x = 0; x < quadtreeSide; x++)
            {
                cells[x, y] = airCell;
            }
        }

        foreach (WorldRenderRegion region in frame.Regions)
        {
            int regionBaseX = (int)((region.Position.X - worldRegionStart.X) * TickableWorld.RegionSize);
            int regionBaseY = (int)((region.Position.Y - worldRegionStart.Y) * TickableWorld.RegionSize);

            foreach (WorldRenderBlock block in region.Blocks)
            {
                BlockInfo blockInfo = BlockRegistry.GetInfo(block.BlockId);
                int worldX = regionBaseX + (int)block.LocalPos.X;
                int worldY = regionBaseY + (int)block.LocalPos.Y;
                if (worldX < 0 || worldX >= worldWidth || worldY < 0 || worldY >= worldHeight)
                {
                    continue;
                }

                int textureY = worldHeight - worldY - 1;
                cells[worldX, textureY] = CreateCell(blockInfo);
            }
        }

        return cells;
    }

    private static WorldCell CreateCell(BlockInfo blockInfo)
    {
        return new WorldCell(
            blockInfo.GetTag(BlockInfo.TagColor),
            GetMaterialCode(blockInfo));
    }

    /// <summary>
    /// Packs the dense colour texture: RGB = block colour, A = per-cell material code (read by the sun
    /// raymarch). Row-major, top-to-bottom.
    /// </summary>
    private static byte[] BuildWorldColorPixels(WorldCell[,] cells, int worldWidth, int worldHeight)
    {
        byte[] pixels = new byte[worldWidth * worldHeight * 4];

        for (int y = 0; y < worldHeight; y++)
        {
            for (int x = 0; x < worldWidth; x++)
            {
                WorldCell cell = cells[x, y];
                int index = ((y * worldWidth) + x) * 4;
                pixels[index + 0] = cell.Color.R;
                pixels[index + 1] = cell.Color.G;
                pixels[index + 2] = cell.Color.B;
                pixels[index + 3] = cell.MaterialCode;
            }
        }

        return pixels;
    }

    /// <summary>
    /// Converts the world's entire pre-built quadtree (a flat <c>(firstChild, value)</c> int array, as
    /// produced by <see cref="TickableWorld.SerializeWorld"/>) into the flat <see cref="GpuQuadtreeNode"/>
    /// array the shader reads directly from its SSBO. The tree is used as-is: leaves carry the world
    /// block id in <c>value</c>, resolved here to the material/refraction the shader needs (internal
    /// nodes get material 255 and keep their first-child index).
    ///
    /// The refraction index is quantised to 1/4096 (the precision the prior 16-bit texture encoding
    /// gave the shader) so this change is bit-identical to the packed-texture path.
    /// </summary>
    private static GpuQuadtreeNode[] BuildQuadtreeNodes(int[] nodes)
    {
        int nodeCount = nodes.Length / 2;

        // An empty tree never occurs for a real render (SerializeWorld always yields at least the root);
        // emit a single air leaf so the SSBO is never zero-length and the shader's node[0] read is safe.
        GpuQuadtreeNode[] result = new GpuQuadtreeNode[Math.Max(nodeCount, 1)];
        if (nodeCount == 0)
        {
            result[0] = new GpuQuadtreeNode(0, AirMaterialCode, 0f);
            return result;
        }

        for (int i = 0; i < nodeCount; i++)
        {
            int firstChild = nodes[i * 2];
            int value = nodes[i * 2 + 1];
            bool isInternal = firstChild >= 0;

            if (isInternal)
            {
                // Internal nodes use material code 255; the shader reads it unconditionally.
                result[i] = new GpuQuadtreeNode(firstChild, InternalMaterialCode, 0f);
            }
            else
            {
                // Leaf: value is the world block id. Resolve its optics for the shader.
                BlockInfo blockInfo = BlockRegistry.GetInfo(value);
                byte materialCode = GetMaterialCode(blockInfo);
                float refractionIndex = QuantizeRefractionIndex(blockInfo.GetTag(BlockInfo.TagRefractionIndex));
                result[i] = new GpuQuadtreeNode(0, materialCode, refractionIndex);
            }
        }

        return result;
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    /// <summary>
    /// Quantises a refraction index to the 1/4096 grid the prior 16-bit texture encoding produced,
    /// then divides in single precision exactly as the shader did (<c>float(encoded) / 4096.0</c>), so
    /// the value handed to the raymarch is bit-identical to the packed-texture path.
    /// </summary>
    private static float QuantizeRefractionIndex(double value)
    {
        ushort encoded = checked((ushort)Math.Clamp((int)Math.Round(value * RefractionEncodingScale), 0, ushort.MaxValue));
        return encoded / 4096f;
    }

    private static byte GetMaterialCode(BlockInfo blockInfo)
    {
        BlockInfo baseBlock = blockInfo.BaseBlock;
        if (ReferenceEquals(baseBlock, Blocks.Air))
        {
            return AirMaterialCode;
        }

        if (ReferenceEquals(baseBlock, Blocks.Water))
        {
            return WaterMaterialCode;
        }

        return SolidMaterialCode;
    }
}
