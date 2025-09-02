using System.Collections.Frozen;
using System.Data;
using System.Numerics;
using Technolize.World.Block;
using Technolize.World.Tag;
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
            List<(BlockInfo block, Vector2 pos)> burnableBlocks = GetSurroundingBlocks(ctx,
                block => block.HasTag(BlockTags.Burnable));
            List<(BlockInfo block, Vector2 pos)> airBlocks = GetTouchingBlocks(ctx, block => block == Blocks.Air);
            if (burnableBlocks.Count > 0 && airBlocks.Count > 0) {
                yield return new Mut(
                    new Chance(
                        new AllOf(
                            new Convert(burnableBlocks.Select(it => it.pos).ToList(), Blocks.Fire),
                            new Chance(
                                // randomly pick between the burning blocks to convert
                                new OneOf(burnableBlocks.Select(it =>
                                    new Convert([it.pos], it.block.GetTag(BlockTags.Burnable))
                                ).ToArray<IAction>()
                            ), 0.05)
                        ),
                        0.1
                    )
                );
                yield break;
            }

            List<(BlockInfo block, Vector2 pos)> surroundingBlocks = GetSurroundingBlocks(ctx, block => block == Blocks.Wood);
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

            List<(BlockInfo block, Vector2 pos)> airBlocks = GetTouchingBlocks(ctx, block => block == Blocks.Air);

            if (airBlocks.Count == 4) {
                yield return new Mut(new Convert([new Vector2(0, 0)], Blocks.Air), 0.2);
            }
        }

        // do regular physics
        foreach (Mut mut in DensityProperties(ctx)) {
            yield return mut;
        }
    }

    private static List<(BlockInfo block, Vector2 pos)> GetTouchingBlocks(IContext ctx, Func<BlockInfo, bool> filter) {
        List<(BlockInfo block, Vector2 pos)> blocks = [];

        for (int x = -1; x < 2; x += 2) {
            for (int y = -1; y < 2; y += 2) {
                if (filter(ctx.Get(x, y).block)) blocks.Add((ctx.Get(x, y).block, new Vector2(x, y)));
            }
        }

        return blocks;
    }

    private static List<(BlockInfo block, Vector2 pos)> GetSurroundingBlocks(IContext ctx, Func<BlockInfo, bool> filter) {
        List<(BlockInfo block, Vector2 pos)> blocks = [];

        for (int x = -1; x < 2; x++) {
            for (int y = -1; y < 2; y++) {
                if (x == 0 && y == 0) continue;
                if (filter(ctx.Get(x, y).block)) blocks.Add((ctx.Get(x, y).block, new Vector2(x, y)));
            }
        }

        return blocks;
    }

    private static IEnumerable<Mut> DensityProperties(IContext ctx) {

        (uint _, MatterState matterState, BlockInfo info) = ctx.Get(0, 0);
        double density = info.GetTag(BlockInfo.TagDensity);

        if (matterState is MatterState.Solid) {
            // solid blocks do not move
            yield break;
        }

        // if the block above is denser, swap with it
        var aboveInfo = ctx.Get(0, 1).info;
        double densityAbove = aboveInfo.GetTag(BlockInfo.TagDensity);
        if (density < densityAbove && aboveInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid) {
            yield return new Mut(new Swap(new Vector2(0, 1)));
            yield break;
        }

        // if the block below is less dense, do nothing since the above rule will handle it
        var belowInfo = ctx.Get(0, -1).info;
        double densityBelow = belowInfo.GetTag(BlockInfo.TagDensity);
        if (density > densityBelow) {
            yield break;
        }

        // if the block to the bottom left is less dense, swap with it
        double densityBottomLeft = ctx.Get(-1, -1).info.GetTag(BlockInfo.TagDensity);
        if (density > densityBottomLeft) {
            yield return new Mut(new Swap(new Vector2(-1, -1)));
        }

        // if the block to the bottom right is less dense, swap with it
        double densityBottomRight = ctx.Get(1, -1).info.GetTag(BlockInfo.TagDensity);
        if (density > densityBottomRight) {
            yield return new Mut(new Swap(new Vector2(1, -1)));
        }

        // powder only does gravity and settling
        if (matterState is MatterState.Powder) yield break;

        var lessDenseBlocks = GetSurroundingBlocks(ctx, blockInfo => density > blockInfo.GetTag(BlockInfo.TagDensity));

        double leftMoveChance = 0.0;
        double rightMoveChance = 0.0;

        // if the block to the left is less dense, swap with it
        double densityLeft = ctx.Get(-1, 0).info.GetTag(BlockInfo.TagDensity);
        if (density > densityLeft) {
            leftMoveChance = lessDenseBlocks.Count(it => Math.Abs(it.pos.X - -1.0) < 0.01);
        }

        // if the block to the right is less dense, swap with it
        double densityRight = ctx.Get(1, 0).info.GetTag(BlockInfo.TagDensity);
        if (density > densityRight) {
            rightMoveChance = lessDenseBlocks.Count(it => Math.Abs(it.pos.X - 1.0) < 0.01);
        }


        if (leftMoveChance > 0.0 && rightMoveChance > 0.0) {
            yield return new Mut(new Swap(new Vector2(-1, 0)), leftMoveChance);
            yield return new Mut(new Swap(new Vector2(1, 0)), rightMoveChance);
        } else if (leftMoveChance > 0.0) {
            yield return new Mut(new Swap(new Vector2(-1, 0)));
        } else if (rightMoveChance > 0.0) {
            yield return new Mut(new Swap(new Vector2(1, 0)));
        }
    }
}
