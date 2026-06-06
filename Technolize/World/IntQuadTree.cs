namespace Technolize.World;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

/// <summary>
/// A quadtree storing <see cref="int"/> values (block ids) over a square, power-of-two area.
/// <para>
/// Each internal branch caches a flat, packed <c>int[]</c> of its subtree's contents. A window of
/// the tree can be read as a packed array via <see cref="GetPacked(int,int,int)"/>; that array is
/// cached at the covering branch node and only rebuilt after a contained value changes, so repeated
/// reads of an unchanged window are allocation-free.
/// </para>
/// <para>
/// Packed arrays use raw rectangular-array memory order (<c>index = x * size + y</c>) so an
/// unchanged window can be uploaded or hashed directly.
/// </para>
/// <para>
/// Thread-safety: the tree tolerates the same concurrent access pattern as a plain
/// <c>uint[,]</c> grid used by the world ticker. Concurrent reads, concurrent value writes to
/// distinct cells, and racing writes to the same cell are all safe (value stores are atomic and
/// follow last-writer-wins, matching array semantics). Structural mutations (subdivision and
/// window clears) are serialized through an internal lock and published with
/// <see cref="Volatile"/>, so concurrent readers/writers never observe a torn node. Like the array
/// it replaces, it does not provide a globally consistent snapshot across threads.
/// </para>
/// </summary>
public class IntQuadTree
{
    /// <summary>The side length of the square area covered by this tree.</summary>
    public int Size { get; }

    private readonly Node _root;
    private readonly object _structureLock = new();

    /// <summary>
    /// Creates a new quadtree covering a <paramref name="size"/> by <paramref name="size"/> area.
    /// </summary>
    /// <param name="size">The side length. Must be a positive power of two.</param>
    /// <param name="fill">The initial value for every cell.</param>
    public IntQuadTree(int size, int fill = 0)
    {
        if (size <= 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("Size must be a positive power of two.", nameof(size));
        }

        Size = size;
        _root = new Node(size, fill);
    }

    /// <summary>Gets the value at the given coordinate.</summary>
    public int Get(int x, int y)
    {
        ValidateCoords(x, y);
        return _root.Get(x, y);
    }

    /// <summary>
    /// Sets the value at the given coordinate.
    /// Invalidates the packed cache of every branch containing the cell when the value changes.
    /// </summary>
    /// <returns><c>true</c> if the value actually changed; otherwise <c>false</c>.</returns>
    public bool Set(int x, int y, int value)
    {
        ValidateCoords(x, y);
        return _root.Set(x, y, value, _structureLock);
    }

    /// <summary>
    /// Returns the cached, packed contents of an aligned square window in
    /// <c>index = x * size + y</c> order (coordinates local to the window). The array is cached at
    /// the covering branch and must not be mutated by callers.
    /// </summary>
    public int[] GetPacked(int originX, int originY, int size)
    {
        ValidateWindow(originX, originY, size);
        return _root.GetPackedWindow(originX, originY, size);
    }

    /// <summary>Returns the cached, packed contents of the whole tree.</summary>
    public int[] GetPacked() => GetPacked(0, 0, Size);

    /// <summary>
    /// Enumerates every non-zero cell within an aligned square window as a window-local position
    /// and value. Treats <c>0</c> as empty (air). Uniform empty subtrees are skipped without
    /// per-cell iteration.
    /// </summary>
    public IEnumerable<(Vector2 localPos, int value)> GetAllBlocks(int originX, int originY, int size)
    {
        ValidateWindow(originX, originY, size);
        return _root.GetAllBlocksWindow(originX, originY, size);
    }

    /// <summary>Enumerates every non-zero cell of the whole tree as its position and value.</summary>
    public IEnumerable<(Vector2 localPos, int value)> GetAllBlocks() => GetAllBlocks(0, 0, Size);

    /// <summary>
    /// Resets every cell within an aligned square window to <c>0</c> (air) and drops the window's
    /// subdivisions.
    /// </summary>
    public void Clear(int originX, int originY, int size)
    {
        ValidateWindow(originX, originY, size);
        _root.ClearWindow(originX, originY, size, _structureLock);
    }

    /// <summary>Resets the whole tree to <c>0</c> (air).</summary>
    public void Clear() => Clear(0, 0, Size);

    /// <summary>
    /// A single node of a flat, GPU-friendly serialization of a quadtree window produced by
    /// <see cref="SerializeWindow"/>. Leaf nodes have <see cref="FirstChild"/> &lt; 0 and carry the
    /// uniform cell value in <see cref="Value"/>. Internal nodes have <see cref="FirstChild"/> &gt;= 0
    /// pointing at the first of their four children, which are stored contiguously in quadrant order
    /// (<c>index = (x &gt;= half ? 1 : 0) | (y &gt;= half ? 2 : 0)</c>).
    /// </summary>
    public readonly record struct SerializedNode(int FirstChild, uint Value)
    {
        /// <summary>True when this node is a leaf carrying a uniform value.</summary>
        public bool IsLeaf => FirstChild < 0;
    }

