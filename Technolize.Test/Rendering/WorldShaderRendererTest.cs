using System.Numerics;
using Technolize.Rendering;
using Technolize.Rendering.Graphics;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Test.Rendering;

/// <summary>
/// GPU tests for <see cref="WorldShaderRenderer"/> — the Silk.NET OpenGL 4.6 port of the world
/// shader pass. They render offscreen at native world resolution through a real 4.6 context and assert
/// the pass is deterministic and responds to world content. Skipped in headless / CI like the other
/// GPU tests.
///
/// A trivial single-leaf air quadtree is used: the colour-texture (surface) material still drives the
/// per-cell branch in the shader, while the refraction/sun raymarch sees an all-air world (fully lit).
/// That keeps the output deterministic without constructing a full world quadtree.
/// </summary>
[TestFixture]
public class WorldShaderRendererTest
{
    private static readonly int[] AirQuadtree = { -1, (int)Blocks.Air.Id };

    [SetUp]
    public void SkipWhenHeadless()
    {
        if (IsHeadlessEnvironment())
        {
            Assert.Ignore("Skipping Silk GPU test - running in headless environment without display support.");
        }
    }

    private static WorldRenderFrame AirFrame()
    {
        return new WorldRenderFrame([], new HashSet<Vector2>(), AirQuadtree);    }

    private static WorldRenderFrame ContentFrame()
    {
        List<WorldRenderBlock> blocks =
        [
            new(new Vector2(4, 4), Blocks.Stone.Id),
            new(new Vector2(5, 4), Blocks.Stone.Id),
            new(new Vector2(6, 6), Blocks.Water.Id),
            new(new Vector2(7, 6), Blocks.Water.Id),
        ];
        WorldRenderRegion region = new(new Vector2(0, 0), 0.0, blocks);
        return new WorldRenderFrame([region], new HashSet<Vector2>(), AirQuadtree);
    }

    /// <summary>
    /// A camera that frames the whole destination rectangle of a single-region (0,0) world. That world
    /// occupies <c>RegionSize</c> cells = <c>RegionSize * 16</c> world-pixels, placed by the renderer at
    /// a negative-Y origin; a default centred camera would leave most of it off-screen. Targeting the
    /// dest-rect centre and zooming to fit guarantees the content lands in the viewport.
    /// </summary>
    private static Camera2D FramedSingleRegionCamera(int viewportWidth, int viewportHeight)
    {
        const float blockSize = 16f;
        float worldPixels = TickableWorld.RegionSize * blockSize;

        // Renderer dest rect for region box [0,1): origin (0, -(worldSize.Y - 1) * 16), size worldPixels².
        Vector2 destOrigin = new(0f, -(TickableWorld.RegionSize - 1) * blockSize);
        Vector2 destCenter = destOrigin + new Vector2(worldPixels, worldPixels) / 2f;

        // Fit the dest rect into the viewport with a small margin.
        float zoom = 0.8f * Math.Min(viewportWidth, viewportHeight) / worldPixels;
        Vector2 offset = new(viewportWidth / 2f, viewportHeight / 2f);
        return new Camera2D(destCenter, offset, zoom);
    }

    [Test]
    public void RenderToPixels_IsDeterministic_AcrossRepeatedRenders()
    {
        using GlContext context = GlContext.CreateOffscreen(16, 16);
        using var renderer = new WorldShaderRenderer(context.Gl);

        byte[] first = renderer.RenderToPixels(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, out int w1, out int h1);
        byte[] second = renderer.RenderToPixels(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, out int w2, out int h2);

        Assert.Multiple(() =>
        {
            Assert.That(w2, Is.EqualTo(w1));
            Assert.That(h2, Is.EqualTo(h1));
            Assert.That(second, Is.EqualTo(first), "Repeated renders of the same frame must be byte-identical.");
        });
    }

    [Test]
    public void RenderToPixels_NativeResolution_MatchesWorldColorSize()
    {
        using GlContext context = GlContext.CreateOffscreen(16, 16);
        using var renderer = new WorldShaderRenderer(context.Gl);

        renderer.RenderToPixels(AirFrame(), new Vector2(0, 0), new Vector2(2, 1), WorldLighting.Default, 0f, out int width, out int height);

        Assert.Multiple(() =>
        {
            Assert.That(width, Is.EqualTo(2 * TickableWorld.RegionSize));
            Assert.That(height, Is.EqualTo(1 * TickableWorld.RegionSize));
        });
    }

