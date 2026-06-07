using System.Numerics;
using Silk.NET.OpenGL;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// A compiled and linked OpenGL shader program with a cached uniform-location table and typed setters.
/// Uniform values use <see cref="System.Numerics"/> vector types so the renderer code (which already
/// builds <c>Vector2/3/4</c> uniform data) feeds them directly.
/// </summary>
public sealed class GlShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocations = new();

    private GlShaderProgram(GL gl, uint handle)
    {
        _gl = gl;
        _handle = handle;
    }

    /// <summary>Compiles and links a program from a vertex- and fragment-shader source file pair.</summary>
    public static GlShaderProgram FromFiles(GL gl, string vertexPath, string fragmentPath)
    {
        string vertexSource = File.ReadAllText(vertexPath);
        string fragmentSource = File.ReadAllText(fragmentPath);
        return FromSource(gl, vertexSource, fragmentSource);
    }

    /// <summary>Compiles and links a program from in-memory vertex- and fragment-shader source.</summary>
    public static GlShaderProgram FromSource(GL gl, string vertexSource, string fragmentSource)
    {
        uint vertex = CompileShader(gl, ShaderType.VertexShader, vertexSource);
        uint fragment = CompileShader(gl, ShaderType.FragmentShader, fragmentSource);

        uint handle = gl.CreateProgram();
        gl.AttachShader(handle, vertex);
        gl.AttachShader(handle, fragment);
        gl.LinkProgram(handle);

        gl.GetProgram(handle, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string log = gl.GetProgramInfoLog(handle);
            gl.DeleteProgram(handle);
            gl.DeleteShader(vertex);
            gl.DeleteShader(fragment);
            throw new InvalidOperationException($"Shader program link failed: {log}");
        }

        // Shaders are no longer needed once linked into the program.
        gl.DetachShader(handle, vertex);
        gl.DetachShader(handle, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        return new GlShaderProgram(gl, handle);
    }

    private static uint CompileShader(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compilation failed: {log}");
        }

        return shader;
    }

    /// <summary>Binds this program as the active program for subsequent draw calls.</summary>
    public void Use() => _gl.UseProgram(_handle);

    private int Location(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int cached))
        {
            return cached;
        }

        int location = _gl.GetUniformLocation(_handle, name);
        _uniformLocations[name] = location;
        return location;
    }

    public void SetFloat(string name, float value) => _gl.Uniform1(Location(name), value);

    public void SetInt(string name, int value) => _gl.Uniform1(Location(name), value);

    public void SetVector2(string name, Vector2 value) => _gl.Uniform2(Location(name), value.X, value.Y);

    public void SetVector3(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);

    public void SetVector4(string name, Vector4 value) => _gl.Uniform4(Location(name), value.X, value.Y, value.Z, value.W);

    /// <summary>Uploads a contiguous array of vec4 values (e.g. light data / colour arrays).</summary>
    public void SetVector4Array(string name, ReadOnlySpan<Vector4> values)
    {
        if (values.Length == 0)
        {
            return;
        }

        ReadOnlySpan<float> floats = System.Runtime.InteropServices.MemoryMarshal.Cast<Vector4, float>(values);
        _gl.Uniform4(Location(name), (uint)values.Length, floats);
    }

    /// <summary>Uploads a 4x4 matrix uniform (column-major, matching GLSL <c>mat4</c>).</summary>
    public unsafe void SetMatrix4(string name, Matrix4x4 value)
    {
        _gl.UniformMatrix4(Location(name), 1, false, (float*)&value);
    }

    /// <summary>
    /// Binds <paramref name="texture"/> to <paramref name="unit"/> and points the named sampler uniform
    /// at that unit. The program must be active (<see cref="Use"/>) before calling.
    /// </summary>
    public void SetTexture(string name, GlTexture texture, int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        texture.Bind();
        _gl.Uniform1(Location(name), unit);
    }

    public void Dispose() => _gl.DeleteProgram(_handle);
}
