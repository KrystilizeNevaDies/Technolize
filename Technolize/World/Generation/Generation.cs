using System.Numerics;
using System.Runtime.CompilerServices;
using Technolize.Utils;
namespace Technolize.World.Generation;

public static class Generation {

    private static readonly ConditionalWeakTable<TickableWorld, Dictionary<Vector2, TickableWorld.Region>> World2PreloadedRegions = new ();

    public static void Generate(TickableWorld world, IGenerator generator, Vector2 regionPos) {
        // get or create the region at the specified position
        long regionKey = TickableWorld.PackRegionKey(regionPos);
        if (!world.Regions.TryGetValue(regionKey, out TickableWorld.Region? region))
        {
            region = new (world, regionPos);
            world.Regions[regionKey] = region;
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
        Coords.WorldToLocal(pos, out int localX1, out int localY1);
        (int localX, int localY) = (localX: localX1, localY: localY1);

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
        Coords.WorldToRegionCoords(pos, out int regionX, out int regionY, out int localX, out int localY);
        Vector2 globalRegionPos = new (regionX, regionY);

        // If the region is not preloaded, we need to create it
        if (!preloadedRegions.TryGetValue(globalRegionPos, out TickableWorld.Region? preloadedRegion)) {
            preloadedRegion = new TickableWorld.Region(world, globalRegionPos);
            preloadedRegions[globalRegionPos] = preloadedRegion;
        }

        // Set the block in the preloaded region
        preloadedRegion.SetBlock(localX, localY, blockId);
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
