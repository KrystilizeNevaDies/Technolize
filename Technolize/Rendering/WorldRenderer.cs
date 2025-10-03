using System.Numerics;
using Raylib_cs;
using Technolize.Utils;
using Technolize.World;
using Technolize.World.Block;
namespace Technolize.Rendering;

public class WorldRenderer(TickableWorld tickableWorld, int screenWidth, int screenHeight)
{

    private const int BlockSize = 16;
    private const double SecondsUntilCachedTexture = 1.0; // seconds to wait until a region is considered inactive and cached as a texture.

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
    
    // Cache frequently used values to reduce repeated calculations
    private static readonly Color GridColor = new (255, 255, 255, 64);
    private static readonly Color AirColor = Blocks.Air.GetTag(BlockInfo.TagColor);
    private static readonly Vector2 RegionSizeVector = new Vector2(TickableWorld.RegionSize);
    private const float BlockSizeFloat = (float)BlockSize;
    private static readonly int RegionSizeInPixels = TickableWorld.RegionSize * BlockSize;

    public void Draw()
    {
        (Vector2 worldStart, Vector2 worldEnd) = GetVisibleWorldBounds();

        // Calculate visible region bounds once for culling
        Vector2 visibleRegionStart = new Vector2(
            (float)Math.Floor(worldStart.X / TickableWorld.RegionSize),
            (float)Math.Floor(worldStart.Y / TickableWorld.RegionSize)
        );
        Vector2 visibleRegionEnd = new Vector2(
            (float)Math.Ceiling(worldEnd.X / TickableWorld.RegionSize),
            (float)Math.Ceiling(worldEnd.Y / TickableWorld.RegionSize)
        );

        // Filter regions by activity and visibility in a single pass
        var visibleActiveRegions = new List<KeyValuePair<Vector2, TickableWorld.Region?>>();
        var visibleInactiveRegions = new List<KeyValuePair<Vector2, TickableWorld.Region?>>();
        
        foreach (var region in tickableWorld.Regions)
        {
            var regionPos = region.Key;
            
            // Visibility culling: skip regions outside the visible area
            if (regionPos.X < visibleRegionStart.X || regionPos.X >= visibleRegionEnd.X ||
                regionPos.Y < visibleRegionStart.Y || regionPos.Y >= visibleRegionEnd.Y)
            {
                continue;
            }
            
            if (region.Value!.TimeSinceLastChanged.Elapsed.TotalSeconds < SecondsUntilCachedTexture)
            {
                visibleActiveRegions.Add(region);
            }
            else
            {
                visibleInactiveRegions.Add(region);
            }
        }

        // render the textures for any inactive regions that have transitioned from active to inactive.
        foreach ((Vector2 regionPos, TickableWorld.Region? region) in visibleInactiveRegions) {

            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture)) {
                // texture already exists, so skip rendering.
                continue;
            }

            texture = Raylib.LoadRenderTexture(TickableWorld.RegionSize, TickableWorld.RegionSize);
            _region2Texture[regionPos] = texture;

            // render this region to an image.
            Raylib.BeginTextureMode(texture);
            
            // Clear the texture with air background to prevent corrupted pixels from previous GPU memory contents
            Raylib.ClearBackground(AirColor);

            foreach ((Vector2 pos, uint blockId) in region!.GetAllBlocks()) {
                // Use cached color lookup that's already optimized
                if (!BlockColors.TryGetValue(blockId, out Color color))
                {
                    BlockInfo block = BlockRegistry.GetInfo(blockId);
                    color = block.GetTag(BlockInfo.TagColor);
                    BlockColors[blockId] = color;
                }
                Raylib.DrawPixel((int)pos.X, TickableWorld.RegionSize - (int) pos.Y - 1, color);
            }

