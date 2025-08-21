using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Ticking;
namespace Technolize;

public static class Program
{
    public static void Main()
    {
        // --- Initialization ---
        const int screenWidth = 1280;
        const int screenHeight = 720;

        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(screenWidth, screenHeight, "Technolize - World Renderer");
        // Raylib.SetTargetFPS(60);

        // Create the world and generate its initial state.
        TickableWorld world = new ();
        // DevGenerator generator = new (1024, 256);
        // generator.Generate(world);

        const int cursorRadius = 10;

        // Create the ticker instance
        SignatureWorldTicker ticker = new (world);

        // Create the renderer and pass it the world and screen dimensions.
        WorldRenderer renderer = new (world, screenWidth, screenHeight);

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
                int centerX = (int) Math.Floor(worldPos.X);
                int centerY = (int) Math.Floor(worldPos.Y);

                BlockInfo block =
                    Raylib.IsKeyDown(KeyboardKey.LeftShift) ? Blocks.Water :
                        Raylib.IsKeyDown(KeyboardKey.LeftControl) ? Blocks.Stone :
                            Blocks.Sand;

                world.BatchSetBlocks(placer =>
                {
                    // Iterate through the bounding box of the circle.
                    for (int x = centerX - cursorRadius; x <= centerX + cursorRadius; x++)
                    {
                        for (int y = centerY - cursorRadius; y <= centerY + cursorRadius; y++)
                        {
                            int dx = x - centerX;
                            int dy = y - centerY;
                            if (dx * dx + dy * dy <= cursorRadius * cursorRadius)
                            {
                                placer.Set(new (x, y), block.Id);
                            }
                        }
                    }
                });
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
