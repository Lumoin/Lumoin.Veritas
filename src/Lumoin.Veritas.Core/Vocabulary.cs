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

        private static byte[] StringBytes { get; } = "http://www.w3.org/2001/XMLSchema#string"u8.ToArray();
        private static byte[] BooleanBytes { get; } = "http://www.w3.org/2001/XMLSchema#boolean"u8.ToArray();
        private static byte[] IntegerBytes { get; } = "http://www.w3.org/2001/XMLSchema#integer"u8.ToArray();
        private static byte[] DoubleBytes { get; } = "http://www.w3.org/2001/XMLSchema#double"u8.ToArray();
        private static byte[] DecimalBytes { get; } = "http://www.w3.org/2001/XMLSchema#decimal"u8.ToArray();
        private static byte[] DateTimeBytes { get; } = "http://www.w3.org/2001/XMLSchema#dateTime"u8.ToArray();
        private static byte[] DateTimeStampBytes { get; } = "http://www.w3.org/2001/XMLSchema#dateTimeStamp"u8.ToArray();
        private static byte[] DateBytes { get; } = "http://www.w3.org/2001/XMLSchema#date"u8.ToArray();
        private static byte[] TimeBytes { get; } = "http://www.w3.org/2001/XMLSchema#time"u8.ToArray();
        private static byte[] FloatBytes { get; } = "http://www.w3.org/2001/XMLSchema#float"u8.ToArray();
        private static byte[] LongBytes { get; } = "http://www.w3.org/2001/XMLSchema#long"u8.ToArray();
        private static byte[] IntBytes { get; } = "http://www.w3.org/2001/XMLSchema#int"u8.ToArray();
        private static byte[] ShortBytes { get; } = "http://www.w3.org/2001/XMLSchema#short"u8.ToArray();
        private static byte[] ByteValueBytes { get; } = "http://www.w3.org/2001/XMLSchema#byte"u8.ToArray();
        private static byte[] NonNegativeIntegerBytes { get; } = "http://www.w3.org/2001/XMLSchema#nonNegativeInteger"u8.ToArray();
        private static byte[] NonPositiveIntegerBytes { get; } = "http://www.w3.org/2001/XMLSchema#nonPositiveInteger"u8.ToArray();
        private static byte[] PositiveIntegerBytes { get; } = "http://www.w3.org/2001/XMLSchema#positiveInteger"u8.ToArray();
        private static byte[] NegativeIntegerBytes { get; } = "http://www.w3.org/2001/XMLSchema#negativeInteger"u8.ToArray();
        private static byte[] UnsignedLongBytes { get; } = "http://www.w3.org/2001/XMLSchema#unsignedLong"u8.ToArray();
        private static byte[] UnsignedIntBytes { get; } = "http://www.w3.org/2001/XMLSchema#unsignedInt"u8.ToArray();
        private static byte[] UnsignedShortBytes { get; } = "http://www.w3.org/2001/XMLSchema#unsignedShort"u8.ToArray();
        private static byte[] UnsignedByteBytes { get; } = "http://www.w3.org/2001/XMLSchema#unsignedByte"u8.ToArray();
        private static byte[] DurationBytes { get; } = "http://www.w3.org/2001/XMLSchema#duration"u8.ToArray();
        private static byte[] YearMonthDurationBytes { get; } = "http://www.w3.org/2001/XMLSchema#yearMonthDuration"u8.ToArray();
        private static byte[] DayTimeDurationBytes { get; } = "http://www.w3.org/2001/XMLSchema#dayTimeDuration"u8.ToArray();
        private static byte[] AnyUriBytes { get; } = "http://www.w3.org/2001/XMLSchema#anyURI"u8.ToArray();
        private static byte[] Base64BinaryBytes { get; } = "http://www.w3.org/2001/XMLSchema#base64Binary"u8.ToArray();
        private static byte[] HexBinaryBytes { get; } = "http://www.w3.org/2001/XMLSchema#hexBinary"u8.ToArray();
        private static byte[] NormalizedStringBytes { get; } = "http://www.w3.org/2001/XMLSchema#normalizedString"u8.ToArray();
        private static byte[] TokenBytes { get; } = "http://www.w3.org/2001/XMLSchema#token"u8.ToArray();
        private static byte[] LanguageBytes { get; } = "http://www.w3.org/2001/XMLSchema#language"u8.ToArray();
        private static byte[] NameBytes { get; } = "http://www.w3.org/2001/XMLSchema#Name"u8.ToArray();
        private static byte[] NcNameBytes { get; } = "http://www.w3.org/2001/XMLSchema#NCName"u8.ToArray();
        private static byte[] NmTokenBytes { get; } = "http://www.w3.org/2001/XMLSchema#NMTOKEN"u8.ToArray();

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

        /// <summary>The <c>xsd:language</c> datatype IRI — a language identifier restriction of <c>xsd:token</c>.</summary>
        public static Utf8String Language { get; } = new(LanguageBytes);

        /// <summary>The <c>xsd:Name</c> datatype IRI — an XML Name restriction of <c>xsd:token</c>.</summary>
        public static Utf8String Name { get; } = new(NameBytes);

        /// <summary>The <c>xsd:NCName</c> datatype IRI — a non-colonized XML name restriction of <c>xsd:Name</c>.</summary>
        public static Utf8String NcName { get; } = new(NcNameBytes);

        /// <summary>The <c>xsd:NMTOKEN</c> datatype IRI — an XML name-token restriction of <c>xsd:token</c>.</summary>
        public static Utf8String NmToken { get; } = new(NmTokenBytes);

        /// <summary>
        /// The shared <see cref="NamedNode"/> instances of the hottest XSD datatype
        /// terms, so parse paths reuse one node per term instead of wrapping the IRI
        /// per literal. Instance sharing is observationally free — <see cref="NamedNode"/>
        /// is an immutable record with value equality.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Vocabulary.Xsd.Nodes.String is the intended usage pattern.")]
        public static class Nodes
        {
            /// <summary>The shared <c>xsd:string</c> datatype node.</summary>
            public static NamedNode String { get; } = new(Xsd.String);
        }
    }

    /// <summary>
    /// The XSD constraining-facet IRIs that OWL 2 datatype restrictions
    /// (<see href="https://www.w3.org/TR/owl2-syntax/#Datatype_Restrictions">Syntax §7.5</see>)
    /// constrain a base datatype with: the value-range bounds, the length
    /// facets, the lexical <c>pattern</c>, and the digit-count facets.
    /// </summary>
    /// <remarks>
    /// These are the same constraining facets as <see href="https://www.w3.org/TR/xmlschema11-2/#rf-facets">XSD 1.1 Part 2 §4.3</see>,
    /// restricted to the set OWL 2 admits. The <c>rdf:langRange</c> facet OWL 2
    /// also admits is in the RDF namespace, not here. They are allocated once
    /// as static byte arrays, like the datatype IRIs in <see cref="Xsd"/>.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Vocabulary.XsdFacets.MinInclusive is the intended usage pattern.")]
    public static class XsdFacets
    {
        private static byte[] MinInclusiveBytes { get; } = "http://www.w3.org/2001/XMLSchema#minInclusive"u8.ToArray();
        private static byte[] MaxInclusiveBytes { get; } = "http://www.w3.org/2001/XMLSchema#maxInclusive"u8.ToArray();
        private static byte[] MinExclusiveBytes { get; } = "http://www.w3.org/2001/XMLSchema#minExclusive"u8.ToArray();
        private static byte[] MaxExclusiveBytes { get; } = "http://www.w3.org/2001/XMLSchema#maxExclusive"u8.ToArray();
        private static byte[] LengthBytes { get; } = "http://www.w3.org/2001/XMLSchema#length"u8.ToArray();
        private static byte[] MinLengthBytes { get; } = "http://www.w3.org/2001/XMLSchema#minLength"u8.ToArray();
        private static byte[] MaxLengthBytes { get; } = "http://www.w3.org/2001/XMLSchema#maxLength"u8.ToArray();
        private static byte[] PatternBytes { get; } = "http://www.w3.org/2001/XMLSchema#pattern"u8.ToArray();
        private static byte[] TotalDigitsBytes { get; } = "http://www.w3.org/2001/XMLSchema#totalDigits"u8.ToArray();
        private static byte[] FractionDigitsBytes { get; } = "http://www.w3.org/2001/XMLSchema#fractionDigits"u8.ToArray();

        /// <summary>The <c>xsd:minInclusive</c> facet IRI — the inclusive lower bound of an ordered value space.</summary>
        public static Utf8String MinInclusive { get; } = new(MinInclusiveBytes);

        /// <summary>The <c>xsd:maxInclusive</c> facet IRI — the inclusive upper bound of an ordered value space.</summary>
        public static Utf8String MaxInclusive { get; } = new(MaxInclusiveBytes);

        /// <summary>The <c>xsd:minExclusive</c> facet IRI — the exclusive lower bound of an ordered value space.</summary>
        public static Utf8String MinExclusive { get; } = new(MinExclusiveBytes);

        /// <summary>The <c>xsd:maxExclusive</c> facet IRI — the exclusive upper bound of an ordered value space.</summary>
        public static Utf8String MaxExclusive { get; } = new(MaxExclusiveBytes);

        /// <summary>The <c>xsd:length</c> facet IRI — the exact length of a string or binary value.</summary>
        public static Utf8String Length { get; } = new(LengthBytes);

        /// <summary>The <c>xsd:minLength</c> facet IRI — the minimum length of a string or binary value.</summary>
        public static Utf8String MinLength { get; } = new(MinLengthBytes);

        /// <summary>The <c>xsd:maxLength</c> facet IRI — the maximum length of a string or binary value.</summary>
        public static Utf8String MaxLength { get; } = new(MaxLengthBytes);

        /// <summary>The <c>xsd:pattern</c> facet IRI — a regular expression the lexical form must match.</summary>
        public static Utf8String Pattern { get; } = new(PatternBytes);

        /// <summary>The <c>xsd:totalDigits</c> facet IRI — the maximum count of significant decimal digits.</summary>
        public static Utf8String TotalDigits { get; } = new(TotalDigitsBytes);

        /// <summary>The <c>xsd:fractionDigits</c> facet IRI — the maximum count of fractional decimal digits.</summary>
        public static Utf8String FractionDigits { get; } = new(FractionDigitsBytes);
    }

    /// <summary>
    /// RDF namespace terms that define the RDF data model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains <c>rdf:type</c>, the RDF-defined datatype IRIs (<c>rdf:langString</c>,
    /// <c>rdf:dirLangString</c>, <c>rdf:JSON</c>, <c>rdf:HTML</c>, <c>rdf:XMLLiteral</c>),
    /// <c>rdf:reifies</c> which is paired with <see cref="TripleTerm"/>, and the
    /// <c>rdf:PropositionForm</c> / <c>rdf:propositionForm*</c> terms that basic-encode a triple term
    /// into a triple-term-free form per RDF 1.2 Interoperability §3.
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

        private static byte[] TypeBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8.ToArray();
        private static byte[] LangStringBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8.ToArray();
        private static byte[] DirLangStringBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"u8.ToArray();
        private static byte[] JsonBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#JSON"u8.ToArray();
        private static byte[] HtmlBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#HTML"u8.ToArray();
        private static byte[] XmlLiteralBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#XMLLiteral"u8.ToArray();
        private static byte[] ReifiesBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#reifies"u8.ToArray();
        private static byte[] PropositionFormBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#PropositionForm"u8.ToArray();
        private static byte[] PropositionFormSubjectBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#propositionFormSubject"u8.ToArray();
        private static byte[] PropositionFormPredicateBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#propositionFormPredicate"u8.ToArray();
        private static byte[] PropositionFormObjectBytes { get; } = "http://www.w3.org/1999/02/22-rdf-syntax-ns#propositionFormObject"u8.ToArray();

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

        /// <summary>The <c>rdf:PropositionForm</c> class that marks a blank node standing for a basic-encoded triple term (RDF 1.2 Interoperability §3).</summary>
        public static Utf8String PropositionForm { get; } = new(PropositionFormBytes);

        /// <summary>The <c>rdf:propositionFormSubject</c> predicate carrying the subject of a basic-encoded triple term (RDF 1.2 Interoperability §3).</summary>
        public static Utf8String PropositionFormSubject { get; } = new(PropositionFormSubjectBytes);

        /// <summary>The <c>rdf:propositionFormPredicate</c> predicate carrying the predicate of a basic-encoded triple term (RDF 1.2 Interoperability §3).</summary>
        public static Utf8String PropositionFormPredicate { get; } = new(PropositionFormPredicateBytes);

        /// <summary>The <c>rdf:propositionFormObject</c> predicate carrying the object of a basic-encoded triple term (RDF 1.2 Interoperability §3).</summary>
        public static Utf8String PropositionFormObject { get; } = new(PropositionFormObjectBytes);

        /// <summary>
        /// The shared <see cref="NamedNode"/> instances of the data-model terms, so hot
        /// parse and serialization paths reuse one node per term instead of wrapping the
        /// IRI per use. <see cref="NamedNode"/> is an immutable record with value
        /// equality, so instance sharing is observationally free and adds a
        /// reference-equality fast path.
        /// </summary>
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Vocabulary.Rdf.Nodes.Type is the intended usage pattern.")]
        public static class Nodes
        {
            /// <summary>The shared <c>rdf:type</c> node.</summary>
            public static NamedNode Type { get; } = new(Rdf.Type);

            /// <summary>The shared <c>rdf:langString</c> datatype node.</summary>
            public static NamedNode LangString { get; } = new(Rdf.LangString);

            /// <summary>The shared <c>rdf:dirLangString</c> datatype node.</summary>
            public static NamedNode DirLangString { get; } = new(Rdf.DirLangString);

            /// <summary>The shared <c>rdf:XMLLiteral</c> datatype node.</summary>
            public static NamedNode XmlLiteral { get; } = new(Rdf.XmlLiteral);

            /// <summary>The shared <c>rdf:reifies</c> predicate node.</summary>
            public static NamedNode Reifies { get; } = new(Rdf.Reifies);
        }
    }
}
