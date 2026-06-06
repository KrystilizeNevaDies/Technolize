using System.Numerics;
using Raylib_cs;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// World renderer that builds a single quadtree covering the entire loaded world each frame and
/// renders it in one shader pass. The whole-world quadtree (not just the visible window) is uploaded
/// so lighting that depends on offscreen geometry stays correct.
/// </summary>
public class WorldShaderRenderer(IWorldRenderSource renderSource, int screenWidth, int screenHeight) : IWorldRenderer
{
    private const int MaxShaderLights = 16;
    private const byte AirMaterialCode = 0;
    private const byte WaterMaterialCode = 1;
    private const byte SolidMaterialCode = 2;
    private const double RefractionEncodingScale = 4096.0;
    private const int BlockSize = 16;
    private const float BlockSizeFloat = BlockSize;

    private sealed class GlobalRenderResources(Texture2D worldColorTexture, Texture2D quadtreeFirstChildTexture, Texture2D quadtreeValueTexture, Vector2 worldOrigin, Vector2 worldSize, Vector2 quadtreeSize, Vector2 quadtreeOrigin, Vector2 quadtreeTextureSize) : IDisposable
    {
        public Texture2D WorldColorTexture { get; } = worldColorTexture;
        public Texture2D QuadtreeFirstChildTexture { get; } = quadtreeFirstChildTexture;
        public Texture2D QuadtreeValueTexture { get; } = quadtreeValueTexture;
        public Vector2 WorldOrigin { get; } = worldOrigin;
        public Vector2 WorldSize { get; } = worldSize;
        public Vector2 QuadtreeSize { get; } = quadtreeSize;
        public Vector2 QuadtreeOrigin { get; } = quadtreeOrigin;
        public Vector2 QuadtreeTextureSize { get; } = quadtreeTextureSize;

        public void Dispose()
        {
            Raylib.UnloadTexture(WorldColorTexture);
            Raylib.UnloadTexture(QuadtreeFirstChildTexture);
            Raylib.UnloadTexture(QuadtreeValueTexture);
        }
    }

    private readonly record struct WorldCell(Color Color, byte MaterialCode);

    public WorldShaderRenderer(TickableWorld tickableWorld, int screenWidth, int screenHeight)
        : this(new TickableWorldRenderSource(tickableWorld), screenWidth, screenHeight)
    {
    }

    private Camera2D _camera = new()
    {
        Target = new(screenWidth / 2f, screenHeight / 2f),
        Offset = new(screenWidth / 2f, screenHeight / 2f),
        Rotation = 0.0f,
        Zoom = 1.0f
    };

    private Raylib_cs.Shader _worldRenderingShader;
    private int _regionSizeLocation;
    private int _regionOriginLocation;
    private int _quadtreeSizeLocation;
    private int _quadtreeOriginLocation;
    private int _quadtreeTextureSizeLocation;
    private int _timeLocation;
    private int _ambientColorLocation;
    private int _sunDirectionLocation;
    private int _sunRayCountLocation;
    private int _sunColorLocation;
    private int _sunIntensityLocation;
    private int _lightCountLocation;
    private int _lightDataLocation;
    private int _lightColorsLocation;
    private int _quadtreeFirstChildLocation;
    private int _quadtreeValueLocation;
    private bool _shadersInitialized;

    private static readonly Color GridColor = new(255, 255, 255, 64);
    private static readonly Color AirColor = Blocks.Air.GetTag(BlockInfo.TagColor);
    private static readonly Color ScheduledRegionFillColor = new(255, 196, 64, 32);
    private static readonly Color ScheduledRegionBorderColor = new(255, 210, 96, 190);
    private static readonly Vector2 RegionSizeVector = new(TickableWorld.RegionSize);

    public bool ShowScheduledRegionOverlay { get; set; }
    public WorldLighting Lighting { get; set; } = WorldLighting.Default;

    /// <summary>
    /// When set, this value is used for the shader <c>time</c> uniform instead of the wall clock,
    /// making the animated water/sun output deterministic for snapshot testing.
    /// </summary>
    internal float? FixedTime { get; set; }

    /// <summary>
    /// When false, the FPS counter and informational text overlays are not drawn. Used by snapshot
    /// testing so the captured frame contains only the deterministic world shader output.
    /// </summary>
    internal bool ShowDebugOverlay { get; set; } = true;

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

