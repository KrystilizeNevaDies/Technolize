using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;
namespace Technolize;

public static class Program
{
    public static void Main()
    {
        // --- Initialization ---
        const int screenWidth = 1280;
        const int screenHeight = 720;

        Raylib.InitWindow(screenWidth, screenHeight, "Technolize - World Renderer");
        Raylib.SetTargetFPS(60);

        // Create the world and generate its initial state.
        var world = new WorldGrid();
        world.GenerateDevWorld();

        // Create the ticker instance
        var ticker = new PatternWorldTicker(world);

        // Create the renderer and pass it the world and screen dimensions.
        var renderer = new WorldRenderer(world, screenWidth, screenHeight);

        // --- Main Game Loop ---
        while (!Raylib.WindowShouldClose())
        {
            // --- Update ---
            renderer.UpdateCamera();

            ticker.Tick();

            // --- Handle Mouse Input ---
            if (Raylib.IsMouseButtonDown(MouseButton.Right))
            {
                // Get the click position in world coordinates from the renderer.
                var worldPos = renderer.GetMouseWorldPosition();

                // Floor the coordinates to get the integer grid cell that was clicked.
                var centerX = (int)Math.Floor(worldPos.X);
                var centerY = (int)Math.Floor(worldPos.Y);

                // Define the radius of the circle. A radius of 2 creates a 5x5 bounding box.
                const int radius = 10;

                var block = Raylib.IsKeyDown(KeyboardKey.LeftShift) ? Blocks.Water : Blocks.Sand;

                // Iterate through the bounding box of the circle.
                for (var x = centerX - radius; x <= centerX + radius; x++)
                {
                    for (var y = centerY - radius; y <= centerY + radius; y++)
                    {
                        var dx = x - centerX;
                        var dy = y - centerY;
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            // Place a Sand block at the valid position.
                            world.SetBlock(new Vector2(x, y), block);
                        }
                    }
                }
            }

            // --- Drawing ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            renderer.Draw();

            Raylib.EndDrawing();
        }

        // --- De-Initialization ---
        Raylib.CloseWindow();
    }
}
