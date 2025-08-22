using System.Numerics;
using System.Runtime.CompilerServices;
using Technolize.Utils;
namespace Technolize.World.Generation;

public static class Generation {

    private static readonly ConditionalWeakTable<TickableWorld, Dictionary<Vector2, TickableWorld.Region>> World2PreloadedRegions = new ();

    public static void Generate(TickableWorld world, IGenerator generator, Vector2 regionPos) {
        // get or create the region at the specified position
        if (!world.Regions.TryGetValue(regionPos, out TickableWorld.Region? region))
        {
            region = new (world, regionPos);
            world.Regions[regionPos] = region;
        }

        // create a preloaded regions dictionary if it doesn't exist
        if (!World2PreloadedRegions.TryGetValue(world, out Dictionary<Vector2, TickableWorld.Region>? preloadedRegions))
        {
            preloadedRegions = new ();
            World2PreloadedRegions.Add(world, preloadedRegions);
        }

        // we don't want the generated region to be ticked by default, so pretend that we already flagged it for ticking
        region.TickAlreadyScheduled = true;

        WorldUnit unit = new (world, region, regionPos, preloadedRegions);
        generator.Generate(unit);
        unit.Apply();

        region.TickAlreadyScheduled = false;
        region.TimeSinceLastChanged.Restart();
    }
}

class WorldUnit(TickableWorld world, TickableWorld.Region region, Vector2 regionPos, Dictionary<Vector2, TickableWorld.Region> preloadedRegions) : IUnit {

    public Vector2 MinPos {
        get => regionPos * TickableWorld.RegionSize;
    }
    public Vector2 MaxPos {
        get => MinPos + Size;
    }
    public Vector2 Size {
        get => new (TickableWorld.RegionSize, TickableWorld.RegionSize);
    }

    public void Set(Vector2 pos, uint blockId) {
        if (pos.X < MinPos.X || pos.X >= MaxPos.X ||
            pos.Y < MinPos.Y || pos.Y >= MaxPos.Y) {
            // this pos is out of bounds, do nothing
            return;
        }

        // convert global position to local position in the region
        (int localX, int localY) = Coords.WorldToLocal(pos);

        // set the block in the region
        region.SetBlock(localX, localY, blockId);
    }

    public IForkedPlacer Fork(Vector2 pos) {
        return new ForkedPlacer(pos, this);
    }

    private class ForkedPlacer(Vector2 offset, WorldUnit unit) : IForkedPlacer {
        public void Set(Vector2 pos, uint blockId) {
            unit.SetGlobalBlock(pos + offset, blockId);
        }
        public IForkedPlacer Fork(Vector2 pos) {
            return unit.Fork(pos + offset);
        }
        public void Dispose() {
        }
    }

    private void SetGlobalBlock(Vector2 pos, uint blockId) {
        var (regionPos, localPos) = Coords.WorldToRegionCoords(pos);

        // If the region is not preloaded, we need to create it
        if (!preloadedRegions.TryGetValue(regionPos, out TickableWorld.Region? preloadedRegion)) {
            preloadedRegion = new TickableWorld.Region(world, regionPos);
            preloadedRegions[regionPos] = preloadedRegion;
        }

        // Set the block in the preloaded region
        preloadedRegion.SetBlock((int)localPos.X, (int)localPos.Y, blockId);
    }

    public void Apply() {
        if (preloadedRegions.TryGetValue(regionPos, out TickableWorld.Region? preloadedRegion)) {
            // If the region is preloaded, we can apply the blocks
            foreach ((Vector2 pos, uint blockId) in preloadedRegion.GetAllBlocks()) {
                region.SetBlock((int) pos.X, (int) pos.Y, blockId);
            }
            preloadedRegions.Remove(regionPos);
        }
    }
}
