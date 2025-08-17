using System.Numerics;
using Raylib_cs;
using Technolize.World.Particle;
namespace Technolize.World.Block;

public static class Blocks
{
    private static readonly int NextId;

    public static BlockInfo Air { get; } = new (NextId++, new Vector2(1, 1), MatterState.Gas, new Color(0, 0, 255, 16)); // slightly blue to indicate air
    public static BlockInfo Water { get; } = new (NextId++, new Vector2(1, 1), MatterState.Liquid, new Color(0, 0, 255));
    public static BlockInfo Stone { get; } = new (NextId++, new Vector2(1, 1), MatterState.Solid, new Color(128, 128, 128));
    public static BlockInfo Sand { get; } = new (NextId++, new Vector2(1, 1), MatterState.Powder, new Color(194, 178, 128));
}
