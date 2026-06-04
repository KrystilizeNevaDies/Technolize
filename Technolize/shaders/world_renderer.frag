#version 330

in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform sampler2D texture0;
uniform sampler2D quadtreeStructure;
uniform sampler2D quadtreeOptics;
uniform vec2 regionSize;
uniform vec2 regionOrigin;
uniform vec2 quadtreeSize;
uniform vec2 quadtreeTextureSize;
uniform float time;
uniform vec2 sunDirection;
uniform int sunRayCount;

const int MATERIAL_AIR = 0;
const int MATERIAL_WATER = 1;
const int MATERIAL_SOLID = 2;
const int MATERIAL_INTERNAL = 255;

const int QUADTREE_MAX_DEPTH = 16;
const int MAX_RAY_STEPS = 256;
const int MAX_LIGHT_STEPS = 256;
const float RAY_EPSILON = 0.02;
const float LARGE_DISTANCE = 1e20;

struct NodeData
{
    int firstChild;
    int material;
    float refractionIndex;
};

struct LeafSample
{
    int material;
    float refractionIndex;
    vec2 minBounds;
    vec2 maxBounds;
};

struct RaycastHit
{
    float escaped;
    float distance;
    int material;
    float refractionIndex;
    vec2 position;
    vec2 normal;
};

vec4 sampleSurface(vec2 localPos)
{
    ivec2 surfaceSize = textureSize(texture0, 0);
    ivec2 texelCoord = ivec2(clamp(floor(localPos), vec2(0.0), vec2(surfaceSize) - vec2(1.0)));
    return texelFetch(texture0, texelCoord, 0);
}

int sampleSurfaceMaterial(vec2 localPos)
{
    vec4 surfaceSample = sampleSurface(localPos);
    return int(round(surfaceSample.a * 255.0));
}

int sampleSurfaceMaterial(ivec2 texelCoord)
{
    ivec2 surfaceSize = textureSize(texture0, 0);
    ivec2 clampedCoord = clamp(texelCoord, ivec2(0), surfaceSize - ivec2(1));
    vec4 surfaceSample = texelFetch(texture0, clampedCoord, 0);
    return int(round(surfaceSample.a * 255.0));
}

float sampleWaterOccupancy(ivec2 texelCoord)
{
    return sampleSurfaceMaterial(texelCoord) == MATERIAL_WATER ? 1.0 : 0.0;
}

vec2 currentFragLocalPos()
{
    return clamp(fragTexCoord * regionSize, vec2(0.5), regionSize - vec2(0.5));
}

vec2 currentFragGlobalPos()
{
    vec2 localPos = currentFragLocalPos();
    return vec2(regionOrigin.x + localPos.x, regionOrigin.y + (regionSize.y - localPos.y));
}

bool isInsideLocal(vec2 localPos)
{
    return localPos.x >= 0.0 && localPos.y >= 0.0 && localPos.x < regionSize.x && localPos.y < regionSize.y;
}

NodeData loadNode(int nodeIndex)
{
    int textureWidth = max(int(quadtreeTextureSize.x), 1);
    ivec2 texelCoord = ivec2(nodeIndex % textureWidth, nodeIndex / textureWidth);
    vec4 structureSample = texelFetch(quadtreeStructure, texelCoord, 0);
    vec4 opticsSample = texelFetch(quadtreeOptics, texelCoord, 0);

    int firstChild =
        int(round(structureSample.r * 255.0)) |
        (int(round(structureSample.g * 255.0)) << 8) |
        (int(round(structureSample.b * 255.0)) << 16);
    int material = int(round(opticsSample.b * 255.0));
    int encodedRefraction = int(round(opticsSample.r * 255.0)) + (int(round(opticsSample.g * 255.0)) << 8);
    float refractionIndex = max(float(encodedRefraction) / 4096.0, 1.0);

    return NodeData(firstChild, material, refractionIndex);
}

