using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Technolize.Rendering.Graphics;

namespace Technolize.Rendering;

/// <summary>
/// A Silk.NET OpenGL 4.6 implementation of <see cref="IWorldRenderer"/> for the live application, so
/// the gameplay/interaction layer (<c>DevInteractions</c>, the game session loop) renders through the
/// Silk backend. It owns a <see cref="Camera2D"/> driven by Silk input (left-drag pan, wheel zoom
/// about the cursor) and renders the world each frame to the default window framebuffer via
/// <see cref="WorldShaderRenderer.RenderToTarget"/>.
/// </summary>
public sealed class WorldRenderer : IWorldRenderer
{
    private const float BlockSize = 16f;

    private readonly GlContext _context;
    private readonly IWorldRenderSource _renderSource;
    private readonly WorldShaderRenderer _renderer;
    private readonly IMouse? _mouse;

    private Camera2D _camera;
    private Vector2 _lastMousePosition;
    private bool _hasLastMousePosition;
    private float _scrollAccumulator;

    public WorldRenderer(GlContext context, IWorldRenderSource renderSource)
    {
        _context = context;
        _renderSource = renderSource;
        _renderer = new WorldShaderRenderer(context.Gl);

        Vector2D<int> size = context.Window.Size;
        _camera = Camera2D.Centered(size.X, size.Y);

        _mouse = context.Input?.Mice.Count > 0 ? context.Input.Mice[0] : null;
        if (_mouse is not null)
        {
            _mouse.Scroll += (_, wheel) => _scrollAccumulator += wheel.Y;
        }
    }

    public bool ShowScheduledRegionOverlay { get; set; }
    public WorldLighting Lighting { get; set; } = WorldLighting.Default;

    /// <summary>Whether the world grid overlay is drawn.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>When set, overrides wall-clock time for the animated water/sun (deterministic capture).</summary>
    public float? FixedTime { get; set; }

    public void UpdateCamera()
    {
        if (_mouse is null)
        {
            return;
        }

        // ImGui intercepts input when the cursor is over its windows; don't pan/zoom the world then.
        bool imguiWantsMouse = ImGui.GetCurrentContext() != IntPtr.Zero && ImGui.GetIO().WantCaptureMouse;
        Vector2 mousePosition = _mouse.Position;

        if (!imguiWantsMouse && _mouse.IsButtonPressed(MouseButton.Left) && _hasLastMousePosition)
        {
            _camera.Pan(mousePosition - _lastMousePosition);
        }

        if (!imguiWantsMouse && _scrollAccumulator != 0f)
        {
            _camera.ZoomAt(mousePosition, _scrollAccumulator);
        }

        _scrollAccumulator = 0f;
        _lastMousePosition = mousePosition;
        _hasLastMousePosition = true;
    }

    public void Draw()
    {
        Vector2D<int> size = _context.Window.Size;
        int width = Math.Max(1, size.X);
        int height = Math.Max(1, size.Y);

        // Capture the whole loaded world; the camera crops it to the visible viewport.
        WorldRenderFrame frame = _renderSource.CaptureWorldFrame();
        (Vector2 regionStart, Vector2 regionEnd) = ComputeWorldRegionBounds(frame);

        float time = FixedTime ?? (float)_context.Window.Time;

        _context.Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _renderer.RenderToTarget(frame, regionStart, regionEnd, Lighting, time, _camera, width, height, ShowGrid);
    }

    public (Vector2 start, Vector2 end) GetVisibleWorldBounds()
    {
        Vector2D<int> size = _context.Window.Size;
        Vector2 topLeft = _camera.ScreenToWorld(new Vector2(0, 0));
        Vector2 bottomRight = _camera.ScreenToWorld(new Vector2(size.X, size.Y));

        int startX = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X) / BlockSize);
        int endX = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X) / BlockSize);
        // World block Y is the negation of the world-pixel Y divided by the block size.
        int startY = (int)Math.Floor(-Math.Max(topLeft.Y, bottomRight.Y) / BlockSize);
        int endY = (int)Math.Ceiling(-Math.Min(topLeft.Y, bottomRight.Y) / BlockSize);

        return (new Vector2(startX, startY), new Vector2(endX, endY));
    }

    public Vector2 GetMouseWorldPosition()
    {
        Vector2 screen = _mouse?.Position ?? Vector2.Zero;
        Vector2 world = _camera.ScreenToWorld(screen);
        world.Y = -world.Y;
        return world / BlockSize;
    }

    private static (Vector2 start, Vector2 end) ComputeWorldRegionBounds(WorldRenderFrame frame)
    {
        if (frame.Regions.Count == 0)
        {
            return (new Vector2(0, 0), new Vector2(1, 1));
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (WorldRenderRegion region in frame.Regions)
        {
            minX = Math.Min(minX, region.Position.X);
            minY = Math.Min(minY, region.Position.Y);
            maxX = Math.Max(maxX, region.Position.X);
            maxY = Math.Max(maxY, region.Position.Y);
        }

        return (new Vector2(minX, minY), new Vector2(maxX + 1, maxY + 1));
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }
}
