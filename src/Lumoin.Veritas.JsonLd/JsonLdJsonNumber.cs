namespace Lumoin.Veritas.JsonLd;

/// <summary>
/// A number inside a <c>@json</c> literal, carrying its raw JSON lexical form.
/// </summary>
/// <remarks>
/// A <c>@json</c>-typed value is preserved verbatim during expansion (W3C
/// JSON-LD 1.1 §"JSON Literals"), including the exact lexical form of its
/// numbers (<c>0.0e0</c>, <c>4.50</c>, <c>1E30</c> are kept as written rather
/// than re-rendered from a parsed <see cref="double"/>/<see cref="long"/>).
/// This carrier holds that raw token so a consumer can emit it unchanged.
/// </remarks>
/// <param name="Raw">The raw JSON number token, exactly as it appeared in the source.</param>
public readonly record struct JsonLdJsonNumber(string Raw)
{
    /// <summary>Returns the raw JSON number token.</summary>
    /// <returns>The raw token.</returns>
    public override string ToString()
    {
        return Raw;
    }
}
