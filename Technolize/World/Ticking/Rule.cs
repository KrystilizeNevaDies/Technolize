using System.Numerics;
using Technolize.World.Block;
using Technolize.World.Tag;
namespace Technolize.World.Ticking;

public interface IAction;

public record Chance(IAction Action, double ActionChance) : IAction;
public record Swap(Vector2 Slot) : IAction;
public record Convert(List<Vector2> Slots, uint Block) : IAction;
public record AddPollution(int Amount) : IAction;
public record OneOf(params IAction[] Actions) : IAction;
public record AllOf(params IAction[] Actions) : IAction;

public static class Rule {
    private const double FireExtinguishSmokeFraction = 0.1;
    private const double SteamCondensationChance = 0.1;

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
            List<(BlockInfo block, Vector2 pos)> surroundingAirBlocks = GetSurroundingBlocks(ctx,
                block => block == Blocks.Air);
            List<(BlockInfo block, Vector2 pos)> burnableBlocks = GetSurroundingBlocks(ctx,
                block => block.HasTag(BlockTags.Burnable));
            // TODO: improve fire burn output
            if (burnableBlocks.Count > 0 && surroundingAirBlocks.Count > 0) {
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
                        0.15
                    )
                );
                yield break;
            }

            List<(BlockInfo block, Vector2 pos)> surroundingBlocks = GetSurroundingBlocks(ctx, block => block == Blocks.Fire);
            double stayChance = surroundingBlocks.Count / 9.0;
            if (stayChance < 0.01) stayChance = 0.01;

            double extinguishChance = 1.0 - stayChance;
            double smokeChance = extinguishChance * FireExtinguishSmokeFraction;
            double airChance = extinguishChance - smokeChance;

            yield return new Mut(new Convert([new Vector2(0, 0)], Blocks.Smoke), smokeChance);
            yield return new Mut(new Convert([new Vector2(0, 0)], Blocks.Air), airChance);

            if (ctx.Get(0, 1).block == Blocks.Air) {
                yield return new Mut(new Swap(new Vector2(0, 1)), 2.0);
            } else {
                yield return new Mut(new AllOf());
            }
        }

        if (ctx.Block == Blocks.Steam)
        {
            if (IsFullySurroundedBy(ctx, Blocks.Air))
            {
                yield return new Mut(new Chance(new Convert([Vector2.Zero], Blocks.Water), SteamCondensationChance));
            }
        }

        if (ctx.Block == Blocks.Smoke) {
            if (ctx.Get(0, 1).block == Blocks.Air) {
                yield return new Mut(new Swap(new Vector2(0, 1)), 4.0);
            }

            List<(BlockInfo block, Vector2 pos)> airBlocks = GetSurroundingBlocks(ctx, block => block == Blocks.Air);

            if (airBlocks.Count == 8) {
                yield return new Mut(new AllOf(
                    new Convert([new Vector2(0, 0)], Blocks.Air),
                    new AddPollution(1)
                ), 0.2);
            }
        }

        // do regular physics
        foreach (Mut mut in DensityProperties(ctx)) {
            yield return mut;
        }
    }

    private static bool IsFullySurroundedBy(IContext ctx, BlockInfo block)
    {
        return GetSurroundingBlocks(ctx, other => other == block).Count == 8;
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
        BlockInfo aboveInfo = ctx.Get(0, 1).info;
        double densityAbove = aboveInfo.GetTag(BlockInfo.TagDensity);
        if (density < densityAbove && aboveInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid) {
            yield return new Mut(new Swap(new Vector2(0, 1)));
            yield break;
        }

        // if the block below is less dense, do nothing since the above rule will handle it
        BlockInfo belowInfo = ctx.Get(0, -1).info;
        double densityBelow = belowInfo.GetTag(BlockInfo.TagDensity);
        if (density > densityBelow) {
            yield break;
        }

        // if the block to the bottom left is less dense and not solid, swap with it
        BlockInfo bottomLeftInfo = ctx.Get(-1, -1).info;
        double densityBottomLeft = bottomLeftInfo.GetTag(BlockInfo.TagDensity);
        if (bottomLeftInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid && density > densityBottomLeft) {
            yield return new Mut(new Swap(new Vector2(-1, -1)));
        }

        // if the block to the bottom right is less dense and not solid, swap with it
        BlockInfo bottomRightInfo = ctx.Get(1, -1).info;
        double densityBottomRight = bottomRightInfo.GetTag(BlockInfo.TagDensity);
        if (bottomRightInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid && density > densityBottomRight) {
            yield return new Mut(new Swap(new Vector2(1, -1)));
        }

        // powder only does gravity and settling
        if (matterState is MatterState.Powder) yield break;

        List<(BlockInfo block, Vector2 pos)> lessDenseBlocks = GetSurroundingBlocks(ctx,
            blockInfo => blockInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid &&
                         density > blockInfo.GetTag(BlockInfo.TagDensity));

        double leftMoveChance = 0.0;
        double rightMoveChance = 0.0;

        // if the block to the left is less dense, swap with it
        BlockInfo leftInfo = ctx.Get(-1, 0).info;
        double densityLeft = leftInfo.GetTag(BlockInfo.TagDensity);
        if (leftInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid && density > densityLeft) {
            leftMoveChance = lessDenseBlocks.Count(it => Math.Abs(it.pos.X - -1.0) < 0.01);
        }

        // if the block to the right is less dense, swap with it
        BlockInfo rightInfo = ctx.Get(1, 0).info;
        double densityRight = rightInfo.GetTag(BlockInfo.TagDensity);
        if (rightInfo.GetTag(BlockInfo.TagMatterState) != MatterState.Solid && density > densityRight) {
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
