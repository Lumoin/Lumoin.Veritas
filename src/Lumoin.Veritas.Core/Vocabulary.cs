using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Well-known IRI constants from the XSD and RDF vocabularies that are
/// directly referenced by the Core data model types.
/// </summary>
/// <remarks>
/// <para>
/// These are allocated once as static byte arrays. Since <see cref="Utf8String"/>
/// is a struct wrapping <see cref="ReadOnlyMemory{T}"/>, these do not participate
/// in pool allocation and remain valid for the lifetime of the application.
/// </para>
/// <para>
/// Only the terms that define the RDF data model are present here: XSD datatypes,
/// <c>rdf:type</c>, the RDF datatype IRIs (<c>rdf:langString</c>, <c>rdf:dirLangString</c>,
/// <c>rdf:JSON</c>, <c>rdf:HTML</c>, <c>rdf:XMLLiteral</c>), and <c>rdf:reifies</c>
/// which is paired with <see cref="TripleTerm"/>. All other RDF and RDFS vocabulary
/// terms live in <c>Lumoin.Veritas.Rdf.RdfVocabulary</c>.
/// </para>
/// </remarks>
public static class Vocabulary
{
    /// <summary>
    /// The XML Schema namespace: <c>http://www.w3.org/2001/XMLSchema#</c>.
    /// </summary>
    /// <remarks>
    /// Datatypes defined in <see href="https://www.w3.org/TR/xmlschema11-2/">XSD 1.1 Part 2</see>,
    /// referenced by <see href="https://www.w3.org/TR/rdf12-concepts/#section-Datatypes">RDF 1.2 Concepts §5</see>.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Vocabulary.Xsd.String is the intended usage pattern.")]
    public static class Xsd
    {
        /// <summary>The XSD namespace IRI.</summary>
        public const string Namespace = "http://www.w3.org/2001/XMLSchema#";

        private static readonly byte[] StringBytes = "http://www.w3.org/2001/XMLSchema#string"u8.ToArray();
        private static readonly byte[] BooleanBytes = "http://www.w3.org/2001/XMLSchema#boolean"u8.ToArray();
        private static readonly byte[] IntegerBytes = "http://www.w3.org/2001/XMLSchema#integer"u8.ToArray();
        private static readonly byte[] DoubleBytes = "http://www.w3.org/2001/XMLSchema#double"u8.ToArray();
        private static readonly byte[] DecimalBytes = "http://www.w3.org/2001/XMLSchema#decimal"u8.ToArray();
        private static readonly byte[] DateTimeBytes = "http://www.w3.org/2001/XMLSchema#dateTime"u8.ToArray();
        private static readonly byte[] DateTimeStampBytes = "http://www.w3.org/2001/XMLSchema#dateTimeStamp"u8.ToArray();
        private static readonly byte[] DateBytes = "http://www.w3.org/2001/XMLSchema#date"u8.ToArray();
        private static readonly byte[] TimeBytes = "http://www.w3.org/2001/XMLSchema#time"u8.ToArray();
        private static readonly byte[] FloatBytes = "http://www.w3.org/2001/XMLSchema#float"u8.ToArray();
        private static readonly byte[] LongBytes = "http://www.w3.org/2001/XMLSchema#long"u8.ToArray();
        private static readonly byte[] IntBytes = "http://www.w3.org/2001/XMLSchema#int"u8.ToArray();
        private static readonly byte[] ShortBytes = "http://www.w3.org/2001/XMLSchema#short"u8.ToArray();
        private static readonly byte[] ByteValueBytes = "http://www.w3.org/2001/XMLSchema#byte"u8.ToArray();
        private static readonly byte[] NonNegativeIntegerBytes = "http://www.w3.org/2001/XMLSchema#nonNegativeInteger"u8.ToArray();
        private static readonly byte[] NonPositiveIntegerBytes = "http://www.w3.org/2001/XMLSchema#nonPositiveInteger"u8.ToArray();
        private static readonly byte[] PositiveIntegerBytes = "http://www.w3.org/2001/XMLSchema#positiveInteger"u8.ToArray();
        private static readonly byte[] NegativeIntegerBytes = "http://www.w3.org/2001/XMLSchema#negativeInteger"u8.ToArray();
        private static readonly byte[] UnsignedLongBytes = "http://www.w3.org/2001/XMLSchema#unsignedLong"u8.ToArray();
        private static readonly byte[] UnsignedIntBytes = "http://www.w3.org/2001/XMLSchema#unsignedInt"u8.ToArray();
        private static readonly byte[] UnsignedShortBytes = "http://www.w3.org/2001/XMLSchema#unsignedShort"u8.ToArray();
        private static readonly byte[] UnsignedByteBytes = "http://www.w3.org/2001/XMLSchema#unsignedByte"u8.ToArray();
        private static readonly byte[] DurationBytes = "http://www.w3.org/2001/XMLSchema#duration"u8.ToArray();
        private static readonly byte[] YearMonthDurationBytes = "http://www.w3.org/2001/XMLSchema#yearMonthDuration"u8.ToArray();
        private static readonly byte[] DayTimeDurationBytes = "http://www.w3.org/2001/XMLSchema#dayTimeDuration"u8.ToArray();
        private static readonly byte[] AnyUriBytes = "http://www.w3.org/2001/XMLSchema#anyURI"u8.ToArray();
        private static readonly byte[] Base64BinaryBytes = "http://www.w3.org/2001/XMLSchema#base64Binary"u8.ToArray();
        private static readonly byte[] HexBinaryBytes = "http://www.w3.org/2001/XMLSchema#hexBinary"u8.ToArray();
        private static readonly byte[] NormalizedStringBytes = "http://www.w3.org/2001/XMLSchema#normalizedString"u8.ToArray();
        private static readonly byte[] TokenBytes = "http://www.w3.org/2001/XMLSchema#token"u8.ToArray();

