#version 330

// Minimal "clean slate" world fragment shader for the r2 renderer. It samples the dense colour texture
// and multiplies it by the CPU-computed lighting texture (the shared lighting buffer the sun/other light
// rays deposit into). No GPU lighting, shadows, water effects, or quadtree traversal happen here - all
// light transport is done on the CPU before the frame. Pairs with world_renderer.screen.vert (camera
// path) and world_renderer.vert (native path), which both provide fragTexCoord in [0, 1] and an
// opaque-white fragColor.
//
// Lighting sampling. The light buffer is computed on a grid that may be finer than the world (the
// renderer's resolution multiplier) but is still discrete, so sampling it naively looks blocky. Options
// considered for smoothing it:
//   * Nearest (texelFetch): sharp, fastest, but shows every light cell as a hard square.
//   * Bilinear (hardware Linear filter): cheap, removes the worst blockiness but leaves diamond/grid
//     artifacts when magnified a lot.
//   * Bicubic B-spline (this shader): a 4-tap filter built on the hardware bilinear sampler that gives
//     smooth C2-continuous gradients with no grid artifacts - the best smoothness/coverage trade-off
//     for a magnified light field. (Catmull-Rom would keep more contrast but can overshoot/ring;
//     B-spline is chosen for the soft, continuous light look.)
// The block colour itself stays nearest-sampled so tiles keep their crisp edges.

in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform sampler2D texture0;   // RGBA8: rgb = block colour, a = material code (ignored here).
uniform sampler2D lightTex;   // RGBA8 (Linear-filtered): rgb = accumulated light (ambient + all sources).
uniform vec2 regionSize;      // World colour texture size in cells.

vec4 sampleTexel(sampler2D tex, vec2 localPos)
{
    ivec2 size = textureSize(tex, 0);
    ivec2 texelCoord = ivec2(clamp(floor(localPos), vec2(0.0), vec2(size) - vec2(1.0)));
    return texelFetch(tex, texelCoord, 0);
}

// Cubic B-spline weights for the four taps around a fractional offset.
vec4 cubicWeights(float f)
{
    float f2 = f * f;
    float f3 = f2 * f;
    float w0 = (-f3 + 3.0 * f2 - 3.0 * f + 1.0) / 6.0;
    float w1 = (3.0 * f3 - 6.0 * f2 + 4.0) / 6.0;
    float w2 = (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0) / 6.0;
    float w3 = f3 / 6.0;
    return vec4(w0, w1, w2, w3);
}

// Smooth bicubic B-spline sample using 4 bilinear taps from the Linear-filtered light texture
// (canonical fast-bicubic: each pair of cubic taps is folded into one hardware bilinear fetch).
vec3 sampleLightBicubic(vec2 uv)
{
    vec2 texSize = vec2(textureSize(lightTex, 0));
    vec2 invTexSize = 1.0 / texSize;

    uv = uv * texSize - 0.5;
    vec2 fxy = fract(uv);
    uv -= fxy;

    vec4 xcubic = cubicWeights(fxy.x);
    vec4 ycubic = cubicWeights(fxy.y);

    // Note: swizzling a constructor result directly (e.g. vec2(...).xyxy) is rejected by some GLSL 330
    // compilers, so the offset basis is built from named vectors instead.
    vec2 tapBias = vec2(-0.5, 1.5);
    vec4 c = uv.xxyy + vec4(tapBias.x, tapBias.y, tapBias.x, tapBias.y);
    vec4 s = vec4(xcubic.x + xcubic.y, xcubic.z + xcubic.w, ycubic.x + ycubic.y, ycubic.z + ycubic.w);
    vec4 offset = c + vec4(xcubic.y, xcubic.w, ycubic.y, ycubic.w) / s;
    offset *= vec4(invTexSize.x, invTexSize.x, invTexSize.y, invTexSize.y);

    vec3 sample0 = texture(lightTex, offset.xz).rgb;
    vec3 sample1 = texture(lightTex, offset.yz).rgb;
    vec3 sample2 = texture(lightTex, offset.xw).rgb;
    vec3 sample3 = texture(lightTex, offset.yw).rgb;

    float sx = s.x / (s.x + s.y);
    float sy = s.z / (s.z + s.w);

    return mix(mix(sample3, sample2, sx), mix(sample1, sample0, sx), sy);
}

void main()
{
    vec2 localPos = clamp(fragTexCoord * regionSize, vec2(0.5), regionSize - vec2(0.5));
    vec3 baseColor = sampleTexel(texture0, localPos).rgb;
    vec3 light = sampleLightBicubic(fragTexCoord);
    finalColor = vec4(baseColor * light, 1.0) * fragColor;
}
