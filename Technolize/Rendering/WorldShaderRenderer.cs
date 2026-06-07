using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Technolize.Rendering.Graphics;
using Technolize.Utils;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// Renders the world shader pass on the Silk.NET OpenGL 4.6 backend. It consumes the backend-agnostic
/// <see cref="WorldShaderResourceBuilder"/> output and runs the <c>world_renderer.frag</c> shader.
///
/// Two paths are provided:
/// <list type="bullet">
///   <item><description>
///     <see cref="RenderToPixels"/> renders offscreen at the world's native resolution (one texel per
///     world cell). The fragment shader works purely in local texture space, so this path is
///     projection-independent, deterministic, and directly verifiable.
///   </description></item>
///   <item><description>
///     <see cref="RenderToScreen"/> is the on-screen camera path: it places the world texture
///     at its world-space destination rectangle and applies a <see cref="Camera2D"/> (pan/zoom),
///     rendering into a viewport-sized framebuffer.
///   </description></item>
/// </list>
/// </summary>
public sealed class WorldShaderRenderer : IDisposable
{
    private const float BlockSize = 16f;

    private static readonly Color AirColor = Blocks.Air.GetTag(BlockInfo.TagColor);

    private readonly GL _gl;
    private readonly GlShaderProgram _nativeShader;
    private readonly GlShaderProgram _screenShader;
    private readonly GlFullScreenQuad _quad;
    private readonly GlOverlayBatch _overlay;
    private GlTimerQuery? _gpuTimer;

    // Persistent lighting target for the two-pass path; recreated when its size changes. The temporal
    // path keeps a single persistent buffer (RGBA16F) and refreshes one horizontal band per frame in
    // place via the scissor test, leaving the rest of the buffer (the previous frames' lighting) intact.
    private GlFramebuffer? _lightingFbo;
    private GlFramebuffer? _historyFbo;

    // Monotonic frame counter for temporal accumulation; drives which tiles refresh each frame. Wrapped
    // to keep the float phase computation in the shader precise over long sessions.
    private int _frameIndex;

    // Previous frame's view signature (camera + viewport + sun + lighting scale). The persistent
    // lighting buffer is SCREEN-SPACE, so any of these changing scrolls/rescales the world under it and
    // invalidates the carried-forward bands. When it changes we force a full relight that frame so the
    // displayed lighting always matches the current view (no smearing while dragging/zooming). World
    // edits and animated time are deliberately NOT part of this: they don't move the world on screen, so
    // their lighting simply refreshes as the sweep reaches them (keeping simulation cheap).
    private int _lastViewSignature;
    private bool _hasLastView;
    private bool _viewChangedThisFrame;

    /// <summary>
    /// Default fraction of the viewport resolution at which the sun-lighting pass runs. <c>1.0</c> keeps
    /// lighting at full resolution; temporal accumulation (see <see cref="EnableTemporalAccumulation"/>)
    /// is what makes full-resolution lighting affordable, so this defaults to full quality.
    /// </summary>
    public const double DefaultLightingResolutionScale = 1.0;

    /// <summary>
    /// Default fraction of pixels whose lighting is recomputed each frame (see
    /// <see cref="PixelUpdateFraction"/>). <c>0.125</c> refreshes the whole screen every ~8 frames,
    /// ~6x cheaper lighting than relighting everything while converging to the exact image when still.
    /// </summary>
    public const double DefaultPixelUpdateFraction = 0.125;

    // Cache of the most recently uploaded resources, reused when the same frame + region bounds are
    // rendered again (e.g. panning/zooming a paused world, or repeated snapshot frames). The live path
    // builds a fresh frame object each tick, so a changed world misses the cache and re-uploads.
    private WorldRenderFrame? _cachedFrame;
    private Vector2 _cachedRegionStart;
    private Vector2 _cachedRegionEnd;
    private WorldShaderResourceData? _cachedData;
    private GpuResources? _cachedResources;

