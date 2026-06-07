#version 430

in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform sampler2D texture0;
uniform vec2 regionSize;
uniform vec2 regionOrigin;
uniform vec2 quadtreeSize;
uniform vec2 quadtreeOrigin;
uniform float time;
uniform vec2 sunDirection;
uniform int sunRayCount;

// Lighting model: an unbounded HDR accumulator (ambient + each light's coloured, shadowed
// contribution), tone-clamped only at output. Shadows are HARD: solids fully occlude via a
// sphere-trace of the solid distance field (solidSdf); water attenuates (Beer-Lambert) but does not
// hard-block. sunRayCount is retained for compatibility but no longer affects the (hard-shadow) output.
const int MAX_LIGHTS = 16;

uniform sampler2D solidSdf;     // R8: floored Euclidean distance (cells) to the nearest solid cell.
uniform vec3 sunColor;          // Directional sun colour (linear-ish, may exceed 1 with intensity).
uniform float sunIntensity;     // Sun strength multiplier.
uniform vec3 ambientColor;      // Unbounded ambient floor added before any light.
uniform int lightCount;         // Active point lights in lightData/lightColor.
uniform vec4 lightData[MAX_LIGHTS];  // xy = position (local cell space), z = radius (cells), w = intensity.
uniform vec4 lightColor[MAX_LIGHTS]; // rgb = colour (a unused).

// The world quadtree, uploaded verbatim as a flat array. Each node is resolved CPU-side to its
// material/refraction, so the shader indexes it directly with no bit-unpacking or texture wrapping.
struct GpuQuadtreeNode
{
    int firstChild;
    int material;
    float refractionIndex;
};

layout(std430, binding = 0) readonly buffer QuadtreeBuffer
{
    GpuQuadtreeNode quadtreeNodes[];
};

const int MATERIAL_AIR = 0;
const int MATERIAL_WATER = 1;
const int MATERIAL_SOLID = 2;
const int MATERIAL_INTERNAL = 255;

// The world quadtree covers the entire world ([0, WorldSize), WorldSize = 2^20), so a leaf can be at
// depth 20. Allow one extra level of headroom.
const int QUADTREE_MAX_DEPTH = 21;
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

vec2 getSunRayDirection();

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

// Material fetch for a texel that the caller has already proven to be in bounds. Skips the
// textureSize query and clamp that sampleSurfaceMaterial(ivec2) performs, which is bit-identical for
// an in-bounds coordinate but removes that work from the per-cell DDA hot loop.
int sampleSurfaceMaterialInBounds(ivec2 texelCoord)
{
    return int(round(texelFetch(texture0, texelCoord, 0).a * 255.0));
}

int sampleLightMaterial(vec2 localPos)
{
    // The light march only samples positions it has already clamped into [0.001, regionSize-0.001],
    // so floor(localPos) is always in bounds and the textureSize/clamp in sampleSurfaceMaterial is
    // redundant here. Fetching directly is bit-identical and lighter in the light loop.
    return int(round(texelFetch(texture0, ivec2(floor(localPos)), 0).a * 255.0));
}

float sampleSurfaceReflectance(vec2 localPos)
{
    vec3 baseColor = sampleSurface(localPos).rgb;
    return clamp((baseColor.r + baseColor.g + baseColor.b) / 3.0, 0.0, 1.0);
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
    GpuQuadtreeNode node = quadtreeNodes[nodeIndex];
    // Internal nodes carry material 255 (MATERIAL_INTERNAL); leaves carry their resolved material.
    // Refraction is clamped to >= 1.0 exactly as the prior packed-texture decode did.
    float refractionIndex = max(node.refractionIndex, 1.0);
    return NodeData(node.firstChild, node.material, refractionIndex);
}

