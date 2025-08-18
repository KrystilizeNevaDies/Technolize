using Raylib_cs;
using Technolize.Utils;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.Test.World;

[TestFixture]
public class ShaderTest
{
    [Test]
    [RaylibWindow]
    public void MinimalShaderOutput()
    {
        string shaderCode = @"
#version 330
in vec2 fragTexCoord;
out vec4 fragColor;
void main() {
    fragColor = vec4(0.5, 0.0, 0.0, 1.0);
}";

        Image texture0 = Raylib.GenImageColor(4, 4, Color.Black);
        Images.PrintImage("Input texture:", texture0);
        Image result = RunShaderCode(4, 4, shaderCode, texture0);
        Images.PrintImage( "After minimal shader:", result);

        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
        {
            Color c = Raylib.GetImageColor(result, x, y);
            Assert.That(c.R, Is.EqualTo(127));
            Assert.That(c.G, Is.EqualTo(0));
            Assert.That(c.B, Is.EqualTo(0));
        }

        Raylib.UnloadImage(texture0);
        Raylib.UnloadImage(result);
    }

    [Test]
    [RaylibWindow]
    public void CoordShaderGradient()
    {
        int texSize = 16;

        // Shader: color based on fragTexCoord
        string shaderCode = @"
#version 330
in vec2 fragTexCoord;
out vec4 fragColor;
void main() {
    // Simple gradient: R = x coord, G = y coord
    fragColor = vec4(fragTexCoord.x, fragTexCoord.y, 0.0, 1.0);
}";

        // Run shader on image
        Image texture0 = Raylib.GenImageColor(texSize, texSize, Color.Black);
        Images.PrintImage("Input texture (black):", texture0);
        Image result = RunShaderCode(texSize, texSize, shaderCode, texture0);
        Images.PrintImage( "Shader output (gradient):", result);

        // Simple assertion: check corners roughly match expected gradient
        // Flip Y: shader Y=0 is bottom row in the image
        Color topLeft = Raylib.GetImageColor(result, 0, texSize - 1);        // shader bottom-left
        Color bottomRight = Raylib.GetImageColor(result, texSize - 1, 0);    // shader top-right

        Assert.That(topLeft.R, Is.LessThan(10));
        Assert.That(topLeft.G, Is.LessThan(10));
        Assert.That(bottomRight.R, Is.GreaterThan(240));
        Assert.That(bottomRight.G, Is.GreaterThan(240));

        Raylib.UnloadImage(result);
    }

    [Test]
    [RaylibWindow]
    public void ShaderReadsTextureValue()
    {
        int texSize = 16;

        string shaderCode = @"
    #version 330
    in vec2 fragTexCoord;
    out vec4 fragColor;

    uniform sampler2D auxTexture;

    void main() {
        fragColor = texture(auxTexture, fragTexCoord);
    }";

        Shader shader = Raylib.LoadShaderFromMemory(null, shaderCode);

        // setup aux texture to read
        Image auxTextureImg = Raylib.GenImageGradientLinear(
            texSize, texSize, 0,
            new Color(0, 0, 255, 255),
            new Color(0, 255, 0, 255)
        );
        Images.PrintImage("Texture to read:", auxTextureImg);
        Texture2D auxTexture = Raylib.LoadTextureFromImage(auxTextureImg);

        // setup rendering context
        Image renderingBoundsImg = Raylib.GenImageColor(texSize, texSize, new Color(0, 0, 0, 0));
        Texture2D renderingBounds = Raylib.LoadTextureFromImage(renderingBoundsImg);
        RenderTexture2D target = Raylib.LoadRenderTexture(texSize, texSize);

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(shader);

        // Bind the texture AFTER beginning shader mode
        Raylib.SetShaderValueTexture(shader, Raylib.GetShaderLocation(shader, "auxTexture"), auxTexture);

        Raylib.DrawTexture(renderingBounds, 0, 0, Color.White);

        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        // Read back the result
        Image result = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref result);

        Images.PrintImage("Shader output (texture read):", result);

        // Check that shader reads texture correctly
        for (int y = 0; y < result.Height; y++)
        for (int x = 0; x < result.Width; x++)
        {
            Color resultColor = Raylib.GetImageColor(result, x, y);
            Color expectedColor = Raylib.GetImageColor(auxTextureImg, x, y);
            Assert.That(resultColor.R, Is.EqualTo(expectedColor.R).Within(1));
            Assert.That(resultColor.G, Is.EqualTo(expectedColor.G).Within(1));
            Assert.That(resultColor.B, Is.EqualTo(expectedColor.B).Within(1));
        }

        Raylib.UnloadShader(shader);
        Raylib.UnloadRenderTexture(target);
        Raylib.UnloadImage(result);
        Raylib.UnloadTexture(auxTexture);
        Raylib.UnloadImage(auxTextureImg);
        Raylib.UnloadTexture(renderingBounds);
        Raylib.UnloadImage(renderingBoundsImg);
    }

    private static Image RunShaderCode(int width, int height, string shaderCode, Image texture0)
    {
        Shader shader = Raylib.LoadShaderFromMemory(null, shaderCode);
        return RunShader(width, height, shader, texture0);
    }

    private static Image RunShader(int width, int height, Shader shader, Image texture0)
    {

        Texture2D tex = Raylib.LoadTextureFromImage(texture0);

        RenderTexture2D target = Raylib.LoadRenderTexture(width, height);

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(shader);
        Raylib.DrawTexture(tex, 0, 0, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        // Read back the result
        Image result = Raylib.LoadImageFromTexture(target.Texture);

        // Cleanup
        Raylib.UnloadTexture(tex);
        Raylib.UnloadShader(shader);
        Raylib.UnloadRenderTexture(target);

        return result;
    }
}
