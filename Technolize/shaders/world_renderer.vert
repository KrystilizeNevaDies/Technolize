#version 330

// Vertex shader for the full-screen-quad world shader pass. The quad is supplied directly in
// normalized device coordinates by GlFullScreenQuad, so no model/view/projection transform is needed:
// the fragment shader works purely in local texture space derived from fragTexCoord.

layout(location = 0) in vec2 aPos;   // NDC position in [-1, 1]
layout(location = 1) in vec2 aUv;    // texture coordinate in [0, 1], top-left origin

out vec2 fragTexCoord;
out vec4 fragColor;

void main()
{
    fragTexCoord = aUv;
    // The world texture is drawn opaque white; the world shader multiplies finalColor by fragColor,
    // so supply opaque white here.
    fragColor = vec4(1.0);
    gl_Position = vec4(aPos, 0.0, 1.0);
}
