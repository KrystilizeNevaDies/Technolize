using System.Numerics;

namespace Technolize.Rendering;

public sealed record WorldRenderFrame(IReadOnlyList<WorldRenderRegion> Regions)
{
    public static WorldRenderFrame Empty { get; } = new([]);
}

public readonly record struct WorldRenderRegion(
    Vector2 Position,
    double SecondsSinceLastChanged,
    IReadOnlyList<WorldRenderBlock> Blocks);

public readonly record struct WorldRenderBlock(Vector2 LocalPos, uint BlockId);