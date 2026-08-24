using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Classification of an XSD datatype IRI into a SPARQL value space.
/// </summary>
/// <remarks>
/// <para>
/// SPARQL 1.1 §17.4.1 defines ordering operators within a value space
/// and treats cross-space comparison as a type error. This enum is
/// the in-memory tag the comparator uses to dispatch: two operands
/// in the same space go to that space's comparator; operands in
/// different spaces produce <see cref="ComparisonResult.Incomparable"/>.
/// </para>
/// <para>
/// The numeric tower (<c>xsd:integer</c>, <c>xsd:decimal</c>,
/// <c>xsd:float</c>, <c>xsd:double</c>, plus the derived integer
/// types) all share <see cref="Numeric"/>; promotion within the
/// tower is handled inside the numeric comparator.
/// </para>
/// </remarks>
internal enum ValueSpace
{
    /// <summary>The datatype is not recognised as a comparable XSD type.</summary>
    Unknown,

    /// <summary>The numeric tower: integer (and derived), decimal, float, double.</summary>
    Numeric,

    /// <summary><c>xsd:string</c> (and language-tagged literals, which use string ordering).</summary>
    String,

    /// <summary><c>xsd:boolean</c>.</summary>
    Boolean,

    /// <summary><c>xsd:dateTime</c> or <c>xsd:dateTimeStamp</c>.</summary>
    DateTime,

    /// <summary><c>xsd:date</c>.</summary>
    Date,

    /// <summary><c>xsd:time</c>.</summary>
    Time,

    /// <summary><c>xsd:duration</c> — partially ordered.</summary>
    Duration,

    /// <summary><c>xsd:yearMonthDuration</c> — totally ordered subtype.</summary>
    YearMonthDuration,

    /// <summary><c>xsd:dayTimeDuration</c> — totally ordered subtype.</summary>
    DayTimeDuration,
}

/// <summary>
/// Classifier from datatype IRI to <see cref="ValueSpace"/>.
/// </summary>
internal static class ValueSpaceClassifier
{
    //Lookup keyed on the datatype IRI as Utf8String. Built once per
    //AppDomain at type init; lookups are O(1) hash table accesses.
    //Keying on Utf8String avoids materialising strings for the
    //comparison; the existing Utf8String equality / hash machinery
    //handles the byte-level match.
    private static Dictionary<Utf8String, ValueSpace> Map { get; } = BuildMap();

    /// <summary>
    /// Classifies the given datatype IRI. Returns
    /// <see cref="ValueSpace.Unknown"/> for unrecognised IRIs.
    /// </summary>
    public static ValueSpace Classify(Utf8String datatypeIri)
        => Map.TryGetValue(datatypeIri, out ValueSpace space) ? space : ValueSpace.Unknown;

    private static Dictionary<Utf8String, ValueSpace> BuildMap()
    {
        Dictionary<Utf8String, ValueSpace> map = new()
        {
            //Numeric tower — primary types.
            [Vocabulary.Xsd.Integer] = ValueSpace.Numeric,
            [Vocabulary.Xsd.Decimal] = ValueSpace.Numeric,
            [Vocabulary.Xsd.Float] = ValueSpace.Numeric,
            [Vocabulary.Xsd.Double] = ValueSpace.Numeric,

            //Numeric tower — derived integer types. All compare as
            //integers; the lexical-form parser produces a BigInteger.
            //ByteValue is the project's name for the xsd:byte IRI.
            [Vocabulary.Xsd.Long] = ValueSpace.Numeric,
            [Vocabulary.Xsd.Int] = ValueSpace.Numeric,
            [Vocabulary.Xsd.Short] = ValueSpace.Numeric,
            [Vocabulary.Xsd.ByteValue] = ValueSpace.Numeric,
            [Vocabulary.Xsd.UnsignedLong] = ValueSpace.Numeric,
            [Vocabulary.Xsd.UnsignedInt] = ValueSpace.Numeric,
            [Vocabulary.Xsd.UnsignedShort] = ValueSpace.Numeric,
            [Vocabulary.Xsd.UnsignedByte] = ValueSpace.Numeric,
            [Vocabulary.Xsd.NonNegativeInteger] = ValueSpace.Numeric,
            [Vocabulary.Xsd.NonPositiveInteger] = ValueSpace.Numeric,
            [Vocabulary.Xsd.PositiveInteger] = ValueSpace.Numeric,
            [Vocabulary.Xsd.NegativeInteger] = ValueSpace.Numeric,

            //String and boolean.
            [Vocabulary.Xsd.String] = ValueSpace.String,
            [Vocabulary.Xsd.Boolean] = ValueSpace.Boolean,

            //Date/time family. dateTimeStamp is a restriction of
            //dateTime requiring a timezone — same value space, the
            //parser enforces the timezone-required invariant when it
            //sees the dateTimeStamp datatype.
            [Vocabulary.Xsd.DateTime] = ValueSpace.DateTime,
            [Vocabulary.Xsd.DateTimeStamp] = ValueSpace.DateTime,
            [Vocabulary.Xsd.Date] = ValueSpace.Date,
            [Vocabulary.Xsd.Time] = ValueSpace.Time,

            //Duration family. The general xsd:duration is partially
            //ordered; the two restricted subtypes are totally ordered
            //within their kind. A yearMonthDuration vs a
            //dayTimeDuration is incomparable.
            [Vocabulary.Xsd.Duration] = ValueSpace.Duration,
            [Vocabulary.Xsd.YearMonthDuration] = ValueSpace.YearMonthDuration,
            [Vocabulary.Xsd.DayTimeDuration] = ValueSpace.DayTimeDuration,
        };

        return map;
    }
}
