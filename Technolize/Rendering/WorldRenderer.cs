using System.Numerics;
using Raylib_cs;
using Technolize.Utils;
using Technolize.World;
using Technolize.World.Block;
namespace Technolize.Rendering;

public class WorldRenderer(TickableWorld tickableWorld, int screenWidth, int screenHeight)
{

    private const int BlockSize = 16;

    private Camera2D _camera = new()
    {
        Target = new (screenWidth / 2f, screenHeight / 2f),
        Offset = new (screenWidth / 2f, screenHeight / 2f),
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
            _camera.Zoom = Math.Clamp(_camera.Zoom, 0.01f, 24.0f);
        }
    }

    private readonly Dictionary<Vector2, RenderTexture2D> _region2Texture = new();

    public void Draw()
    {
        (Vector2 worldStart, Vector2 worldEnd) = GetVisibleWorldBounds();

        var activeRegions = tickableWorld.Regions.Where(region => region.Value!.WasChangedLastTick).ToList();
        var inactiveRegions = tickableWorld.Regions.Where(region => !region.Value!.WasChangedLastTick).ToList();

        // render the textures for any inactive regions that have transitioned from active to inactive.
        foreach ((Vector2 regionPos, TickableWorld.Region? region) in inactiveRegions) {

            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture)) {
                // texture already exists, so skip rendering.
                continue;
            }

            texture = Raylib.LoadRenderTexture(TickableWorld.RegionSize, TickableWorld.RegionSize);
            _region2Texture[regionPos] = texture;

            // render this region to an image.
            Raylib.BeginTextureMode(texture);

            foreach (var (pos, blockId) in region!.GetAllBlocks()) {
                BlockInfo block = BlockRegistry.GetInfo(blockId);
                Color color = block.Color;
                Raylib.DrawPixel((int)pos.X, TickableWorld.RegionSize - (int) pos.Y - 1, color);
            }

            Raylib.EndTextureMode();
        }

        Raylib.BeginMode2D(_camera);

        // fill with air background
        Raylib.ClearBackground(Blocks.Air.Color);

        // render the active regions that are currently visible.
        foreach ((Vector2 regionPos, TickableWorld.Region? region) in activeRegions) {

            // if we have a texture for this region, unload it.
            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture)) {
                // Unload the texture if it exists, as we are rendering the blocks directly.
                Raylib.UnloadRenderTexture(texture);
                _region2Texture.Remove(regionPos);
            }

            // region is actively ticking, so render the blocks directly instead of using a texture.
            // we use a texture only for inactive regions.

            // first draw air background
            foreach ((Vector2 localPos, uint blockId) in region!.GetAllBlocks()) {
                Vector2 position = regionPos * TickableWorld.RegionSize + localPos;
                if (!BlockColors.TryGetValue(blockId, out Color color))
                {
                    BlockInfo block = BlockRegistry.GetInfo(blockId);
                    color = block.Color;
                    BlockColors[blockId] = color;
                }

                Raylib.DrawRectangle(
                    (int) position.X * BlockSize,
                    (int) -position.Y * BlockSize,
                    BlockSize,
                    BlockSize,
                    color);
            }

            // draw border for all regions that are currently ticking.
            Vector2 worldPos = regionPos * TickableWorld.RegionSize * BlockSize;
            Rectangle border = new (
                worldPos.X,
                -worldPos.Y - (TickableWorld.RegionSize - 1) * BlockSize,
                TickableWorld.RegionSize * BlockSize,
                TickableWorld.RegionSize * BlockSize
            );

            Raylib.DrawRectangleLinesEx(border, 2.0f / _camera.Zoom, Color.White);
        }

        // render the inactive regions that are currently visible.
        foreach ((Vector2 regionPos, RenderTexture2D texture) in _region2Texture) {

            Vector2 worldPos = regionPos * TickableWorld.RegionSize * BlockSize;
            Rectangle source = new (0, 0, TickableWorld.RegionSize, -TickableWorld.RegionSize);
            Rectangle dest = new (
                worldPos.X,
                -worldPos.Y - (TickableWorld.RegionSize - 1) * BlockSize,
                TickableWorld.RegionSize * BlockSize,
                TickableWorld.RegionSize * BlockSize
            );

            Raylib.DrawTexturePro(
                texture.Texture,
                source,
                dest,
                new (0, 0),
                0.0f,
                Color.White);
        }

        Color gridColor = new (255, 255, 255, 64);
        const double targetGridCount = 256;
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
            Vector2 worldGridStart = new (x * BlockSize, -worldStart.Y * BlockSize);
            Vector2 worldGridEnd = new (x * BlockSize, -worldEnd.Y * BlockSize);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, 2.0f / _camera.Zoom, gridColor);
        }

        for (int y = (int)worldStart.Y; y <= (int)worldEnd.Y; y++)
        {
            if (y % gridSize != 0) continue;
            Vector2 worldGridStart = new (worldStart.X * BlockSize, -y * BlockSize);
            Vector2 worldGridEnd = new (worldEnd.X * BlockSize, -y * BlockSize);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, 2.0f / _camera.Zoom, gridColor);
        }

        Raylib.EndMode2D();

        Raylib.DrawFPS(10, 10);

        Vector2 mousePos = GetMouseWorldPosition();
        mousePos = mousePos with
        {
            X = (float)Math.Floor(mousePos.X),
            Y = (float)Math.Floor(mousePos.Y)
        };
        Raylib.DrawText($"Mouse World Position: ({mousePos.X:F2}, {mousePos.Y:F2})", 10, 40, 20, Color.White);

        Raylib.DrawText($"Updating Region Count: {activeRegions.Count}", 10, 70, 20, Color.White);
    }

    public (Vector2 start, Vector2 end) GetVisibleWorldBounds()
    {
        Vector2 screenTopLeft = Raylib.GetScreenToWorld2D(new (0, 0), _camera);
        Vector2 screenBottomRight = Raylib.GetScreenToWorld2D(new (Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), _camera);

        double offset = screenTopLeft.Y * -2.0 - Raylib.GetScreenHeight() / _camera.Zoom;
        int worldStartX = (int)Math.Floor(screenTopLeft.X / BlockSize);
        int worldStartY = (int)Math.Floor((screenTopLeft.Y + offset) / BlockSize);
        int worldEndX = (int)Math.Ceiling(screenBottomRight.X / BlockSize) + 1;
        int worldEndY = (int)Math.Ceiling((screenBottomRight.Y + offset) / BlockSize) + 1;

        return (new (worldStartX, worldStartY), new (worldEndX, worldEndY));
    }

    public Vector2 GetMouseWorldPosition()
    {
        Vector2 raylibWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);
        raylibWorld.Y = -raylibWorld.Y;
        return raylibWorld / BlockSize;
    }
}
