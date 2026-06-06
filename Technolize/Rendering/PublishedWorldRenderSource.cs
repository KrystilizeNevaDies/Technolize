using System.Numerics;

namespace Technolize.Rendering;

public sealed class PublishedWorldRenderSource : IWorldRenderSource
{
    private WorldRenderFrame _currentFrame = WorldRenderFrame.Empty;

    public void Publish(WorldRenderFrame frame)
    {
        Interlocked.Exchange(ref _currentFrame, frame);
    }

    public WorldRenderFrame CaptureFrame(Vector2 visibleRegionStart, Vector2 visibleRegionEnd)
    {
        WorldRenderFrame frame = Interlocked.CompareExchange(ref _currentFrame, WorldRenderFrame.Empty, WorldRenderFrame.Empty)
            ?? WorldRenderFrame.Empty;

        return WorldRenderFrameBuilder.Filter(frame, visibleRegionStart, visibleRegionEnd);
    }

    public WorldRenderFrame CaptureWorldFrame()
    {
        // The published frame already covers the entire world (FromWorld with no bounds), so return
        // it unfiltered.
        return Interlocked.CompareExchange(ref _currentFrame, WorldRenderFrame.Empty, WorldRenderFrame.Empty)
            ?? WorldRenderFrame.Empty;
    }
}