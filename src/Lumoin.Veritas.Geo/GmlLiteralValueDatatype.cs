using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The value-layer definition of <c>geo:gmlLiteral</c>, declaring
/// <see cref="ValueDatatypeFacets.LexicalValidity"/> only. A lexical form is valid when its GML body is
/// recognized well-formed, invalid when the body is provably outside the GML grammar, and indeterminate
/// when recognition abstains (an uncertified element or construct, or the nesting cap) — an abstention
/// leaves the engine's built-in acceptance standing. The supported profile is GML 3.2 (OGC 07-036); a
/// form carries no CRS prefix, because a GML element names its own spatial reference system in its
/// <c>srsName</c> attribute, whose value is semantics rather than lexical shape. An empty lexical form is
/// the empty geometry and is well-formed, a verdict the recognizer answers itself.
/// <see cref="SameValue"/> abstains unconditionally: geometric identity is a computation over parsed
/// geometry, not lexical forms, so <c>=</c> stays at exact term identity for GML literals.
/// </summary>
public sealed class GmlLiteralValueDatatype : ValueDatatype
{
    /// <summary>The shared definition instance; nothing registers it — a composing host does.</summary>
    public static GmlLiteralValueDatatype Instance { get; } = new();

    /// <summary>Only <see cref="Instance"/> exists.</summary>
    private GmlLiteralValueDatatype()
    {
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => GeoVocabulary.Geo.GmlLiteral;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => ValueDatatypeFacets.LexicalValidity;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        return GmlLexical.Recognize(lexicalForm.Span) switch
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
