using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using Technolize.Utils;
namespace Technolize.World.Ticking;

public class GpuWorldTicker
{
    private const int GpuPadding = 2; // padding on both sides of the region sim.
    private const int GpuRegionSize = GpuTextureWorld.RegionSize + GpuPadding * 2; // Size of a gpu region

    private readonly GpuTextureWorld _world;
    private readonly Shader _decisionShader;
    private readonly Shader _updateShader;
    private readonly Shader _outlineShader;

    private readonly Image _renderingBoundsImg;
    private readonly Texture2D _renderingBounds;

    private readonly RenderTexture2D _paddedInputTarget;
    private readonly RenderTexture2D _decisionTarget;
    private readonly RenderTexture2D _applyTarget;

    private Stopwatch startTime = new ();

    public GpuWorldTicker(GpuTextureWorld world)
    {
        _world = world;
        _decisionShader = Raylib.LoadShader(null, "shaders/simulation/decision.frag");
        _updateShader = Raylib.LoadShader(null, "shaders/simulation/update.frag");
        _outlineShader = Raylib.LoadShader(null, "shaders/simulation/outline.frag");

        _renderingBoundsImg = Raylib.GenImageColor(GpuRegionSize, GpuRegionSize, new (0, 0, 0, 0));
        _renderingBounds = Raylib.LoadTextureFromImage(_renderingBoundsImg);

        _paddedInputTarget = Raylib.LoadRenderTexture(GpuRegionSize, GpuRegionSize);
        _decisionTarget = Raylib.LoadRenderTexture(GpuRegionSize, GpuRegionSize);
        _applyTarget = Raylib.LoadRenderTexture(GpuRegionSize, GpuRegionSize);

        startTime.Stop();
    }

    public void Tick()
    {
        List<Action> regionTickActions = [];

        foreach ((Vector2 pos, RenderTexture2D texture) in _world.Regions)
        {
            regionTickActions.Add(TickRegion(pos, texture));
        }

        // Execute all region tick actions
        foreach (Action action in regionTickActions)
        {
            action();
        }
    }

