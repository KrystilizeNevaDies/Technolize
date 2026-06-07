using System.Numerics;
using Technolize.Rendering.Graphics;

namespace Technolize.Test.Rendering;

/// <summary>
/// Pure-CPU tests for <see cref="Camera2D"/>'s screen↔world conventions. No GPU is required, so these
/// run everywhere and pin the projection maths the on-screen render path and the input/UI code depend on.
/// </summary>
[TestFixture]
public class Camera2DTest
{
    [Test]
    public void CenteredCamera_MapsWorldToScreenOneToOne()
    {
        Camera2D camera = Camera2D.Centered(1280, 720);

        // Target == Offset == centre and zoom 1 ⇒ screen == world.
        Assert.Multiple(() =>
        {
            Assert.That(camera.WorldToScreen(new Vector2(0, 0)), Is.EqualTo(new Vector2(0, 0)));
            Assert.That(camera.WorldToScreen(new Vector2(100, -50)), Is.EqualTo(new Vector2(100, -50)));
            Assert.That(camera.WorldToScreen(new Vector2(640, 360)), Is.EqualTo(new Vector2(640, 360)));
        });
    }

    [Test]
    public void WorldToScreen_AppliesZoomAboutTarget()
    {
        // Target maps to Offset regardless of zoom; points scale about the target.
        Camera2D camera = new(target: new Vector2(100, 100), offset: new Vector2(640, 360), zoom: 2.0f);

        Assert.Multiple(() =>
        {
            Assert.That(camera.WorldToScreen(new Vector2(100, 100)), Is.EqualTo(new Vector2(640, 360)));
            Assert.That(camera.WorldToScreen(new Vector2(110, 100)), Is.EqualTo(new Vector2(660, 360)));
            Assert.That(camera.WorldToScreen(new Vector2(100, 90)), Is.EqualTo(new Vector2(640, 340)));
        });
    }

    [Test]
    public void ScreenToWorld_IsInverseOfWorldToScreen()
    {
        Camera2D camera = new(target: new Vector2(-30, 215), offset: new Vector2(400, 300), zoom: 0.75f);

        foreach (Vector2 world in new[] { new Vector2(0, 0), new Vector2(123, -456), new Vector2(-789, 321) })
        {
            Vector2 roundTrip = camera.ScreenToWorld(camera.WorldToScreen(world));
            Assert.That(roundTrip.X, Is.EqualTo(world.X).Within(1e-3));
            Assert.That(roundTrip.Y, Is.EqualTo(world.Y).Within(1e-3));
        }
    }

    [Test]
    public void Panning_ShiftsTargetInWorldSpace()
    {
        Camera2D camera = Camera2D.Centered(800, 600);
        Vector2 before = camera.ScreenToWorld(new Vector2(0, 0));

        // Mirror the renderer's pan: dragging moves the target opposite the cursor delta / zoom.
        camera.Target -= new Vector2(20, 10) / camera.Zoom;
        Vector2 after = camera.ScreenToWorld(new Vector2(0, 0));

        Assert.That(after, Is.EqualTo(before - new Vector2(20, 10)));
    }

    [Test]
    public void Pan_KeepsWorldPointUnderCursor()
    {
        Camera2D camera = new(target: new Vector2(50, 50), offset: new Vector2(400, 300), zoom: 2.0f);
        Vector2 dragDelta = new(30, -18);

        // The world point currently under the (moving) cursor stays under it after panning by the delta.
        Vector2 cursorBefore = new(400, 300);
        Vector2 worldUnderCursor = camera.ScreenToWorld(cursorBefore);

        camera.Pan(dragDelta);

        Vector2 cursorAfter = cursorBefore + dragDelta;
        Vector2 worldUnderCursorAfter = camera.ScreenToWorld(cursorAfter);
        Assert.Multiple(() =>
        {
            Assert.That(worldUnderCursorAfter.X, Is.EqualTo(worldUnderCursor.X).Within(1e-3));
            Assert.That(worldUnderCursorAfter.Y, Is.EqualTo(worldUnderCursor.Y).Within(1e-3));
        });
    }

    [Test]
    public void ZoomAt_KeepsAnchorWorldPointFixedOnScreen()
    {
        Camera2D camera = Camera2D.Centered(1280, 720);
        Vector2 anchor = new(900, 500);
        Vector2 worldBefore = camera.ScreenToWorld(anchor);

        camera.ZoomAt(anchor, wheelMove: 1f);

        // After zooming in about the anchor, the same world point must still project to the anchor.
        Vector2 screenAfter = camera.WorldToScreen(worldBefore);
        Assert.Multiple(() =>
        {
            Assert.That(camera.Zoom, Is.EqualTo(1.1f).Within(1e-4), "one wheel notch zooms by 1.1x");
            Assert.That(screenAfter.X, Is.EqualTo(anchor.X).Within(1e-2));
            Assert.That(screenAfter.Y, Is.EqualTo(anchor.Y).Within(1e-2));
        });
    }

    [Test]
    public void ZoomAt_ClampsToRange()
    {
        Camera2D camera = Camera2D.Centered(800, 600);

        for (int i = 0; i < 200; i++)
        {
            camera.ZoomAt(new Vector2(400, 300), wheelMove: 1f);
        }
        Assert.That(camera.Zoom, Is.EqualTo(24.0f).Within(1e-3), "zoom clamps at the maximum");

        for (int i = 0; i < 400; i++)
        {
            camera.ZoomAt(new Vector2(400, 300), wheelMove: -1f);
        }
        Assert.That(camera.Zoom, Is.EqualTo(0.01f).Within(1e-4), "zoom clamps at the minimum");
    }

    [Test]
    public void ZoomAt_ZeroWheel_IsNoOp()
    {
        Camera2D camera = new(target: new Vector2(10, 20), offset: new Vector2(30, 40), zoom: 3f);
        Camera2D before = camera;

        camera.ZoomAt(new Vector2(100, 100), wheelMove: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(camera.Zoom, Is.EqualTo(before.Zoom));
            Assert.That(camera.Target, Is.EqualTo(before.Target));
            Assert.That(camera.Offset, Is.EqualTo(before.Offset));
        });
    }
}
