using System.Numerics;
using ImGuiNET;
using Silk.NET.OpenGL;
using Technolize.Rendering.Graphics;

namespace Technolize.Test.Rendering;

/// <summary>
/// Proof-of-concept / smoke test for the Dear ImGui UI layer used by the menus and HUD. It opens a
/// real OpenGL 4.6 window, runs a handful of frames building sample panels that mirror the live game's
/// UI (playback controls, brush, lighting, a debug overlay), and asserts the full ImGui pipeline drives
/// without error.
///
/// Skipped automatically in headless / CI environments, matching the other GPU tests.
/// </summary>
[TestFixture]
public class ImGuiHostPocTest
{
    [SetUp]
    public void SkipWhenHeadless()
    {
        if (IsHeadlessEnvironment())
        {
            Assert.Ignore("Skipping ImGui GPU test - running in headless environment without display support.");
        }
    }

    [Test]
    public void WindowedImGui_RendersSamplePanels_ForSeveralFrames()
    {
        using GlContext context = GlContext.CreateWindowed(960, 600, "Technolize ImGui POC");
        using var imgui = new ImGuiHost(context);

        GL gl = context.Gl;

        // A few sample panels mirroring the game's UI so the ImGui look can be evaluated.
        int brushSize = 3;
        float sunAngle = 45f;
        int sunRays = 16;
        bool paused = false;

        Assert.DoesNotThrow(() =>
        {
            for (int frame = 0; frame < 5; frame++)
            {
                context.Window.DoEvents();

                gl.Viewport(0, 0, 960, 600);
                gl.ClearColor(0.05f, 0.07f, 0.09f, 1f);
                gl.Clear((uint)ClearBufferMask.ColorBufferBit);

                imgui.BeginFrame(1f / 60f);

                ImGui.Begin("Playback");
                if (ImGui.Button(paused ? "Play" : "Pause"))
                {
                    paused = !paused;
                }
                ImGui.SameLine();
                ImGui.Button("Single Tick");
                ImGui.End();

                ImGui.Begin("Brush");
                ImGui.SliderInt("Size", ref brushSize, 1, 16);
                ImGui.End();

                ImGui.Begin("Lighting");
                ImGui.SliderFloat("Sun Angle", ref sunAngle, 0f, 360f);
                ImGui.SliderInt("Rays", ref sunRays, 1, 64);
                ImGui.End();

                ImGui.SetNextWindowPos(new Vector2(10, 10));
                ImGui.Begin("HUD",
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize);
                ImGui.Text($"Frame {frame}");
                ImGui.Text($"Paused: {paused}");
                ImGui.End();

                imgui.EndFrame();

                context.Window.GLContext?.SwapBuffers();
            }
        });
    }

    private static bool IsHeadlessEnvironment()
    {
        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return true;
        }

        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEADLESS"));
    }
}
