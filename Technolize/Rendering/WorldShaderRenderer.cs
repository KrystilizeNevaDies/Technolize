using System.Numerics;
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

        using GpuResources resources = UploadResources(data);
        using var framebuffer = new GlFramebuffer(_gl, data.WorldColorWidth, data.WorldColorHeight);

        framebuffer.Bind();
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _nativeShader.Use();
        ApplyFragmentUniforms(_nativeShader, data, lighting, time);
        BindSamplers(_nativeShader, resources);
        _quad.Draw();

        byte[] pixels = framebuffer.ReadPixels();
        framebuffer.Unbind();
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
        WorldShaderResourceData data = WorldShaderResourceBuilder.Build(frame, worldRegionStart, worldRegionEnd);

        using GpuResources resources = UploadResources(data);

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
        ApplyFragmentUniforms(_screenShader, data, lighting, time);
        _screenShader.SetVector2("destOrigin", destOrigin);
        _screenShader.SetVector2("destSize", destSize);
        _screenShader.SetVector2("cameraTarget", camera.Target);
        _screenShader.SetVector2("cameraOffset", camera.Offset);
        _screenShader.SetFloat("cameraZoom", camera.Zoom);
        _screenShader.SetVector2("viewportSize", new Vector2(viewportWidth, viewportHeight));
        BindSamplers(_screenShader, resources);
        _quad.Draw();

        if (drawGrid)
        {
            DrawGrid(camera, viewportWidth, viewportHeight);
        }
    }

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
        return new GpuResources(
            GlTexture.CreateRgba8(_gl, data.WorldColorWidth, data.WorldColorHeight, data.WorldColorPixels),
            GlTexture.CreateRgba8(_gl, data.QuadtreeTextureWidth, data.QuadtreeTextureHeight, data.QuadtreeFirstChildPixels),
            GlTexture.CreateRgba8(_gl, data.QuadtreeTextureWidth, data.QuadtreeTextureHeight, data.QuadtreeValuePixels));
    }

    private static void BindSamplers(GlShaderProgram shader, GpuResources resources)
    {
        // The shader reads texture0 (dense colour) plus the two packed quadtree textures for the
        // refraction raymarch, on distinct texture units.
        shader.SetTexture("texture0", resources.Color, 0);
        shader.SetTexture("quadtreeFirstChild", resources.FirstChild, 1);
        shader.SetTexture("quadtreeValue", resources.Value, 2);
    }

    private static void ApplyFragmentUniforms(GlShaderProgram shader, WorldShaderResourceData data, WorldLighting lighting, float time)
    {
        shader.SetVector2("regionSize", data.WorldSize);
        shader.SetVector2("regionOrigin", data.WorldOrigin);
        shader.SetVector2("quadtreeSize", data.QuadtreeSize);
        shader.SetVector2("quadtreeOrigin", data.QuadtreeOrigin);
        shader.SetVector2("quadtreeTextureSize", data.QuadtreeTextureSize);
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
        _overlay.Dispose();
        _quad.Dispose();
        _screenShader.Dispose();
        _nativeShader.Dispose();
    }

    private sealed record GpuResources(GlTexture Color, GlTexture FirstChild, GlTexture Value) : IDisposable
    {
        public void Dispose()
        {
            Color.Dispose();
            FirstChild.Dispose();
            Value.Dispose();
        }
    }
}
