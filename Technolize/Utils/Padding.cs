using System.Numerics;
using Raylib_cs;
namespace Technolize.Utils;

public static class Padding
{
    public static void PadInput(int padding, Texture2D input, RenderTexture2D output)
    {
        Raylib.BeginTextureMode(output);
        Raylib.ClearBackground(Color.Black);

        // draw the input texture with padding
        Raylib.DrawTexturePro(input,
            new (0, 0, input.Width, input.Height),
            new (padding, padding, input.Width, input.Height),
            Vector2.Zero, 0, Color.White
        );

        Raylib.EndTextureMode();
    }

    public static void UnpadInput(int padding, Texture2D input, RenderTexture2D output)
    {
        Raylib.BeginTextureMode(output);
        Raylib.ClearBackground(Color.Black);

        // draw the input texture without padding
        Raylib.DrawTexturePro(input,
            new (padding, padding, output.Texture.Width, output.Texture.Height),
            new (0, 0, output.Texture.Width, output.Texture.Height),
            Vector2.Zero, 0, Color.White
        );

        Raylib.EndTextureMode();
    }
}
