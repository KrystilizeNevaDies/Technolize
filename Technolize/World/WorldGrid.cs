using System.Numerics;
using Technolize.World.Block;
using Technolize.World.Particle;
using Technolize.World.Tag;
namespace Technolize.World;

public class WorldGrid
{
    // Defines the size of each region.
    public const int RegionSize = 8;

    // The main storage for the world, mapping region coordinates to region data.
    private readonly Dictionary<Vector2, Region> _regions = new();
    public readonly ISet<Vector2> NeedsTicking = new HashSet<Vector2>();

    /// <summary>
    /// A private class representing a chunk of the world.
    /// It contains dense arrays for block and tag data.
    /// </summary>
    private class Region
    {
        public const int Size = RegionSize;
        public const int BlockCount = Size * Size;

        // --- Data Arrays ---
        private readonly long[] _blocks = new long[BlockCount];
        private readonly ITagged?[] _blockTags = new ITagged?[BlockCount];

        public Region()
        {
            // Pre-fill the region with default states.
            for (var i = 0; i < BlockCount; i++)
            {
                _blocks[i] = Blocks.Air.Id;
            }
        }

        public Region(Region other)
        {
            // Copy data from another region.
            Array.Copy(other._blocks, _blocks, BlockCount);
            Array.Copy(other._blockTags, _blockTags, BlockCount);
        }

        // Block accessors
        public long GetBlockId(int index) => _blocks[index];
        public void SetBlockId(int index, long id) => _blocks[index] = id;

        // Tag accessors
        public ITagged? GetTags(int index) => _blockTags[index];
        public void SetTags(int index, ITagged? tags) => _blockTags[index] = tags;
    }

    /// <summary>
    /// Gets the BlockInfo for a specific world coordinate.
    /// </summary>
    public BlockInfo GetBlock(Vector2 position)
    {
        var regionCoords = WorldToRegionCoords(position);

        if (_regions.TryGetValue(regionCoords, out var region))
        {
            var localIndex = WorldToLocalIndex(position);
            var blockId = region.GetBlockId(localIndex);
            return BlockRegistry.GetInfo((int) blockId);
        }

        // If the region doesn't exist, it's all Air.
        return Blocks.Air;
    }

    private void NeedsTick(Vector2 pos)
    {
        // add all the surrounding blocks as well
        NeedsTicking.Add(pos);

        NeedsTicking.Add(pos with { X = pos.X + 1 });
        NeedsTicking.Add(pos with { X = pos.X - 1 });
        NeedsTicking.Add(pos with { Y = pos.Y + 1 });
        NeedsTicking.Add(pos with { Y = pos.Y - 1 });
        NeedsTicking.Add(pos with { X = pos.X + 1, Y = pos.Y + 1 });
        NeedsTicking.Add(pos with { X = pos.X - 1, Y = pos.Y + 1 });
        NeedsTicking.Add(pos with { X = pos.X + 1, Y = pos.Y - 1 });
        NeedsTicking.Add(pos with { X = pos.X - 1, Y = pos.Y - 1 });
    }

    public void SetBlock(Vector2 position, BlockInfo blockInfo, ITagged? tags = null)
    {
        // If we are setting a block to Air in a region that doesn't exist, do nothing.
        var regionCoords = WorldToRegionCoords(position);
        if (blockInfo.Id == Blocks.Air.Id && !_regions.ContainsKey(regionCoords))
        {
            return;
        }

        // Needs to be ticked
        NeedsTick(position);

        // Get or create the region.
        if (!_regions.TryGetValue(regionCoords, out var region))
        {
            region = new Region();
            _regions[regionCoords] = region;
        }

        var localIndex = WorldToLocalIndex(position);
        region.SetBlockId(localIndex, blockInfo.Id);
        region.SetTags(localIndex, tags);
    }

    /// <summary>
    /// Gets the instance-specific tags for a block at a given coordinate.
    /// </summary>
    public ITagged GetTags(Vector2 position)
    {
        var regionCoords = WorldToRegionCoords(position);

        if (_regions.TryGetValue(regionCoords, out var region))
        {
            var localIndex = WorldToLocalIndex(position);
            return region.GetTags(localIndex) ?? ITagged.Empty;
        }

        return ITagged.Empty;
    }

    /// <summary>
    /// Sets the instance-specific tags for a block at a given coordinate.
    /// </summary>
    public void SetTags(Vector2 position, ITagged tags)
    {
        // Needs ticking
        NeedsTick(position);

        var regionCoords = WorldToRegionCoords(position);
        if (!_regions.TryGetValue(regionCoords, out var region))
        {
            return;
        }
        var localIndex = WorldToLocalIndex(position);
        // Only set tags if the block is not Air.
        if (region.GetBlockId(localIndex) != Blocks.Air.Id)
        {
            region.SetTags(localIndex, tags);
        }
    }

    /// <summary>
    /// Converts a world block coordinate to the coordinate of the region it belongs to.
    /// </summary>
    private Vector2 WorldToRegionCoords(Vector2 worldPos)
    {
        var regionX = (int)Math.Floor(worldPos.X / RegionSize);
        var regionY = (int)Math.Floor(worldPos.Y / RegionSize);
        return new Vector2(regionX, regionY);
    }

