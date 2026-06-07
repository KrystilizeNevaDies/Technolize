using System.Numerics;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A 2D pan/zoom camera using simple screen↔world conventions. Rotation is intentionally omitted: the
/// application never rotates the world camera.
///
/// Conventions:
/// <list type="bullet">
///   <item><description><c>screen = (world - Target) * Zoom + Offset</c></description></item>
///   <item><description><c>world  = (screen - Offset) / Zoom + Target</c></description></item>
/// </list>
/// </summary>
public struct Camera2D
{
    /// <summary>World-space point that maps to <see cref="Offset"/> on screen.</summary>
    public Vector2 Target;

    /// <summary>Screen-space point (usually the viewport centre) that <see cref="Target"/> maps to.</summary>
    public Vector2 Offset;

    /// <summary>Uniform zoom factor; 1.0 means one world pixel per screen pixel.</summary>
    public float Zoom;

    public Camera2D(Vector2 target, Vector2 offset, float zoom)
    {
        Target = target;
        Offset = offset;
        Zoom = zoom;
    }

    /// <summary>
    /// Creates a camera centred on the viewport at zoom 1, matching the default <c>WorldShaderRenderer</c>
    /// camera (<c>Target = Offset = (width/2, height/2)</c>), which renders world pixels 1:1 to screen.
    /// </summary>
    public static Camera2D Centered(int viewportWidth, int viewportHeight)
    {
        Vector2 centre = new(viewportWidth / 2f, viewportHeight / 2f);
        return new Camera2D(centre, centre, 1.0f);
    }

    /// <summary>Projects a world-space point to screen-space pixels.</summary>
    public readonly Vector2 WorldToScreen(Vector2 world) => ((world - Target) * Zoom) + Offset;

    /// <summary>Unprojects a screen-space pixel back to world-space.</summary>
    public readonly Vector2 ScreenToWorld(Vector2 screen) => ((screen - Offset) / Zoom) + Target;

    /// <summary>
    /// Pans by a screen-space drag delta (left-drag pan: <c>Target -= delta / Zoom</c>). The world point
    /// under the cursor stays under the cursor.
    /// </summary>
    public void Pan(Vector2 screenDelta)
    {
        Target -= screenDelta / Zoom;
    }

    /// <summary>
    /// Zooms about a screen-space anchor (typically the cursor) by a mouse-wheel step: re-anchor
    /// <see cref="Offset"/> to the cursor and <see cref="Target"/> to the world point under it, then scale
    /// by 1.1 per wheel notch and clamp. Zooming keeps the world point under the anchor fixed on screen.
    /// </summary>
    public void ZoomAt(Vector2 screenAnchor, float wheelMove, float minZoom = 0.01f, float maxZoom = 24.0f)
    {
        if (wheelMove == 0f)
        {
            return;
        }

        Vector2 worldUnderAnchor = ScreenToWorld(screenAnchor);
        Offset = screenAnchor;
        Target = worldUnderAnchor;

        const float zoomStep = 1.1f;
        Zoom *= wheelMove > 0f ? zoomStep : 1f / zoomStep;
        Zoom = Math.Clamp(Zoom, minZoom, maxZoom);
    }
}
