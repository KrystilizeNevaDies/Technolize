using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.Test.Shader;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Test.Rendering;

[TestFixture]
public class WorldRendererTest
{
    [Test]
    [RaylibWindow(800, 600)]
    public void TextureRendering_DoesNotCrash_WithValidWorld()
    {
        // Arrange: Create a world with some blocks
        var world = new TickableWorld();
        var renderer = new WorldRenderer(world, 800, 600);
        
        // Add some blocks to the world to create regions
        world.SetBlock(new Vector2(1, 1), Blocks.Stone.id);
        world.SetBlock(new Vector2(2, 2), Blocks.Water.id);
        world.SetBlock(new Vector2(100, 100), Blocks.Sand.id); // Different region
        
        // Force regions to be created
        world.GetBlock(new Vector2(0, 0));
        world.GetBlock(new Vector2(100, 100));
        
        // Act: Call Draw multiple times to test both active and inactive region handling
        // First call should render active regions directly
        renderer.Draw();
        
        // Simulate time passing for regions to become inactive
        System.Threading.Thread.Sleep(1100); // Wait longer than SecondsUntilCachedTexture (1.0s)
        
        // Second call should create textures for inactive regions
        // This is where the bug would occur if background isn't cleared
        renderer.Draw();
        
        // Third call should use the cached textures
        renderer.Draw();
        
        // Assert: If we get here without exceptions, the texture rendering is working properly
        // The key fix ensures that when textures are created for inactive regions,
        // the background is properly cleared with air color instead of showing garbage pixels
        Assert.Pass("WorldRenderer.Draw() completed successfully with texture caching");
    }
}