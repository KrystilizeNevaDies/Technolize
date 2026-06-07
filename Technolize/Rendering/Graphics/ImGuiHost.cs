using Silk.NET.OpenGL.Extensions.ImGui;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// Hosts a Dear ImGui frame over a Silk.NET OpenGL context. This is the UI layer for the menus, HUD,
/// and inventory.
///
/// Per frame the caller does:
/// <code>
///   imgui.BeginFrame(deltaSeconds);
///   // ... ImGui.* widget calls describing the UI ...
///   imgui.EndFrame();
/// </code>
/// ImGui is immediate-mode, which maps directly onto how the current UI code is structured
/// (per-frame "if hovered / clicked then draw" logic in Program.cs).
/// </summary>
public sealed class ImGuiHost : IDisposable
{
    private readonly ImGuiController _controller;

    /// <summary>
    /// Creates the ImGui controller bound to the visible window's GL context and input. The context
    /// must have been created with <see cref="GlContext.CreateWindowed"/> (offscreen contexts have
    /// no input and cannot host interactive UI).
    /// </summary>
    public ImGuiHost(GlContext context)
    {
        if (context.Input is null)
        {
            throw new InvalidOperationException(
                "ImGuiHost requires an input context; create the window with GlContext.CreateWindowed.");
        }

        _controller = new ImGuiController(context.Gl, context.Window, context.Input);
    }

    /// <summary>Starts a new ImGui frame. Call before issuing any ImGui widget calls.</summary>
    public void BeginFrame(float deltaSeconds) => _controller.Update(deltaSeconds);

    /// <summary>Renders the ImGui draw data accumulated this frame into the current framebuffer.</summary>
    public void EndFrame() => _controller.Render();

    public void Dispose() => _controller.Dispose();
}
