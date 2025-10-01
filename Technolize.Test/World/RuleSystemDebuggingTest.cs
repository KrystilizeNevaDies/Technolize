using System.Numerics;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Ticking;
using Convert = Technolize.World.Ticking.Convert;

namespace Technolize.Test.World;

/// <summary>
/// Debug helper to understand how the rule system actually works
/// </summary>
[TestFixture]
public class RuleSystemDebuggingTest
{
    private TickableWorld _world;
    private MutationContext CreateContext(Vector2 position) => new(position, _world);

    [SetUp]
    public void SetUp()
    {
        _world = new TickableWorld();
    }

    [Test]
    public void DebugWaterAboveAir()
    {
        // Arrange: Water above Air
        Vector2 waterPos = new(0, 1);
        Vector2 airPos = new(0, 0);
        _world.SetBlock(waterPos, Blocks.Water);
        _world.SetBlock(airPos, Blocks.Air);

        var waterContext = CreateContext(waterPos);
        var airContext = CreateContext(airPos);

        // Act
        var waterMutations = Rule.CalculateMutations(waterContext).ToList();
        var airMutations = Rule.CalculateMutations(airContext).ToList();

        // Debug output
        Console.WriteLine($"Water mutations count: {waterMutations.Count}");
        foreach (var mutation in waterMutations)
        {
            Console.WriteLine($"Water mutation: {mutation.action} (chance: {mutation.chance})");
        }
        
        Console.WriteLine($"Air mutations count: {airMutations.Count}");
        foreach (var mutation in airMutations)
        {
            Console.WriteLine($"Air mutation: {mutation.action} (chance: {mutation.chance})");
        }

        // Basic assertion
        Assert.Pass("Check console output for mutations");
    }

    [Test]
    public void DebugFireNextToWood()
    {
        // Arrange: Fire next to wood
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Wood);
        _world.SetBlock(new(2, 1), Blocks.Air);

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Debug output
        Console.WriteLine($"Fire mutations count: {mutations.Count}");
        foreach (var mutation in mutations)
        {
            Console.WriteLine($"Mutation: {mutation.action} (chance: {mutation.chance})");
            if (mutation.action is Convert convert)
            {
                Console.WriteLine($"  Convert to block ID: {convert.Block}");
                Console.WriteLine($"  Convert positions: {string.Join(", ", convert.Slots)}");
            }
        }

        // Debug neighbors
        Console.WriteLine("Fire neighbors:");
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var neighbor = context.Get(dx, dy);
                Console.WriteLine($"  ({dx},{dy}): Block ID {neighbor.block} ({BlockRegistry.GetInfo(neighbor.block).GetTag(BlockInfo.TagDisplayName)})");
            }
        }

        // Basic assertion
        Assert.Pass("Check console output for mutations");
    }

    [Test]
    public void DebugAirAndWaterInteraction()
    {
        // Debug air trying to rise through water
        Vector2 airPos = new(0, 0);
        Vector2 waterPos = new(0, 1);
        _world.SetBlock(airPos, Blocks.Air);
        _world.SetBlock(waterPos, Blocks.Water);

        var airContext = CreateContext(airPos);

        // Act
        var mutations = Rule.CalculateMutations(airContext).ToList();

        // Debug output
        Console.WriteLine($"Air context: block={airContext.Info.GetTag(BlockInfo.TagDisplayName)}, density={airContext.Info.GetTag(BlockInfo.TagDensity)}");
        Console.WriteLine($"Above air: block={airContext.Get(0, 1).info.GetTag(BlockInfo.TagDisplayName)}, density={airContext.Get(0, 1).info.GetTag(BlockInfo.TagDensity)}");
        Console.WriteLine($"Air mutations count: {mutations.Count}");
        foreach (var mutation in mutations)
        {
            Console.WriteLine($"Air mutation: {mutation.action} (chance: {mutation.chance})");
        }

        Assert.Pass("Check console output for mutations");
    }
}