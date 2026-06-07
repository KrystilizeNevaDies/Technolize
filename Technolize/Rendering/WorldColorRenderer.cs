using System.Numerics;
using Silk.NET.OpenGL;
using Technolize.Rendering.Graphics;
using Technolize.Rendering.Lighting;
using Technolize.Utils;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// A clean-slate secondary world renderer (selectable via <c>--r2</c>). It is a stripped copy of
/// <see cref="WorldShaderRenderer"/> that renders only the block colour modulated by a CPU-computed
/// lighting texture. Before every frame a CPU compute stage (<see cref="CpuLightingStage"/>) casts rays
/// from each light source (currently the sun) through a dense optical copy of the world
/// (<see cref="CpuLightingField"/>), depositing light into a shared lighting buffer. The fragment shader
/// (<c>world_color.frag</c>) then just multiplies the block colour by that lighting texture — no GPU
/// lighting, shadows, water effects, quadtree traversal, or grid overlay.
/// </summary>
public sealed class WorldColorRenderer : IWorldShaderTarget
{
    private const float BlockSize = 16f;

    // Lighting resolution multiplier: light cells per world cell along each axis. Higher = finer
    // lighting detail (smoother caustics/shadows) at a quadratic CPU + upload cost. 1 keeps one light
    // cell per world tile.
    private const int ResolutionMultiplier = 4;

    // Sun rays are cast across the field, oversampled per column for smooth caustics. Because the marcher
    // energy-normalizes each deposit, this only controls how finely the light streams are resolved, not
    // the overall brightness, so it can scale with the field instead of being a fixed (huge) count.
    private const int SunRaysPerColumn = 4;
    private const int MinSunRays = 256;
    private const int MaxSunRays = 8192;

    private static readonly Color AirColor = Blocks.Air.GetTag(BlockInfo.TagColor);

    private readonly GL _gl;
    private readonly GlShaderProgram _screenShader;
    private readonly GlFullScreenQuad _quad;
    private readonly CpuLightingStage _lightingStage = new();

    // Cache of the most recently uploaded colour texture + optical field, reused when the same frame +
    // region bounds are rendered again (e.g. panning/zooming a paused world). The live path builds a
    // fresh frame each tick, so a changed world misses the cache and re-uploads.
    private WorldRenderFrame? _cachedFrame;
    private Vector2 _cachedRegionStart;
    private Vector2 _cachedRegionEnd;
    private WorldShaderResourceData? _cachedData;
    private GlTexture? _cachedColor;
    private CpuLightingField? _cachedField;

    // The lighting texture is recomputed and re-uploaded every frame (light sources may move/change
    // independently of the world), so it is owned separately from the cached colour upload.
    private GlTexture? _lightTexture;

    public WorldColorRenderer(GL gl, string? shaderDirectory = null)
    {
        _gl = gl;
        string directory = shaderDirectory ?? Path.Combine(AppContext.BaseDirectory, "shaders");
        string fragPath = Path.Combine(directory, "world_color.frag");

        _screenShader = GlShaderProgram.FromFiles(gl, Path.Combine(directory, "world_renderer.screen.vert"), fragPath);
        _quad = new GlFullScreenQuad(gl);
    }

    /// <summary>
    /// Renders the world colour through the given camera into whichever framebuffer is currently bound,
    /// without reading pixels back. The world is lit by the CPU lighting stage built from
    /// <paramref name="lighting"/>; <paramref name="time"/> and <paramref name="drawGrid"/> are ignored.
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
        (WorldShaderResourceData data, GlTexture color, CpuLightingField field) =
            GetOrBuildResources(frame, worldRegionStart, worldRegionEnd);

        // CPU compute stage: cast this frame's light rays into the shared lighting buffer and upload it.
        GlTexture lightTexture = BuildLightTexture(field, lighting);

