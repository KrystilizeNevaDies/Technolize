using Technolize.Rendering.Graphics;

namespace Technolize.Test.Rendering;

/// <summary>
/// End-to-end smoke tests for the Silk.NET OpenGL 4.6 rendering foundation. They create a real hidden
/// 4.6 core context, compile a shader, render a full-screen quad into a framebuffer object, and read
/// the pixels back to assert the pipeline works on this machine.
///
/// They are skipped automatically in headless / CI environments where no display (and therefore no GL
/// context) is available.
/// </summary>
[TestFixture]
public class GlFoundationTest
{
    private const string VertexSource = """
        #version 460 core
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aUv;
        out vec2 vUv;
        void main()
        {
            vUv = aUv;
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    [SetUp]
    public void SkipWhenHeadless()
    {
        if (IsHeadlessEnvironment())
        {
            Assert.Ignore("Skipping Silk.NET GPU test - running in headless environment without display support.");
        }
    }

    [Test]
    public void OffscreenContext_RendersSolidColor_AndReadsItBack()
    {
        const string fragmentSource = """
            #version 460 core
            in vec2 vUv;
            out vec4 frag;
            void main()
            {
                frag = vec4(0.25, 0.5, 0.75, 1.0);
            }
            """;

        using GlContext context = GlContext.CreateOffscreen(16, 16);
        using var framebuffer = new GlFramebuffer(context.Gl, 16, 16);
        using GlShaderProgram shader = GlShaderProgram.FromSource(context.Gl, VertexSource, fragmentSource);
        using var quad = new GlFullScreenQuad(context.Gl);

        framebuffer.Bind();
        shader.Use();
        quad.Draw();

        byte[] pixels = framebuffer.ReadPixels();

        // Centre pixel (8, 8): R=0.25, G=0.5, B=0.75, A=1.0 quantised to 8-bit.
        int index = ((8 * 16) + 8) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(pixels[index + 0], Is.EqualTo(64).Within(2), "red");
            Assert.That(pixels[index + 1], Is.EqualTo(128).Within(2), "green");
            Assert.That(pixels[index + 2], Is.EqualTo(191).Within(2), "blue");
            Assert.That(pixels[index + 3], Is.EqualTo(255), "alpha");
        });
    }

    [Test]
    public void OffscreenContext_SamplesUploadedTexture()
    {
        const string fragmentSource = """
            #version 460 core
            in vec2 vUv;
            out vec4 frag;
            uniform sampler2D source;
            void main()
            {
                frag = texture(source, vUv);
            }
            """;

        // A 1x1 opaque orange texture (R=200, G=100, B=20, A=255).
        byte[] texel = { 200, 100, 20, 255 };

        using GlContext context = GlContext.CreateOffscreen(8, 8);
        using var framebuffer = new GlFramebuffer(context.Gl, 8, 8);
        using GlShaderProgram shader = GlShaderProgram.FromSource(context.Gl, VertexSource, fragmentSource);
        using GlTexture texture = GlTexture.CreateRgba8(context.Gl, 1, 1, texel);
        using var quad = new GlFullScreenQuad(context.Gl);

        framebuffer.Bind();
        shader.Use();
        shader.SetTexture("source", texture, 0);
        quad.Draw();

        byte[] pixels = framebuffer.ReadPixels();

        int index = ((4 * 8) + 4) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(pixels[index + 0], Is.EqualTo(200).Within(2), "red");
            Assert.That(pixels[index + 1], Is.EqualTo(100).Within(2), "green");
            Assert.That(pixels[index + 2], Is.EqualTo(20).Within(2), "blue");
            Assert.That(pixels[index + 3], Is.EqualTo(255), "alpha");
        });
    }

    private static bool IsHeadlessEnvironment()
    {
        // Windows always has a display available; the env-var checks below let GPU tests skip under the
        // same CI / headless conditions as the rest of the suite.
        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return true;
        }

        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEADLESS"));
    }
}
