using System.Numerics;
using Raylib_cs;
namespace Technolize.World.Block;

public static class Blocks
{
    private static readonly uint NextId;

    public static BlockInfo Air { get; } = new (NextId++, new (1, 1), MatterState.Gas, new (0, 0, 0));
    public static BlockInfo Water { get; } = new (NextId++, new (1, 1), MatterState.Liquid, new (0, 0, 255));
    public static BlockInfo Stone { get; } = new (NextId++, new (1, 1), MatterState.Solid, new (128, 128, 128));
    public static BlockInfo Sand { get; } = new (NextId++, new (1, 1), MatterState.Powder, new (194, 178, 128));

    public static ISet<BlockInfo> AllBlocks() {
        return new HashSet<BlockInfo>
        {
            Air,
            Water,
            Stone,
            Sand
        };
    }
}
