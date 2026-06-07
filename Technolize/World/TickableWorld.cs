using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Diagnostics;
using Technolize.Utils;
using Technolize.World.Block;
using Technolize.World.Generation;
namespace Technolize.World;

using System;
using System.Collections.Generic;
using System.Numerics;

/// <summary>
/// A CPU-based IWorld implementation for testing and development.
/// Stores world data in memory using a dictionary of chunked arrays.
/// </summary>
public class TickableWorld : IWorld {
    public static readonly int RegionSize = Vector<uint>.Count * 4;

    /// <summary>The side length, in cells, of the single quadtree backing the whole world.</summary>
    public const int WorldSize = 1 << 20;

    /// <summary>The cell offset that maps world coordinate 0 to the centre of the quadtree.</summary>
    public const int WorldOffset = WorldSize / 2;

    /// <summary>
    /// The single quadtree holding every block in the world. All regions are lightweight handles
    /// addressing windows of this tree; there is no per-region block storage.
    /// </summary>
    private readonly IntQuadTree _blocks = new (WorldSize);

    internal readonly ConcurrentDictionary<long, RegionTickState> Regions = new();
    private ConcurrentDictionary<long, bool> _needsTick = [];
    private readonly object _regionCreationLock = new();
    private int _pollutionCount;

    public IGenerator Generator { get; set; } = new FloorGenerator();

    /// <summary>
    /// Per-grid-cell scheduling metadata. The world stores no per-region blocks any more (all blocks
    /// live in the single global quadtree); a region is now purely a fixed-size scheduling bucket,
    /// and this is the only state it carries.
    /// </summary>
    public sealed class RegionTickState {
        internal bool TickAlreadyScheduled;
        internal readonly Stopwatch TimeSinceLastChanged = Stopwatch.StartNew();
    }

    /// <summary>Maps a region coordinate axis to the corresponding global quadtree origin.</summary>
    private static int RegionOrigin(int regionCoord) => regionCoord * RegionSize + WorldOffset;

    public long GetBlock(Vector2 position)
    {
        Coords.WorldToRegionCoords(position, out int regionX, out int regionY, out _, out _);

        // Reading a block lazily generates its region, matching the previous region-handle behaviour.
        EnsureRegion(new Vector2(regionX, regionY));

        return _blocks.Get((int) position.X + WorldOffset, (int) position.Y + WorldOffset);
    }

    public void SetBlock(Vector2 position, long block)
    {
        Coords.WorldToRegionCoords(position, out int regionX, out int regionY, out int localX, out int localY);
        Vector2 regionPos = new (regionX, regionY);
        long regionKey = PackRegionKey(regionPos);

        if (block == Blocks.Air && !Regions.ContainsKey(regionKey))
        {
            return;
        }

        RegionTickState state = EnsureRegion(regionPos);

        if (!_blocks.Set((int) position.X + WorldOffset, (int) position.Y + WorldOffset, (int) block)) return;
        RequireTick(regionPos, state, localX, localY);
        state.TimeSinceLastChanged.Restart();
    }

    /// <summary>
    /// Ensures the region at <paramref name="regionPos"/> exists and has been generated once, then
    /// returns its scheduling state. Generation runs while creation is serialized.
    /// </summary>
    public RegionTickState EnsureRegion(Vector2 regionPos) {
        long regionKey = PackRegionKey(regionPos);
        if (Regions.TryGetValue(regionKey, out RegionTickState? state)) return state;

        lock (_regionCreationLock)
        {
            if (Regions.TryGetValue(regionKey, out state)) return state;

            // create the scheduling state and generate the region once while creation is serialized.
            state = new RegionTickState();
            Regions[regionKey] = state;
            Generation.Generation.Generate(this, Generator, regionPos);
            return state;
        }
    }

    /// <summary>Schedules ticks for a write at the given region-local cell, if not already scheduled.</summary>
    private void RequireTick(Vector2 regionPos, RegionTickState state, int localX, int localY) {
        bool isLocal = localX != 0 && localY != 0 && localX != RegionSize - 1 && localY != RegionSize - 1;
        if (!state.TickAlreadyScheduled) {
            ProcessUpdate(regionPos, isLocal);
        }
    }

    /// <summary>
    /// Schedules ticks for a region-local cell of an existing region. Used by the ticker when a
    /// mutation requires its neighbourhood to be re-evaluated.
    /// </summary>
    internal void RequireRegionTick(Vector2 regionPos, int localX, int localY) {
        if (Regions.TryGetValue(PackRegionKey(regionPos), out RegionTickState? state)) {
            RequireTick(regionPos, state, localX, localY);
        }
    }

    /// <summary>
    /// Writes a block addressed by region coordinate and region-local offset directly into the global
    /// quadtree, scheduling ticks as needed. Used by generation and batch placement.
    /// </summary>
    internal void SetRegionBlock(Vector2 regionPos, RegionTickState state, int localX, int localY, uint block) {
        int treeX = RegionOrigin((int) regionPos.X) + localX;
        int treeY = RegionOrigin((int) regionPos.Y) + localY;
        if (!_blocks.Set(treeX, treeY, (int) block)) return;
        RequireTick(regionPos, state, localX, localY);
        state.TimeSinceLastChanged.Restart();
    }

