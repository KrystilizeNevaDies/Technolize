#version 330

uniform sampler2D currentState;

in vec2 fragTexCoord;
out vec4 fragColor;

// Helper to get a block's full color data
vec4 getBlock(vec2 pos) {
    vec2 texelSize = 1.0 / textureSize(currentState, 0);
    return texture(currentState, pos * texelSize);
}

void main() {
    float textureWidth = textureSize(currentState, 0).x;
    float texelSizeX = 1.0 / textureWidth;

    vec2 lineEnd;
    if (fragTexCoord.x / texelSizeX == 0.0) lineEnd = vec2(1.0, 0.0) * textureWidth + 1;
    if (fragTexCoord.x / texelSizeX == 1.0) lineEnd = vec2(-1.0, 0.0) * textureWidth + 1;
    if (fragTexCoord.x / texelSizeX == 2.0) lineEnd = vec2(0.0, 1.0) * textureWidth + 1;
    if (fragTexCoord.x / texelSizeX == 3.0) lineEnd = vec2(0.0, -1.0) * textureWidth + 1;

    // check this line to see if it is all air (black)
    for (int x = 0; x < lineEnd.x; x++) {
        for (int y = 0; y < lineEnd.y; y++) {
            vec4 blockColor = getBlock(vec2(x, y));
            if (blockColor.r > 0.0 || blockColor.g > 0.0 || blockColor.b > 0.0) {
                fragColor = vec4(1.0, 1.0, 1.0, 1.0); // Not all air, so draw white
                return;
            }
        }
    }

    fragColor = vec4(0.0, 0.0, 0.0, 0.0);
}
