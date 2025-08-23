using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation;
using Technolize.World.Generation.Noise;
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
        const double framesPerSecond = 120.0;
        const double ticksPerSecond = -1;
        // Raylib.SetTargetFPS((int) framesPerSecond);

        // Create the world and generate its initial state.
        TickableWorld world = new ();
        world.Generator = new SimpleNoiseGenerator();
        // world.Generator = new BlankGenerator();

        // Create the ticker instance
        SignatureWorldTicker ticker = new (world);

        // Create the renderer and pass it the world and screen dimensions.
        WorldRenderer renderer = new (world, screenWidth, screenHeight);

        DevInteractions interactions = new (world, renderer);

        // kick off world updates
        world.GetBlock(new Vector2(0, 0));
        world.ProcessUpdate(new Vector2(0, 0));

        Stopwatch totalTime = new();
        totalTime.Start();

        int timesTicked = 0;

        // --- Main Game Loop ---
        while (!Raylib.WindowShouldClose())
        {
            // Tick the world if enough time has passed.
            if (ticksPerSecond < 0.0 || timesTicked / ticksPerSecond < totalTime.Elapsed.TotalSeconds)
            {
                timesTicked++;
                ticker.Tick();
            }

            // --- Update ---
            renderer.UpdateCamera();

            // --- Handle Mouse Input ---
            interactions.Tick();

            // --- Drawing ---
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            renderer.Draw();

            // render the interaction information
            string brushSize = $"Brush Size: {interactions.BrushSize}";
            Raylib.DrawText(brushSize, 10, Raylib.GetScreenHeight() - 50, 20, Color.White);

            string blockInfo = $"Selected Block: {interactions.SelectedBlock.Name} ({interactions.SelectedBlock.Id})";
            Raylib.DrawText(blockInfo, 10, Raylib.GetScreenHeight() - 20, 20, Color.White);

            Raylib.EndDrawing();
        }

        // --- De-Initialization ---
        Raylib.CloseWindow();
    }
}
