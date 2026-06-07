using System.Numerics;
using Technolize.Utils;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// Backend-agnostic CPU representation of the GPU resources the world shader needs for one frame:
/// the dense colour texture and the two packed quadtree textures, plus the coordinate metadata the
/// shader reads as uniforms.
///
/// Pixel buffers are tightly-packed RGBA8, row-major, top-to-bottom (4 bytes per pixel) — the layout
/// expected by <c>GlTexture.CreateRgba8</c>.
/// </summary>
public sealed record WorldShaderResourceData(
    byte[] WorldColorPixels,
    int WorldColorWidth,
    int WorldColorHeight,
    byte[] QuadtreeFirstChildPixels,
    byte[] QuadtreeValuePixels,
    int QuadtreeTextureWidth,
    int QuadtreeTextureHeight,
    Vector2 WorldOrigin,
    Vector2 WorldSize,
    Vector2 QuadtreeSize,
    Vector2 QuadtreeOrigin,
    Vector2 QuadtreeTextureSize);

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
    private const double RefractionEncodingScale = 4096.0;

    /// <summary>Width (in nodes/pixels) of the quadtree textures before wrapping to additional rows.</summary>
    private const int QuadtreeTextureWidth = 2048;

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

        (byte[] firstChildPixels, byte[] valuePixels, int textureWidth, int textureHeight) =
            BuildQuadtreePixels(frame.WorldQuadtree);

        Vector2 worldOrigin = worldRegionStart * RegionSizeVector;

        // Tree coordinate that local draw-space position (0, 0) maps to. Local Y is flipped relative
        // to world Y (top row of the colour texture is the highest world row), so the shader maps a
        // local position p to tree coords as quadtreeOrigin + (p.x, -p.y).
        Vector2 quadtreeOrigin = new(
            worldOrigin.X + TickableWorld.WorldOffset,
            worldOrigin.Y + worldHeight + TickableWorld.WorldOffset);

        return new WorldShaderResourceData(
            colorPixels,
            worldWidth,
            worldHeight,
            firstChildPixels,
            valuePixels,
            textureWidth,
            textureHeight,
            worldOrigin,
            new Vector2(worldWidth, worldHeight),
            new Vector2(TickableWorld.WorldSize, TickableWorld.WorldSize),
            quadtreeOrigin,
            new Vector2(textureWidth, textureHeight));
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
    /// Packs the world's entire pre-built quadtree (a flat <c>(firstChild, value)</c> int array, as
    /// produced by <see cref="TickableWorld.SerializeWorld"/>) into the two RGBA buffers the shader
    /// reads. The tree is used as-is: leaves carry the world block id in <c>value</c>, resolved here to
    /// the material/refraction the shader needs (internal nodes get material 255).
    /// <list type="bullet">
    ///   <item><description>firstChild: RGB = 24-bit first-child index (0 for leaves), A = material code.</description></item>
    ///   <item><description>value: R,G = 16-bit refraction index, B = material code, A = 255.</description></item>
    /// </list>
    /// Padding pixels in the final row stay (0, 0, 0, 255), matching the prior black-filled image.
    /// </summary>
    private static (byte[] firstChild, byte[] value, int width, int height) BuildQuadtreePixels(int[] nodes)
    {
        int nodeCount = nodes.Length / 2;

        int textureWidth = Math.Min(QuadtreeTextureWidth, Math.Max(nodeCount, 1));
        int textureHeight = (Math.Max(nodeCount, 1) + textureWidth - 1) / textureWidth;

        int pixelCount = textureWidth * textureHeight;
        byte[] firstChildPixels = new byte[pixelCount * 4];
        byte[] valuePixels = new byte[pixelCount * 4];

        // Initialise to opaque black, the prior GenImageColor(Color.Black) fill for padding pixels.
        for (int i = 0; i < pixelCount; i++)
        {
            firstChildPixels[(i * 4) + 3] = 255;
            valuePixels[(i * 4) + 3] = 255;
        }

        for (int i = 0; i < nodeCount; i++)
        {
            int firstChild = nodes[i * 2];
            int value = nodes[i * 2 + 1];
            bool isInternal = firstChild >= 0;

            int childIndex = isInternal ? firstChild : 0;
            byte materialCode;
            ushort encodedRefraction;
            if (isInternal)
            {
                // Internal nodes use material code 255; the shader reads it unconditionally.
                materialCode = 255;
                encodedRefraction = 0;
            }
            else
            {
                // Leaf: value is the world block id. Resolve its optics for the shader.
                BlockInfo blockInfo = BlockRegistry.GetInfo(value);
                materialCode = GetMaterialCode(blockInfo);
                encodedRefraction = EncodeRefractionIndex(blockInfo.GetTag(BlockInfo.TagRefractionIndex));
            }

            int index = i * 4;

            firstChildPixels[index + 0] = (byte)(childIndex & 0xFF);
            firstChildPixels[index + 1] = (byte)((childIndex >> 8) & 0xFF);
            firstChildPixels[index + 2] = (byte)((childIndex >> 16) & 0xFF);
            firstChildPixels[index + 3] = materialCode;

            valuePixels[index + 0] = (byte)(encodedRefraction & 0xFF);
            valuePixels[index + 1] = (byte)(encodedRefraction >> 8);
            valuePixels[index + 2] = materialCode;
            valuePixels[index + 3] = 255;
        }

        return (firstChildPixels, valuePixels, textureWidth, textureHeight);
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

    private static ushort EncodeRefractionIndex(double value)
    {
        return checked((ushort)Math.Clamp((int)Math.Round(value * RefractionEncodingScale), 0, ushort.MaxValue));
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
