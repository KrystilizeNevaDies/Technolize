#version 330

// Camera-projected vertex shader for world-space coloured overlay geometry (grid lines, region
// overlays). Each vertex carries a world-pixel position and an RGBA colour. The same scalar-uniform
// camera transform as world_renderer.screen.vert is applied so the overlay aligns exactly with
// the projected world quad.

layout(location = 0) in vec2 aWorldPos;
layout(location = 1) in vec4 aColor;

uniform vec2 cameraTarget;
uniform vec2 cameraOffset;
uniform float cameraZoom;
uniform vec2 viewportSize;

out vec4 vColor;

void main()
{
    vColor = aColor;

    vec2 screen = (aWorldPos - cameraTarget) * cameraZoom + cameraOffset;
    vec2 ndc = vec2(
        screen.x / (viewportSize.x * 0.5) - 1.0,
        1.0 - screen.y / (viewportSize.y * 0.5));

    gl_Position = vec4(ndc, 0.0, 1.0);
}
