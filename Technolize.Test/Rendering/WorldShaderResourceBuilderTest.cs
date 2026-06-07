using System.Numerics;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Test.Rendering;

/// <summary>
/// Pure-CPU tests for <see cref="WorldShaderResourceBuilder"/>, the backend-agnostic half of the world
/// shader renderer that packs the colour and quadtree textures. These run without any GPU/window, so
/// they validate the exact byte layout the GPU upload path depends on.
/// </summary>
[TestFixture]
public class WorldShaderResourceBuilderTest
{
    private static int RegionSize => TickableWorld.RegionSize;

    [Test]
    public void Build_ColorBufferDimensions_CoverTheRegionBox()
    {
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), []);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(2, 1));

        Assert.Multiple(() =>
        {
            Assert.That(data.WorldColorWidth, Is.EqualTo(2 * RegionSize));
            Assert.That(data.WorldColorHeight, Is.EqualTo(1 * RegionSize));
            Assert.That(data.WorldColorPixels, Has.Length.EqualTo(data.WorldColorWidth * data.WorldColorHeight * 4));
            Assert.That(data.WorldSize, Is.EqualTo(new Vector2(2 * RegionSize, RegionSize)));
        });
    }

    [Test]
    public void Build_BlockColor_IsWrittenAtVerticallyFlippedRow()
    {
        // A single Stone block at region-local (0, 0). World Y is flipped into the texture, so it lands
        // on the bottom row (textureY = height - 1), column 0, with material 2 (solid) in the alpha.
        WorldRenderBlock block = new(new Vector2(0, 0), Blocks.Stone.Id);
        WorldRenderRegion region = new(new Vector2(0, 0), 0.0, [block]);
        WorldRenderFrame frame = new([region], new HashSet<Vector2>(), []);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        int width = data.WorldColorWidth;
        int height = data.WorldColorHeight;
        int bottomRowIndex = (((height - 1) * width) + 0) * 4;

        Assert.Multiple(() =>
        {
            Assert.That(data.WorldColorPixels[bottomRowIndex + 0], Is.EqualTo(130), "red");
            Assert.That(data.WorldColorPixels[bottomRowIndex + 1], Is.EqualTo(135), "green");
            Assert.That(data.WorldColorPixels[bottomRowIndex + 2], Is.EqualTo(140), "blue");
            Assert.That(data.WorldColorPixels[bottomRowIndex + 3], Is.EqualTo(2), "material code (solid) in alpha");
        });

        // An untouched cell (top-left) stays air: material code 0 in the alpha channel.
        Assert.That(data.WorldColorPixels[3], Is.EqualTo(0), "air material code");
    }

    [Test]
    public void Build_SolidSdf_IsZeroAtSolidAndGrowsWithDistance()
    {
        // A single Stone block at region-local (0, 0) -> bottom row, column 0 of the texture. The solid
        // distance field is the floored Euclidean distance (in cells) to the nearest solid cell.
        WorldRenderBlock block = new(new Vector2(0, 0), Blocks.Stone.Id);
        WorldRenderRegion region = new(new Vector2(0, 0), 0.0, [block]);
        WorldRenderFrame frame = new([region], new HashSet<Vector2>(), []);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        int width = data.WorldColorWidth;
        int height = data.WorldColorHeight;
        Assert.That(data.SolidSdfPixels, Has.Length.EqualTo(width * height), "one byte per pixel");

        int solidIndex = ((height - 1) * width) + 0; // the solid cell
        Assert.Multiple(() =>
        {
            Assert.That(data.SolidSdfPixels[solidIndex], Is.EqualTo(0), "distance is 0 at the solid cell");
            // Orthogonal neighbour (one cell right) is distance 1.
            Assert.That(data.SolidSdfPixels[solidIndex + 1], Is.EqualTo(1), "one cell from the solid");
            // The cell directly above (one row up) is distance 1.
            Assert.That(data.SolidSdfPixels[solidIndex - width], Is.EqualTo(1), "one row from the solid");
            // The diagonal neighbour is sqrt(2) ~ 1.41, floored to 1.
            Assert.That(data.SolidSdfPixels[solidIndex - width + 1], Is.EqualTo(1), "diagonal floored to 1");
        });
    }

    [Test]
    public void Build_SolidSdf_AllAirWorld_IsSaturated()
    {
        // No solids anywhere -> every texel's nearest-solid distance is "infinite", capped at 255.
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), []);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        Assert.That(data.SolidSdfPixels, Is.All.EqualTo(255), "no solids -> all distances saturate at 255");
    }

    [Test]
    public void Build_QuadtreeInternalNode_StoresChildIndexAndMaterial255()
    {
        // node 0: internal pointing at firstChild=4; node 1: a Stone leaf (firstChild < 0).
        int[] nodes =
        {
            4, 0,
            -1, (int)Blocks.Stone.Id,
        };
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), nodes);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        // Internal node 0: keeps its first-child index, material 255 (the shader's INTERNAL sentinel).
        GpuQuadtreeNode internalNode = data.QuadtreeNodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(internalNode.FirstChild, Is.EqualTo(4), "first-child index");
            Assert.That(internalNode.Material, Is.EqualTo(255), "internal node material");
            Assert.That(internalNode.RefractionIndex, Is.EqualTo(0f), "internal node refraction");
        });
    }

    [Test]
    public void Build_QuadtreeLeafNode_StoresRefractionAndMaterial()
    {
        int[] nodes =
        {
            4, 0,
            -1, (int)Blocks.Stone.Id,
        };
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), nodes);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        // Leaf node 1: child index 0, material (solid = 2), refraction quantised to 1/4096 exactly as
        // the prior 16-bit texture encoding did (1.52 * 4096 = 6225.92 -> 6226; 6226 / 4096 in float).
        GpuQuadtreeNode leaf = data.QuadtreeNodes[1];
        Assert.Multiple(() =>
        {
            Assert.That(leaf.FirstChild, Is.EqualTo(0), "leaf first-child index");
            Assert.That(leaf.Material, Is.EqualTo(2), "leaf material (solid)");
            Assert.That(leaf.RefractionIndex, Is.EqualTo(6226 / 4096f), "quantised refraction index");
        });
    }

    [Test]
    public void Build_QuadtreeMetadata_DerivesOriginFromRegionStart()
    {
        int[] nodes = { -1, (int)Blocks.Air.Id };
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), nodes);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(data.WorldOrigin, Is.EqualTo(new Vector2(0, 0)));
            Assert.That(data.QuadtreeOrigin.X, Is.EqualTo(TickableWorld.WorldOffset));
            Assert.That(data.QuadtreeOrigin.Y, Is.EqualTo(data.WorldColorHeight + TickableWorld.WorldOffset));
            Assert.That(data.QuadtreeSize, Is.EqualTo(new Vector2(TickableWorld.WorldSize, TickableWorld.WorldSize)));
        });
    }
}
