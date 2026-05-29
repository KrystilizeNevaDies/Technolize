using System.Numerics;
using Technolize.World.Block;
using Technolize.World.Tag;
namespace Technolize.World.Ticking;

public interface IAction {
    // returns a pruned version of this action if it is redundant in the given context, or null if it is completely redundant
    public IAction? PruneRedundant(MutationContext context);
}

public record Chance(IAction Action, double ActionChance) : IAction {
    public IAction? PruneRedundant(MutationContext context) {
        IAction? prunedAction = Action.PruneRedundant(context);
        return prunedAction == null ? null : this;
    }
}
public record Swap(Vector2 Slot) : IAction {
    public IAction? PruneRedundant(MutationContext context) {
        (uint block, MatterState _, BlockInfo _) = context.Get(Slot);
        (uint selfBlock, MatterState _, BlockInfo _) = context.Get(Vector2.Zero);

        // if the blocks are the same, the swap is redundant
        return block == selfBlock ? null : this;
    }
}
public record Convert(List<Vector2> Slots, uint Block) : IAction {
    public IAction? PruneRedundant(MutationContext context) {
        List<Vector2> prunedSlots = [];

        foreach (Vector2 slot in Slots) {
            (uint block, MatterState matterState, BlockInfo info) = context.Get(slot);
            if (block != Block) {
                prunedSlots.Add(slot);
            }
        }

        if (prunedSlots.Count == 0) {
            return null;
        }
        if (prunedSlots.Count == Slots.Count) {
            return this;
        }
        return this with {
            Slots = prunedSlots
        };
    }
}
public record AddPollution(int Amount) : IAction {
    public IAction? PruneRedundant(MutationContext context) {
        // pollution is never redundant since it has no state
        return this;
    }
}
public record OneOf(params IAction[] Actions) : IAction {
    public IAction? PruneRedundant(MutationContext context) {
        List<IAction> prunedActions = [];

        foreach (IAction action in Actions) {
            IAction? prunedAction = action.PruneRedundant(context);
            if (prunedAction != null) {
                prunedActions.Add(prunedAction);
            }
        }

        if (prunedActions.Count == 0) {
            return null;
        }
        if (prunedActions.Count == Actions.Length) {
            return this;
        }
        return this with {
            Actions = prunedActions.ToArray()
        };
    }
}
public record AllOf(params IAction[] Actions) : IAction {
    public IAction? PruneRedundant(MutationContext context) {
        List<IAction> prunedActions = [];

        foreach (IAction action in Actions) {
            IAction? prunedAction = action.PruneRedundant(context);
            if (prunedAction != null) {
                prunedActions.Add(prunedAction);
            }
        }

        if (prunedActions.Count == 0) {
            return null;
        }
        if (prunedActions.Count == Actions.Length) {
            return this;
        }
        return this with {
            Actions = prunedActions.ToArray()
        };
    }
}
public static class Rule {
    private const double FireExtinguishSmokeFraction = 0.1;
    private const double SteamCondensationChance = 0.1;
    private const double WetTransferDownChance = 0.005;
    private const double WetSpreadAdjacentChance = 0.02;
    private const double WetSpreadAdjacentWeight = 0.1;
    private const double WetFadeChance = 0.01;

    public record Mut(IAction Action, double Chance = 1.0);

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

        bool hasWetMutation = false;

        if (CanTransferWetnessDown(ctx.Info, ctx.Get(0, -1).info))
        {
            BlockInfo below = ctx.Get(0, -1).info;

            yield return new Mut(new Chance(
                new AllOf(
                    new Convert([Vector2.Zero], ctx.Info.WithState(CommonBlockStates.Wet, false)),
                    new Convert([new Vector2(0, -1)], below.WithState(CommonBlockStates.Wet, true))
                ),
                WetTransferDownChance
            ));
            hasWetMutation = true;
        }

        foreach (Vector2 spreadOffset in GetWetSpreadOffsets())
        {
            BlockInfo spreadTarget = ctx.Get(spreadOffset).info;
            if (!CanSpreadWetnessInto(ctx.Info, spreadTarget))
            {
                continue;
            }

            yield return new Mut(
                new Chance(
                    new AllOf(
                        new Convert([Vector2.Zero], ctx.Info.WithState(CommonBlockStates.Wet, false)),
                        new Convert([spreadOffset], spreadTarget.WithState(CommonBlockStates.Wet, true))
                    ),
                    WetSpreadAdjacentChance
                ),
                WetSpreadAdjacentWeight
            );
            hasWetMutation = true;
        }

        if (CanFadeWetness(ctx))
        {
            yield return new Mut(new Chance(
                new Convert([Vector2.Zero], ctx.Info.WithState(CommonBlockStates.Wet, false)),
                WetFadeChance
            ));
            hasWetMutation = true;
        }

        if (hasWetMutation)
        {
            yield break;
        }

        if (ctx.Info.BaseBlock == Blocks.Water)
        {
            List<(BlockInfo block, Vector2 pos)> absorbableTargets = GetSurroundingBlocks(ctx, CanAbsorbWaterInto)
                .Where(it => IsCardinal(it.pos))
                .ToList();

            if (absorbableTargets.Count > 0)
            {
                yield return new Mut(new AllOf(
                    new Convert([Vector2.Zero], Blocks.Air),
                    new OneOf(absorbableTargets.Select(it =>
                        (IAction)new Convert([it.pos], it.block.WithState(CommonBlockStates.Wet, true))
                    ).ToArray())
                ));
                yield break;
            }
        }

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
                            ), 0.2)
                        ),
                        0.25
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

    private static bool CanAbsorbNeighboringWater(BlockInfo block)
    {
        return block.HasState(CommonBlockStates.Wet) && !block.GetState(CommonBlockStates.Wet);
    }

    private static bool CanAbsorbWaterInto(BlockInfo block)
    {
        return CanAbsorbNeighboringWater(block);
    }

    private static bool CanTransferWetnessDown(BlockInfo block, BlockInfo below)
    {
        return CanSpreadWetnessInto(block, below);
    }

    private static bool CanSpreadWetnessInto(BlockInfo block, BlockInfo target)
    {
        return block.HasState(CommonBlockStates.Wet)
               && block.GetState(CommonBlockStates.Wet)
               && target.HasState(CommonBlockStates.Wet)
               && !target.GetState(CommonBlockStates.Wet);
    }

    private static bool CanFadeWetness(IContext ctx)
    {
        if (!ctx.Info.HasState(CommonBlockStates.Wet))
        {
            return false;
        }

        List<(BlockInfo block, Vector2 pos)> wetApplicableNeighbors = GetSurroundingBlocks(ctx, block => block.HasState(CommonBlockStates.Wet));
        if (wetApplicableNeighbors.Count == 0)
        {
            return false;
        }

        return wetApplicableNeighbors.All(it => !it.block.GetState(CommonBlockStates.Wet));
    }

    private static IEnumerable<Vector2> GetWetSpreadOffsets()
    {
        yield return new Vector2(0, 1);
        yield return new Vector2(-1, 0);
        yield return new Vector2(1, 0);
    }

    private static bool IsCardinal(Vector2 offset)
    {
        return Math.Abs(offset.X) + Math.Abs(offset.Y) == 1;
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
