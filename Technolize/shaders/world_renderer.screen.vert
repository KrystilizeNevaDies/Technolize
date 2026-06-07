#version 330

// Camera-projected vertex shader for the on-screen world shader pass. It applies the camera transform
// from scalar uniforms directly, avoiding any matrix-convention pitfalls:
//   worldPos = destOrigin + uv * destSize           (source UV -> dest rectangle)
//   screen   = (worldPos - cameraTarget) * zoom + cameraOffset   (2D camera, rotation 0)
//   ndc      = (screen / (viewport * 0.5)) with Y flipped
// The fragment shader works in local texture space (fragTexCoord), so it is unaffected by the camera.

layout(location = 0) in vec2 aPos;   // unused in the camera path (kept for shared GlFullScreenQuad VBO)
layout(location = 1) in vec2 aUv;    // texture coordinate in [0, 1], top-left origin

uniform vec2 destOrigin;     // world-space pixel position of UV (0, 0)
uniform vec2 destSize;       // world-space pixel size the texture is stretched across
uniform vec2 cameraTarget;
uniform vec2 cameraOffset;
uniform float cameraZoom;
uniform vec2 viewportSize;   // framebuffer size in pixels

out vec2 fragTexCoord;
out vec4 fragColor;

void main()
{
    fragTexCoord = aUv;
    fragColor = vec4(1.0); // The world texture is drawn opaque white.

    vec2 worldPos = destOrigin + aUv * destSize;
    vec2 screen = (worldPos - cameraTarget) * cameraZoom + cameraOffset;
    vec2 ndc = vec2(
        screen.x / (viewportSize.x * 0.5) - 1.0,
        1.0 - screen.y / (viewportSize.y * 0.5));

    gl_Position = vec4(ndc, 0.0, 1.0);
}
