using System.Numerics;
using Raylib_cs;
using Technolize.Utils;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.World;

public class GpuWorldTicker(GpuTextureWorld world)
{
    private readonly Random _random = new();

    public void Tick()
    {
        foreach (var (pos, texture) in world.Regions)
        {
            TickRegion(pos, texture);
        }
    }

    private void TickRegion(Vector2 pos, RenderTexture2D worldTexture)
    {
        RenderTexture2D target = Raylib.LoadRenderTexture(GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize);
        GenerateDecisionMatrix(pos, worldTexture, target);

        // target now has directions for each block
        // Apply the decisions to the world texture
        Image decisionImage = Raylib.LoadImageFromTexture(target.Texture);

        Images.PrintImageDirs("decisions: ", decisionImage);
        Images.ViewImage(decisionImage);
    }

    private void GenerateDecisionMatrix(Vector2 pos, RenderTexture2D worldTexture, RenderTexture2D target)
    {
        var shaderCode = @"
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

        // Create a shader from the code
        Shader shader = Raylib.LoadShaderFromMemory(null, shaderCode);

        // setup rendering context
        Image renderingBoundsImg = Raylib.GenImageColor(GpuTextureWorld.RegionSize, GpuTextureWorld.RegionSize, new Color(0, 0, 0, 0));
        Texture2D renderingBounds = Raylib.LoadTextureFromImage(renderingBoundsImg);

        Image worldStateImg = Raylib.LoadImageFromTexture(worldTexture.Texture);

        Raylib.BeginTextureMode(target);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(shader);

        // Bind the texture AFTER beginning shader mode
        Raylib.SetShaderValueTexture(shader, Raylib.GetShaderLocation(shader, "worldState"), worldTexture.Texture);

        Raylib.DrawTexture(renderingBounds, 0, 0, Color.White);

        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        Raylib.UnloadShader(shader);

        Image outputImage = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref outputImage);
    }

    private void ApplyDecisionMatrix(RenderTexture2D worldTexture, RenderTexture2D decisionTexture, RenderTexture2D finalTarget)
{
    // This shader reads from the original world state and the decision matrix
    // to calculate the final position of each block.
    var shaderCode = @"
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
    vec4 encoded = texture(decisionTexture, fragTexCoord + offset * texelSize);
    // Decode from [0, 1] color range back to [-1, 1] vector range
    return vec2(round((encoded.r - 0.5) * 2.0), round((encoded.g - 0.5) * 2.0));
}

void main() {
    // Default to staying the same.
    fragColor = getBlock(vec2(0.0));

    // --- Check if any neighbor wants to move INTO our spot ---
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
    Shader shader = Raylib.LoadShaderFromMemory(null, shaderCode);

    Raylib.BeginTextureMode(finalTarget);
    Raylib.ClearBackground(Color.Blank); // Important to clear the target
    Raylib.BeginShaderMode(shader);

    // Get shader locations for our two input textures
    int currentStateLoc = Raylib.GetShaderLocation(shader, "currentState");
    int decisionTexLoc = Raylib.GetShaderLocation(shader, "decisionTexture");

    // Bind both textures
    Raylib.SetShaderValueTexture(shader, currentStateLoc, worldTexture.Texture);
    Raylib.SetShaderValueTexture(shader, decisionTexLoc, decisionTexture.Texture);

    // Draw a texture to trigger the shader across the entire render target.
    // The content of this draw doesn't matter, only its size and position.
    Raylib.DrawTexture(worldTexture.Texture, 0, 0, Color.White);

    Raylib.EndShaderMode();
    Raylib.EndTextureMode();

    Raylib.UnloadShader(shader);
}
}
