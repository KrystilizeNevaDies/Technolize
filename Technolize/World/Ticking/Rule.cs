using System.Collections.Frozen;
using System.Data;
using System.Numerics;
using Technolize.World.Block;
namespace Technolize.World.Ticking;

public interface IAction;

public record Chance(IAction Action, double ActionChance) : IAction;
public record Swap(Vector2 Slot) : IAction;
public record Convert(List<Vector2> Slots, uint Block) : IAction;
public record OneOf(params IAction[] Actions) : IAction;
public record AllOf(params IAction[] Actions) : IAction;

public static class Rule {
    public record Mut(IAction action, double chance = 1.0);

    public interface IContext {
        /// <summary>
        /// Gets a neighboring block.
        /// </summary>
        (uint block, MatterState matterState, BlockInfo info) Get(int x, int y);
        (uint block, MatterState matterState, BlockInfo info) Get(Vector2 pos) => Get((int)pos.X, (int)pos.Y);

        uint Block { get => Info; }
        MatterState MatterState { get => Info.GetTag(BlockInfo.TagMatterState); }
        BlockInfo Info { get; }
    }

    public static IEnumerable<Mut> CalculateMutations(IContext ctx) {

        if (ctx.Block == Blocks.Fire) {
            List<(uint block, Vector2 pos)> woodBlocks = GetSurroundingBlocks(ctx, block => block == Blocks.Wood);
            List<(uint block, Vector2 pos)> airBlocks = GetTouchingBlocks(ctx, block => block == Blocks.Air);
            if (woodBlocks.Count > 0 && airBlocks.Count > 0) {
                yield return new Mut(
                    new Chance(
                        new AllOf(
                            new Convert(woodBlocks.Select(it => it.pos).ToList(), Blocks.Fire),
                            new Chance(new Convert([new Vector2(0, 0)], Blocks.Charcoal), 0.05)
                        ),
                        0.1
                    )
                );
                yield break;
            }

            List<(uint block, Vector2 pos)> surroundingBlocks = GetSurroundingBlocks(ctx, block => block == Blocks.Wood);
            double chance = 1.0 - surroundingBlocks.Count / 9.0;

            yield return new Mut(new Convert([new Vector2(0, 0)], Blocks.Smoke), chance * 0.1);

            if (ctx.Get(0, 1).block == Blocks.Air) {
                yield return new Mut(new Swap(new Vector2(0, 1)), 2.0);
            }
        }

        if (ctx.Block == Blocks.Smoke) {
            if (ctx.Get(0, 1).block == Blocks.Air) {
                yield return new Mut(new Swap(new Vector2(0, 1)), 4.0);
            }

            List<(uint block, Vector2 pos)> airBlocks = GetTouchingBlocks(ctx, block => block == Blocks.Air);

            if (airBlocks.Count == 4) {
                yield return new Mut(new Convert([new Vector2(0, 0)], Blocks.Air), 0.2);
            }
        }

        // do regular physics
        foreach (Mut mut in MatterStateProperties(ctx)) {
            yield return mut;
        }
    }

    private static List<(uint block, Vector2 pos)> GetTouchingBlocks(IContext ctx, Func<uint, bool> filter) {
        List<(uint block, Vector2 pos)> blocks = [];

        for (int x = -1; x < 2; x += 2) {
            for (int y = -1; y < 2; y += 2) {
                if (filter(ctx.Get(x, y).block)) blocks.Add((ctx.Get(x, y).block, new Vector2(x, y)));
            }
        }

        return blocks;
    }

    private static List<(uint block, Vector2 pos)> GetSurroundingBlocks(IContext ctx, Func<uint, bool> filter) {
        List<(uint block, Vector2 pos)> blocks = [];

        for (int x = -1; x < 2; x++) {
            for (int y = -1; y < 2; y++) {
                if (x == 0 && y == 0) continue;
                if (filter(ctx.Get(x, y).block)) blocks.Add((ctx.Get(x, y).block, new Vector2(x, y)));
            }
        }

        return blocks;
    }

    private static IEnumerable<Mut> MatterStateProperties(IContext ctx) {

        var (block, matterState, _) = ctx.Get(0, 0);

        if (block == Blocks.Air) {
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
