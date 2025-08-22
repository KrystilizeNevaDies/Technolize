using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World.Block;
namespace Technolize.World.Interaction;

public class DevInteractions(TickableWorld world, WorldRenderer renderer) {

    public BlockInfo SelectedBlock {
        get => Blocks.AllBlocks().OrderBy(b => b.Id).ElementAt(_blockIndex % Blocks.AllBlocks().Count);
    }

    private int _blockIndex;
    public int BrushSize { get; private set; } = 10;

    public void Tick() {

        int keyPressed = Raylib.GetKeyPressed();
        while (keyPressed != 0) {
            if (keyPressed == (int)KeyboardKey.Space) {
                _blockIndex += 1;
            }

            // brush size controls
            if (keyPressed == (int)KeyboardKey.Up) {
                BrushSize = Math.Min(BrushSize + 1, 100); // Limit max brush size
            }
            if (keyPressed == (int)KeyboardKey.Down) {
                BrushSize = Math.Max(BrushSize - 1, 1); // Limit min brush size
            }

            keyPressed = Raylib.GetKeyPressed();
        }

        if (Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            // Get the click position in world coordinates from the renderer.
            Vector2 worldPos = renderer.GetMouseWorldPosition();

            // Floor the coordinates to get the integer grid cell that was clicked.
            int centerX = (int) Math.Floor(worldPos.X);
            int centerY = (int) Math.Floor(worldPos.Y);

            uint selectedBlockId = SelectedBlock.Id;
            world.BatchSetBlocks(placer => {
                // Iterate through the bounding box of the circle.
                for (int x = centerX - BrushSize; x <= centerX + BrushSize; x++)
                {
                    for (int y = centerY - BrushSize; y <= centerY + BrushSize; y++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        if (dx * dx + dy * dy <= BrushSize * BrushSize)
                        {
                            placer.Set(new (x, y), selectedBlockId);
                        }
                    }
                }
            });
        }
    }
}
