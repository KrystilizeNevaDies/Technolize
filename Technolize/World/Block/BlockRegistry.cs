using System.Reflection;
using Technolize.World.Particle;
namespace Technolize.World.Block;

public static class BlockRegistry
{
    private static readonly BlockInfo[] BlockLookup;
    public static int BlockCount { get; }

    static BlockRegistry()
    {
        var blockProperties = typeof(Blocks).GetProperties(BindingFlags.Public | BindingFlags.Static)
                        .Where(p => p.PropertyType == typeof(BlockInfo))
                        .Select(p => (BlockInfo) p.GetValue(null)!)
                        .OrderBy(b => b.Id)
                        .ToList();

        BlockCount = blockProperties.Count;
        BlockLookup = new BlockInfo[BlockCount];

        foreach (var blockInfo in blockProperties)
        {
            BlockLookup[blockInfo.Id] = blockInfo;
        }
    }

    public static BlockInfo GetInfo(long id)
    {
        if (id < 0 || id >= BlockLookup.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"Block ID {id} is out of range. Valid range is 0 to {BlockLookup.Length - 1}.");
        }
        return BlockLookup[id];
    }
}
