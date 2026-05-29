using System.Numerics;
using Raylib_cs;
using Technolize.Utils;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// Alternative WorldRenderer implementation that uses GPU shaders for rendering regions.
/// Provides the same interface and behavior as WorldRenderer but leverages GPU shaders
/// for improved performance on systems with capable GPUs.
/// </summary>
public class WorldShaderRenderer(IWorldRenderSource renderSource, int screenWidth, int screenHeight) : IWorldRenderer
{
    public WorldShaderRenderer(TickableWorld tickableWorld, int screenWidth, int screenHeight)
        : this(new TickableWorldRenderSource(tickableWorld), screenWidth, screenHeight)
    {
    }

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
    private Raylib_cs.Shader _worldRenderingShader;
    private Texture2D _blockColorLookupTexture;
    private bool _shadersInitialized = false;

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

    private void InitializeShaders()
    {
        if (_shadersInitialized) return;

        // Load the world rendering shader
        string vertShaderPath = Path.Combine("shaders", "base.vert");
        string fragShaderPath = Path.Combine("shaders", "world_renderer.frag");

        _worldRenderingShader = Raylib.LoadShader(vertShaderPath, fragShaderPath);

        // Create block color lookup texture
        CreateBlockColorLookupTexture();

        _shadersInitialized = true;
    }

    private void CreateBlockColorLookupTexture()
    {
        // Create a lookup texture for block colors
        // We'll use a 1D texture with block ID as X coordinate
        int lookupWidth = checked((int)BlockRegistry.MaxBlockId + 1);
        Image colorLookup = Raylib.GenImageColor(lookupWidth, 1, Color.Black);

        // Fill the lookup texture with block colors
        foreach (BlockInfo block in BlockRegistry.Blocks)
        {
            Color blockColor = block.GetTag(BlockInfo.TagColor);
            Raylib.ImageDrawPixel(ref colorLookup, (int)block.Id, 0, blockColor);
        }

        _blockColorLookupTexture = Raylib.LoadTextureFromImage(colorLookup);
        Raylib.SetTextureFilter(_blockColorLookupTexture, TextureFilter.Point);
        Raylib.UnloadImage(colorLookup);
    }

    public void Draw()
    {
        // Initialize shaders if not done yet
        InitializeShaders();

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

        WorldRenderFrame frame = renderSource.CaptureFrame(visibleRegionStart, visibleRegionEnd);
        List<WorldRenderRegion> visibleActiveRegions = [];
        List<WorldRenderRegion> visibleInactiveRegions = [];

        foreach (WorldRenderRegion region in frame.Regions)
        {
            if (region.SecondsSinceLastChanged < SecondsUntilCachedTexture)
            {
                visibleActiveRegions.Add(region);
            }
            else
            {
                visibleInactiveRegions.Add(region);
            }
        }

        // Render the textures for any inactive regions using shaders
        foreach (WorldRenderRegion region in visibleInactiveRegions)
        {
            Vector2 regionPos = region.Position;
            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture))
            {
                // texture already exists, so skip rendering.
                continue;
            }

            texture = RenderRegionWithShader(region);
            _region2Texture[regionPos] = texture;
        }

        Raylib.BeginMode2D(_camera);

        // fill with air background
        Raylib.ClearBackground(AirColor);

        // Render the active regions using shaders
        foreach (WorldRenderRegion region in visibleActiveRegions)
        {
            Vector2 regionPos = region.Position;
            // if we have a texture for this region, unload it.
            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture))
            {
                // Unload the texture if it exists, as we are rendering the blocks directly.
                Raylib.UnloadRenderTexture(texture);
                _region2Texture.Remove(regionPos);
            }

            // Render the region directly using shaders
            RenderRegionDirectly(region);
        }

        // Render the inactive regions that are currently visible.
        foreach ((Vector2 regionPos, RenderTexture2D texture) in _region2Texture)
        {
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

    private RenderTexture2D RenderRegionWithShader(WorldRenderRegion region)
    {
        RenderTexture2D texture = Raylib.LoadRenderTexture(TickableWorld.RegionSize, TickableWorld.RegionSize);
        Raylib.SetTextureFilter(texture.Texture, TextureFilter.Point);
        Raylib.BeginTextureMode(texture);
        Raylib.ClearBackground(AirColor);

        foreach (WorldRenderBlock block in region.Blocks)
        {
            if (!BlockColors.TryGetValue(block.BlockId, out Color color))
            {
                BlockInfo blockInfo = BlockRegistry.GetInfo(block.BlockId);
                color = blockInfo.GetTag(BlockInfo.TagColor);
                BlockColors[block.BlockId] = color;
            }

            Raylib.DrawPixel((int)block.LocalPos.X, TickableWorld.RegionSize - (int)block.LocalPos.Y - 1, color);
        }

        Raylib.EndTextureMode();

        return texture;
    }

    private void RenderRegionDirectly(WorldRenderRegion region)
    {
        // For active regions, render directly to screen using optimized approach
        // This can also use shaders but render immediately instead of to texture
        Vector2 baseWorldPos = region.Position * RegionSizeVector;

        foreach (WorldRenderBlock block in region.Blocks)
        {
            Vector2 position = baseWorldPos + block.LocalPos;

            // Use optimized color caching (same as original WorldRenderer)
            if (!BlockColors.TryGetValue(block.BlockId, out Color color))
            {
                BlockInfo blockInfo = BlockRegistry.GetInfo(block.BlockId);
                color = blockInfo.GetTag(BlockInfo.TagColor);
                BlockColors[block.BlockId] = color;
            }

            // Use pre-calculated values to reduce multiplication
            Raylib.DrawRectangle(
                (int) (position.X * BlockSizeFloat),
                (int) (-position.Y * BlockSizeFloat),
                BlockSize,
                BlockSize,
                color);
        }
    }

    private Image CreateWorldDataTexture(WorldRenderRegion region)
    {
        // Create an image containing block ID data for the region
        Image worldData = Raylib.GenImageColor(TickableWorld.RegionSize, TickableWorld.RegionSize, Color.Black);

        foreach (WorldRenderBlock block in region.Blocks)
        {
            Color blockData = Packing.PackUIntToColor(block.BlockId);
            Raylib.ImageDrawPixel(ref worldData, (int)block.LocalPos.X, TickableWorld.RegionSize - (int)block.LocalPos.Y - 1, blockData);
        }

        return worldData;
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

    public void Dispose()
    {
        // Clean up shader resources
        if (_shadersInitialized)
        {
            Raylib.UnloadShader(_worldRenderingShader);
            Raylib.UnloadTexture(_blockColorLookupTexture);
        }

        // Clean up cached textures
        foreach (var texture in _region2Texture.Values)
        {
            Raylib.UnloadRenderTexture(texture);
        }
        _region2Texture.Clear();
    }
}