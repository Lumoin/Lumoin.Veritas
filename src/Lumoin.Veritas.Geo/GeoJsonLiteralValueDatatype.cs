using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The value-layer definition of <c>geo:geoJSONLiteral</c>, declaring
/// <see cref="ValueDatatypeFacets.LexicalValidity"/> only. A lexical form is valid when its GeoJSON body
/// is recognized well-formed, invalid when the body is provably outside the GeoJSON geometry-object
/// grammar, and indeterminate when recognition abstains (an uncertified construct, or the nesting cap) —
/// an abstention leaves the engine's built-in acceptance standing. The spatial reference system is always
/// CRS84 by this datatype's own definition, so no spatial reference system appears in the lexical form.
/// An empty lexical form is the empty geometry and is well-formed, a verdict the recognizer answers
/// itself. <see cref="SameValue"/> abstains unconditionally: geometric identity is a computation over
/// parsed geometry, not lexical forms, so <c>=</c> stays at exact term identity for GeoJSON literals.
/// </summary>
public sealed class GeoJsonLiteralValueDatatype : ValueDatatype
{
    /// <summary>The shared definition instance; nothing registers it — a composing host does.</summary>
    public static GeoJsonLiteralValueDatatype Instance { get; } = new();

    /// <summary>Only <see cref="Instance"/> exists.</summary>
    private GeoJsonLiteralValueDatatype()
    {
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => GeoVocabulary.Geo.GeoJsonLiteral;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => ValueDatatypeFacets.LexicalValidity;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        return GeoJsonLexical.Recognize(lexicalForm.Span) switch
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
