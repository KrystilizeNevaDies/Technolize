using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Technolize.Utils;
using Technolize.World.Block;
namespace Technolize.World.Ticking;

public unsafe class SignatureWorldTicker(TickableWorld tickableWorld)
{
    private static readonly int PaddedSize = TickableWorld.RegionSize + 2;
    private readonly ThreadLocal<ulong[,]> _signatures = new (() => new ulong[PaddedSize, PaddedSize]);
    private readonly ThreadLocal<uint[,]> _paddedRegion = new (() => new uint[PaddedSize, PaddedSize]);
    
    // Pool for local actions dictionaries to reduce allocations
    private readonly ThreadLocal<Dictionary<Vector2, Action>> _localActionsPool = new(() => new Dictionary<Vector2, Action>());

    /// <summary>
    /// Ticks the world once. Iterate through the returned IEnumerable to complete processing the tick.
    /// </summary>
    /// <returns>IEnumerable to progress the tick in a coroutine. Useful for time-slicing.</returns>
    public void Tick() {
        Dictionary<Vector2, Action> actions = new ();

        // compute and process signatures
        // var tasks = tickableWorld.UseNeedsTick().Select(pos => Task.Run(() => {
        // foreach (Vector2 pos in tickableWorld.UseNeedsTick()) {
        Parallel.ForEach(tickableWorld.UseNeedsTick(), pos => {
            if (!tickableWorld.Regions.TryGetValue(pos, out TickableWorld.Region? region)) {
                // If the region is not loaded, skip processing
                return;
            }

            // if the region is below y = 0, clear the region, and don't process it.
            if (pos.Y < 0) {
                lock (actions) {
                    actions.Add(pos * TickableWorld.RegionSize, () => {
                        region!.Clear();
                    });
                }
                return;
            }

            ulong[,] signatures = _signatures.Value!;

            // reset signatures - optimized with unsafe memory operations
            unsafe
            {
                fixed (ulong* ptr = &signatures[0, 0])
                {
                    var span = new Span<ulong>(ptr, PaddedSize * PaddedSize);
                    span.Clear();
                }
            }

            uint[,] blocks = GetPaddedRegion(pos, region!);

            ReadOnlySpan<uint> inputSpan = MemoryMarshal.CreateReadOnlySpan(ref blocks[0, 0], PaddedSize * PaddedSize);
            Span<ulong> outputSpan = MemoryMarshal.CreateSpan(ref signatures[0, 0], PaddedSize * PaddedSize);
            SignatureProcessor.ComputeSignature(inputSpan, outputSpan, PaddedSize, PaddedSize);

            Dictionary<Vector2, Action> localActions = _localActionsPool.Value!;
            localActions.Clear(); // Reuse existing dictionary

            for (int y = 0; y < TickableWorld.RegionSize; y++) {
                for (int x = 0; x < TickableWorld.RegionSize; x++) {
                    ulong signature = signatures[x + 1, y + 1];
                    Action? matchedRule = ProcessSignature(signature, new Vector2(x, y) + pos * TickableWorld.RegionSize);
                    if (matchedRule != null) {
                        localActions[new Vector2(x, y) + pos * TickableWorld.RegionSize] = matchedRule;
                    }
                }
            }

            lock (actions) {
                // merge local actions into the global actions dictionary
                // optimized: use EnsureCapacity if available and iterate more efficiently
                if (localActions.Count > 0) {
                    foreach (var kvp in localActions) {
                        actions[kvp.Key] = kvp.Value;
                    }
                }
            }
        });

        // apply actions - optimized execution without expensive LINQ
        if (actions.Count > 0) {
            // Convert to array once and shuffle in-place for better performance
            var actionArray = new Action[actions.Count];
            int index = 0;
            foreach (var kvp in actions) {
                actionArray[index++] = kvp.Value;
            }
            
            // Fisher-Yates shuffle for randomization without allocating multiple collections
            for (int i = actionArray.Length - 1; i > 0; i--) {
                int j = Random.Shared.Next(i + 1);
                (actionArray[i], actionArray[j]) = (actionArray[j], actionArray[i]);
            }
            
            // Execute actions directly
            foreach (var action in actionArray) {
                action();
            }
        }
    }

