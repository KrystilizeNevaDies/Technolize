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
        Vector2 waterPos = new(1, 1);
        Vector2 airPos = new(1, 0);

        // Use BatchSetBlocks to ensure we control the entire area
        _world.BatchSetBlocks(placer => {
            // Set a 3x3 area explicitly to air first
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    placer.Set(new Vector2(x, y), Blocks.Air);
                }
            }
            placer.Set(waterPos, Blocks.Water);
            placer.Set(airPos, Blocks.Air);
        });

        MutationContext airContext = CreateContext(airPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should want to swap with the water above it (air rises)
        Assert.That(mutations, Has.Count.GreaterThan(0));
        Rule.Mut? swapMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
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

        MutationContext airContext = CreateContext(airPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should want to swap with the sand above it (air rises)
        Assert.That(mutations, Has.Count.GreaterThan(0));
        Rule.Mut? swapMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
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

        MutationContext airContext = CreateContext(airPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should NOT want to move upward when it's already above denser water
        List<Rule.Mut> upwardMutations = mutations.Where(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1)).ToList();
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

        MutationContext context = CreateContext(stonePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Stone should not move (solid blocks don't fall)
        List<Rule.Mut> swapMutations = mutations.Where(m => m.action is Swap).ToList();
        Assert.That(swapMutations, Is.Empty, "Solid blocks should not move due to gravity");
    }

    [Test]
    public void LiquidFlowsSideways_DensityInteraction()
    {
        // Arrange: Water with air to the left and right
        Vector2 waterPos = new(1, 0);
        Vector2 leftAirPos = new(0, 0);
        Vector2 rightAirPos = new(2, 0);
        Vector2 belowStone = new(1, -1); // Add solid ground below
        _world.SetBlock(waterPos, Blocks.Water);
        _world.SetBlock(leftAirPos, Blocks.Air);
        _world.SetBlock(rightAirPos, Blocks.Air);
        _world.SetBlock(belowStone, Blocks.Stone); // Solid block below to prevent downward movement

        MutationContext context = CreateContext(waterPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Water should want to flow sideways into air (only liquids and gases flow sideways)
        List<Rule.Mut> sidewaysMutations = mutations.Where(m => m.action is Swap swap &&
                                                                (swap.Slot == new Vector2(-1, 0) || swap.Slot == new Vector2(1, 0))).ToList();
        Assert.That(sidewaysMutations, Has.Count.GreaterThan(0), "Liquid should flow sideways into air when grounded");
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

        MutationContext context = CreateContext(sandPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Sand should not flow sideways (only liquids and gases do that)
        List<Rule.Mut> sidewaysMutations = mutations.Where(m => m.action is Swap swap &&
                                                                (swap.Slot == new Vector2(-1, 0) || swap.Slot == new Vector2(1, 0))).ToList();
        Assert.That(sidewaysMutations, Is.Empty, "Powder should not flow sideways like liquids");
    }

    #endregion

    #region Wet State Interaction Tests

    [Test]
    public void DryWetCapableTileAbsorbsAdjacentWaterAndBecomesWet()
    {
        Vector2 dirtPos = new(1, 1);
        Vector2 waterPos = new(2, 1);
        BlockInfo wetDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, true);

        _world.SetBlock(dirtPos, Blocks.Dirt);
        _world.SetBlock(waterPos, Blocks.Water);

        MutationContext context = CreateContext(waterPos);

        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        Assert.That(mutations.Any(m => ContainsUnconditionalBlockConversionAtSlot(m.action, Blocks.Air, Vector2.Zero)), Is.True,
            "Water should disappear when absorbed into a dry wet-capable tile.");
        Assert.That(mutations.Any(m => ContainsUnconditionalBlockConversionAtSlot(m.action, wetDirt, new Vector2(-1, 0))), Is.True,
            "A dry wet-capable tile next to water should convert into its wet variant.");
    }

    [Test]
    public void SingleWaterCanOnlyWetOneAdjacentDryTile()
    {
        Vector2 waterPos = new(1, 1);
        Vector2 leftDirtPos = new(0, 1);
        Vector2 rightDirtPos = new(2, 1);

        _world.SetBlock(waterPos, Blocks.Water);
        _world.SetBlock(leftDirtPos, Blocks.Dirt);
        _world.SetBlock(rightDirtPos, Blocks.Dirt);

        MutationContext context = CreateContext(waterPos);

        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        Assert.That(mutations, Has.Count.EqualTo(1),
            "A water source with absorbable neighbors should emit a single absorption mutation.");

        Assert.That(mutations[0].action, Is.TypeOf<AllOf>());
        AllOf absorption = (AllOf)mutations[0].action;

        Assert.That(absorption.Actions.Any(action => action is Convert convert
                                                     && convert.Block == Blocks.Air
                                                     && convert.Slots.Contains(Vector2.Zero)), Is.True,
            "The water source should be consumed by the absorption mutation.");

        OneOf? wetChoice = absorption.Actions.OfType<OneOf>().SingleOrDefault();
        Assert.That(wetChoice, Is.Not.Null,
            "Exactly one adjacent dry tile should be chosen to receive the absorbed water.");
        Assert.That(wetChoice!.Actions, Has.Length.EqualTo(2),
            "Each absorbable dry neighbor should be represented as an alternative choice, not a simultaneous conversion.");
    }

    [Test]
    public void WetStateMovesDownIntoDryWetCapableTile()
    {
        Vector2 topPos = new(1, 2);
        Vector2 bottomPos = new(1, 1);
        BlockInfo wetGrass = Blocks.Grass.WithState(CommonBlockStates.Wet, true);
        BlockInfo dryGrass = Blocks.Grass.WithState(CommonBlockStates.Wet, false);
        BlockInfo transferredDryGrass = Blocks.Grass.WithState(CommonBlockStates.Wet, false);
        BlockInfo transferredWetGrass = Blocks.Grass.WithState(CommonBlockStates.Wet, true);

        _world.SetBlock(topPos, wetGrass);
        _world.SetBlock(bottomPos, dryGrass);

        MutationContext context = CreateContext(topPos);

        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        double? dryOutChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, transferredDryGrass, Vector2.Zero))
            .FirstOrDefault(chance => chance.HasValue);
        double? wetBelowChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, transferredWetGrass, new Vector2(0, -1)))
            .FirstOrDefault(chance => chance.HasValue);

        Assert.That(dryOutChance, Is.EqualTo(0.1),
            "A wet tile should only dry out through a chance-based downward wetness transfer.");
        Assert.That(wetBelowChance, Is.EqualTo(0.1),
            "A dry wet-capable tile below should only receive wetness through the same chance-based transfer.");
    }

    [Test]
    public void WetStateCanSpreadUpAndSidewaysWithSmallerChance()
    {
        Vector2 wetPos = new(1, 1);
        BlockInfo wetGrass = Blocks.Grass.WithState(CommonBlockStates.Wet, true);
        BlockInfo wetVariant = Blocks.Grass.WithState(CommonBlockStates.Wet, true);

        _world.SetBlock(wetPos, wetGrass);
        _world.SetBlock(new Vector2(1, 2), Blocks.Grass);
        _world.SetBlock(new Vector2(0, 1), Blocks.Grass);
        _world.SetBlock(new Vector2(2, 1), Blocks.Grass);

        MutationContext context = CreateContext(wetPos);

        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        double? wetUpChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, wetVariant, new Vector2(0, 1)))
            .FirstOrDefault(chance => chance.HasValue);
        double? wetLeftChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, wetVariant, new Vector2(-1, 0)))
            .FirstOrDefault(chance => chance.HasValue);
        double? wetRightChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, wetVariant, new Vector2(1, 0)))
            .FirstOrDefault(chance => chance.HasValue);

        Assert.That(wetUpChance, Is.EqualTo(0.02),
            "Wetness should be able to spread upward with a small chance.");
        Assert.That(wetLeftChance, Is.EqualTo(0.02),
            "Wetness should be able to spread left with a small chance.");
        Assert.That(wetRightChance, Is.EqualTo(0.02),
            "Wetness should be able to spread right with a small chance.");
    }

    [Test]
    public void WetnessDoesNotDuplicateWhenPlacedOnTopOfLargeDirtSquare()
    {
        Vector2 wetPos = new(4, 4);
        BlockInfo wetDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, true);
        BlockInfo dryDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, false);

        _world.BatchSetBlocks(placer =>
        {
            for (int x = 0; x < 9; x++)
            {
                for (int y = 0; y < 9; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Dirt);
                }
            }

            placer.Set(wetPos, wetDirt);
        });

        MutationContext context = CreateContext(wetPos);

        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        int duplicatingWetnessMutations = mutations.Count(mutation =>
            (ContainsBlockConversionAtSlot(mutation.action, wetDirt, new Vector2(0, -1))
             || ContainsBlockConversionAtSlot(mutation.action, wetDirt, new Vector2(0, 1))
             || ContainsBlockConversionAtSlot(mutation.action, wetDirt, new Vector2(-1, 0))
             || ContainsBlockConversionAtSlot(mutation.action, wetDirt, new Vector2(1, 0)))
            && !ContainsBlockConversionAtSlot(mutation.action, dryDirt, Vector2.Zero));

        Assert.That(duplicatingWetnessMutations, Is.EqualTo(0),
            "Wetness propagation should conserve the number of wet tiles instead of creating extra wet tiles above a dirt patch.");
    }

    [Test]
    public void WetnessCanFadeWithSmallChanceNearNonWetApplicableBlocks()
    {
        Vector2 wetPos = new(1, 1);
        BlockInfo wetDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, true);
        BlockInfo dryDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, false);

        _world.BatchSetBlocks(placer =>
        {
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Stone);
                }
            }

            placer.Set(wetPos, wetDirt);
            // Use a diagonal dry wet-applicable tile so fade is eligible without enabling transfer/spread paths.
            placer.Set(new Vector2(0, 0), dryDirt);
        });

        MutationContext context = CreateContext(wetPos);
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        double? fadeChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, dryDirt, Vector2.Zero))
            .FirstOrDefault(chance => chance.HasValue);

        Assert.That(fadeChance, Is.EqualTo(0.01),
            "Wetness should have a small chance to fade when all nearby wet-applicable neighbors are dry.");
    }

    [Test]
    public void WetnessDoesNotFadeWhenNoMovementIsPossible()
    {
        Vector2 wetPos = new(1, 1);
        BlockInfo wetDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, true);
        BlockInfo dryDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, false);

        _world.BatchSetBlocks(placer =>
        {
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Stone);
                }
            }

            placer.Set(wetPos, wetDirt);
        });

        MutationContext context = CreateContext(wetPos);
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        double? fadeChance = mutations
            .Select(m => FindChanceForBlockConversionAtSlot(m.action, dryDirt, Vector2.Zero))
            .FirstOrDefault(chance => chance.HasValue);

        Assert.That(fadeChance, Is.Null,
            "Wetness should not fade when there are no neighboring wet-applicable tiles to evaluate.");
    }

    #endregion

    #region Fire and Burning Interaction Tests

    [Test]
    public void FireSpreadsToBurnableBlocks()
    {
        // Arrange: Fire surrounded by burnable wood AND air in diagonal positions
        Vector2 firePos = new(1, 1);
        Vector2 woodPos = new(0, 1);
        Vector2 airDiagonal = new(0, 0); // Diagonal air position (touching)
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(woodPos, Blocks.Wood);
        _world.SetBlock(airDiagonal, Blocks.Air); // Air in diagonal position

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should want to spread to burnable blocks
        List<Rule.Mut> burnMutations = mutations.Where(m => m.action is Chance chance &&
                                                            chance.Action is AllOf allOf &&
                                                            allOf.Actions.Any(a => a is Convert convert && convert.Block == Blocks.Fire)).ToList();
        Assert.That(burnMutations, Has.Count.GreaterThan(0), "Fire should spread to burnable blocks when air is present in diagonal positions");
    }

    [Test]
    public void FireConvertsWoodToCharcoal()
    {
        // Arrange: Fire next to wood with air in diagonal position
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Wood);
        _world.SetBlock(new(0, 0), Blocks.Air); // Air in diagonal position

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Should contain conversion to charcoal
        bool hasCharcoalConversion = mutations.Any(m => ContainsCharcoalConversion(m.action));
        Assert.That(hasCharcoalConversion, Is.True, "Fire should convert wood to charcoal when air is diagonally present");
    }

    [Test]
    public void FireConvertsGrassToSmoke()
    {
        // Arrange: Fire next to grass with air in diagonal position
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Grass);
        _world.SetBlock(new(0, 0), Blocks.Air); // Air in diagonal position

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Should contain conversion to smoke
        bool hasSmokeConversion = mutations.Any(m => ContainsSmokeConversion(m.action));
        Assert.That(hasSmokeConversion, Is.True, "Fire should convert grass to smoke when air is diagonally present");
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

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should not spread without air
        List<Rule.Mut> burnMutations = mutations.Where(m => m.action is Chance chance &&
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

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should want to move upward
        Rule.Mut? upwardMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(upwardMutation, Is.Not.Null, "Fire should move upward into air");
        Assert.That(upwardMutation.chance, Is.EqualTo(2.0), "Fire should have chance 2.0 to move upward");
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

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Fire should convert to smoke when isolated
        Rule.Mut? smokeConversion = mutations.FirstOrDefault(m => m.action is Convert convert && convert.Block == Blocks.Smoke);
        Assert.That(smokeConversion, Is.Not.Null, "Isolated fire should convert to smoke");
    }

    #endregion

    #region Steam Interaction Tests

    [Test]
    public void SteamCanCondenseToWater_WhenSurroundedByAir()
    {
        // Arrange: Steam fully surrounded by air in all 8 neighboring cells.
        Vector2 steamPos = new(1, 1);
        _world.BatchSetBlocks(placer => {
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Air);
                }
            }

            placer.Set(steamPos, Blocks.Steam);
        });

        MutationContext context = CreateContext(steamPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Steam should get a probabilistic condensation path into water.
        double? condensationChance = FindChanceForBlockConversionAtSlot(mutations.Select(m => m.action), Blocks.Water, Vector2.Zero);
        Assert.That(condensationChance, Is.Not.Null, "Steam should have a chance to condense into water when fully surrounded by air");
        Assert.That(condensationChance!.Value, Is.EqualTo(0.1), "Steam condensation should use the configured random chance");
    }

    [Test]
    public void SteamDoesNotCondenseToWater_WhenNotFullySurroundedByAir()
    {
        // Arrange: Steam with one non-air neighbor, so it is not fully surrounded.
        Vector2 steamPos = new(1, 1);
        _world.BatchSetBlocks(placer => {
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Air);
                }
            }

            placer.Set(steamPos, Blocks.Steam);
            placer.Set(new Vector2(2, 2), Blocks.Stone);
        });

        MutationContext context = CreateContext(steamPos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Steam should not get the condensation mutation unless all 8 neighbors are air.
        double? condensationChance = FindChanceForBlockConversionAtSlot(mutations.Select(m => m.action), Blocks.Water, Vector2.Zero);
        Assert.That(condensationChance, Is.Null, "Steam should not condense into water unless all 8 surrounding cells are air");
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

        MutationContext context = CreateContext(smokePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Smoke should want to move upward
        Rule.Mut? upwardMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(upwardMutation, Is.Not.Null, "Smoke should move upward into air");
        Assert.That(upwardMutation.chance, Is.GreaterThan(1.0), "Smoke should have higher chance to move upward");
    }

    [Test]
    public void SmokeDisappears_WhenSurroundedByAir()
    {
        // Arrange: Smoke fully surrounded by air in all 8 neighboring cells.
        Vector2 smokePos = new(1, 1);
        _world.BatchSetBlocks(placer => {
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Air);
                }
            }

            placer.Set(smokePos, Blocks.Smoke);
        });

        MutationContext context = CreateContext(smokePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Smoke should convert to air and increment pollution when isolated in air.
        Rule.Mut? airConversion = mutations.FirstOrDefault(m => ContainsAirConversion(m.action));
        Assert.That(airConversion, Is.Not.Null, "Smoke should dissipate to air when fully surrounded by air");
        Assert.That(airConversion!.chance, Is.EqualTo(0.2), "Smoke dissipation should keep its small weighted chance");
        Assert.That(ContainsPollutionIncrement(airConversion.action), Is.True, "Smoke dissipation should increment pollution");
    }

    [Test]
    public void SmokeDoesNotDisappear_WhenNotFullySurroundedByAir()
    {
        // Arrange: Smoke with one non-air neighbor, so it is not fully surrounded.
        Vector2 smokePos = new(1, 1);
        _world.BatchSetBlocks(placer => {
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    placer.Set(new Vector2(x, y), Blocks.Air);
                }
            }

            placer.Set(smokePos, Blocks.Smoke);
            placer.Set(new Vector2(2, 2), Blocks.Stone);
        });

        MutationContext context = CreateContext(smokePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert: Smoke should NOT convert to air
        Rule.Mut? airConversion = mutations.FirstOrDefault(m => ContainsAirConversion(m.action));
        Assert.That(airConversion, Is.Null, "Smoke should not dissipate unless all 8 surrounding cells are air");
    }

    #endregion

    #region Complex Multi-Block Interaction Tests

    [Test]
    public void ComplexBurningScenario_FireSpreadAndConversion()
    {
        // Arrange: Complex scene with fire, wood, grass, and air in diagonal positions
        Vector2 firePos = new(1, 1);
        _world.SetBlock(firePos, Blocks.Fire);
        _world.SetBlock(new(0, 1), Blocks.Wood);   // Wood to the left
        _world.SetBlock(new(2, 1), Blocks.Grass);  // Grass to the right
        _world.SetBlock(new(0, 0), Blocks.Air);    // Air in diagonal position for fire spread
        _world.SetBlock(new(1, 2), Blocks.Air);    // Air above for fire movement

        MutationContext context = CreateContext(firePos);

        // Act
        List<Rule.Mut> mutations = Rule.CalculateMutations(context).ToList();

        // Assert multiple behaviors
        Assert.That(mutations, Has.Count.GreaterThan(0), "Fire should produce mutations in complex scenario");

        // Should want to spread to both wood and grass (when burnable blocks + air are present, spreading takes priority)
        bool hasSpreadMutations = mutations.Any(m => m.action is Chance chance &&
            chance.Action is AllOf allOf &&
            allOf.Actions.Any(a => a is Convert convert && convert.Block == Blocks.Fire));
        Assert.That(hasSpreadMutations, Is.True, "Fire should want to spread in complex scenario");

        // When fire can spread, it doesn't move upward (yield break in line 52 of Rule.cs)
        // So we check that either spreading OR upward movement occurs
        Rule.Mut? upwardMutation = mutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(hasSpreadMutations || upwardMutation != null, Is.True,
            "Fire should either spread OR move upward in complex scenario");
    }

    [Test]
    public void DensityLayering_MultipleFluidTypes()
    {
        // Test that multiple fluid types settle in proper density order
        // This is more of an integration test for the density system

        // Arrange: Set up a larger area to avoid auto-fill interference
        Vector2 airPos = new(1, 0);
        Vector2 waterPos = new(1, 1);

        // Use BatchSetBlocks to ensure we control the entire area
        _world.BatchSetBlocks(placer => {
            // Set a 3x3 area explicitly
            for (int x = 0; x < 3; x++) {
                for (int y = 0; y < 3; y++) {
                    placer.Set(new Vector2(x, y), Blocks.Air); // Default everything to air
                }
            }
            placer.Set(airPos, Blocks.Air);
            placer.Set(waterPos, Blocks.Water);
        });

        MutationContext airContext = CreateContext(airPos);

        // Act
        List<Rule.Mut> airMutations = Rule.CalculateMutations(airContext).ToList();

        // Assert: Air should rise up through water
        Rule.Mut? airRiseMutation = airMutations.FirstOrDefault(m => m.action is Swap swap && swap.Slot == new Vector2(0, 1));
        Assert.That(airRiseMutation, Is.Not.Null, "Air should want to rise up through water");
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

    private bool ContainsBlockConversion(IAction action, BlockInfo block)
    {
        return action switch
        {
            Convert convert => convert.Block == block,
            Chance chance => ContainsBlockConversion(chance.Action, block),
            AllOf allOf => allOf.Actions.Any(it => ContainsBlockConversion(it, block)),
            OneOf oneOf => oneOf.Actions.Any(it => ContainsBlockConversion(it, block)),
            _ => false
        };
    }

    private bool ContainsBlockConversionAtSlot(IAction action, BlockInfo block, Vector2 slot)
    {
        return action switch
        {
            Convert convert => convert.Block == block && convert.Slots.Contains(slot),
            Chance chance => ContainsBlockConversionAtSlot(chance.Action, block, slot),
            AllOf allOf => allOf.Actions.Any(it => ContainsBlockConversionAtSlot(it, block, slot)),
            OneOf oneOf => oneOf.Actions.Any(it => ContainsBlockConversionAtSlot(it, block, slot)),
            _ => false
        };
    }

    private bool ContainsUnconditionalBlockConversionAtSlot(IAction action, BlockInfo block, Vector2 slot)
    {
        return action switch
        {
            Convert convert => convert.Block == block && convert.Slots.Contains(slot),
            Chance => false,
            AllOf allOf => allOf.Actions.Any(it => ContainsUnconditionalBlockConversionAtSlot(it, block, slot)),
            OneOf oneOf => oneOf.Actions.Any(it => ContainsUnconditionalBlockConversionAtSlot(it, block, slot)),
            _ => false
        };
    }

    private double? FindChanceForBlockConversionAtSlot(IEnumerable<IAction> actions, BlockInfo block, Vector2 slot)
    {
        foreach (IAction action in actions)
        {
            double? chance = FindChanceForBlockConversionAtSlot(action, block, slot);
            if (chance.HasValue)
            {
                return chance;
            }
        }

        return null;
    }

    private double? FindChanceForBlockConversionAtSlot(IAction action, BlockInfo block, Vector2 slot)
    {
        return action switch
        {
            Chance chance when ContainsBlockConversionAtSlot(chance.Action, block, slot) => chance.ActionChance,
            Chance chance => FindChanceForBlockConversionAtSlot(chance.Action, block, slot),
            AllOf allOf => FindChanceForBlockConversionAtSlot(allOf.Actions, block, slot),
            OneOf oneOf => FindChanceForBlockConversionAtSlot(oneOf.Actions, block, slot),
            _ => null
        };
    }

    private bool ContainsAirConversion(IAction action)
    {
        return action switch
        {
            Convert convert => convert.Block == Blocks.Air,
            Chance chance => ContainsAirConversion(chance.Action),
            AllOf allOf => allOf.Actions.Any(ContainsAirConversion),
            OneOf oneOf => oneOf.Actions.Any(ContainsAirConversion),
            _ => false
        };
    }

    private bool ContainsPollutionIncrement(IAction action)
    {
        return action switch
        {
            AddPollution addPollution => addPollution.Amount > 0,
            Chance chance => ContainsPollutionIncrement(chance.Action),
            AllOf allOf => allOf.Actions.Any(ContainsPollutionIncrement),
            OneOf oneOf => oneOf.Actions.Any(ContainsPollutionIncrement),
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
