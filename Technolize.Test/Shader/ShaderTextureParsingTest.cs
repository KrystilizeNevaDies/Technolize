using System.Numerics;
using Raylib_cs;
namespace Technolize.Test.Shader;

[TestFixture]
public class ShaderTextureParsingTest
{
    private const string PassthroughFragmentShader = @"
    #version 330
    uniform sampler2D inputTexture;
    in vec2 fragTexCoord;
    out vec4 finalColor;

    void main()
    {
        finalColor = texture(inputTexture, fragTexCoord);
    }
    ";

    [Test]
    [RaylibWindow]
    public void ShaderCanProcess_1x1_Texture()
    {
        Color customColor = new Color(128, 0, 128, 255); // Purple

        Color[] pixels = new Color[1];
        pixels[0] = customColor;

        Texture2D inputTexture;
        unsafe
        {
            fixed (Color* ptr = pixels)
            {
                Image image = new Image {
                    Data = ptr,
                    Width = 1,
                    Height = 1,
                    Format = PixelFormat.UncompressedR8G8B8A8,
                    Mipmaps = 1
                };
                inputTexture = Raylib.LoadTextureFromImage(image);
            }
        }

        Raylib_cs.Shader passthroughShader = Raylib.LoadShaderFromMemory(null, PassthroughFragmentShader);
        RenderTexture2D outputTexture = Raylib.LoadRenderTexture(1, 1);
        int textureUniformLocation = Raylib.GetShaderLocation(passthroughShader, "inputTexture");

        Raylib.BeginTextureMode(outputTexture);
        Raylib.ClearBackground(Color.Blank);
        Raylib.BeginShaderMode(passthroughShader);
        Raylib.SetShaderValueTexture(passthroughShader, textureUniformLocation, inputTexture);
        Raylib.DrawRectangle(0, 0, 1, 1, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        Image resultImage = Raylib.LoadImageFromTexture(outputTexture.Texture);
        Color resultColor = Raylib.GetImageColor(resultImage, 0, 0);

        Assert.That(resultColor.R, Is.EqualTo(customColor.R));
        Assert.That(resultColor.G, Is.EqualTo(customColor.G));
        Assert.That(resultColor.B, Is.EqualTo(customColor.B));
        Assert.That(resultColor.A, Is.EqualTo(customColor.A));

        Raylib.UnloadImage(resultImage);
        Raylib.UnloadTexture(inputTexture);
        Raylib.UnloadRenderTexture(outputTexture);
        Raylib.UnloadShader(passthroughShader);
    }

    [TestCase(2)]
    [TestCase(7)]
    [TestCase(16)]
    [RaylibWindow]
    public void ShaderCanProcess_NxN_Texture(int textureSize)
    {
        Color[] pixels = new Color[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                pixels[y * textureSize + x] = new Color(x % 256, y % 256, (x + y) % 256, 255);
            }
        }

        Texture2D inputTexture;
        unsafe
        {
            fixed (Color* ptr = pixels)
            {
                Image image = new Image {
                    Data = ptr,
                    Width = textureSize,
                    Height = textureSize,
                    Format = PixelFormat.UncompressedR8G8B8A8,
                    Mipmaps = 1
                };
                inputTexture = Raylib.LoadTextureFromImage(image);
            }
        }

        Raylib_cs.Shader passthroughShader = Raylib.LoadShaderFromMemory(null, PassthroughFragmentShader);
        RenderTexture2D outputTexture = Raylib.LoadRenderTexture(textureSize, textureSize);
        int textureUniformLocation = Raylib.GetShaderLocation(passthroughShader, "inputTexture");

        Raylib.BeginTextureMode(outputTexture);
        Raylib.ClearBackground(Color.Blank);
        Raylib.BeginShaderMode(passthroughShader);
        Raylib.SetShaderValueTexture(passthroughShader, textureUniformLocation, inputTexture);
        Raylib.DrawTextureRec(
            inputTexture,
            new Rectangle(0, 0, textureSize, textureSize),
            new Vector2(0, 0),
            Color.White
        );
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        Image resultImage = Raylib.LoadImageFromTexture(outputTexture.Texture);

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                int flippedY = textureSize - 1 - y;
                Color resultColor = Raylib.GetImageColor(resultImage, x, flippedY);
                Color expectedColor = pixels[y * textureSize + x];

                Assert.That(resultColor.R, Is.EqualTo(expectedColor.R), $"Mismatch at ({x},{y})");
                Assert.That(resultColor.G, Is.EqualTo(expectedColor.G), $"Mismatch at ({x},{y})");
                Assert.That(resultColor.B, Is.EqualTo(expectedColor.B), $"Mismatch at ({x},{y})");
                Assert.That(resultColor.A, Is.EqualTo(expectedColor.A), $"Mismatch at ({x},{y})");
            }
        }

        Raylib.UnloadImage(resultImage);
        Raylib.UnloadTexture(inputTexture);
        Raylib.UnloadRenderTexture(outputTexture);
        Raylib.UnloadShader(passthroughShader);
    }
}