    /// <summary>
    /// Serializes an aligned square window of the tree into a flat node array suitable for direct
    /// upload to the GPU. The array is breadth-first ordered so each internal node's four children
    /// occupy contiguous slots at <c>FirstChild + 0..3</c> in quadrant order. Index 0 is the window
    /// root. Uniform subtrees collapse to a single leaf.
    /// </summary>
    public SerializedNode[] SerializeWindow(int originX, int originY, int size)
    {
        ValidateWindow(originX, originY, size);

        // descend to the node that exactly covers the window, or collapse a uniform ancestor.
        Node node = _root;
        int x = originX, y = originY, curSize = Size;
        while (curSize > size)
        {
            Node[]? children = Volatile.Read(ref node._children);
            if (children == null)
            {
                return [new SerializedNode(-1, (uint)node._uniformValue)];
            }

            int half = curSize >> 1;
            int quadrant = (x >= half ? 1 : 0) | (y >= half ? 2 : 0);
            if (x >= half) x -= half;
            if (y >= half) y -= half;
            node = children[quadrant];
            curSize = half;
        }

        return node.SerializeSelf();
    }

    /// <summary>
    /// Serializes an aligned square window of the tree into a flat <see cref="int"/> array suitable
    /// for direct upload to the GPU. Each node occupies two consecutive ints:
    /// <c>[i*2] = firstChild</c> (&lt; 0 for a leaf, otherwise the index of the node's first child),
    /// and <c>[i*2 + 1] = value</c> (the uniform cell value for a leaf; <c>0</c> for an internal node).
    /// The array is breadth-first ordered so each internal node's four children occupy contiguous
    /// node slots at <c>firstChild + 0..3</c> in quadrant order. Node 0 is the window root.
    /// </summary>
    public int[] SerializeWindowToInts(int originX, int originY, int size)
    {
        SerializedNode[] nodes = SerializeWindow(originX, originY, size);
        int[] result = new int[nodes.Length * 2];
        for (int i = 0; i < nodes.Length; i++)
        {
            result[i * 2] = nodes[i].FirstChild;
            result[i * 2 + 1] = (int)nodes[i].Value;
        }

        return result;
    }

    /// <summary>Serializes the whole tree into a flat <see cref="int"/> array; see
    /// <see cref="SerializeWindowToInts(int,int,int)"/> for the layout.</summary>
    public int[] SerializeToInts() => SerializeWindowToInts(0, 0, Size);

    private void ValidateCoords(int x, int y)
    {
        if ((uint)x >= (uint)Size || (uint)y >= (uint)Size)
        {
            throw new ArgumentOutOfRangeException($"Coordinate ({x}, {y}) is out of bounds for a tree of size {Size}.");
        }
    }

    private void ValidateWindow(int originX, int originY, int size)
    {
        if (size <= 0 || (size & (size - 1)) != 0 || size > Size)
        {
            throw new ArgumentException($"Window size {size} must be a positive power of two no larger than {Size}.", nameof(size));
        }

        if ((uint)originX > (uint)(Size - size) || (uint)originY > (uint)(Size - size)
            || originX % size != 0 || originY % size != 0)
        {
            throw new ArgumentOutOfRangeException($"Window origin ({originX}, {originY}) is not aligned/in-bounds for size {size} in a tree of size {Size}.");
        }
    }

    /// <summary>
    /// A single quadtree node. A node is "uniform" (a single value over its whole area, with no
    /// children) until a differing value forces it to subdivide into four children. Branches cache
    /// their packed contents until a contained value changes.
    /// </summary>
    private sealed class Node
    {
        private readonly int _size;

        // Value of every cell while this node is uniform (i.e. while _children is null).
        // Internal so the enclosing IntQuadTree (e.g. SerializeWindow) can read it directly.
        internal int _uniformValue;

        // Children in quadrant order: index = (x >= half ? 1 : 0) | (y >= half ? 2 : 0).
        // Null while the node is uniform. Read/written via Volatile so a concurrent navigator
        // sees either null (uniform) or a fully-constructed array, never a partial one.
        // Internal so the enclosing IntQuadTree (e.g. SerializeWindow) can read it directly.
        internal Node[]? _children;

        // Cached packed contents; null when invalidated by a change. Published via Volatile so the
        // array's element writes are visible to readers that observe a non-null reference.
        private int[]? _packedCache;

        // Cached flat GPU serialization of this subtree (indices relative to this node). Null when
        // invalidated by a change. Position-independent, so it stays valid across frames until the
        // subtree changes. Published via Volatile, same tolerance as _packedCache.
        private SerializedNode[]? _serializedCache;