        /// <summary>The <c>xsd:string</c> datatype IRI.</summary>
        public static Utf8String String { get; } = new(StringBytes);

        /// <summary>The <c>xsd:boolean</c> datatype IRI.</summary>
        public static Utf8String Boolean { get; } = new(BooleanBytes);

        /// <summary>The <c>xsd:integer</c> datatype IRI.</summary>
        public static Utf8String Integer { get; } = new(IntegerBytes);

        /// <summary>The <c>xsd:double</c> datatype IRI.</summary>
        public static Utf8String Double { get; } = new(DoubleBytes);

        /// <summary>The <c>xsd:decimal</c> datatype IRI.</summary>
        public static Utf8String Decimal { get; } = new(DecimalBytes);

        /// <summary>The <c>xsd:dateTime</c> datatype IRI.</summary>
        public static Utf8String DateTime { get; } = new(DateTimeBytes);

        /// <summary>The <c>xsd:dateTimeStamp</c> datatype IRI — restriction of <c>xsd:dateTime</c> requiring a timezone.</summary>
        public static Utf8String DateTimeStamp { get; } = new(DateTimeStampBytes);

        /// <summary>The <c>xsd:date</c> datatype IRI.</summary>
        public static Utf8String Date { get; } = new(DateBytes);

        /// <summary>The <c>xsd:time</c> datatype IRI.</summary>
        public static Utf8String Time { get; } = new(TimeBytes);

        /// <summary>The <c>xsd:float</c> datatype IRI.</summary>
        public static Utf8String Float { get; } = new(FloatBytes);

        /// <summary>The <c>xsd:long</c> datatype IRI.</summary>
        public static Utf8String Long { get; } = new(LongBytes);

        /// <summary>The <c>xsd:int</c> datatype IRI.</summary>
        public static Utf8String Int { get; } = new(IntBytes);

        /// <summary>The <c>xsd:short</c> datatype IRI.</summary>
        public static Utf8String Short { get; } = new(ShortBytes);

        /// <summary>The <c>xsd:byte</c> datatype IRI.</summary>
        /// <remarks>Named <c>ByteValue</c> rather than <c>Byte</c> to avoid collision with <see cref="System.Byte"/>.</remarks>
        public static Utf8String ByteValue { get; } = new(ByteValueBytes);

        /// <summary>The <c>xsd:nonNegativeInteger</c> datatype IRI.</summary>
        public static Utf8String NonNegativeInteger { get; } = new(NonNegativeIntegerBytes);

        /// <summary>The <c>xsd:nonPositiveInteger</c> datatype IRI.</summary>
        public static Utf8String NonPositiveInteger { get; } = new(NonPositiveIntegerBytes);

        /// <summary>The <c>xsd:positiveInteger</c> datatype IRI.</summary>
        public static Utf8String PositiveInteger { get; } = new(PositiveIntegerBytes);

        /// <summary>The <c>xsd:negativeInteger</c> datatype IRI.</summary>
        public static Utf8String NegativeInteger { get; } = new(NegativeIntegerBytes);

        /// <summary>The <c>xsd:unsignedLong</c> datatype IRI.</summary>
        public static Utf8String UnsignedLong { get; } = new(UnsignedLongBytes);

        /// <summary>The <c>xsd:unsignedInt</c> datatype IRI.</summary>
        public static Utf8String UnsignedInt { get; } = new(UnsignedIntBytes);

        /// <summary>The <c>xsd:unsignedShort</c> datatype IRI.</summary>
        public static Utf8String UnsignedShort { get; } = new(UnsignedShortBytes);

