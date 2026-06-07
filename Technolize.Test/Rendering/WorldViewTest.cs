using System.Numerics;
using Technolize.Rendering;
using Technolize.Rendering.Graphics;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Test.Rendering;

/// <summary>
/// Headless-skippable smoke test for <see cref="WorldView"/>, the live Silk.NET world-view host.
/// It opens a real OpenGL 4.6 window, drives several frames through the full path (input poll → world
/// render to the default framebuffer → ImGui HUD), and asserts the loop runs without error. The render
/// correctness itself is covered by the deterministic snapshot gate; this verifies the interactive
/// pieces wire together on a real window.
/// </summary>
[TestFixture]
public class WorldViewTest
{
    [SetUp]
    public void SkipWhenHeadless()
    {
        if (IsHeadlessEnvironment())
        {
            Assert.Ignore("Skipping Silk world-view GPU test - running in headless environment without display support.");
        }
    }

    [Test]
    public void RenderFrame_RunsSeveralFrames_WithoutError()
    {
        TickableWorld world = CreateSmallWorld();

        using GlContext context = GlContext.CreateWindowed(640, 480, "Technolize WorldView Test");
        using var view = new WorldView(context, new TickableWorldRenderSource(world))
        {
            FixedTime = 7.5f,
        };

        Assert.DoesNotThrow(() =>
        {
            for (int frame = 0; frame < 5; frame++)
            {
                context.Window.DoEvents();
                view.RenderFrame(1f / 60f);
                context.Window.GLContext?.SwapBuffers();
            }
        });
    }

    [Test]
    public void ShowGrid_TogglesWithoutError()
    {
        TickableWorld world = CreateSmallWorld();

        using GlContext context = GlContext.CreateWindowed(320, 240, "Technolize WorldView Grid Test");
        using var view = new WorldView(context, new TickableWorldRenderSource(world))
        {
            FixedTime = 7.5f,
        };

        Assert.DoesNotThrow(() =>
        {
            view.ShowGrid = false;
            context.Window.DoEvents();
            view.RenderFrame(1f / 60f);

            view.ShowGrid = true;
            context.Window.DoEvents();
            view.RenderFrame(1f / 60f);
            context.Window.GLContext?.SwapBuffers();
        });
    }

    private static TickableWorld CreateSmallWorld()
    {
        TickableWorld world = new();
        world.BatchSetBlocks(placer =>
        {
            for (int x = -8; x <= 8; x++)
            {
                for (int y = -8; y <= 8; y++)
                {
                    uint block = y < 0 ? Blocks.Water.Id : Blocks.Air.Id;
                    if (y <= -6)
                    {
                        block = Blocks.Stone.Id;
                    }
                    placer.Set(new Vector2(x, y), block);
                }
            }
        });

        return world;
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
