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
}

public interface IAction
{
    public record Swap(Vector2 Slot) : IAction;
    public record Convert(Vector2 Slot, uint Block) : IAction;
    public record OneOf(params IAction[] Actions) : IAction;
    public record AllOf(params IAction[] Actions) : IAction;
}

public record Rule(Dictionary<Vector2, FrozenSet<uint>> Slots, IAction Action, int Priority)
{

    public static IEnumerable<Rule> GetRules()
    {
        FrozenSet<uint> air = FrozenSet.Create(Blocks.Air.Id);
        FrozenSet<uint> airOrLiquid = air.With(BlockTypes.Liquid);

        {
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

        {
            // settling patterns
            yield return new (
                new() {
                    { new (0, 0), BlockTypes.Powder },
                    { new (-1, -1), airOrLiquid }
                },
                new IAction.Swap(new (-1, -1)),
                1
            );

            yield return new (
                new() {
                    { new (0, 0), BlockTypes.Powder },
                    { new (1, -1), airOrLiquid }
                },
                new IAction.Swap(new (1, -1)),
                1
            );

            yield return new (
                new() {
                    { new (0, 0), BlockTypes.Liquid },
                    { new (-1, -1), air }
                },
                new IAction.Swap(new (-1, -1)),
                1
            );

            yield return new (
                new() {
                    { new (0, 0), BlockTypes.Liquid },
                    { new (1, -1), air }
                },
                new IAction.Swap(new (1, -1)),
                1
            );

            yield return new (
                new() {
                    { new (-1, 1), air },
                    { new (0, 0), BlockTypes.Liquid },
                    { new (-1, 0), air }
                },
                new IAction.Swap(new (-1, 0)),
                1
            );

            yield return new (
                new() {
                    { new (1, 1), air },
                    { new (0, 0), BlockTypes.Liquid },
                    { new (1, 0), air }
                },
                new IAction.Swap(new (1, 0)),
                1
            );
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
}

static class SetUtils {
    public static FrozenSet<uint> With(this ISet<uint> set, ISet<uint> other) {
        return set.Union(other).ToFrozenSet();
    }
}
