using System.Numerics;
using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// Draws camera-projected, per-vertex-coloured triangles (world-space overlay geometry such as the
/// grid and region overlays) with alpha blending. Vertices are interleaved
/// <c>(posX, posY, r, g, b, a)</c> in world-pixel space, exactly as produced by
/// <see cref="WorldGridBuilder"/>.
/// </summary>
public sealed class GlOverlayBatch : IDisposable
{
    private const int FloatsPerVertex = 6;

    private readonly GL _gl;
    private readonly GlShaderProgram _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private int _capacityFloats;

    public unsafe GlOverlayBatch(GL gl, string? shaderDirectory = null)
    {
        _gl = gl;
        string directory = shaderDirectory ?? Path.Combine(AppContext.BaseDirectory, "shaders");
        _shader = GlShaderProgram.FromFiles(
            gl,
            Path.Combine(directory, "world_overlay.vert"),
            Path.Combine(directory, "world_overlay.frag"));

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        const uint stride = FloatsPerVertex * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    /// <summary>
    /// Draws the given interleaved triangle vertices through the camera into the bound framebuffer,
    /// blending over the existing contents. <paramref name="vertexCount"/> must be a multiple of 3;
    /// a count of 0 is a no-op.
    /// </summary>
    public unsafe void Draw(
        ReadOnlySpan<float> vertices,
        int vertexCount,
        Camera2D camera,
        int viewportWidth,
        int viewportHeight)
    {
        if (vertexCount <= 0)
        {
            return;
        }

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        int neededFloats = vertexCount * FloatsPerVertex;
        fixed (float* data = vertices)
        {
            if (neededFloats > _capacityFloats)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(neededFloats * sizeof(float)), data, BufferUsageARB.DynamicDraw);
                _capacityFloats = neededFloats;
            }
            else
            {
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(neededFloats * sizeof(float)), data);
            }
        }

        bool blendWasEnabled = _gl.IsEnabled(EnableCap.Blend);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _shader.Use();
        _shader.SetVector2("cameraTarget", camera.Target);
        _shader.SetVector2("cameraOffset", camera.Offset);
        _shader.SetFloat("cameraZoom", camera.Zoom);
        _shader.SetVector2("viewportSize", new Vector2(viewportWidth, viewportHeight));

        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);

        if (!blendWasEnabled)
        {
            _gl.Disable(EnableCap.Blend);
        }

        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}
