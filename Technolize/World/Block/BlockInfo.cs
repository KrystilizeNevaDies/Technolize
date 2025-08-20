using System.Numerics;
using Raylib_cs;
namespace Technolize.World.Block;

public sealed class BlockInfo(int id, Vector2 size, MatterState matterState, Color color)
{
    public int Id { get; } = id;
    public Vector2 Size { get; } = size;
    public MatterState MatterState { get; } = matterState;
    public Color Color { get; } = color;
}

public enum MatterState
{
    /// <summary>
    /// A fixed block, like a stone wall or a machine.
    /// </summary>
    Solid,

    /// <summary>
    /// A loose particle, like sand or dust.
    /// </summary>
    Powder,

    /// <summary>
    /// A flowing particle, like water.
    /// </summary>
    Liquid,

    /// <summary>
    ///
    /// </summary>
    Gas
}
