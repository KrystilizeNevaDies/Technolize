using Technolize.World.Block;
namespace Technolize.World;

using System;
using System.Collections.Generic;
using System.Numerics;

/// <summary>
/// A CPU-based IWorld implementation for testing and development.
/// Stores world data in memory using a dictionary of chunked arrays.
/// </summary>
public class CpuWorld : IWorld
{
    public const int RegionSize = 512;
    private readonly Dictionary<Vector2, Region> _regions = new();

    /// <summary>
    /// A single, fixed-size chunk of the world holding block data in a 2D array.
    /// </summary>
    private class Region
    {
        private readonly long[,] _blocks = new long[RegionSize, RegionSize];

        public long GetBlock(int x, int y)
        {
            return _blocks[x, y];
        }

        public void SetBlock(int x, int y, long block)
        {
            _blocks[x, y] = block;
        }

        public IEnumerable<(Vector2 localPos, long block)> GetAllBlocks()
        {
            for (int y = 0; y < RegionSize; y++)
            {
                for (int x = 0; x < RegionSize; x++)
                {
                    if (_blocks[x, y] != Blocks.Air.Id)
                    {
                        yield return (new Vector2(x, y), _blocks[x, y]);
                    }
                }
            }
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

        return (new Vector2(regionX, regionY), new Vector2(localX, localY));
    }

    public long GetBlock(Vector2 position)
    {
        (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

        return _regions.TryGetValue(regionPos, out Region? region) ? region.GetBlock((int)localPos.X, (int)localPos.Y) : Blocks.Air.Id;

    }

    public void SetBlock(Vector2 position, long block)
    {
        (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

        if (block == Blocks.Air.Id && !_regions.ContainsKey(regionPos))
        {
            return;
        }

        if (!_regions.TryGetValue(regionPos, out Region? region))
        {
            region = new Region();
            _regions[regionPos] = region;
        }

        region.SetBlock((int)localPos.X, (int)localPos.Y, block);
    }

    public void SwapBlocks(Vector2 posA, Vector2 posB)
    {
        long blockA = GetBlock(posA);
        long blockB = GetBlock(posB);
        SetBlock(posA, blockB);
        SetBlock(posB, blockA);
    }

    public IEnumerable<(Vector2 Position, long Block)> GetBlocks(Vector2? min, Vector2? max)
    {
        List<Vector2> regionPositions = new List<Vector2>(_regions.Keys);

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

            if (!_regions.TryGetValue(regionPos, out Region? region)) continue;

            foreach ((Vector2 localPos, long block) in region.GetAllBlocks())
            {
                Vector2 worldPos = new Vector2(regionMinX + localPos.X, regionMinY + localPos.Y);

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
        BatchPlacer placer = new BatchPlacer();
        blockPlacerConsumer(placer);

        foreach ((Vector2 regionPos, List<(Vector2 localPos, long block)> placements) in placer.PendingBlocks)
        {
            if (!_regions.TryGetValue(regionPos, out Region? region))
            {
                region = new Region();
                _regions[regionPos] = region;
            }

            foreach ((Vector2 localPos, long block) in placements)
            {
                region.SetBlock((int)localPos.X, (int)localPos.Y, block);
            }
        }
    }

    public void Unload()
    {
        _regions.Clear();
    }
}
