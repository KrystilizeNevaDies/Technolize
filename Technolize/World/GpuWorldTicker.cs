using System.Numerics;
using Raylib_cs;
using Technolize.Utils;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.World;

public class GpuWorldTicker
{
    private readonly GpuTextureWorld _world;
    private readonly Shader _decisionShader;
    private readonly Shader _updateShader;

    private readonly Image _renderingBoundsImg;
    private readonly Texture2D _renderingBounds;

    private readonly RenderTexture2D _decisionTarget;
    private readonly RenderTexture2D _applyTarget;

    public GpuWorldTicker(GpuTextureWorld world)
    {
        _world = world;
        _decisionShader = Raylib.LoadShaderFromMemory(null, DecisionShaderCode);
        _updateShader = Raylib.LoadShaderFromMemory(null, UpdateShaderCode);

        _renderingBoundsImg = Raylib.GenImageColor(GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize, new Color(0, 0, 0, 0));
        _renderingBounds = Raylib.LoadTextureFromImage(_renderingBoundsImg);

        _decisionTarget = Raylib.LoadRenderTexture(GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize);
        _applyTarget = Raylib.LoadRenderTexture(GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize);
    }

    private const string DecisionShaderCode = @"
#version 330

uniform sampler2D worldState;

in vec2 fragTexCoord;
out vec4 fragColor;

void main()
{
    vec4 thisBlockColor = texture(worldState, fragTexCoord);

    vec2 texelSize = 1.0 / textureSize(worldState, 0);
    vec4 blockBelowColor = texture(worldState, fragTexCoord - vec2(0.0, texelSize.y));

    vec2 moveDirection = vec2(0.0);

    bool isNonAir = thisBlockColor.r > 0.0 || thisBlockColor.g > 0.0 || thisBlockColor.b > 0.0;
    bool isBelowAir = blockBelowColor.r == 0.0 && blockBelowColor.g == 0.0 && blockBelowColor.b == 0.0;

    if (isNonAir && isBelowAir)
    {
        moveDirection = vec2(0.0, -1.0);
    }

    fragColor = vec4(moveDirection.x * 0.5 + 0.5, moveDirection.y * 0.5 + 0.5, 0.0, 1.0);
}
";

    private const string UpdateShaderCode = @"
#version 330

uniform sampler2D currentState;
uniform sampler2D decisionTexture;

in vec2 fragTexCoord;
out vec4 fragColor;

// Helper to get a block's full color data from an offset.
vec4 getBlock(vec2 offset) {
    vec2 texelSize = 1.0 / textureSize(currentState, 0);
    return texture(currentState, fragTexCoord + offset * texelSize);
}

// Helper to decode a movement vector from the decision texture.
vec2 getDecision(vec2 offset) {
    vec2 texelSize = 1.0 / textureSize(decisionTexture, 0);
    vec2 coord = fragTexCoord + offset * texelSize;
    // y needs to be flipped because the texture is flipped vertically.
    coord.y = 1.0 - coord.y; // Flip Y coordinate for correct sampling
    vec4 encoded = texture(decisionTexture, coord);
    return vec2(round((encoded.r - 0.5) * 2.0), round((encoded.g - 0.5) * 2.0));
}

void main() {
    // Default to staying the same.
    fragColor = getBlock(vec2(0.0, 0.0));
    //fragColor = vec4((getDecision(vec2(0.0, 0.0)) + 1.0) * 0.5, 0.0, 1.0);

    // If the block above us decided to move down, we become that block.
    if (getDecision(vec2(0.0, 1.0)) == vec2(0.0, -1.0)) { 
        fragColor = getBlock(vec2(0.0, 1.0));
        return;
    }

    // --- If nobody is moving in, check if we are moving out ---
    if (getDecision(vec2(0.0)) != vec2(0.0)) {
        // We decided to move, so our spot becomes Air (black).
        fragColor = vec4(0.0, 0.0, 0.0, 1.0);
    }
}
";

    public void Tick()
    {
        foreach (var (pos, texture) in _world.Regions)
        {
            TickRegion(pos, texture);
        }
    }

    private void TickRegion(Vector2 pos, RenderTexture2D worldTexture)
    {
        GenerateDecisionMatrix(pos, worldTexture, _decisionTarget);

        ApplyDecisionMatrix(worldTexture, _decisionTarget, _applyTarget);

        Raylib.BeginTextureMode(worldTexture);
        Raylib.ClearBackground(Color.Black);
        Raylib.DrawTexture(_applyTarget.Texture, 0, 0, Color.White);
        Raylib.EndTextureMode();

        // reset textures
        Raylib.BeginTextureMode(_decisionTarget);
        Raylib.ClearBackground(Color.Black);
        Raylib.EndTextureMode();

        Raylib.BeginTextureMode(_applyTarget);
        Raylib.ClearBackground(Color.Black);
        Raylib.EndTextureMode();
    }

    private void GenerateDecisionMatrix(Vector2 pos, RenderTexture2D worldTexture, RenderTexture2D target)
    {

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(_decisionShader);

        // Bind the texture AFTER beginning shader mode
        Raylib.SetShaderValueTexture(_decisionShader, Raylib.GetShaderLocation(_decisionShader, "worldState"), worldTexture.Texture);

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

    public void Unload()
    {
        Raylib.UnloadShader(_decisionShader);
        Raylib.UnloadShader(_updateShader);
    }
}
