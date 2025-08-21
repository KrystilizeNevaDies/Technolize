using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Technolize.Utils;
using Technolize.World.Block;
namespace Technolize.World.Ticking;

public class HashSignatureWorldTicker(CpuWorld world)
{
    private static readonly int PaddedSize = CpuWorld.RegionSize + 2;
    private readonly ulong[,] _signatures = new ulong[PaddedSize, PaddedSize];
    private readonly uint[,] _paddedRegion = new uint[PaddedSize, PaddedSize];

    public void Tick() {
        Pattern[] patterns = Pattern.GetPatterns().ToArray();
        Dictionary<Vector2, Action> actions = new ();

        // compute and process signatures
        foreach (Vector2 pos in world.UseNeedsTick()) {
            if (!world.Regions.TryGetValue(pos, out CpuWorld.Region? region))
            {
                // If the region is not loaded, skip processing
                continue;
            }

            // if the region is loaded, but is empty, remove it.
            if (region!.IsEmpty)
            {
                actions.Add(pos * CpuWorld.RegionSize, () => {
                    world.Regions.Remove(pos);
                });
                continue;
            }

            // if the region is below y = 0, clear the region, and don't process it.
            if (pos.Y < 0) {
                actions.Add(pos * CpuWorld.RegionSize, () => {
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

            for (int y = 0; y < CpuWorld.RegionSize; y++)
            {
                for (int x = 0; x < CpuWorld.RegionSize; x++)
                {
                    ulong signature = _signatures[x + 1, y + 1];
                    Action? matchedPattern = ProcessSignature(signature, new Vector2(x, y) + pos * CpuWorld.RegionSize, patterns);
                    if (matchedPattern != null)
                    {
                        actions[new Vector2(x, y) + pos * CpuWorld.RegionSize] = matchedPattern;
                    }
                }
            }
        }

        // apply actions
        IOrderedEnumerable<KeyValuePair<Vector2, Action>> ordered = actions
            .OrderBy(kvp => kvp.Key.Y)
            .ThenBy(_ => Random.Shared.NextSingle() - 0.5f); // Randomize same-priority action order

        foreach (KeyValuePair<Vector2, Action> kvp in ordered)
        {
            kvp.Value();
        }
    }

    private uint[,] GetPaddedRegion(Vector2 pos, CpuWorld.Region region)
    {
        for (int y = 0; y < CpuWorld.RegionSize; y++)
        {
            for (int x = 0; x < CpuWorld.RegionSize; x++)
            {
                _paddedRegion[x + 1, y + 1] = region.Blocks[x, y];
            }
        }

        // fill the borders from surrounding regions

        // left
        {
            if (world.Regions.TryGetValue(pos with { X = pos.X - 1 }, out CpuWorld.Region? neighbor)) {
                for (int y = 0; y < CpuWorld.RegionSize; y++)
                {
                    _paddedRegion[0, y + 1] = neighbor.Blocks[CpuWorld.RegionSize - 1, y];
                }
            }
        }

        // right
        {
            if (world.Regions.TryGetValue(pos with { X = pos.X + 1 }, out CpuWorld.Region? neighbor)) {
                for (int y = 0; y < CpuWorld.RegionSize; y++)
                {
                    _paddedRegion[CpuWorld.RegionSize + 1, y + 1] = neighbor.Blocks[0, y];
                }
            }
        }

        // top
        {
            if (world.Regions.TryGetValue(pos with { Y = pos.Y - 1 }, out CpuWorld.Region? neighbor)) {
                for (int x = 0; x < CpuWorld.RegionSize; x++)
                {
                    _paddedRegion[x + 1, 0] = neighbor.Blocks[x, CpuWorld.RegionSize - 1];
                }
            }
        }

        // bottom
        {
            if (world.Regions.TryGetValue(pos with { Y = pos.Y + 1 }, out CpuWorld.Region? neighbor)) {
                for (int x = 0; x < CpuWorld.RegionSize; x++)
                {
                    _paddedRegion[x + 1, CpuWorld.RegionSize + 1] = neighbor.Blocks[x, 0];
                }
            }
        }

        return _paddedRegion;
    }

    private readonly Dictionary<ulong, List<Pattern>> _signaturePatterns = new();

    private Action? ProcessSignature(ulong signature, Vector2 pos, Pattern[] patterns)
    {
        if (!_signaturePatterns.TryGetValue(signature, out List<Pattern>? matchedPatterns))
        {
            // signature not found, compute it.
            matchedPatterns = ComputePatterns(pos, patterns);
            _signaturePatterns[signature] = matchedPatterns;
        }

        if (matchedPatterns.Count == 0) return null;

        int minPriority = matchedPatterns.Min(p => p.Priority);
        Pattern? randomPattern = matchedPatterns.Where(p => p.Priority == minPriority)
            .OrderBy(_ => Random.Shared.Next())
            .FirstOrDefault();

        return randomPattern == null ? null : ExecutePatternAction(randomPattern.Action, pos);
    }

    private Action ExecutePatternAction(IAction someAction, Vector2 position)
    {
        switch (someAction)
        {
            case IAction.Convert convert:
                return () => world.SetBlock(position + convert.Slot, convert.Block);
            case IAction.Swap swap:
                return () => world.SwapBlocks(position, position + swap.Slot);
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

    private List<Pattern> ComputePatterns(Vector2 pos, Pattern[] patterns) {
        List<Pattern> matchedPatterns = [];

        foreach (Pattern localPattern in patterns) {
            bool matches = true;
            foreach (KeyValuePair<Vector2, ISet<uint>> localPatternSlot in localPattern.Slots) {
                uint blockId = (uint) world.GetBlock(pos + localPatternSlot.Key);
                if (!localPatternSlot.Value.Contains(blockId)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                matchedPatterns.Add(localPattern);
            }
        }

        return matchedPatterns;
    }
}

static class BlockTypes {
    public static readonly ISet<uint> Powder = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Powder)
        .Select(block => block.Id)
        .ToFrozenSet();

    public static readonly ISet<uint> Liquid = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Liquid)
        .Select(block => block.Id)
        .ToFrozenSet();

    public static readonly ISet<uint> Solid = Blocks.AllBlocks()
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
static class SetUtils {
    public static ISet<uint> With(this ISet<uint> set, ISet<uint> other) {
        return set.Union(other).ToFrozenSet();
    }
}

public record Pattern(Dictionary<Vector2, ISet<uint>> Slots, IAction Action, int Priority)
{

    public static IEnumerable<Pattern> GetPatterns()
    {
        ISet<uint> air = FrozenSet.Create(Blocks.Air.Id);
        ISet<uint> airOrLiquid = air.With(BlockTypes.Liquid);

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
                {
                    new (0, 0), BlockTypes.Liquid
                },
                {
                    new (0, -1), BlockTypes.Solid
                }
            },
            new IAction.AllOf(
                new IAction.Convert(new (0, 0), Blocks.Sand.Id),
                new IAction.Convert(new (0, -1), Blocks.Sand.Id)
            ),
            0
        );
    }
}
