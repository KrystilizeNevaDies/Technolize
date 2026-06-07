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
    public void Build_QuadtreeInternalNode_PacksChildIndexAndMaterial255()
    {
        // node 0: internal pointing at firstChild=4; node 1: a Stone leaf (firstChild < 0).
        int[] nodes =
        {
            4, 0,
            -1, (int)Blocks.Stone.Id,
        };
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), nodes);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        // Internal node 0: firstChild 24-bit in RGB, material 255 in alpha.
        Assert.Multiple(() =>
        {
            Assert.That(data.QuadtreeFirstChildPixels[0], Is.EqualTo(4), "firstChild low byte");
            Assert.That(data.QuadtreeFirstChildPixels[1], Is.EqualTo(0), "firstChild mid byte");
            Assert.That(data.QuadtreeFirstChildPixels[2], Is.EqualTo(0), "firstChild high byte");
            Assert.That(data.QuadtreeFirstChildPixels[3], Is.EqualTo(255), "internal node material");
        });
    }

    [Test]
    public void Build_QuadtreeLeafNode_PacksRefractionAndMaterial()
    {
        int[] nodes =
        {
            4, 0,
            -1, (int)Blocks.Stone.Id,
        };
        WorldRenderFrame frame = new([], new HashSet<Vector2>(), nodes);

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, new Vector2(0, 0), new Vector2(1, 1));

        // Leaf node 1 (pixel index 1 → byte offset 4). Refraction 1.52 * 4096 = 6226 = 0x1852.
        const int leaf = 4;
        Assert.Multiple(() =>
        {
            // firstChild buffer: leaves carry child index 0, material (solid = 2) in alpha.
            Assert.That(data.QuadtreeFirstChildPixels[leaf + 0], Is.EqualTo(0));
            Assert.That(data.QuadtreeFirstChildPixels[leaf + 1], Is.EqualTo(0));
            Assert.That(data.QuadtreeFirstChildPixels[leaf + 2], Is.EqualTo(0));
            Assert.That(data.QuadtreeFirstChildPixels[leaf + 3], Is.EqualTo(2), "leaf material in alpha");

            // value buffer: R,G = 16-bit refraction (0x1852), B = material, A = 255.
            Assert.That(data.QuadtreeValuePixels[leaf + 0], Is.EqualTo(0x52), "refraction low byte");
            Assert.That(data.QuadtreeValuePixels[leaf + 1], Is.EqualTo(0x18), "refraction high byte");
            Assert.That(data.QuadtreeValuePixels[leaf + 2], Is.EqualTo(2), "value material");
            Assert.That(data.QuadtreeValuePixels[leaf + 3], Is.EqualTo(255), "value alpha");
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
            Assert.That(data.QuadtreeTextureSize, Is.EqualTo(new Vector2(data.QuadtreeTextureWidth, data.QuadtreeTextureHeight)));
        });
    }
}
