using System.Text.Json;
namespace Technolize.World.Tag;

public class TagContainer : ITaggable
{

    public TagContainer()
    {
    }

    private readonly Dictionary<string, object> _tags = new ();

    public T? GetTag<T>(string key) where T : class
    {
        if (_tags.TryGetValue(key, out var tag))
        {
            return tag as T;
        }
        return null;
    }

    public T? SetTag<T>(string key, T tag) where T : class
    {
        if (tag is null)
        {
            throw new ArgumentNullException(nameof(tag), "Tag cannot be null");
        }

        AssertSafe(tag);

        var previousTag = _tags.TryGetValue(key, out var existingTag) ? existingTag as T : null;
        _tags[key] = tag;
        return previousTag;
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
