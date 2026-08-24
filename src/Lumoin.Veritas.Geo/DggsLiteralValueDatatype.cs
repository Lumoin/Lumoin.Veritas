using Lumoin.Veritas.Rdf.Values;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The value-layer definition of <c>geo:dggsLiteral</c>, declaring
/// <see cref="ValueDatatypeFacets.LexicalValidity"/> only. A lexical form is valid when it is empty (the
/// empty geometry) or a conformant house-flavour form (the recognizer certifies the whole body when the
/// prefix carries <see cref="A5DggsVocabulary.GridIri"/>), invalid when it is provably outside the
/// literal grammar — a non-empty form without the angle-bracket IRI prefix, an unterminated or empty IRI
/// region, a raw whitespace or control character inside the brackets, a missing whitespace separator, a
/// prefix without geometry data, or a non-conformant house-flavour body — and indeterminate for every
/// non-empty geometry-data body after a valid FOREIGN-grid prefix, because that data is formulated
/// according to the DGGS the IRI identifies and lies outside this definition's jurisdiction — an
/// abstention leaves the engine's built-in acceptance standing.
/// <see cref="SameValue"/> abstains unconditionally under this generic datatype; the house
/// <see cref="A5DggsLiteralValueDatatype"/> subclass decides value identity for its own flavour.
/// </summary>
public sealed class DggsLiteralValueDatatype : ValueDatatype
{
    /// <summary>The shared definition instance; nothing registers it — a composing host does.</summary>
    public static DggsLiteralValueDatatype Instance { get; } = new();

    /// <summary>Only <see cref="Instance"/> exists.</summary>
    private DggsLiteralValueDatatype()
    {
    }

    /// <inheritdoc/>
    public override Utf8String DatatypeIri => GeoVocabulary.Geo.DggsLiteral;

    /// <inheritdoc/>
    public override ValueDatatypeFacets Facets => ValueDatatypeFacets.LexicalValidity;

    /// <inheritdoc/>
    public override ValueLexicalValidity ValidateLexicalForm(Utf8String lexicalForm)
    {
        return DggsLexical.Recognize(lexicalForm.Span, out _) switch
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