        _gl.Viewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
        _gl.ClearColor(AirColor.R / 255f, AirColor.G / 255f, AirColor.B / 255f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        // World-space destination rectangle: UV (0,0) maps to the top-left of the world texture, which
        // is the highest world row.
        Vector2 destOrigin = new(
            data.WorldOrigin.X * BlockSize,
            -(data.WorldOrigin.Y + data.WorldSize.Y - 1.0f) * BlockSize);
        Vector2 destSize = new(data.WorldSize.X * BlockSize, data.WorldSize.Y * BlockSize);

        _screenShader.Use();
        _screenShader.SetVector2("regionSize", data.WorldSize);
        _screenShader.SetVector2("destOrigin", destOrigin);
        _screenShader.SetVector2("destSize", destSize);
        _screenShader.SetVector2("cameraTarget", camera.Target);
        _screenShader.SetVector2("cameraOffset", camera.Offset);
        _screenShader.SetFloat("cameraZoom", camera.Zoom);
        _screenShader.SetVector2("viewportSize", new Vector2(viewportWidth, viewportHeight));
        _screenShader.SetTexture("texture0", color, 0);
        _screenShader.SetTexture("lightTex", lightTexture, 1);

        _quad.Draw();
    }

    /// <summary>
    /// Runs the CPU lighting compute for this frame and uploads the result as the lighting texture. The
    /// emitter list is lighting-agnostic, so additional sources (point lights, emissive blocks) can be
    /// added here without touching the ray marcher or the shader.
    /// </summary>
    private GlTexture BuildLightTexture(CpuLightingField field, WorldLighting? lighting)
    {
        WorldLighting effective = lighting ?? WorldLighting.Default;
        Vector3 ambient = effective.AmbientColor.ToVector3();
        Vector3 sunRadiance = effective.SunColor.ToVector3() * effective.SunIntensity;

        int sunRayCount = Math.Clamp(field.Width * SunRaysPerColumn, MinSunRays, MaxSunRays);
        List<ILightEmitter> emitters = new()
        {
            new SunLightEmitter(effective.SunDirection, sunRadiance, sunRayCount),
        };

        byte[] lightPixels = _lightingStage.Compute(field, ambient, emitters, out int width, out int height);

        _lightTexture?.Dispose();
        // Bilinear filtering smooths the lighting between texels so the (now higher-resolution) light
        // grid reads as continuous gradients rather than blocky cells; the shader adds bicubic taps on
        // top for further smoothing.
        _lightTexture = GlTexture.CreateRgba8(_gl, width, height, lightPixels, linear: true);
        return _lightTexture;
    }

    /// <summary>
    /// Returns the uploaded colour texture and optical field for <paramref name="frame"/> at the given
    /// bounds, reusing the cached upload when the same frame object and bounds were rendered last time.
    /// </summary>
    private (WorldShaderResourceData data, GlTexture color, CpuLightingField field) GetOrBuildResources(
        WorldRenderFrame frame, Vector2 worldRegionStart, Vector2 worldRegionEnd)
    {
        if (_cachedColor is not null && _cachedData is not null && _cachedField is not null
            && ReferenceEquals(_cachedFrame, frame)
            && _cachedRegionStart == worldRegionStart
            && _cachedRegionEnd == worldRegionEnd)
        {
            return (_cachedData, _cachedColor, _cachedField);
        }

        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, worldRegionStart, worldRegionEnd);
        GlTexture color = GlTexture.CreateRgba8(_gl, data.WorldColorWidth, data.WorldColorHeight, data.WorldColorPixels);
        CpuLightingField field = CpuLightingField.Build(frame, worldRegionStart, worldRegionEnd, ResolutionMultiplier);

        _cachedColor?.Dispose();
        _cachedFrame = frame;
        _cachedRegionStart = worldRegionStart;
        _cachedRegionEnd = worldRegionEnd;
        _cachedData = data;
        _cachedColor = color;
        _cachedField = field;
        return (data, color, field);
    }

    public void Dispose()
    {
        _lightTexture?.Dispose();
        _cachedColor?.Dispose();
        _quad.Dispose();
        _screenShader.Dispose();
    }
}
