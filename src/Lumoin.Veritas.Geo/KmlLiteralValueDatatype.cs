using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The value-layer definition of <c>geo:kmlLiteral</c>, declaring
/// <see cref="ValueDatatypeFacets.LexicalValidity"/> only. A lexical form is valid when its KML body is
/// recognized well-formed, invalid when the body is provably outside the KML geometry grammar, and
/// indeterminate when recognition abstains (an uncertified element or construct, or the nesting cap) —
/// an abstention leaves the engine's built-in acceptance standing. The spatial reference system is always
/// CRS84 by this datatype's own definition, so no spatial reference system appears in the lexical form.
/// An empty lexical form is the empty geometry and is well-formed, a verdict the recognizer answers
/// itself. <see cref="SameValue"/> abstains unconditionally: geometric identity is a computation over
/// parsed geometry, not lexical forms, so <c>=</c> stays at exact term identity for KML literals.
/// </summary>
public sealed class KmlLiteralValueDatatype : ValueDatatype
{
    /// <summary>The shared definition instance; nothing registers it — a composing host does.</summary>
    public static KmlLiteralValueDatatype Instance { get; } = new();

    /// <summary>Only <see cref="Instance"/> exists.</summary>
    private KmlLiteralValueDatatype()
    {
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => GeoVocabulary.Geo.KmlLiteral;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => ValueDatatypeFacets.LexicalValidity;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        return KmlLexical.Recognize(lexicalForm.Span) switch
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
