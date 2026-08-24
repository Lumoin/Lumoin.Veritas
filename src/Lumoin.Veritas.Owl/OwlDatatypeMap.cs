using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// The OWL 2 datatype maps as IRI sets: the full OWL 2 DL map
/// (<see href="https://www.w3.org/TR/owl2-syntax/#Datatype_Maps">Syntax §4</see>)
/// and the profile restrictions of it
/// (<see href="https://www.w3.org/TR/owl2-profiles/">Profiles §2.4/§3.3/§4.2</see>).
/// <c>rdfs:Literal</c> is a member of every set — it denotes the data domain
/// itself.
/// </summary>
public static class OwlDatatypeMap
{
    /// <summary>The datatypes OWL 2 EL and QL admit (the shared list whose value spaces are infinite or trivial).</summary>
    public static IReadOnlySet<Utf8String> ElQl { get; } = new HashSet<Utf8String>
    {
        OwlVocabulary.Real,
        OwlVocabulary.Rational,
        RdfVocabulary.Rdfs.LiteralClass,
        Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#PlainLiteral"),
        Vocabulary.Rdf.XmlLiteral,
        Vocabulary.Xsd.Decimal,
        Vocabulary.Xsd.Integer,
        Vocabulary.Xsd.NonNegativeInteger,
        Vocabulary.Xsd.String,
        Vocabulary.Xsd.NormalizedString,
        Vocabulary.Xsd.Token,
        Vocabulary.Xsd.Name,
        Vocabulary.Xsd.NcName,
        Vocabulary.Xsd.NmToken,
        Vocabulary.Xsd.HexBinary,
        Vocabulary.Xsd.Base64Binary,
        Vocabulary.Xsd.AnyUri,
        Vocabulary.Xsd.DateTime,
        Vocabulary.Xsd.DateTimeStamp,
    };

    /// <summary>The datatypes OWL 2 RL admits (the full datatype map except <c>owl:real</c> and <c>owl:rational</c>).</summary>
    public static IReadOnlySet<Utf8String> Rl { get; } = new HashSet<Utf8String>
    {
        RdfVocabulary.Rdfs.LiteralClass,
        Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#PlainLiteral"),
        Vocabulary.Rdf.XmlLiteral,
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
        Vocabulary.Xsd.Double,
        Vocabulary.Xsd.Float,
        Vocabulary.Xsd.Boolean,
        Vocabulary.Xsd.String,
        Vocabulary.Xsd.NormalizedString,
        Vocabulary.Xsd.Token,
        Vocabulary.Xsd.Language,
        Vocabulary.Xsd.Name,
        Vocabulary.Xsd.NcName,
        Vocabulary.Xsd.NmToken,
        Vocabulary.Xsd.HexBinary,
        Vocabulary.Xsd.Base64Binary,
        Vocabulary.Xsd.AnyUri,
        Vocabulary.Xsd.DateTime,
        Vocabulary.Xsd.DateTimeStamp,
    };

    /// <summary>The full OWL 2 DL datatype map: the RL list plus <c>owl:real</c> and <c>owl:rational</c>.</summary>
    public static IReadOnlySet<Utf8String> Dl { get; } = BuildDl();

    private static HashSet<Utf8String> BuildDl()
    {
        HashSet<Utf8String> map = [.. Rl];
        map.Add(OwlVocabulary.Real);
        map.Add(OwlVocabulary.Rational);

        return map;
    }

    private static Utf8String Iri(string value)
    {
        return Utf8Strings.From(value);
    }
}