LeafSample lookupLeaf(vec2 localPos)
{
    vec2 clampedPos = clamp(localPos, vec2(0.0), quadtreeSize - vec2(0.001));
    vec2 minBounds = vec2(0.0);
    vec2 maxBounds = quadtreeSize;
    int nodeIndex = 0;

    for (int depth = 0; depth < QUADTREE_MAX_DEPTH; depth++)
    {
        NodeData node = loadNode(nodeIndex);
        if (node.material != MATERIAL_INTERNAL)
        {
            return LeafSample(node.material, node.refractionIndex, minBounds, maxBounds);
        }

        vec2 center = (minBounds + maxBounds) * 0.5;
        int childOffset = 0;

        if (clampedPos.x >= center.x)
        {
            childOffset += 1;
            minBounds.x = center.x;
        }
        else
        {
            maxBounds.x = center.x;
        }

        if (clampedPos.y >= center.y)
        {
            childOffset += 2;
            minBounds.y = center.y;
        }
        else
        {
            maxBounds.y = center.y;
        }

        nodeIndex = node.firstChild + childOffset;
    }

    NodeData terminalNode = loadNode(nodeIndex);
    int fallbackMaterial = terminalNode.material == MATERIAL_INTERNAL ? MATERIAL_AIR : terminalNode.material;
    return LeafSample(fallbackMaterial, terminalNode.refractionIndex, minBounds, maxBounds);
}

float computeBoundaryDistance(vec2 position, vec2 direction, LeafSample leaf, out vec2 boundaryNormal)
{
    float distanceX = LARGE_DISTANCE;
    float distanceY = LARGE_DISTANCE;
    vec2 normalX = vec2(0.0);
    vec2 normalY = vec2(0.0);

    if (direction.x > 0.0)
    {
        distanceX = (leaf.maxBounds.x - position.x) / direction.x;
        normalX = vec2(-1.0, 0.0);
    }
    else if (direction.x < 0.0)
    {
        distanceX = (leaf.minBounds.x - position.x) / direction.x;
        normalX = vec2(1.0, 0.0);
    }

    if (direction.y > 0.0)
    {
        distanceY = (leaf.maxBounds.y - position.y) / direction.y;
        normalY = vec2(0.0, -1.0);
    }
    else if (direction.y < 0.0)
    {
        distanceY = (leaf.minBounds.y - position.y) / direction.y;
        normalY = vec2(0.0, 1.0);
    }

    if (distanceX <= distanceY)
    {
        boundaryNormal = normalX;
        return max(distanceX, 0.0);
    }

    boundaryNormal = normalY;
    return max(distanceY, 0.0);
}

RaycastHit raycast(vec2 origin, vec2 direction)
{
    RaycastHit hit;
    hit.escaped = 0.0;
    hit.distance = 0.0;
    hit.material = MATERIAL_AIR;
    hit.refractionIndex = 1.0;
    hit.position = origin;
    hit.normal = vec2(0.0, -1.0);

    vec2 position = origin;
    vec2 rayDirection = normalize(direction);

    for (int step = 0; step < MAX_RAY_STEPS; step++)
    {
        if (!isInsideLocal(position))
        {
            hit.escaped = 1.0;
            hit.position = position;
            return hit;
        }

        LeafSample leaf = lookupLeaf(position);
        vec2 boundaryNormal;
        float distanceToBoundary = computeBoundaryDistance(position, rayDirection, leaf, boundaryNormal);

        hit.distance += distanceToBoundary;
        hit.material = leaf.material;
        hit.refractionIndex = leaf.refractionIndex;
        hit.normal = boundaryNormal;

        vec2 boundaryPosition = position + rayDirection * distanceToBoundary;
        vec2 nextPosition = boundaryPosition + rayDirection * RAY_EPSILON;
        hit.position = boundaryPosition;

        if (!isInsideLocal(nextPosition))
        {
            hit.escaped = 1.0;
            return hit;
        }

        LeafSample nextLeaf = lookupLeaf(nextPosition);
        if (nextLeaf.material != leaf.material || abs(nextLeaf.refractionIndex - leaf.refractionIndex) > 0.0001)
        {
            hit.material = nextLeaf.material;
            hit.refractionIndex = nextLeaf.refractionIndex;
            hit.position = nextPosition;
            return hit;
        }

        position = nextPosition;
    }

    return hit;
}

