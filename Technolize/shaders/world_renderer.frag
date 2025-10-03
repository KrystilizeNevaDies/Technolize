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

void main()
{
    // Sample the block ID from world data
    vec4 worldSample = texture(worldData, fragTexCoord);
    
    // Extract block ID from red channel
    float blockId = worldSample.r;
    
    // Early exit for air blocks (blockId == 0) to improve performance
    if (blockId < 0.004) // Approximately 1/255, accounting for floating point precision
    {
        discard; // Let the background color (air) show through
    }
    
    // Get the color for this block ID using direct texture lookup
    // Block IDs are stored as normalized values, so we can use them directly
    vec2 colorLookup = vec2(blockId, 0.5);
    vec3 color = texture(blockColors, colorLookup).rgb;
    
    // Set the final output color
    finalColor = vec4(color, 1.0);
}