using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// Owns a Silk.NET OpenGL 4.6 core-profile context.
///
/// The context can be created either as a visible interactive window (for the live application) or as
/// a hidden window for offscreen / headless rendering (snapshot tests, benchmarks). In both cases a
/// real 4.6 core context is requested so the modern features the rendering pipeline targets (SSBOs,
/// immutable texture storage, direct state access, compute) are available.
/// </summary>
public sealed class GlContext : IDisposable
{
    private GlContext(IWindow window, GL gl, IInputContext? input)
    {
        Window = window;
        Gl = gl;
        Input = input;
    }

    /// <summary>The underlying Silk.NET window. Drives input and the swap-chain for the visible path.</summary>
    public IWindow Window { get; }

    /// <summary>The OpenGL 4.6 command interface bound to <see cref="Window"/>'s context.</summary>
    public GL Gl { get; }

    /// <summary>
    /// The input context (keyboard/mouse), created only for visible windows. Required by the ImGui
    /// controller. Null for offscreen contexts, which never receive input.
    /// </summary>
    public IInputContext? Input { get; }

    /// <summary>
    /// Creates a hidden OpenGL 4.6 context suitable for offscreen rendering. The window is never shown;
    /// rendering targets a framebuffer object and pixels are read back on the CPU.
    /// </summary>
    public static GlContext CreateOffscreen(int width, int height)
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(Math.Max(1, width), Math.Max(1, height)),
            IsVisible = false,
            Title = "Technolize (offscreen)",
            ShouldSwapAutomatically = false,
            API = OpenGl46Core(),
            // Offscreen rendering never presents to the screen, so vsync would only add latency.
            VSync = false,
        };

        return Create(options, createInput: false);
    }

    /// <summary>
    /// Creates a visible, resizable OpenGL 4.6 window for the interactive application.
    /// </summary>
    public static GlContext CreateWindowed(int width, int height, string title)
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(Math.Max(1, width), Math.Max(1, height)),
            Title = title,
            WindowBorder = WindowBorder.Resizable,
            API = OpenGl46Core(),
            VSync = false,
        };

        return Create(options, createInput: true);
    }

    private static GraphicsAPI OpenGl46Core()
    {
        return new GraphicsAPI(
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(4, 6));
    }

    private static GlContext Create(WindowOptions options, bool createInput)
    {
        IWindow window = Silk.NET.Windowing.Window.Create(options);

        // Initialize creates the native window and makes the GL context current on this thread.
        window.Initialize();

        GL gl = GL.GetApi(window);
        IInputContext? input = createInput ? window.CreateInput() : null;
        return new GlContext(window, gl, input);
    }

    public void Dispose()
    {
        Input?.Dispose();
        Gl.Dispose();
        Window.Dispose();
    }
}
