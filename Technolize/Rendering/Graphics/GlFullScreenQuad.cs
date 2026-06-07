using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A unit full-screen quad (two triangles spanning normalized device coordinates) used to run a
/// fragment shader over every pixel of the bound framebuffer; it drives the world shader pass.
///
/// Vertex attributes are bound by explicit location for the modern core profile:
/// <list type="bullet">
///   <item><description>location 0: <c>vec2</c> position in NDC ([-1, 1]).</description></item>
///   <item><description>location 1: <c>vec2</c> texture coordinate ([0, 1], top-left origin).</description></item>
/// </list>
/// </summary>
public sealed class GlFullScreenQuad : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;

    // Two triangles covering the screen. Each vertex: posX, posY, u, v.
    // Texture V is flipped so (0,0) is the top-left, matching the row-major top-down textures.
    private static readonly float[] Vertices =
    {
        -1f, -1f, 0f, 1f,
         1f, -1f, 1f, 1f,
         1f,  1f, 1f, 0f,

        -1f, -1f, 0f, 1f,
         1f,  1f, 1f, 0f,
        -1f,  1f, 0f, 0f,
    };

    public unsafe GlFullScreenQuad(GL gl)
    {
        _gl = gl;

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        _vbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* data = Vertices)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(Vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        const uint stride = 4 * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    /// <summary>Draws the quad. The desired shader program must already be active.</summary>
    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
