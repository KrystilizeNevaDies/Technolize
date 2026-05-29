using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Technolize.Utils;
using Technolize.World.Block;
namespace Technolize.World.Ticking;

public sealed record TickCycleTimings(
    int ActiveRegionCount,
    double RegionSelectionMs,
    double ParallelPhaseMs,
    double ActionPreparationMs,
    double ActionExecutionMs,
    double TotalMs,
    double WorkerRegionPaddingMs,
    double WorkerSignatureComputationMs,
    double WorkerRuleMatchingMs,
    double WorkerActionMergeMs)
{
    public double WorkerAccumulatedMs => WorkerRegionPaddingMs + WorkerSignatureComputationMs + WorkerRuleMatchingMs + WorkerActionMergeMs;
    public double EstimatedParallelism => ParallelPhaseMs <= 0.0 ? 0.0 : WorkerAccumulatedMs / ParallelPhaseMs;
}

public delegate void ExecuteAction(bool useLocks);

internal sealed class CompiledSignaturePlan
{
    private static readonly CompiledSignaturePlan Empty = new([], 0.0);

    private readonly CompiledMutation[] _mutations;

    public bool IsEmpty => _mutations.Length == 0;
    public double TotalChance { get; }

    private CompiledSignaturePlan(CompiledMutation[] mutations, double totalChance)
    {
        _mutations = mutations;
        TotalChance = totalChance;
    }

    public static CompiledSignaturePlan Compile(List<Rule.Mut> mutations)
    {
        if (mutations.Count == 0)
        {
            return Empty;
        }

        CompiledMutation[] compiledMutations = new CompiledMutation[mutations.Count];
        double cumulativeChance = 0.0;
        for (int i = 0; i < mutations.Count; i++)
        {
            Rule.Mut mutation = mutations[i];
            cumulativeChance += mutation.Chance;
            compiledMutations[i] = new CompiledMutation(cumulativeChance, CompileAction(mutation.Action));
        }

        return new CompiledSignaturePlan(compiledMutations, cumulativeChance);
    }

    public CompiledAction SelectAction(double randomChance)
    {
        int low = 0;
        int high = _mutations.Length - 1;
        while (low < high)
        {
            int mid = low + ((high - low) / 2);
            if (_mutations[mid].CumulativeChance >= randomChance)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return _mutations[low].Action;
    }

    private static CompiledAction CompileAction(IAction action)
    {
        return action switch
        {
            Convert convert => new CompiledConvertAction(convert.Slots.ToArray(), convert.Block),
            AddPollution addPollution => new CompiledAddPollutionAction(addPollution.Amount),
            Swap swap => new CompiledSwapAction(swap.Slot),
            Chance chance => new CompiledChanceAction(CompileAction(chance.Action), chance.ActionChance),
            OneOf oneOf => new CompiledOneOfAction(oneOf.Actions.Select(CompileAction).ToArray()),
            AllOf allOf => new CompiledAllOfAction(allOf.Actions.Select(CompileAction).ToArray()),
            _ => throw new NotImplementedException($"Unsupported action type '{action.GetType().Name}'.")
        };
    }

    private sealed record CompiledMutation(double CumulativeChance, CompiledAction Action);
}

internal abstract record CompiledAction;
internal sealed record CompiledConvertAction(Vector2[] Slots, uint Block) : CompiledAction;
internal sealed record CompiledAddPollutionAction(int Amount) : CompiledAction;
internal sealed record CompiledSwapAction(Vector2 Slot) : CompiledAction;
internal sealed record CompiledChanceAction(CompiledAction Action, double ActionChance) : CompiledAction;
internal sealed record CompiledOneOfAction(CompiledAction[] Actions) : CompiledAction;
internal sealed record CompiledAllOfAction(CompiledAction[] Actions) : CompiledAction;

public unsafe class SignatureWorldTicker(TickableWorld tickableWorld) : IDisposable
{
    private static readonly int PaddedSize = TickableWorld.RegionSize + 2;
    private readonly ThreadLocal<ulong[,]> _signatures = new (() => new ulong[PaddedSize, PaddedSize]);
    private readonly ThreadLocal<uint[,]> _paddedRegion = new (() => new uint[PaddedSize, PaddedSize]);

    // Pool for local actions dictionaries to reduce allocations
    private readonly ThreadLocal<Dictionary<ulong, CompiledSignaturePlan>> _signatureRules = new(() => new Dictionary<ulong, CompiledSignaturePlan>());

    public bool ManualTimingsEnabled { get; set; }
    public TickCycleTimings? LastTickTimings { get; private set; }

