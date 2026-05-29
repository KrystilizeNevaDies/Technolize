using System.Reflection;
using Raylib_cs;
namespace Technolize.World.Block;

public static class BlockRegistry
{
    private static readonly BlockInfo[] BlockLookup;
    public static int BlockCount { get; }
    public static uint MaxBlockId { get; }

    private static readonly Dictionary<Color, BlockInfo> BlockInfoLookup;

    public static List<BlockInfo> Blocks
    {
        get => BlockLookup.ToList();
    }

    static BlockRegistry()
    {
        List<BlockInfo> baseBlocks = typeof(Blocks).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(BlockInfo))
            .Select(p => (BlockInfo)p.GetValue(null)!)
            .OrderBy(b => b.Id)
            .ToList();

        List<BlockInfo> blockProperties = baseBlocks
            .SelectMany(block => block.GetAllStates())
            .OrderBy(block => block.Id)
            .ToList();

        BlockCount = blockProperties.Count;
        MaxBlockId = blockProperties.Count > 0 ? blockProperties[^1].Id : 0;
        BlockLookup = new BlockInfo[BlockCount];

        foreach (BlockInfo blockInfo in blockProperties)
        {
            BlockLookup[blockInfo] = blockInfo;
        }

        BlockInfoLookup = baseBlocks
            .GroupBy(block => block.GetTag(BlockInfo.TagColor))
            .ToDictionary(group => group.Key, group => group.First());
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