    private void DrawTextureWithPadding(Vector2 pos, RenderTexture2D paddedWorldTexture)
    {
        Raylib.BeginTextureMode(paddedWorldTexture);
        Raylib.ClearBackground(Color.Black);

        // Image original = Raylib.LoadImageFromTexture(paddedWorldTexture.Texture);
        // Images.PrintImage("original:", original);

        // draw the neighboring regions as padding
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2 neighborPos = pos with { X = pos.X + dx, Y = pos.Y + dy };
                if (_world.Regions.TryGetValue(neighborPos, out RenderTexture2D neighborTexture))
                {

                    Rectangle sourceRec = new (
                        0,
                        0,
                        GpuTextureWorld.RegionSize,
                        GpuTextureWorld.RegionSize
                    );
                    Rectangle targetRec = new (
                        GpuPadding + dx * GpuTextureWorld.RegionSize + GpuPadding * -dx,
                        GpuPadding + dy * GpuTextureWorld.RegionSize + GpuPadding * -dy,
                        GpuTextureWorld.RegionSize,
                        GpuTextureWorld.RegionSize
                    );
                    Raylib.DrawTexturePro(neighborTexture.Texture, sourceRec,
                        targetRec,
                        Vector2.Zero, 0, Color.White);

                    // Image neighborImage = Raylib.LoadImageFromTexture(neighborTexture.Texture);
                    // Images.PrintImage($"neighbor ({dx}, {dy}):", neighborImage);
                }
            }
        }

        Raylib.EndTextureMode();

        // Image paddedImage = Raylib.LoadImageFromTexture(paddedWorldTexture.Texture);
        // Images.PrintImage("padded:", paddedImage);

        // Images.ViewImage(paddedImage);
    }

    private Action TickRegion(Vector2 pos, RenderTexture2D worldTexture)
    {
        // Pad the input texture to account for neighbors
        DrawTextureWithPadding(pos, _paddedInputTarget);

        GenerateDecisionMatrix(pos, _paddedInputTarget, _decisionTarget);

        ApplyDecisionMatrix(_paddedInputTarget, _decisionTarget, _applyTarget);

        Raylib.BeginTextureMode(worldTexture);
        Raylib.ClearBackground(Color.Black);

        Rectangle sourceRec = new (GpuPadding, GpuPadding, GpuTextureWorld.RegionSize, -GpuTextureWorld.RegionSize);
        Raylib.DrawTexturePro(_applyTarget.Texture, sourceRec, new (0, 0, GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize), Vector2.Zero, 0, Color.White);
        Raylib.EndTextureMode();

        // checks edges and creates new regions if necessary
        return () => CheckEdges(pos, worldTexture);
    }

    private void GenerateDecisionMatrix(Vector2 pos, RenderTexture2D worldTexture, RenderTexture2D target)
    {

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(_decisionShader);

        // Bind the texture AFTER beginning shader mode
        Raylib.SetShaderValueTexture(_decisionShader, Raylib.GetShaderLocation(_decisionShader, "worldState"), worldTexture.Texture);
        Raylib.SetShaderValue(_decisionShader, Raylib.GetShaderLocation(_decisionShader, "time"), startTime.ElapsedMilliseconds / 1000f, ShaderUniformDataType.Float);

        Raylib.DrawTexture(_renderingBounds, 0, 0, Color.White);

        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    private void ApplyDecisionMatrix(RenderTexture2D worldTexture, RenderTexture2D decisionTexture, RenderTexture2D finalTarget)
    {
        // setup rendering context
        Raylib.BeginTextureMode(finalTarget);
        Raylib.ClearBackground(Color.Blank); // Important to clear the target
        Raylib.BeginShaderMode(_updateShader);

        // Bind both textures
        Raylib.SetShaderValueTexture(_updateShader, Raylib.GetShaderLocation(_updateShader, "currentState"), worldTexture.Texture);
        Raylib.SetShaderValueTexture(_updateShader, Raylib.GetShaderLocation(_updateShader, "decisionTexture"), decisionTexture.Texture);

        // Draw a texture to trigger the shader across the entire render target.
        // The content of this draw doesn't matter, only its size and position.
        Raylib.DrawTexture(_renderingBounds, 0, 0, Color.White);

        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    private void CheckEdges(Vector2 pos, RenderTexture2D worldTexture)
    {
        // if this regions is already completely surrounded by other regions, we can skip the edge check
        if (_world.Regions.ContainsKey(pos with
            {
                X = pos.X - 1
            }) &&
            _world.Regions.ContainsKey(pos with
            {
                X = pos.X + 1
            }) &&
            _world.Regions.ContainsKey(pos with
            {
                Y = pos.Y + 1
            }) &&
            _world.Regions.ContainsKey(pos with
            {
                Y = pos.Y - 1
            }))
        {
            return;
        }

        RenderTexture2D edgesResult = Raylib.LoadRenderTexture(4, 1);
        Image renderingBoundsImg = Raylib.GenImageColor(4, 1, Color.White);
        Texture2D renderingBounds = Raylib.LoadTextureFromImage(renderingBoundsImg);

        // setup rendering context
        Raylib.BeginTextureMode(edgesResult);
        Raylib.ClearBackground(Color.Blank); // Important to clear the target
        Raylib.BeginShaderMode(_outlineShader);

        // Bind both textures
        Raylib.SetShaderValueTexture(_outlineShader, Raylib.GetShaderLocation(_outlineShader, "currentState"), worldTexture.Texture);

        // Draw a texture to trigger the shader across the entire render target.
        // The content of this draw doesn't matter, only its size and position.
        Raylib.DrawTexture(renderingBounds, 0, 0, Color.White);

        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        Image imageResult = Raylib.LoadImageFromTexture(edgesResult.Texture);

        try
        {
            if (!Images.IsImageBlack(imageResult))
            {
                Images.PrintImage("edges result:", imageResult);
            }

            // If the edges result is not all black, we have to create a new region
            // order is left, right, top, bottom

            // left
            if (Raylib.GetImageColor(imageResult, 0, 0).R > 0)
            {
                Vector2 newRegionPos = pos with { X = pos.X - 1 };
                if (!_world.Regions.ContainsKey(newRegionPos))
                {
                    _world.ComputeRegion(newRegionPos, out _);
                }
            }

            // right
            if (Raylib.GetImageColor(imageResult, 1, 0).R > 0)
            {
                Vector2 newRegionPos = pos with { X = pos.X + 1 };
                if (!_world.Regions.ContainsKey(newRegionPos))
                {
                    _world.ComputeRegion(newRegionPos, out _);
                }
            }

            // top
            if (Raylib.GetImageColor(imageResult, 2, 0).R > 0)
            {
                Vector2 newRegionPos = pos with { Y = pos.Y + 1 };
                if (!_world.Regions.ContainsKey(newRegionPos))
                {
                    _world.ComputeRegion(newRegionPos, out _);
                }
            }

            if (Raylib.GetImageColor(imageResult, 3, 0).R > 0)
            {
                Vector2 newRegionPos = pos with { Y = pos.Y - 1 };
                if (!_world.Regions.ContainsKey(newRegionPos))
                {
                    _world.ComputeRegion(newRegionPos, out _);
                }
            }
        }
        finally
        {
            // cleanup
            Raylib.UnloadTexture(renderingBounds);
            Raylib.UnloadImage(renderingBoundsImg);
            Raylib.UnloadImage(imageResult);
            Raylib.UnloadRenderTexture(edgesResult);
        }
    }

    public void Unload()
    {
        Raylib.UnloadShader(_decisionShader);
        Raylib.UnloadShader(_updateShader);
    }
}
