using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The literal-diagnosis wire writer: each of the four statuses renders its whole document exactly, the
/// refusal kind and its offending byte ride the two located statuses alone, an unrostered kind renders the
/// abstaining token rather than an integer, and the datatype echoes escaped straight from its UTF-8 bytes.
/// Every row pins the complete document, so the shape every tier answers with is a fact rather than a
/// substring.
/// </summary>
[TestClass]
internal sealed class GeoLiteralDiagnosisJsonTests
{
    /// <summary>A body that stands under its datatype renders the standing status and no refusal fields.</summary>
    [TestMethod]
    public void TheStandingStatusRendersWithoutRefusalFields()
    {
        string json = GeoLiteralDiagnosisJson.Write(GeoVocabulary.Geo.WktLiteral, new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Valid, GeometryCodecRefusal.None));

        Assert.AreEqual("{\"status\":\"valid\",\"datatype\":\"http://www.opengis.net/ont/geosparql#wktLiteral\"}", json);
    }

    /// <summary>A datatype outside the answered family renders the abstention and no refusal fields.</summary>
    [TestMethod]
    public void TheAbstentionRendersWithoutRefusalFields()
    {
        string json = GeoLiteralDiagnosisJson.Write(Vocabulary.Xsd.String, new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.UnsupportedDatatype, GeometryCodecRefusal.None));

        Assert.AreEqual("{\"status\":\"unsupported\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#string\"}", json);
    }

    /// <summary>A tolerated yet unreadable body renders the warning with its reason and offending byte.</summary>
    [TestMethod]
    public void TheWarningCarriesItsReasonAndOffendingByte()
    {
        string json = GeoLiteralDiagnosisJson.Write(GeoVocabulary.Geo.GmlLiteral, new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Warning, new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, 7)));

        Assert.AreEqual("{\"status\":\"warning\",\"kind\":\"StructuralViolation\",\"byteOffset\":7,\"datatype\":\"http://www.opengis.net/ont/geosparql#gmlLiteral\"}", json);
    }

    /// <summary>A body that breaks its datatype's grammar renders the invalid verdict with its reason and offending byte.</summary>
    [TestMethod]
    public void TheInvalidVerdictCarriesItsReasonAndOffendingByte()
    {
        string json = GeoLiteralDiagnosisJson.Write(A5DggsVocabulary.DatatypeIri, new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, 3)));

        Assert.AreEqual("{\"status\":\"invalid\",\"kind\":\"MalformedDocument\",\"byteOffset\":3,\"datatype\":\"https://lumoin.com/veritas/dggs/a5Literal\"}", json);
    }

    /// <summary>An unlocated refusal renders its minus-one offset rather than fabricating a byte.</summary>
    [TestMethod]
    public void AnUnlocatedRefusalRendersTheMinusOneOffset()
    {
        string json = GeoLiteralDiagnosisJson.Write(GeoVocabulary.Geo.KmlLiteral, new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, -1)));

        Assert.AreEqual("{\"status\":\"invalid\",\"kind\":\"MalformedDocument\",\"byteOffset\":-1,\"datatype\":\"http://www.opengis.net/ont/geosparql#kmlLiteral\"}", json);
    }

    /// <summary>A kind outside the closed roster renders the roster's own abstaining token, never an integer.</summary>
    [TestMethod]
    public void AnUnrosteredKindRendersTheAbstainingToken()
    {
        string json = GeoLiteralDiagnosisJson.Write(GeoVocabulary.Geo.WktLiteral, new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Invalid, new GeometryCodecRefusal((GeometryCodecRefusalKind)9999, 0)));

        Assert.AreEqual("{\"status\":\"invalid\",\"kind\":\"None\",\"byteOffset\":0,\"datatype\":\"http://www.opengis.net/ont/geosparql#wktLiteral\"}", json);
    }

    /// <summary>The datatype escapes per RFC 8259 straight from its UTF-8 bytes, so a metacharacter can never break the document.</summary>
    [TestMethod]
    public void TheDatatypeEscapesFromItsUtf8Bytes()
    {
        string json = GeoLiteralDiagnosisJson.Write(Utf8Strings.From("http://example.org/\"quoted\"\\one"), new GeoLiteralDiagnosis(GeoLiteralDiagnosisStatus.Valid, GeometryCodecRefusal.None));

        Assert.AreEqual("{\"status\":\"valid\",\"datatype\":\"http://example.org/\\\"quoted\\\"\\\\one\"}", json);
    }
}
