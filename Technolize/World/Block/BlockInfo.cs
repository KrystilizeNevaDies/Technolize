using Raylib_cs;
using Technolize.World.Tag;

namespace Technolize.World.Block;

public sealed record BlockInfo : ITagged
{
    public static readonly Tag<string> TagDisplayName = "DisplayName";
    public static readonly Tag<Color> TagColor = "Color";
    public static readonly Tag<MatterState> TagMatterState = "MatterState";

    /// <summary>
    /// Density (weight) of the block, used for physics calculations like falling speed and pressure.
    /// Measured in kg/m³ (kilograms per cubic meter).
    /// </summary>
    public static readonly Tag<double> TagDensity = "Density";

    public static implicit operator uint(BlockInfo block) => block.Id;
    public static implicit operator BlockInfo(uint id) => BlockRegistry.GetInfo(id);

    public uint Id { get; }
    public BlockInfo BaseBlock { get; }
    public bool IsDefaultState => ReferenceEquals(this, BaseBlock);

    private readonly TagStruct _tags;
    private readonly IReadOnlyList<BlockStateProperty> _stateProperties;
    private readonly IReadOnlyDictionary<BlockStateProperty, int> _stateIndexes;
    private readonly IReadOnlyDictionary<string, BlockInfo>? _variantsByStateKey;

    public TagStruct Tags => _tags;

    private BlockInfo(
        uint id,
        TagStruct tags,
        BlockInfo? baseBlock,
        IReadOnlyList<BlockStateProperty>? stateProperties,
        IReadOnlyDictionary<BlockStateProperty, int>? stateIndexes,
        IReadOnlyDictionary<string, BlockInfo>? variantsByStateKey)
    {
        this.Id = id;
        _tags = tags;
        BaseBlock = baseBlock ?? this;
        _stateProperties = stateProperties ?? Array.Empty<BlockStateProperty>();
        _stateIndexes = stateIndexes ?? new Dictionary<BlockStateProperty, int>();
        _variantsByStateKey = variantsByStateKey;

        EnsureRequiredTags();
    }

    public static BlockInfo Build(ref uint nextId, Action<ITaggable> configure,
        Action<BlockStateBuilder>? configureStates = null,
        Func<BlockStateAccessor, TagStruct, TagStruct>? configureVariantTags = null)
    {
        TagContainer tagContainer = new();
        configure(tagContainer);
        TagStruct baseTags = tagContainer.ToTagStruct();

        BlockStateBuilder stateBuilder = new();
        configureStates?.Invoke(stateBuilder);
        IReadOnlyList<BlockStateProperty> stateProperties = stateBuilder.Build();

        if (stateProperties.Count == 0)
        {
            return new BlockInfo(nextId++, baseTags, null, null, null, null);
        }

        Dictionary<BlockStateProperty, int> defaultStateIndexes = stateProperties
            .ToDictionary(property => property, property => property.DefaultIndex);
        Dictionary<string, BlockInfo> variantsByStateKey = [];

        BlockInfo defaultVariant = new(
            nextId++,
            baseTags,
            null,
            stateProperties,
            defaultStateIndexes,
            variantsByStateKey);

        variantsByStateKey[BuildStateKey(stateProperties, defaultStateIndexes)] = defaultVariant;

        foreach (Dictionary<BlockStateProperty, int> stateIndexes in EnumerateStateIndexes(stateProperties))
        {
            string stateKey = BuildStateKey(stateProperties, stateIndexes);
            if (variantsByStateKey.ContainsKey(stateKey))
            {
                continue;
            }

            BlockStateAccessor accessor = new(stateIndexes);
            TagStruct variantTags = configureVariantTags?.Invoke(accessor, baseTags) ?? baseTags;

            BlockInfo variant = new(
                nextId++,
                variantTags,
                defaultVariant,
                stateProperties,
                stateIndexes,
                null);

            variantsByStateKey[stateKey] = variant;
        }

        return defaultVariant;
    }

    public T? GetTag<T>(Tag<T> key) => Tags.GetTag(key);
    public bool HasTag<T>(Tag<T> key) => Tags.HasTag(key);

