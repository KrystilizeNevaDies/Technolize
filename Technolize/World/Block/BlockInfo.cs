using Raylib_cs;
using Technolize.World.Tag;
namespace Technolize.World.Block;

public record BlockInfo : ITagged {
    public static readonly Tag<MatterState> TagMatterState = "MatterState";
    public static readonly Tag<Color> TagColor = "Color";
    public static readonly Tag<string> TagDisplayName = "DisplayName";

    public static implicit operator uint(BlockInfo block) => block.id;
    public static implicit operator BlockInfo(uint id) => BlockRegistry.GetInfo(id);

    public uint id { get; }
    public TagStruct tags { get; }

    public BlockInfo(uint id, TagStruct tags) {
        this.id = id;
        this.tags = tags;

        // ensure all the required tags are present
        GetTag(TagMatterState);
        GetTag(TagColor);
        GetTag(TagDisplayName);
    }

    public static BlockInfo Build(uint id, Action<ITaggable> configure) {
        TagContainer container = new ();
        configure(container);
        return new BlockInfo(id, container.ToTagStruct());
    }

    public T? GetTag<T>(Tag<T> key) => tags.GetTag(key);
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
