using System.Collections.Frozen;
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
    public static readonly int RegionSize = Vector<uint>.Count * 2;
    internal readonly Dictionary<Vector2, Region?> Regions = new();
    private HashSet<Vector2> _needsTick = [];

    public IGenerator Generator { get; set; } = new FloorGenerator();

    /// <summary>
    /// A single, fixed-size chunk of the world holding block data in a 2D array.
    /// </summary>
    public class Region
    {

        private readonly TickableWorld tickableWorld;
        private readonly Vector2 position;

        public Region(TickableWorld tickableWorld, Vector2 position)
        {
            this.tickableWorld = tickableWorld;
            this.position = position;
        }

        public readonly uint[,] Blocks = new uint[RegionSize, RegionSize];
        internal bool TickAlreadyScheduled;
        internal bool WasChangedLastTick;

        public bool IsEmpty {
            get {
                for (int y = 0; y < RegionSize; y++)
                {
                    for (int x = 0; x < RegionSize; x++)
                    {
                        if (Blocks[x, y] != Block.Blocks.Air.Id)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        public uint GetBlock(int x, int y)
        {
            return Blocks[x, y];
        }

        public void SetBlock(int x, int y, uint block)
        {
            if (!TickAlreadyScheduled) {
                tickableWorld.ProcessUpdate(position);
            }
            WasChangedLastTick = true;
            Blocks[x, y] = block;
        }

        public IEnumerable<(Vector2 localPos, uint block)> GetAllBlocks()
        {
            for (int y = 0; y < RegionSize; y++)
            {
                for (int x = 0; x < RegionSize; x++)
                {
                    if (Blocks[x, y] != Block.Blocks.Air.Id)
                    {
                        yield return (new (x, y), Blocks[x, y]);
                    }
                }
            }
        }

        public void Clear() {
            for (int y = 0; y < RegionSize; y++)
            {
                for (int x = 0; x < RegionSize; x++)
                {
                    Blocks[x, y] = Block.Blocks.Air.Id;
                }
            }
            TickAlreadyScheduled = false;
            WasChangedLastTick = false;
        }

        public void SwapBlocks(int posAx, int posAy, int posBx, int posBy) {
            if (posAx < 0 || posAx >= RegionSize || posAy < 0 || posAy >= RegionSize ||
                posBx < 0 || posBx >= RegionSize || posBy < 0 || posBy >= RegionSize)
            {
                throw new ArgumentOutOfRangeException("Position out of bounds for region.");
            }

            if (!TickAlreadyScheduled) {
                tickableWorld.ProcessUpdate(position);
            }
            WasChangedLastTick = true;

            (Blocks[posAx, posAy], Blocks[posBx, posBy]) = (Blocks[posBx, posBy], Blocks[posAx, posAy]);
        }
    }

    public long GetBlock(Vector2 position)
    {
        (Vector2 regionPos, Vector2 localPos) = Coords.WorldToRegionCoords(position);

        Region region = GetRegion(regionPos);

        return region.GetBlock((int) localPos.X, (int) localPos.Y);

    }

    public void SetBlock(Vector2 position, long block)
    {
        (Vector2 regionPos, Vector2 localPos) = Coords.WorldToRegionCoords(position);

        if (block == Blocks.Air.Id && !Regions.ContainsKey(regionPos))
        {
            return;
        }

        Region region = GetRegion(regionPos);

        region.SetBlock((int)localPos.X, (int)localPos.Y, (uint) block);
    }

    public Region GetRegion(Vector2 regionPos) {
        if (Regions.TryGetValue(regionPos, out Region? region)) return region!;

        // create a new region and generate it
        region = new (this, regionPos);
        Regions[regionPos] = region;
        Generation.Generation.Generate(this, Generator, regionPos);
        return region;
    }

    public void SwapBlocks(Vector2 posA, Vector2 posB)
    {
        if (posA.GetRegion() == posB.GetRegion())
        {
            if (Regions.TryGetValue(posA.GetRegion(), out Region? region)) {
                (int localPosAx, int localPosAy) = Coords.WorldToLocal(posA);
                (int localPosBx, int localPosBy) = Coords.WorldToLocal(posB);
                region!.SwapBlocks(
                    localPosAx, localPosAy,
                    localPosBx, localPosBy
                );
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
        List<Vector2> regionPositions = new (Regions.Keys);

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

            if (!Regions.TryGetValue(regionPos, out Region? region)) continue;

            foreach ((Vector2 localPos, long block) in region.GetAllBlocks())
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
            (Vector2 regionPos, Vector2 localPos) = Coords.WorldToRegionCoords(position);

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
            Region region = GetRegion(regionPos);

            foreach ((Vector2 localPos, long block) in placements)
            {
                region.SetBlock((int)localPos.X, (int)localPos.Y, (uint) block);
            }
        }
    }

    private void ProcessUpdate(Vector2 regionPos) {
        lock (_needsTick) {
            // for each neighbor, add to NeedsTick
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++) {
                    Vector2 neighborPos = new (regionPos.X + dx, regionPos.Y + dy);
                    if (Regions.TryGetValue(neighborPos, out Region? region)) {
                        region.TickAlreadyScheduled = true;
                        _needsTick.Add(neighborPos);
                    }
                }
            }
        }
    }

    public ISet<Vector2> UseNeedsTick()
    {
        lock (_needsTick)
        {
            ISet<Vector2> result = _needsTick.ToFrozenSet();
            _needsTick = new HashSet<Vector2>();

            // reset all regions that need ticking
            foreach (Vector2 regionPos in result) {
                if (Regions.TryGetValue(regionPos, out Region? region))
                {
                    region!.TickAlreadyScheduled = false;
                    region.WasChangedLastTick = false;
                }
            }

            return result;
        }
    }

    public void Unload()
    {
        Regions.Clear();
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
