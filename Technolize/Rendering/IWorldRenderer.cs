using System.Numerics;

namespace Technolize.Rendering;

public interface IWorldRenderer : IDisposable
{
    bool ShowScheduledRegionOverlay { get; set; }
    void UpdateCamera();
    void Draw();
    (Vector2 start, Vector2 end) GetVisibleWorldBounds();
    Vector2 GetMouseWorldPosition();
}