    /// <summary>Reads a single region-local cell straight from the global quadtree (no generation).</summary>
    internal uint GetRegionBlock(Vector2 regionPos, int localX, int localY) {
        int treeX = RegionOrigin((int) regionPos.X) + localX;
        int treeY = RegionOrigin((int) regionPos.Y) + localY;
        return (uint) _blocks.Get(treeX, treeY);
    }

    /// <summary>
    /// Returns the cached, packed block ids for a region's window in <c>index = x * RegionSize + y</c>
    /// order. The returned array is the live quadtree cache and must not be mutated by callers.
    /// </summary>
    internal int[] GetRegionPacked(Vector2 regionPos)
        => _blocks.GetPacked(RegionOrigin((int) regionPos.X), RegionOrigin((int) regionPos.Y), RegionSize);

    /// <summary>Enumerates every non-air cell of a region's window as a region-local position and id.</summary>
    internal IEnumerable<(Vector2 localPos, uint block)> GetRegionBlocks(Vector2 regionPos)
    {
        foreach ((Vector2 localPos, int value) in _blocks.GetAllBlocks(
                     RegionOrigin((int) regionPos.X), RegionOrigin((int) regionPos.Y), RegionSize))
        {
            yield return (localPos, (uint) value);
        }
    }

    /// <summary>
    /// Serializes a single region's window of the global quadtree into a flat <see cref="int"/> array
    /// (two ints per node: <c>firstChild</c>, <c>value</c>); see
    /// <see cref="IntQuadTree.SerializeWindowToInts(int,int,int)"/> for the layout. Region windows are
    /// always aligned power-of-two windows of the tree, so this reads the live subtree directly.
    /// </summary>
    public int[] SerializeRegion(Vector2 regionPos)
        => _blocks.SerializeWindowToInts(RegionOrigin((int) regionPos.X), RegionOrigin((int) regionPos.Y), RegionSize);

    /// <summary>
    /// Serializes an aligned square window of the global quadtree (in absolute tree coordinates,
    /// <c>[0, WorldSize)</c>) into a flat <see cref="int"/> array; see
    /// <see cref="IntQuadTree.SerializeWindowToInts(int,int,int)"/> for the layout. <paramref name="size"/>
    /// must be a power of two and <paramref name="originX"/>/<paramref name="originY"/> aligned to it.
    /// </summary>
    public int[] SerializeWindow(int originX, int originY, int size)
        => _blocks.SerializeWindowToInts(originX, originY, size);

    /// <summary>Serializes the entire world's global quadtree into a flat <see cref="int"/> array.</summary>
    public int[] SerializeWorld() => _blocks.SerializeToInts();


    public void AddPollution(int amount = 1)
    {
        Interlocked.Add(ref _pollutionCount, amount);
    }

    public int GetPollutionCount()
    {
        return Volatile.Read(ref _pollutionCount);
    }

    public void SwapBlocks(Vector2 posA, Vector2 posB)
    {
        Vector2 regionPos = posA.GetRegion();
        if (regionPos == posB.GetRegion())
        {
            if (Regions.TryGetValue(PackRegionKey(regionPos), out RegionTickState? state)) {
                Coords.WorldToLocal(posA, out int localPosAx, out int localPosAy);
                Coords.WorldToLocal(posB, out int localPosBx, out int localPosBy);

                if (!state.TickAlreadyScheduled) {
                    ProcessUpdate(regionPos);
                }
                state.TimeSinceLastChanged.Restart();

                int treeAx = RegionOrigin((int) regionPos.X) + localPosAx;
                int treeAy = RegionOrigin((int) regionPos.Y) + localPosAy;
                int treeBx = RegionOrigin((int) regionPos.X) + localPosBx;
                int treeBy = RegionOrigin((int) regionPos.Y) + localPosBy;
                int blockA = _blocks.Get(treeAx, treeAy);
                int blockB = _blocks.Get(treeBx, treeBy);
                _blocks.Set(treeAx, treeAy, blockB);
                _blocks.Set(treeBx, treeBy, blockA);
            }
        }
        else
        {
            long blockA = GetBlock(posA);
            long blockB = GetBlock(posB);
            SetBlock(posA, blockB);
            SetBlock(posB, blockA);
        }
    }

