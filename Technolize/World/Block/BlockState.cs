namespace Technolize.World.Block;

public abstract record BlockStateProperty(string Name)
{
    internal abstract int ValueCount { get; }
    internal abstract int DefaultIndex { get; }
    internal abstract object GetValueObject(int index);
    internal abstract int GetValueIndex(object value);
}

public sealed record BlockStateProperty<T> : BlockStateProperty where T : notnull
{
    private readonly IReadOnlyList<T> _values;
    private readonly Dictionary<T, int> _valueIndexes;

    public BlockStateProperty(string name, IReadOnlyList<T> values, T defaultValue) : base(name)
    {
        if (values.Count == 0)
        {
            throw new ArgumentException("State property must define at least one value.", nameof(values));
        }

        _values = values.ToArray();
        _valueIndexes = _values
            .Select((value, index) => (value, index))
            .ToDictionary(it => it.value, it => it.index);

        if (!_valueIndexes.TryGetValue(defaultValue, out int defaultIndex))
        {
            throw new ArgumentException($"Default value '{defaultValue}' is not part of the allowed values for state '{name}'.", nameof(defaultValue));
        }

        DefaultValue = defaultValue;
        DefaultIndex = defaultIndex;
    }

    public T DefaultValue { get; }
    public IReadOnlyList<T> Values => _values;

    internal override int ValueCount => _values.Count;
    internal override int DefaultIndex { get; }

    public T GetValue(int index)
    {
        return _values[index];
    }

    internal override object GetValueObject(int index)
    {
        return GetValue(index);
    }

    public int GetIndex(T value)
    {
        if (!_valueIndexes.TryGetValue(value, out int index))
        {
            throw new ArgumentException($"Value '{value}' is not valid for state '{Name}'.", nameof(value));
        }

        return index;
    }

    internal override int GetValueIndex(object value)
    {
        return value is T typedValue
            ? GetIndex(typedValue)
            : throw new ArgumentException($"Value '{value}' is not valid for state '{Name}'.", nameof(value));
    }
}

public static class BlockStateProperties
{
    public static BlockStateProperty<bool> Bool(string name, bool defaultValue = false)
    {
        return new BlockStateProperty<bool>(name, [false, true], defaultValue);
    }
}

public sealed class BlockStateBuilder
{
    private readonly List<BlockStateProperty> _properties = [];

    public BlockStateBuilder Add<T>(BlockStateProperty<T> property) where T : notnull
    {
        if (_properties.Any(existing => existing.Name == property.Name))
        {
            throw new ArgumentException($"State property '{property.Name}' is already registered for this block.", nameof(property));
        }

        _properties.Add(property);
        return this;
    }

    internal IReadOnlyList<BlockStateProperty> Build()
    {
        return _properties.ToArray();
    }
}

public readonly record struct BlockStateAccessor
{
    private readonly IReadOnlyDictionary<BlockStateProperty, int> _stateIndexes;

    internal BlockStateAccessor(IReadOnlyDictionary<BlockStateProperty, int> stateIndexes)
    {
        _stateIndexes = stateIndexes;
    }

    public bool Has<T>(BlockStateProperty<T> property) where T : notnull
    {
        return _stateIndexes.ContainsKey(property);
    }

    public T Get<T>(BlockStateProperty<T> property) where T : notnull
    {
        if (!_stateIndexes.TryGetValue(property, out int index))
        {
            throw new ArgumentException($"State '{property.Name}' is not defined in this accessor.", nameof(property));
        }

        return property.GetValue(index);
    }
}

public static class CommonBlockStates
{
    public static readonly BlockStateProperty<bool> Wet = BlockStateProperties.Bool("wet");
}