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
    double TotalMs,
    double RegionPaddingPerActiveRegionMs,
    double SignatureComputationPerActiveRegionMs,
    double RuleMatchingPerActiveRegionMs);

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

    public static CompiledSignaturePlan Compile(List<Rule.Candidate> mutations)
    {
        if (mutations.Count == 0)
        {
            return Empty;
        }

        CompiledMutation[] compiledMutations = new CompiledMutation[mutations.Count];
        double cumulativeChance = 0.0;
        for (int i = 0; i < mutations.Count; i++)
        {
            Rule.Candidate mutation = mutations[i];
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
    private const int ValidationRetryCount = 2;

    private readonly ThreadLocal<ulong[,]> _signatures = new (() => new ulong[PaddedSize, PaddedSize]);
    private readonly ThreadLocal<uint[,]> _paddedRegion = new (() => new uint[PaddedSize, PaddedSize]);
    private readonly ThreadLocal<uint[,]> _liveNeighborhood = new(() => new uint[3, 3]);
    private readonly ThreadLocal<uint[,]> _validationNeighborhood = new(() => new uint[3, 3]);
    private readonly ThreadLocal<byte[,]> _dirtyCenters = new(() => new byte[PaddedSize, PaddedSize]);

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

                // reset signatures
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
                byte[,] dirtyCenters = _dirtyCenters.Value!;
                Array.Clear(dirtyCenters);
                int[] regionIndices = TickingCache.GetRandomShuffledRegion();

                // process each block in the region based on its signature
                foreach (int index in regionIndices) {
                    int x = index % TickableWorld.RegionSize;
                    int y = index / TickableWorld.RegionSize;

                    Vector2 blockPos = new Vector2(x, y) + pos * TickableWorld.RegionSize;

                    double selectionSample = Random.Shared.NextDouble();
                    if (dirtyCenters[x + 1, y + 1] != 0)
                    {
                        if (!TryExecuteUpdatedNeighborhoodAction(blocks, x, y, selectionSample, blockPos, pos, dirtyCenters))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        ulong signature = signatures[x + 1, y + 1];
                        LocalGrid localGrid = new (blocks, x, y);
                        CompiledSignaturePlan plan = GetCompiledPlan(signature, localGrid);
                        if (plan.IsEmpty)
                        {
                            continue;
                        }

                        CompiledAction selectedAction = plan.SelectAction(selectionSample * plan.TotalChance);
                        ExecuteCompiledAction(selectedAction, blockPos, pos, dirtyCenters, blocks);
                    }
                }
                if (measureTimings) {
                    Interlocked.Add(ref workerRuleMatchingTicks, Stopwatch.GetTimestamp() - workerRuleMatchingStart);
                }
            }
        });
        long parallelTicks = measureTimings ? Stopwatch.GetTimestamp() - parallelStart : 0L;

        if (measureTimings) {
            double activeRegionCount = Math.Max(1, regionsToTick.Length);
            LastTickTimings = new TickCycleTimings(
                regionsToTick.Length,
                ToMilliseconds(regionSelectionTicks),
                ToMilliseconds(parallelTicks),
                ToMilliseconds(Stopwatch.GetTimestamp() - totalStart),
                ToMilliseconds(workerRegionPaddingTicks) / activeRegionCount,
                ToMilliseconds(workerSignatureTicks) / activeRegionCount,
                ToMilliseconds(workerRuleMatchingTicks) / activeRegionCount);
        } else {
            LastTickTimings = null;
        }

        return regionsToTick.Length;
    }

    public void Dispose()
    {
        _signatures.Dispose();
        _paddedRegion.Dispose();
        _liveNeighborhood.Dispose();
        _validationNeighborhood.Dispose();
        _dirtyCenters.Dispose();
        _signatureRules.Dispose();
    }

    internal bool ExecuteValidatedSnapshotAction(uint[,] snapshotRegion, int offsetX, int offsetY, Vector2 position, double selectionSample)
    {
        CompiledSignaturePlan plan = GetCompiledPlan(SignatureProcessor.ComputeSignature(snapshotRegion), new LocalGrid(snapshotRegion, offsetX, offsetY));
        if (plan.IsEmpty)
        {
            return false;
        }

        CompiledAction snapshotAction = plan.SelectAction(selectionSample * plan.TotalChance);
        byte[,] dirtyCenters = _dirtyCenters.Value!;
        Array.Clear(dirtyCenters);
        return TryExecuteValidatedAction(position, snapshotRegion, offsetX, offsetY, snapshotAction, selectionSample, position.GetRegion(), dirtyCenters);
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

    private CompiledSignaturePlan GetCompiledPlan(ulong signature, LocalGrid localGrid)
    {
        Dictionary<ulong, CompiledSignaturePlan>? signatureRules = _signatureRules.Value!;
        if (!signatureRules.TryGetValue(signature, out CompiledSignaturePlan? plan))
        {
            // signature not found, compute it.
            List<Rule.Candidate> mutations = ComputeMutations(localGrid);
            plan = CompiledSignaturePlan.Compile(mutations);
            signatureRules[signature] = plan;
        }

        return plan;
    }

    private bool TryExecuteUpdatedNeighborhoodAction(
        uint[,] paddedRegion,
        int offsetX,
        int offsetY,
        double selectionSample,
        Vector2 position,
        Vector2 currentRegionPos,
        byte[,] dirtyCenters)
    {
        uint[,] currentNeighborhood = _liveNeighborhood.Value!;
        CopyNeighborhood(paddedRegion, offsetX, offsetY, currentNeighborhood);
        if (!TrySelectAction(currentNeighborhood, selectionSample, out CompiledAction action))
        {
            return false;
        }

        ExecuteCompiledAction(action, position, currentRegionPos, dirtyCenters, paddedRegion);
        return true;
    }

    private bool TryExecuteValidatedAction(
        Vector2 position,
        uint[,] snapshotRegion,
        int offsetX,
        int offsetY,
        CompiledAction snapshotAction,
        double selectionSample,
        Vector2 currentRegionPos,
        byte[,] dirtyCenters)
    {
        uint[,] sourceNeighborhood = _liveNeighborhood.Value!;
        uint[,] validationNeighborhood = _validationNeighborhood.Value!;

        CaptureLiveNeighborhood(position, sourceNeighborhood);
        bool useSnapshotAction = NeighborhoodMatchesSnapshot(sourceNeighborhood, snapshotRegion, offsetX, offsetY);

        for (int attempt = 0; attempt < ValidationRetryCount; attempt++)
        {
            CompiledAction actionToExecute;
            if (useSnapshotAction)
            {
                actionToExecute = snapshotAction;
            }
            else if (!TrySelectAction(sourceNeighborhood, selectionSample, out actionToExecute))
            {
                return false;
            }

            CaptureLiveNeighborhood(position, validationNeighborhood);
            if (NeighborhoodsEqual(sourceNeighborhood, validationNeighborhood))
            {
                ExecuteCompiledAction(actionToExecute, position, currentRegionPos, dirtyCenters, paddedRegion: null);
                return true;
            }

            (sourceNeighborhood, validationNeighborhood) = (validationNeighborhood, sourceNeighborhood);
            useSnapshotAction = false;
        }

        return false;
    }

    private bool TrySelectAction(uint[,] neighborhood, double selectionSample, out CompiledAction action)
    {
        CompiledSignaturePlan plan = GetCompiledPlan(SignatureProcessor.ComputeSignature(neighborhood), new LocalGrid(neighborhood, 0, 0));
        if (plan.IsEmpty)
        {
            action = null!;
            return false;
        }

        action = plan.SelectAction(selectionSample * plan.TotalChance);
        return true;
    }

    private void ExecuteCompiledAction(CompiledAction action, Vector2 position, Vector2 currentRegionPos, byte[,] dirtyCenters, uint[,]? paddedRegion)
    {
        switch (action)
        {
            case CompiledConvertAction convert:
                foreach (Vector2 slot in convert.Slots)
                {
                    Vector2 targetPosition = position + slot;
                    tickableWorld.SetBlock(targetPosition, convert.Block);
                    SetSnapshotBlock(paddedRegion, currentRegionPos, targetPosition, convert.Block);
                    MarkDirtyCenters(currentRegionPos, targetPosition, dirtyCenters);
                }
                return;
            case CompiledAddPollutionAction addPollution:
                tickableWorld.AddPollution(addPollution.Amount);
                return;
            case CompiledSwapAction swap:
                Vector2 swapTarget = position + swap.Slot;
                tickableWorld.SwapBlocks(position, swapTarget);
                SwapSnapshotBlocks(paddedRegion, currentRegionPos, position, swapTarget);
                MarkDirtyCenters(currentRegionPos, position, dirtyCenters);
                MarkDirtyCenters(currentRegionPos, swapTarget, dirtyCenters);
                return;
            case CompiledChanceAction chance:
                if (Random.Shared.NextDouble() < chance.ActionChance)
                {
                    ExecuteCompiledAction(chance.Action, position, currentRegionPos, dirtyCenters, paddedRegion);
                }
                else
                {
                    RequireTick(position);
                }
                return;
            case CompiledOneOfAction oneOf:
                ExecuteCompiledAction(oneOf.Actions[Random.Shared.Next(oneOf.Actions.Length)], position, currentRegionPos, dirtyCenters, paddedRegion);
                if (oneOf.Actions.Length > 1)
                {
                    RequireTick(position);
                }
                return;
            case CompiledAllOfAction allOf:
                foreach (CompiledAction childAction in allOf.Actions)
                {
                    ExecuteCompiledAction(childAction, position, currentRegionPos, dirtyCenters, paddedRegion);
                }
                return;
        }

        throw new NotImplementedException();
    }

    private void CaptureLiveNeighborhood(Vector2 position, uint[,] destination)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                destination[dx + 1, dy + 1] = (uint)tickableWorld.GetBlock(position + new Vector2(dx, dy));
            }
        }
    }

    private static void CopyNeighborhood(uint[,] sourceRegion, int offsetX, int offsetY, uint[,] destination)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                destination[dx + 1, dy + 1] = sourceRegion[offsetX + dx + 1, offsetY + dy + 1];
            }
        }
    }

    private static bool NeighborhoodMatchesSnapshot(uint[,] neighborhood, uint[,] snapshotRegion, int offsetX, int offsetY)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (neighborhood[dx + 1, dy + 1] != snapshotRegion[offsetX + dx + 1, offsetY + dy + 1])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool NeighborhoodsEqual(uint[,] left, uint[,] right)
    {
        for (int dy = 0; dy < 3; dy++)
        {
            for (int dx = 0; dx < 3; dx++)
            {
                if (left[dx, dy] != right[dx, dy])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void SetSnapshotBlock(uint[,]? paddedRegion, Vector2 currentRegionPos, Vector2 worldPosition, uint block)
    {
        if (paddedRegion == null)
        {
            return;
        }

        if (!TryGetSnapshotCoords(paddedRegion, currentRegionPos, worldPosition, out int paddedX, out int paddedY))
        {
            return;
        }

        paddedRegion[paddedX, paddedY] = block;
    }

    private static void SwapSnapshotBlocks(uint[,]? paddedRegion, Vector2 currentRegionPos, Vector2 position, Vector2 swapTarget)
    {
        if (paddedRegion == null)
        {
            return;
        }

        if (!TryGetSnapshotCoords(paddedRegion, currentRegionPos, position, out int positionX, out int positionY)
            || !TryGetSnapshotCoords(paddedRegion, currentRegionPos, swapTarget, out int targetX, out int targetY))
        {
            return;
        }

        (paddedRegion[positionX, positionY], paddedRegion[targetX, targetY]) = (paddedRegion[targetX, targetY], paddedRegion[positionX, positionY]);
    }

    private static bool TryGetSnapshotCoords(uint[,] paddedRegion, Vector2 currentRegionPos, Vector2 worldPosition, out int paddedX, out int paddedY)
    {
        paddedX = (int)(worldPosition.X - currentRegionPos.X * TickableWorld.RegionSize) + 1;
        paddedY = (int)(worldPosition.Y - currentRegionPos.Y * TickableWorld.RegionSize) + 1;
        return paddedX >= 0
               && paddedX < paddedRegion.GetLength(0)
               && paddedY >= 0
               && paddedY < paddedRegion.GetLength(1);
    }

    private static void MarkDirtyCenters(Vector2 currentRegionPos, Vector2 worldPosition, byte[,] dirtyCenters)
    {
        int paddedX = (int)(worldPosition.X - currentRegionPos.X * TickableWorld.RegionSize) + 1;
        int paddedY = (int)(worldPosition.Y - currentRegionPos.Y * TickableWorld.RegionSize) + 1;

        int minX = Math.Max(1, paddedX - 1);
        int maxX = Math.Min(TickableWorld.RegionSize, paddedX + 1);
        int minY = Math.Max(1, paddedY - 1);
        int maxY = Math.Min(TickableWorld.RegionSize, paddedY + 1);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                dirtyCenters[x, y] = 1;
            }
        }
    }

    private void RequireTick(Vector2 position)
    {
        (Vector2 regionPos, Vector2 localPos) = Coords.WorldToRegionCoords(position);
        tickableWorld.Regions[regionPos]!.RequireTick((int)localPos.X, (int)localPos.Y);
    }

    private List<Rule.Candidate> ComputeMutations(LocalGrid localGrid) {
        MutationContext context = new (localGrid);
        return Rule.CalculateMutations(context)
            .Select(mut => {
                IAction action = mut.Action.PruneRedundant(context) ?? new AllOf();
                return mut with { Action = action };
            })
            .ToList()!;
    }
}


/// <param name="LocalBlocks">3x3 grid of blocks, with the center block being the current block</param>
public record MutationContext(LocalGrid localGrid) : Rule.IContext {
    public BlockInfo Info => BlockRegistry.GetInfo(localGrid.Get(0, 0));

    public (uint block, MatterState matterState, BlockInfo info) Get(Vector2 offset) {
        return Get((int) offset.X, (int) offset.Y);
    }
    public (uint block, MatterState matterState, BlockInfo info) Get(int x, int y) {
        if (x < -1 || x > 1 || y < -1 || y > 1) {
            throw new ArgumentOutOfRangeException($"Get() only supports x/y values of -1, 0, or 1. Got x={x}, y={y}");
        }
        uint block = localGrid.Get(x, y);
        BlockInfo info = BlockRegistry.GetInfo(block);
        return (block, info.GetTag(BlockInfo.TagMatterState), info);
    }
}

public record LocalGrid(uint[,] RegionGrid, int OffsetX, int OffsetY) {
    public uint Get(int x, int y) {
        return RegionGrid[x + OffsetX + 1, y + OffsetY + 1];
    }
}
