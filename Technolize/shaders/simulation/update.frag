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

bool testNeighbor(vec2 offset) {
    vec2 decision = getDecision(offset);
    if (offset + decision == vec2(0.0, 0.0)) {
        // If the decision matches the offset, we are moving into this block.
        fragColor = getBlock(offset);
        return true;
    }
    return false;
}

void main() {
    // Default to staying the same.
    fragColor = getBlock(vec2(0.0, 0.0));

    if (testNeighbor(vec2(-1.0, 1.0))) return; // Top Left
    if (testNeighbor(vec2(0.0, 1.0))) return;  // Top
    if (testNeighbor(vec2(1.0, 1.0))) return;  // Top Right
    if (testNeighbor(vec2(-1.0, 0.0))) return;  // Left
    if (testNeighbor(vec2(0.0, 0.0))) return;   // Middle
    if (testNeighbor(vec2(1.0, 0.0))) return;   // Right
    if (testNeighbor(vec2(-1.0, -1.0))) return;  // Bottom Left
    if (testNeighbor(vec2(0.0, -1.0))) return;   // Bottom
    if (testNeighbor(vec2(1.0, -1.0))) return;   // Bottom Right

    // --- If nobody is moving in, check if we are moving out ---
    if (getDecision(vec2(0.0)) != vec2(0.0)) {
        // We decided to move, so our spot becomes Air (black).
        fragColor = vec4(0.0, 0.0, 0.0, 1.0);
    }
}
