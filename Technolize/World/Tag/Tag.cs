namespace Technolize.World.Tag;

public record Tag<T>(string Key, TagAdaptor<object, T> Adaptor) {

    public Tag(string key) : this(key, CastAdaptor<object, T>()) { }

    // implicit conversions to/from string
    public static implicit operator string(Tag<T> tag) => tag.Key;
    public static implicit operator Tag<T>(string key) => new (key);

    private static TagAdaptor<TF, TT> CastAdaptor<TF, TT>() => new (
        tf => tf is TT val ? val : throw new InvalidCastException($"Cannot cast {tf?.GetType().Name ?? "null"} to {typeof(TT).Name}"),
        tt => tt is TF val ? val : throw new InvalidCastException($"Cannot cast {tt?.GetType().Name ?? "null"} to {typeof(TF).Name}")
    );

    public Tag<TO> Map<TO>(TagAdaptor<T, TO> adaptor) {
        return new Tag<TO>(Key, Adaptor.Then(adaptor));
    }

    public Tag<TO> Map<TO>(Func<T, TO> forwards, Func<TO, T> backwards) {
        return Map(new TagAdaptor<T, TO>(forwards, backwards));
    }

    public Tag<T> Default(Func<T> defaultValue) {
        return Map(
            t => t is null ? defaultValue() : t,
            o => o
        );
    }
}


public record TagAdaptor<F, T>(Func<F, T> Forwards, Func<T, F> Backwards) {

    public TagAdaptor<F, TH> Then<TH>(TagAdaptor<T, TH> next) {
        return new TagAdaptor<F, TH>(
            f => next.Forwards(Forwards(f)),
            h => Backwards(next.Backwards(h))
        );
    }
}
