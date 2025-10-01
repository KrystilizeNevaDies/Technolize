using System.Numerics;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Ticking;
using Convert = Technolize.World.Ticking.Convert;
namespace Technolize.Test.World;

[TestFixture]
public class BlockInteractionTest
{
    private TickableWorld _world;
    private MutationContext CreateContext(Vector2 position) => new(position, _world);

    [SetUp]
    public void SetUp()
    {
        _world = new TickableWorld();
    }

    #region Basic Block Operations Tests

    [Test]
    public void CanSwapDifferentBlockTypes()
    {
        // Arrange
        Vector2 pos1 = new(0, 0);
        Vector2 pos2 = new(1, 0);
        _world.SetBlock(pos1, Blocks.Stone);
        _world.SetBlock(pos2, Blocks.Water);

        // Act
        _world.SwapBlocks(pos1, pos2);

        // Assert
        Assert.That(_world.GetBlock(pos1), Is.EqualTo(Blocks.Water.id));
        Assert.That(_world.GetBlock(pos2), Is.EqualTo(Blocks.Stone.id));
    }

    [Test]
    public void CanSwapWithAir()
    {
        // Arrange
        Vector2 pos1 = new(0, 0);
        Vector2 pos2 = new(1, 0);
        _world.SetBlock(pos1, Blocks.Sand);
        _world.SetBlock(pos2, Blocks.Air);

        // Act
        _world.SwapBlocks(pos1, pos2);

        // Assert
        Assert.That(_world.GetBlock(pos1), Is.EqualTo(Blocks.Air.id));
        Assert.That(_world.GetBlock(pos2), Is.EqualTo(Blocks.Sand.id));
    }

    #endregion

    #region Density-Based Physics Tests

    [Test]
    public void LiquidFallsThroughGas_DensityInteraction()
    {
        // Arrange: Water above Air (air is lighter and should float up)
        Vector2 waterPos = new(0, 1);
        Vector2 airPos = new(0, 0);
        _world.SetBlock(waterPos, Blocks.Water);
        _world.SetBlock(airPos, Blocks.Air);

        var airContext = CreateContext(airPos);

        // Act
        var mutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should want to swap with the water above it (air rises)
        Assert.That(mutations, Has.Count.GreaterThan(0));
        var swapMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(swapMutation, Is.Not.Null, "Air should want to rise up through water");
    }

    [Test]
    public void PowderFallsThroughGas_DensityInteraction()
    {
        // Arrange: Sand above Air (air is lighter and should float up)
        Vector2 sandPos = new(0, 1);
        Vector2 airPos = new(0, 0);
        _world.SetBlock(sandPos, Blocks.Sand);
        _world.SetBlock(airPos, Blocks.Air);

        var airContext = CreateContext(airPos);

        // Act
        var mutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should want to swap with the sand above it (air rises)
        Assert.That(mutations, Has.Count.GreaterThan(0));
        var swapMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(swapMutation, Is.Not.Null, "Air should want to rise up through sand");
    }

