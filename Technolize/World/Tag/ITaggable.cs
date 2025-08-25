namespace Technolize.World.Tag;

public interface ITaggable : ITagged
{
    /// <summary>
    /// Sets a tag of type T on the object.
    /// </summary>
    /// <param name="key">The key to associate with the tag.</param>
    /// <param name="tag">The value to set as a tag.</param>
    /// <typeparam name="T">The type of the tag to set.</typeparam>
    /// <returns>The previous tag of type T, or null if no such tag existed.</returns>
    T? SetTag<T>(Tag<T> key, T tag);
}
