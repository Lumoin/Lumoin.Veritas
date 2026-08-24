using System;
using System.Collections.Generic;
using System.Numerics;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Tests whether a literal's lexical form is a legal value of its XSD datatype's lexical space — for example,
/// <c>"aldi"^^xsd:integer</c> is ill-formed even though its datatype IRI is <c>xsd:integer</c>.
/// </summary>
/// <remarks>
/// <para>
/// A thin facade over the existing value parsers (<see cref="NumericValue"/>, <see cref="DateTimeValue"/>,
/// <see cref="XsdDuration"/>): a lexical form is valid for a modelled datatype exactly when that datatype's
/// parser accepts it. Datatypes outside the modelled XSD set (and <c>xsd:string</c> / language-tagged literals,
/// whose lexical space is unconstrained) are treated as valid — SHACL only rejects an ill-formed value for a
/// datatype it understands (SHACL 1.2 Core §6.3.2, and the XSD-datatypes well-formedness requirement) —
/// unless the caller's <see cref="ValueDatatypeRegistry"/> holds a definition for the datatype IRI that
/// declares <see cref="ValueDatatypeFacets.LexicalValidity"/> and rules the form provably invalid. A verdict
/// of <see cref="ValueLexicalValidity.Indeterminate"/> leaves the built-in acceptance standing, so an
/// abstaining definition can never reject a form.
/// </para>
/// <para>
/// The derived integer types (<c>xsd:byte</c>, <c>xsd:int</c>, <c>xsd:unsignedLong</c>,
/// <c>xsd:nonNegativeInteger</c>, …) constrain the value range as well as the lexical pattern; since
/// <see cref="NumericValue"/> parses every integer as an unbounded <see cref="BigInteger"/>, the bounds are
/// applied here.
/// </para>
/// </remarks>
public static class XsdLexicalValidity
{
    //Min/max bounds for the derived (bounded) integer datatypes; a null bound is open on that side. xsd:integer
    //itself is unbounded and is absent from the table.
    private static Dictionary<Utf8String, (BigInteger? Min, BigInteger? Max)> IntegerBounds { get; } = BuildIntegerBounds();

    /// <summary>
    /// Returns whether the datatype named by <paramref name="datatypeIri"/> is one whose lexical space this
    /// facade genuinely models — so an <see cref="IsValidLexicalForm"/> acceptance for it is a checked
    /// verdict (a parser's, or the unconstrained lexical space <c>xsd:string</c> genuinely has), never the
    /// default acceptance an unmodelled datatype receives. A consumer drawing an AFFIRMATIVE conclusion from
    /// validity must gate on this first; the default acceptance is safe only for silence-preserving uses.
    /// </summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns><see langword="true"/> when the datatype's lexical space is modelled.</returns>
    public static bool ModelsDatatype(Utf8String datatypeIri)
    {
        return ValueSpaceClassifier.Classify(datatypeIri) != ValueSpace.Unknown;
    }

    /// <summary>
    /// Returns whether <paramref name="lexicalForm"/> is a legal value of the datatype named by
    /// <paramref name="datatypeIri"/>'s lexical space. Unmodelled datatypes (and string/language-tagged literals)
    /// are accepted unless a definition in <paramref name="valueDatatypes"/> rules the form provably invalid.
    /// </summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="valueDatatypes">The value-layer datatype registry consulted for unmodelled datatype IRIs; <see cref="ValueDatatypeRegistry.Empty"/> preserves the built-in acceptance exactly.</param>
    /// <returns><see langword="true"/> when the lexical form is well-formed for the datatype.</returns>
    public static bool IsValidLexicalForm(Utf8String lexicalForm, Utf8String datatypeIri, ValueDatatypeRegistry valueDatatypes)
    {
        ArgumentNullException.ThrowIfNull(valueDatatypes);

        ValueSpace space = ValueSpaceClassifier.Classify(datatypeIri);

        return space switch
        {
            ValueSpace.Numeric => IsValidNumeric(lexicalForm, datatypeIri),
            ValueSpace.Boolean => IsValidBoolean(lexicalForm),
            ValueSpace.DateTime => DateTimeValue.TryParseDateTime(lexicalForm.Span, datatypeIri.Equals(Vocabulary.Xsd.DateTimeStamp), out _),
            ValueSpace.Date => DateTimeValue.TryParseDate(lexicalForm.Span, out _),
            ValueSpace.Time => DateTimeValue.TryParseTime(lexicalForm.Span, out _),
            ValueSpace.Duration or ValueSpace.YearMonthDuration or ValueSpace.DayTimeDuration => XsdDuration.TryParse(lexicalForm.ToString(), space, out _),

            //String, language-tagged, and unmodelled datatypes have no built-in lexical-form constraint; the
            //value-datatype registry may still constrain an unmodelled datatype IRI.
            _ => IsValidUnmodelled(lexicalForm, datatypeIri, valueDatatypes)
        };
    }

