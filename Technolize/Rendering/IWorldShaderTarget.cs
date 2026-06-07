using System.Numerics;
using Technolize.Rendering.Graphics;

namespace Technolize.Rendering;

/// <summary>
/// The on-screen world draw contract shared by the GPU world renderers (<see cref="WorldShaderRenderer"/>
/// and <see cref="WorldColorRenderer"/>). It lets <see cref="WorldRenderer"/> drive either backend
/// through one camera/interaction wrapper, selecting the implementation at construction time.
/// </summary>
public interface IWorldShaderTarget : IDisposable
{
    /// <summary>
    /// Renders the world region through the camera into the currently-bound framebuffer. Implementations
    /// may ignore <paramref name="lighting"/> and <paramref name="drawGrid"/> if they render a simpler
    /// pass (e.g. the colour-only renderer).
    /// </summary>
    void RenderToTarget(
        WorldRenderFrame frame,
        Vector2 worldRegionStart,
        Vector2 worldRegionEnd,
        WorldLighting lighting,
        float time,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight,
        bool drawGrid = false);
}
