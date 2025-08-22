using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Technolize.Utils;
using Technolize.World.Block;
namespace Technolize.World.Ticking;

public class SignatureWorldTicker(TickableWorld tickableWorld)
{
    private static readonly int PaddedSize = TickableWorld.RegionSize + 2;
    private readonly ThreadLocal<ulong[,]> _signatures = new (() => new ulong[PaddedSize, PaddedSize]);
    private readonly ThreadLocal<uint[,]> _paddedRegion = new (() => new uint[PaddedSize, PaddedSize]);

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

            var signatures = _signatures.Value!;

            // reset signatures
            for (int y = 0; y < PaddedSize; y++) {
                for (int x = 0; x < PaddedSize; x++) {
                    signatures[x, y] = 0;
                }
            }

            uint[,] blocks = GetPaddedRegion(pos, region!);

            ReadOnlySpan<uint> inputSpan = MemoryMarshal.CreateReadOnlySpan(ref blocks[0, 0], PaddedSize * PaddedSize);
            Span<ulong> outputSpan = MemoryMarshal.CreateSpan(ref signatures[0, 0], PaddedSize * PaddedSize);
            SignatureProcessor.ComputeSignature(inputSpan, outputSpan, PaddedSize, PaddedSize);

            var localActions = new Dictionary<Vector2, Action>();

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
                foreach (var kvp in localActions) {
                    actions[kvp.Key] = kvp.Value;
                }
            }
        });

        // apply actions
        actions
            // .OrderBy(kvp => kvp.Key.Y + Random.Shared.NextDouble())
            .OrderBy(kvp => Random.Shared.NextDouble())
            .Select(kvp => kvp.Value)
            .ToList()
            .ForEach(action => action());
    }

    private uint[,] GetPaddedRegion(Vector2 pos, TickableWorld.Region region)
    {
        var paddedRegion = _paddedRegion.Value!;
        for (int y = 0; y < TickableWorld.RegionSize; y++)
        {
            for (int x = 0; x < TickableWorld.RegionSize; x++)
            {
                paddedRegion[x + 1, y + 1] = region.Blocks[x, y];
            }
        }

        // fill the borders from surrounding regions

        lock (tickableWorld) {
            // left
            {
                TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { X = pos.X - 1 });
                for (int y = 0; y < TickableWorld.RegionSize; y++) {
                    paddedRegion[0, y + 1] = neighbor.Blocks[TickableWorld.RegionSize - 1, y];
                }
            }

            // right
            {
                TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { X = pos.X + 1 });
                for (int y = 0; y < TickableWorld.RegionSize; y++) {
                    paddedRegion[TickableWorld.RegionSize + 1, y + 1] = neighbor.Blocks[0, y];
                }
            }

            // top
            {
                TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { Y = pos.Y - 1 });
                for (int x = 0; x < TickableWorld.RegionSize; x++) {
                    paddedRegion[x + 1, 0] = neighbor.Blocks[x, TickableWorld.RegionSize - 1];
                }
            }

            // bottom
            {
                TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { Y = pos.Y + 1 });
                for (int x = 0; x < TickableWorld.RegionSize; x++) {
                    paddedRegion[x + 1, TickableWorld.RegionSize + 1] = neighbor.Blocks[x, 0];
                }
            }
        }

        return paddedRegion;
    }

    private readonly ThreadLocal<Dictionary<ulong, List<Rule>>> _signatureRules = new(() => new Dictionary<ulong, List<Rule>>());

    private Action? ProcessSignature(ulong signature, Vector2 pos)
    {
        var signatureRules = _signatureRules.Value!;
        if (!signatureRules.TryGetValue(signature, out List<Rule>? matchedRules))
        {
            // signature not found, compute it.
            matchedRules = ComputeRules(pos);
            signatureRules[signature] = matchedRules;
        }

        if (matchedRules.Count == 0) return null;

        double chanceSum = matchedRules.Sum(p => p.Chance);

        double randomChance = Random.Shared.NextDouble() * chanceSum;
        double cumulativeChance = 0.0;
        foreach (Rule rule in matchedRules)
        {
            cumulativeChance += rule.Chance;
            if (cumulativeChance >= randomChance)
            {
                return ExecuteRuleAction(rule.Action, pos);
            }
        }

        throw new InvalidOperationException("No rule matched the signature, but we expected at least one.");
    }

    private Action ExecuteRuleAction(IAction someAction, Vector2 position)
    {
        switch (someAction)
        {
            case IAction.Convert convert:
                return () => tickableWorld.SetBlock(position + convert.Slot, convert.Block);
            case IAction.Swap swap:
                return () => tickableWorld.SwapBlocks(position, position + swap.Slot);
            case IAction.Chance chance:
                double randomValue = Random.Shared.NextDouble();
                return randomValue < chance.ActionChance ?
                    ExecuteRuleAction(chance.Action, position) :
                    () => {
                        // we need to make sure this block gets ticked next tick if the chance fails.
                        var (regionPos, localPos) = Coords.WorldToRegionCoords(position);
                        tickableWorld.Regions[regionPos]!.RequireTick((int)localPos.X, (int)localPos.Y);
                    };
            case IAction.OneOf oneOf:
                IAction action = oneOf.Actions[Random.Shared.Next(oneOf.Actions.Length)];
                return ExecuteRuleAction(action, position);
            case IAction.AllOf allOf:
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

    private List<Rule> ComputeRules(Vector2 pos) {
        List<Rule> matchedRules = [];

        foreach (Rule localRule in Rule.GetRules()) {
            bool matches = true;
            foreach (var localRuleSlot in localRule.Slots) {
                uint blockId = (uint) tickableWorld.GetBlock(pos + localRuleSlot.Key);
                if (!localRuleSlot.Value.Contains(blockId)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                matchedRules.Add(localRule);
            }
        }

        if (matchedRules.Count == 0) {
            return matchedRules;
        }

        // only use the lowest priority rules
        int minPriority = matchedRules.Count > 0 ? matchedRules.Min(p => p.Priority) : int.MaxValue;
        return matchedRules.Where(p => p.Priority == minPriority).ToList();
    }
}