float traceDistance(vec2 origin, vec2 direction, int material)
{
    vec2 rayDirection = normalize(direction);
    vec2 position = clamp(origin, vec2(0.001), regionSize - vec2(0.001));
    float depth = 0.0;

    if (abs(rayDirection.x) < 0.0001 && abs(rayDirection.y) < 0.0001)
    {
        return 0.0;
    }

    ivec2 cell = ivec2(floor(position));
    ivec2 stepDir = ivec2(sign(rayDirection));
    vec2 deltaDistance = vec2(
        abs(rayDirection.x) > 0.0001 ? abs(1.0 / rayDirection.x) : LARGE_DISTANCE,
        abs(rayDirection.y) > 0.0001 ? abs(1.0 / rayDirection.y) : LARGE_DISTANCE);
    vec2 nextBoundary = vec2(
        rayDirection.x > 0.0 ? float(cell.x + 1) : float(cell.x),
        rayDirection.y > 0.0 ? float(cell.y + 1) : float(cell.y));
    vec2 sideDistance = vec2(
        abs(rayDirection.x) > 0.0001 ? (nextBoundary.x - position.x) / rayDirection.x : LARGE_DISTANCE,
        abs(rayDirection.y) > 0.0001 ? (nextBoundary.y - position.y) / rayDirection.y : LARGE_DISTANCE);
    sideDistance = max(sideDistance, vec2(0.0));
    float traveled = 0.0;

    for (int step = 0; step < MAX_RAY_STEPS; step++)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= int(regionSize.x) || cell.y >= int(regionSize.y))
        {
            return depth;
        }

        if (sampleSurfaceMaterial(cell) != material)
        {
            return depth;
        }

        bool stepAlongX = sideDistance.x < sideDistance.y;
        float nextTraveled = stepAlongX ? sideDistance.x : sideDistance.y;
        depth += nextTraveled - traveled;
        traveled = nextTraveled;

        if (stepAlongX)
        {
            cell.x += stepDir.x;
            sideDistance.x += deltaDistance.x;
        }
        else
        {
            cell.y += stepDir.y;
            sideDistance.y += deltaDistance.y;
        }
    }

    return depth;
}

vec2 computeInterfaceNormal(ivec2 currentCell, ivec2 nextCell)
{
    ivec2 delta = nextCell - currentCell;

    if (delta.x > 0)
    {
        return vec2(-1.0, 0.0);
    }

    if (delta.x < 0)
    {
        return vec2(1.0, 0.0);
    }

    if (delta.y > 0)
    {
        return vec2(0.0, -1.0);
    }

    if (delta.y < 0)
    {
        return vec2(0.0, 1.0);
    }

    return -normalize(vec2(sign(float(currentCell.x)), sign(float(currentCell.y))));
}

vec2 computeSmoothedInterfaceNormal(vec2 localPos, int currentMaterial, vec2 fallbackNormal)
{
    ivec2 center = ivec2(floor(clamp(localPos, vec2(0.0), regionSize - vec2(0.001))));

    float topLeft = sampleWaterOccupancy(center + ivec2(-1, -1));
    float top = sampleWaterOccupancy(center + ivec2(0, -1));
    float topRight = sampleWaterOccupancy(center + ivec2(1, -1));
    float left = sampleWaterOccupancy(center + ivec2(-1, 0));
    float right = sampleWaterOccupancy(center + ivec2(1, 0));
    float bottomLeft = sampleWaterOccupancy(center + ivec2(-1, 1));
    float bottom = sampleWaterOccupancy(center + ivec2(0, 1));
    float bottomRight = sampleWaterOccupancy(center + ivec2(1, 1));

    float gradientX = (topRight + 2.0 * right + bottomRight) - (topLeft + 2.0 * left + bottomLeft);
    float gradientY = (bottomLeft + 2.0 * bottom + bottomRight) - (topLeft + 2.0 * top + topRight);
    vec2 waterNormal = vec2(gradientX, gradientY);

    if (dot(waterNormal, waterNormal) < 0.0001)
    {
        return fallbackNormal;
    }

    waterNormal = normalize(waterNormal);
    vec2 targetNormal = currentMaterial == MATERIAL_WATER ? waterNormal : -waterNormal;
    return normalize(mix(fallbackNormal, targetNormal, 0.85));
}

