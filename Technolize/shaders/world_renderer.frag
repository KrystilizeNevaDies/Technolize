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

ivec2 getTextureCoord(vec2 texCoord, ivec2 textureDimensions) {
    vec2 scaledCoord = texCoord * vec2(textureDimensions);
    ivec2 clampedCoord = ivec2(floor(scaledCoord));
    clampedCoord.x = clamp(clampedCoord.x, 0, textureDimensions.x - 1);
    clampedCoord.y = clamp(clampedCoord.y, 0, textureDimensions.y - 1);
    return clampedCoord;
}

uint decodeBlockId(vec4 encodedSample) {
    uvec4 encodedBytes = uvec4(round(encodedSample * 255.0));
    return encodedBytes.r |
        (encodedBytes.g << 8u) |
        (encodedBytes.b << 16u) |
        (encodedBytes.a << 24u);
}

vec3 getBlockColor(uint blockId) {
    uint lookupWidth = uint(textureSize(blockColors, 0).x);
    uint clampedBlockId = min(blockId, lookupWidth - 1u);
    return texelFetch(blockColors, ivec2(int(clampedBlockId), 0), 0).rgb;
}

void main()
{
    ivec2 worldTextureSize = textureSize(worldData, 0);
    ivec2 worldCoord = getTextureCoord(fragTexCoord, worldTextureSize);
    vec4 worldSample = texelFetch(worldData, worldCoord, 0);
    
    uint blockId = decodeBlockId(worldSample);
    
    // Get the color for this block ID
    vec3 color = getBlockColor(blockId);
    
    // Set the final output color
    finalColor = vec4(color, 1.0);
}