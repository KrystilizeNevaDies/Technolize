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
    private readonly uint[,] _signatures = new uint[PaddedSize, PaddedSize];
    private readonly uint[,] _paddedRegion = new uint[PaddedSize, PaddedSize];

    public void Tick() {
        LocalPattern[] patterns = LocalPattern.GetPatterns().ToArray();
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

            // if the region is below y = 0, don't process it.
            if (pos.Y < 0) {
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
            Span<uint> outputSpan = MemoryMarshal.CreateSpan(ref _signatures[0, 0], PaddedSize * PaddedSize);
            SignatureProcessor.ComputeSignature(inputSpan, outputSpan, PaddedSize, PaddedSize);

            for (int y = 0; y < CpuWorld.RegionSize; y++)
            {
                for (int x = 0; x < CpuWorld.RegionSize; x++)
                {
                    uint signature = _signatures[x + 1, y + 1];
                    Action? matchedPattern = ProcessSignature(signature, new Vector2(x, y) + pos * CpuWorld.RegionSize, patterns);
                    if (matchedPattern != null)
                    {
                        actions[new Vector2(x, y) + pos * CpuWorld.RegionSize] = matchedPattern;
                    }
                }
            }
        }

        // apply actions
        var ordered = actions
            .OrderBy(kvp => kvp.Key.Y)
            .ThenBy(_ => Random.Shared.NextSingle() - 0.5f) // Randomize same-priority action order
            .ToList();

        // Parallel.ForEach(ordered, kvp =>
        // {
        //     kvp.Value();
        // });
        foreach (var kvp in ordered)
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
            if (world.Regions.TryGetValue(pos with { X = pos.X - 1 }, out var neighbor)) {
                for (int y = 0; y < CpuWorld.RegionSize; y++)
                {
                    _paddedRegion[0, y + 1] = neighbor.Blocks[CpuWorld.RegionSize - 1, y];
                }
            }
        }

        // right
        {
            if (world.Regions.TryGetValue(pos with { X = pos.X + 1 }, out var neighbor)) {
                for (int y = 0; y < CpuWorld.RegionSize; y++)
                {
                    _paddedRegion[CpuWorld.RegionSize + 1, y + 1] = neighbor.Blocks[0, y];
                }
            }
        }

        // top
        {
            if (world.Regions.TryGetValue(pos with { Y = pos.Y - 1 }, out var neighbor)) {
                for (int x = 0; x < CpuWorld.RegionSize; x++)
                {
                    _paddedRegion[x + 1, 0] = neighbor.Blocks[x, CpuWorld.RegionSize - 1];
                }
            }
        }

        // bottom
        {
            if (world.Regions.TryGetValue(pos with { Y = pos.Y + 1 }, out var neighbor)) {
                for (int x = 0; x < CpuWorld.RegionSize; x++)
                {
                    _paddedRegion[x + 1, CpuWorld.RegionSize + 1] = neighbor.Blocks[x, 0];
                }
            }
        }

        return _paddedRegion;
    }

    private readonly Dictionary<uint, List<LocalPattern>> _signaturePatterns = new();

    private Action? ProcessSignature(uint signature, Vector2 pos, LocalPattern[] patterns)
    {
        if (!_signaturePatterns.TryGetValue(signature, out List<LocalPattern>? matchedPatterns))
        {
            // signature not found, compute it.
            matchedPatterns = ComputePatterns(pos, patterns);
            _signaturePatterns[signature] = matchedPatterns;
            Console.WriteLine($"Computed {matchedPatterns.Count} patterns for signature {signature}");
        }

        if (matchedPatterns.Count == 0) return null;

        int minPriority = matchedPatterns.Min(p => p.Priority);
        LocalPattern? randomPattern = matchedPatterns.Where(p => p.Priority == minPriority)
            .OrderBy(_ => Random.Shared.Next())
            .FirstOrDefault();

        return randomPattern == null ? null : ExecutePatternAction(randomPattern.Action, pos);
    }

    private Action ExecutePatternAction(ILocalPatternAction patternAction, Vector2 position)
    {
        switch (patternAction)
        {
            case ILocalPatternAction.Convert convert:
                return () => world.SetBlock(position + convert.Slot, convert.Block);
            case ILocalPatternAction.Swap swap:
                return () => world.SwapBlocks(position, position + swap.Slot);
            case ILocalPatternAction.OneOf oneOf:
                ILocalPatternAction action = oneOf.Actions[Random.Shared.Next(oneOf.Actions.Length)];
                return ExecutePatternAction(action, position);
            case ILocalPatternAction.AllOf allOf:
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

    private List<LocalPattern> ComputePatterns(Vector2 pos, LocalPattern[] patterns) {
        List<LocalPattern> matchedPatterns = [];

        foreach (LocalPattern localPattern in patterns) {
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
        .Select(block => (uint)block.Id)
        .ToFrozenSet();

    public static readonly ISet<uint> Liquid = Blocks.AllBlocks()
        .Where(block => block.MatterState == MatterState.Liquid)
        .Select(block => (uint)block.Id)
        .ToFrozenSet();
}

public interface ILocalPatternAction
{
    public record Swap(Vector2 Slot) : ILocalPatternAction;
    public record Convert(Vector2 Slot, uint Block) : ILocalPatternAction;
    public record OneOf(params ILocalPatternAction[] Actions) : ILocalPatternAction;
    public record AllOf(params ILocalPatternAction[] Actions) : ILocalPatternAction;
}

public record LocalPattern(Dictionary<Vector2, ISet<uint>> Slots, ILocalPatternAction Action, int Priority)
{
    public static IEnumerable<LocalPattern> GetPatterns()
    {
        {
            yield return new LocalPattern(
                new Dictionary<Vector2, ISet<uint>>
                {
                    { new Vector2(0, 0), BlockTypes.Powder },
                    { new Vector2(0, -1), FrozenSet.Create((uint)Blocks.Air.Id) }
                },
                new ILocalPatternAction.Swap(new Vector2(0, -1)),
                0
            );

            // yield return new LocalPattern(
            //     new Dictionary<Vector2, ISet<int>>
            //     {
            //         { new Vector2(0, 0), BlockTypes.Liquid },
            //         { new Vector2(0, -1), FrozenSet.Create(Blocks.Air.Id) }
            //     },
            //     new ILocalPatternAction.Swap(new Vector2(0, -1)),
            //     0
            // );
            //
            // yield return new LocalPattern(
            //     new Dictionary<Vector2, ISet<int>>
            //     {
            //         { new Vector2(0, 0), BlockTypes.Powder },
            //         { new Vector2(0, -1), BlockTypes.Liquid }
            //     },
            //     new ILocalPatternAction.Swap(new Vector2(0, -1)),
            //     0
            // );
        }

        // {
        //     yield return new LocalPattern(
        //         new Dictionary<Vector2, ISet<int>>
        //         {
        //             { new Vector2(0, 0), BlockTypes.Powder },
        //             { new Vector2(-1, -1), Slot.Air }
        //         },
        //         new ILocalPatternAction.Swap(new Vector2(-1, -1)),
        //         1
        //     );
        //
        //     yield return new LocalPattern(
        //         new Dictionary<Vector2, ISet<int>>
        //         {
        //             { new Vector2(0, 0), BlockTypes.Powder },
        //             { new Vector2(1, -1), Slot.Air }
        //         },
        //         new ILocalPatternAction.Swap(new Vector2(1, -1)),
        //         1
        //     );
        //
        //     yield return new LocalPattern(
        //         new Dictionary<Vector2, ISet<int>>
        //         {
        //             { new Vector2(0, 0), BlockTypes.Liquid },
        //             { new Vector2(-1, -1), Slot.Air }
        //         },
        //         new ILocalPatternAction.Swap(new Vector2(-1, -1)),
        //         1
        //     );
        //
        //     yield return new LocalPattern(
        //         new Dictionary<Vector2, ISet<int>>
        //         {
        //             { new Vector2(0, 0), BlockTypes.Liquid },
        //             { new Vector2(1, -1), Slot.Air }
        //         },
        //         new ILocalPatternAction.Swap(new Vector2(1, -1)),
        //         1
        //     );
        //
        //     yield return new LocalPattern(
        //         new Dictionary<Vector2, ISet<int>>
        //         {
        //             { new Vector2(-1, 1), Slot.Air },
        //             { new Vector2(0, 0), BlockTypes.Liquid },
        //             { new Vector2(-1, 0), Slot.Air }
        //         },
        //         new ILocalPatternAction.Swap(new Vector2(-1, 0)),
        //         1
        //     );
        //
        //     yield return new LocalPattern(
        //         new Dictionary<Vector2, ISet<int>>
        //         {
        //             { new Vector2(1, 1), Slot.Air },
        //             { new Vector2(0, 0), BlockTypes.Liquid },
        //             { new Vector2(1, 0), Slot.Air }
        //         },
        //         new ILocalPatternAction.Swap(new Vector2(1, 0)),
        //         1
        //     );
        // }
        //
        // yield return new LocalPattern(
        //     new Dictionary<Vector2, ISet<int>>
        //     {
        //         {
        //             new Vector2(0, 0), Slot.Liquid
        //         },
        //         {
        //             new Vector2(0, -1), Slot.Solid
        //         }
        //     },
        //     new ILocalPatternAction.AllOf(
        //         new ILocalPatternAction.Convert(new Vector2(0, 0), Blocks.Sand),
        //         new ILocalPatternAction.Convert(new Vector2(0, -1), Blocks.Sand)
        //     ),
        //     0
        // );
    }
}
