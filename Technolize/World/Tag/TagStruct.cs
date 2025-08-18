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

    public T? GetTag<T>(string key) where T : class
    {
        if (_tags.TryGetValue(key, out object? tag))
        {
            return tag as T;
        }
        return null;
    }

    public TagStruct WithTag<T>(string key, T tag) where T : class
    {
        if (tag is null)
        {
            throw new ArgumentNullException(nameof(tag), "Tag cannot be null");
        }

        AssertSafe(tag);

        Dictionary<string, object> newTags = new Dictionary<string, object>(_tags)
        {
            [key] = tag
        };
        return new TagStruct(newTags);
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
