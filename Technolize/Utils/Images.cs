using Raylib_cs;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.Utils;

public class Images
{
    public static void ViewImage(Image img)
    {
        // Resize window to match image
        Raylib.SetWindowSize(img.Width, img.Height);

        // Bring window to front / focus
        Raylib.SetWindowState(ConfigFlags.AlwaysRunWindow | ConfigFlags.ResizableWindow);

        // Load texture from image
        Texture2D tex = Raylib.LoadTextureFromImage(img);

        // Draw loop, blocks until mouse click
        while (!Raylib.IsMouseButtonPressed(MouseButton.Left) &&
               !Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            Raylib.DrawTexture(tex, 0, 0, Color.White);
            Raylib.EndDrawing();
        }

        // Cleanup
        Raylib.UnloadTexture(tex);

        // Shrink window back to 1x1
        Raylib.SetWindowSize(1, 1);
    }

    public static void PrintImage(string label, Image img)
    {
        Console.Write("\e[0m");
        Console.WriteLine(label);
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                Color c = Raylib.GetImageColor(img, x, y);

                // ANSI escape: 48;2;r;g;b sets background color
                Console.Write($"\e[48;2;{c.R};{c.G};{c.B}m   ");
                Console.ResetColor(); // reset for next block
            }
            // remove color
            Console.Write("\e[0m"); // reset color at end of line
            Console.WriteLine($" | {y}");
        }
        Console.WriteLine();
    }

    public static void PrintImageBlocks(string label, Image img)
    {
        Console.Write("\e[0m");
        Console.WriteLine(label);
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                int blockId = Packing.UnpackColorToInt(Raylib.GetImageColor(img, x, y));
                BlockInfo info = BlockRegistry.GetInfo(blockId);
                Color c = info.Color;

                // ANSI escape: 48;2;r;g;b sets background color
                Console.Write($"\e[48;2;{c.R};{c.G};{c.B}m   ");
                Console.ResetColor(); // reset for next block
            }
            // remove color
            Console.Write("\e[0m"); // reset color at end of line
            Console.WriteLine($" | {y}");
        }
        Console.WriteLine();
    }

    public static void PrintImageDirs(string label, Image img)
    {
        Console.WriteLine(label);
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                Color dir = Raylib.GetImageColor(img, x, y);

                double xDir = dir.R / 127.0 - 1.0;
                double yDir = dir.G / 127.0 - 1.0;

                if (xDir == 0 && yDir == 0)
                {
                    continue;
                }

                Console.WriteLine($"Block ({x}, {y}): Direction = ({xDir:F2}, {yDir:F2})");
            }
        }
        Console.WriteLine();
    }
}
