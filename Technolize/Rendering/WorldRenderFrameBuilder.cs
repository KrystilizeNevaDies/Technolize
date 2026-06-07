using System.Collections.Frozen;
using System.Numerics;
using Technolize.World;

namespace Technolize.Rendering;

public static class WorldRenderFrameBuilder
{
    public static WorldRenderFrame FromWorld(TickableWorld world)
    {
        return FromWorld(world, null, null);
    }

    public static WorldRenderFrame FromWorld(TickableWorld world, Vector2? visibleRegionStart, Vector2? visibleRegionEnd)
    {
        List<WorldRenderRegion> visibleRegions = new();
        IEnumerable<Vector2> scheduledRegionSource = world.PeekNeedsTick();

        foreach ((long regionKey, TickableWorld.RegionTickState? state) in world.Regions)
        {
            if (state is null)
            {
                continue;
            }

            TickableWorld.UnpackRegionKey(regionKey, out int regionX, out int regionY);
            Vector2 regionPos = new(regionX, regionY);

            if (visibleRegionStart is Vector2 start && visibleRegionEnd is Vector2 end)
            {
                if (regionPos.X < start.X || regionPos.X >= end.X || regionPos.Y < start.Y || regionPos.Y >= end.Y)
                {
                    continue;
                }
            }

            WorldRenderBlock[] blocks = world
                .GetRegionBlocks(regionPos)
                .Select(block => new WorldRenderBlock(block.localPos, block.block))
                .ToArray();

            visibleRegions.Add(new WorldRenderRegion(
                regionPos,
                state.TimeSinceLastChanged.Elapsed.TotalSeconds,
                blocks));
        }

            FrozenSet<Vector2> scheduledRegions = scheduledRegionSource
                .Where(regionPos =>
                    visibleRegionStart is not Vector2 start || visibleRegionEnd is not Vector2 end ||
                    (regionPos.X >= start.X && regionPos.X < end.X && regionPos.Y >= start.Y && regionPos.Y < end.Y))
                .ToFrozenSet();

            // Always serialize the ENTIRE world's quadtree as-is. The renderer uploads this whole tree
            // to the GPU; it must never be windowed or rebuilt.
            // Serialize only an aligned power-of-two WINDOW of the global quadtree that covers the
            // visible regions, rather than the entire 2^20 tree. This keeps the GPU tree shallow
            // (depth ~log2(window) instead of ~20) and its coordinates small enough for float32 to
            // resolve sub-cell positions, which is what makes the leaf-jumping march viable.
            (int[] quadtree, int windowOriginX, int windowOriginY, int windowSize) =
                SerializeQuadtreeWindow(world, visibleRegions);

            return new WorldRenderFrame(
                visibleRegions, scheduledRegions, quadtree, windowOriginX, windowOriginY, windowSize);
    }

    /// <summary>
    /// Serializes the smallest aligned power-of-two window of the global quadtree that fully contains
    /// every region in <paramref name="regions"/> (in absolute tree coordinates). Returns the flat node
    /// array plus the window's origin and size. For an empty region set returns an empty array and a
    /// zero window (the "whole world" sentinel the resource builder treats as legacy/air).
    /// </summary>
    private static (int[] nodes, int originX, int originY, int size) SerializeQuadtreeWindow(
        TickableWorld world, List<WorldRenderRegion> regions)
    {
        if (regions.Count == 0)
        {
            return ([], 0, 0, 0);
        }

        int minRegionX = int.MaxValue, minRegionY = int.MaxValue;
        int maxRegionX = int.MinValue, maxRegionY = int.MinValue;
        foreach (WorldRenderRegion region in regions)
        {
            minRegionX = Math.Min(minRegionX, (int)region.Position.X);
            minRegionY = Math.Min(minRegionY, (int)region.Position.Y);
            maxRegionX = Math.Max(maxRegionX, (int)region.Position.X);
            maxRegionY = Math.Max(maxRegionY, (int)region.Position.Y);
        }

        int regionSize = TickableWorld.RegionSize;
        // Absolute tree-coordinate AABB of the visible regions (WorldOffset centres the world).
        int aabbMinX = (minRegionX * regionSize) + TickableWorld.WorldOffset;
        int aabbMinY = (minRegionY * regionSize) + TickableWorld.WorldOffset;
        int width = (maxRegionX - minRegionX + 1) * regionSize;
        int height = (maxRegionY - minRegionY + 1) * regionSize;

        // Smallest aligned power-of-two window containing the AABB. Grow until an aligned window of the
        // current size spans the whole AABB on both axes (the AABB may straddle an alignment boundary).
        int size = NextPowerOfTwo(Math.Max(width, height));
        int originX, originY;
        while (true)
        {
            originX = (aabbMinX / size) * size;
            originY = (aabbMinY / size) * size;
            if (originX + size >= aabbMinX + width && originY + size >= aabbMinY + height)
            {
                break;
            }

            size <<= 1;
        }

        int[] nodes = world.SerializeWindow(originX, originY, size);
        return (nodes, originX, originY, size);
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    public static WorldRenderFrame Filter(WorldRenderFrame frame, Vector2 visibleRegionStart, Vector2 visibleRegionEnd)
    {
        List<WorldRenderRegion> visibleRegions = new();
        HashSet<Vector2> visibleScheduledRegions = new();

        foreach (WorldRenderRegion region in frame.Regions)
        {
            if (region.Position.X < visibleRegionStart.X || region.Position.X >= visibleRegionEnd.X ||
                region.Position.Y < visibleRegionStart.Y || region.Position.Y >= visibleRegionEnd.Y)
            {
                continue;
            }

            visibleRegions.Add(region);
        }

        foreach (Vector2 regionPos in frame.ScheduledRegions)
        {
            if (regionPos.X < visibleRegionStart.X || regionPos.X >= visibleRegionEnd.X ||
                regionPos.Y < visibleRegionStart.Y || regionPos.Y >= visibleRegionEnd.Y)
            {
                continue;
            }

            visibleScheduledRegions.Add(regionPos);
        }

        return new WorldRenderFrame(
            visibleRegions, visibleScheduledRegions, frame.WorldQuadtree,
            frame.QuadtreeWindowOriginX, frame.QuadtreeWindowOriginY, frame.QuadtreeWindowSize);
    }
}
