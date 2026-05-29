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

        foreach ((Vector2 regionPos, TickableWorld.Region? region) in world.Regions)
        {
            if (region is null)
            {
                continue;
            }

            if (visibleRegionStart is Vector2 start && visibleRegionEnd is Vector2 end)
            {
                if (regionPos.X < start.X || regionPos.X >= end.X || regionPos.Y < start.Y || regionPos.Y >= end.Y)
                {
                    continue;
                }
            }

            WorldRenderBlock[] blocks = region
                .GetAllBlocks()
                .Select(block => new WorldRenderBlock(block.localPos, block.block))
                .ToArray();

            visibleRegions.Add(new WorldRenderRegion(
                regionPos,
                region.TimeSinceLastChanged.Elapsed.TotalSeconds,
                blocks));
        }

        return new WorldRenderFrame(visibleRegions);
    }

    public static WorldRenderFrame Filter(WorldRenderFrame frame, Vector2 visibleRegionStart, Vector2 visibleRegionEnd)
    {
        List<WorldRenderRegion> visibleRegions = new();

        foreach (WorldRenderRegion region in frame.Regions)
        {
            if (region.Position.X < visibleRegionStart.X || region.Position.X >= visibleRegionEnd.X ||
                region.Position.Y < visibleRegionStart.Y || region.Position.Y >= visibleRegionEnd.Y)
            {
                continue;
            }

            visibleRegions.Add(region);
        }

        return new WorldRenderFrame(visibleRegions);
    }
}
