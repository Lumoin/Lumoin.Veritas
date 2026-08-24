using System.Collections.Frozen;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Datatypes;

/// <summary>
/// The value-space family of an OWL 2 datatype. Datatypes in different
/// families have disjoint value spaces in the OWL 2 datatype map, so a value
/// constrained to two different families at once cannot exist —
/// <see cref="Literal"/> is the sole exception, denoting the whole data
/// domain (a superset of every family).
/// </summary>
public enum OwlDatatypeFamily
{
    /// <summary>The datatype is not in the modelled map; its value space is unknown.</summary>
    Unknown = 0,

    /// <summary>The <c>rdfs:Literal</c> universal datatype — the whole data domain.</summary>
    Literal,

    /// <summary>The numeric line (<c>owl:real</c>/<c>rational</c>, the <c>xsd:decimal</c>/<c>integer</c> tower, <c>xsd:float</c>, <c>xsd:double</c>).</summary>
    Numeric,

    /// <summary>The two-element <c>xsd:boolean</c> value space.</summary>
    Boolean,

    /// <summary>The string-like datatypes (<c>xsd:string</c> and its lexical restrictions).</summary>
    Text,

    /// <summary>The date/time datatypes (<c>xsd:dateTime</c>, <c>date</c>, <c>time</c>, …).</summary>
    Temporal,

    /// <summary>The binary datatypes (<c>xsd:hexBinary</c>, <c>xsd:base64Binary</c>).</summary>
    Binary,

    /// <summary>The <c>xsd:anyURI</c> value space.</summary>
    AnyUri,

    /// <summary>The <c>rdf:XMLLiteral</c> value space — XML content, whose value identity is exclusive Canonical XML equality.</summary>
    XmlLiteral,
}

/// <summary>
/// The numeric value space a numeric datatype belongs to. The OWL 2 spec
/// gives <c>xsd:float</c> and <c>xsd:double</c> value spaces that are fresh
/// copies disjoint from the exact-real line (and from each other): a value
/// of one is never a value of another, so the three are kept apart.
/// </summary>
public enum OwlNumericSpace
{
    /// <summary>The datatype is not numeric.</summary>
    None = 0,

    /// <summary>The exact-real line: <c>owl:real</c>, <c>owl:rational</c>, <c>xsd:decimal</c>, and the <c>xsd:integer</c> tower.</summary>
    ExactReal,

    /// <summary>The <c>xsd:float</c> value space (IEEE single, with <c>NaN</c>, infinities, and signed zero).</summary>
    Float,

    /// <summary>The <c>xsd:double</c> value space (IEEE double, with <c>NaN</c>, infinities, and signed zero).</summary>
    Double,
}

/// <summary>
/// Classifies a datatype IRI into its value-space family and, for numeric
/// datatypes, its numeric sub-space. This generalises the families the RL
/// term-id oracle reads, refining the numeric family into the three disjoint
/// numeric spaces the datatype satisfiability checker needs.
/// </summary>
public static class OwlDatatypeFamilies
{
    /// <summary>The exact-real numeric datatypes — one shared, totally ordered value space the interval algebra reasons over.</summary>
    private static FrozenSet<Utf8String> ExactRealIris { get; } = new HashSet<Utf8String>
    {
        OwlVocabulary.Real,
        OwlVocabulary.Rational,
        Vocabulary.Xsd.Decimal,
        Vocabulary.Xsd.Integer,
        Vocabulary.Xsd.NonNegativeInteger,
        Vocabulary.Xsd.NonPositiveInteger,
        Vocabulary.Xsd.PositiveInteger,
        Vocabulary.Xsd.NegativeInteger,
        Vocabulary.Xsd.Long,
        Vocabulary.Xsd.Int,
        Vocabulary.Xsd.Short,
        Vocabulary.Xsd.ByteValue,
        Vocabulary.Xsd.UnsignedLong,
        Vocabulary.Xsd.UnsignedInt,
        Vocabulary.Xsd.UnsignedShort,
        Vocabulary.Xsd.UnsignedByte,
    }.ToFrozenSet();

    /// <summary>The string-like datatypes that share the string value space's lexical character.</summary>
    private static FrozenSet<Utf8String> TextIris { get; } = new HashSet<Utf8String>
    {
        Vocabulary.Xsd.String,
        Vocabulary.Xsd.NormalizedString,
        Vocabulary.Xsd.Token,
        Vocabulary.Xsd.Language,
        Vocabulary.Xsd.Name,
        Vocabulary.Xsd.NcName,
        Vocabulary.Xsd.NmToken,
    }.ToFrozenSet();

    /// <summary>The date/time datatypes whose values are temporal points.</summary>
    private static FrozenSet<Utf8String> TemporalIris { get; } = new HashSet<Utf8String>
    {
        Vocabulary.Xsd.DateTime,
        Vocabulary.Xsd.DateTimeStamp,
        Vocabulary.Xsd.Date,
        Vocabulary.Xsd.Time,
    }.ToFrozenSet();

    /// <summary>The binary datatypes whose values are octet sequences.</summary>
    private static FrozenSet<Utf8String> BinaryIris { get; } = new HashSet<Utf8String>
    {
        Vocabulary.Xsd.HexBinary,
        Vocabulary.Xsd.Base64Binary,
    }.ToFrozenSet();

    /// <summary>
    /// Classifies a datatype IRI into its value-space family.
    /// </summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The family, or <see cref="OwlDatatypeFamily.Unknown"/> when the datatype is outside the modelled map.</returns>
    public static OwlDatatypeFamily Classify(Utf8String datatypeIri)
    {
        if(datatypeIri.Equals(RdfVocabulary.Rdfs.LiteralClass))
        {
            return OwlDatatypeFamily.Literal;
        }

        if(NumericSpaceOf(datatypeIri) != OwlNumericSpace.None)
        {
            return OwlDatatypeFamily.Numeric;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Boolean))
        {
            return OwlDatatypeFamily.Boolean;
        }

        if(TemporalIris.Contains(datatypeIri))
        {
            return OwlDatatypeFamily.Temporal;
        }

        if(BinaryIris.Contains(datatypeIri))
        {
            return OwlDatatypeFamily.Binary;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.AnyUri))
        {
            return OwlDatatypeFamily.AnyUri;
        }

        if(TextIris.Contains(datatypeIri))
        {
            return OwlDatatypeFamily.Text;
        }

        if(datatypeIri.Equals(Vocabulary.Rdf.XmlLiteral))
        {
            return OwlDatatypeFamily.XmlLiteral;
        }

        return OwlDatatypeFamily.Unknown;
    }

    /// <summary>
    /// Returns the numeric sub-space of a numeric datatype IRI.
    /// </summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The numeric space, or <see cref="OwlNumericSpace.None"/> when the datatype is not numeric.</returns>
    public static OwlNumericSpace NumericSpaceOf(Utf8String datatypeIri)
    {
        if(datatypeIri.Equals(Vocabulary.Xsd.Float))
        {
            return OwlNumericSpace.Float;
        }

        if(datatypeIri.Equals(Vocabulary.Xsd.Double))
        {
            return OwlNumericSpace.Double;
        }

        if(ExactRealIris.Contains(datatypeIri))
        {
            return OwlNumericSpace.ExactReal;
        }

        return OwlNumericSpace.None;
    }
}