    private void InitializeShaders()
    {
        if (_shadersInitialized)
        {
            return;
        }

        string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "shaders");
        string vertShaderPath = Path.Combine(shaderDirectory, "base.vert");
        string fragShaderPath = Path.Combine(shaderDirectory, "world_renderer.frag");

        _worldRenderingShader = Raylib.LoadShader(vertShaderPath, fragShaderPath);

        _regionSizeLocation = Raylib.GetShaderLocation(_worldRenderingShader, "regionSize");
        _regionOriginLocation = Raylib.GetShaderLocation(_worldRenderingShader, "regionOrigin");
        _quadtreeSizeLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeSize");
        _quadtreeOriginLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeOrigin");
        _quadtreeTextureSizeLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeTextureSize");
        _timeLocation = Raylib.GetShaderLocation(_worldRenderingShader, "time");
        _ambientColorLocation = Raylib.GetShaderLocation(_worldRenderingShader, "ambientColor");
        _sunDirectionLocation = Raylib.GetShaderLocation(_worldRenderingShader, "sunDirection");
        _sunRayCountLocation = Raylib.GetShaderLocation(_worldRenderingShader, "sunRayCount");
        _sunColorLocation = Raylib.GetShaderLocation(_worldRenderingShader, "sunColor");
        _sunIntensityLocation = Raylib.GetShaderLocation(_worldRenderingShader, "sunIntensity");
        _lightCountLocation = Raylib.GetShaderLocation(_worldRenderingShader, "lightCount");
        _lightDataLocation = Raylib.GetShaderLocation(_worldRenderingShader, "lightData[0]");
        _lightColorsLocation = Raylib.GetShaderLocation(_worldRenderingShader, "lightColors[0]");
        _quadtreeFirstChildLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeFirstChild");
        _quadtreeValueLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeValue");