    public WorldShaderRenderer(GL gl, string? shaderDirectory = null)
    {
        _gl = gl;
        string directory = shaderDirectory ?? Path.Combine(AppContext.BaseDirectory, "shaders");
        string fragPath = Path.Combine(directory, "world_renderer.frag");

        _nativeShader = GlShaderProgram.FromFiles(gl, Path.Combine(directory, "world_renderer.vert"), fragPath);
        _screenShader = GlShaderProgram.FromFiles(gl, Path.Combine(directory, "world_renderer.screen.vert"), fragPath);
        _quad = new GlFullScreenQuad(gl);
        _overlay = new GlOverlayBatch(gl, directory);
    }

    /// <summary>
    /// When set, the world shader draw in <see cref="RenderToTarget"/> is wrapped in a GPU timer query
    /// and the elapsed GPU time (milliseconds, shader execution only — no readback) is exposed via
    /// <see cref="LastGpuDrawMs"/>. Off by default since resolving the query stalls the pipeline.
    /// </summary>
    public bool EnableGpuTiming { get; set; }

    /// <summary>The GPU time of the most recent world-shader draw, in milliseconds, or null if timing
    /// is disabled or no frame has been drawn yet.</summary>
    public double? LastGpuDrawMs { get; private set; }

    /// <summary>
    /// Fraction of the viewport resolution at which the sun-lighting pass runs in <see cref="RenderToTarget"/>
    /// / <see cref="RenderToScreen"/>. <c>1.0</c> keeps lighting at full resolution (the default, made
    /// affordable by temporal accumulation); values below <c>1.0</c> render the soft shadows at a reduced
    /// resolution and bilinearly upsample them, trading sharpness for speed. The native
    /// <see cref="RenderToPixels"/> path always shades single-pass at full resolution.
    /// </summary>
    public double LightingResolutionScale { get; set; } = DefaultLightingResolutionScale;

    /// <summary>
    /// When enabled (the default), the lighting pass refreshes only a fraction of the screen's pixels
    /// each frame (see <see cref="PixelUpdateFraction"/>) and carries the rest forward from a history
    /// buffer, so full-resolution, full-ray soft shadows are spread across several frames at a fraction
    /// of the per-frame cost. A static view converges to the exact full-quality image; a moving view
    /// trails by up to one refresh sweep (lower fractions trail longer).
    /// </summary>
    public bool EnableTemporalAccumulation { get; set; } = true;

    /// <summary>
    /// Fraction (0..1) of the screen's pixels whose lighting is recomputed each frame when
    /// <see cref="EnableTemporalAccumulation"/> is on. <c>1.0</c> relights everything every frame (no
    /// amortization); lower values refresh fewer pixels per frame for proportionally cheaper frames,
    /// taking ~<c>1 / PixelUpdateFraction</c> frames to refresh the whole screen. Pixels are refreshed
    /// in screen-space tiles so whole GPU warps share the work, making the saving real. This is the
    /// primary speed/latency knob: e.g. <c>0.25</c> ≈ 4x cheaper lighting, <c>0.03</c> ≈ 33x cheaper.
    /// </summary>
    public double PixelUpdateFraction { get; set; } = DefaultPixelUpdateFraction;

    /// <summary>
    /// Renders the loaded world region box to a tightly-packed RGBA8 buffer at native world resolution
    /// (<paramref name="width"/> x <paramref name="height"/> = the world colour texture size).
    /// </summary>
    public byte[] RenderToPixels(
        WorldRenderFrame frame,
        Vector2 worldRegionStart,
        Vector2 worldRegionEnd,
        WorldLighting lighting,
        float time,
        out int width,
        out int height)
    {
        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, worldRegionStart, worldRegionEnd);
        width = data.WorldColorWidth;
        height = data.WorldColorHeight;

        GpuResources resources = UploadResources(data);
        using var framebuffer = new GlFramebuffer(_gl, data.WorldColorWidth, data.WorldColorHeight);

        framebuffer.Bind();
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _nativeShader.Use();
        _nativeShader.SetInt("passMode", 0);
        ApplyFragmentUniforms(_nativeShader, data, lighting, time);
        BindSamplers(_nativeShader, resources);
        _quad.Draw();

