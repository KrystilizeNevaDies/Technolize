using System.Numerics;
using Raylib_cs;
using Technolize.World;

namespace Technolize.Rendering;

/// <summary>
/// A renderer specifically optimized for the GpuTextureWorld.
/// It works by drawing the region textures directly from the GPU to the screen,
/// avoiding the costly process of reading pixel data back to the CPU.
/// </summary>
public class GpuWorldRenderer(GpuTextureWorld world, int screenWidth, int screenHeight)
{
    private const int BlockSize = 16;

    private Camera2D _camera = new()
    {
        Target = new (screenWidth / 2f, screenHeight / 2f),
        Offset = new (screenWidth / 2f, screenHeight / 2f),
        Rotation = 0.0f,
        Zoom = 1.0f
    };

    public void UpdateCamera()
    {
        // Pan with the left mouse button
        if (Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            Vector2 delta = Raylib.GetMouseDelta();
            _camera.Target -= delta / _camera.Zoom;
        }

        // Zoom with the mouse wheel
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

        // The source rectangle for drawing the texture.
        Rectangle sourceRec = new Rectangle(0, 0, GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize);

        // Iterate through each region that exists in the world
        foreach ((Vector2 regionPos, RenderTexture2D regionTexture) in world.Regions)
        {
            Vector2 alignedPos = regionPos with { Y = regionPos.Y + 1.0f };
            // Calculate the destination rectangle on the screen
            Rectangle destRec = new Rectangle(
                alignedPos.X * GpuTextureWorld.RegionSize * BlockSize,
                alignedPos.Y * GpuTextureWorld.RegionSize * BlockSize - GpuTextureWorld.RegionSize * BlockSize * 2, // adjust y-pos for flipping
                GpuTextureWorld.RegionSize * BlockSize,
                GpuTextureWorld.RegionSize * BlockSize
            );

            // Culling: Check if the region is visible before drawing
            // if (Raylib.CheckCollisionRecs(GetCameraViewRec(), destRec))
            // {
            //     Raylib.DrawTexturePro(regionTexture.Texture, sourceRec, destRec, Vector2.Zero, 0, Color.White);
            // }
            Raylib.DrawTexturePro(regionTexture.Texture, sourceRec, destRec, Vector2.Zero, 0, Color.White);
        }

        DrawGrid();

        Raylib.EndMode2D();

        DrawDebugInfo();
    }

    private void DrawGrid()
    {
        (Vector2 worldStart, Vector2 worldEnd) = GetVisibleWorldBounds();
        Color gridColor = new (255, 255, 255, 64);
        const double targetGridCount = 32;
        double worldWidth = (worldEnd.X - worldStart.X);
        double worldHeight = (worldEnd.Y - worldStart.Y);
        double variableGridSize = Math.Max(1, Math.Max(worldWidth, worldHeight) / targetGridCount);

        int gridSize = 1;
        while (gridSize < variableGridSize)
        {
            gridSize *= 2;
        }
        if (gridSize < 1) gridSize = 1;

        for (float x = (float)Math.Floor(worldStart.X / gridSize) * gridSize; x <= worldEnd.X; x += gridSize)
        {
            Raylib.DrawLineV(
                new (x * BlockSize, -worldStart.Y * BlockSize),
                new (x * BlockSize, -worldEnd.Y * BlockSize),
                gridColor);
        }

        for (float y = (float)Math.Floor(worldStart.Y / gridSize) * gridSize; y <= worldEnd.Y; y += gridSize)
        {
            Raylib.DrawLineV(
                new (worldStart.X * BlockSize, -y * BlockSize),
                new (worldEnd.X * BlockSize, -y * BlockSize),
                gridColor);
        }
    }

    private void DrawDebugInfo()
    {
        (Vector2 worldStart, Vector2 worldEnd) = GetVisibleWorldBounds();
        Raylib.DrawText($"Visible area: ({worldStart.X:F0},{worldStart.Y:F0}) to ({worldEnd.X:F0},{worldEnd.Y:F0})", 10, 10, 20, Color.White);
        Raylib.DrawFPS(10, 40);

        Vector2 worldPos = GetMouseWorldPosition();
        worldPos = worldPos with
        {
            X = (float)Math.Floor(worldPos.X),
            Y = (float)Math.Floor(worldPos.Y)
        };
        Raylib.DrawText($"Mouse World Position: ({worldPos.X:F2}, {worldPos.Y:F2})", 10, 70, 20, Color.White);
        Raylib.DrawText($"Zoom: {_camera.Zoom:F2}", 10, 100, 20, Color.White);
    }

    private (Vector2 start, Vector2 end) GetVisibleWorldBounds()
    {
        Vector2 topLeft = Raylib.GetScreenToWorld2D(new (0, 0), _camera);
        Vector2 bottomRight = Raylib.GetScreenToWorld2D(new (Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), _camera);

        // Convert from Raylib's world coordinates to our block coordinates
        topLeft.Y = -topLeft.Y;
        bottomRight.Y = -bottomRight.Y;

        return (
                new ((float)Math.Floor(topLeft.X / BlockSize), (float)Math.Floor(bottomRight.Y / BlockSize)),
                new ((float)Math.Ceiling(bottomRight.X / BlockSize), (float)Math.Ceiling(topLeft.Y / BlockSize))
            );
    }

    private Rectangle GetCameraViewRec()
    {
        Vector2 topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, _camera);
        Vector2 bottomRight = Raylib.GetScreenToWorld2D(new (Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), _camera);
        return new (topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    public Vector2 GetMouseWorldPosition()
    {
        Vector2 raylibWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);
        raylibWorld.Y = -raylibWorld.Y; // Flip Y to match our coordinate system
        return raylibWorld / BlockSize;
    }
}