        internal Node(int size, int value)
        {
            _size = size;
            _uniformValue = value;
        }

        // Drops both derived caches for this node. Called wherever a contained value changes.
        private void InvalidateCaches()
        {
            Volatile.Write(ref _packedCache, null);
            Volatile.Write(ref _serializedCache, null);
        }

        internal int Get(int x, int y)
        {
            Node node = this;
            while (true)
            {
                Node[]? children = Volatile.Read(ref node._children);
                if (children == null)
                {
                    return node._uniformValue;
                }

                int half = node._size >> 1;
                int quadrant = (x >= half ? 1 : 0) | (y >= half ? 2 : 0);
                if (x >= half) x -= half;
                if (y >= half) y -= half;
                node = children[quadrant];
            }
        }

        internal bool Set(int x, int y, int value, object structureLock)
        {
            if (_size == 1)
            {
                if (_uniformValue == value)
                {
                    return false;
                }

                _uniformValue = value;
                InvalidateCaches();
                return true;
            }

            Node[]? children = Volatile.Read(ref _children);
            if (children == null)
            {
                if (_uniformValue == value)
                {
                    return false;
                }

                lock (structureLock)
                {
                    children = Volatile.Read(ref _children);
                    if (children == null)
                    {
                        children = Subdivide();
                    }
                }
            }

            int half = _size >> 1;
            int quadrant = (x >= half ? 1 : 0) | (y >= half ? 2 : 0);
            bool changed = children[quadrant].Set(x >= half ? x - half : x, y >= half ? y - half : y, value, structureLock);

            if (changed)
            {
                InvalidateCaches();
                TryMergeUniformChildren(structureLock);
            }

            return changed;
        }

        // Collapses this branch back into a single uniform "filled" leaf when all four of its children
        // are themselves uniform leaves holding the same value (i.e. the whole area became one block).
        // This is the inverse of <see cref="Subdivide"/> and is what keeps the tree shallow: without it
        // a branch that subdivided once stays subdivided forever, inflating depth and node count long
        // after the area became uniform again. Because Set is recursive, each ancestor calls this after
        // its child changes, so a fill collapses bottom-up and the pruning propagates up the tree.
        //
        // Thread-safety matches the rest of the tree: the structural swap is done under
        // <paramref name="structureLock"/> and the merged value is published before the children are
        // cleared, so a concurrent navigator sees either the full children array or null + the merged
        // value, never a torn node. As with the tree's lock-free leaf writes, it adds no globally
        // consistent snapshot guarantee.
        private void TryMergeUniformChildren(object structureLock)
        {
            // Lock-free pre-check: bail immediately unless every child is already a uniform leaf with
            // the same value. In active areas children are internal, so this exits after one read.
            Node[]? children = Volatile.Read(ref _children);
            if (children == null)
            {
                return;
            }

            int mergedValue = children[0]._uniformValue;
            for (int i = 0; i < 4; i++)
            {
                if (Volatile.Read(ref children[i]._children) != null || children[i]._uniformValue != mergedValue)
                {
                    return;
                }
            }

            lock (structureLock)
            {
                children = Volatile.Read(ref _children);
                if (children == null)
                {
                    return;
                }

                // Re-verify under the lock: a child could have subdivided or changed since the pre-check.
                mergedValue = children[0]._uniformValue;
                for (int i = 0; i < 4; i++)
                {
                    if (Volatile.Read(ref children[i]._children) != null || children[i]._uniformValue != mergedValue)
                    {
                        return;
                    }
                }

                // Publish the merged value before clearing children so an acquiring reader that sees
                // null children also sees the correct uniform value.
                _uniformValue = mergedValue;
                Volatile.Write(ref _children, null);
            }
        }

        internal int[] GetPackedWindow(int x, int y, int size)
        {
            Node node = this;
            while (node._size > size)
            {
                Node[]? children = Volatile.Read(ref node._children);
                if (children == null)
                {
                    int[] uniform = new int[size * size];
                    if (node._uniformValue != 0)
                    {
                        Array.Fill(uniform, node._uniformValue);
                    }

                    return uniform;
                }

                int half = node._size >> 1;
                int quadrant = (x >= half ? 1 : 0) | (y >= half ? 2 : 0);
                if (x >= half) x -= half;
                if (y >= half) y -= half;
                node = children[quadrant];
            }

            return node.GetPackedSelf();
        }

