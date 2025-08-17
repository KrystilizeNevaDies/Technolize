#version 330

// Input from vertex shader
in vec2 fragTexCoord;
in vec4 fragColor;

// Output color for the current pixel
out vec4 finalColor;

// Uniforms from C#
uniform vec2 resolution; // The size of the screen (e.g., 800.0, 480.0)
uniform float time;      // The running time for animations

// A simple function to draw a rectangle.
// It returns 1.0 if the coordinate 'uv' is inside the rectangle, and 0.0 otherwise.
float drawRect(vec2 uv, vec2 pos, vec2 size) {
    vec2 halfSize = size * 0.5;
    // Check if the uv is within the horizontal and vertical bounds
    float horz = step(pos.x - halfSize.x, uv.x) - step(pos.x + halfSize.x, uv.x);
    float vert = step(pos.y - halfSize.y, uv.y) - step(pos.y + halfSize.y, uv.y);
    return horz * vert; // Will be 1.0 only if inside both bounds
}


void main()
{
    // 1. Normalize and center coordinates
    // We convert the pixel coordinates from (0, 1) to a centered coordinate system
    // that is corrected for the screen's aspect ratio.
    // This makes (0,0) the center of the screen.
    vec2 uv = (2.0 * gl_FragCoord.xy - resolution.xy) / resolution.y;

    // 2. Define colors
    vec3 backgroundColor = vec3(0.1, 0.1, 0.2); // Dark blue
    vec3 boxColor1 = vec3(0.9, 0.4, 0.2);       // Orange
    vec3 boxColor2 = vec3(0.3, 0.8, 0.5);       // Green

    // 3. Define the scene
    // Start with the background color.
    vec3 finalPixel = backgroundColor;

    // --- Draw a static box ---
    vec2 box1_pos = vec2(-0.7, 0.0);
    vec2 box1_size = vec2(0.5, 0.5);
    float box1_mask = drawRect(uv, box1_pos, box1_size);

    // The mix() function blends between two values.
    // If box1_mask is 1.0, we get boxColor1. If it's 0.0, we get finalPixel.
    finalPixel = mix(finalPixel, boxColor1, box1_mask);

    // --- Draw an animated box ---
    // Make the box move up and down using a sine wave based on time.
    vec2 box2_pos = vec2(0.7, sin(time * 2.0) * 0.5);
    vec2 box2_size = vec2(0.4, 0.4);
    float box2_mask = drawRect(uv, box2_pos, box2_size);

    finalPixel = mix(finalPixel, boxColor2, box2_mask);


    // 4. Set the final output color
    finalColor = vec4(finalPixel, 1.0);
}