    /// <summary>
    /// Converts a world block coordinate to a 1D index within its local region array.
    /// </summary>
    private int WorldToLocalIndex(Vector2 worldPos)
    {
        // Use the modulo operator to get the local X/Y, ensuring it's positive.
        var localX = (int)worldPos.X % RegionSize;
        if (localX < 0) localX += RegionSize;

        var localY = (int)worldPos.Y % RegionSize;
        if (localY < 0) localY += RegionSize;

        return localY * RegionSize + localX;
    }

    /// <summary>
    /// Generates a simple, predictable world for development and testing purposes.
    /// The exact same logic as before, now populating regions automatically.
    /// </summary>
    public void GenerateDevWorld(int width = 1600, int height = 100)
    {
        var stoneLevel = height / 3;
        var sandLevel = stoneLevel + 8;

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var position = new Vector2(x, y);
                if (y < stoneLevel)
                {
                    SetBlock(position, Blocks.Stone);
                }
                else if (y < sandLevel)
                {
                    SetBlock(position, Blocks.Sand);
                }
            }
        }

        var poolStartX = width / 2;
        var poolWidth = width / 4;
        for (var x = poolStartX; x < poolStartX + poolWidth; x++)
        {
            for (var y = stoneLevel; y < sandLevel; y++)
            {
                SetBlock(new Vector2(x, y), Blocks.Water);
            }
        }
    }

    public void SwapBlocks(Vector2 position, Vector2 swapSlot)
    {
        var block1 = GetBlock(position);
        var tags1 = GetTags(position);
        var block2 = GetBlock(swapSlot);
        var tags2 = GetTags(swapSlot);

        SetBlock(position, block2);
        SetTags(position, tags2);
        SetBlock(swapSlot, block1);
        SetTags(swapSlot, tags1);
    }

    /// <summary>
    /// Queries all non-air blocks within a specified Axis-Aligned Bounding Box (AABB).
    /// This is an efficient iterator that only checks regions overlapped by the bounds.
    /// </summary>
    /// <param name="minBounds">The minimum corner (bottom-left) of the bounding box in world coordinates.</param>
    /// <param name="maxBounds">The maximum corner (top-right) of the bounding box in world coordinates.</param>
    /// <returns>An IEnumerable that yields a tuple for each non-air block found.</returns>
    public IEnumerable<(int x, int y, long blockId)> GetBlocksForRendering(Vector2 minBounds, Vector2 maxBounds)
    {
        var startRegionCoords = WorldToRegionCoords(minBounds);
        var endRegionCoords = WorldToRegionCoords(maxBounds);

        for (var ry = (int)startRegionCoords.Y; ry <= (int)endRegionCoords.Y; ry++)
        {
            for (var rx = (int)startRegionCoords.X; rx <= (int)endRegionCoords.X; rx++)
            {
                var regionCoords = new Vector2(rx, ry);
                if (!_regions.TryGetValue(regionCoords, out var region))
                {
                    continue;
                }

                var startX = Math.Max(minBounds.X, regionCoords.X * RegionSize);
                var startY = Math.Max(minBounds.Y, regionCoords.Y * RegionSize);

                var endX = Math.Min(maxBounds.X, (regionCoords.X + 1) * RegionSize - 1);
                var endY = Math.Min(maxBounds.Y, (regionCoords.Y + 1) * RegionSize - 1);

                for (var y = (int)Math.Floor(startY); y <= (int)Math.Floor(endY); y++)
                {
                    for (var x = (int)Math.Floor(startX); x <= (int)Math.Floor(endX); x++)
                    {
                        var worldPos = new Vector2(x, y);
                        var localIndex = WorldToLocalIndex(worldPos);

                        var blockId = region.GetBlockId(localIndex);

                        if (blockId == Blocks.Air.Id)
                        {
                            continue;
                        }

                        yield return (x, y, blockId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Iterates through all tickable block positions in the world.
    /// </summary>
    public IEnumerable<(int x, int y, long blockId)> GetTickableBlocks()
    {
        foreach (var pos in NeedsTicking)
        {
            var regionCoords = WorldToRegionCoords(pos);
            if (!_regions.TryGetValue(regionCoords, out var region))
            {
                continue;
            }
            var localIndex = WorldToLocalIndex(pos);
            var blockId = region.GetBlockId(localIndex);

            // If the block is not Air, yield its position and ID.
            if (blockId != Blocks.Air.Id)
            {
                yield return ((int) pos.X, (int) pos.Y, blockId);
            }
        }
    }

    public Vector2 RandomPos(Random random)
    {
        // Get a random region from the existing regions.
        if (_regions.Count == 0) return Vector2.Zero; // No regions available

        var randomRegion = _regions.ElementAt(new Random().Next(_regions.Count));
        var regionCoords = randomRegion.Key;

        // Get a random local index within the region.
        var localIndex = random.Next(Region.BlockCount);

        // Convert the local index to world coordinates.
        var localX = localIndex % Region.Size;
        var localY = localIndex / Region.Size;

        return new Vector2(regionCoords.X * Region.Size + localX, regionCoords.Y * Region.Size + localY);
    }
}
