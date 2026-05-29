using System.Numerics;

namespace Technolize.Rendering;

public sealed record WorldRenderFrame(IReadOnlyList<WorldRenderRegion> Regions, IReadOnlySet<Vector2> ScheduledRegions)
{
    public static WorldRenderFrame Empty { get; } = new([], new HashSet<Vector2>());
}

public readonly record struct WorldRenderRegion(
    Vector2 Position,
    double SecondsSinceLastChanged,
    IReadOnlyList<WorldRenderBlock> Blocks);

public readonly record struct WorldRenderBlock(Vector2 LocalPos, uint BlockId);