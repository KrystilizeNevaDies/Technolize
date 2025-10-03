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
public class WorldShaderRenderer(TickableWorld tickableWorld, int screenWidth, int screenHeight)
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
    private Raylib_cs.Shader _worldRenderingShader;
    private Texture2D _blockColorLookupTexture;
    private bool _shadersInitialized = false;

    // Cache shader uniform locations for performance
    private int _worldDataLocation = -1;
    private int _blockColorsLocation = -1; 
    private int _regionSizeLocation = -1;

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
    
    // Cache for world data textures to avoid repeated creation
    private readonly Dictionary<Vector2, Texture2D> _worldDataTextureCache = new();
    
    // Cache frequently used values to reduce repeated calculations
    private static readonly Color GridColor = new (255, 255, 255, 64);
    private static readonly Color AirColor = Blocks.Air.GetTag(BlockInfo.TagColor);
    private static readonly Vector2 RegionSizeVector = new Vector2(TickableWorld.RegionSize);
    private const float BlockSizeFloat = (float)BlockSize;
    private static readonly int RegionSizeInPixels = TickableWorld.RegionSize * BlockSize;
    
    // Pre-allocated array for region size to avoid repeated allocations
    private static readonly float[] _regionSizeArray = { TickableWorld.RegionSize, TickableWorld.RegionSize };

    private void InitializeShaders()
    {
        if (_shadersInitialized) return;

        // Load the world rendering shader
        string vertShaderPath = Path.Combine("shaders", "base.vert");
        string fragShaderPath = Path.Combine("shaders", "world_renderer.frag");
        
        _worldRenderingShader = Raylib.LoadShader(vertShaderPath, fragShaderPath);
        
        // Cache uniform locations for performance
        _worldDataLocation = Raylib.GetShaderLocation(_worldRenderingShader, "worldData");
        _blockColorsLocation = Raylib.GetShaderLocation(_worldRenderingShader, "blockColors");
        _regionSizeLocation = Raylib.GetShaderLocation(_worldRenderingShader, "regionSize");
        
        // Create block color lookup texture
        CreateBlockColorLookupTexture();
        
        _shadersInitialized = true;
    }

    private void CreateBlockColorLookupTexture()
    {
        // Create a lookup texture for block colors
        // Use a 1D texture with block ID as X coordinate for optimal cache usage
        const int maxBlockId = 256; // Assume max 256 block types
        Image colorLookup = Raylib.GenImageColor(maxBlockId, 1, Color.Black);

        // Fill the lookup texture with block colors in a single pass
        var allBlocks = Blocks.AllBlocks().ToArray();
        foreach (var block in allBlocks)
        {
            if (block.id < maxBlockId)
            {
                Color blockColor = block.GetTag(BlockInfo.TagColor);
                Raylib.ImageDrawPixel(ref colorLookup, (int)block.id, 0, blockColor);
            }
        }

        _blockColorLookupTexture = Raylib.LoadTextureFromImage(colorLookup);
        
        // Set texture filter to nearest for crisp pixel lookups
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

        // Render the textures for any inactive regions using shaders
        foreach ((Vector2 regionPos, TickableWorld.Region? region) in visibleInactiveRegions) 
        {
            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture)) 
            {
                // texture already exists, so skip rendering.
                continue;
            }

            texture = RenderRegionWithShader(region!, regionPos);
            _region2Texture[regionPos] = texture;
        }

        Raylib.BeginMode2D(_camera);

        // fill with air background
        Raylib.ClearBackground(AirColor);

        // Render the active regions using shaders
        foreach ((Vector2 regionPos, TickableWorld.Region? region) in visibleActiveRegions) 
        {
            // if we have a texture for this region, unload it.
            if (_region2Texture.TryGetValue(regionPos, out RenderTexture2D texture)) 
            {
                // Unload the texture if it exists, as we are rendering the blocks directly.
                Raylib.UnloadRenderTexture(texture);
                _region2Texture.Remove(regionPos);
            }

            // Render the region directly using shaders
            RenderRegionDirectly(region!, regionPos);
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

    private RenderTexture2D RenderRegionWithShader(TickableWorld.Region region, Vector2 regionPos)
    {
        RenderTexture2D texture = Raylib.LoadRenderTexture(TickableWorld.RegionSize, TickableWorld.RegionSize);

        // Check if we have a cached world data texture for this region
        Texture2D worldDataTexture;
        if (!_worldDataTextureCache.TryGetValue(regionPos, out worldDataTexture))
        {
            // Create and cache world data texture for this region
            Image worldDataImage = CreateWorldDataTexture(region);
            worldDataTexture = Raylib.LoadTextureFromImage(worldDataImage);
            _worldDataTextureCache[regionPos] = worldDataTexture;
            Raylib.UnloadImage(worldDataImage);
        }

        // Render using shader
        Raylib.BeginTextureMode(texture);
        Raylib.ClearBackground(AirColor);
        
        Raylib.BeginShaderMode(_worldRenderingShader);
        
        // Set shader uniforms using cached locations for better performance
        Raylib.SetShaderValueTexture(_worldRenderingShader, _worldDataLocation, worldDataTexture);
        Raylib.SetShaderValueTexture(_worldRenderingShader, _blockColorsLocation, _blockColorLookupTexture);
        
        // Use pre-cached region size array to avoid allocation
        Raylib.SetShaderValue(_worldRenderingShader, _regionSizeLocation, 
            _regionSizeArray, ShaderUniformDataType.Vec2);

        // Draw a full-screen quad to trigger the fragment shader
        Raylib.DrawTexture(worldDataTexture, 0, 0, Color.White);
        
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        return texture;
    }

    private void RenderRegionDirectly(TickableWorld.Region region, Vector2 regionPos)
    {
        // For active regions, render directly to screen using optimized approach
        // This can also use shaders but render immediately instead of to texture
        Vector2 baseWorldPos = regionPos * RegionSizeVector;

        foreach ((Vector2 localPos, uint blockId) in region.GetAllBlocks()) 
        {
            Vector2 position = baseWorldPos + localPos;
            
            // Use optimized color caching (same as original WorldRenderer)
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
    }

    private Image CreateWorldDataTexture(TickableWorld.Region region)
    {
        // Create an image containing block ID data for the region
        // Use GenImageColor for consistent behavior with Raylib
        Image worldData = Raylib.GenImageColor(TickableWorld.RegionSize, TickableWorld.RegionSize, Color.Black);

        // Pre-process blocks for efficiency
        foreach ((Vector2 pos, uint blockId) in region.GetAllBlocks())
        {
            // Encode block ID in the red channel
            byte normalizedBlockId = (byte)Math.Min(blockId, 255);
            Color blockData = new Color((byte)normalizedBlockId, (byte)0, (byte)0, (byte)255);
            int y = TickableWorld.RegionSize - (int)pos.Y - 1; // Flip Y for texture coordinates
            
            // Bounds check before drawing
            if (pos.X >= 0 && pos.X < TickableWorld.RegionSize && y >= 0 && y < TickableWorld.RegionSize)
            {
                Raylib.ImageDrawPixel(ref worldData, (int)pos.X, y, blockData);
            }
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
        
        // Clean up world data texture cache
        foreach (var texture in _worldDataTextureCache.Values)
        {
            Raylib.UnloadTexture(texture);
        }
        _worldDataTextureCache.Clear();
    }
}