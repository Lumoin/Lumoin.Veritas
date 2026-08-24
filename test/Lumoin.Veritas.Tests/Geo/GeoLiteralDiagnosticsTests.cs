using System.Text;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The geometry-literal diagnosis face's battery: every one of the six answered datatypes diagnosed
/// across the four states, with the reason and the first offending byte pinned on each. A body that
/// breaks its datatype's certified grammar is invalid; a body the grammar tolerates yet no codec reader
/// can read is a warning; a datatype outside the six is the abstention. Offsets are relative to the WHOLE
/// literal body, count UTF-8 bytes rather than characters, and are computed from the offending text a row
/// states, never hand-counted. The closing row is the corpus-wide agreement battery: across every geometry
/// fixture corpus the repository carries, a diagnosis is invalid exactly where the datatype's own
/// validator answers invalid, so the two paths can never drift apart silently.
/// </summary>
[TestClass]
internal sealed class GeoLiteralDiagnosticsTests
{
    /// <summary>The house grid's angle-bracket prefix, shared by the DGGS rows.</summary>
    private const string HousePrefix = "<https://lumoin.com/veritas/dggs/a5>";

    /// <summary>A conformant house-flavour cell-set literal over one cell.</summary>
    private const string HouseCells = HousePrefix + " CELLS (4f05dccc726e0000)";

    /// <summary>A foreign-grid literal: the prefix stands and the geometry data belongs to the grid its IRI names.</summary>
    private const string ForeignGridLiteral = "<https://w3id.org/dggs/auspix> CELL (R3234)";

    /// <summary>The IRI of a datatype outside this face's jurisdiction.</summary>
    private const string UnansweredDatatypeIri = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>How many wrapping collections carry a member past the readers' nesting bound.</summary>
    private const int WrappersPastTheNestingBound = 32;

    /// <summary>
    /// The well-known-text corpus of the agreement battery: the certified tags, the empty forms, provably
    /// broken bodies, the curve-tag abstention, the structurally thin body, and both CRS-prefix cases.
    /// </summary>
    private static string[] WellKnownTextCorpus { get; } =
    [
        "",
        "   ",
        "POINT(1 2)",
        "POINT EMPTY",
        "POINT ZM (1 2 3 4)",
        "LINESTRING(0 0, 1 1, 2 2)",
        "LINESTRING(1 2)",
        "POLYGON((0 0, 0 10, 10 10, 0 0))",
        "POLYGON((0 0, 1 0, 1 1, 2 2))",
        "MULTIPOINT((1 2), (3 4))",
        "GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(0 0, 1 1))",
        "CIRCULARSTRING(0 0, 1 1, 2 0)",
        "POINT(1 X)",
        "POINT(1 2",
        "not a geometry",
        "SRID=4326;POINT(1 2)",
        "<http://www.opengis.net/def/crs/EPSG/0/4326> POINT(1 2)",
        "<http://www.opengis.net/def/crs/EPSG/0/4326> POINT(1 X)",
        "<http://www.opengis.net/def/crs/EPSG/0/4326>",
        "<unterminated POINT(1 2)"
    ];

    /// <summary>
    /// The DGGS corpus of the agreement battery, ridden by the generic datatype and the house subclass
    /// alike: the empty form, conformant and violating house bodies, foreign grids, and the broken prefix
    /// families.
    /// </summary>
    private static string[] DggsCorpus { get; } =
    [
        "",
        "   ",
        HouseCells,
        HousePrefix + " CELLS (600000000000000 a00000000000000)",
        HousePrefix + " CELLS (xyz)",
        HousePrefix + " CELLS ()",
        HousePrefix + " CELLS (4f05dccc726e0000) extra",
        ForeignGridLiteral,
        "<https://w3id.org/dggs/auspix>",
        "<https://example.org/dggs/järjestelmä> CELL (R3234)",
        "CELL (R3234)",
        "<>"
    ];

    /// <summary>A well-known-text body broken inside its position is invalid at the byte that broke it.</summary>
    [TestMethod]
    public void WellKnownTextInvalidBodyLocatesItsOffendingByte()
    {
        AssertInvalid(GeoVocabulary.Geo.WktLiteral, "POINT(1 X)", GeometryCodecRefusalKind.MalformedDocument, "X");
    }

