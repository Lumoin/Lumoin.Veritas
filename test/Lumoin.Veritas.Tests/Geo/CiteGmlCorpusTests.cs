using System.Security.Cryptography;
using System.Text;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Transforms;
using Lumoin.Veritas.Geo.Xml;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The CITE corpus clearance family: every XML instance artifact vendored from the OGC CITE
/// GML 3.2 suites runs through the public reader and lands on its pinned outcome, and every
/// recorded adaptation twin derives to its pinned bytes before doing the same. A parse asserts
/// the geometry kind, emptiness, the recognized coordinate-reference identity, and the SHA-256 of
/// the canonical text form; a refusal asserts the kind and the byte anchor. The corpus is input,
/// never specification — outcomes pin the shipped contract, and changing one is a recorded
/// contract event.
/// </summary>
[TestClass]
internal sealed class CiteGmlCorpusTests
{
    [DataRow("fragments/gml32-features-FeatureCollection-1-fragment-0-adapted.xml", DisplayName = "fragments/gml32-features-FeatureCollection-1-fragment-0-adapted.xml parses")]
    [DataRow("fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-0-adapted.xml", DisplayName = "fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-0-adapted.xml parses")]
    [DataRow("fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-1-adapted.xml", DisplayName = "fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-1-adapted.xml parses")]
    [DataRow("fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-2-adapted.xml", DisplayName = "fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-2-adapted.xml parses")]
    [DataRow("fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-3-adapted.xml", DisplayName = "fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-3-adapted.xml parses")]
    [DataRow("fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-4-adapted.xml", DisplayName = "fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-4-adapted.xml parses")]
    [DataRow("fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-5-adapted.xml", DisplayName = "fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-5-adapted.xml parses")]
    [DataRow("fragments/gml32-geom-CompositeCurve-fragment-0-adapted.xml", DisplayName = "fragments/gml32-geom-CompositeCurve-fragment-0-adapted.xml parses")]
    [DataRow("fragments/gml32-geom-CompositeCurve-fragment-1-adapted.xml", DisplayName = "fragments/gml32-geom-CompositeCurve-fragment-1-adapted.xml parses")]
    [DataRow("fragments/gml32-SimpleFeature-2-fragment-0-adapted.xml", DisplayName = "fragments/gml32-SimpleFeature-2-fragment-0-adapted.xml parses")]
    [DataRow("originals/gml32/geom/Curve-LineString.xml", DisplayName = "originals/gml32/geom/Curve-LineString.xml parses")]
    [DataRow("originals/gml32/geom/Point-axisOrder.xml", DisplayName = "originals/gml32/geom/Point-axisOrder.xml parses")]
    [DataRow("originals/gml32/geom/Polygon-InteriorCrossesExterior.xml", DisplayName = "originals/gml32/geom/Polygon-InteriorCrossesExterior.xml parses")]
    [DataRow("originals/gml32/geom/Polygon-InteriorRing.xml", DisplayName = "originals/gml32/geom/Polygon-InteriorRing.xml parses")]
    [DataRow("originals/gml32/geom/Polygon-InteriorTouchesExterior.xml", DisplayName = "originals/gml32/geom/Polygon-InteriorTouchesExterior.xml parses")]
    [DataRow("originals/gml32/geom/Surface-ExteriorCCW.xml", DisplayName = "originals/gml32/geom/Surface-ExteriorCCW.xml parses")]
    [DataRow("originals/gml32/geom/Surface-ExteriorCW.xml", DisplayName = "originals/gml32/geom/Surface-ExteriorCW.xml parses")]
    [DataRow("originals/gml32/geom/Surface-InteriorCCW.xml", DisplayName = "originals/gml32/geom/Surface-InteriorCCW.xml parses")]
    [DataRow("originals/gml32/geom/Surface-PolygonPatch-2.xml", DisplayName = "originals/gml32/geom/Surface-PolygonPatch-2.xml parses")]
    [TestMethod]
    public void AcceptedArtifactsMaterializeTheirPinnedValues(string relativePath)
    {
        var expectation = CiteGmlCorpusExpectations.Artifacts[relativePath];
        byte[] document = File.ReadAllBytes(CiteGmlCorpusPaths.GetPath(relativePath));

        bool accepted = GmlGeometryReader.TryRead(
            document,
            out FlatGeometry geometry,
            out CoordinateReferenceSystem coordinateReference,
            out GeometryCodecRefusal refusal);

        Assert.IsTrue(accepted, $"Expected a parse but the reader refused {refusal.Kind} at {refusal.ByteOffset}.");
        AssertAcceptedFacts(in geometry, coordinateReference, expectation);
    }