        /// <summary>The <c>xsd:unsignedByte</c> datatype IRI.</summary>
        public static Utf8String UnsignedByte { get; } = new(UnsignedByteBytes);

        /// <summary>The <c>xsd:duration</c> datatype IRI — partially ordered duration value space.</summary>
        public static Utf8String Duration { get; } = new(DurationBytes);

        /// <summary>The <c>xsd:yearMonthDuration</c> datatype IRI — duration restricted to year and month components; totally ordered within its kind.</summary>
        public static Utf8String YearMonthDuration { get; } = new(YearMonthDurationBytes);

        /// <summary>The <c>xsd:dayTimeDuration</c> datatype IRI — duration restricted to day, hour, minute, second components; totally ordered within its kind.</summary>
        public static Utf8String DayTimeDuration { get; } = new(DayTimeDurationBytes);

        /// <summary>The <c>xsd:anyURI</c> datatype IRI.</summary>
        public static Utf8String AnyUri { get; } = new(AnyUriBytes);

        /// <summary>The <c>xsd:base64Binary</c> datatype IRI.</summary>
        public static Utf8String Base64Binary { get; } = new(Base64BinaryBytes);

        /// <summary>The <c>xsd:hexBinary</c> datatype IRI.</summary>
        public static Utf8String HexBinary { get; } = new(HexBinaryBytes);

        /// <summary>The <c>xsd:normalizedString</c> datatype IRI.</summary>
        public static Utf8String NormalizedString { get; } = new(NormalizedStringBytes);

        /// <summary>The <c>xsd:token</c> datatype IRI.</summary>
        public static Utf8String Token { get; } = new(TokenBytes);
    }

    /// <summary>
    /// RDF namespace terms that define the RDF data model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains <c>rdf:type</c>, the RDF-defined datatype IRIs (<c>rdf:langString</c>,
    /// <c>rdf:dirLangString</c>, <c>rdf:JSON</c>, <c>rdf:HTML</c>, <c>rdf:XMLLiteral</c>),
    /// and <c>rdf:reifies</c> which is paired with <see cref="TripleTerm"/>.
    /// </para>
    /// <para>
    /// All other RDF terms (list vocabulary, classic reification, <c>rdf:Property</c>,
    /// <c>rdf:value</c>) and all RDFS terms are defined in
    /// <c>Lumoin.Veritas.Rdf.RdfVocabulary</c>.
    /// </para>
    /// <para>
    /// Defined in <see href="https://www.w3.org/TR/rdf12-concepts/">RDF 1.2 Concepts</see>:
    /// datatypes in §5, <c>rdf:type</c> in §4.1, <c>rdf:reifies</c> in §3.5.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Vocabulary.Rdf.Type is the intended usage pattern.")]
    public static class Rdf
    {
        /// <summary>The RDF namespace IRI.</summary>
        public const string Namespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

        private static readonly byte[] TypeBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8.ToArray();
        private static readonly byte[] LangStringBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8.ToArray();
        private static readonly byte[] DirLangStringBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"u8.ToArray();
        private static readonly byte[] JsonBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON"u8.ToArray();
        private static readonly byte[] HtmlBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#HTML"u8.ToArray();
        private static readonly byte[] XmlLiteralBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#XMLLiteral"u8.ToArray();
        private static readonly byte[] ReifiesBytes = "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8.ToArray();

        /// <summary>The <c>rdf:type</c> predicate IRI.</summary>
        public static Utf8String Type { get; } = new(TypeBytes);

        /// <summary>The <c>rdf:langString</c> datatype IRI for language-tagged strings.</summary>
        public static Utf8String LangString { get; } = new(LangStringBytes);

        /// <summary>The <c>rdf:dirLangString</c> datatype IRI for directional language-tagged strings (RDF 1.2).</summary>
        public static Utf8String DirLangString { get; } = new(DirLangStringBytes);

        /// <summary>The <c>rdf:JSON</c> datatype IRI for JSON literals (RDF 1.2).</summary>
        public static Utf8String Json { get; } = new(JsonBytes);

        /// <summary>The <c>rdf:HTML</c> datatype IRI for HTML fragment literals.</summary>
        public static Utf8String Html { get; } = new(HtmlBytes);

        /// <summary>The <c>rdf:XMLLiteral</c> datatype IRI for XML fragment literals.</summary>
        public static Utf8String XmlLiteral { get; } = new(XmlLiteralBytes);

        /// <summary>The <c>rdf:reifies</c> predicate for RDF 1.2 triple term reification.</summary>
        public static Utf8String Reifies { get; } = new(ReifiesBytes);
    }
}
