using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Technolize.Rendering.Graphics;

namespace Technolize.Rendering;

/// <summary>
/// A live, interactive world view rendered entirely on the Silk.NET OpenGL 4.6 backend: a real window
/// whose camera is driven by Silk.NET input (left-drag pan, wheel zoom about the cursor), the world is
/// drawn each frame via <see cref="WorldShaderRenderer.RenderToTarget"/> to the default framebuffer,
/// and a Dear ImGui HUD is overlaid via <see cref="ImGuiHost"/>.
///
/// This wires the complete live Silk path (window + input + camera + world render + ImGui) end-to-end
/// as a standalone host. The render correctness itself is covered by the deterministic snapshot gate;
/// this host wires the interactive pieces together.
/// </summary>
public sealed class WorldView : IDisposable
{
    private readonly GlContext _context;
    private readonly IWorldRenderSource _renderSource;
    private readonly WorldShaderRenderer _renderer;
    private readonly ImGuiHost _imgui;
    private readonly IMouse? _mouse;

    private Camera2D _camera;
    private Vector2 _lastMousePosition;
    private bool _hasLastMousePosition;
    private float _scrollAccumulator;

    public WorldView(GlContext context, IWorldRenderSource renderSource)
    {
        if (context.Input is null)
        {
            throw new InvalidOperationException("WorldView requires a windowed context with input.");
        }

        _context = context;
        _renderSource = renderSource;
        _renderer = new WorldShaderRenderer(context.Gl);
        _imgui = new ImGuiHost(context);

        Vector2D<int> size = context.Window.Size;
        _camera = Camera2D.Centered(size.X, size.Y);

        _mouse = context.Input.Mice.Count > 0 ? context.Input.Mice[0] : null;
        if (_mouse is not null)
        {
            _mouse.Scroll += (_, wheel) => _scrollAccumulator += wheel.Y;
        }
    }

    /// <summary>The current camera; exposed for tests and HUD read-out.</summary>
    public Camera2D Camera => _camera;

    /// <summary>Lighting applied to the world shader pass.</summary>
    public WorldLighting Lighting { get; set; } = WorldLighting.Default;

    /// <summary>When set, overrides wall-clock time for the animated water/sun (deterministic capture).</summary>
    public float? FixedTime { get; set; }

    /// <summary>Whether to draw the world grid overlay.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Advances input, renders one frame to the default framebuffer, and overlays the HUD. The caller
    /// owns the window event/swap loop; this is the per-frame body.
    /// </summary>
    public void RenderFrame(float deltaSeconds)
    {
        UpdateCameraFromInput();

        Vector2D<int> size = _context.Window.Size;
        int width = Math.Max(1, size.X);
        int height = Math.Max(1, size.Y);

        WorldRenderFrame frame = _renderSource.CaptureWorldFrame();
        (Vector2 regionStart, Vector2 regionEnd) = ComputeWorldRegionBounds(frame);

        float time = FixedTime ?? (float)_context.Window.Time;

        // Bind the default framebuffer (0) so the world renders directly to the window.
        _context.Gl.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, 0);
        _renderer.RenderToTarget(frame, regionStart, regionEnd, Lighting, time, _camera, width, height, ShowGrid);

        _imgui.BeginFrame(deltaSeconds);
        DrawHud(frame, width, height);
        _imgui.EndFrame();
    }

    private void DrawHud(WorldRenderFrame frame, int width, int height)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10));
        ImGui.Begin("World View",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
        ImGui.Text("Technolize — Silk.NET OpenGL 4.6");
        ImGui.Separator();
        ImGui.Text($"Viewport: {width} x {height}");
        ImGui.Text($"Regions: {frame.Regions.Count}");
        ImGui.Text($"Zoom: {_camera.Zoom:0.000}");
        ImGui.Text($"Target: ({_camera.Target.X:0.0}, {_camera.Target.Y:0.0})");
        ImGui.Checkbox("Grid", ref _showGridField);
        ShowGrid = _showGridField;
        ImGui.End();
    }

    private bool _showGridField = true;

    private void UpdateCameraFromInput()
    {
        if (_mouse is null)
        {
            return;
        }

        // ImGui intercepts input when the cursor is over its windows, so the world camera should not
        // also react to those clicks/scrolls.
        ImGuiIOPtr io = ImGui.GetIO();
        bool imguiWantsMouse = io.WantCaptureMouse;

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

    /// <summary>
    /// Returns the half-open region-space bounding box covering every loaded region in the frame, so
    /// the whole world is rendered (matching <see cref="WorldShaderRenderer"/>). Falls back to a unit
    /// box when no regions are loaded.
    /// </summary>
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
        _imgui.Dispose();
        _renderer.Dispose();
    }
}
