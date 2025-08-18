using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Particle;
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
        GpuTextureWorld world = new GpuTextureWorld();
        // var generator = new DevGenerator(32, 32);
        // generator.Generate(world);
        world.SetBlock(new Vector2(0, 0), Blocks.Stone.Id);

        // Create the ticker instance
        GpuWorldTicker ticker = new GpuWorldTicker(world);

        // Create the renderer and pass it the world and screen dimensions.
        GpuWorldRenderer renderer = new GpuWorldRenderer(world, screenWidth, screenHeight);

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
                Vector2 worldPos = renderer.GetMouseWorldPosition();

                // Floor the coordinates to get the integer grid cell that was clicked.
                int centerX = (int)Math.Floor(worldPos.X);
                int centerY = (int)Math.Floor(worldPos.Y);

                const int radius = 2;

                BlockInfo block = Raylib.IsKeyDown(KeyboardKey.LeftShift) ? Blocks.Water : Blocks.Sand;

                // Iterate through the bounding box of the circle.
                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    for (int y = centerY - radius; y <= centerY + radius; y++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            // Place a Sand block at the valid position.
                            world.SetBlock(new Vector2(x, y), block.Id);
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
