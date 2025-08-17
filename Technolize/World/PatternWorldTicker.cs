using System.Numerics;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.World;

public class PatternWorldTicker(WorldGrid world)
{
    private readonly Random _random = new();
    internal int Ticks;

    /// <summary>
    /// Ticks the simulation for all dynamic blocks within a given bounding box.
    /// </summary>
    public void Tick()
    {
        Ticks++;

        // add some random ticks to the simulation
        for (var i = 0; i < 256; i++)
        {
            world.NeedsTicking.Add(world.RandomPos(_random));
        }

        var patterns = Pattern.GetPatterns().ToArray();
        var actions = new Dictionary<Vector2, Action>();
        foreach (var position in world.NeedsTicking)
        {
            var action = ProcessBlock(position, patterns);
            if (action != null)
            {
                actions[position] = action;
            }
        }

        // clear the list of blocks that need ticking.
        world.NeedsTicking.Clear();

        // sort actions by Y position to ensure we process from bottom to top.
        var ordered = actions.OrderBy(kvp => kvp.Key.Y)
                        .ThenBy(kvp => _random.Next(-1, 2));

        // Execute all actions in the sorted order.
        foreach (var (_, action) in ordered)
        {
            action();
        }
    }

    private Action? ProcessBlock(Vector2 position, Pattern[] patterns)
    {
        // if the block is below y = -100, we destroy it.
        if (position.Y < -100 && world.GetBlock(position).Id != Blocks.Air.Id)
        {
            return () => world.SetBlock(position, Blocks.Air);
        }

        var matched = patterns
            .Where(pattern => MatchesPattern(position, pattern))
            .OrderBy(pattern => pattern.Priority)
            .ThenBy(_ => _random.Next(-1, 2)) // randomize order for same priority patterns
            .FirstOrDefault();

        return matched == null ? null : ExecutePatternAction(matched.Action, position);

    }

    private Action ExecutePatternAction(IPatternAction patternAction, Vector2 position)
    {
        switch (patternAction)
        {
            case IPatternAction.Swap swap:
                return () => world.SwapBlocks(position, position + swap.Slot);
            case IPatternAction.Convert convert:
                return () => world.SetBlock(position + convert.Slot, convert.Block);
            case IPatternAction.OneOf oneOf:
                // Randomly choose one of the actions
                var action = oneOf.Actions[_random.Next(oneOf.Actions.Length)];
                return ExecutePatternAction(action, position);
            case IPatternAction.AllOf allOf:
                var actions = allOf.Actions.Select(a => ExecutePatternAction(a, position)).ToArray();
                return () =>
                {
                    foreach (var act in actions)
                    {
                        act();
                    }
                };
        }
        throw new NotImplementedException();
    }

    private bool MatchesPattern(Vector2 position, Pattern pattern)
    {
        var thisBlock = world.GetBlock(position);
        foreach (var (offset, slot) in pattern.Slots)
        {
            var worldPos = position + offset;
            var block = world.GetBlock(worldPos);
            switch (slot)
            {
                case Slot.Air when block.Id != Blocks.Air.Id:
                case Slot.Same when block.Id != thisBlock.Id:
                case Slot.Powder when block.MatterState != MatterState.Powder:
                case Slot.Liquid when block.MatterState != MatterState.Liquid:
                case Slot.Solid when block.MatterState != MatterState.Solid:
                case Slot.Gas when block.MatterState != MatterState.Gas:
                    return false; // pattern failed to match
            }
        }
        return true;
    }
}

enum Slot
{
    Air,
    Same,
    Powder,
    Liquid,
    Solid,
    Gas
}

interface IPatternAction
{
    public record Swap(Vector2 Slot) : IPatternAction;
    public record Convert(Vector2 Slot, BlockInfo Block) : IPatternAction;
    public record OneOf(params IPatternAction[] Actions) : IPatternAction;
    public record AllOf(params IPatternAction[] Actions) : IPatternAction;
}

record Pattern(Dictionary<Vector2, Slot> Slots, IPatternAction Action, int Priority)
{
    public static IEnumerable<Pattern> GetPatterns()
    {
        // gravity
        {
            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Powder },
                    { new Vector2(0, -1), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(0, -1)),
                0
            );

            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Liquid },
                    { new Vector2(0, -1), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(0, -1)),
                0
            );

            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Powder },
                    { new Vector2(0, -1), Slot.Liquid }
                },
                new IPatternAction.Swap(new Vector2(0, -1)),
                0
            );
        }

        // settling
        {
            // powder
            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Powder },
                    { new Vector2(-1, -1), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(-1, -1)),
                1
            );

            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Powder },
                    { new Vector2(1, -1), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(1, -1)),
                1
            );

            // liquid
            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Liquid },
                    { new Vector2(-1, -1), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(-1, -1)),
                1
            );

            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(0, 0), Slot.Liquid },
                    { new Vector2(1, -1), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(1, -1)),
                1
            );

            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(-1, 1), Slot.Air },
                    { new Vector2(0, 0), Slot.Liquid },
                    { new Vector2(-1, 0), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(-1, 0)),
                1
            );

            yield return new Pattern(
                new Dictionary<Vector2, Slot>
                {
                    { new Vector2(1, 1), Slot.Air },
                    { new Vector2(0, 0), Slot.Liquid },
                    { new Vector2(1, 0), Slot.Air }
                },
                new IPatternAction.Swap(new Vector2(1, 0)),
                1
            );
        }

        // recipes

        // stone -> sand erosion via water
        // yield return new Pattern(
        //     new Dictionary<Vector2, Slot>
        //     {
        //         { new Vector2(0, 0), Slot.Liquid },
        //         { new Vector2(0, -1), Slot.Solid }
        //     },
        //     // convert both to stone
        //     new IPatternAction.AllOf(
        //         new IPatternAction.Convert(new Vector2(0, 0), Blocks.Sand),
        //         new IPatternAction.Convert(new Vector2(0, -1), Blocks.Sand)
        //     ),
        //     0
        // );
    }
}