        _shadersInitialized = true;
    }

    public void Draw()
    {
        InitializeShaders();

        (Vector2 worldStart, Vector2 worldEnd) = GetVisibleWorldBounds();
        Vector2 visibleRegionStart = new(
            (float)Math.Floor(worldStart.X / TickableWorld.RegionSize),
            (float)Math.Floor(worldStart.Y / TickableWorld.RegionSize)
        );
        Vector2 visibleRegionEnd = new(
            (float)Math.Ceiling(worldEnd.X / TickableWorld.RegionSize),
            (float)Math.Ceiling(worldEnd.Y / TickableWorld.RegionSize)
        );

        // Always upload the ENTIRE world's quadtree, not just the on-screen window. Water lighting
        // raymarches depend on geometry outside the view (sun attenuation through water above the
        // camera, refraction through offscreen bodies, etc.), so the quadtree and colour texture must
        // cover every loaded region. The camera still crops the drawn result to the visible area.
        WorldRenderFrame frame = renderSource.CaptureWorldFrame();
        (Vector2 worldRegionStart, Vector2 worldRegionEnd) = GetWorldRegionBounds(frame, visibleRegionStart, visibleRegionEnd);
        using GlobalRenderResources globalResources = CreateWorldResources(frame, worldRegionStart, worldRegionEnd);

        Raylib.BeginMode2D(_camera);
        Raylib.ClearBackground(AirColor);
        DrawVisibleWorldWithShader(globalResources);
        RenderGrid(worldStart, worldEnd);
        if (ShowScheduledRegionOverlay)
        {
            RenderScheduledRegionOverlay(frame.ScheduledRegions);
        }

        Raylib.EndMode2D();

        if (!ShowDebugOverlay)
        {
            return;
        }

        Raylib.DrawFPS(10, 10);

        Vector2 mousePos = GetMouseWorldPosition();
        mousePos = mousePos with
        {
            X = (float)Math.Floor(mousePos.X),
            Y = (float)Math.Floor(mousePos.Y)
        };
        Raylib.DrawText($"Mouse World Position: ({mousePos.X:F2}, {mousePos.Y:F2})", 10, 40, 20, Color.White);
        Raylib.DrawText($"World Region Count: {frame.Regions.Count}", 10, 70, 20, Color.White);
        if (ShowScheduledRegionOverlay)
        {
            Raylib.DrawText($"Scheduled Region Count: {frame.ScheduledRegions.Count}", 10, 100, 20, ScheduledRegionBorderColor);
        }
    }

    /// <summary>
    /// Returns the half-open region-space bounding box <c>[start, end)</c> covering every loaded
    /// region in the frame, so the whole world is uploaded. Falls back to the visible bounds when no
    /// regions are loaded.
    /// </summary>
    private static (Vector2 start, Vector2 end) GetWorldRegionBounds(WorldRenderFrame frame, Vector2 fallbackStart, Vector2 fallbackEnd)
    {
        if (frame.Regions.Count == 0)
        {
            return (fallbackStart, fallbackEnd);
        }

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (WorldRenderRegion region in frame.Regions)
        {
            minX = Math.Min(minX, region.Position.X);
            minY = Math.Min(minY, region.Position.Y);
            maxX = Math.Max(maxX, region.Position.X);
            maxY = Math.Max(maxY, region.Position.Y);
        }

        return (new Vector2(minX, minY), new Vector2(maxX + 1, maxY + 1));
    }

    private static void RenderScheduledRegionOverlay(IReadOnlySet<Vector2> scheduledRegions)
    {
        foreach (Vector2 regionPos in scheduledRegions)
        {
            Rectangle regionRect = CreateRegionRect(regionPos);
            Raylib.DrawRectangleRec(regionRect, ScheduledRegionFillColor);
            Raylib.DrawRectangleLinesEx(regionRect, 2.0f, ScheduledRegionBorderColor);
        }
    }

    private static Rectangle CreateRegionRect(Vector2 regionPos)
    {
        Vector2 worldPos = regionPos * TickableWorld.RegionSize * BlockSizeFloat;
        float regionSizeInPixels = TickableWorld.RegionSize * BlockSizeFloat;
        return new Rectangle(
            worldPos.X,
            -worldPos.Y - (TickableWorld.RegionSize - 1) * BlockSizeFloat,
            regionSizeInPixels,
            regionSizeInPixels);
    }

    private void DrawVisibleWorldWithShader(GlobalRenderResources resources)
    {
        Rectangle source = new(0, 0, resources.WorldSize.X, resources.WorldSize.Y);
        Rectangle dest = new(
            resources.WorldOrigin.X * BlockSizeFloat,
            -(resources.WorldOrigin.Y + resources.WorldSize.Y - 1.0f) * BlockSizeFloat,
            resources.WorldSize.X * BlockSizeFloat,
            resources.WorldSize.Y * BlockSizeFloat
        );

        Raylib.SetShaderValue(_worldRenderingShader, _regionSizeLocation, resources.WorldSize, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_worldRenderingShader, _regionOriginLocation, resources.WorldOrigin, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_worldRenderingShader, _quadtreeSizeLocation, resources.QuadtreeSize, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_worldRenderingShader, _quadtreeOriginLocation, resources.QuadtreeOrigin, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_worldRenderingShader, _quadtreeTextureSizeLocation, resources.QuadtreeTextureSize, ShaderUniformDataType.Vec2);
        ApplyLighting(resources.WorldOrigin, resources.WorldSize);

        Raylib.BeginShaderMode(_worldRenderingShader);
        // Bind the custom sampler textures AFTER BeginShaderMode: enabling the shader flushes Raylib's
        // batch and resets its active texture units, which would otherwise clear bindings set before it
        // (leaving the samplers reading an unbound (0,0,0,1) and corrupting the packed quadtree).
        Raylib.SetShaderValueTexture(_worldRenderingShader, _quadtreeFirstChildLocation, resources.QuadtreeFirstChildTexture);
        Raylib.SetShaderValueTexture(_worldRenderingShader, _quadtreeValueLocation, resources.QuadtreeValueTexture);
        Raylib.DrawTexturePro(resources.WorldColorTexture, source, dest, new(0, 0), 0.0f, Color.White);
        Raylib.EndShaderMode();
    }

    private void ApplyLighting(Vector2 worldOrigin, Vector2 worldSize)
    {
        float time = FixedTime ?? (float)Raylib.GetTime();
        Raylib.SetShaderValue(_worldRenderingShader, _timeLocation, time, ShaderUniformDataType.Float);

        WorldLighting lighting = Lighting ?? WorldLighting.Default;
        Vector2 sunDirection = lighting.SunDirection.LengthSquared() > 0.0f
            ? Vector2.Normalize(lighting.SunDirection)
            : WorldLighting.Default.SunDirection;
        int sunRayCount = Math.Clamp(lighting.SunRayCount, 1, 64);

        Raylib.SetShaderValue(_worldRenderingShader, _sunDirectionLocation, sunDirection, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_worldRenderingShader, _sunRayCountLocation, sunRayCount, ShaderUniformDataType.Int);
        Raylib.SetShaderValue(_worldRenderingShader, _sunIntensityLocation, lighting.SunIntensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_worldRenderingShader, _sunColorLocation, ToShaderVector3(lighting.SunColor), ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_worldRenderingShader, _ambientColorLocation, ToShaderVector3(lighting.AmbientColor), ShaderUniformDataType.Vec3);

        WorldLightSource[] activeLights = SelectLightsForView(worldOrigin, worldSize, lighting.LightSources);
        Raylib.SetShaderValue(_worldRenderingShader, _lightCountLocation, activeLights.Length, ShaderUniformDataType.Int);
        if (activeLights.Length == 0)
        {
            return;
        }

        Vector4[] lightData = new Vector4[activeLights.Length];
        Vector4[] lightColors = new Vector4[activeLights.Length];
        for (int i = 0; i < activeLights.Length; i++)
        {
            WorldLightSource light = activeLights[i];
            lightData[i] = new Vector4(light.Position, Math.Max(light.Radius, 0.001f), Math.Max(light.Intensity, 0.0f));
            lightColors[i] = new Vector4(ToShaderVector3(light.Color), 1.0f);
        }

        Raylib.SetShaderValueV(_worldRenderingShader, _lightDataLocation, lightData.AsSpan(), ShaderUniformDataType.Vec4, activeLights.Length);
        Raylib.SetShaderValueV(_worldRenderingShader, _lightColorsLocation, lightColors.AsSpan(), ShaderUniformDataType.Vec4, activeLights.Length);
    }

    private static WorldLightSource[] SelectLightsForView(Vector2 worldOrigin, Vector2 worldSize, IReadOnlyList<WorldLightSource> lightSources)
    {
        if (lightSources.Count == 0)
        {
            return [];
        }

        Vector2 viewCenter = worldOrigin + worldSize / 2.0f;
        return lightSources
            .OrderBy(light => Vector2.DistanceSquared(light.Position, viewCenter))
            .Take(MaxShaderLights)
            .ToArray();
    }

    private static Vector3 ToShaderVector3(Color color)
    {
        return new Vector3(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f);
    }

    private static GlobalRenderResources CreateWorldResources(WorldRenderFrame frame, Vector2 worldRegionStart, Vector2 worldRegionEnd)
    {
        int worldWidth = Math.Max(1, (int)((worldRegionEnd.X - worldRegionStart.X) * TickableWorld.RegionSize));
        int worldHeight = Math.Max(1, (int)((worldRegionEnd.Y - worldRegionStart.Y) * TickableWorld.RegionSize));
        int quadtreeSide = NextPowerOfTwo(Math.Max(worldWidth, worldHeight));

        // The dense colour texture (base colour + per-cell material in alpha for the sun raymarch)
        // still covers the loaded world's bounding box.
        WorldCell[,] cells = CreateWorldCells(frame, worldRegionStart, worldWidth, worldHeight, quadtreeSide);
        Texture2D worldColorTexture = CreateWorldColorTexture(cells, worldWidth, worldHeight);

        // The refraction quadtree is the world's ENTIRE pre-built quadtree, uploaded as-is. Its
        // coordinate space is the whole tree ([0, WorldSize) with tree coord = world coord +
        // WorldOffset), independent of the visible window.
        (Texture2D quadtreeFirstChildTexture, Texture2D quadtreeValueTexture, Vector2 quadtreeTextureSize) = PackWorldQuadtreeTextures(frame.WorldQuadtree);

        Vector2 worldOrigin = worldRegionStart * RegionSizeVector;

        // Tree coordinate that local draw-space position (0, 0) maps to. Local Y is flipped relative
        // to world Y (top row of the colour texture is the highest world row), so the shader maps a
        // local position p to tree coords as quadtreeOrigin + (p.x, -p.y).
        Vector2 quadtreeOrigin = new(
            worldOrigin.X + TickableWorld.WorldOffset,
            worldOrigin.Y + worldHeight + TickableWorld.WorldOffset);

        return new GlobalRenderResources(
            worldColorTexture,
            quadtreeFirstChildTexture,
            quadtreeValueTexture,
            worldOrigin,
            new Vector2(worldWidth, worldHeight),
            new Vector2(TickableWorld.WorldSize, TickableWorld.WorldSize),
            quadtreeOrigin,
            quadtreeTextureSize);
    }

    private static WorldCell[,] CreateWorldCells(WorldRenderFrame frame, Vector2 worldRegionStart, int worldWidth, int worldHeight, int quadtreeSide)
    {
        WorldCell[,] cells = new WorldCell[quadtreeSide, quadtreeSide];
        WorldCell airCell = CreateCell(Blocks.Air);

        for (int y = 0; y < quadtreeSide; y++)
        {
            for (int x = 0; x < quadtreeSide; x++)
            {
                cells[x, y] = airCell;
            }
        }

        foreach (WorldRenderRegion region in frame.Regions)
        {
            int regionBaseX = (int)((region.Position.X - worldRegionStart.X) * TickableWorld.RegionSize);
            int regionBaseY = (int)((region.Position.Y - worldRegionStart.Y) * TickableWorld.RegionSize);

            foreach (WorldRenderBlock block in region.Blocks)
            {
                BlockInfo blockInfo = BlockRegistry.GetInfo(block.BlockId);
                int worldX = regionBaseX + (int)block.LocalPos.X;
                int worldY = regionBaseY + (int)block.LocalPos.Y;
                if (worldX < 0 || worldX >= worldWidth || worldY < 0 || worldY >= worldHeight)
                {
                    continue;
                }

                int textureY = worldHeight - worldY - 1;
                cells[worldX, textureY] = CreateCell(blockInfo);
            }
        }

        return cells;
    }

    private static WorldCell CreateCell(BlockInfo blockInfo)
    {
        return new WorldCell(
            blockInfo.GetTag(BlockInfo.TagColor),
            GetMaterialCode(blockInfo));
    }

    private static Texture2D CreateWorldColorTexture(WorldCell[,] cells, int worldWidth, int worldHeight)
    {
        Image worldColors = Raylib.GenImageColor(worldWidth, worldHeight, AirColor);

        for (int y = 0; y < worldHeight; y++)
        {
            for (int x = 0; x < worldWidth; x++)
            {
                WorldCell cell = cells[x, y];
                Raylib.ImageDrawPixel(ref worldColors, x, y, new Color(cell.Color.R, cell.Color.G, cell.Color.B, cell.MaterialCode));
            }
        }

        Texture2D texture = Raylib.LoadTextureFromImage(worldColors);
        Raylib.SetTextureFilter(texture, TextureFilter.Point);
        Raylib.UnloadImage(worldColors);
        return texture;
    }

    /// <summary>
    /// Packs the world's entire pre-built quadtree (a flat <c>(firstChild, value)</c> int array, as
    /// produced by <see cref="TickableWorld.SerializeWorld"/>) into the two GPU textures the shader
    /// reads. The tree is used as-is: leaves carry the world block id in <c>value</c>, which is
    /// resolved here to the material/refraction the shader needs (internal nodes get material 255).
    /// Nothing is windowed or rebuilt.
    /// </summary>
    private static (Texture2D firstChildTexture, Texture2D valueTexture, Vector2 textureSize) PackWorldQuadtreeTextures(int[] nodes)
    {
        const int QuadtreeTextureWidth = 2048;
        int nodeCount = nodes.Length / 2;

        int textureWidth = Math.Min(QuadtreeTextureWidth, Math.Max(nodeCount, 1));
        int textureHeight = (Math.Max(nodeCount, 1) + textureWidth - 1) / textureWidth;

        Image firstChildImage = Raylib.GenImageColor(textureWidth, textureHeight, Color.Black);
        Image valueImage = Raylib.GenImageColor(textureWidth, textureHeight, Color.Black);

        for (int i = 0; i < nodeCount; i++)
        {
            int firstChild = nodes[i * 2];
            int value = nodes[i * 2 + 1];
            bool isInternal = firstChild >= 0;

            int childIndex = isInternal ? firstChild : 0;
            byte materialCode;
            ushort encodedRefraction;
            if (isInternal)
            {
                // Internal nodes use material code 255; the shader reads it unconditionally.
                materialCode = 255;
                encodedRefraction = 0;
            }
            else
            {
                // Leaf: value is the world block id. Resolve its optics for the shader.
                BlockInfo blockInfo = BlockRegistry.GetInfo(value);
                materialCode = GetMaterialCode(blockInfo);
                encodedRefraction = EncodeRefractionIndex(blockInfo.GetTag(BlockInfo.TagRefractionIndex));
            }

            // quadtreeFirstChild: RGB = 24-bit first-child index (0 for leaves), A = material code.
            var firstChildColor = new Color(
                (byte)(childIndex & 0xFF),
                (byte)((childIndex >> 8) & 0xFF),
                (byte)((childIndex >> 16) & 0xFF),
                materialCode);

            // quadtreeValue: R,G = 16-bit refraction index, B = material code.
            var valueColor = new Color(
                (byte)(encodedRefraction & 0xFF),
                (byte)(encodedRefraction >> 8),
                materialCode,
                (byte)255);

            int textureX = i % textureWidth;
            int textureY = i / textureWidth;
            Raylib.ImageDrawPixel(ref firstChildImage, textureX, textureY, firstChildColor);
            Raylib.ImageDrawPixel(ref valueImage, textureX, textureY, valueColor);
        }

        Texture2D firstChildTexture = Raylib.LoadTextureFromImage(firstChildImage);
        Texture2D valueTexture = Raylib.LoadTextureFromImage(valueImage);
        Raylib.SetTextureFilter(firstChildTexture, TextureFilter.Point);
        Raylib.SetTextureFilter(valueTexture, TextureFilter.Point);
        Raylib.UnloadImage(firstChildImage);
        Raylib.UnloadImage(valueImage);
        return (firstChildTexture, valueTexture, new Vector2(textureWidth, textureHeight));
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    private static ushort EncodeRefractionIndex(double value)
    {
        return checked((ushort)Math.Clamp((int)Math.Round(value * RefractionEncodingScale), 0, ushort.MaxValue));
    }

    private static byte GetMaterialCode(BlockInfo blockInfo)
    {
        BlockInfo baseBlock = blockInfo.BaseBlock;
        if (ReferenceEquals(baseBlock, Blocks.Air))
        {
            return AirMaterialCode;
        }

        if (ReferenceEquals(baseBlock, Blocks.Water))
        {
            return WaterMaterialCode;
        }

        return SolidMaterialCode;
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

        for (int x = (int)worldStart.X; x <= (int)worldEnd.X; x++)
        {
            if (x % gridSize != 0) continue;
            float xPos = x * BlockSizeFloat;
            Vector2 worldGridStart = new(xPos, -worldStart.Y * BlockSizeFloat);
            Vector2 worldGridEnd = new(xPos, -worldEnd.Y * BlockSizeFloat);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, lineWidth, GridColor);
        }

        for (int y = (int)worldStart.Y; y <= (int)worldEnd.Y; y++)
        {
            if (y % gridSize != 0) continue;
            float yPos = -y * BlockSizeFloat;
            Vector2 worldGridStart = new(worldStart.X * BlockSizeFloat, yPos);
            Vector2 worldGridEnd = new(worldEnd.X * BlockSizeFloat, yPos);
            Raylib.DrawLineEx(worldGridStart, worldGridEnd, lineWidth, GridColor);
        }
    }

    public (Vector2 start, Vector2 end) GetVisibleWorldBounds()
    {
        Vector2 screenTopLeft = Raylib.GetScreenToWorld2D(new(0, 0), _camera);
        Vector2 screenBottomRight = Raylib.GetScreenToWorld2D(new(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), _camera);

        double offset = screenTopLeft.Y * -2.0 - Raylib.GetScreenHeight() / _camera.Zoom;
        int worldStartX = (int)Math.Floor(screenTopLeft.X / BlockSize);
        int worldStartY = (int)Math.Floor((screenTopLeft.Y + offset) / BlockSize);
        int worldEndX = (int)Math.Ceiling(screenBottomRight.X / BlockSize) + 1;
        int worldEndY = (int)Math.Ceiling((screenBottomRight.Y + offset) / BlockSize) + 1;

        return (new(worldStartX, worldStartY), new(worldEndX, worldEndY));
    }

    public Vector2 GetMouseWorldPosition()
    {
        Vector2 raylibWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);
        raylibWorld.Y = -raylibWorld.Y;
        return raylibWorld / BlockSize;
    }

    public void Dispose()
    {
        if (_shadersInitialized)
        {
            Raylib.UnloadShader(_worldRenderingShader);
        }
    }
}