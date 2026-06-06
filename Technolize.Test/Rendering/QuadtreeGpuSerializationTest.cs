using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Test.Rendering;

[TestFixture]
public class QuadtreeGpuSerializationTest
{
    // Walks a flat serialized quadtree node list by descending from the root, picking child
    // (x>=cx?1:0)|(y>=cy?2:0) at firstChild+offset - the same traversal a GPU consumer would use.
    private static IntQuadTree.SerializedNode NavigateLeaf(IntQuadTree.SerializedNode[] nodes, int x, int y, int side)
    {
        int nodeIndex = 0;
        int minX = 0, minY = 0, maxX = side, maxY = side;

        for (int depth = 0; depth < 32; depth++)
        {
            IntQuadTree.SerializedNode node = nodes[nodeIndex];
            if (node.IsLeaf)
            {
                return node;
            }

            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;
            int offset = 0;

            if (x >= centerX) { offset += 1; minX = centerX; } else { maxX = centerX; }
            if (y >= centerY) { offset += 2; minY = centerY; } else { maxY = centerY; }

            nodeIndex = node.FirstChild + offset;
        }

        throw new InvalidOperationException("Quadtree traversal exceeded maximum depth.");
    }

    [Test]
    public void SerializeWindow_NavigatedLikeConsumer_MatchesEveryCell()
    {
        const int side = 64;
        IntQuadTree tree = new(side);
        uint[,] expected = new uint[side, side];
        Random rng = new(20260605);

        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                uint value = (uint)rng.Next(0, 4);
                tree.Set(x, y, (int)value);
                expected[x, y] = value;
            }
        }

        IntQuadTree.SerializedNode[] nodes = tree.SerializeWindow(0, 0, side);

        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                IntQuadTree.SerializedNode leaf = NavigateLeaf(nodes, x, y, side);
                Assert.That(leaf.Value, Is.EqualTo(expected[x, y]), $"Mismatch at ({x}, {y}).");
            }
        }
    }

    [Test]
    public void SerializeWindow_InternalChildrenAreContiguousAndInBounds()
    {
        const int side = 32;
        IntQuadTree tree = new(side);
        Random rng = new(7);
        for (int i = 0; i < 200; i++)
        {
            tree.Set(rng.Next(side), rng.Next(side), rng.Next(1, 5));
        }

        IntQuadTree.SerializedNode[] nodes = tree.SerializeWindow(0, 0, side);

        foreach (IntQuadTree.SerializedNode node in nodes)
        {
            if (node.IsLeaf)
            {
                continue;
            }

            // The four children must occupy contiguous in-bounds slots: firstChild + 0..3.
            Assert.That(node.FirstChild, Is.GreaterThanOrEqualTo(1));
            Assert.That(node.FirstChild + 3, Is.LessThan(nodes.Length));
        }
    }

    [Test]
    public void SerializeWindow_UniformRegionCollapsesToSingleLeaf()
    {
        const int side = 16;
        IntQuadTree tree = new(side, fill: 3);

        IntQuadTree.SerializedNode[] nodes = tree.SerializeWindow(0, 0, side);

        Assert.That(nodes, Has.Length.EqualTo(1));
        Assert.That(nodes[0].IsLeaf, Is.True);
        Assert.That(nodes[0].Value, Is.EqualTo(3u));
    }

    [Test]
    public void SerializeWindow_UnchangedSubtree_ReturnsCachedReference()
    {
        const int side = 32;
        IntQuadTree tree = new(side);
        tree.Set(1, 1, 2);
        tree.Set(20, 20, 1);

        IntQuadTree.SerializedNode[] first = tree.SerializeWindow(0, 0, side);
        IntQuadTree.SerializedNode[] second = tree.SerializeWindow(0, 0, side);

        // An unchanged subtree must hand back the exact same cached array (no re-walk/alloc).
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void SerializeWindow_AfterChange_RebuildsCache()
    {
        const int side = 32;
        IntQuadTree tree = new(side);
        tree.Set(1, 1, 2);

        IntQuadTree.SerializedNode[] before = tree.SerializeWindow(0, 0, side);
        tree.Set(5, 6, 1);
        IntQuadTree.SerializedNode[] after = tree.SerializeWindow(0, 0, side);

        Assert.That(after, Is.Not.SameAs(before));

        // And the rebuilt serialization must reflect the new cell.
        IntQuadTree.SerializedNode leaf = NavigateLeaf(after, 5, 6, side);
        Assert.That(leaf.Value, Is.EqualTo(1u));
    }

    [Test]
    public void SerializeWindow_NoOpSet_KeepsCachedReference()
    {
        const int side = 16;
        IntQuadTree tree = new(side);
        tree.Set(3, 3, 2);

        IntQuadTree.SerializedNode[] first = tree.SerializeWindow(0, 0, side);
        tree.Set(3, 3, 2); // value unchanged -> must not invalidate the cache
        IntQuadTree.SerializedNode[] second = tree.SerializeWindow(0, 0, side);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void SerializeWindow_AlignedSubWindow_MatchesWorldCells()
    {
        const int side = 64;
        const int windowSize = 16;
        const int originX = 32;
        const int originY = 16;
        IntQuadTree tree = new(side);
        Random rng = new(99);
        uint[,] expected = new uint[windowSize, windowSize];

        for (int lx = 0; lx < windowSize; lx++)
        {
            for (int ly = 0; ly < windowSize; ly++)
            {
                uint value = (uint)rng.Next(0, 3);
                tree.Set(originX + lx, originY + ly, (int)value);
                expected[lx, ly] = value;
            }
        }

        IntQuadTree.SerializedNode[] nodes = tree.SerializeWindow(originX, originY, windowSize);

        for (int lx = 0; lx < windowSize; lx++)
        {
            for (int ly = 0; ly < windowSize; ly++)
            {
                IntQuadTree.SerializedNode leaf = NavigateLeaf(nodes, lx, ly, windowSize);
                Assert.That(leaf.Value, Is.EqualTo(expected[lx, ly]), $"Mismatch at window ({lx}, {ly}).");
            }
        }
    }

    // Walks a flat int-array serialized quadtree (two ints per node: [i*2]=firstChild, [i*2+1]=value)
    // exactly the way a GPU consumer would, returning the leaf value covering (x, y).
    private static int NavigateLeafInts(int[] data, int x, int y, int side)
    {
        int nodeIndex = 0;
        int minX = 0, minY = 0, maxX = side, maxY = side;

        for (int depth = 0; depth < 32; depth++)
        {
            int firstChild = data[nodeIndex * 2];
            int value = data[nodeIndex * 2 + 1];
            if (firstChild < 0)
            {
                return value;
            }

            int centerX = (minX + maxX) / 2;
            int centerY = (minY + maxY) / 2;
            int offset = 0;

            if (x >= centerX) { offset += 1; minX = centerX; } else { maxX = centerX; }
            if (y >= centerY) { offset += 2; minY = centerY; } else { maxY = centerY; }

            nodeIndex = firstChild + offset;
        }

        throw new InvalidOperationException("Quadtree traversal exceeded maximum depth.");
    }

    [Test]
    public void SerializeWindowToInts_MatchesSerializedNodeArray()
    {
        const int side = 32;
        IntQuadTree tree = new(side);
        Random rng = new(123);
        for (int i = 0; i < 300; i++)
        {
            tree.Set(rng.Next(side), rng.Next(side), rng.Next(1, 6));
        }

        IntQuadTree.SerializedNode[] nodes = tree.SerializeWindow(0, 0, side);
        int[] data = tree.SerializeWindowToInts(0, 0, side);

        Assert.That(data, Has.Length.EqualTo(nodes.Length * 2));
        for (int i = 0; i < nodes.Length; i++)
        {
            Assert.That(data[i * 2], Is.EqualTo(nodes[i].FirstChild), $"firstChild mismatch at node {i}.");
            Assert.That(data[i * 2 + 1], Is.EqualTo((int)nodes[i].Value), $"value mismatch at node {i}.");
        }
    }

    [Test]
    public void SerializeWindowToInts_NavigatedLikeConsumer_MatchesEveryCell()
    {
        const int side = 64;
        IntQuadTree tree = new(side);
        uint[,] expected = new uint[side, side];
        Random rng = new(456);

        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                uint value = (uint)rng.Next(0, 4);
                tree.Set(x, y, (int)value);
                expected[x, y] = value;
            }
        }

        int[] data = tree.SerializeToInts();

        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                int value = NavigateLeafInts(data, x, y, side);
                Assert.That(value, Is.EqualTo((int)expected[x, y]), $"Mismatch at ({x}, {y}).");
            }
        }
    }

    [Test]
    public void SerializeWindowToInts_UniformTreeIsSingleLeaf()
    {
        const int side = 16;
        IntQuadTree tree = new(side, fill: 5);

        int[] data = tree.SerializeToInts();

        Assert.That(data, Has.Length.EqualTo(2));
        Assert.That(data[0], Is.LessThan(0)); // leaf
        Assert.That(data[1], Is.EqualTo(5));
    }

    [Test]
    public void TickableWorld_SerializeRegion_RoundTripsBlocks()
    {
        TickableWorld world = new();
        // Place a few distinct blocks within a single region's window.
        world.SetBlock(new System.Numerics.Vector2(0, 0), Blocks.Sand);
        world.SetBlock(new System.Numerics.Vector2(2, 3), Blocks.Stone);
        world.SetBlock(new System.Numerics.Vector2(5, 1), Blocks.Water);

        System.Numerics.Vector2 regionPos = new(0, 0);
        int[] data = world.SerializeRegion(regionPos);

        Assert.That(data.Length % 2, Is.EqualTo(0));
        Assert.That(NavigateLeafInts(data, 0, 0, TickableWorld.RegionSize), Is.EqualTo((int)Blocks.Sand.Id));
        Assert.That(NavigateLeafInts(data, 2, 3, TickableWorld.RegionSize), Is.EqualTo((int)Blocks.Stone.Id));
        Assert.That(NavigateLeafInts(data, 5, 1, TickableWorld.RegionSize), Is.EqualTo((int)Blocks.Water.Id));
    }

    [Test]
    public void Set_FillingABranchWithOneBlock_CollapsesToSingleLeaf()
    {
        const int side = 16;
        IntQuadTree tree = new(side);

        // Force the tree to subdivide all the way down, then refill the whole area with one value.
        tree.Set(0, 0, 1);
        tree.Set(15, 15, 2);
        Assert.That(tree.SerializeToInts().Length / 2, Is.GreaterThan(1), "Tree should have subdivided.");

        for (int x = 0; x < side; x++)
        {
            for (int y = 0; y < side; y++)
            {
                tree.Set(x, y, 7);
            }
        }

        int[] data = tree.SerializeToInts();
        Assert.That(data.Length / 2, Is.EqualTo(1), "A fully uniform tree must collapse to a single leaf.");
        Assert.That(data[0], Is.LessThan(0)); // leaf
        Assert.That(data[1], Is.EqualTo(7));
    }

    [Test]
    public void Set_PartialFill_CollapsesOnlyUniformSubtrees()
    {
        const int side = 16;
        IntQuadTree tree = new(side);

        // Make the whole tree non-uniform, then fill exactly the bottom-left 8x8 quadrant with one
        // value while leaving one differing cell elsewhere.
        tree.Set(0, 0, 1);
        tree.Set(15, 15, 2);

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                tree.Set(x, y, 3);
            }
        }

        int[] data = tree.SerializeToInts();

        // The filled quadrant collapses to a single leaf, so the whole tree is far smaller than the
        // fully-subdivided node count, yet every cell still reads back correctly.
        Assert.That(data.Length / 2, Is.LessThan(64));
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                Assert.That(NavigateLeafInts(data, x, y, side), Is.EqualTo(3), $"Cell ({x},{y}) should be 3.");
            }
        }
        Assert.That(NavigateLeafInts(data, 15, 15, side), Is.EqualTo(2));
    }
}

