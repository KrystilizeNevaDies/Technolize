using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Interaction;
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


        // Create the ticker instance
        SignatureWorldTicker ticker = new (world);

        // Create the renderer and pass it the world and screen dimensions.
        WorldRenderer renderer = new (world, screenWidth, screenHeight);

        DevInteractions interactions = new (world, renderer);

        // --- Main Game Loop ---
        while (!Raylib.WindowShouldClose())
        {
            // --- Update ---
            renderer.UpdateCamera();

            ticker.Tick();

            // --- Handle Mouse Input ---
            interactions.Tick();

            // --- Drawing ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            renderer.Draw();

            // render the selected block info
            string blockInfo = $"Selected Block: {interactions.SelectedBlock.Name} ({interactions.SelectedBlock.Id})";
            Raylib.DrawText(blockInfo, 10, Raylib.GetScreenHeight() - 20, 20, Color.White);

            Raylib.EndDrawing();
        }

        // --- De-Initialization ---
        Raylib.CloseWindow();
    }
}
