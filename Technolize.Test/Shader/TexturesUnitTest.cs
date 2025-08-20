using System.Numerics;
using Raylib_cs;
namespace Technolize.Test.Shader;

public class TexturesUnitTest
{
    [Test]
    [RaylibWindow]
    public void CanWriteAndReadPixelInRenderTexture()
    {
        const int textureSize = 32;
        Vector2 testPosition = new Vector2(10, 20);

        // This color uses the same packing logic as the GpuTextureWorld
        // to ensure the alpha channel is fully opaque.
        const long blockId = 2;
        Color colorToWrite = new Color(
            (byte)(blockId >> 16 & 0xFF),
            (byte)(blockId >> 8 & 0xFF),
            (byte)(blockId & 0xFF),
            (byte) 255
        );

        RenderTexture2D targetTexture = Raylib.LoadRenderTexture(textureSize, textureSize);

        Raylib.BeginTextureMode(targetTexture);
        Raylib.ClearBackground(new Color(0, 0, 0, 0)); // Start with a known blank state
        Raylib.DrawPixelV(testPosition, colorToWrite);
        Raylib.EndTextureMode();

        Image downloadedImage = Raylib.LoadImageFromTexture(targetTexture.Texture);

        // Apply the Y-flip correction
        int flippedY = textureSize - 1 - (int)testPosition.Y;
        Color colorRead = Raylib.GetImageColor(downloadedImage, (int)testPosition.X, flippedY);

        long resultId = colorRead.R << 16 | colorRead.G << 8 | colorRead.B;

        Raylib.UnloadImage(downloadedImage);
        Raylib.UnloadRenderTexture(targetTexture);

        Assert.That(resultId, Is.EqualTo(blockId));
    }
}
