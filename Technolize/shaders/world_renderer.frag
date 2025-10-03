#version 330

// Input from vertex shader
in vec2 fragTexCoord;
in vec4 fragColor;

// Output color for the current pixel
out vec4 finalColor;

// Uniforms - world data and block colors
uniform sampler2D worldData;    // Contains block IDs as encoded colors
uniform sampler2D blockColors;  // Lookup table for block ID -> color mapping
uniform vec2 regionSize;        // Size of the region in blocks

// Block color constants (matching C# Blocks class)
vec3 getBlockColor(float blockId) {
    // Convert block ID to normalized value for texture lookup
    // Block IDs are encoded as the red channel normalized to [0,1]
    vec2 colorLookup = vec2(blockId, 0.5);
    return texture(blockColors, colorLookup).rgb;
}

void main()
{
    // Sample the block ID from world data
    vec4 worldSample = texture(worldData, fragTexCoord);
    
    // Extract block ID from red channel (assuming block IDs are stored as normalized values)
    float blockId = worldSample.r;
    
    // Get the color for this block ID
    vec3 color = getBlockColor(blockId);
    
    // Set the final output color
    finalColor = vec4(color, 1.0);
}