        byte[] pixels = framebuffer.ReadPixels();
        framebuffer.Unbind();
        resources.Dispose();
        return pixels;
    }

    /// <summary>
    /// Renders the world through the given camera into a <paramref name="viewportWidth"/> x
    /// <paramref name="viewportHeight"/> framebuffer and returns the RGBA8 pixels.
    /// </summary>
    public byte[] RenderToScreen(
        WorldRenderFrame frame,
        Vector2 worldRegionStart,
        Vector2 worldRegionEnd,
        WorldLighting lighting,
        float time,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight,
        bool drawGrid = false)
    {
        using var framebuffer = new GlFramebuffer(_gl, viewportWidth, viewportHeight);
        framebuffer.Bind();

        RenderToTarget(frame, worldRegionStart, worldRegionEnd, lighting, time, camera, viewportWidth, viewportHeight, drawGrid);

        byte[] pixels = framebuffer.ReadPixels();
        framebuffer.Unbind();
        return pixels;
    }

    /// <summary>
    /// Renders the world through the given camera into whichever framebuffer is currently bound (e.g.
    /// the default window framebuffer for the live application, or a caller-managed offscreen target),
    /// without reading pixels back. Sets the viewport, clears to the air colour, draws the world quad,
    /// and optionally the grid overlay.
    /// </summary>
    public void RenderToTarget(
        WorldRenderFrame frame,
        Vector2 worldRegionStart,
        Vector2 worldRegionEnd,
        WorldLighting lighting,
        float time,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight,
        bool drawGrid = false)
    {
        (WorldShaderResourceData data, GpuResources resources) =
            GetOrBuildResources(frame, worldRegionStart, worldRegionEnd);

        // World-space destination rectangle: UV (0,0) maps to the top-left of the world texture, which
        // is the highest world row.
        Vector2 destOrigin = new(
            data.WorldOrigin.X * BlockSize,
            -(data.WorldOrigin.Y + data.WorldSize.Y - 1.0f) * BlockSize);
        Vector2 destSize = new(data.WorldSize.X * BlockSize, data.WorldSize.Y * BlockSize);

        bool temporal = EnableTemporalAccumulation;
        bool twoPass = temporal || LightingResolutionScale < 1.0;

        // Advance the monotonic frame counter that drives which tiles refresh this frame. It never
        // resets: a static view holds every tile at its (constant) value, so the composed image is the
        // exact full-quality result regardless of which tiles re-shaded; a moving view simply trails by
        // one refresh sweep. Wrapped to keep the shader's float phase computation precise.
        if (temporal)
        {
            _frameIndex = (_frameIndex + 1) & 0xFFFFF;

            // The screen-space lighting buffer is only valid for the camera/viewport/sun it was shaded
            // with; if any of those changed, the carried-forward bands no longer line up with the world,
            // so force a full relight this frame instead of showing the misaligned history.
            int viewSignature = ComputeViewSignature(lighting, camera, viewportWidth, viewportHeight);
            _viewChangedThisFrame = !_hasLastView || viewSignature != _lastViewSignature;
            _lastViewSignature = viewSignature;
            _hasLastView = true;
        }

        if (EnableGpuTiming)
        {
            _gpuTimer ??= new GlTimerQuery(_gl);
            _gpuTimer.Begin();
        }

        if (twoPass)
        {
            RenderTwoPass(data, resources, lighting, time, camera, viewportWidth, viewportHeight, destOrigin, destSize, temporal);
        }
        else
        {
            _gl.Viewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
            _gl.ClearColor(AirColor.R / 255f, AirColor.G / 255f, AirColor.B / 255f, 1f);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

            _screenShader.Use();
            _screenShader.SetInt("passMode", 0);
            SetScreenUniforms(data, lighting, time, camera, viewportWidth, viewportHeight, destOrigin, destSize);
            BindSamplers(_screenShader, resources);
            _quad.Draw();
        }

        if (EnableGpuTiming)
        {
            _gpuTimer!.End();
            LastGpuDrawMs = _gpuTimer.ResolveElapsedMs();
        }

        if (drawGrid)
        {
            DrawGrid(camera, viewportWidth, viewportHeight);
        }
    }

    /// <summary>
    /// A hash of the parameters that determine where the world projects on screen plus how it is lit.
    /// A change means the persistent screen-space lighting buffer no longer aligns with the world and
    /// must be fully relit. Deliberately excludes world content and animated time, which do not move
    /// the world on screen and so can refresh incrementally as the sweep reaches them.
    /// </summary>
    private static int ComputeViewSignature(WorldLighting lighting, Camera2D camera, int viewportWidth, int viewportHeight)
    {
        HashCode hash = new();
        hash.Add(camera.Target.X);
        hash.Add(camera.Target.Y);
        hash.Add(camera.Offset.X);
        hash.Add(camera.Offset.Y);
        hash.Add(camera.Zoom);
        hash.Add(viewportWidth);
        hash.Add(viewportHeight);
        hash.Add(lighting.SunDirection.X);
        hash.Add(lighting.SunDirection.Y);
        return hash.ToHashCode();
    }

    /// <summary>
    /// The decoupled lighting path. Pass 1 renders the sun transmittance into a lighting framebuffer
    /// (at <see cref="LightingResolutionScale"/> of the viewport); pass 2 composites it against the
    /// full-resolution base colour. When <paramref name="temporal"/> is set, pass 1 refreshes only a
    /// <see cref="PixelUpdateFraction"/> fraction of screen tiles with full-ray lighting and copies the
    /// rest from a ping-ponged history (RGBA16F), so a static view converges to the exact full-quality
    /// image; otherwise it shades all rays for every pixel in one frame. The world quad and camera are
    /// identical in both passes, so a fragment's screen position maps directly between them.
    /// </summary>
    private void RenderTwoPass(
        WorldShaderResourceData data,
        GpuResources resources,
        WorldLighting lighting,
        float time,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight,
        Vector2 destOrigin,
        Vector2 destSize,
        bool temporal)
    {
        double scale = Math.Clamp(LightingResolutionScale, 0.01, 1.0);
        int lightingWidth = Math.Max(2, (int)Math.Round(viewportWidth * scale));
        int lightingHeight = Math.Max(2, (int)Math.Round(viewportHeight * scale));

        // Capture the caller's bound framebuffer (default window FB for the live path, or an offscreen
        // target for the snapshot/benchmark path) BEFORE creating any FBO — a freshly created
        // GlFramebuffer leaves framebuffer 0 bound, which would otherwise be mistaken for the target on
        // the first frame and send pass 2 to the wrong destination.
        _gl.GetInteger(GLEnum.FramebufferBinding, out int previousFbo);

        GlFramebuffer lightingTarget;
        if (temporal)
        {
            bool resized = EnsureHistoryFbo(lightingWidth, lightingHeight);

            // Pass 1 — refresh one horizontal band of the persistent lighting buffer in place. The band
            // is selected by the scissor test (set below), so ONLY those rows are shaded — the rest of
            // the buffer keeps the lighting computed on earlier frames. The band sweeps top-to-bottom,
            // covering the whole buffer over ceil(1/fraction) frames, so a static view converges to the
            // exact full-quality image. On the first frame after a (re)allocation the buffer is empty,
            // so refresh everything once to avoid showing uninitialised rows.
            double fraction = Math.Clamp(PixelUpdateFraction, 0.0, 1.0);
            bool fullRefresh = resized || _viewChangedThisFrame || fraction >= 0.999 || fraction <= 0.0;
            int bandHeight = fullRefresh ? lightingHeight : Math.Max(1, (int)Math.Ceiling(fraction * lightingHeight));
            int bandCount = (lightingHeight + bandHeight - 1) / bandHeight;
            int bandStart = fullRefresh ? 0 : (_frameIndex % bandCount) * bandHeight;
            int bandRows = Math.Min(bandHeight, lightingHeight - bandStart);

            _historyFbo!.Bind();
            if (!fullRefresh)
            {
                _gl.Enable(EnableCap.ScissorTest);
                _gl.Scissor(0, bandStart, (uint)lightingWidth, (uint)bandRows);
            }

            _screenShader.Use();
            _screenShader.SetInt("passMode", 1);
            SetScreenUniforms(data, lighting, time, camera, viewportWidth, viewportHeight, destOrigin, destSize);
            BindSamplers(_screenShader, resources);
            _quad.Draw();

            if (!fullRefresh)
            {
                _gl.Disable(EnableCap.ScissorTest);
            }

            lightingTarget = _historyFbo;
        }
        else
        {
            EnsureLightingFbo(lightingWidth, lightingHeight);

            // Pass 1 — all rays for every pixel in one frame (no temporal refresh). Cleared to white so
            // the bilinear upsample never darkens the world edge.
            _lightingFbo!.Bind();
            _gl.ClearColor(1f, 1f, 1f, 1f);
            _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

            _screenShader.Use();
            _screenShader.SetInt("passMode", 1);
            SetScreenUniforms(data, lighting, time, camera, viewportWidth, viewportHeight, destOrigin, destSize);
            BindSamplers(_screenShader, resources);
            _quad.Draw();

            lightingTarget = _lightingFbo;
        }

        // Pass 2 — full-resolution composite that samples the (possibly upsampled) lighting texture.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFbo);
        _gl.Viewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
        _gl.ClearColor(AirColor.R / 255f, AirColor.G / 255f, AirColor.B / 255f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _screenShader.Use();
        _screenShader.SetInt("passMode", 2);
        SetScreenUniforms(data, lighting, time, camera, viewportWidth, viewportHeight, destOrigin, destSize);
        BindSamplers(_screenShader, resources);
        lightingTarget.BindColorTexture(3);
        _screenShader.SetInt("lightingTexture", 3);
        _quad.Draw();
    }

    /// <summary>
    /// Ensures the persistent temporal lighting buffer matches the given size, recreating (and
    /// white-initialising) it when needed. Returns true when it was (re)created this call, signalling
    /// that the caller must do a full refresh so no uninitialised rows are shown.
    /// </summary>
    private bool EnsureHistoryFbo(int width, int height)
    {
        if (_historyFbo is not null && _historyFbo.Width == width && _historyFbo.Height == height)
        {
            return false;
        }

        _historyFbo?.Dispose();
        _historyFbo = new GlFramebuffer(_gl, width, height, linearFilter: true, floatColor: true);

        // Initialise to white (fully lit) so any rows not yet covered by the sweep read as lit.
        _historyFbo.Bind();
        _gl.ClearColor(1f, 1f, 1f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return true;
    }

    private void EnsureLightingFbo(int width, int height)
    {
        if (_lightingFbo is not null && _lightingFbo.Width == width && _lightingFbo.Height == height)
        {
            return;
        }

        _lightingFbo?.Dispose();
        _lightingFbo = new GlFramebuffer(_gl, width, height, linearFilter: true);
    }

    private static void SetScreenUniforms(
        GlShaderProgram shader,
        WorldShaderResourceData data,
        WorldLighting lighting,
        float time,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight,
        Vector2 destOrigin,
        Vector2 destSize)
    {
        ApplyFragmentUniforms(shader, data, lighting, time);
        shader.SetVector2("destOrigin", destOrigin);
        shader.SetVector2("destSize", destSize);
        shader.SetVector2("cameraTarget", camera.Target);
        shader.SetVector2("cameraOffset", camera.Offset);
        shader.SetFloat("cameraZoom", camera.Zoom);
        shader.SetVector2("viewportSize", new Vector2(viewportWidth, viewportHeight));
    }

    private void SetScreenUniforms(
        WorldShaderResourceData data,
        WorldLighting lighting,
        float time,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight,
        Vector2 destOrigin,
        Vector2 destSize)
        => SetScreenUniforms(_screenShader, data, lighting, time, camera, viewportWidth, viewportHeight, destOrigin, destSize);

    private void DrawGrid(Camera2D camera, int viewportWidth, int viewportHeight)
    {
        (Vector2 worldStart, Vector2 worldEnd) = ComputeVisibleBlockBounds(camera, viewportWidth, viewportHeight);
        float[] vertices = WorldGridBuilder.BuildGridVertices(worldStart, worldEnd, camera.Zoom, out int vertexCount);
        _overlay.Draw(vertices, vertexCount, camera, viewportWidth, viewportHeight);
    }

    /// <summary>
    /// Computes the visible world block AABB for the camera/viewport, accounting for the world Y flip
    /// (screen-space world pixels use <c>-blockY * 16</c>). A one-block margin is added so grid lines at
    /// the edges are not clipped.
    /// </summary>
    private static (Vector2 start, Vector2 end) ComputeVisibleBlockBounds(Camera2D camera, int viewportWidth, int viewportHeight)
    {
        Vector2 cornerA = camera.ScreenToWorld(new Vector2(0, 0));
        Vector2 cornerB = camera.ScreenToWorld(new Vector2(viewportWidth, viewportHeight));

        float pxMin = Math.Min(cornerA.X, cornerB.X);
        float pxMax = Math.Max(cornerA.X, cornerB.X);
        float pyMin = Math.Min(cornerA.Y, cornerB.Y);
        float pyMax = Math.Max(cornerA.Y, cornerB.Y);

        int blockMinX = (int)Math.Floor(pxMin / BlockSize) - 1;
        int blockMaxX = (int)Math.Ceiling(pxMax / BlockSize) + 1;
        // World block Y is the negation of the world-pixel Y divided by the block size.
        int blockMinY = (int)Math.Floor(-pyMax / BlockSize) - 1;
        int blockMaxY = (int)Math.Ceiling(-pyMin / BlockSize) + 1;

        return (new Vector2(blockMinX, blockMinY), new Vector2(blockMaxX, blockMaxY));
    }

    private GpuResources UploadResources(WorldShaderResourceData data)
    {
        ReadOnlySpan<byte> nodeBytes = MemoryMarshal.AsBytes(data.QuadtreeNodes.AsSpan());
        return new GpuResources(
            GlTexture.CreateRgba8(_gl, data.WorldColorWidth, data.WorldColorHeight, data.WorldColorPixels),
            GlShaderStorageBuffer.Create(_gl, nodeBytes));
    }

    /// <summary>
    /// Returns the uploaded resources for <paramref name="frame"/> at the given bounds, reusing the
    /// cached upload when the same frame object and bounds were rendered last time. The live path
    /// allocates a new frame each tick, so a changed world naturally misses and re-uploads.
    /// </summary>
    private (WorldShaderResourceData data, GpuResources resources) GetOrBuildResources(
        WorldRenderFrame frame, Vector2 worldRegionStart, Vector2 worldRegionEnd)
    {
        if (_cachedResources is not null && _cachedData is not null
            && ReferenceEquals(_cachedFrame, frame)
            && _cachedRegionStart == worldRegionStart
            && _cachedRegionEnd == worldRegionEnd)
        {
            return (_cachedData, _cachedResources);
        }

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, worldRegionStart, worldRegionEnd);
        GpuResources resources = UploadResources(data);

        _cachedResources?.Dispose();
        _cachedFrame = frame;
        _cachedRegionStart = worldRegionStart;
        _cachedRegionEnd = worldRegionEnd;
        _cachedData = data;
        _cachedResources = resources;
        return (data, resources);
    }

    private static void BindSamplers(GlShaderProgram shader, GpuResources resources)
    {
        // The shader reads texture0 (dense colour) on texture unit 0 and the quadtree from the SSBO
        // bound at std430 binding point 0.
        shader.SetTexture("texture0", resources.Color, 0);
        resources.Quadtree.Bind(0);
    }

    private static void ApplyFragmentUniforms(GlShaderProgram shader, WorldShaderResourceData data, WorldLighting lighting, float time)
    {
        shader.SetVector2("regionSize", data.WorldSize);
        shader.SetVector2("regionOrigin", data.WorldOrigin);
        shader.SetVector2("quadtreeSize", data.QuadtreeSize);
        shader.SetVector2("quadtreeOrigin", data.QuadtreeOrigin);
        shader.SetFloat("time", time);

        WorldLighting effective = lighting ?? WorldLighting.Default;
        Vector2 sunDirection = effective.SunDirection.LengthSquared() > 0f
            ? Vector2.Normalize(effective.SunDirection)
            : WorldLighting.Default.SunDirection;
        shader.SetVector2("sunDirection", sunDirection);
        shader.SetInt("sunRayCount", Math.Clamp(effective.SunRayCount, 1, 64));
    }

    public void Dispose()
    {
        _cachedResources?.Dispose();
        _lightingFbo?.Dispose();
        _historyFbo?.Dispose();
        _gpuTimer?.Dispose();
        _overlay.Dispose();
        _quad.Dispose();
        _screenShader.Dispose();
        _nativeShader.Dispose();
    }

    private sealed record GpuResources(GlTexture Color, GlShaderStorageBuffer Quadtree) : IDisposable
    {
        public void Dispose()
        {
            Color.Dispose();
            Quadtree.Dispose();
        }
    }
}
