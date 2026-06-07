using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// An immutable shader storage buffer object (SSBO) holding a tightly-packed blob of data the fragment
/// shader reads as a <c>std430</c> buffer. This replaces the two packed RGBA8 quadtree textures: the
/// world quadtree is uploaded as a plain array of structs and indexed directly, with no bit-packing,
/// texture-width wrapping, or duplicated material codes.
/// </summary>
public sealed class GlShaderStorageBuffer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;

    /// <summary>Creates an SSBO and uploads <paramref name="data"/> (static-draw usage).</summary>
    public static unsafe GlShaderStorageBuffer Create(GL gl, ReadOnlySpan<byte> data)
    {
        uint handle = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, handle);

        fixed (byte* ptr = data)
        {
            gl.BufferData(
                BufferTargetARB.ShaderStorageBuffer,
                (nuint)data.Length,
                ptr,
                BufferUsageARB.StaticDraw);
        }

        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
        return new GlShaderStorageBuffer(gl, handle);
    }

    private GlShaderStorageBuffer(GL gl, uint handle)
    {
        _gl = gl;
        _handle = handle;
    }

    /// <summary>Binds this buffer to the given <c>std430</c> binding point for the active program.</summary>
    public void Bind(uint bindingIndex) =>
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, bindingIndex, _handle);

    public void Dispose() => _gl.DeleteBuffer(_handle);
}
