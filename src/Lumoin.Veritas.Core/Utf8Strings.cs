namespace Lumoin.Veritas.Core;

/// <summary>
/// Pooled construction of <see cref="Utf8String"/> from .NET strings. The shared <see cref="Lumoin.Base.Utf8String"/>
/// is materialization-pool-only — it has no heap factory — so Veritas interns string text through one shared,
/// bounded, thread-safe <see cref="Lumoin.Base.Utf8StringInterner"/>: equal text returns one shared value, and the
/// interner evicts its cold entries so the table stays bounded. Use this for term and vocabulary text built from
/// managed strings; where the UTF-8 bytes are already in hand, construct over them directly instead.
/// </summary>
public static class Utf8Strings
{
    /// <summary>The shared, bounded, thread-safe interner backing <see cref="From(string)"/>.</summary>
    private static Lumoin.Base.Utf8StringInterner Interner { get; } = new();

    /// <summary>Interns <paramref name="value"/> as UTF-8 and returns the shared pooled <see cref="Utf8String"/>.</summary>
    /// <param name="value">The string to intern.</param>
    /// <returns>The interned UTF-8 string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Utf8String From(string value)
    {
        return Interner.Intern(value);
    }
}
