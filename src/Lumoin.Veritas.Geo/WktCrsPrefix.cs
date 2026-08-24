using System;

namespace Lumoin.Veritas.Geo;

/// <summary>The origin of the coordinate reference system IRI in a decomposed <c>geo:wktLiteral</c>.</summary>
public enum WktCrsSource
{
    /// <summary>The lexical form carries no IRI prefix; the default CRS84 applies — the zero default.</summary>
    Defaulted = 0,

    /// <summary>The lexical form names its coordinate reference system in an explicit <c>&lt;IRI&gt;</c> prefix.</summary>
    Explicit,
}

/// <summary>
/// The decomposition of a <c>geo:wktLiteral</c> lexical form into its coordinate reference system IRI and
/// its WKT geometry body: an optional <c>&lt;IRI&gt;</c> prefix followed by whitespace and the WKT text,
/// with CRS84 assumed when the prefix is absent. <see cref="CrsIri"/> and <see cref="Body"/> are zero-copy
/// slices of the parsed input (the defaulted CRS IRI is the one shared constant), and <see cref="Source"/>
/// records which case produced the CRS, so a caller never re-parses to distinguish them.
/// </summary>
public readonly record struct WktCrsPrefix
{
    /// <summary>The default CRS IRI bytes (<c>http://www.opengis.net/def/crs/OGC/1.3/CRS84</c>).</summary>
    private static byte[] DefaultCrsIriBytes { get; } = "http://www.opengis.net/def/crs/OGC/1.3/CRS84"u8.ToArray();

    /// <summary>The CRS IRI assumed for a lexical form with no explicit prefix.</summary>
    public static Utf8String DefaultCrsIri { get; } = new(DefaultCrsIriBytes);

    /// <summary>The coordinate reference system IRI: the explicit prefix's IRI, or <see cref="DefaultCrsIri"/>.</summary>
    public Utf8String CrsIri { get; }

    /// <summary>The WKT geometry body after the prefix and separating whitespace; empty for an empty geometry.</summary>
    public Utf8String Body { get; }

    /// <summary>Whether the CRS IRI was explicit in the lexical form or defaulted.</summary>
    public WktCrsSource Source { get; }

    /// <summary>Carries a completed decomposition; only <see cref="TryParse"/> constructs one.</summary>
    /// <param name="crsIri">The coordinate reference system IRI.</param>
    /// <param name="body">The WKT geometry body.</param>
    /// <param name="source">The origin of the CRS IRI.</param>
    private WktCrsPrefix(Utf8String crsIri, Utf8String body, WktCrsSource source)
    {
        CrsIri = crsIri;
        Body = body;
        Source = source;
    }

    /// <summary>
    /// Decomposes a <c>geo:wktLiteral</c> lexical form. A form with no <c>&lt;</c> after optional leading
    /// whitespace is entirely body under the defaulted CRS; an explicit prefix must close with <c>&gt;</c>,
    /// carry a non-empty IRI free of whitespace and angle brackets, and be separated from a non-empty body
    /// by at least one whitespace byte. An empty or all-whitespace remainder is an empty body — the empty
    /// geometry's lexical form.
    /// </summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <param name="result">The decomposition, when the form's prefix structure is valid.</param>
    /// <returns><see langword="true"/> when the prefix structure is valid; the body's WKT grammar is not examined.</returns>
    public static bool TryParse(Utf8String lexicalForm, out WktCrsPrefix result)
    {
        ReadOnlySpan<byte> span = lexicalForm.Span;
        int index = 0;
        while(index < span.Length && WktLexical.IsWhitespace(span[index]))
        {
            index++;
        }

        if(index == span.Length || span[index] != (byte)'<')
        {
            result = new WktCrsPrefix(DefaultCrsIri, lexicalForm.Slice(index), WktCrsSource.Defaulted);

            return true;
        }

        int iriStart = index + 1;
        int close = -1;
        for(int i = iriStart; i < span.Length; i++)
        {
            byte value = span[i];
            if(value == (byte)'>')
            {
                close = i;
                break;
            }

            if(value == (byte)'<' || WktLexical.IsWhitespace(value))
            {
                result = default;

                return false;
            }
        }

        if(close < 0 || close == iriStart)
        {
            result = default;

            return false;
        }

        Utf8String crsIri = lexicalForm.Slice(iriStart, close - iriStart);
        int bodyIndex = close + 1;
        if(bodyIndex == span.Length)
        {
            result = new WktCrsPrefix(crsIri, lexicalForm.Slice(bodyIndex), WktCrsSource.Explicit);

            return true;
        }

        if(!WktLexical.IsWhitespace(span[bodyIndex]))
        {
            result = default;

            return false;
        }

        while(bodyIndex < span.Length && WktLexical.IsWhitespace(span[bodyIndex]))
        {
            bodyIndex++;
        }

        result = new WktCrsPrefix(crsIri, lexicalForm.Slice(bodyIndex), WktCrsSource.Explicit);

        return true;
    }
}
