using System.Text.Json;
namespace Technolize.World.Tag;

public class TagStruct : ITaggableStruct<TagStruct>
{
    public TagStruct(Dictionary<string, object> tags)
    {
        foreach (KeyValuePair<string, object> kvp in tags)
        {
            AssertSafe(kvp.Value);
            _tags[kvp.Key] = kvp.Value;
        }
    }

    private readonly Dictionary<string, object> _tags = new ();

    public T? GetTag<T>(Tag<T> key)
    {
        if (_tags.TryGetValue(key, out object? tag))
        {
            return key.Adaptor.Forwards(tag);
        }
        return default(T);
    }

    public bool HasTag<T>(Tag<T> key)
    {
        return _tags.ContainsKey(key);
    }

    public TagStruct WithTag<T>(Tag<T> key, T tag)
    {
        AssertSafe(tag);

        Dictionary<string, object> newTags = new (_tags)
        {
            [key] = key.Adaptor.Backwards(tag)
        };
        return new (newTags);
    }

    private static void AssertSafe(object tag)
    {
        // tag is safe if it can be json-serialized
        try
        {
            // TODO: use JsonTypeInfo somehow, it's more efficient
            JsonSerializer.Serialize(tag);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Tag is not safe to store in TagContainer", nameof(tag), ex);
        }
    }
}
