using System.Numerics;
using Raylib_cs;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.Rendering;

public class WorldRenderer(IWorld world, int screenWidth, int screenHeight)
{

    private const int BlockSize = 16;

    private Camera2D _camera = new()
    {
        Target = new Vector2(screenWidth / 2f, screenHeight / 2f),
        Offset = new Vector2(screenWidth / 2f, screenHeight / 2f),
        Rotation = 0.0f,
        Zoom = 1.0f
    };

    private static readonly Dictionary<long, Color> BlockColors = new();

    public void UpdateCamera()
    {
        if (Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            Vector2 delta = Raylib.GetMouseDelta();
            _camera.Target -= delta / _camera.Zoom;
        }

        float wheelMove = Raylib.GetMouseWheelMove();
        if (wheelMove != 0)
        {
            Vector2 mouseWorldPos = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);
            _camera.Offset = Raylib.GetMousePosition();
            _camera.Target = mouseWorldPos;

            const float zoomAmount = 1.1f;
            _camera.Zoom *= wheelMove > 0 ? zoomAmount : 1 / zoomAmount;
            _camera.Zoom = Math.Clamp(_camera.Zoom, 0.1f, 20.0f);
        }
    }

    public void Draw()
    {
        Raylib.BeginMode2D(_camera);

        (Vector2 worldStart, Vector2 worldEnd) = GetVisibleWorldBounds();

        foreach ((Vector2 position, long blockId) in world.GetBlocks(worldStart, worldEnd))
        {
            if (!BlockColors.TryGetValue(blockId, out Color color))
            {
                BlockInfo block = BlockRegistry.GetInfo(blockId);
                color = block.Color;
                BlockColors[blockId] = color;
            }

            Raylib.DrawRectangle(
                (int)position.X * BlockSize,
                (int)-position.Y * BlockSize,
                BlockSize,
                BlockSize,
                color);

            // if (this block needs ticking, draw a debug outline)
            // if (_ticker.NeedsTicking.Contains(new Vector2(x, y)))
            // {
            //     Draw a red outline around the block to indicate it needs ticking.
            //     Raylib.DrawRectangleLinesEx(new Rectangle(x * BlockSize, -y * BlockSize, BlockSize, BlockSize), 4.0f, Color.Red);
            // }
        }

        Color gridColor = new Color(255, 255, 255, 64);
        const double targetGridCount = 512;
        double worldWidth = (worldEnd.X - worldStart.X) * BlockSize;
        double worldHeight = (worldEnd.Y - worldStart.Y) * BlockSize;
        double variableGridSize = Math.Max(1, Math.Max(worldWidth, worldHeight) / targetGridCount);

        int gridSize = 1;
        while (gridSize < variableGridSize)
        {
            gridSize *= 2;
        }

        for (int x = (int)worldStart.X; x <= (int)worldEnd.X; x++)
        {
            if (x % gridSize != 0) continue;
            Vector2 worldGridStart = new Vector2(x * BlockSize, -worldStart.Y * BlockSize);
            Vector2 worldGridEnd = new Vector2(x * BlockSize, -worldEnd.Y * BlockSize);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, 2.0f / _camera.Zoom, gridColor);
        }

        for (int y = (int)worldStart.Y; y <= (int)worldEnd.Y; y++)
        {
            if (y % gridSize != 0) continue;
            Vector2 worldGridStart = new Vector2(worldStart.X * BlockSize, -y * BlockSize);
            Vector2 worldGridEnd = new Vector2(worldEnd.X * BlockSize, -y * BlockSize);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, 2.0f / _camera.Zoom, gridColor);
        }

        Raylib.EndMode2D();

        Raylib.DrawText($"Visible area: ({worldStart.X},{worldStart.Y}) to ({worldEnd.X},{worldEnd.Y})", 10, 10, 20, Color.White);
        Raylib.DrawFPS(10, 40);
        Raylib.DrawText($"Camera Offset: ({_camera.Offset.X:F2}, {_camera.Offset.Y:F2})", 10, 70, 20, Color.White);
        Raylib.DrawText($"Camera Target: ({_camera.Target.X:F2}, {_camera.Target.Y:F2})", 10, 100, 20, Color.White);
        Vector2 diff = _camera.Offset - _camera.Target;
        Raylib.DrawText($"Camera Offset - Target: ({diff.X:F2}, {diff.Y:F2})", 10, 120, 20, Color.White);

        Vector2 worldPos = GetMouseWorldPosition();
        worldPos = worldPos with
        {
            X = (float)Math.Floor(worldPos.X),
            Y = (float)Math.Floor(worldPos.Y)
        };
        Raylib.DrawText($"Mouse World Position: ({worldPos.X:F2}, {worldPos.Y:F2})", 10, 150, 20, Color.White);
    }

    public (Vector2 start, Vector2 end) GetVisibleWorldBounds()
    {
        Vector2 screenTopLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 0), _camera);
        Vector2 screenBottomRight = Raylib.GetScreenToWorld2D(new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), _camera);

        double offset = screenTopLeft.Y * -2.0 - Raylib.GetScreenHeight() / _camera.Zoom;
        int worldStartX = (int)Math.Floor(screenTopLeft.X / BlockSize);
        int worldStartY = (int)Math.Floor((screenTopLeft.Y + offset) / BlockSize);
        int worldEndX = (int)Math.Ceiling(screenBottomRight.X / BlockSize) + 1;
        int worldEndY = (int)Math.Ceiling((screenBottomRight.Y + offset) / BlockSize) + 1;

        return (new Vector2(worldStartX, worldStartY), new Vector2(worldEndX, worldEndY));
    }

    public Vector2 GetMouseWorldPosition()
    {
        Vector2 raylibWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);
        raylibWorld.Y = -raylibWorld.Y;
        return raylibWorld / BlockSize;
    }
}
