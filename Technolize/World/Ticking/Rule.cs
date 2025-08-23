using System.Collections.Frozen;
using System.Data;
using System.Numerics;
using Technolize.World.Block;
namespace Technolize.World.Ticking;

public interface IAction;

public record Chance(IAction Action, double ActionChance) : IAction;
public record Swap(Vector2 Slot) : IAction;
public record Convert(Vector2 Slot, uint Block) : IAction;
public record OneOf(params IAction[] Actions) : IAction;
public record AllOf(params IAction[] Actions) : IAction;

public static class Rule {
    public record Mut(IAction action, double chance = 1.0);

    public interface IContext {
        /// <summary>
        /// Gets a neighboring block.
        /// </summary>
        (uint block, MatterState matterState, BlockInfo info) Get(int x, int y);

        uint Block { get => Info.Id; }
        MatterState MatterState { get => Info.MatterState; }
        BlockInfo Info { get; }
    }

    public static IEnumerable<Mut> CalculateMutations(IContext ctx) {

        // do regular physics
        foreach (Mut mut in MatterStateProperties(ctx)) {
            yield return mut;
        }
    }

    private static IEnumerable<Mut> MatterStateProperties(IContext ctx) {

        var (block, matterState, _) = ctx.Get(0, 0);

        if (block == Blocks.Air.Id) {
            // air does not do anything
            yield break;
        }

        if (matterState == MatterState.Gas) {
            // gas randomly moves around
            if (matterState == ctx.Get(1, 0).matterState) yield return new Mut(new Swap(new Vector2(1, 0)));
            if (matterState == ctx.Get(-1, 0).matterState) yield return new Mut(new Swap(new Vector2(-1, 0)));
            if (matterState == ctx.Get(0, 1).matterState) yield return new Mut(new Swap(new Vector2(0, 1)));
            if (matterState == ctx.Get(0, -1).matterState) yield return new Mut(new Swap(new Vector2(0, -1)));
        }

        // gravity applies to powder and liquid
        if (matterState is MatterState.Powder or MatterState.Liquid) {
            // if the matterState is lower, fall down
            if (matterState < ctx.Get(0, -1).matterState) {
                yield return new Mut(new Swap(new Vector2(0, -1)));
                yield break;
            }
        }

        // settling applies to liquid and powder
        if (matterState is MatterState.Liquid or MatterState.Powder) {

            if (matterState < ctx.Get(-1, -1).matterState) {
                yield return new Mut(new Swap(new Vector2(-1, -1)));
            }

            if (matterState < ctx.Get(1, -1).matterState) {
                yield return new Mut(new Swap(new Vector2(1, -1)));
            }
        }

        // flowing applies to liquid
        if (matterState == MatterState.Liquid) {
            if (matterState < ctx.Get(-1, 1).matterState &&
                matterState < ctx.Get(-1, 0).matterState) {
                yield return new Mut(new Swap(new Vector2(-1, 0)));
            }

            if (matterState < ctx.Get(1, 1).matterState &&
                matterState < ctx.Get(1, 0).matterState) {
                yield return new Mut(new Swap(new Vector2(1, 0)));
            }
        }
    }
}
