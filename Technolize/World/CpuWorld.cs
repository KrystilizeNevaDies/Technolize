using System.Collections.Frozen;
using Technolize.World.Block;
namespace Technolize.World;

using System;
using System.Collections.Generic;
using System.Numerics;

/// <summary>
/// A CPU-based IWorld implementation for testing and development.
/// Stores world data in memory using a dictionary of chunked arrays.
/// </summary>
public class CpuWorld : IWorld {
    public static readonly int RegionSize = Vector<uint>.Count * 2;
    public readonly Dictionary<Vector2, Region?> Regions = new();
    private ISet<Vector2> _needsTick = new HashSet<Vector2>();

    /// <summary>
    /// A single, fixed-size chunk of the world holding block data in a 2D array.
    /// </summary>
    public class Region(CpuWorld world, Vector2 position)
    {
        public readonly uint[,] Blocks = new uint[RegionSize, RegionSize];
        internal bool NeedsTick;
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
            if (!NeedsTick) {
                world.ProcessUpdate(position);
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
            NeedsTick = false;
            WasChangedLastTick = false;
        }
        public void SwapBlocks(int posAx, int posAy, int posBx, int posBy) {
            if (posAx < 0 || posAx >= RegionSize || posAy < 0 || posAy >= RegionSize ||
                posBx < 0 || posBx >= RegionSize || posBy < 0 || posBy >= RegionSize)
            {
                throw new ArgumentOutOfRangeException("Position out of bounds for region.");
            }

            if (!NeedsTick) {
                world.ProcessUpdate(position);
            }
            WasChangedLastTick = true;

            (Blocks[posAx, posAy], Blocks[posBx, posBy]) = (Blocks[posBx, posBy], Blocks[posAx, posAy]);
        }
    }

    private static (Vector2 regionPos, Vector2 localPos) WorldToRegionCoords(Vector2 worldPos)
    {
        int regionX = (int)Math.Floor(worldPos.X / RegionSize);
        int regionY = (int)Math.Floor(worldPos.Y / RegionSize);

        int localX = (int)worldPos.X % RegionSize;
        if (localX < 0) localX += RegionSize;

        int localY = (int)worldPos.Y % RegionSize;
        if (localY < 0) localY += RegionSize;

        return (new (regionX, regionY), new (localX, localY));
    }

    public long GetBlock(Vector2 position)
    {
        (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

        return Regions.TryGetValue(regionPos, out Region? region) ? region!.GetBlock((int)localPos.X, (int)localPos.Y) : Blocks.Air.Id;

    }

    public void SetBlock(Vector2 position, long block)
    {
        (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

        if (block == Blocks.Air.Id && !Regions.ContainsKey(regionPos))
        {
            return;
        }

        if (!Regions.TryGetValue(regionPos, out Region? region))
        {
            region = new (this, regionPos);
            Regions[regionPos] = region;
        }

        region.SetBlock((int)localPos.X, (int)localPos.Y, (uint) block);
    }

    private int Mod(int value, int modulus)
    {
        return (value % modulus + modulus) % modulus;
    }

    public void SwapBlocks(Vector2 posA, Vector2 posB)
    {
        if (posA.GetRegion() == posB.GetRegion())
        {
            if (Regions.TryGetValue(posA.GetRegion(), out Region? region)) {
                region!.SwapBlocks(
                    Mod((int)posA.X, RegionSize), Mod((int)posA.Y, RegionSize),
                    Mod((int)posB.X, RegionSize), Mod((int)posB.Y, RegionSize)
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
            (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

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
            if (!Regions.TryGetValue(regionPos, out Region? region))
            {
                region = new (this, regionPos);
                Regions[regionPos] = region;
            }

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
                        region.NeedsTick = true;
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
                    region!.NeedsTick = false;
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
        int regionX = (int)Math.Floor(position.X / CpuWorld.RegionSize);
        int regionY = (int)Math.Floor(position.Y / CpuWorld.RegionSize);
        return new (regionX, regionY);
    }
}
