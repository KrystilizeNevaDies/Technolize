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
        }

        // Basic assertion
        Assert.Pass("Check console output for mutations");
    }

    [Test]
    public void DebugBlockProperties()
    {
        // Debug block properties
        Console.WriteLine("Block Properties:");
        Console.WriteLine($"Air: id={Blocks.Air.id}, density={Blocks.Air.GetTag(BlockInfo.TagDensity)}, matter={Blocks.Air.GetTag(BlockInfo.TagMatterState)}");
        Console.WriteLine($"Water: id={Blocks.Water.id}, density={Blocks.Water.GetTag(BlockInfo.TagDensity)}, matter={Blocks.Water.GetTag(BlockInfo.TagMatterState)}");
        Console.WriteLine($"Stone: id={Blocks.Stone.id}, density={Blocks.Stone.GetTag(BlockInfo.TagDensity)}, matter={Blocks.Stone.GetTag(BlockInfo.TagMatterState)}");
        Console.WriteLine($"Sand: id={Blocks.Sand.id}, density={Blocks.Sand.GetTag(BlockInfo.TagDensity)}, matter={Blocks.Sand.GetTag(BlockInfo.TagMatterState)}");
        Console.WriteLine($"Fire: id={Blocks.Fire.id}, density={Blocks.Fire.GetTag(BlockInfo.TagDensity)}, matter={Blocks.Fire.GetTag(BlockInfo.TagMatterState)}");
        Console.WriteLine($"Smoke: id={Blocks.Smoke.id}, density={Blocks.Smoke.GetTag(BlockInfo.TagDensity)}, matter={Blocks.Smoke.GetTag(BlockInfo.TagMatterState)}");

        Assert.Pass("Check console output for block properties");
    }
}