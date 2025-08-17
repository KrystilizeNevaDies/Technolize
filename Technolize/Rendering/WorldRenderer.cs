using System.Numerics;
using Raylib_cs;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.Rendering;

public class WorldRenderer(WorldGrid world, int screenWidth, int screenHeight)
{
    // The size in pixels to draw each block/particle.
    private const int BlockSize = 16;

    private Camera2D _camera = new()
    {
        Target = new Vector2(screenWidth / 2f, screenHeight / 2f),
        Offset = new Vector2(screenWidth / 2f, screenHeight / 2f),
        Rotation = 0.0f,
        Zoom = 1.0f
    };

    /// <summary>
    /// Handles camera controls like panning and zooming. Call this every frame.
    /// </summary>
    public void UpdateCamera()
    {
        // Panning with middle mouse button
        if (Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            var delta = Raylib.GetMouseDelta();
            // Move camera target in the opposite direction of the mouse drag
            _camera.Target -= delta / _camera.Zoom;
        }

        // Zooming with mouse wheel
        var wheelMove = Raylib.GetMouseWheelMove();
        if (wheelMove != 0)
        {
            // Get the world point that is under the mouse
            var mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);

            // Set the offset to the mouse position
            _camera.Offset = Raylib.GetMousePosition();

            // Set the target to the world point under the mouse
            _camera.Target = mouseWorldPos;

            // Apply the zoom
            var zoomAmount = 1.1f;
            if (wheelMove > 0)
            {
                _camera.Zoom *= zoomAmount;
            }
            else
            {
                _camera.Zoom /= zoomAmount;
            }

            // Clamp zoom levels to prevent extreme values
            _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 20.0f);
        }
    }

    private static Dictionary<long, Color> _blockColors = new();

    /// <summary>
    /// Draws the visible portion of the world grid. Call this inside a Raylib.BeginDrawing() block.
    /// </summary>
    public void Draw()
    {
        Raylib.BeginMode2D(_camera);

        // --- View Culling ---
        var (worldStart, worldEnd) = GetVisibleWorldBounds();

        // Loop only through the visible grid cells.
        foreach (var (x, y, blockId) in world.GetBlocksForRendering(worldStart, worldEnd))
        {
            if (!_blockColors.TryGetValue(blockId, out var color))
            {
                // If we haven't seen this block type before, get its color.
                var block = BlockRegistry.GetInfo(blockId);
                color = block.Color;
                _blockColors[blockId] = color; // Cache the color for future use
            }

            // Draw the block as a rectangle.
            Raylib.DrawRectangle(x * BlockSize, -y * BlockSize, BlockSize, BlockSize, color);

            // // if this block needs ticking, draw a debug outline
            // if (world.NeedsTicking.Contains(new Vector2(x, y)))
            // {
            //     // Draw a red outline around the block to indicate it needs ticking.
            //     Raylib.DrawRectangleLinesEx(new Rectangle(x * BlockSize, -y * BlockSize, BlockSize, BlockSize), 4.0f, Color.Red);
            // }
        }

        // draw grid lines
        var gridColor = new Color(255, 255, 255, 64);
        double targetGridCount = 512; // Target number of grid lines
        double worldWidth = (worldEnd.X - worldStart.X) * BlockSize;
        double worldHeight = (worldEnd.Y - worldStart.Y) * BlockSize;
        double variableGridSize = (int) Math.Max(1, (Math.Max(worldWidth, worldHeight) / targetGridCount));

        // use the next power of 2 for grid size
        var gridSize = 1;
        while (gridSize < variableGridSize)
        {
            gridSize *= 2;
        }

        for (var x = worldStart.X; x <= worldEnd.X; x++)
        {
            if (x % gridSize != 0) continue; // Skip lines that are not multiples of gridSize
            var worldGridStart = new Vector2(x * BlockSize, -worldStart.Y * BlockSize);
            var worldGridEnd = new Vector2(x * BlockSize, -worldEnd.Y * BlockSize);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, 2.0f / _camera.Zoom, gridColor);
        }

        for (var y = worldStart.Y; y <= worldEnd.Y; y++)
        {
            if (y % gridSize != 0) continue; // Skip lines that are not multiples of gridSize
            var worldGridStart = new Vector2(worldStart.X * BlockSize, -y * BlockSize);
            var worldGridEnd = new Vector2(worldEnd.X * BlockSize, -y * BlockSize);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, 2.0f / _camera.Zoom, gridColor);
        }

        Raylib.EndMode2D();

        // --- UI ---
        // Draw UI elements outside of the camera mode so they are not affected by zoom/pan.
        Raylib.DrawText($"Visible area: ({worldStart.X},{worldStart.Y}) to ({worldEnd.X},{worldEnd.Y})", 10, 10, 20, Color.White);
        Raylib.DrawFPS(10, 40);

        // target position
        Raylib.DrawText($"Camera Offset: ({_camera.Offset.X:F2}, {_camera.Offset.Y:F2})", 10, 70, 20, Color.White);
        Raylib.DrawText($"Camera Target: ({_camera.Target.X:F2}, {_camera.Target.Y:F2})", 10, 100, 20, Color.White);
        var diff = _camera.Offset - _camera.Target;
        Raylib.DrawText($"Camera Offset - Target: ({diff.X:F2}, {diff.Y:F2})", 10, 120, 20, Color.White);

        var worldPos = GetMouseWorldPosition();
        // floor the coordinates to get the integer grid cell that was clicked.
        worldPos = worldPos with
        {
            X = (float) Math.Floor(worldPos.X),
            Y = (float) Math.Floor(worldPos.Y)
        };
        Raylib.DrawText($"Mouse World Position: ({worldPos.X:F2}, {worldPos.Y:F2})", 10, 150, 20, Color.White);
    }

    public (Vector2 start, Vector2 end) GetVisibleWorldBounds()
    {
        // Calculate the top-left and bottom-right corners of the screen in world coordinates.
        var screenTopLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 0), _camera);
        var screenBottomRight = Raylib.GetScreenToWorld2D(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), _camera);

        // Convert world coordinates to grid coordinates.
        var offset = (screenTopLeft.Y) * -2.0 - Raylib.GetScreenHeight() / _camera.Zoom;
        var worldStartX = (int) Math.Floor(screenTopLeft.X / BlockSize);
        var worldStartY = (int) Math.Floor((screenTopLeft.Y + offset) / BlockSize);
        var worldEndX = (int) Math.Ceiling(screenBottomRight.X / BlockSize);
        var worldEndY = (int) Math.Ceiling((screenBottomRight.Y + offset) / BlockSize);

        return (new Vector2(worldStartX, worldStartY), new Vector2(worldEndX, worldEndY));
    }

    /// <summary>
    /// Converts the current mouse screen position to its corresponding world coordinate.
    /// </summary>
    /// <returns>The mouse position in world coordinates.</returns>
    public Vector2 GetMouseWorldPosition()
    {
        var raylibWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);

        // flip y
        raylibWorld.Y = -raylibWorld.Y;

        // Convert to block coordinates
        return raylibWorld / BlockSize;
    }
}
