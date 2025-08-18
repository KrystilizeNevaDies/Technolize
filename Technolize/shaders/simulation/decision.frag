#version 330

uniform sampler2D worldState;

in vec2 fragTexCoord;
out vec4 fragColor;

// block consts
const vec3 AIR = vec3(0, 0, 0);
const vec3 WATER = vec3(0, 0, 255.0 / 255.0);
const vec3 SAND = vec3(194.0 / 255.0, 178.0 / 255.0, 128.0 / 255.0);
const vec3 STONE = vec3(128.0 / 255.0, 128.0 / 255.0, 128.0 / 255.0);

float dist(vec3 a, vec3 b) {
    return length(a - b);
}

vec3 getClosestBlock(vec3 block) {
    // get the closest block
    float airDist = dist(block, AIR);
    float waterDist = dist(block, WATER);
    float sandDist = dist(block, SAND);
    float stoneDist = dist(block, STONE);

    if (airDist < waterDist && airDist < sandDist && airDist < stoneDist) {
        return AIR;
    }

    if (waterDist < airDist && waterDist < sandDist && waterDist < stoneDist) {
        return WATER;
    }

    if (sandDist < airDist && sandDist < waterDist && sandDist < stoneDist) {
        return SAND;
    }

    if (stoneDist < airDist && stoneDist < waterDist && stoneDist < sandDist) {
        return STONE;
    }

    // if all distances are equal for some reason, return air
    return AIR;
}

vec3 getBlock(vec2 offset) {
    vec2 texelSize = 1.0 / textureSize(worldState, 0);
    return getClosestBlock(texture(worldState, fragTexCoord + offset * texelSize).rgb);
}

vec2 trySandGravity(vec3 block) {
    if (block == SAND && getBlock(vec2(0.0, -1.0)) == AIR) {
        return vec2(0.0, -1.0);
    }
    return vec2(0.0);
}

void applyDirection(vec2 moveDir) {
    fragColor = vec4(moveDir.x * 0.5 + 0.5, moveDir.y * 0.5 + 0.5, 0.0, 1.0);
}

float random(vec2 st) {
    return fract(sin(dot(st.xy, vec2(12.9898,78.233))) * 43758.5453123);
}

void main()
{
    vec3 block = getBlock(vec2(0.0));
    vec3 upBlock = getBlock(vec2(0.0, 1.0));
    vec3 downBlock = getBlock(vec2(0.0, -1.0));

    if (block == SAND || block == WATER) {
        // gravity first
        if (getBlock(vec2(0.0, -1.0)) == AIR) {
            applyDirection(vec2(0.0, -1.0));
            return;
        }

        // settling movement
        vec3 leftDownBlock = getBlock(vec2(-1.0, 0.0));
        vec3 rightDownBlock = getBlock(vec2(1.0, 0.0));

        // check if we can move left
        bool canSettleLeft = leftDownBlock == AIR && upBlock == block && downBlock == block;
        bool canSettleRight = rightDownBlock == AIR && upBlock == block && downBlock == block;

        if (canSettleLeft && canSettleRight) {
            // if both sides are free, move randomly
            applyDirection(vec2((random(fragTexCoord) < 0.5 ? -1.0 : 1.0), -1.0));
            return;
        }

        if (canSettleLeft) {
            applyDirection(vec2(-1.0, -1.0));
            return;
        }

        if (canSettleRight) {
            applyDirection(vec2(1.0, -1.0));
            return;
        }
    }

    if (block == WATER) {
        // water movement
        vec3 leftBlock = getBlock(vec2(-1.0, 0.0));
        vec3 rightBlock = getBlock(vec2(1.0, 0.0));

        bool canFlowLeft = leftBlock == AIR;
        bool canFlowRight = rightBlock == AIR;

        if (canFlowLeft && canFlowRight) {
            // if both sides are free, move randomly
            applyDirection(vec2((random(fragTexCoord) < 0.5 ? -1.0 : 1.0), 0.0));
            return;
        }

        if (canFlowLeft) {
            applyDirection(vec2(-1.0, 0.0));
            return;
        }

        if (canFlowRight) {
            applyDirection(vec2(1.0, 0.0));
            return;
        }
    }

    applyDirection(vec2(0.0));
}