    [Test]
    public void LiquidFloatsThroughDenserLiquid_DensityInteraction()
    {
        // Arrange: Air below Water (air is less dense and should rise)
        Vector2 airPos = new(0, 1);
        Vector2 waterPos = new(0, 0);
        _world.SetBlock(airPos, Blocks.Air);
        _world.SetBlock(waterPos, Blocks.Water);

        var airContext = CreateContext(airPos);

        // Act
        var mutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should NOT want to move upward when it's already above denser water
        var upwardMutations = mutations.Where(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1)).ToList();
        Assert.That(upwardMutations, Is.Empty, "Air should not try to move up when already above denser water");
    }

    [Test]
    public void SolidBlocksDoNotMove_DensityInteraction()
    {
        // Arrange: Stone above Air (stone is solid)
        Vector2 stonePos = new(0, 1);
        Vector2 airPos = new(0, 0);
        _world.SetBlock(stonePos, Blocks.Stone);
        _world.SetBlock(airPos, Blocks.Air);

        var context = CreateContext(stonePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Stone should not move (solid blocks don't fall)
        var swapMutations = mutations.Where(m => m.action is Swap).ToList();
        Assert.That(swapMutations, Is.Empty, "Solid blocks should not move due to gravity");
    }

    [Test]
    public void LiquidFlowsSideways_DensityInteraction()
    {
        // Arrange: Water with air to the left and right
        Vector2 waterPos = new(1, 0);
        Vector2 leftAirPos = new(0, 0);
        Vector2 rightAirPos = new(2, 0);
        _world.SetBlock(waterPos, Blocks.Water);
        _world.SetBlock(leftAirPos, Blocks.Air);
        _world.SetBlock(rightAirPos, Blocks.Air);

        var context = CreateContext(waterPos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Water should want to flow sideways into air
        var sidewaysMutations = mutations.Where(m => m.action is Swap swap && 
            (swap.Slot == new Vector2(-1, 0) || swap.Slot == new Vector2(1, 0))).ToList();
        Assert.That(sidewaysMutations, Has.Count.GreaterThan(0), "Liquid should flow sideways into air");
    }

    [Test]
    public void PowderDoesNotFlowSideways()
    {
        // Arrange: Sand with air to the left and right
        Vector2 sandPos = new(1, 0);
        Vector2 leftAirPos = new(0, 0);
        Vector2 rightAirPos = new(2, 0);
        _world.SetBlock(sandPos, Blocks.Sand);
        _world.SetBlock(leftAirPos, Blocks.Air);
        _world.SetBlock(rightAirPos, Blocks.Air);

        var context = CreateContext(sandPos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Sand should not flow sideways (only liquids and gases do that)
        var sidewaysMutations = mutations.Where(m => m.action is Swap swap && 
            (swap.Slot == new Vector2(-1, 0) || swap.Slot == new Vector2(1, 0))).ToList();
        Assert.That(sidewaysMutations, Is.Empty, "Powder should not flow sideways like liquids");
    }

    #endregion

    #region Fire and Burning Interaction Tests

    [Test]
    public void FireSpreadsToBurnableBlocks()
    {
        // Arrange: Fire surrounded by burnable wood
        Vector2 firePos = new(1, 1);
        Vector2 woodPos = new(0, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(woodPos, Blocks.Wood);
        _world.SetBlock(new(2, 1), Blocks.Air); // Need air for burning

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should want to spread to burnable blocks
        var burnMutations = mutations.Where(m => m.action is Chance chance && 
            chance.Action is AllOf allOf &&
            allOf.Actions.Any(a => a is Convert convert && convert.Block == Blocks.Fire)).ToList();
        Assert.That(burnMutations, Has.Count.GreaterThan(0), "Fire should spread to burnable blocks when air is present");
    }

    [Test]
    public void FireConvertsWoodToCharcoal()
    {
        // Arrange: Fire next to wood with air present
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Wood);
        _world.SetBlock(new(2, 1), Blocks.Air);

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Should contain conversion to charcoal
        bool hasCharcoalConversion = mutations.Any(m => ContainsCharcoalConversion(m.action));
        Assert.That(hasCharcoalConversion, Is.True, "Fire should convert wood to charcoal");
    }

    [Test]
    public void FireConvertsLeavesToSmoke()
    {
        // Arrange: Fire next to leaves with air present
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Leaves);
        _world.SetBlock(new(2, 1), Blocks.Air);

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Should contain conversion to smoke
        bool hasSmokeConversion = mutations.Any(m => ContainsSmokeConversion(m.action));
        Assert.That(hasSmokeConversion, Is.True, "Fire should convert leaves to smoke");
    }

    [Test]
    public void FireWithoutAirDoesNotSpread()
    {
        // Arrange: Fire surrounded by burnable wood but no air
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Wood);
        _world.SetBlock(new(2, 1), Blocks.Wood);
        _world.SetBlock(new(1, 0), Blocks.Wood);
        _world.SetBlock(new(1, 2), Blocks.Wood);
        // No air blocks

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should not spread without air
        var burnMutations = mutations.Where(m => m.action is Chance chance && 
            chance.Action is AllOf allOf &&
            allOf.Actions.Any(a => a is Convert convert && convert.Block == Blocks.Fire)).ToList();
        Assert.That(burnMutations, Is.Empty, "Fire should not spread without air present");
    }

    [Test]
    public void FireMovesUpward()
    {
        // Arrange: Fire with air above
        Vector2 firePos = new(0, 0);
        Vector2 airAbove = new(0, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(airAbove, Blocks.Air);

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should want to move upward
        var upwardMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(upwardMutation, Is.Not.Null, "Fire should move upward into air");
        Assert.That(upwardMutation.chance, Is.GreaterThan(1.0), "Fire should have higher chance to move upward");
    }

    [Test]
    public void FireConvertsToSmokeWhenIsolated()
    {
        // Arrange: Fire with no burnable neighbors but some wood far away
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        // Surround with air and non-burnable blocks
        _world.SetBlock(new(0, 1), Blocks.Air);
        _world.SetBlock(new(2, 1), Blocks.Air);
        _world.SetBlock(new(1, 0), Blocks.Stone);
        _world.SetBlock(new(1, 2), Blocks.Air);
        // Add wood outside of immediate neighbors to test isolation
        _world.SetBlock(new(3, 3), Blocks.Wood);

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should convert to smoke when isolated
        var smokeConversion = mutations.FirstOrDefault(m => m.action is Convert convert && convert.Block == Blocks.Smoke);
        Assert.That(smokeConversion, Is.Not.Null, "Isolated fire should convert to smoke");
    }

    #endregion

    #region Smoke Interaction Tests

    [Test]
    public void SmokeMovesUpward()
    {
        // Arrange: Smoke with air above
        Vector2 smokePos = new(0, 0);
        Vector2 airAbove = new(0, 1);
        _world.SetBlock(smokePos, Blocks.Smoke);
        _world.SetBlock(airAbove, Blocks.Air);

        var context = CreateContext(smokePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Smoke should want to move upward
        var upwardMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(upwardMutation, Is.Not.Null, "Smoke should move upward into air");
        Assert.That(upwardMutation.chance, Is.GreaterThan(1.0), "Smoke should have higher chance to move upward");
    }

    [Test]
    public void SmokeDisappears_WhenSurroundedByAir()
    {
        // Arrange: Smoke surrounded by air (4 touching positions)
        Vector2 smokePos = new(1, 1);
        _world.SetBlock(smokePos, Blocks.Smoke);
        _world.SetBlock(new(0, 0), Blocks.Air); // Top-left (touching)
        _world.SetBlock(new(2, 0), Blocks.Air); // Top-right (touching)
        _world.SetBlock(new(0, 2), Blocks.Air); // Bottom-left (touching)
        _world.SetBlock(new(2, 2), Blocks.Air); // Bottom-right (touching)

        var context = CreateContext(smokePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Smoke should convert to air when surrounded
        var airConversion = mutations.FirstOrDefault(m => m.action is Convert convert && convert.Block == Blocks.Air);
        Assert.That(airConversion, Is.Not.Null, "Smoke should dissipate to air when surrounded by air");
        Assert.That(airConversion.chance, Is.EqualTo(0.2), "Smoke dissipation should have 20% chance");
    }

    [Test]
    public void SmokeDoesNotDisappear_WhenNotFullySurroundedByAir()
    {
        // Arrange: Smoke with only 3 air blocks touching (not all 4)
        Vector2 smokePos = new(1, 1);
        _world.SetBlock(smokePos, Blocks.Smoke);
        _world.SetBlock(new(0, 0), Blocks.Air); // Top-left (touching)
        _world.SetBlock(new(2, 0), Blocks.Air); // Top-right (touching)
        _world.SetBlock(new(0, 2), Blocks.Air); // Bottom-left (touching)
        _world.SetBlock(new(2, 2), Blocks.Stone); // Bottom-right (touching) - NOT air

        var context = CreateContext(smokePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Smoke should NOT convert to air
        var airConversion = mutations.FirstOrDefault(m => m.action is Convert convert && convert.Block == Blocks.Air);
        Assert.That(airConversion, Is.Null, "Smoke should not dissipate unless all 4 touching corners are air");
    }

    #endregion

    #region Complex Multi-Block Interaction Tests

    [Test]
    public void ComplexBurningScenario_FireSpreadAndConversion()
    {
        // Arrange: Complex scene with fire, wood, leaves, and air
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Wood);   // Wood to the left
        _world.SetBlock(new(2, 1), Blocks.Leaves); // Leaves to the right
        _world.SetBlock(new(1, 0), Blocks.Air);    // Air below
        _world.SetBlock(new(1, 2), Blocks.Air);    // Air above
        _world.SetBlock(new(0, 0), Blocks.Air);    // Air bottom-left
        _world.SetBlock(new(2, 0), Blocks.Air);    // Air bottom-right

        var context = CreateContext(firePos);

        // Act
        var mutations = Rule.CalculateMutations(context).ToList();

        // Assert multiple behaviors
        Assert.That(mutations, Has.Count.GreaterThan(0), "Fire should produce mutations in complex scenario");
        
        // Should want to spread to both wood and leaves
        bool hasSpreadMutations = mutations.Any(m => m.action is Chance chance && 
            chance.Action is AllOf allOf &&
            allOf.Actions.Any(a => a is Convert convert && convert.Block == Blocks.Fire));
        Assert.That(hasSpreadMutations, Is.True, "Fire should want to spread in complex scenario");
        
        // Should want to move upward
        var upwardMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(upwardMutation, Is.Not.Null, "Fire should want to move upward even in complex scenario");
    }

    [Test]
    public void DensityLayering_MultipleFluidTypes()
    {
        // Test that multiple fluid types settle in proper density order
        // This is more of an integration test for the density system
        
        // Arrange: Air at bottom, then water, then "heavier water" (if we had it)
        // For now, test water above air
        Vector2 airPos = new(0, 0);
        Vector2 waterPos = new(0, 1);
        _world.SetBlock(airPos, Blocks.Air);
        _world.SetBlock(waterPos, Blocks.Water);

        var waterContext = CreateContext(waterPos);
        var airContext = CreateContext(airPos);

        // Act
        var waterMutations = Rule.CalculateMutations(waterContext).ToList();
        var airMutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Water should fall, air should rise
        var waterFallMutation = waterMutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, -1));
        var airRiseMutation = airMutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        
        Assert.That(waterFallMutation, Is.Not.Null, "Water should want to fall down");
        Assert.That(airRiseMutation, Is.Not.Null, "Air should want to rise up");
    }

    #endregion

    #region Helper Methods

    private bool ContainsCharcoalConversion(IAction action)
    {
        return action switch
        {
            Convert convert => convert.Block == Blocks.Charcoal,
            Chance chance => ContainsCharcoalConversion(chance.Action),
            AllOf allOf => allOf.Actions.Any(ContainsCharcoalConversion),
            OneOf oneOf => oneOf.Actions.Any(ContainsCharcoalConversion),
            _ => false
        };
    }

    private bool ContainsSmokeConversion(IAction action)
    {
        return action switch
        {
            Convert convert => convert.Block == Blocks.Smoke,
            Chance chance => ContainsSmokeConversion(chance.Action),
            AllOf allOf => allOf.Actions.Any(ContainsSmokeConversion),
            OneOf oneOf => oneOf.Actions.Any(ContainsSmokeConversion),
            _ => false
        };
    }

    #endregion
}

/// <summary>
/// Test context for rule mutations - implements the IContext interface needed by Rule.CalculateMutations
/// </summary>
public record MutationContext(Vector2 Position, TickableWorld World) : Rule.IContext
{
    public BlockInfo Info => BlockRegistry.GetInfo(World.GetBlock(Position));

    public (uint block, MatterState matterState, BlockInfo info) Get(int x, int y)
    {
        Vector2 pos = Position + new Vector2(x, y);
        uint blockId = (uint)World.GetBlock(pos);
        BlockInfo blockInfo = BlockRegistry.GetInfo(blockId);
        MatterState matterState = blockInfo.GetTag(BlockInfo.TagMatterState);
        return (blockId, matterState, blockInfo);
    }
}