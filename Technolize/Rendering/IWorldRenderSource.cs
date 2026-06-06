using System.Numerics;

namespace Technolize.Rendering;

public interface IWorldRenderSource
{
    WorldRenderFrame CaptureFrame(Vector2 visibleRegionStart, Vector2 visibleRegionEnd);

    /// <summary>
    /// Captures a frame containing every loaded region in the world, with no visible-window filtering.
    /// Used by renderers that must upload the entire world (e.g. the full world quadtree) rather than
    /// only what is on screen.
    /// </summary>
    WorldRenderFrame CaptureWorldFrame();
}