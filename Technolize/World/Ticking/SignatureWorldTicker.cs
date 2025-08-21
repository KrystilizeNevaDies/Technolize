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
    private readonly ulong[,] _signatures = new ulong[PaddedSize, PaddedSize];
    private readonly uint[,] _paddedRegion = new uint[PaddedSize, PaddedSize];

    public void Tick() {
        Rule[] patterns = Rule.GetRules().ToArray();
        Dictionary<Vector2, Action> actions = new ();

        // compute and process signatures
        foreach (Vector2 pos in tickableWorld.UseNeedsTick()) {
            if (!tickableWorld.Regions.TryGetValue(pos, out TickableWorld.Region? region))
            {
                // If the region is not loaded, skip processing
                continue;
            }

            // if the region is below y = 0, clear the region, and don't process it.
            if (pos.Y < 0) {
                actions.Add(pos * TickableWorld.RegionSize, () => {
                    region.Clear();
                });
                continue;
            }

            // reset signatures
            for (int y = 0; y < PaddedSize; y++)
            {
                for (int x = 0; x < PaddedSize; x++)
                {
                    _signatures[x, y] = 0;
                }
            }

            uint[,] blocks = GetPaddedRegion(pos, region);

            ReadOnlySpan<uint> inputSpan = MemoryMarshal.CreateReadOnlySpan(ref blocks[0, 0], PaddedSize * PaddedSize);
            Span<ulong> outputSpan = MemoryMarshal.CreateSpan(ref _signatures[0, 0], PaddedSize * PaddedSize);
            SignatureProcessor.ComputeSignature(inputSpan, outputSpan, PaddedSize, PaddedSize);

            for (int y = 0; y < TickableWorld.RegionSize; y++)
            {
                for (int x = 0; x < TickableWorld.RegionSize; x++)
                {
                    ulong signature = _signatures[x + 1, y + 1];
                    Action? matchedPattern = ProcessSignature(signature, new Vector2(x, y) + pos * TickableWorld.RegionSize, patterns);
                    if (matchedPattern != null)
                    {
                        actions[new Vector2(x, y) + pos * TickableWorld.RegionSize] = matchedPattern;
                    }
                }
            }
        }

        // apply actions
        IOrderedEnumerable<KeyValuePair<Vector2, Action>> ordered = actions
            .OrderBy(kvp => kvp.Key.Y)
            .ThenBy(_ => Random.Shared.NextSingle() - 0.5f) // Randomize same-priority action order
            ;

        foreach (KeyValuePair<Vector2, Action> kvp in ordered)
        {
            kvp.Value();
        }
    }

    private uint[,] GetPaddedRegion(Vector2 pos, TickableWorld.Region region)
    {
        for (int y = 0; y < TickableWorld.RegionSize; y++)
        {
            for (int x = 0; x < TickableWorld.RegionSize; x++)
            {
                _paddedRegion[x + 1, y + 1] = region.Blocks[x, y];
            }
        }

        // fill the borders from surrounding regions

        // left
        {
            TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { X = pos.X - 1 });
            for (int y = 0; y < TickableWorld.RegionSize; y++) {
                _paddedRegion[0, y + 1] = neighbor.Blocks[TickableWorld.RegionSize - 1, y];
            }
        }

        // right
        {
            TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { X = pos.X + 1 });
            for (int y = 0; y < TickableWorld.RegionSize; y++) {
                _paddedRegion[TickableWorld.RegionSize + 1, y + 1] = neighbor.Blocks[0, y];
            }
        }

        // top
        {
            TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { Y = pos.Y - 1 });
            for (int x = 0; x < TickableWorld.RegionSize; x++) {
                _paddedRegion[x + 1, 0] = neighbor.Blocks[x, TickableWorld.RegionSize - 1];
            }
        }

        // bottom
        {
            TickableWorld.Region neighbor = tickableWorld.GetRegion(pos with { Y = pos.Y + 1 });
            for (int x = 0; x < TickableWorld.RegionSize; x++) {
                _paddedRegion[x + 1, TickableWorld.RegionSize + 1] = neighbor.Blocks[x, 0];
            }
        }

        return _paddedRegion;
    }

    private readonly Dictionary<ulong, List<Rule>> _signaturePatterns = new();

    private Action? ProcessSignature(ulong signature, Vector2 pos, Rule[] patterns)
    {
        if (!_signaturePatterns.TryGetValue(signature, out List<Rule>? matchedPatterns))
        {
            // signature not found, compute it.
            matchedPatterns = ComputePatterns(pos, patterns);
            _signaturePatterns[signature] = matchedPatterns;
        }

        if (matchedPatterns.Count == 0) return null;

        int index = Random.Shared.Next(matchedPatterns.Count);
        Rule randomRule = matchedPatterns[index];

        return ExecutePatternAction(randomRule.Action, pos);
    }

    private Action ExecutePatternAction(IAction someAction, Vector2 position)
    {
        switch (someAction)
        {
            case IAction.Convert convert:
                return () => tickableWorld.SetBlock(position + convert.Slot, convert.Block);
            case IAction.Swap swap:
                return () => tickableWorld.SwapBlocks(position, position + swap.Slot);
            case IAction.OneOf oneOf:
                IAction action = oneOf.Actions[Random.Shared.Next(oneOf.Actions.Length)];
                return ExecutePatternAction(action, position);
            case IAction.AllOf allOf:
                Action[] actions = allOf.Actions
                    .Select(action => ExecutePatternAction(action, position))
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

    private List<Rule> ComputePatterns(Vector2 pos, Rule[] patterns) {
        List<Rule> matchedPatterns = [];

        foreach (Rule localPattern in patterns) {
            bool matches = true;
            foreach (var localPatternSlot in localPattern.Slots) {
                uint blockId = (uint) tickableWorld.GetBlock(pos + localPatternSlot.Key);
                if (!localPatternSlot.Value.Contains(blockId)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                matchedPatterns.Add(localPattern);
            }
        }

        if (matchedPatterns.Count == 0) {
            return matchedPatterns;
        }

        // only use the lowest priority patterns
        int minPriority = matchedPatterns.Count > 0 ? matchedPatterns.Min(p => p.Priority) : int.MaxValue;
        return matchedPatterns.Where(p => p.Priority == minPriority).ToList();
    }
}
