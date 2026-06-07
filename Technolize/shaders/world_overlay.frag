#version 330

// Fragment shader for world-space coloured overlay geometry. Emits the interpolated per-vertex colour
// directly; alpha blending (configured by the renderer) composites it over the world pass.

in vec4 vColor;
out vec4 finalColor;

void main()
{
    finalColor = vColor;
}