    [Test]
    public void RenderToPixels_AirWorld_OutputsAirBaseColor()
    {
        using GlContext context = GlContext.CreateOffscreen(16, 16);
        using var renderer = new WorldShaderRenderer(context.Gl);

        byte[] pixels = renderer.RenderToPixels(AirFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 0f, out int width, out int height);

        // Every cell is air, which takes the shader's early-out: finalColor = baseColor (air) * white.
        // Sample the centre texel; it must be the air colour (25, 25, 35) with opaque alpha.
        int centre = (((height / 2) * width) + (width / 2)) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(pixels[centre + 0], Is.EqualTo(25), "red");
            Assert.That(pixels[centre + 1], Is.EqualTo(25), "green");
            Assert.That(pixels[centre + 2], Is.EqualTo(35), "blue");
            Assert.That(pixels[centre + 3], Is.EqualTo(255), "alpha");
        });
    }

    [Test]
    public void RenderToPixels_RespondsToWorldContent()
    {
        using GlContext context = GlContext.CreateOffscreen(16, 16);
        using var renderer = new WorldShaderRenderer(context.Gl);

        byte[] airPixels = renderer.RenderToPixels(AirFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, out _, out _);
        byte[] contentPixels = renderer.RenderToPixels(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, out _, out _);

        Assert.That(contentPixels, Is.Not.EqualTo(airPixels), "Stone/water cells must change the rendered output versus an all-air world.");
    }

    [Test]
    public void RenderToScreen_IsDeterministic_AcrossRepeatedRenders()
    {
        const int width = 256;
        const int height = 192;
        Camera2D camera = FramedSingleRegionCamera(width, height);

        using GlContext context = GlContext.CreateOffscreen(width, height);
        using var renderer = new WorldShaderRenderer(context.Gl);

        byte[] first = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, camera, width, height);
        byte[] second = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, camera, width, height);

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Length.EqualTo(width * height * 4));
            Assert.That(second, Is.EqualTo(first), "Repeated screen renders of the same frame/camera must be byte-identical.");
        });
    }

    [Test]
    public void RenderToScreen_DrawsWorldContent_OverAirBackground()
    {
        const int width = 256;
        const int height = 192;
        Camera2D camera = FramedSingleRegionCamera(width, height);

        using GlContext context = GlContext.CreateOffscreen(width, height);
        using var renderer = new WorldShaderRenderer(context.Gl);

        byte[] pixels = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, camera, width, height);

        // The framebuffer is cleared to the air colour (25, 25, 35); the world quad covers part of it
        // with shaded stone/water. At least one pixel must differ from the clear colour, proving the
        // camera-projected quad actually landed in the viewport.
        bool foundNonBackground = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 25 || pixels[i + 1] != 25 || pixels[i + 2] != 35)
            {
                foundNonBackground = true;
                break;
            }
        }

        Assert.That(foundNonBackground, Is.True, "Expected the projected world quad to cover part of the viewport.");
    }

    [Test]
    public void RenderToScreen_PanningChangesOutput()
    {
        const int width = 256;
        const int height = 192;

        using GlContext context = GlContext.CreateOffscreen(width, height);
        using var renderer = new WorldShaderRenderer(context.Gl);

        Camera2D centered = FramedSingleRegionCamera(width, height);
        Camera2D panned = centered;
        panned.Target += new Vector2(40, 25);

        byte[] before = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, centered, width, height);
        byte[] after = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, panned, width, height);

        Assert.That(after, Is.Not.EqualTo(before), "Panning the camera must change the rendered viewport.");
    }

    [Test]
    public void RenderToScreen_WithGrid_IsDeterministicAndChangesOutput()
    {
        const int width = 256;
        const int height = 192;
        Camera2D camera = FramedSingleRegionCamera(width, height);

        using GlContext context = GlContext.CreateOffscreen(width, height);
        using var renderer = new WorldShaderRenderer(context.Gl);

        byte[] noGrid = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, camera, width, height, drawGrid: false);
        byte[] withGrid1 = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, camera, width, height, drawGrid: true);
        byte[] withGrid2 = renderer.RenderToScreen(ContentFrame(), new Vector2(0, 0), new Vector2(1, 1), WorldLighting.Default, 7.5f, camera, width, height, drawGrid: true);

        Assert.Multiple(() =>
        {
            Assert.That(withGrid2, Is.EqualTo(withGrid1), "Grid rendering must be deterministic.");
            Assert.That(withGrid1, Is.Not.EqualTo(noGrid), "The grid overlay must change the rendered output.");
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