float sampleRefractionIndex(vec2 localPos, int material)
{
    if (material == MATERIAL_AIR)
    {
        return 1.0;
    }

    LeafSample leaf = lookupLeaf(clamp(localPos, vec2(0.0), quadtreeSize - vec2(0.001)));
    return max(leaf.refractionIndex, 1.0);
}

float findLightTransmittance(vec2 pos, vec2 direction)
{
    const float WaterAbsorption = 0.04;

    vec2 rayDirection = normalize(direction);
    vec2 position = clamp(pos, vec2(0.001), regionSize - vec2(0.001));
    float totalDistance = 0.0;

    if (abs(rayDirection.x) < 0.0001 && abs(rayDirection.y) < 0.0001)
    {
        return 0.0;
    }

    int currentMaterial = sampleSurfaceMaterial(ivec2(floor(position)));
    if (currentMaterial == MATERIAL_SOLID)
    {
        return 0.0;
    }

    for (int i = 0; i < MAX_LIGHT_STEPS; i++)
    {
        float distance = traceDistance(position, rayDirection, currentMaterial);
        if (distance <= RAY_EPSILON)
        {
            vec2 nudgedPosition = position + rayDirection * RAY_EPSILON;
            if (!isInsideLocal(nudgedPosition))
            {
                break;
            }

            position = clamp(nudgedPosition, vec2(0.001), regionSize - vec2(0.001));
            currentMaterial = sampleSurfaceMaterial(ivec2(floor(position)));
            if (currentMaterial == MATERIAL_SOLID)
            {
                return 0.0;
            }

            continue;
        }

        if (currentMaterial == MATERIAL_WATER)
        {
            totalDistance += distance;
        }

        vec2 boundaryPosition = position + rayDirection * distance;
        vec2 nextPosition = boundaryPosition + rayDirection * RAY_EPSILON;
        if (!isInsideLocal(nextPosition))
        {
            break;
        }

        ivec2 currentCell = ivec2(floor(clamp(position, vec2(0.0), regionSize - vec2(0.001))));
        ivec2 nextCell = ivec2(floor(clamp(nextPosition, vec2(0.0), regionSize - vec2(0.001))));
        int nextMaterial = sampleSurfaceMaterial(nextCell);

        if (nextMaterial == MATERIAL_SOLID)
        {
            return 0.0;
        }

        if (nextMaterial == currentMaterial)
        {
            position = clamp(nextPosition, vec2(0.001), regionSize - vec2(0.001));
            continue;
        }

        vec2 fallbackNormal = computeInterfaceNormal(currentCell, nextCell);
        vec2 normal = computeSmoothedInterfaceNormal(boundaryPosition, currentMaterial, fallbackNormal);
        float currentRefractionIndex = sampleRefractionIndex(boundaryPosition - rayDirection * RAY_EPSILON, currentMaterial);
        float nextRefractionIndex = sampleRefractionIndex(nextPosition, nextMaterial);
        float eta = currentRefractionIndex / max(nextRefractionIndex, 0.0001);
        vec2 refractedDirection = refract(rayDirection, normal, eta);

        if (length(refractedDirection) < 0.0001)
        {
            rayDirection = normalize(reflect(rayDirection, normal));
            position = clamp(boundaryPosition + rayDirection * RAY_EPSILON, vec2(0.001), regionSize - vec2(0.001));
            continue;
        }

        rayDirection = normalize(refractedDirection);
        position = clamp(boundaryPosition + rayDirection * RAY_EPSILON, vec2(0.001), regionSize - vec2(0.001));
        currentMaterial = nextMaterial;
    }

    vec2 sunlightDirection = normalize(vec2(0.0, -1.0));
    float angleCosine = max(dot(rayDirection, sunlightDirection), 0.0);
    float mediumTransmittance = exp(-totalDistance * WaterAbsorption);
    return mediumTransmittance * angleCosine;
}