    // simple thread pool scheduler with a degree of parallelism equal to the number of processor cores
    private TaskScheduler _tickScheduler = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default,  Environment.ProcessorCount).ConcurrentScheduler;

    /// <summary>
    /// Ticks the world once. Iterate through the returned IEnumerable to complete processing the tick.
    /// </summary>
    /// <returns>The number of active regions processed in this tick.</returns>
    public int Tick(bool fullScan = false) {
        bool measureTimings = ManualTimingsEnabled;
        long totalStart = measureTimings ? Stopwatch.GetTimestamp() : 0L;
        long regionSelectionStart = measureTimings ? Stopwatch.GetTimestamp() : 0L;
        Vector2[] regionsToTick = GetRegionsToTick(fullScan);
        long regionSelectionTicks = measureTimings ? Stopwatch.GetTimestamp() - regionSelectionStart : 0L;

        long workerRegionPaddingTicks = 0L;
        long workerSignatureTicks = 0L;
        long workerRuleMatchingTicks = 0L;
        long workerActionMergeTicks = 0L;

        long parallelStart = measureTimings ? Stopwatch.GetTimestamp() : 0L;

        // batch the regions
        List<Vector2[]> regionBatches = new ();
        int batchSize = regionsToTick.Length / Environment.ProcessorCount + 1;
        for (int i = 0; i < regionsToTick.Length; i += batchSize) {
            int size = Math.Min(batchSize, regionsToTick.Length - i);
            Vector2[] batch = new Vector2[size];
            Array.Copy(regionsToTick, i, batch, 0, size);
            regionBatches.Add(batch);
        }

        // construct ParallelOptions for our custom task scheduler
        ParallelOptions parallelOptions = new () { TaskScheduler = _tickScheduler };

        Parallel.ForEach(regionBatches, parallelOptions, batch => {
            Random random = new ((int) batch[0].X * 397 ^ (int) batch[0].Y);
            foreach (Vector2 pos in batch) {
                if (!tickableWorld.Regions.TryGetValue(pos, out TickableWorld.Region? region)) {
                    // If the region is not loaded, skip processing
                    return;
                }

                // if the region is below y = 0, don't process it.
                if (pos.Y < 0) {
                    return;
                }

                ulong[,] signatures = _signatures.Value!;

                // reset signatures - optimized with unsafe memory operations
                fixed (ulong* ptr = &signatures[0, 0])
                {
                    new Span<ulong> (ptr, PaddedSize * PaddedSize).Clear();
                }

                long workerRegionPaddingStart = measureTimings ? Stopwatch.GetTimestamp() : 0L;
                uint[,] blocks = GetPaddedRegion(pos, region!);
                if (measureTimings) {
                    Interlocked.Add(ref workerRegionPaddingTicks, Stopwatch.GetTimestamp() - workerRegionPaddingStart);
                }

                long workerSignatureStart = measureTimings ? Stopwatch.GetTimestamp() : 0L;
                ReadOnlySpan<uint> inputSpan = MemoryMarshal.CreateReadOnlySpan(ref blocks[0, 0], PaddedSize * PaddedSize);
                Span<ulong> outputSpan = MemoryMarshal.CreateSpan(ref signatures[0, 0], PaddedSize * PaddedSize);
                SignatureProcessor.ComputeSignature(inputSpan, outputSpan, PaddedSize, PaddedSize);
                if (measureTimings) {
                    Interlocked.Add(ref workerSignatureTicks, Stopwatch.GetTimestamp() - workerSignatureStart);
                }

                long workerRuleMatchingStart = measureTimings ? Stopwatch.GetTimestamp() : 0L;
                int[] regionIndices = TickingCache.GetRandomShuffledRegion();

                // process each block in the region based on its signature
                foreach (int index in regionIndices) {
                    int x = index % TickableWorld.RegionSize;
                    int y = index / TickableWorld.RegionSize;

                    ulong signature = signatures[x + 1, y + 1];
                    ExecuteAction? matchedRule = ProcessSignature(signature, new Vector2(x, y) + pos * TickableWorld.RegionSize);
                    if (matchedRule == null) continue;

                    // if the action is completely local, and cannot extend out of this region, apply it here
                    if (y > 0 && y < TickableWorld.RegionSize - 1 && x > 0 && x < TickableWorld.RegionSize - 1) {
                        matchedRule(false);
                    } else {
                        matchedRule(true);
                    }
                }
                if (measureTimings) {
                    Interlocked.Add(ref workerRuleMatchingTicks, Stopwatch.GetTimestamp() - workerRuleMatchingStart);
                }
            }
        });
        long parallelTicks = measureTimings ? Stopwatch.GetTimestamp() - parallelStart : 0L;

        // apply actions - optimized execution without expensive LINQ
        long actionPreparationTicks = 0L;
        long actionExecutionTicks = 0L;

        if (measureTimings) {
            LastTickTimings = new TickCycleTimings(
                regionsToTick.Length,
                ToMilliseconds(regionSelectionTicks),
                ToMilliseconds(parallelTicks),
                ToMilliseconds(actionPreparationTicks),
                ToMilliseconds(actionExecutionTicks),
                ToMilliseconds(Stopwatch.GetTimestamp() - totalStart),
                ToMilliseconds(workerRegionPaddingTicks),
                ToMilliseconds(workerSignatureTicks),
                ToMilliseconds(workerRuleMatchingTicks),
                ToMilliseconds(workerActionMergeTicks));
        } else {
            LastTickTimings = null;
        }

        return regionsToTick.Length;
    }

