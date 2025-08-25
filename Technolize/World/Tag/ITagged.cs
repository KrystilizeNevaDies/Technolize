namespace Technolize.World.Tag;

public interface ITagged
{
    /// <summary>
    /// Gets a tag of type T from the object.
    /// </summary>
    /// <param name="key">The key associated with the tag.</param>
    /// <typeparam name="T">The type of the tag to get.</typeparam>
    /// <returns>The tag of type T, or null if no such tag exists.</returns
    public T? GetTag<T>(Tag<T> key);
    public bool HasTag<T>(Tag<T> key);
    public bool TryGetTag<T>(Tag<T> key, out T? value) {
        if (HasTag(key)) {
            value = GetTag(key);
            return true;
        }
        value = default(T);
        return false;
    }

    public static readonly TagStruct Empty = new (new ());
}
