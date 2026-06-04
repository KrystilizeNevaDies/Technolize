using System.Numerics;
using Raylib_cs;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Rendering;

/// <summary>
/// World renderer that builds a single visible-world quadtree each frame and renders it in one shader pass.
/// </summary>
public class WorldShaderRenderer(IWorldRenderSource renderSource, int screenWidth, int screenHeight) : IWorldRenderer
{
    private const int MaxShaderLights = 16;
    private const byte AirMaterialCode = 0;
    private const byte WaterMaterialCode = 1;
    private const byte SolidMaterialCode = 2;
    private const byte InternalNodeMaterialCode = byte.MaxValue;
    private const double RefractionEncodingScale = 4096.0;
    private const int BlockSize = 16;
    private const float BlockSizeFloat = BlockSize;

    private sealed class GlobalRenderResources(Texture2D worldColorTexture, Texture2D quadtreeStructureTexture, Texture2D quadtreeOpticsTexture, Vector2 worldOrigin, Vector2 worldSize, Vector2 quadtreeSize, Vector2 quadtreeTextureSize) : IDisposable
    {
        public Texture2D WorldColorTexture { get; } = worldColorTexture;
        public Texture2D QuadtreeStructureTexture { get; } = quadtreeStructureTexture;
        public Texture2D QuadtreeOpticsTexture { get; } = quadtreeOpticsTexture;
        public Vector2 WorldOrigin { get; } = worldOrigin;
        public Vector2 WorldSize { get; } = worldSize;
        public Vector2 QuadtreeSize { get; } = quadtreeSize;
        public Vector2 QuadtreeTextureSize { get; } = quadtreeTextureSize;

        public void Dispose()
        {
            Raylib.UnloadTexture(WorldColorTexture);
            Raylib.UnloadTexture(QuadtreeStructureTexture);
            Raylib.UnloadTexture(QuadtreeOpticsTexture);
        }
    }

    private readonly record struct WorldCell(Color Color, byte MaterialCode, ushort EncodedRefractionIndex);
    private readonly record struct QuadtreeNode(int FirstChildIndex, byte MaterialCode, ushort EncodedRefractionIndex);

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
    private int _quadtreeStructureLocation;
    private int _quadtreeOpticsLocation;
    private bool _shadersInitialized;

    private static readonly Color GridColor = new(255, 255, 255, 64);
    private static readonly Color AirColor = Blocks.Air.GetTag(BlockInfo.TagColor);
    private static readonly Color ScheduledRegionFillColor = new(255, 196, 64, 32);
    private static readonly Color ScheduledRegionBorderColor = new(255, 210, 96, 190);
    private static readonly Vector2 RegionSizeVector = new(TickableWorld.RegionSize);

    public bool ShowScheduledRegionOverlay { get; set; }
    public WorldLighting Lighting { get; set; } = WorldLighting.Default;

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
        _quadtreeStructureLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeStructure");
        _quadtreeOpticsLocation = Raylib.GetShaderLocation(_worldRenderingShader, "quadtreeOptics");

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

        WorldRenderFrame frame = renderSource.CaptureFrame(visibleRegionStart, visibleRegionEnd);
        using GlobalRenderResources globalResources = CreateVisibleWorldResources(frame, visibleRegionStart, visibleRegionEnd);

