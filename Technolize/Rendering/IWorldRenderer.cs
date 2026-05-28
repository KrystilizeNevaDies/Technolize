using System.Numerics;

namespace Technolize.Rendering;

public interface IWorldRenderer : IDisposable
{
    void UpdateCamera();
    void Draw();
    (Vector2 start, Vector2 end) GetVisibleWorldBounds();
    Vector2 GetMouseWorldPosition();
}