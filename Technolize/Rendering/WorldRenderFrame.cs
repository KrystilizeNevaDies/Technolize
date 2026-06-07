using System.Numerics;

namespace Technolize.Rendering;

/// <summary>
/// A thread-safe snapshot of the world for one render. <see cref="WorldQuadtree"/> is the flat
/// serialization of an aligned square window of the global quadtree covering the rendered regions;
/// <see cref="QuadtreeWindowOriginX"/>/<see cref="QuadtreeWindowOriginY"/>/<see cref="QuadtreeWindowSize"/>
/// describe that window in absolute tree coordinates so the resource builder can map draw-space
/// positions into the (shallow) windowed tree. A window size of 0 means "whole world" (legacy path:
/// the array is the entire <c>[0, WorldSize)</c> tree), used by unit tests that supply nodes directly.
/// </summary>
public sealed record WorldRenderFrame(
    IReadOnlyList<WorldRenderRegion> Regions,
    IReadOnlySet<Vector2> ScheduledRegions,
    int[] WorldQuadtree,
    int QuadtreeWindowOriginX = 0,
    int QuadtreeWindowOriginY = 0,
    int QuadtreeWindowSize = 0)
{
    public static WorldRenderFrame Empty { get; } = new([], new HashSet<Vector2>(), []);
}

public readonly record struct WorldRenderRegion(
    Vector2 Position,
    double SecondsSinceLastChanged,
    IReadOnlyList<WorldRenderBlock> Blocks);

public readonly record struct WorldRenderBlock(Vector2 LocalPos, uint BlockId);