    public bool Equals(BlockInfo? other)
    {
        return other is not null && Id == other.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    public bool HasState<T>(BlockStateProperty<T> property) where T : notnull
    {
        return _stateIndexes.ContainsKey(property);
    }

    public T GetState<T>(BlockStateProperty<T> property) where T : notnull
    {
        if (!_stateIndexes.TryGetValue(property, out int index))
        {
            throw new ArgumentException($"Block '{GetTag(TagDisplayName) ?? Id.ToString()}' does not define state '{property.Name}'.", nameof(property));
        }

        return property.GetValue(index);
    }

    public BlockInfo WithState<T>(BlockStateProperty<T> property, T value) where T : notnull
    {
        if (!HasState(property))
        {
            throw new ArgumentException($"Block '{GetTag(TagDisplayName) ?? Id.ToString()}' does not define state '{property.Name}'.", nameof(property));
        }

        if (BaseBlock._variantsByStateKey is null)
        {
            throw new InvalidOperationException($"Block '{GetTag(TagDisplayName) ?? Id.ToString()}' does not expose state variants.");
        }

        Dictionary<BlockStateProperty, int> updatedStateIndexes = BaseBlock._stateProperties
            .ToDictionary(
                blockState => blockState,
                blockState => ReferenceEquals(blockState, property)
                    ? property.GetIndex(value)
                    : _stateIndexes[blockState]);

        string stateKey = BuildStateKey(BaseBlock._stateProperties, updatedStateIndexes);
        if (!BaseBlock._variantsByStateKey.TryGetValue(stateKey, out BlockInfo? variant))
        {
            throw new InvalidOperationException($"No block variant exists for state key '{stateKey}'.");
        }

        return variant;
    }

    internal IEnumerable<BlockInfo> GetAllStates()
    {
        if (BaseBlock._variantsByStateKey is null)
        {
            yield return this;
            yield break;
        }

        foreach (BlockInfo variant in BaseBlock._variantsByStateKey.Values.OrderBy(block => block.Id))
        {
            yield return variant;
        }
    }

    private void EnsureRequiredTags()
    {
        if (!HasTag(TagMatterState)) throw new ArgumentException($"Block {Id} is missing required tag {TagMatterState}");
        if (!HasTag(TagColor)) throw new ArgumentException($"Block {Id} is missing required tag {TagColor}");
        if (!HasTag(TagDisplayName)) throw new ArgumentException($"Block {Id} is missing required tag {TagDisplayName}");
        if (!HasTag(TagDensity)) throw new ArgumentException($"Block {Id} is missing required tag {TagDensity}");
    }

    private static IEnumerable<Dictionary<BlockStateProperty, int>> EnumerateStateIndexes(IReadOnlyList<BlockStateProperty> stateProperties)
    {
        Dictionary<BlockStateProperty, int> current = [];
        foreach (Dictionary<BlockStateProperty, int> stateIndexes in EnumerateStateIndexes(stateProperties, 0, current))
        {
            yield return stateIndexes;
        }
    }

    private static IEnumerable<Dictionary<BlockStateProperty, int>> EnumerateStateIndexes(
        IReadOnlyList<BlockStateProperty> stateProperties,
        int propertyIndex,
        Dictionary<BlockStateProperty, int> current)
    {
        if (propertyIndex >= stateProperties.Count)
        {
            yield return new Dictionary<BlockStateProperty, int>(current);
            yield break;
        }

        BlockStateProperty property = stateProperties[propertyIndex];
        for (int valueIndex = 0; valueIndex < property.ValueCount; valueIndex++)
        {
            current[property] = valueIndex;
            foreach (Dictionary<BlockStateProperty, int> stateIndexes in EnumerateStateIndexes(stateProperties, propertyIndex + 1, current))
            {
                yield return stateIndexes;
            }
        }

        current.Remove(property);
    }

    private static string BuildStateKey(IReadOnlyList<BlockStateProperty> stateProperties, IReadOnlyDictionary<BlockStateProperty, int> stateIndexes)
    {
        return string.Join('|', stateProperties.Select(property => stateIndexes[property].ToString()));
    }
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