        private int[] GetPackedSelf()
        {
            int[]? cache = Volatile.Read(ref _packedCache);
            if (cache != null)
            {
                return cache;
            }

            int[] packed = new int[_size * _size];

            Node[]? children = Volatile.Read(ref _children);
            if (children == null)
            {
                if (_uniformValue != 0)
                {
                    Array.Fill(packed, _uniformValue);
                }
            }
            else
            {
                int half = _size >> 1;
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    int[] childPacked = children[quadrant].GetPackedSelf();
                    int offsetX = (quadrant & 1) != 0 ? half : 0;
                    int offsetY = (quadrant & 2) != 0 ? half : 0;

                    // childPacked is contiguous along its local y; copy one column-row run at a time.
                    for (int localX = 0; localX < half; localX++)
                    {
                        int destIndex = (offsetX + localX) * _size + offsetY;
                        int sourceIndex = localX * half;
                        Array.Copy(childPacked, sourceIndex, packed, destIndex, half);
                    }
                }
            }

            Volatile.Write(ref _packedCache, packed);
            return packed;
        }

        internal SerializedNode[] SerializeSelf()
        {
            SerializedNode[]? cache = Volatile.Read(ref _serializedCache);
            if (cache != null)
            {
                return cache;
            }

            List<SerializedNode> result = [default];
            Queue<(Node node, int slot)> queue = new();
            queue.Enqueue((this, 0));

            while (queue.Count > 0)
            {
                (Node current, int slot) = queue.Dequeue();
                Node[]? children = Volatile.Read(ref current._children);
                if (children == null)
                {
                    result[slot] = new SerializedNode(-1, (uint)current._uniformValue);
                    continue;
                }

                int firstChild = result.Count;
                result.Add(default);
                result.Add(default);
                result.Add(default);
                result.Add(default);
                result[slot] = new SerializedNode(firstChild, 0);

                for (int o = 0; o < 4; o++)
                {
                    queue.Enqueue((children[o], firstChild + o));
                }
            }

            SerializedNode[] serialized = result.ToArray();
            Volatile.Write(ref _serializedCache, serialized);
            return serialized;
        }

        internal IEnumerable<(Vector2 localPos, int value)> GetAllBlocksWindow(int x, int y, int size)
        {
            Node node = this;
            while (node._size > size)
            {
                Node[]? children = Volatile.Read(ref node._children);
                if (children == null)
                {
                    if (node._uniformValue == 0)
                    {
                        yield break;
                    }

                    for (int localX = 0; localX < size; localX++)
                    {
                        for (int localY = 0; localY < size; localY++)
                        {
                            yield return (new Vector2(localX, localY), node._uniformValue);
                        }
                    }

                    yield break;
                }

                int half = node._size >> 1;
                int quadrant = (x >= half ? 1 : 0) | (y >= half ? 2 : 0);
                if (x >= half) x -= half;
                if (y >= half) y -= half;
                node = children[quadrant];
            }

            foreach ((Vector2 localPos, int value) block in node.GetAllBlocksLocal(0, 0))
            {
                yield return block;
            }
        }

        private IEnumerable<(Vector2 localPos, int value)> GetAllBlocksLocal(int originX, int originY)
        {
            Node[]? children = Volatile.Read(ref _children);
            if (children == null)
            {
                if (_uniformValue == 0)
                {
                    yield break;
                }

                for (int x = 0; x < _size; x++)
                {
                    for (int y = 0; y < _size; y++)
                    {
                        yield return (new Vector2(originX + x, originY + y), _uniformValue);
                    }
                }

                yield break;
            }

            int half = _size >> 1;
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                int childOriginX = originX + ((quadrant & 1) != 0 ? half : 0);
                int childOriginY = originY + ((quadrant & 2) != 0 ? half : 0);
                foreach ((Vector2 localPos, int value) block in children[quadrant].GetAllBlocksLocal(childOriginX, childOriginY))
                {
                    yield return block;
                }
            }
        }

        internal void ClearWindow(int x, int y, int size, object structureLock)
        {
            if (_size == size)
            {
                lock (structureLock)
                {
                    Volatile.Write(ref _children, null);
                    _uniformValue = 0;
                }

                InvalidateCaches();
                return;
            }

            Node[]? children = Volatile.Read(ref _children);
            if (children == null)
            {
                if (_uniformValue == 0)
                {
                    return;
                }

                lock (structureLock)
                {
                    children = Volatile.Read(ref _children);
                    if (children == null)
                    {
                        children = Subdivide();
                    }
                }
            }

            int half = _size >> 1;
            int quadrant = (x >= half ? 1 : 0) | (y >= half ? 2 : 0);
            children[quadrant].ClearWindow(x >= half ? x - half : x, y >= half ? y - half : y, size, structureLock);
            InvalidateCaches();
        }

        private Node[] Subdivide()
        {
            int half = _size >> 1;
            Node[] children = new Node[4];
            int value = _uniformValue;
            for (int i = 0; i < 4; i++)
            {
                children[i] = new Node(half, value);
            }

            Volatile.Write(ref _children, children);
            return children;
        }
    }
}
