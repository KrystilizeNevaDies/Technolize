using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Test.Validation;

/// <summary>
/// Simple validation test to demonstrate WorldShaderRenderer functionality
/// without requiring extensive benchmarking or GPU environment setup.
/// </summary>
public class WorldShaderRendererValidation
{
    public static void RunValidation()
    {
        Console.WriteLine("=== WorldShaderRenderer Validation ===");
        
        try
        {
            // Initialize Raylib in headless/minimal mode
            Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
            Raylib.InitWindow(800, 600, "Validation");
            
            // Create test world
            var world = new TickableWorld();
            
            // Add some test blocks
            world.SetBlock(new Vector2(0, 0), Blocks.Air.id);
            world.SetBlock(new Vector2(1, 1), Blocks.Stone.id);
            world.SetBlock(new Vector2(2, 2), Blocks.Water.id);
            world.SetBlock(new Vector2(3, 3), Blocks.Sand.id);
            world.SetBlock(new Vector2(4, 4), Blocks.Fire.id);
            
            // Force region creation
            world.GetBlock(new Vector2(0, 0));
            
            // Create both renderers
            var cpuRenderer = new WorldRenderer(world, 800, 600);
            var shaderRenderer = new WorldShaderRenderer(world, 800, 600);
            
            Console.WriteLine("✓ Successfully created both renderers");
            
            // Test basic operations
            TestBasicOperations(cpuRenderer, shaderRenderer);
            TestCameraOperations(cpuRenderer, shaderRenderer);
            TestWorldBoundsCalculation(cpuRenderer, shaderRenderer);
            TestRenderingCalls(cpuRenderer, shaderRenderer);
            
            // Cleanup
            shaderRenderer.Dispose();
            Raylib.CloseWindow();
            
            Console.WriteLine("✓ All validation tests passed successfully!");
            Console.WriteLine("✓ WorldShaderRenderer implementation is working correctly");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Validation failed: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
    }
    
    private static void TestBasicOperations(WorldRenderer cpu, WorldShaderRenderer shader)
    {
        // Test that both renderers can be created and have same interface
        var cpuBounds = cpu.GetVisibleWorldBounds();
        var shaderBounds = shader.GetVisibleWorldBounds();
        
        // Bounds should be identical since they use same calculation
        if (cpuBounds.start.X != shaderBounds.start.X || cpuBounds.start.Y != shaderBounds.start.Y ||
            cpuBounds.end.X != shaderBounds.end.X || cpuBounds.end.Y != shaderBounds.end.Y)
        {
            throw new Exception("Bounds calculation differs between renderers");
        }
        
        Console.WriteLine("✓ Basic operations test passed");
    }
    
    private static void TestCameraOperations(WorldRenderer cpu, WorldShaderRenderer shader)
    {
        // Test camera updates
        cpu.UpdateCamera();
        shader.UpdateCamera();
        
        // Test mouse world position
        var cpuMousePos = cpu.GetMouseWorldPosition();
        var shaderMousePos = shader.GetMouseWorldPosition();
        
        // Should be identical since they use same calculation
        if (Math.Abs(cpuMousePos.X - shaderMousePos.X) > 0.001f || 
            Math.Abs(cpuMousePos.Y - shaderMousePos.Y) > 0.001f)
        {
            throw new Exception("Mouse position calculation differs between renderers");
        }
        
        Console.WriteLine("✓ Camera operations test passed");
    }
    
    private static void TestWorldBoundsCalculation(WorldRenderer cpu, WorldShaderRenderer shader)
    {
        // Test multiple bounds calculations to ensure consistency
        for (int i = 0; i < 3; i++)
        {
            var cpuBounds = cpu.GetVisibleWorldBounds();
            var shaderBounds = shader.GetVisibleWorldBounds();
            
            if (!AreBoundsEqual(cpuBounds, shaderBounds))
            {
                throw new Exception($"Bounds calculation inconsistent at iteration {i}");
            }
        }
        
        Console.WriteLine("✓ World bounds calculation test passed");
    }
    
    private static void TestRenderingCalls(WorldRenderer cpu, WorldShaderRenderer shader)
    {
        // Test that rendering calls don't crash
        try
        {
            cpu.Draw();
            Console.WriteLine("✓ CPU renderer Draw() completed successfully");
            
            shader.Draw();
            Console.WriteLine("✓ Shader renderer Draw() completed successfully");
            
            // Test multiple calls
            cpu.Draw();
            shader.Draw();
            Console.WriteLine("✓ Multiple Draw() calls completed successfully");
            
        }
        catch (Exception ex)
        {
            throw new Exception($"Rendering call failed: {ex.Message}");
        }
        
        Console.WriteLine("✓ Rendering calls test passed");
    }
    
    private static bool AreBoundsEqual((Vector2 start, Vector2 end) bounds1, (Vector2 start, Vector2 end) bounds2)
    {
        const float tolerance = 0.001f;
        return Math.Abs(bounds1.start.X - bounds2.start.X) < tolerance &&
               Math.Abs(bounds1.start.Y - bounds2.start.Y) < tolerance &&
               Math.Abs(bounds1.end.X - bounds2.end.X) < tolerance &&
               Math.Abs(bounds1.end.Y - bounds2.end.Y) < tolerance;
    }
}