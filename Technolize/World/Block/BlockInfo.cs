using System.Numerics;
using Raylib_cs;
namespace Technolize.World.Block;

public sealed class BlockInfo(string name, uint id, Vector2 size, MatterState matterState, Color color)
{
    public string Name { get; } = name;
    public uint Id { get; } = id;
    public Vector2 Size { get; } = size;
    public MatterState MatterState { get; } = matterState;
    public Color Color { get; } = color;
}

public enum MatterState
{
    /// <summary>
    /// A fixed block, like a stone wall or a machine.
    /// Solid blocks do not move and are not affected by gravity.
    /// </summary>
    Solid,

    /// <summary>
    /// A loose particle, like sand or dust.
    /// Powder blocks settle and are affected by gravity.
    /// </summary>
    Powder,

    /// <summary>
    /// A flowing particle, like water.
    /// Liquid blocks flow, settle, and are affected by gravity.
    /// </summary>
    Liquid,

    /// <summary>
    /// A gas, like air or mist.
    /// Gas blocks randomly move around and are not affected by gravity.
    /// </summary>
    Gas
}