vec2 rotateVec2(vec2 value, float angle)
{
    float sinAngle = sin(angle);
    float cosAngle = cos(angle);
    return vec2(
        value.x * cosAngle - value.y * sinAngle,
        value.x * sinAngle + value.y * cosAngle);
}

float hash11(float value)
{
    return fract(sin(value * 127.1) * 43758.5453123);
}

float noise1D(float value)
{
    float baseValue = floor(value);
    float fraction = fract(value);
    float smoothFraction = fraction * fraction * (3.0 - 2.0 * fraction);
    float left = hash11(baseValue);
    float right = hash11(baseValue + 1.0);
    return mix(left, right, smoothFraction);
}

float computeRaySway(int rayIndex, vec2 fragmentGlobalPos)
{
    const float SunRaySwayAmplitude = 0.16;
    const float SunRaySwaySpeed = 256.0;
    float positionPhase = dot(fragmentGlobalPos, vec2(0.75487766, 0.56984029)) * 6.2831853;
    float seededTime = time * SunRaySwaySpeed + float(rayIndex) * 17.0 + positionPhase;
    float swayNoise = noise1D(seededTime) * 2.0 - 1.0;
    return swayNoise * SunRaySwayAmplitude;
}

vec2 getSunRayDirection()
{
    vec2 localSunDirection = vec2(-sunDirection.x, sunDirection.y);
    if (dot(localSunDirection, localSunDirection) < 0.0001)
    {
        return vec2(0.0, -1.0);
    }

    return normalize(localSunDirection);
}

void main()
{
    vec2 localPos = currentFragLocalPos();
    vec2 globalPos = currentFragGlobalPos();
    vec4 surfaceSample = sampleSurface(localPos);
    vec3 baseColor = surfaceSample.rgb;
    int surfaceMaterial = int(round(surfaceSample.a * 255.0));

    if (surfaceMaterial != MATERIAL_WATER)
    {
        finalColor = vec4(baseColor, 1.0) * fragColor;
        return;
    }

    vec2 sunRayDirection = getSunRayDirection();
    float totalSunlight = 0.0;
    const int MaxSunSampleCount = 64;
    const float SunAngularSpread = 0.45;
    int activeSunRayCount = clamp(sunRayCount, 1, MaxSunSampleCount);

    // Optimize denominator calculation outside the loop
    float tDenominator = activeSunRayCount == 1 ? 1.0 : float(activeSunRayCount - 1);

    for (int i = 0; i < MaxSunSampleCount; i++) {
        if (i >= activeSunRayCount)
        {
            break;
        }

        float t = activeSunRayCount == 1 ? 0.5 : float(i) / tDenominator;
    float angleOffset = mix(-SunAngularSpread, SunAngularSpread, t) + computeRaySway(i, globalPos);
        vec2 rayDir = rotateVec2(sunRayDirection, angleOffset);

        // This handles exactly how much light makes it to 'localPos'
        totalSunlight += findLightTransmittance(localPos, rayDir);
    }
    // Average the sunlight
    totalSunlight = totalSunlight / float(activeSunRayCount);

    // Black is simply lack of light
    vec3 finalWaterColor = baseColor * totalSunlight;

    float surfaceHighlight = smoothstep(0.95, 1.0, totalSunlight);
    finalWaterColor = mix(finalWaterColor, vec3(1.0), surfaceHighlight * 0.75);

    finalColor = vec4(clamp(finalWaterColor, 0.0, 1.0), 1.0) * fragColor;
}
