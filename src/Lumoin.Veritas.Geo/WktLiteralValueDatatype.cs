using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The value-layer definition of <c>geo:wktLiteral</c>, declaring
/// <see cref="ValueDatatypeFacets.LexicalValidity"/> only. A lexical form is valid when its CRS prefix
/// structure parses and its WKT body is recognized well-formed, invalid when either is provably broken,
/// and indeterminate when recognition abstains (an uncertified curve tag, or the nesting cap) — an
/// abstention leaves the engine's built-in acceptance standing. <see cref="SameValue"/> abstains
/// unconditionally: geometric identity is a computation over parsed geometry, not lexical forms, so
/// <c>=</c> stays at exact term identity for WKT literals.
/// </summary>
public sealed class WktLiteralValueDatatype : ValueDatatype
{
    /// <summary>The shared definition instance; nothing registers it — a composing host does.</summary>
    public static WktLiteralValueDatatype Instance { get; } = new();

    /// <summary>Only <see cref="Instance"/> exists.</summary>
    private WktLiteralValueDatatype()
    {
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => GeoVocabulary.Geo.WktLiteral;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => ValueDatatypeFacets.LexicalValidity;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        if(!WktCrsPrefix.TryParse(lexicalForm, out WktCrsPrefix decomposition))
        {
            return ValueLexicalValidity.Invalid;
        }

        return WktLexical.Recognize(decomposition.Body.Span, out _) switch
        {
            GeometryLexicalRecognition.WellFormed => ValueLexicalValidity.Valid,
            GeometryLexicalRecognition.Malformed => ValueLexicalValidity.Invalid,
            _ => ValueLexicalValidity.Indeterminate,
        };
    }

    /// <inheritdoc/>
    public override ValueIdentity SameValue(Utf8String first, Utf8String second)
    {
        return ValueIdentity.Indeterminate;
    }
}
