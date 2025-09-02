using Raylib_cs;
using Technolize.World.Tag;
namespace Technolize.World.Block;

public record BlockInfo : ITagged {
    public static readonly Tag<string> TagDisplayName = "DisplayName";
    public static readonly Tag<Color> TagColor = "Color";
    public static readonly Tag<MatterState> TagMatterState = "MatterState";

    /// <summary>
    /// Density (weight) of the block, used for physics calculations like falling speed and pressure.
    /// Measured in kg/m³ (kilograms per cubic meter).
    /// </summary>
    public static readonly Tag<double> TagDensity = "Density";

    public static implicit operator uint(BlockInfo block) => block.id;
    public static implicit operator BlockInfo(uint id) => BlockRegistry.GetInfo(id);

    public uint id { get; }

    private TagStruct? _tags;
    private Action<ITaggable> _configure;
    public TagStruct tags {
        get {
            _tags ??= Configure();
            return _tags;
        }
    }

    private BlockInfo(uint id, Action<ITaggable> configure) {
        this.id = id;
        _configure = configure;

        // ensure all the required tags are present
        if (!HasTag(TagMatterState)) throw new ArgumentException($"Block {id} is missing required tag {TagMatterState}");
        if (!HasTag(TagColor)) throw new ArgumentException($"Block {id} is missing required tag {TagColor}");
        if (!HasTag(TagDisplayName)) throw new ArgumentException($"Block {id} is missing required tag {TagDisplayName}");
        if (!HasTag(TagDensity)) throw new ArgumentException($"Block {id} is missing required tag {TagDensity}");
    }

    public static BlockInfo Build(uint id, Action<ITaggable> configure) {
        return new BlockInfo(id, configure);
    }

    private TagStruct Configure() {
        TagContainer container = new ();
        _configure(container);
        _configure = null!;
        return container.ToTagStruct();
    }

    public T? GetTag<T>(Tag<T> key) => tags.GetTag(key);
    public bool HasTag<T>(Tag<T> key) => tags.HasTag(key);
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
