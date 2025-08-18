#version 330

uniform sampler2D worldState;
uniform sampler2D blockIdToMatterState;

in vec2 fragTexCoord;
out vec4 fragColor;

vec4 packInt(int value) {
    float r = float(value % 256);
    value /= 256;
    float g = float(value % 256);
    value /= 256;
    float b = float(value % 256);
    return vec4(r / 255.0, g / 255.0, b / 255.0, 0);
}

int unpackInt(vec4 color) {
    return int(
        color.r * 255.0 +
        color.g * 255.0 * 256.0 +
        color.b * 255.0 * 256.0 * 256.0
    );
}

int getBlockId(vec2 offset) {
    vec4 blockData = texelFetch(worldState, ivec2(fragTexCoord * textureSize(worldState, 0)), 0);
    int blockId = unpackInt(blockData);
    return blockId;
}

int getBlockState(int blockId) {
    return unpackInt(texelFetch(blockIdToMatterState, ivec2(blockId, 0), 0));
}

void main() {
    int thisBlockId = getBlockId(vec2(0.0));
    int thisState = getBlockState(thisBlockId);

    // Normalize for color output
    fragColor = vec4(float(thisState) / 255.0);
}
