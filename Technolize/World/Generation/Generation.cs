using System.Numerics;
using System.Runtime.CompilerServices;
using Technolize.Utils;
namespace Technolize.World.Generation;

public static class Generation {

    private static readonly ConditionalWeakTable<TickableWorld, Dictionary<Vector2, Dictionary<Vector2, uint>>> World2PreloadedRegions = new ();

    public static void Generate(TickableWorld world, IGenerator generator, Vector2 regionPos) {
        // get or create the scheduling state for the region at the specified position
        long regionKey = TickableWorld.PackRegionKey(regionPos);
        if (!world.Regions.TryGetValue(regionKey, out TickableWorld.RegionTickState? state))
        {
            state = new TickableWorld.RegionTickState();
            world.Regions[regionKey] = state;
        }

        // create a preloaded regions dictionary if it doesn't exist
        if (!World2PreloadedRegions.TryGetValue(world, out Dictionary<Vector2, Dictionary<Vector2, uint>>? preloadedRegions))
        {
            preloadedRegions = new ();
            World2PreloadedRegions.Add(world, preloadedRegions);
        }

        // we don't want the generated region to be ticked by default, so pretend that we already flagged it for ticking
        state.TickAlreadyScheduled = true;

        WorldUnit unit = new (world, regionPos, state, preloadedRegions);
        generator.Generate(unit);
        unit.Apply();

        state.TickAlreadyScheduled = false;
        state.TimeSinceLastChanged.Restart();
    }
}

class WorldUnit(TickableWorld world, Vector2 regionPos, TickableWorld.RegionTickState state, Dictionary<Vector2, Dictionary<Vector2, uint>> preloadedRegions) : IUnit {

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
        world.SetRegionBlock(regionPos, state, localX, localY, blockId);
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

        if (globalRegionPos == regionPos) {
            // writes destined for the region currently being generated can be applied immediately
            world.SetRegionBlock(regionPos, state, localX, localY, blockId);
            return;
        }

        // defer cross-region writes into a standalone scratch buffer so they do not touch the live
        // world tree until that neighbour region is itself generated
        if (!preloadedRegions.TryGetValue(globalRegionPos, out Dictionary<Vector2, uint>? preloadedRegion)) {
            preloadedRegion = new ();
            preloadedRegions[globalRegionPos] = preloadedRegion;
        }

        preloadedRegion[new Vector2(localX, localY)] = blockId;
    }

    public void Apply() {
        if (preloadedRegions.TryGetValue(regionPos, out Dictionary<Vector2, uint>? preloadedRegion)) {
            // apply blocks deferred here while neighbouring regions were generated
            foreach ((Vector2 pos, uint blockId) in preloadedRegion) {
                world.SetRegionBlock(regionPos, state, (int) pos.X, (int) pos.Y, blockId);
            }
            preloadedRegions.Remove(regionPos);
        }
    }
}