    [DataRow("originals/gml32-data/Alpha-1.xml", DisplayName = "originals/gml32-data/Alpha-1.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/Alpha-xinclude.xml", DisplayName = "originals/gml32-data/Alpha-xinclude.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/atom-feed-2.xml", DisplayName = "originals/gml32-data/atom-feed-2.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/atom-feed.xml", DisplayName = "originals/gml32-data/atom-feed.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/capabilities-simple.xml", DisplayName = "originals/gml32-data/capabilities-simple.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/Gamma.xml", DisplayName = "originals/gml32-data/Gamma.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/SimpleFeature-1.xml", DisplayName = "originals/gml32-data/SimpleFeature-1.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/SimpleFeature-2.xml", DisplayName = "originals/gml32-data/SimpleFeature-2.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32-data/SimpleFeature-xml-model.xml", DisplayName = "originals/gml32-data/SimpleFeature-xml-model.xml refuses ProhibitedConstruct")]
    [DataRow("originals/gml32/aixm/AirportHeliport.xml", DisplayName = "originals/gml32/aixm/AirportHeliport.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/Alpha-1.xml", DisplayName = "originals/gml32/Alpha-1.xml refuses ProhibitedConstruct")]
    [DataRow("originals/gml32/Alpha-xinclude.xml", DisplayName = "originals/gml32/Alpha-xinclude.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/atom-feed.xml", DisplayName = "originals/gml32/atom-feed.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/basic-message.xml", DisplayName = "originals/gml32/basic-message.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/capabilities-simple.xml", DisplayName = "originals/gml32/capabilities-simple.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/envelopes/Envelope-httpRef.xml", DisplayName = "originals/gml32/envelopes/Envelope-httpRef.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/envelopes/Envelope-invalidCorner.xml", DisplayName = "originals/gml32/envelopes/Envelope-invalidCorner.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/envelopes/Envelope-invalidCRS.xml", DisplayName = "originals/gml32/envelopes/Envelope-invalidCRS.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/envelopes/Envelope-noCRS.xml", DisplayName = "originals/gml32/envelopes/Envelope-noCRS.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/envelopes/Envelope-valid.xml", DisplayName = "originals/gml32/envelopes/Envelope-valid.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/features/FeatureCollection-1.xml", DisplayName = "originals/gml32/features/FeatureCollection-1.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/features/FZK-Haus-LoD2-KIT.xml", DisplayName = "originals/gml32/features/FZK-Haus-LoD2-KIT.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/Gamma-any.xml", DisplayName = "originals/gml32/Gamma-any.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/Gamma.xml", DisplayName = "originals/gml32/Gamma.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/AIXMSurface-InteriorCCW.xml", DisplayName = "originals/gml32/geom/AIXMSurface-InteriorCCW.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/AIXMSurface-InteriorCrossesExterior.xml", DisplayName = "originals/gml32/geom/AIXMSurface-InteriorCrossesExterior.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/AIXMSurface.xml", DisplayName = "originals/gml32/geom/AIXMSurface.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/CompositeCurve.xml", DisplayName = "originals/gml32/geom/CompositeCurve.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Curve-ArcByCenterPoint.xml", DisplayName = "originals/gml32/geom/Curve-ArcByCenterPoint.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Curve-disconnected.xml", DisplayName = "originals/gml32/geom/Curve-disconnected.xml refuses StructuralViolation")]
    [DataRow("originals/gml32/geom/Curve-empty.xml", DisplayName = "originals/gml32/geom/Curve-empty.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Curve-GeodesicString.xml", DisplayName = "originals/gml32/geom/Curve-GeodesicString.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Curve-ID_250.xml", DisplayName = "originals/gml32/geom/Curve-ID_250.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Curve-LineString-axisOrder.xml", DisplayName = "originals/gml32/geom/Curve-LineString-axisOrder.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Curve-tripartite.xml", DisplayName = "originals/gml32/geom/Curve-tripartite.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/ElevatedSurface.xml", DisplayName = "originals/gml32/geom/ElevatedSurface.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/LineString-invalidCoords.xml", DisplayName = "originals/gml32/geom/LineString-invalidCoords.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/LineString-srsName-http.xml", DisplayName = "originals/gml32/geom/LineString-srsName-http.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/LineString.xml", DisplayName = "originals/gml32/geom/LineString.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/MultiCurve-1.xml", DisplayName = "originals/gml32/geom/MultiCurve-1.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/MultiCurve-2.xml", DisplayName = "originals/gml32/geom/MultiCurve-2.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/MultiPoint-1.xml", DisplayName = "originals/gml32/geom/MultiPoint-1.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/MultiSurface-ROSPA0080.xml", DisplayName = "originals/gml32/geom/MultiSurface-ROSPA0080.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/MultiSurface.xml", DisplayName = "originals/gml32/geom/MultiSurface.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Point-2.5D.xml", DisplayName = "originals/gml32/geom/Point-2.5D.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Point-27700.xml", DisplayName = "originals/gml32/geom/Point-27700.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Point-epsg3045.xml", DisplayName = "originals/gml32/geom/Point-epsg3045.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Point-srsNameOnPos.xml", DisplayName = "originals/gml32/geom/Point-srsNameOnPos.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/PointWithBearing.xml", DisplayName = "originals/gml32/geom/PointWithBearing.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Polygon-InteriorNotClosed.xml", DisplayName = "originals/gml32/geom/Polygon-InteriorNotClosed.xml refuses StructuralViolation")]
    [DataRow("originals/gml32/geom/Polygon-NotClosed.xml", DisplayName = "originals/gml32/geom/Polygon-NotClosed.xml refuses StructuralViolation")]
    [DataRow("originals/gml32/geom/Polygon-UTM.xml", DisplayName = "originals/gml32/geom/Polygon-UTM.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Surface-Curve-ID_250.xml", DisplayName = "originals/gml32/geom/Surface-Curve-ID_250.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Surface-DiscontiguousPatches.xml", DisplayName = "originals/gml32/geom/Surface-DiscontiguousPatches.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Surface-PolygonPatch-1.xml", DisplayName = "originals/gml32/geom/Surface-PolygonPatch-1.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Surface-PolygonPatch-3.xml", DisplayName = "originals/gml32/geom/Surface-PolygonPatch-3.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Surface-PolygonPatch-AxisOrder.xml", DisplayName = "originals/gml32/geom/Surface-PolygonPatch-AxisOrder.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/geom/Surface-PolygonPatch-ExteriorCurve.xml", DisplayName = "originals/gml32/geom/Surface-PolygonPatch-ExteriorCurve.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Surface-PolygonPatch-ExteriorCurveCW.xml", DisplayName = "originals/gml32/geom/Surface-PolygonPatch-ExteriorCurveCW.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/geom/Surface-RectangleTriangle.xml", DisplayName = "originals/gml32/geom/Surface-RectangleTriangle.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/gmlring2.xml", DisplayName = "originals/gml32/gmlring2.xml refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("originals/gml32/note.xml", DisplayName = "originals/gml32/note.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/schema-catalog-gml-3.2.1.xml", DisplayName = "originals/gml32/schema-catalog-gml-3.2.1.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/schema-catalog.xml", DisplayName = "originals/gml32/schema-catalog.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/SimpleFeature-1.xml", DisplayName = "originals/gml32/SimpleFeature-1.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/SimpleFeature-2.xml", DisplayName = "originals/gml32/SimpleFeature-2.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/soapui/ets-gml32-soapui-project.xml", DisplayName = "originals/gml32/soapui/ets-gml32-soapui-project.xml refuses UnsupportedGeometry")]
    [DataRow("originals/gml32/test-run-props.xml", DisplayName = "originals/gml32/test-run-props.xml refuses ProhibitedConstruct")]
    [TestMethod]
    public void RefusedArtifactsAnchorAtTheirPinnedBytes(string relativePath)
    {
        var expectation = CiteGmlCorpusExpectations.Artifacts[relativePath];
        byte[] document = File.ReadAllBytes(CiteGmlCorpusPaths.GetPath(relativePath));

        bool accepted = GmlGeometryReader.TryRead(
            document,
            out FlatGeometry _,
            out CoordinateReferenceSystem _,
            out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "Expected a refusal but the reader accepted the document.");
        Assert.AreEqual(expectation.RefusalKind, refusal.Kind, "The refusal kind drifted from the pinned outcome.");
        Assert.AreEqual(expectation.RefusalByteOffset, refusal.ByteOffset, "The refusal anchor drifted from the pinned outcome.");
    }

    [DataRow("gml32-envelopes-Envelope-httpRef-adapted", DisplayName = "gml32-envelopes-Envelope-httpRef-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-envelopes-Envelope-invalidCorner-adapted", DisplayName = "gml32-envelopes-Envelope-invalidCorner-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-envelopes-Envelope-invalidCRS-adapted", DisplayName = "gml32-envelopes-Envelope-invalidCRS-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-envelopes-Envelope-noCRS-adapted", DisplayName = "gml32-envelopes-Envelope-noCRS-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-envelopes-Envelope-valid-adapted", DisplayName = "gml32-envelopes-Envelope-valid-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-geom-AIXMSurface-InteriorCCW-renamespaced-adapted", DisplayName = "gml32-geom-AIXMSurface-InteriorCCW-renamespaced-adapted parses")]
    [DataRow("gml32-geom-AIXMSurface-InteriorCrossesExterior-renamespaced-adapted", DisplayName = "gml32-geom-AIXMSurface-InteriorCrossesExterior-renamespaced-adapted parses")]
    [DataRow("gml32-geom-AIXMSurface-renamespaced-adapted", DisplayName = "gml32-geom-AIXMSurface-renamespaced-adapted parses")]
    [DataRow("gml32-geom-CompositeCurve-rootcrs-adapted", DisplayName = "gml32-geom-CompositeCurve-rootcrs-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-geom-Curve-empty-adapted", DisplayName = "gml32-geom-Curve-empty-adapted parses")]
    [DataRow("gml32-geom-Curve-LineString-axisOrder-adapted", DisplayName = "gml32-geom-Curve-LineString-axisOrder-adapted parses")]
    [DataRow("gml32-geom-LineString-adapted", DisplayName = "gml32-geom-LineString-adapted parses")]
    [DataRow("gml32-geom-LineString-invalidCoords-adapted", DisplayName = "gml32-geom-LineString-invalidCoords-adapted parses")]
    [DataRow("gml32-geom-LineString-srsName-http-adapted", DisplayName = "gml32-geom-LineString-srsName-http-adapted parses")]
    [DataRow("gml32-geom-MultiCurve-1-adapted", DisplayName = "gml32-geom-MultiCurve-1-adapted parses")]
    [DataRow("gml32-geom-MultiCurve-2-adapted", DisplayName = "gml32-geom-MultiCurve-2-adapted parses")]
    [DataRow("gml32-geom-MultiPoint-1-adapted", DisplayName = "gml32-geom-MultiPoint-1-adapted parses")]
    [DataRow("gml32-geom-MultiSurface-adapted", DisplayName = "gml32-geom-MultiSurface-adapted parses")]
    [DataRow("gml32-geom-MultiSurface-ROSPA0080-adapted", DisplayName = "gml32-geom-MultiSurface-ROSPA0080-adapted parses")]
    [DataRow("gml32-geom-Point-2.5D-adapted", DisplayName = "gml32-geom-Point-2.5D-adapted parses")]
    [DataRow("gml32-geom-Point-27700-adapted", DisplayName = "gml32-geom-Point-27700-adapted parses")]
    [DataRow("gml32-geom-Point-epsg3045-adapted", DisplayName = "gml32-geom-Point-epsg3045-adapted parses")]
    [DataRow("gml32-geom-Point-srsNameOnPos-adapted", DisplayName = "gml32-geom-Point-srsNameOnPos-adapted refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("gml32-geom-Point-srsNameOnPos-rootcrs-adapted", DisplayName = "gml32-geom-Point-srsNameOnPos-rootcrs-adapted parses")]
    [DataRow("gml32-geom-Polygon-UTM-adapted", DisplayName = "gml32-geom-Polygon-UTM-adapted parses")]
    [DataRow("gml32-geom-Surface-DiscontiguousPatches-adapted", DisplayName = "gml32-geom-Surface-DiscontiguousPatches-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-geom-Surface-PolygonPatch-1-adapted", DisplayName = "gml32-geom-Surface-PolygonPatch-1-adapted parses")]
    [DataRow("gml32-geom-Surface-PolygonPatch-3-adapted", DisplayName = "gml32-geom-Surface-PolygonPatch-3-adapted parses")]
    [DataRow("gml32-geom-Surface-PolygonPatch-AxisOrder-adapted", DisplayName = "gml32-geom-Surface-PolygonPatch-AxisOrder-adapted parses")]
    [DataRow("gml32-geom-Surface-RectangleTriangle-adapted", DisplayName = "gml32-geom-Surface-RectangleTriangle-adapted refuses UnsupportedGeometry")]
    [DataRow("gml32-gmlring2-adapted", DisplayName = "gml32-gmlring2-adapted refuses UnrecognizedCoordinateReferenceSystem")]
    [DataRow("gml32-gmlring2-rootcrs-adapted", DisplayName = "gml32-gmlring2-rootcrs-adapted refuses StructuralViolation")]
    [TestMethod]
    public void DerivedTwinsClearWithTheirPinnedOutcomes(string twinIdentifier)
    {
        var twin = CiteGmlCorpusExpectations.Twins[twinIdentifier];
        string sourceText = File.ReadAllText(CiteGmlCorpusPaths.GetPath(twin.SourcePath));
        byte[] derived = CiteGmlCorpusDerivations.Derive(sourceText, twin.Rule);
        string derivedSha256 = Convert.ToHexStringLower(SHA256.HashData(derived));
        Assert.AreEqual(twin.DerivedSha256, derivedSha256, "The derivation produced different bytes than the recorded transformation.");

        bool accepted = GmlGeometryReader.TryRead(
            derived,
            out FlatGeometry geometry,
            out CoordinateReferenceSystem coordinateReference,
            out GeometryCodecRefusal refusal);

        if(twin.Outcome.Accepts)
        {
            Assert.IsTrue(accepted, $"Expected a parse but the reader refused {refusal.Kind} at {refusal.ByteOffset}.");
            AssertAcceptedFacts(in geometry, coordinateReference, twin.Outcome);
        }
        else
        {
            Assert.IsFalse(accepted, "Expected a refusal but the reader accepted the twin.");
            Assert.AreEqual(twin.Outcome.RefusalKind, refusal.Kind, "The refusal kind drifted from the pinned outcome.");
            Assert.AreEqual(twin.Outcome.RefusalByteOffset, refusal.ByteOffset, "The refusal anchor drifted from the pinned outcome.");
        }
    }

    /// <summary>Asserts the pinned value facts of an accepted artifact: kind, emptiness, system, and canonical digest.</summary>
    private static void AssertAcceptedFacts(
        in FlatGeometry geometry,
        CoordinateReferenceSystem coordinateReference,
        CiteGmlCorpusExpectations.CorpusExpectation expectation)
    {
        Assert.AreEqual(expectation.GeometryKind, geometry.Kind, "The geometry kind drifted from the pinned outcome.");
        Assert.AreEqual(expectation.IsEmpty, geometry.IsEmpty, "The emptiness fact drifted from the pinned outcome.");
        Assert.AreEqual(expectation.CoordinateReferenceIri, coordinateReference.Iri, "The recognized system drifted from the pinned outcome.");

        string canonicalText = WktGeometryWriter.WriteString(in geometry);
        string canonicalSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)));
        Assert.AreEqual(expectation.WktSha256, canonicalSha256, "The materialized value drifted from the pinned canonical digest.");
    }
}