    /// <summary>
    /// Decides validity for a datatype IRI outside the modelled XSD set by consulting the value-layer
    /// registry. Accepts unless a registered definition declares
    /// <see cref="ValueDatatypeFacets.LexicalValidity"/> and answers
    /// <see cref="ValueLexicalValidity.Invalid"/>; an abstention leaves the acceptance standing. The
    /// reservation gate guarantees no XSD-namespace, RDF-namespace, or classifier-modelled IRI can carry a
    /// registration, so the modelled arms above are unreachable through this path.
    /// </summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <param name="datatypeIri">The unmodelled datatype IRI.</param>
    /// <param name="valueDatatypes">The value-layer datatype registry.</param>
    /// <returns><see langword="true"/> unless a registered definition proves the form invalid.</returns>
    private static bool IsValidUnmodelled(Utf8String lexicalForm, Utf8String datatypeIri, ValueDatatypeRegistry valueDatatypes)
    {
        if(valueDatatypes.IsEmpty
            || !valueDatatypes.TryGet(datatypeIri, out ValueDatatype? registered)
            || (registered.Facets & ValueDatatypeFacets.LexicalValidity) == ValueDatatypeFacets.None)
        {
            return true;
        }

        return registered.ValidateLexicalForm(lexicalForm) != ValueLexicalValidity.Invalid;
    }

    /// <summary>Returns whether a lexical form is a valid numeric value of its datatype, including the value-range bounds of the derived integer types.</summary>
    /// <param name="lexicalForm">The lexical form.</param>
    /// <param name="datatypeIri">The numeric datatype IRI.</param>
    /// <returns><see langword="true"/> when the lexical form parses and (for a bounded integer type) is in range.</returns>
    private static bool IsValidNumeric(Utf8String lexicalForm, Utf8String datatypeIri)
    {
        if(!NumericValue.TryParse(lexicalForm.ToString(), datatypeIri, out NumericValue value))
        {
            return false;
        }

        if(value.Kind != NumericKind.Integer || !IntegerBounds.TryGetValue(datatypeIri, out (BigInteger? Min, BigInteger? Max) bounds))
        {
            return true;
        }

        BigInteger integer = value.AsInteger();

        return (bounds.Min is not BigInteger min || integer >= min)
            && (bounds.Max is not BigInteger max || integer <= max);
    }

    /// <summary>Returns whether a lexical form is in the <c>xsd:boolean</c> lexical space (exactly <c>{true, false, 1, 0}</c>).</summary>
    /// <param name="lexicalForm">The lexical form.</param>
    /// <returns><see langword="true"/> when the form is a boolean lexical.</returns>
    private static bool IsValidBoolean(Utf8String lexicalForm)
    {
        ReadOnlySpan<byte> span = lexicalForm.Span;

        return span.SequenceEqual("true"u8) || span.SequenceEqual("false"u8) || span.SequenceEqual("1"u8) || span.SequenceEqual("0"u8);
    }

    /// <summary>Builds the value-range bounds for the derived integer datatypes.</summary>
    /// <returns>The bounds table keyed by datatype IRI.</returns>
    private static Dictionary<Utf8String, (BigInteger? Min, BigInteger? Max)> BuildIntegerBounds()
    {
        return new Dictionary<Utf8String, (BigInteger? Min, BigInteger? Max)>
        {
            [Vocabulary.Xsd.ByteValue] = (sbyte.MinValue, sbyte.MaxValue),
            [Vocabulary.Xsd.Short] = (short.MinValue, short.MaxValue),
            [Vocabulary.Xsd.Int] = (int.MinValue, int.MaxValue),
            [Vocabulary.Xsd.Long] = (long.MinValue, long.MaxValue),
            [Vocabulary.Xsd.UnsignedByte] = (0, byte.MaxValue),
            [Vocabulary.Xsd.UnsignedShort] = (0, ushort.MaxValue),
            [Vocabulary.Xsd.UnsignedInt] = (0, uint.MaxValue),
            [Vocabulary.Xsd.UnsignedLong] = (0, ulong.MaxValue),
            [Vocabulary.Xsd.NonNegativeInteger] = (0, null),
            [Vocabulary.Xsd.PositiveInteger] = (1, null),
            [Vocabulary.Xsd.NonPositiveInteger] = (null, 0),
            [Vocabulary.Xsd.NegativeInteger] = (null, -1),
        };
    }
}
