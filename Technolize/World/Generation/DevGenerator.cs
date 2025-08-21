using System.Numerics;
using Technolize.World.Block;
namespace Technolize.World.Generation;

/// <summary>
/// A utility class to generate a pre-defined developer/test world.
/// </summary>
public class DevGenerator
{
    private readonly int _width;
    private readonly int _height;
    private readonly Random _random = new();

    /// <summary>
    /// Creates a generator for a world of a specific size.
    /// </summary>
    /// <param name="width">The width of the world to generate.</param>
    /// <param name="height">The height of the world to generate.</param>
    public DevGenerator(int width = 256, int height = 128)
    {
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Populates the given IWorld with a standard set of blocks for development and testing.
    /// This method uses the highly efficient BatchSetBlocks for world creation.
    /// </summary>
    /// <param name="world">The world instance to populate.</param>
    public void Generate(IWorld world)
    {
        world.BatchSetBlocks(placer =>
        {
            GenerateContainer(placer);
            GenerateWater(placer);
            GenerateSandPile(placer);
            GenerateBoulders(placer);
        });
    }

    private void GenerateContainer(IBlockPlacer placer)
    {
        const int wallThickness = 5;
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                // Place the floor and side walls
                if (y < wallThickness || x < wallThickness || x >= _width - wallThickness)
                {
                    placer.Set(new (x, y), Blocks.Stone.Id);
                }
            }
        }
    }

    private void GenerateWater(IBlockPlacer placer)
    {
        const int wallThickness = 5;
        int waterLevel = _height / 2 - 10;

        for (int y = wallThickness; y < waterLevel; y++)
        {
            for (int x = wallThickness; x < _width - wallThickness; x++)
            {
                placer.Set(new (x, y), Blocks.Water.Id);
            }
        }
    }

    private void GenerateSandPile(IBlockPlacer placer)
    {
        const int sandWidth = 60;
        const int sandHeight = 30;

        int sandStartX = (_width - sandWidth) / 2;
        int sandStartY = _height / 2;

        for (int y = 0; y < sandHeight; y++)
        {
            for (int x = 0; x < sandWidth; x++)
            {
                placer.Set(new (sandStartX + x, sandStartY + y), Blocks.Sand.Id);
            }
        }
    }

    private void GenerateBoulders(IBlockPlacer placer)
    {
        const int wallThickness = 5;
        int waterLevel = _height / 2 - 10;
        int boulderCount = _random.Next(8, 15);

        for (int i = 0; i < boulderCount; i++)
        {
            int boulderX = _random.Next(wallThickness + 10, _width - wallThickness - 10);
            int boulderY = _random.Next(wallThickness, waterLevel);

            // Make boulders of varying small sizes
            int boulderWidth = _random.Next(2, 5);
            int boulderHeight = _random.Next(2, 5);

            for(int y = 0; y < boulderHeight; y++)
            {
                for(int x = 0; x < boulderWidth; x++)
                {
                    placer.Set(new (boulderX + x, boulderY + y), Blocks.Stone.Id);
                }
            }
        }
    }
}
