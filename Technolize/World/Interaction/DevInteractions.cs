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

    public void Tick() {

        const int cursorRadius = 10;

        int keyPressed = Raylib.GetKeyPressed();
        while (keyPressed != 0) {
            if (keyPressed == (int)KeyboardKey.Space) {
                _blockIndex += 1;
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
                            placer.Set(new (x, y), SelectedBlock.Id);
                        }
                    }
                }
            });
        }
    }
}
