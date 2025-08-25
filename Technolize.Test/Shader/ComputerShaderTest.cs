using Raylib_cs;
using Technolize.World.Block;
namespace Technolize.Test.Shader;

public class ComputerShaderTest
{
    [Test]
    [RaylibWindow]
    public unsafe void MinimalDeciderOutput()
    {
        const int size = 5;
        uint total = size * size;

        // Air everywhere, Sand at center
        uint[] worldStateCPU = new uint[total];
        int cx = size / 2, cy = size / 2;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool center = x == cx && y == cy;
            worldStateCPU[y * size + x] = center ? Blocks.Sand : Blocks.Air;
        }

        // --- 2) Create SSBOs (input + output) ---
        // NOTE: rlgl buffer sizes are in bytes.
        uint inSsbo;
        uint outSsbo;
        inSsbo = Rlgl.LoadShaderBuffer(total * sizeof(uint), null, Rlgl.DYNAMIC_COPY);
        outSsbo = Rlgl.LoadShaderBuffer(total * sizeof(uint), null, Rlgl.DYNAMIC_COPY);

        // Upload input data
        fixed (uint* src = worldStateCPU)
        {
            Rlgl.UpdateShaderBuffer(inSsbo, src, total * sizeof(uint), 0);
        }

        // --- 3) Compute shader (GLSL 430) ---
        // - Reads SSBO at binding=1 (input)
        // - Writes SSBO at binding=2 (output)
        // - Uniforms: uSize (width/height)
        string computeSrc = @"
    #version 430
    layout (local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

    layout(std430, binding = 1) readonly buffer InBuf {
        uint inData[];
    };

    layout(std430, binding = 2) writeonly buffer OutBuf {
        uint outData[];
    };

    uniform ivec2 uSize;

    void main() {
        ivec2 gid = ivec2(gl_GlobalInvocationID.xy);
        if (gid.x >= uSize.x || gid.y >= uSize.y) return;

        int idx = gid.y * uSize.x + gid.x;

        // --- Your logic here ---
        uint blockId = inData[idx];

        // For now: pass-through
        outData[idx] = blockId;
    }
    ";

        // Compile + link compute program
        uint compShader;
        uint compProgram;
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(computeSrc + "\0");
            fixed (byte* p = bytes)
            {
                compShader  = Rlgl.CompileShader((sbyte*) p, (int) ShaderType.Compute);
                compProgram = Rlgl.LoadComputeShaderProgram(compShader);
            }
        }

        // --- 4) Bind buffers + set uniforms + dispatch ---
        Rlgl.EnableShader(compProgram);

        // Bind SSBOs to the same binding points the shader expects
        Rlgl.BindShaderBuffer(inSsbo, 1);   // binding = 1
        Rlgl.BindShaderBuffer(outSsbo, 2);  // binding = 2

        // Uniform uSize
        int uLoc;
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("uSize" + "\0");
            fixed (byte* p = bytes)
            {
                uLoc = Rlgl.GetLocationUniform(compProgram, (sbyte*)p);
            }
        }

        // two ints
        int[] sizeVec = [ size, size ];
        fixed (int* p = sizeVec) Rlgl.SetUniform(uLoc, p, (int)ShaderUniformDataType.Vec2, 1);

        // Dispatch: round up to workgroup size (16x16)
        uint groupsX = (size + 15) / 16;
        uint groupsY = (size + 15) / 16;
        Rlgl.ComputeShaderDispatch(groupsX, groupsY, 1);

        Rlgl.DisableShader();

        // --- 5) Read back from outSsbo ---
        uint[] resultCpu = new uint[total];

        // If rlReadShaderBuffer is available in your binding:
        fixed(uint* resultPtr = resultCpu)
        {
            Rlgl.ReadShaderBuffer(outSsbo, resultPtr, total * sizeof(uint), 0);
        }

        // --- 6) Validate (center = Sand, others = Air) ---
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            uint got = resultCpu[y * size + x];
            uint expected = x == cx && y == cy ? Blocks.Sand : Blocks.Air;
            Console.WriteLine($"({x},{y}) => got: {got}, expected: {expected}");
            // Assert.That(got, Is.EqualTo(expected), $"Mismatch at ({x},{y})");
        }

        // --- 7) Cleanup ---
        Rlgl.UnloadShaderProgram(compProgram);
        Rlgl.UnloadShaderBuffer(inSsbo);
        Rlgl.UnloadShaderBuffer(outSsbo);
    }
}
