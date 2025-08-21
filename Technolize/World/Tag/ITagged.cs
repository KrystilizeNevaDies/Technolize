namespace Technolize.World.Tag;

public interface ITagged
{
    /// <summary>
    /// Gets a tag of type T from the object.
    /// </summary>
    /// <param name="key">The key associated with the tag.</param>
    /// <typeparam name="T">The type of the tag to get.</typeparam>
    /// <returns>The tag of type T, or null if no such tag exists.</returns
    T? GetTag<T>(string key) where T : class;

    public static readonly TagStruct Empty = new (new ());
}
