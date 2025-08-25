namespace Technolize.World.Tag;

public interface ITaggableStruct<out TS> : ITagged where TS : ITaggableStruct<TS>
{
    /// <summary>
    /// Recreates this struct with the specified tag.
    /// </summary>
    /// <param name="key">The key to associate with the tag.</param>
    /// <param name="tag">The value to set as a tag.</param>
    /// <typeparam name="T">The type of the tag to set.</typeparam>
    /// <returns>A new instance of the struct with the specified tag.</returns>
    TS WithTag<T>(Tag<T> key, T tag);
}
