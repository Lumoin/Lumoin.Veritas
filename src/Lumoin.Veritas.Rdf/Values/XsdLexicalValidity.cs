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
/// datatype it understands (SHACL 1.2 Core §6.3.2, and the XSD-datatypes well-formedness requirement).
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
    /// Returns whether <paramref name="lexicalForm"/> is a legal value of the datatype named by
    /// <paramref name="datatypeIri"/>'s lexical space. Unmodelled datatypes (and string/language-tagged literals)
    /// are accepted.
    /// </summary>
    /// <param name="lexicalForm">The literal's lexical form.</param>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <returns><see langword="true"/> when the lexical form is well-formed for the datatype.</returns>
    public static bool IsValidLexicalForm(Utf8String lexicalForm, Utf8String datatypeIri)
    {
        ValueSpace space = ValueSpaceClassifier.Classify(datatypeIri);

        return space switch
        {
            ValueSpace.Numeric => IsValidNumeric(lexicalForm, datatypeIri),
            ValueSpace.Boolean => IsValidBoolean(lexicalForm),
            ValueSpace.DateTime => DateTimeValue.TryParseDateTime(lexicalForm.ToString(), datatypeIri.Equals(Vocabulary.Xsd.DateTimeStamp), out _),
            ValueSpace.Date => DateTimeValue.TryParseDate(lexicalForm.ToString(), out _),
            ValueSpace.Time => DateTimeValue.TryParseTime(lexicalForm.ToString(), out _),
            ValueSpace.Duration or ValueSpace.YearMonthDuration or ValueSpace.DayTimeDuration => XsdDuration.TryParse(lexicalForm.ToString(), space, out _),

            //String, language-tagged, and unmodelled datatypes have no lexical-form constraint this checker enforces.
            _ => true
        };
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