    /// <summary>
    /// A well-known-text body behind an explicit CRS prefix reports its offense against the WHOLE literal:
    /// the stack sees the geometry text alone, so the stripped prefix's width is added back and the offset
    /// lands inside the geometry text.
    /// </summary>
    [TestMethod]
    public void WellKnownTextCrsPrefixedBodyReBasesItsOffsetIntoTheGeometryText()
    {
        const string Body = "<http://www.opengis.net/def/crs/EPSG/0/4326> POINT(1 X)";
        GeoLiteralDiagnosis diagnosis = Diagnose(GeoVocabulary.Geo.WktLiteral, Body);

        Assert.AreEqual(GeoLiteralDiagnosisStatus.Invalid, diagnosis.Status, "the broken geometry text is invalid behind the prefix too");
        Assert.AreEqual(
            new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, XmlScannerAssert.ByteOffsetOf(Body, "X")),
            diagnosis.Refusal,
            "the offset names the offending byte of the whole literal");
        Assert.IsGreaterThan(XmlScannerAssert.ByteOffsetOf(Body, "POINT"), diagnosis.Refusal.ByteOffset, "the offset lands inside the geometry text, past the stripped prefix");
    }

    /// <summary>
    /// A structurally thin line string warns: the grammar admits a one-position line and the validator
    /// answers valid, yet no reader can materialize it, so no evaluation over the literal can succeed.
    /// </summary>
    [TestMethod]
    public void WellKnownTextThinLineStringWarnsAtItsClosingByte()
    {
        const string Body = "LINESTRING(1 2)";

        Assert.AreEqual(ValueLexicalValidity.Valid, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(Body)), "the validator tolerates the thin line");
        AssertWarning(GeoVocabulary.Geo.WktLiteral, Body, GeometryCodecRefusalKind.StructuralViolation, ")");
    }

    /// <summary>A curve tag warns at the tag: the recognizer abstains on its uncertified content grammar and the reader refuses it as unsupported.</summary>
    [TestMethod]
    public void WellKnownTextCurveTagBodyWarnsAtTheTag()
    {
        const string Body = "CIRCULARSTRING(0 0, 1 1, 2 0)";

        Assert.AreEqual(ValueLexicalValidity.Indeterminate, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(Body)), "the validator abstains on the curve tag");
        AssertWarning(GeoVocabulary.Geo.WktLiteral, Body, GeometryCodecRefusalKind.UnsupportedGeometry, "CIRCULARSTRING");
    }

    /// <summary>A body past the nesting bound warns at the member the bound refused, the validator having abstained on depth.</summary>
    [TestMethod]
    public void WellKnownTextBodyPastTheNestingBoundWarnsAtTheRefusedMember()
    {
        string body = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", WrappersPastTheNestingBound))
            + "POINT(1 2)"
            + string.Concat(Enumerable.Repeat(")", WrappersPastTheNestingBound));

        Assert.AreEqual(ValueLexicalValidity.Indeterminate, WktLiteralValueDatatype.Instance.ValidateLexicalForm(Utf8Strings.From(body)), "the validator abstains past the nesting bound");
        AssertWarning(GeoVocabulary.Geo.WktLiteral, body, GeometryCodecRefusalKind.NestingTooDeep, "POINT(1 2)");
    }

    /// <summary>A readable well-known-text body stands, and both empty forms stand as the empty geometry.</summary>
    [TestMethod]
    public void WellKnownTextValidBodiesStand()
    {
        AssertStanding(GeoVocabulary.Geo.WktLiteral, "POINT(1 2)");
        AssertStanding(GeoVocabulary.Geo.WktLiteral, "POINT EMPTY");
        AssertStanding(GeoVocabulary.Geo.WktLiteral, "");
        AssertStanding(GeoVocabulary.Geo.WktLiteral, "   ");
    }

    /// <summary>A GML root outside the GML namespace is provably not an element of that schema, so the literal is invalid at the root.</summary>
    [TestMethod]
    public void GmlInvalidBodyLocatesItsOffendingByte()
    {
        AssertInvalid(
            GeoVocabulary.Geo.GmlLiteral,
            "<Point xmlns=\"http://example.org/geometry\"><pos>1 2</pos></Point>",
            GeometryCodecRefusalKind.UnsupportedGeometry,
            "<Point");
    }

    /// <summary>A readable GML fragment stands, and the empty form stands as the empty geometry.</summary>
    [TestMethod]
    public void GmlValidBodiesStand()
    {
        AssertStanding(GeoVocabulary.Geo.GmlLiteral, GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody));
        AssertStanding(GeoVocabulary.Geo.GmlLiteral, "");
    }

    /// <summary>Content after a complete GeoJSON geometry breaks the body's grammar, so the literal is invalid at that content's first byte.</summary>
    [TestMethod]
    public void GeoJsonInvalidBodyLocatesItsOffendingByte()
    {
        AssertInvalid(
            GeoVocabulary.Geo.GeoJsonLiteral,
            GeoJsonTestDocuments.CanonicalPoint + " trailing",
            GeometryCodecRefusalKind.TrailingContent,
            "trailing");
    }

    /// <summary>A readable GeoJSON geometry stands, and the empty form stands as the empty geometry.</summary>
    [TestMethod]
    public void GeoJsonValidBodiesStand()
    {
        AssertStanding(GeoVocabulary.Geo.GeoJsonLiteral, GeoJsonTestDocuments.CanonicalPoint);
        AssertStanding(GeoVocabulary.Geo.GeoJsonLiteral, "");
    }

    /// <summary>A KML root bound outside the KML family is provably not an element of that schema, so the literal is invalid at the root.</summary>
    [TestMethod]
    public void KmlInvalidBodyLocatesItsOffendingByte()
    {
        AssertInvalid(
            GeoVocabulary.Geo.KmlLiteral,
            "<Point xmlns=\"http://example.org/geometry\"><coordinates>1,2</coordinates></Point>",
            GeometryCodecRefusalKind.UnsupportedGeometry,
            "<Point");
    }

    /// <summary>A readable KML geometry stands, and the empty form stands as the empty geometry.</summary>
    [TestMethod]
    public void KmlValidBodiesStand()
    {
        AssertStanding(GeoVocabulary.Geo.KmlLiteral, KmlTestDocuments.Root("Point", KmlTestDocuments.PointCoordinates));
        AssertStanding(GeoVocabulary.Geo.KmlLiteral, "");
    }

    /// <summary>A DGGS form outside the literal grammar is invalid at the byte where the grammar could not be extended: the missing prefix and the non-conformant house body alike.</summary>
    [TestMethod]
    public void DggsInvalidBodiesLocateTheirOffendingByte()
    {
        AssertInvalid(GeoVocabulary.Geo.DggsLiteral, "CELL (R3234)", GeometryCodecRefusalKind.MalformedDocument, "CELL");
        AssertInvalid(GeoVocabulary.Geo.DggsLiteral, HousePrefix + " CELLS (xyz)", GeometryCodecRefusalKind.MalformedDocument, "xyz");
    }

    /// <summary>
    /// A conformant house-flavour form stands, the empty form stands, and a foreign grid stands too: its
    /// data is formulated according to the DGGS its IRI names, so nothing is left to locate and no reason
    /// is fabricated.
    /// </summary>
    [TestMethod]
    public void DggsValidBodiesStand()
    {
        AssertStanding(GeoVocabulary.Geo.DggsLiteral, HouseCells);
        AssertStanding(GeoVocabulary.Geo.DggsLiteral, ForeignGridLiteral);
        AssertStanding(GeoVocabulary.Geo.DggsLiteral, "");
    }

    /// <summary>
    /// The house subclass certifies its whole grammar, so a foreign grid IRI, a non-conformant cell body,
    /// and a form without the prefix are each invalid at their located byte.
    /// </summary>
    [TestMethod]
    public void A5InvalidBodiesLocateTheirOffendingByte()
    {
        AssertInvalid(A5DggsVocabulary.DatatypeIri, ForeignGridLiteral, GeometryCodecRefusalKind.MalformedDocument, "https://w3id.org");
        AssertInvalid(A5DggsVocabulary.DatatypeIri, HousePrefix + " CELLS (xyz)", GeometryCodecRefusalKind.MalformedDocument, "xyz");
        AssertInvalidAt(A5DggsVocabulary.DatatypeIri, "   ", GeometryCodecRefusalKind.MalformedDocument, 0);
    }

    /// <summary>A conformant house-flavour form stands under the subclass, and so does the empty form.</summary>
    [TestMethod]
    public void A5ValidBodiesStand()
    {
        AssertStanding(A5DggsVocabulary.DatatypeIri, HouseCells);
        AssertStanding(A5DggsVocabulary.DatatypeIri, "");
    }

    /// <summary>A datatype outside the answered six abstains with no reason — never a claim about the body.</summary>
    [TestMethod]
    public void UnansweredDatatypeAbstains()
    {
        GeoLiteralDiagnosis diagnosis = Diagnose(Utf8Strings.From(UnansweredDatatypeIri), "POINT(1 X)");

        Assert.AreEqual(GeoLiteralDiagnosisStatus.UnsupportedDatatype, diagnosis.Status, "a datatype outside the six is the abstention");
        Assert.AreEqual(GeometryCodecRefusal.None, diagnosis.Refusal, "an abstention names no reason and no byte");
    }

    /// <summary>
    /// Reported offsets index UTF-8 bytes, not characters: a literal whose IRI carries two-byte characters
    /// ahead of the offense reports an offset wider than the character index, the frame every transcoding
    /// caller answers in.
    /// </summary>
    [TestMethod]
    public void DiagnosisOffsetsIndexUtf8BytesNotCharacters()
    {
        const string Body = "<https://example.org/dggs/järjestelmä>CELL (R3234)";
        int byteOffset = XmlScannerAssert.ByteOffsetOf(Body, "CELL");
        int characterIndex = Body.IndexOf("CELL", StringComparison.Ordinal);
        GeoLiteralDiagnosis diagnosis = Diagnose(GeoVocabulary.Geo.DggsLiteral, Body);

        Assert.AreNotEqual(characterIndex, byteOffset, "the fixture must actually differ between the two frames");
        Assert.AreEqual(GeoLiteralDiagnosisStatus.Invalid, diagnosis.Status, "the missing separator breaks the literal grammar");
        Assert.AreEqual(new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, byteOffset), diagnosis.Refusal, "the offset indexes the transcoded UTF-8 form");
    }

    /// <summary>
    /// The agreement invariant across every geometry fixture corpus the repository carries: a diagnosis
    /// answers invalid EXACTLY where the datatype's own validator answers invalid, so a warning never
    /// stands in for a grammar violation and a violation never degrades to a warning. A disagreement on any
    /// fixture is a defect named by that fixture.
    /// </summary>
    [TestMethod]
    public void EveryFixtureCorpusAgreesWithItsDatatypeValidator()
    {
        foreach(string body in WellKnownTextCorpus)
        {
            AssertAgreement(WktLiteralValueDatatype.Instance, Utf8Strings.From(body), body);
        }

        foreach(string body in DggsCorpus)
        {
            AssertAgreement(DggsLiteralValueDatatype.Instance, Utf8Strings.From(body), body);
            AssertAgreement(A5DggsLiteralValueDatatype.Instance, Utf8Strings.From(body), body);
        }

        foreach(string document in GeoJsonTestDocuments.CanonicalCorpus)
        {
            AssertAgreement(GeoJsonLiteralValueDatatype.Instance, Utf8Strings.From(document), document);
        }

        foreach(string document in GeoJsonTestDocuments.FeatureCorpus)
        {
            AssertAgreement(GeoJsonLiteralValueDatatype.Instance, Utf8Strings.From(document), document);
        }

        foreach(string relativePath in CiteGmlCorpusExpectations.Artifacts.Keys)
        {
            byte[] document = File.ReadAllBytes(CiteGmlCorpusPaths.GetPath(relativePath));
            AssertAgreement(GmlLiteralValueDatatype.Instance, new Utf8String(document), relativePath);
        }

        foreach(string relativePath in CiteKmlCorpusExpectations.Artifacts.Keys)
        {
            byte[] document = File.ReadAllBytes(CiteKmlCorpusPaths.GetPath(relativePath));
            AssertAgreement(KmlLiteralValueDatatype.Instance, new Utf8String(document), relativePath);
        }
    }

    /// <summary>Diagnoses one literal body through the face, binding the GeoJSON reader the composing host binds.</summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's lexical form.</param>
    /// <returns>The diagnosis.</returns>
    private static GeoLiteralDiagnosis Diagnose(Utf8String datatypeIri, string body)
    {
        return GeoLiteralDiagnostics.Describe(datatypeIri, Encoding.UTF8.GetBytes(body), GeoJsonGeometryReader.TryRead);
    }

    /// <summary>Asserts a body diagnoses invalid with the whole expected reason at the offset the offending text names.</summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's lexical form.</param>
    /// <param name="kind">The expected reason.</param>
    /// <param name="offendingText">The text whose first byte the offset must name.</param>
    private static void AssertInvalid(Utf8String datatypeIri, string body, GeometryCodecRefusalKind kind, string offendingText)
    {
        AssertInvalidAt(datatypeIri, body, kind, XmlScannerAssert.ByteOffsetOf(body, offendingText));
    }

    /// <summary>Asserts a body diagnoses invalid with the whole expected reason at the expected byte.</summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's lexical form.</param>
    /// <param name="kind">The expected reason.</param>
    /// <param name="expectedOffset">The expected offending byte, relative to the whole body.</param>
    private static void AssertInvalidAt(Utf8String datatypeIri, string body, GeometryCodecRefusalKind kind, int expectedOffset)
    {
        GeoLiteralDiagnosis diagnosis = Diagnose(datatypeIri, body);

        Assert.AreEqual(GeoLiteralDiagnosisStatus.Invalid, diagnosis.Status, $"'{body}' must diagnose invalid");
        Assert.AreEqual(new GeometryCodecRefusal(kind, expectedOffset), diagnosis.Refusal, $"the whole reason for '{body}'");
    }

    /// <summary>Asserts a body diagnoses a warning with the whole expected reason at the offset the offending text names.</summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's lexical form.</param>
    /// <param name="kind">The expected reason.</param>
    /// <param name="offendingText">The text whose first byte the offset must name.</param>
    private static void AssertWarning(Utf8String datatypeIri, string body, GeometryCodecRefusalKind kind, string offendingText)
    {
        GeoLiteralDiagnosis diagnosis = Diagnose(datatypeIri, body);

        Assert.AreEqual(GeoLiteralDiagnosisStatus.Warning, diagnosis.Status, $"'{body}' must diagnose a warning");
        Assert.AreEqual(
            new GeometryCodecRefusal(kind, XmlScannerAssert.ByteOffsetOf(body, offendingText)),
            diagnosis.Refusal,
            $"the whole reason for '{body}'");
    }

    /// <summary>Asserts a body stands under its datatype with no reason accompanying the verdict.</summary>
    /// <param name="datatypeIri">The literal's datatype IRI.</param>
    /// <param name="body">The literal's lexical form.</param>
    private static void AssertStanding(Utf8String datatypeIri, string body)
    {
        GeoLiteralDiagnosis diagnosis = Diagnose(datatypeIri, body);

        Assert.AreEqual(GeoLiteralDiagnosisStatus.Valid, diagnosis.Status, $"'{body}' must stand");
        Assert.AreEqual(GeometryCodecRefusal.None, diagnosis.Refusal, $"a standing body names no reason ('{body}')");
    }

    /// <summary>
    /// Asserts the agreement invariant for one fixture: the diagnosis answers invalid exactly where the
    /// datatype's validator answers invalid.
    /// </summary>
    /// <param name="datatype">The value-layer definition whose IRI the diagnosis answers for.</param>
    /// <param name="literal">The literal's lexical form.</param>
    /// <param name="fixtureName">The fixture's name, carried into a failure.</param>
    private static void AssertAgreement(ValueDatatype datatype, Utf8String literal, string fixtureName)
    {
        GeoLiteralDiagnosis diagnosis = GeoLiteralDiagnostics.Describe(datatype.DatatypeIri, literal.Span, GeoJsonGeometryReader.TryRead);
        ValueLexicalValidity validity = datatype.ValidateLexicalForm(literal);

        if(diagnosis.Status == GeoLiteralDiagnosisStatus.Invalid)
        {
            Assert.AreEqual(ValueLexicalValidity.Invalid, validity, $"'{fixtureName}' diagnoses invalid, so its validator must answer invalid");
        }
        else
        {
            Assert.AreNotEqual(ValueLexicalValidity.Invalid, validity, $"'{fixtureName}' diagnoses {diagnosis.Status}, so its validator must not answer invalid");
        }
    }
}
