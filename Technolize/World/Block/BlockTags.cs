using Technolize.World.Tag;
namespace Technolize.World.Block;

public static class BlockTags {
    /// <summary>
    /// The block to convert this block into when burned.
    /// </summary>
    public static readonly Tag<uint> Burnable = "Burnable";

    /// <summary>
    /// Whether fire can spread into this block and turn it into fire.
    /// </summary>
    public static readonly Tag<bool> FireSpreadable = "FireSpreadable";

}