LeafSample lookupLeafTree(vec2 treePos)
{
    // Descend the world quadtree (in global tree coordinates) to the leaf covering treePos, returning
    // its material/refraction and its tree-space bounds.
    vec2 clampedPos = clamp(treePos, vec2(0.0), quadtreeSize - vec2(0.001));
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

// Maps a local draw-space position into global tree coordinates. X is a straight offset; Y is flipped
// (the local top row is the highest world row), matching currentFragGlobalPos plus the tree's
// WorldOffset baked into quadtreeOrigin.
vec2 localToTree(vec2 localPos)
{
    return quadtreeOrigin + vec2(localPos.x, -localPos.y);
}

LeafSample lookupLeaf(vec2 localPos)
{
    return lookupLeafTree(localToTree(localPos));
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

float traceBinaryDistance(vec2 origin, vec2 direction, int material)
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

    // Hoist the integer region bounds out of the per-cell loop (bit-identical to int(regionSize.*)).
    ivec2 regionBound = ivec2(regionSize);

    for (int step = 0; step < MAX_RAY_STEPS; step++)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= regionBound.x || cell.y >= regionBound.y)
        {
            return depth;
        }

        if (sampleSurfaceMaterialInBounds(cell) != material)
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

// Measures how far a ray travels through a contiguous run of `material` by jumping whole uniform
// quadtree leaves, crossing leaf boundaries with a PATH STACK instead of re-descending from the root.
// The stack holds the node-index chain from root to the current leaf; on a boundary crossing we pop
// only the levels the ray actually left, then descend from there. Crossings between nearby leaves
// therefore touch a couple of nodes (O(1) amortized) rather than ~depth levels, which is what makes a
// quadtree light-march competitive: large uniform bodies jump in one step, and fragmented areas still
// pay only the small ascent/descent delta per boundary.
//
// All coordinates here are REGION-RELATIVE (absolute tree coord minus quadtreeOrigin). This is
// essential: absolute tree coords are ~2^19, where float32 has no sub-cell precision, so boundary
// distances and the RAY_EPSILON nudge would be destroyed. Relative coords keep marched positions
// small and precise, while ancestor box corners stay exact integers (<= 2^20, exact in float32).
float traceAscentDistance(vec2 originLocal, vec2 directionLocal, int material)
{
    vec2 dirLocal = normalize(directionLocal);
    if (abs(dirLocal.x) < 0.0001 && abs(dirLocal.y) < 0.0001)
    {
        return 0.0;
    }

    // Region-relative tree space: rel = absoluteTree - quadtreeOrigin. The local->tree map is a Y
    // reflection plus a translation, so localToTree(p) - quadtreeOrigin = (p.x, -p.y); distances are
    // preserved (isometry), so depth accumulated here is valid in local/world units.
    vec2 pos = vec2(originLocal.x, -originLocal.y);
    vec2 dir = vec2(dirLocal.x, -dirLocal.y);
    vec2 rootMin = -quadtreeOrigin;

    int stackNode[QUADTREE_MAX_DEPTH + 1];
    vec2 stackMin[QUADTREE_MAX_DEPTH + 1];

    // Initial descent from the root to the leaf containing the origin, recording the path.
    int sp = 0;
    stackNode[0] = 0;
    stackMin[0] = rootMin;
    NodeData node = loadNode(0);
    for (int d = 0; d < QUADTREE_MAX_DEPTH; d++)
    {
        if (node.material != MATERIAL_INTERNAL) break;
        float childSize = quadtreeSize.x * exp2(-float(sp + 1));
        vec2 center = stackMin[sp] + vec2(childSize);
        vec2 childMin = stackMin[sp];
        int childOffset = 0;
        if (pos.x >= center.x) { childOffset += 1; childMin.x = center.x; }
        if (pos.y >= center.y) { childOffset += 2; childMin.y = center.y; }
        sp++;
        stackNode[sp] = node.firstChild + childOffset;
        stackMin[sp] = childMin;
        node = loadNode(stackNode[sp]);
    }

    float depth = 0.0;
    for (int step = 0; step < MAX_RAY_STEPS; step++)
    {
        if (node.material != material)
        {
            return depth;
        }

        float leafSize = quadtreeSize.x * exp2(-float(sp));
        vec2 boundaryNormal;
        LeafSample leaf = LeafSample(node.material, node.refractionIndex, stackMin[sp], stackMin[sp] + vec2(leafSize));
        float dist = computeBoundaryDistance(pos, dir, leaf, boundaryNormal);
        depth += dist;
        pos += dir * (dist + RAY_EPSILON);

        if (pos.x < rootMin.x || pos.y < rootMin.y
            || pos.x >= rootMin.x + quadtreeSize.x || pos.y >= rootMin.y + quadtreeSize.y)
        {
            return depth;
        }

        // Ascend: pop until the top node's box contains the new position (the root always does).
        for (int a = 0; a < QUADTREE_MAX_DEPTH; a++)
        {
            if (sp == 0) break;
            float sz = quadtreeSize.x * exp2(-float(sp));
            vec2 mn = stackMin[sp];
            if (pos.x >= mn.x && pos.x < mn.x + sz && pos.y >= mn.y && pos.y < mn.y + sz) break;
            sp--;
        }

        // Descend from the deepest containing ancestor to the leaf that now holds the position.
        node = loadNode(stackNode[sp]);
        for (int d = 0; d < QUADTREE_MAX_DEPTH; d++)
        {
            if (node.material != MATERIAL_INTERNAL) break;
            float childSize = quadtreeSize.x * exp2(-float(sp + 1));
            vec2 center = stackMin[sp] + vec2(childSize);
            vec2 childMin = stackMin[sp];
            int childOffset = 0;
            if (pos.x >= center.x) { childOffset += 1; childMin.x = center.x; }
            if (pos.y >= center.y) { childOffset += 2; childMin.y = center.y; }
            sp++;
            stackNode[sp] = node.firstChild + childOffset;
            stackMin[sp] = childMin;
            node = loadNode(stackNode[sp]);
        }
    }

    return depth;
}

// Measures how far a ray travels through a contiguous run of `material` by stepping the dense surface
// texture cell by cell (DDA).
//
// NOTE: the quadtree leaf-jumping march (traceAscentDistance, kept below) was measured to be ~7x
// SLOWER than this dense DDA on the real (fragmented) world: pillars and air pockets make the tree
// mostly small leaves, so the per-jump ascent/descent node loads dwarf a single texture fetch per
// cell. It only wins for large uniform bodies. The dense DDA is the faster and stable path here even
// with the now-shallow region-local quadtree window.
float traceDistance(vec2 origin, vec2 direction, int material)
{
    return traceBinaryDistance(origin, direction, material);
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

float sampleRefractionIndex(vec2 localPos, int material)
{
    if (material == MATERIAL_AIR)
    {
        return 1.0;
    }

    // lookupLeaf maps the local position into the world tree's coordinate space itself.
    LeafSample leaf = lookupLeaf(localPos);
    return max(leaf.refractionIndex, 1.0);
}

float getMaterialAbsorption(int material)
{
    if (material == MATERIAL_WATER)
    {
        return 0.02;
    }

    if (material == MATERIAL_SOLID)
    {
        return 0.1;
    }

    return 0.0;
}

float findLightTransmittance(vec2 pos, vec2 direction)
{
    vec2 rayDirection = normalize(direction);
    vec2 position = clamp(pos, vec2(0.001), regionSize - vec2(0.001));
    float totalOpticalDepth = 0.0;
    float reflectedLight = 1.0;
    bool escaped = false;

    if (abs(rayDirection.x) < 0.0001 && abs(rayDirection.y) < 0.0001)
    {
        return 0.0;
    }

    int currentMaterial = sampleLightMaterial(position);

    for (int i = 0; i < MAX_LIGHT_STEPS; i++)
    {
        float distance = traceDistance(position, rayDirection, currentMaterial);
        if (distance <= RAY_EPSILON)
        {
            vec2 nudgedPosition = position + rayDirection * RAY_EPSILON;
            if (!isInsideLocal(nudgedPosition))
            {
                escaped = true;
                break;
            }

            position = clamp(nudgedPosition, vec2(0.001), regionSize - vec2(0.001));
            currentMaterial = sampleLightMaterial(position);
            continue;
        }

        if (currentMaterial != MATERIAL_AIR)
        {
            totalOpticalDepth += distance * getMaterialAbsorption(currentMaterial);
            if (totalOpticalDepth >= 6.0)
            {
                return 0.0;
            }
        }

        vec2 boundaryPosition = position + rayDirection * distance;
        vec2 nextPosition = boundaryPosition + rayDirection * RAY_EPSILON;
        if (!isInsideLocal(nextPosition))
        {
            escaped = true;
            break;
        }

        ivec2 currentCell = ivec2(floor(clamp(position, vec2(0.0), regionSize - vec2(0.001))));
        ivec2 nextCell = ivec2(floor(clamp(nextPosition, vec2(0.0), regionSize - vec2(0.001))));
        int nextMaterial = sampleLightMaterial(nextPosition);

        if (nextMaterial == currentMaterial)
        {
            position = clamp(nextPosition, vec2(0.001), regionSize - vec2(0.001));
            continue;
        }

        vec2 fallbackNormal = computeInterfaceNormal(currentCell, nextCell);
        vec2 normal = fallbackNormal;

        if (currentMaterial == MATERIAL_WATER && nextMaterial == MATERIAL_SOLID)
        {
            reflectedLight *= sampleSurfaceReflectance(nextPosition);
            if (reflectedLight <= 0.001)
            {
                return 0.0;
            }

            rayDirection = normalize(reflect(rayDirection, normal));
            position = clamp(boundaryPosition + rayDirection * RAY_EPSILON, vec2(0.001), regionSize - vec2(0.001));
            currentMaterial = sampleLightMaterial(position);
            continue;
        }

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

    if (!escaped)
    {
        return 0.0;
    }

    float mediumTransmittance = exp(-totalOpticalDepth);
    return mediumTransmittance * reflectedLight;
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
    const float SunRaySwaySpeed = 1.0;
    float positionPhase = hash11(fragmentGlobalPos.x * 12.9898 + fragmentGlobalPos.y * 78.233);
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

// Reads the floored solid-distance field (cells to nearest solid) at a cell.
int sampleSolidDistance(ivec2 cell)
{
    ivec2 sdfSize = textureSize(solidSdf, 0);
    ivec2 clamped = clamp(cell, ivec2(0), sdfSize - ivec2(1));
    return int(round(texelFetch(solidSdf, clamped, 0).r * 255.0));
}

// Hard-shadow visibility from `origin` toward a light along `dir`, for up to `maxDist` cells (use a
// large value for the directional sun, which only ends by leaving the region). Sphere-traces the solid
// distance field: empty/uniform space is crossed in one step sized by the distance to the nearest
// solid, so a clear sky path resolves in a few steps instead of hundreds of dense cells.
//
// Solids are HARD occluders (visibility 0). Water attenuates via Beer-Lambert along the path; because
// water is a uniform medium, accumulating absorption * stepLength over big steps is exact within a
// body. The originating surface cell is skipped so a lit solid face does not shadow itself.
float traceShadowSDF(vec2 origin, vec2 dir, float maxDist)
{
    if (dot(dir, dir) < 1e-8)
    {
        return 0.0;
    }

    dir = normalize(dir);
    ivec2 originCell = ivec2(floor(origin));
    vec2 position = origin;
    float traveled = 0.0;
    float opticalDepth = 0.0;

    for (int step = 0; step < MAX_LIGHT_STEPS; step++)
    {
        if (!isInsideLocal(position) || traveled >= maxDist)
        {
            // Left the region (reached the sky) or reached the point light: surviving light only the
            // medium it passed through dimmed.
            return exp(-opticalDepth);
        }

        ivec2 cell = ivec2(floor(position));
        int material = int(round(texelFetch(texture0, clamp(cell, ivec2(0), textureSize(texture0, 0) - ivec2(1)), 0).a * 255.0));

        if (material == MATERIAL_SOLID && cell != originCell)
        {
            return 0.0; // hard shadow
        }

        float distanceToSolid = float(sampleSolidDistance(cell));
        float advance = max(distanceToSolid, 1.0);
        advance = min(advance, maxDist - traveled);

        if (material == MATERIAL_WATER)
        {
            opticalDepth += advance * getMaterialAbsorption(MATERIAL_WATER);
            if (opticalDepth >= 8.0)
            {
                return 0.0;
            }
        }

        position += dir * advance;
        traveled += advance;
    }

    return exp(-opticalDepth);
}

void main()
{
    vec2 localPos = currentFragLocalPos();
    vec4 surfaceSample = sampleSurface(localPos);
    vec3 baseColor = surfaceSample.rgb;
    int surfaceMaterial = int(round(surfaceSample.a * 255.0));

    if (surfaceMaterial != MATERIAL_WATER && surfaceMaterial != MATERIAL_SOLID)
    {
        finalColor = vec4(baseColor, 1.0) * fragColor;
        return;
    }

    // Unbounded HDR light accumulator: ambient floor + each light's coloured, shadowed contribution.
    vec3 accumulatedLight = ambientColor;

    // Directional sun: a single hard-shadow ray (solids fully occlude, water attenuates).
    vec2 sunDir = getSunRayDirection();
    float sunVisibility = traceShadowSDF(localPos, sunDir, LARGE_DISTANCE);
    accumulatedLight += sunColor * sunIntensity * sunVisibility;

    // Point lights: one hard-shadow ray each, with smooth distance falloff inside the light's radius.
    for (int i = 0; i < lightCount && i < MAX_LIGHTS; i++)
    {
        vec2 toLight = lightData[i].xy - localPos;
        float radius = lightData[i].z;
        float intensity = lightData[i].w;
        float distance = length(toLight);

        if (distance >= radius)
        {
            continue;
        }

        float visibility = distance < 1e-4 ? 1.0 : traceShadowSDF(localPos, toLight / distance, distance);
        float falloff = 1.0 - (distance / radius);
        falloff *= falloff;
        accumulatedLight += lightColor[i].rgb * intensity * falloff * visibility;
    }

    vec3 litColor = baseColor * accumulatedLight;

    if (surfaceMaterial == MATERIAL_WATER)
    {
        // Bright sunlit water keeps a sun-tinted surface sparkle.
        float surfaceHighlight = smoothstep(0.9, 1.0, sunVisibility);
        litColor = mix(litColor, sunColor, surfaceHighlight * 0.4);
    }

    finalColor = vec4(clamp(litColor, 0.0, 1.0), 1.0) * fragColor;
}