        Raylib.BeginMode2D(_camera);
        Raylib.ClearBackground(AirColor);
        DrawVisibleWorldWithShader(globalResources);
        RenderGrid(worldStart, worldEnd);
        if (ShowScheduledRegionOverlay)
        {
            RenderScheduledRegionOverlay(frame.ScheduledRegions);
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
        Raylib.DrawText($"Visible Region Count: {frame.Regions.Count}", 10, 70, 20, Color.White);
        if (ShowScheduledRegionOverlay)
        {
            Raylib.DrawText($"Scheduled Region Count: {frame.ScheduledRegions.Count}", 10, 100, 20, ScheduledRegionBorderColor);
        }
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
        Raylib.SetShaderValue(_worldRenderingShader, _quadtreeTextureSizeLocation, resources.QuadtreeTextureSize, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValueTexture(_worldRenderingShader, _quadtreeStructureLocation, resources.QuadtreeStructureTexture);
        Raylib.SetShaderValueTexture(_worldRenderingShader, _quadtreeOpticsLocation, resources.QuadtreeOpticsTexture);
        ApplyLighting(resources.WorldOrigin, resources.WorldSize);

        Raylib.BeginShaderMode(_worldRenderingShader);
        Raylib.DrawTexturePro(resources.WorldColorTexture, source, dest, new(0, 0), 0.0f, Color.White);
        Raylib.EndShaderMode();
    }

    private void ApplyLighting(Vector2 worldOrigin, Vector2 worldSize)
    {
        Raylib.SetShaderValue(_worldRenderingShader, _timeLocation, (float)Raylib.GetTime(), ShaderUniformDataType.Float);

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

    private static GlobalRenderResources CreateVisibleWorldResources(WorldRenderFrame frame, Vector2 visibleRegionStart, Vector2 visibleRegionEnd)
    {
        int worldWidth = Math.Max(1, (int)((visibleRegionEnd.X - visibleRegionStart.X) * TickableWorld.RegionSize));
        int worldHeight = Math.Max(1, (int)((visibleRegionEnd.Y - visibleRegionStart.Y) * TickableWorld.RegionSize));
        int quadtreeSide = NextPowerOfTwo(Math.Max(worldWidth, worldHeight));

        WorldCell[,] cells = CreateVisibleWorldCells(frame, visibleRegionStart, worldWidth, worldHeight, quadtreeSide);
        Texture2D worldColorTexture = CreateWorldColorTexture(cells, worldWidth, worldHeight);
        (Texture2D quadtreeStructureTexture, Texture2D quadtreeOpticsTexture, Vector2 quadtreeTextureSize) = CreateQuadtreeTextures(cells);

        return new GlobalRenderResources(
            worldColorTexture,
            quadtreeStructureTexture,
            quadtreeOpticsTexture,
            visibleRegionStart * RegionSizeVector,
            new Vector2(worldWidth, worldHeight),
            new Vector2(quadtreeSide, quadtreeSide),
            quadtreeTextureSize);
    }

    private static WorldCell[,] CreateVisibleWorldCells(WorldRenderFrame frame, Vector2 visibleRegionStart, int worldWidth, int worldHeight, int quadtreeSide)
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
            int regionBaseX = (int)((region.Position.X - visibleRegionStart.X) * TickableWorld.RegionSize);
            int regionBaseY = (int)((region.Position.Y - visibleRegionStart.Y) * TickableWorld.RegionSize);

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
            GetMaterialCode(blockInfo),
            EncodeRefractionIndex(blockInfo.GetTag(BlockInfo.TagRefractionIndex)));
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

    private static (Texture2D structureTexture, Texture2D opticsTexture, Vector2 textureSize) CreateQuadtreeTextures(WorldCell[,] cells)
    {
        const int QuadtreeTextureWidth = 2048;
        List<QuadtreeNode> nodes = [];
        BuildQuadtreeNode(cells, 0, 0, cells.GetLength(0), nodes);

        int textureWidth = Math.Min(QuadtreeTextureWidth, Math.Max(nodes.Count, 1));
        int textureHeight = (nodes.Count + textureWidth - 1) / textureWidth;

        Image structureImage = Raylib.GenImageColor(textureWidth, textureHeight, Color.Black);
        Image opticsImage = Raylib.GenImageColor(textureWidth, textureHeight, Color.Black);

        for (int i = 0; i < nodes.Count; i++)
        {
            QuadtreeNode node = nodes[i];
            int textureX = i % textureWidth;
            int textureY = i / textureWidth;
            byte childLo = (byte)(node.FirstChildIndex & 0xFF);
            byte childMid = (byte)((node.FirstChildIndex >> 8) & 0xFF);
            byte childHi = (byte)((node.FirstChildIndex >> 16) & 0xFF);
            byte refractionLo = (byte)(node.EncodedRefractionIndex & 0xFF);
            byte refractionHi = (byte)(node.EncodedRefractionIndex >> 8);

            Raylib.ImageDrawPixel(ref structureImage, textureX, textureY, new Color(childLo, childMid, childHi, node.MaterialCode));
            Raylib.ImageDrawPixel(ref opticsImage, textureX, textureY, new Color(refractionLo, refractionHi, node.MaterialCode, (byte)255));
        }

        Texture2D structureTexture = Raylib.LoadTextureFromImage(structureImage);
        Texture2D opticsTexture = Raylib.LoadTextureFromImage(opticsImage);
        Raylib.SetTextureFilter(structureTexture, TextureFilter.Point);
        Raylib.SetTextureFilter(opticsTexture, TextureFilter.Point);
        Raylib.UnloadImage(structureImage);
        Raylib.UnloadImage(opticsImage);
        return (structureTexture, opticsTexture, new Vector2(textureWidth, textureHeight));
    }

    private static int BuildQuadtreeNode(WorldCell[,] cells, int startX, int startY, int size, List<QuadtreeNode> nodes)
    {
        int nodeIndex = nodes.Count;
        nodes.Add(default);

        if (size == 1)
        {
            WorldCell singleCell = cells[startX, startY];
            nodes[nodeIndex] = new QuadtreeNode(0, singleCell.MaterialCode, singleCell.EncodedRefractionIndex);
            return nodeIndex;
        }

        if (TryGetUniformCell(cells, startX, startY, size, out WorldCell uniformCell))
        {
            nodes[nodeIndex] = new QuadtreeNode(0, uniformCell.MaterialCode, uniformCell.EncodedRefractionIndex);
            return nodeIndex;
        }

        int firstChildIndex = nodes.Count;
        int childSize = size / 2;
        BuildQuadtreeNode(cells, startX, startY, childSize, nodes);
        BuildQuadtreeNode(cells, startX + childSize, startY, childSize, nodes);
        BuildQuadtreeNode(cells, startX, startY + childSize, childSize, nodes);
        BuildQuadtreeNode(cells, startX + childSize, startY + childSize, childSize, nodes);
        nodes[nodeIndex] = new QuadtreeNode(firstChildIndex, InternalNodeMaterialCode, 0);
        return nodeIndex;
    }

    private static bool TryGetUniformCell(WorldCell[,] cells, int startX, int startY, int size, out WorldCell uniformCell)
    {
        uniformCell = cells[startX, startY];
        for (int y = startY; y < startY + size; y++)
        {
            for (int x = startX; x < startX + size; x++)
            {
                if (!cells[x, y].Equals(uniformCell))
                {
                    return false;
                }
            }
        }

        return true;
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