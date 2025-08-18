using System.Numerics;
using Technolize.World.Block;
using Technolize.World.Particle;
namespace Technolize.World;

public class PatternWorldTicker(IWorld world)
{
    private readonly Random _random = new();

    private static MatterState GetMatterState(long blockId)
    {
        return BlockRegistry.GetInfo(blockId).MatterState;
    }

    public void Tick()
    {

        Pattern[] patterns = Pattern.GetPatterns().ToArray();
        Dictionary<Vector2, Action> actions = new Dictionary<Vector2, Action>();

        foreach ((Vector2 position, long block) in world.GetBlocks())
        {
            Action? action = ProcessBlock(position, patterns);
            if (action != null)
            {
                actions[position] = action;
            }
        }

        IOrderedEnumerable<KeyValuePair<Vector2, Action>> ordered = actions.OrderBy(kvp => kvp.Key.Y)
                        .ThenBy(kvp => _random.Next(-1, 2));

        foreach ((Vector2 _, Action action) in ordered)
        {
            action();
        }
    }

    private Action? ProcessBlock(Vector2 position, Pattern[] patterns)
    {
        if (position.Y < -100 && world.GetBlock(position) != Blocks.Air.Id)
        {
            return () => world.SetBlock(position, Blocks.Air.Id);
        }

        Pattern? matched = patterns
            .Where(pattern => MatchesPattern(position, pattern))
            .OrderBy(pattern => pattern.Priority)
            .ThenBy(_ => _random.Next(-1, 2))
            .FirstOrDefault();

        return matched == null ? null : ExecutePatternAction(matched.Action, position);
    }

    private Action ExecutePatternAction(IPatternAction patternAction, Vector2 position)
    {
        switch (patternAction)
        {
            case IPatternAction.Swap swap:
                return () => world.SwapBlocks(position, position + swap.Slot);
            case IPatternAction.OneOf oneOf:
                IPatternAction action = oneOf.Actions[_random.Next(oneOf.Actions.Length)];
                return ExecutePatternAction(action, position);
        }
        throw new NotImplementedException();
    }

    private bool MatchesPattern(Vector2 position, Pattern pattern)
    {
        long thisBlockId = world.GetBlock(position);

        foreach ((Vector2 offset, Slot slot) in pattern.Slots)
        {
            Vector2 worldPos = position + offset;
            long blockId = world.GetBlock(worldPos);
            MatterState matterState = GetMatterState(blockId);

            switch (slot)
            {
                case Slot.Air when blockId != Blocks.Air.Id:
                case Slot.Same when blockId != thisBlockId:
                case Slot.Powder when matterState != MatterState.Powder:
                case Slot.Liquid when matterState != MatterState.Liquid:
                case Slot.Solid when matterState != MatterState.Solid:
                case Slot.Gas when matterState != MatterState.Gas:
                    return false;
            }
        }
        return true;
    }
}

public enum Slot
{
    Air,
    Same,
    Powder,
    Liquid,
    Solid,
    Gas
}

public interface IPatternAction
{
    public record Swap(Vector2 Slot) : IPatternAction;
    public record OneOf(params IPatternAction[] Actions) : IPatternAction;
}

public record Pattern(Dictionary<Vector2, Slot> Slots, IPatternAction Action, int Priority)
{
    public static IEnumerable<Pattern> GetPatterns()
    {
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

        {
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
    }
}
