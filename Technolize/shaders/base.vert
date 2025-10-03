#version 330

// Input vertex attributes from Raylib
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;

// Output values to the fragment shader
out vec2 fragTexCoord;
out vec4 fragColor;

// Uniforms matrices provided by Raylib
uniform mat4 mvp;

void main()
{
    // Pass the vertex attributes to the fragment shader
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;

    // Calculate the final vertex position on screen
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
