using System.Numerics;
using Technolize.World;

namespace Technolize.Rendering;

public sealed class TickableWorldRenderSource(TickableWorld world) : IWorldRenderSource
{
    public WorldRenderFrame CaptureFrame(Vector2 visibleRegionStart, Vector2 visibleRegionEnd)
    {
        return WorldRenderFrameBuilder.FromWorld(world, visibleRegionStart, visibleRegionEnd);
    }
}