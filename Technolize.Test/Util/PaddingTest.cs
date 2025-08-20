using Raylib_cs;
using Technolize.Utils;
namespace Technolize.Test.World;

[TestFixture]
public class PaddingTest
{

    [Test]
    [RaylibWindow]
    public void TestPadding()
    {
        // generate a texture with some content
        int size = 16;
        int padding = 4;
        Image inputImg = Raylib.GenImageGradientLinear(size, size, 0, Color.Red, Color.Blue);
        Texture2D inputTexture = Raylib.LoadTextureFromImage(inputImg);

        // create a render target for the padded output
        RenderTexture2D outputTarget = Raylib.LoadRenderTexture(size + padding * 2, size + padding * 2);
        Padding.PadInput(padding, inputTexture, outputTarget);

        // unpad the texture
        RenderTexture2D unpaddedTarget = Raylib.LoadRenderTexture(size, size);
        Padding.UnpadInput(padding, outputTarget.Texture, unpaddedTarget);

        // assert that the unpadded texture matches the original input
        Image originalImg = Raylib.LoadImageFromTexture(inputTexture);
        Image outputImg = Raylib.LoadImageFromTexture(unpaddedTarget.Texture);

        Images.PrintImage("original:", originalImg);
        Images.PrintImage("output:", outputImg);

        for (int x = 0; x < outputImg.Width; x++)
        {
            for (int y = 0; y < outputImg.Height; y++)
            {
                Color originalColor = Raylib.GetImageColor(originalImg, x, y);
                Color outputColor = Raylib.GetImageColor(outputImg, x, y);

                Assert.That(outputColor.R, Is.EqualTo(originalColor.R).Within(2), $"Pixel mismatch at ({x}, {y}): expected {originalColor} but got {outputColor}");
                Assert.That(outputColor.G, Is.EqualTo(originalColor.G).Within(2), $"Pixel mismatch at ({x}, {y}): expected {originalColor} but got {outputColor}");
                Assert.That(outputColor.B, Is.EqualTo(originalColor.B).Within(2), $"Pixel mismatch at ({x}, {y}): expected {originalColor} but got {outputColor}");
            }
        }
    }
}