    public IEnumerable<(Vector2 Position, long Block)> GetBlocks(Vector2? min, Vector2? max)
    {
        List<Vector2> regionPositions = new(Regions.Count);
        foreach (long regionKey in Regions.Keys)
        {
            UnpackRegionKey(regionKey, out int regionX, out int regionY);
            regionPositions.Add(new Vector2(regionX, regionY));
        }

        foreach (Vector2 regionPos in regionPositions)
        {
            float regionMinX = regionPos.X * RegionSize;
            float regionMinY = regionPos.Y * RegionSize;
            float regionMaxX = regionMinX + RegionSize - 1;
            float regionMaxY = regionMinY + RegionSize - 1;

            if (min.HasValue && (regionMaxX < min.Value.X || regionMaxY < min.Value.Y))
            {
                continue;
            }
            if (max.HasValue && (regionMinX >= max.Value.X || regionMinY >= max.Value.Y))
            {
                continue;
            }

            if (!Regions.ContainsKey(PackRegionKey(regionPos))) continue;

            foreach ((Vector2 localPos, long block) in GetRegionBlocks(regionPos))
            {
                Vector2 worldPos = new (regionMinX + localPos.X, regionMinY + localPos.Y);

                if (min.HasValue && (worldPos.X < min.Value.X || worldPos.Y < min.Value.Y))
                {
                    continue;
                }
                if (max.HasValue && (worldPos.X >= max.Value.X || worldPos.Y >= max.Value.Y))
                {
                    continue;
                }

                yield return (worldPos, block);
            }
        }
    }

    private class BatchPlacer : IBlockPlacer
    {
        public readonly Dictionary<Vector2, List<(Vector2 localPos, long block)>> PendingBlocks = new();

        public void Set(Vector2 position, long block)
        {
            Coords.WorldToRegionCoords(position, out int regionX, out int regionY, out int localX, out int localY);
            Vector2 regionPos = new (regionX, regionY);
            Vector2 localPos = new (localX, localY);

            if (!PendingBlocks.TryGetValue(regionPos, out List<(Vector2 localPos, long block)>? placements))
            {
                placements = [];
                PendingBlocks[regionPos] = placements;
            }

            placements.Add((localPos, block));
        }
    }

    public void BatchSetBlocks(Action<IBlockPlacer> blockPlacerConsumer)
    {
        BatchPlacer placer = new ();
        blockPlacerConsumer(placer);

        foreach ((Vector2 regionPos, List<(Vector2 localPos, long block)> placements) in placer.PendingBlocks)
        {
            RegionTickState state = EnsureRegion(regionPos);

            foreach ((Vector2 localPos, long block) in placements)
            {
                SetRegionBlock(regionPos, state, (int)localPos.X, (int)localPos.Y, (uint) block);
            }
        }
    }

    /// <summary>
    /// Prepares the given region to be ticked next tick.
    /// </summary>
    /// <param name="regionPos">The position of the region to be ticked.</param>
    /// <param name="localOnly">If true, only the specified region will be ticked. If false, all neighboring regions will also be ticked.</param>
    public void ProcessUpdate(Vector2 regionPos, bool localOnly = false) {
        if (localOnly) {
            // only process the region itself
            long regionKey = PackRegionKey(regionPos);
            if (Regions.TryGetValue(regionKey, out RegionTickState? state)) {
                state.TickAlreadyScheduled = true;
                _needsTick[regionKey] = true;
            }
            return;
        }

        // for each neighbor, add to NeedsTick
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++) {
                Vector2 neighborPos = new (regionPos.X + dx, regionPos.Y + dy);
                long neighborKey = PackRegionKey(neighborPos);
                if (Regions.TryGetValue(neighborKey, out RegionTickState? state)) {
                    state.TickAlreadyScheduled = true;
                    _needsTick[neighborKey] = true;
                }
            }
        }
    }

    public FrozenSet<Vector2> UseNeedsTick()
    {
        FrozenDictionary<long, bool> result = _needsTick.ToFrozenDictionary();
        _needsTick = new ConcurrentDictionary<long, bool>();

        // reset all regions that need ticking
        foreach ((long regionKey, bool _) in result) {
            if (Regions.TryGetValue(regionKey, out RegionTickState? state))
            {
                state.TickAlreadyScheduled = false;
            }
        }

        HashSet<Vector2> regionPositions = new();
        foreach (long regionKey in result.Keys)
        {
            UnpackRegionKey(regionKey, out int regionX, out int regionY);
            regionPositions.Add(new Vector2(regionX, regionY));
        }

        return regionPositions.ToFrozenSet();
    }

    public FrozenSet<Vector2> PeekNeedsTick()
    {
        HashSet<Vector2> regionPositions = new();
        foreach (long regionKey in _needsTick.Keys)
        {
            UnpackRegionKey(regionKey, out int regionX, out int regionY);
            regionPositions.Add(new Vector2(regionX, regionY));
        }

        return regionPositions.ToFrozenSet();
    }

    public void Unload()
    {
        Regions.Clear();
        _blocks.Clear();
        Interlocked.Exchange(ref _pollutionCount, 0);
    }

    internal static long PackRegionKey(Vector2 regionPos)
    {
        int x = (int)regionPos.X;
        int y = (int)regionPos.Y;
        return ((long)x << 32) | (uint)y;
    }

    internal static void UnpackRegionKey(long regionKey, out int x, out int y)
    {
        x = (int)(regionKey >> 32);
        y = (int)regionKey;
    }
}

static class Vector2Extensions
{
    public static Vector2 GetRegion(this Vector2 position)
    {
        int regionX = (int)Math.Floor(position.X / TickableWorld.RegionSize);
        int regionY = (int)Math.Floor(position.Y / TickableWorld.RegionSize);
        return new (regionX, regionY);
    }
}