            Raylib.EndTextureMode();
        }

        Raylib.BeginMode2D(_camera);

        // fill with air background
        Raylib.ClearBackground(AirColor);

        // render the active regions that are currently visible.
        foreach ((Vector2 regionPos, TickableWorld.Region? region) in visibleActiveRegions) {

            // if we have a texture for this region, unload it.
            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture)) {
                // Unload the texture if it exists, as we are rendering the blocks directly.
                Raylib.UnloadRenderTexture(texture);
                _region2Texture.Remove(regionPos);
            }

            // region is actively ticking, so render the blocks directly instead of using a texture.
            // we use a texture only for inactive regions.

            // Pre-calculate base world position for this region
            Vector2 baseWorldPos = regionPos * RegionSizeVector;

            // first draw air background
            foreach ((Vector2 localPos, uint blockId) in region!.GetAllBlocks()) {
                Vector2 position = baseWorldPos + localPos;
                
                // Use optimized color caching
                if (!BlockColors.TryGetValue(blockId, out Color color))
                {
                    BlockInfo block = BlockRegistry.GetInfo(blockId);
                    color = block.GetTag(BlockInfo.TagColor);
                    BlockColors[blockId] = color;
                }

                // Use pre-calculated values to reduce multiplication
                Raylib.DrawRectangle(
                    (int) (position.X * BlockSizeFloat),
                    (int) (-position.Y * BlockSizeFloat),
                    BlockSize,
                    BlockSize,
                    color);
            }

            // draw border for all regions that are currently ticking.
            Vector2 worldPos = regionPos * RegionSizeInPixels;
            Rectangle border = new (
                worldPos.X,
                -worldPos.Y - (TickableWorld.RegionSize - 1) * BlockSize,
                RegionSizeInPixels,
                RegionSizeInPixels
            );

            // Raylib.DrawRectangleRec(border, new Color(255, 255, 255, 64));
        }

        // render the inactive regions that are currently visible.
        foreach ((Vector2 regionPos, RenderTexture2D texture) in _region2Texture) {
            // Additional visibility check for cached textures
            if (regionPos.X < visibleRegionStart.X || regionPos.X >= visibleRegionEnd.X ||
                regionPos.Y < visibleRegionStart.Y || regionPos.Y >= visibleRegionEnd.Y)
            {
                continue;
            }

            Vector2 worldPos = regionPos * RegionSizeInPixels;
            Rectangle source = new (0, 0, TickableWorld.RegionSize, -TickableWorld.RegionSize);
            Rectangle dest = new (
                worldPos.X,
                -worldPos.Y - (TickableWorld.RegionSize - 1) * BlockSize,
                RegionSizeInPixels,
                RegionSizeInPixels
            );

            Raylib.DrawTexturePro(
                texture.Texture,
                source,
                dest,
                new (0, 0),
                0.0f,
                Color.White);
        }

        // Optimize grid rendering by caching calculations
        RenderGrid(worldStart, worldEnd);

        Raylib.EndMode2D();

        Raylib.DrawFPS(10, 10);

        Vector2 mousePos = GetMouseWorldPosition();
        mousePos = mousePos with
        {
            X = (float)Math.Floor(mousePos.X),
            Y = (float)Math.Floor(mousePos.Y)
        };
        Raylib.DrawText($"Mouse World Position: ({mousePos.X:F2}, {mousePos.Y:F2})", 10, 40, 20, Color.White);

        Raylib.DrawText($"Updating Region Count: {visibleActiveRegions.Count}", 10, 70, 20, Color.White);
    }

    private void RenderGrid(Vector2 worldStart, Vector2 worldEnd)
    {
        const double targetGridCount = 256;
        double worldWidth = (worldEnd.X - worldStart.X) * BlockSize;
        double worldHeight = (worldEnd.Y - worldStart.Y) * BlockSize;
        double variableGridSize = Math.Max(1, Math.Max(worldWidth, worldHeight) / targetGridCount);

        int gridSize = 1;
        while (gridSize < variableGridSize)
        {
            gridSize *= 2;
        }

        float lineWidth = 2.0f / _camera.Zoom;
        int gridSizePixels = gridSize * BlockSize;

        for (int x = (int)worldStart.X; x <= (int)worldEnd.X; x++)
        {
            if (x % gridSize != 0) continue;
            float xPos = x * BlockSizeFloat;
            Vector2 worldGridStart = new (xPos, -worldStart.Y * BlockSizeFloat);
            Vector2 worldGridEnd = new (xPos, -worldEnd.Y * BlockSizeFloat);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, lineWidth, GridColor);
        }

        for (int y = (int)worldStart.Y; y <= (int)worldEnd.Y; y++)
        {
            if (y % gridSize != 0) continue;
            float yPos = -y * BlockSizeFloat;
            Vector2 worldGridStart = new (worldStart.X * BlockSizeFloat, yPos);
            Vector2 worldGridEnd = new (worldEnd.X * BlockSizeFloat, yPos);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, lineWidth, GridColor);
        }
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