    private uint[,] GetPaddedRegion(Vector2 pos, TickableWorld.Region region)
    {
        uint[,] paddedRegion = _paddedRegion.Value!;
        
        // Copy center region data with optimized memory access
        unsafe
        {
            fixed (uint* pPadded = &paddedRegion[1, 1], pSource = &region.Blocks[0, 0])
            {
                for (int y = 0; y < TickableWorld.RegionSize; y++)
                {
                    uint* srcRow = pSource + y * TickableWorld.RegionSize;
                    uint* dstRow = pPadded + y * PaddedSize;
                    for (int x = 0; x < TickableWorld.RegionSize; x++)
                    {
                        dstRow[x] = srcRow[x];
                    }
                }
            }
        }

        // Pre-fetch neighbor regions to minimize lock time
        TickableWorld.Region leftNeighbor, rightNeighbor, topNeighbor, bottomNeighbor;
        lock (tickableWorld) {
            leftNeighbor = tickableWorld.GetRegion(pos with { X = pos.X - 1 });
            rightNeighbor = tickableWorld.GetRegion(pos with { X = pos.X + 1 });
            topNeighbor = tickableWorld.GetRegion(pos with { Y = pos.Y - 1 });
            bottomNeighbor = tickableWorld.GetRegion(pos with { Y = pos.Y + 1 });
        }

        // Fill borders without locks - improved memory access patterns
        // left border
        for (int y = 0; y < TickableWorld.RegionSize; y++) {
            paddedRegion[0, y + 1] = leftNeighbor.Blocks[TickableWorld.RegionSize - 1, y];
        }

        // right border
        for (int y = 0; y < TickableWorld.RegionSize; y++) {
            paddedRegion[TickableWorld.RegionSize + 1, y + 1] = rightNeighbor.Blocks[0, y];
        }

        // top border
        for (int x = 0; x < TickableWorld.RegionSize; x++) {
            paddedRegion[x + 1, 0] = topNeighbor.Blocks[x, TickableWorld.RegionSize - 1];
        }

        // bottom border
        for (int x = 0; x < TickableWorld.RegionSize; x++) {
            paddedRegion[x + 1, TickableWorld.RegionSize + 1] = bottomNeighbor.Blocks[x, 0];
        }

        return paddedRegion;
    }

    private readonly ThreadLocal<Dictionary<ulong, List<Rule.Mut>>> _signatureRules = new(() => new Dictionary<ulong, List<Rule.Mut>>());

    private Action? ProcessSignature(ulong signature, Vector2 pos)
    {
        Dictionary<ulong, List<Rule.Mut>>? signatureRules = _signatureRules.Value!;
        if (!signatureRules.TryGetValue(signature, out List<Rule.Mut>? mutations))
        {
            // signature not found, compute it.
            mutations = ComputeMutations(pos);
            signatureRules[signature] = mutations;
        }

        if (mutations.Count == 0) return null;

        double chanceSum = mutations.Sum(p => p.chance);

        double randomChance = Random.Shared.NextDouble() * chanceSum;
        double cumulativeChance = 0.0;
        foreach (Rule.Mut mut in mutations)
        {
            cumulativeChance += mut.chance;
            if (cumulativeChance >= randomChance)
            {
                return ExecuteRuleAction(mut.action, pos);
            }
        }

        throw new InvalidOperationException("No rule matched the signature, but we expected at least one.");
    }

    private Action ExecuteRuleAction(IAction someAction, Vector2 position)
    {
        switch (someAction)
        {
            case Convert convert:
                return () => {
                    foreach (Vector2 slot in convert.Slots) {
                        tickableWorld.SetBlock(position + slot, convert.Block);
                    }
                };
            case Swap swap:
                return () => tickableWorld.SwapBlocks(position, position + swap.Slot);
            case Chance chance:
                double randomValue = Random.Shared.NextDouble();
                return randomValue < chance.ActionChance ?
                    ExecuteRuleAction(chance.Action, position) :
                    () => {
                        // we need to make sure this block gets ticked next tick if the chance fails.
                        (Vector2 regionPos, Vector2 localPos) = Coords.WorldToRegionCoords(position);
                        tickableWorld.Regions[regionPos]!.RequireTick((int)localPos.X, (int)localPos.Y);
                    };
            case OneOf oneOf:
                IAction action = oneOf.Actions[Random.Shared.Next(oneOf.Actions.Length)];
                return ExecuteRuleAction(action, position);
            case AllOf allOf:
                Action[] actions = allOf.Actions
                    .Select(action => ExecuteRuleAction(action, position))
                    .ToArray();
                return () =>
                {
                    foreach (Action action in actions)
                    {
                        action();
                    }
                };
        }
        throw new NotImplementedException();
    }

    private List<Rule.Mut> ComputeMutations(Vector2 pos) {
        MutationContext context = new (pos, tickableWorld);
        return Rule.CalculateMutations(context).ToList();
    }
}

record MutationContext(Vector2 Position, TickableWorld World) : Rule.IContext {
    public BlockInfo Info => BlockRegistry.GetInfo(World.GetBlock(Position));

    public (uint block, MatterState matterState, BlockInfo info) Get(int x, int y) {
        if (x < -1 || x > 1 || y < -1 || y > 1) {
            throw new ArgumentOutOfRangeException($"Get() only supports x/y values of -1, 0, or 1. Got x={x}, y={y}");
        }
        Vector2 pos = Position + new Vector2(x, y);
        BlockInfo info = BlockRegistry.GetInfo(World.GetBlock(pos));
        return (info.id, MatterState: info.GetTag(BlockInfo.TagMatterState), info);
    }
}
