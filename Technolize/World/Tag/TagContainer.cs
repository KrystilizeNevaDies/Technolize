using System.Text.Json;
namespace Technolize.World.Tag;

public class TagContainer : ITaggable
{

    public TagContainer()
    {
    }

    private readonly Dictionary<string, object> _tags = new ();

    public T? GetTag<T>(Tag<T> key) {
        return _tags.TryGetValue(key, out object? tag) ? key.Adaptor.Forwards(tag) : default(T?);
    }

    public bool HasTag<T>(Tag<T> key) {
        return _tags.ContainsKey(key);
    }

    public T? SetTag<T>(Tag<T> key, T tag)
    {
        AssertSafe(tag);

        T? previousTag = _tags.TryGetValue(key, out object? existingTag) && existingTag is T val ? val : default(T?);
        _tags[key] = key.Adaptor.Backwards(tag);
        return previousTag;
    }

    public TagStruct ToTagStruct() {
        return new TagStruct(_tags);
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
