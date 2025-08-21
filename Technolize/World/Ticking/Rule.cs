using System.Collections.Frozen;
using System.Data;
using System.Numerics;
using Technolize.World.Block;
namespace Technolize.World.Ticking;


static class BlockTypes {
    public static readonly FrozenSet<uint> Powder = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Powder)
        .Select(block => block.Id)
        .ToFrozenSet();

    public static readonly FrozenSet<uint> Liquid = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Liquid)
        .Select(block => block.Id)
        .ToFrozenSet();

    public static readonly FrozenSet<uint> Solid = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Solid)
        .Select(block => block.Id)
        .ToFrozenSet();

    public static readonly FrozenSet<uint> Gas = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Gas && block.Id != Blocks.Air.Id)
        .Select(block => block.Id)
        .ToFrozenSet();
}

public interface IAction
{
    public record Chance(IAction Action, double ActionChance) : IAction;
    public record Swap(Vector2 Slot) : IAction;
    public record Convert(Vector2 Slot, uint Block) : IAction;
    public record OneOf(params IAction[] Actions) : IAction;
    public record AllOf(params IAction[] Actions) : IAction;
}

public record Rule(Dictionary<Vector2, FrozenSet<uint>> Slots, IAction Action, int Priority, double Chance = 1.0)
{

    public static IEnumerable<Rule> GetRules()
    {


        foreach (Rule rule in GetGravityRules()) {
            yield return rule;
        }

        foreach (Rule rule in GetSettleRules()) {
            yield return rule;
        }

        foreach (Rule rule in GetMistingRules()) {
            yield return rule;
        }

        foreach (Rule rule in GetDissolutionRules()) {
            yield return rule;
        }

        yield return new (
            new() {
                { new (0, 0), BlockTypes.Liquid },
                { new (0, -1), [Blocks.Stone.Id] }
            },
            new IAction.AllOf(
                new IAction.Convert(new (0, 0), Blocks.Sand.Id),
                new IAction.Convert(new (0, -1), Blocks.Sand.Id)
            ),
            0
        );
    }

    private static IEnumerable<Rule> GetDissolutionRules() {
        FrozenSet<uint> air = FrozenSet.Create(Blocks.Air.Id);

        yield return new (
            new() {
                { new (-1, 1), air },
                { new (0, 1), air },
                { new (1, 1), air },
                { new (1, 0), air },
                { new(0, 0), [Blocks.Water.Id] },
                { new (-1, 0), air }
            },
            new IAction.Convert(new (0, 0), Blocks.Mist.Id),
            3,
            0.1
        );
    }

    private static IEnumerable<Rule> GetMistingRules() {
        FrozenSet<uint> air = FrozenSet.Create(Blocks.Air.Id);
        FrozenSet<uint> nonMist = Blocks.AllBlockIds().Without([Blocks.Mist.Id]);
        yield return new (
            new() {
                { new(0, 0), [Blocks.Water.Id] },
                { new (0, -1), air }
            },
            new IAction.Convert(new (0, 0), Blocks.Mist.Id),
            0,
            0.01
        );

        yield return new (
            new() {
                { new (0, 1), air },
                { new(0, 0), [Blocks.Mist.Id] }
            },
            new IAction.Swap(new (0, 1)),
            1
        );

        yield return new (
            new() {
                { new (-1, 0), air },
                { new(0, 0), [Blocks.Mist.Id] },
                { new(1, 0), nonMist }
            },
            new IAction.Swap(new (-1, 0)),
            1
        );

        yield return new (
            new() {
                { new (1, 0), air },
                { new(0, 0), [Blocks.Mist.Id] },
                { new(-1, 0), nonMist }
            },
            new IAction.Swap(new (1, 0)),
            1
        );

        yield return new (
            new() {
                { new(0, 0), [Blocks.Mist.Id] },
            },
            new IAction.Chance(new IAction.Convert(new (0, 0), Blocks.Water.Id), 0.01),
            1
        );

        yield return new (
            new() {
                { new(0, 0), [Blocks.Mist.Id] },
            },
            // TODO: Convert this to another byproduct instead of air.
            new IAction.Convert(new (0, 0), Blocks.Air.Id),
            1,
            0.001
        );

        yield return new (
            new() {
                { new (-1, 1), air },
                { new (0, 1), air },
                { new (1, 1), air },
                { new (-1, 0), air },
                { new(0, 0), [Blocks.Mist.Id] },
                { new (1, 0), air },
                { new (-1, -1), air },
                { new (0, -1), air },
                { new (1, -1), air },
            },
            new IAction.Chance(new IAction.Convert(new (0, 0), Blocks.Water.Id), 0.1),
            1
        );

        yield return new (
            new() {
                { new (0, 1), [Blocks.Water.Id] },
                { new (-1, 0), [Blocks.Water.Id] },
                { new(0, 0), [Blocks.Mist.Id] },
                { new (1, 0), [Blocks.Water.Id] },
                { new (0, -1), [Blocks.Water.Id] },
            },
            new IAction.Convert(new (0, 0), Blocks.Water.Id),
            0
        );
    }

