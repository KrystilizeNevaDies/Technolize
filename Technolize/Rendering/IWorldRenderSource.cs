using System.Numerics;

namespace Technolize.Rendering;

public interface IWorldRenderSource
{
    WorldRenderFrame CaptureFrame(Vector2 visibleRegionStart, Vector2 visibleRegionEnd);
}