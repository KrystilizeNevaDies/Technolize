using System.Reflection;
using Raylib_cs;
namespace Technolize.World.Block;

public static class BlockRegistry
{
    private static readonly BlockInfo[] BlockLookup;
    public static int BlockCount { get; }

    private static readonly Dictionary<Color, BlockInfo> BlockInfoLookup;

    public static List<BlockInfo> Blocks
    {
        get => BlockLookup.ToList();
    }

    static BlockRegistry()
    {
        List<BlockInfo> blockProperties = typeof(Blocks).GetProperties(BindingFlags.Public | BindingFlags.Static)
                        .Where(p => p.PropertyType == typeof(BlockInfo))
                        .Select(p => (BlockInfo) p.GetValue(null)!)
                        .OrderBy(b => b.id)
                        .ToList();

        BlockCount = blockProperties.Count;
        BlockLookup = new BlockInfo[BlockCount];

        foreach (BlockInfo blockInfo in blockProperties)
        {
            BlockLookup[blockInfo] = blockInfo;
        }

        BlockInfoLookup = BlockLookup.ToDictionary(b => b.GetTag(BlockInfo.TagColor), b => b);
    }

    public static BlockInfo GetInfo(long id)
    {
        if (id < 0 || id >= BlockLookup.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"Block ID {id} is out of range. Valid range is 0 to {BlockLookup.Length - 1}.");
        }
        return BlockLookup[id];
    }
    public static BlockInfo GetInfoByColor(Color color)
    {
        return BlockInfoLookup.TryGetValue(color, out BlockInfo? blockInfo) ? blockInfo : Block.Blocks.Air; // Default to Air if color not found
    }
}