    private static IEnumerable<Rule> GetGravityRules() {
        FrozenSet<uint> air = FrozenSet.Create(Blocks.Air.Id);
        yield return new (
            new() {
                { new(0, 0), BlockTypes.Powder },
                { new (0, -1), air }
            },
            new IAction.Swap(new (0, -1)),
            0
        );

        yield return new (
            new() {
                { new (0, 0), BlockTypes.Liquid },
                { new (0, -1), air }
            },
            new IAction.Swap(new (0, -1)),
            0
        );

        yield return new (
            new() {
                { new (0, 0), BlockTypes.Powder },
                { new (0, -1), BlockTypes.Liquid }
            },
            new IAction.Swap(new (0, -1)),
            0
        );
    }

    private static IEnumerable<Rule> GetSettleRules() {
        FrozenSet<uint> air = FrozenSet.Create(Blocks.Air.Id);
        FrozenSet<uint> airOrLiquid = air.With(BlockTypes.Liquid);

        // settling patterns
        yield return new (
            new() {
                { new (0, 0), BlockTypes.Powder },
                { new (-1, -1), airOrLiquid }
            },
            new IAction.Swap(new (-1, -1)),
            5
        );

        yield return new (
            new() {
                { new (0, 0), BlockTypes.Powder },
                { new (1, -1), airOrLiquid }
            },
            new IAction.Swap(new (1, -1)),
            5
        );

        yield return new (
            new() {
                { new (-1, 1), air },
                { new (0, 1), air },
                { new (0, 0), BlockTypes.Liquid },
                { new (1, 0), BlockTypes.Liquid },
                { new (-1, 0), air }
            },
            new IAction.Swap(new (-1, 0)),
            1
        );

        yield return new (
            new() {
                { new (1, 1), air },
                { new (0, 1), air },
                { new (0, 0), BlockTypes.Liquid },
                { new (-1, 0), BlockTypes.Liquid },
                { new (1, 0), air }
            },
            new IAction.Swap(new (1, 0)),
            1
        );

        yield return new (
            new() {
                { new (0, 0), BlockTypes.Liquid },
                { new (-1, -1), air }
            },
            new IAction.Swap(new (-1, -1)),
            2
        );

        yield return new (
            new() {
                { new (0, 0), BlockTypes.Liquid },
                { new (1, -1), air }
            },
            new IAction.Swap(new (1, -1)),
            2
        );

        yield return new (
            new() {
                { new (-1, 1), air },
                { new (0, 0), BlockTypes.Liquid },
                { new (-1, 0), air }
            },
            new IAction.Swap(new (-1, 0)),
            3
        );

        yield return new (
            new() {
                { new (1, 1), air },
                { new (0, 0), BlockTypes.Liquid },
                { new (1, 0), air }
            },
            new IAction.Swap(new (1, 0)),
            3
        );
    }
}

static class SetUtils {
    public static FrozenSet<uint> With(this ISet<uint> set, IEnumerable<uint> other) {
        return set.Union(other).ToFrozenSet();
    }
    public static FrozenSet<uint> Without(this ISet<uint> set, IEnumerable<uint> other) {
        return set.Except(other).ToFrozenSet();
    }
}