    public void Dispose()
    {
        _signatures.Dispose();
        _paddedRegion.Dispose();
        _signatureRules.Dispose();
    }

    private Vector2[] GetRegionsToTick(bool fullScan)
    {
        if (!fullScan)
        {
            return tickableWorld.UseNeedsTick().ToArray();
        }

        return tickableWorld.Regions
            .Where(entry => entry.Value is not null)
            .Select(entry => entry.Key)
            .ToArray();
    }

    private static double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private uint[,] GetPaddedRegion(Vector2 pos, TickableWorld.Region region)
    {
        uint[,] paddedRegion = _paddedRegion.Value!;

        // Copy center region data with optimized memory access
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

        // Pre-fetch neighbor regions to minimize lock time
        TickableWorld.Region leftNeighbor = tickableWorld.GetRegion(pos with { X = pos.X - 1 });
        TickableWorld.Region rightNeighbor = tickableWorld.GetRegion(pos with { X = pos.X + 1 });
        TickableWorld.Region topNeighbor = tickableWorld.GetRegion(pos with { Y = pos.Y - 1 });
        TickableWorld.Region bottomNeighbor = tickableWorld.GetRegion(pos with { Y = pos.Y + 1 });

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

    private ExecuteAction? ProcessSignature(ulong signature, Vector2 pos)
    {
        Dictionary<ulong, CompiledSignaturePlan>? signatureRules = _signatureRules.Value!;
        if (!signatureRules.TryGetValue(signature, out CompiledSignaturePlan? plan))
        {
            // signature not found, compute it.
            List<Rule.Mut> mutations = ComputeMutations(pos);
            plan = CompiledSignaturePlan.Compile(mutations);
            signatureRules[signature] = plan;
        }

        if (plan.IsEmpty) return null;

        return ExecuteCompiledAction(plan.SelectAction(Random.Shared.NextDouble() * plan.TotalChance), pos);
    }

    private ExecuteAction ExecuteCompiledAction(CompiledAction action, Vector2 position)
    {
        switch (action)
        {
            case CompiledConvertAction convert:
                return (useLocks) => {
                    foreach (Vector2 slot in convert.Slots) {
                        tickableWorld.SetBlock(position + slot, convert.Block);
                    }
                };
            case CompiledAddPollutionAction addPollution:
                return (useLocks) => tickableWorld.AddPollution(addPollution.Amount);
            case CompiledSwapAction swap:
                return (useLocks) => tickableWorld.SwapBlocks(position, position + swap.Slot);
            case CompiledChanceAction chance:
                double randomValue = Random.Shared.NextDouble();
                return randomValue < chance.ActionChance ?
                    ExecuteCompiledAction(chance.Action, position) :
                    (useLocks) => {
                        // we need to make sure this block gets ticked next tick if the chance fails.
                        (Vector2 regionPos, Vector2 localPos) = Coords.WorldToRegionCoords(position);
                        tickableWorld.Regions[regionPos]!.RequireTick((int)localPos.X, (int)localPos.Y);
                    };
            case CompiledOneOfAction oneOf:
                CompiledAction selectedAction = oneOf.Actions[Random.Shared.Next(oneOf.Actions.Length)];
                return ExecuteCompiledAction(selectedAction, position);
            case CompiledAllOfAction allOf:
                ExecuteAction[] actions = new ExecuteAction[allOf.Actions.Length];
                for (int i = 0; i < allOf.Actions.Length; i++)
                {
                    actions[i] = ExecuteCompiledAction(allOf.Actions[i], position);
                }
                return (useLocks) =>
                {
                    foreach (ExecuteAction compiledAction in actions)
                    {
                        compiledAction(useLocks);
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
        return (info.Id, info.GetTag(BlockInfo.TagMatterState), info);
    }
}
