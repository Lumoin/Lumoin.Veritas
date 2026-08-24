using System;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Transforms;
using Lumoin.Veritas.Geo.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The GML reader family: roster acceptance with reference-text cross-checks, the
/// six-spelling system recognition, the dimension walk, the curve and ring and
/// surface machinery with certified linearization, and the rejection matrix over
/// the recognized-but-refused vocabulary, the structural rules, and the security
/// floor through the public reader. Refusal rows assert the kind AND the byte
/// offset, and offsets are computed from markers, never hand-counted.
/// </summary>
[TestClass]
internal sealed class GmlGeometryReaderTests
{
    /// <summary>Linear roster documents equal their reference-text readings structurally and bitwise.</summary>
    [TestMethod]
    [DataRow("Point", "<gml:pos>1 2</gml:pos>", "POINT (1 2)", DisplayName = "the point materializes from its position")]
    [DataRow("Point", "<gml:posList>1 2</gml:posList>", "POINT (1 2)", DisplayName = "a one-position list carries a point under the documented widening")]
    [DataRow("LineString", "<gml:posList>0 0 1 1 2 0</gml:posList>", "LINESTRING (0 0, 1 1, 2 0)", DisplayName = "the position list carries a line string")]
    [DataRow("LineString", "<gml:pos>0 0</gml:pos><gml:pos>1 1</gml:pos><gml:pos>2 0</gml:pos>", "LINESTRING (0 0, 1 1, 2 0)", DisplayName = "repeated single positions read everywhere the list does")]
    [DataRow("Polygon", GmlTestDocuments.SquarePolygonBody, "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "the polygon reads its exterior")]
    [DataRow("Polygon", GmlTestDocuments.SquarePolygonBody + "<gml:interior><gml:LinearRing><gml:posList>1 1 2 1 2 2 1 1</gml:posList></gml:LinearRing></gml:interior>", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "the polygon reads its interior after the exterior")]
    [DataRow("MultiPoint", "<gml:pointMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:pointMember><gml:pointMember><gml:Point><gml:pos>3 4</gml:pos></gml:Point></gml:pointMember>", "MULTIPOINT ((1 2), (3 4))", DisplayName = "singular point members aggregate")]
    [DataRow("MultiPoint", "<gml:pointMembers><gml:Point><gml:pos>1 2</gml:pos></gml:Point><gml:Point><gml:pos>3 4</gml:pos></gml:Point></gml:pointMembers>", "MULTIPOINT ((1 2), (3 4))", DisplayName = "the plural member container reads wider than the profile writes")]
    [DataRow("MultiCurve", "<gml:curveMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:curveMember>", "MULTILINESTRING ((0 0, 1 1))", DisplayName = "a line string member aggregates into the multi curve")]
    [DataRow("MultiCurve", "<gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember>", "MULTILINESTRING ((0 0, 1 1))", DisplayName = "a segmented curve member flattens into the multi curve")]
    [DataRow("MultiSurface", "<gml:surfaceMember><gml:Polygon>" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon></gml:surfaceMember>", "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)))", DisplayName = "a polygon member aggregates into the multi surface")]
    [DataRow("MultiGeometry", "<gml:geometryMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember><gml:geometryMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:geometryMember>", "GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", DisplayName = "the heterogeneous collection carries its members")]
    public void AcceptedDocumentsMatchTheirReferenceText(string localName, string body, string wkt)
    {
        GmlAssert.MatchesWkt(GmlTestDocuments.Root(localName, body), CoordinateReferenceSystem.Crs84, wkt);
    }

    /// <summary>A segmented curve materializes as a line string equal to its reference reading.</summary>
    [TestMethod]
    public void TheCurveMaterializesAsALineString()
    {
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("Curve", GmlTestDocuments.LinearCurveBody), CoordinateReferenceSystem.Crs84, "LINESTRING (0 0, 1 1, 2 0)");
    }

    /// <summary>A surface with one planar patch normalizes to the polygon.</summary>
    [TestMethod]
    public void TheOnePatchSurfaceNormalizesToThePolygon()
    {
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("Surface", GmlTestDocuments.OnePatchSurfaceBody), CoordinateReferenceSystem.Crs84, "POLYGON ((0 0, 4 0, 4 4, 0 0))");
    }

    /// <summary>A surface with several patches normalizes to the multi polygon, and a surface member flattens beside a polygon member.</summary>
    [TestMethod]
    public void SurfacePatchesNormalizeAndFlatten()
    {
        string twoPatches = "<gml:patches><gml:PolygonPatch>" + GmlTestDocuments.SquarePolygonBody + "</gml:PolygonPatch><gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>10 10 14 10 14 14 10 10</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch></gml:patches>";
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("Surface", twoPatches), CoordinateReferenceSystem.Crs84, "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))");

        string flattening = "<gml:surfaceMember><gml:Polygon>" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon></gml:surfaceMember><gml:surfaceMember><gml:Surface><gml:patches><gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>10 10 14 10 14 14 10 10</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface></gml:surfaceMember>";
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("MultiSurface", flattening), CoordinateReferenceSystem.Crs84, "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))");
    }

    /// <summary>Memberless and boundary-less forms read as the typed empties at their sanctioned positions.</summary>
    [TestMethod]
    [DataRow("MultiPoint", "", (int)GeometryKind.MultiPoint, DisplayName = "the memberless multi point is the typed empty")]
    [DataRow("MultiCurve", "", (int)GeometryKind.MultiLineString, DisplayName = "the memberless multi curve is the typed empty")]
    [DataRow("MultiSurface", "", (int)GeometryKind.MultiPolygon, DisplayName = "the memberless multi surface is the typed empty")]
    [DataRow("MultiGeometry", "", (int)GeometryKind.GeometryCollection, DisplayName = "the memberless collection is the typed empty")]
    [DataRow("Polygon", "", (int)GeometryKind.Polygon, DisplayName = "the exterior-less polygon is the typed empty")]
    [DataRow("Curve", "<gml:segments></gml:segments>", (int)GeometryKind.LineString, DisplayName = "the zero-segment curve is the typed empty line string")]
    [DataRow("Surface", "<gml:patches></gml:patches>", (int)GeometryKind.Polygon, DisplayName = "the patch-less surface is the typed empty polygon")]
    public void TypedEmptiesReadFromTheirSchemaValidForms(string localName, string body, int expectedKind)
    {
        using FlatGeometry geometry = GmlAssert.Accepts(GmlTestDocuments.Root(localName, body), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual((GeometryKind)expectedKind, geometry.Kind, "the typed empty keeps its kind");
        Assert.IsTrue(geometry.IsEmpty, "the value is empty");

        using FlatGeometry expected = FlatGeometry.Empty((GeometryKind)expectedKind);
        Assert.AreEqual(expected, geometry, "the value equals the typed empty every other producer makes");
    }

    /// <summary>Every roster spelling recognizes its system, and coordinates ride the declared axis order unswapped.</summary>
    [TestMethod]
    [DataRow(GmlTestDocuments.Crs84, 0, DisplayName = "the CRS84 canonical spelling recognizes")]
    [DataRow(GmlTestDocuments.Epsg4326, 1, DisplayName = "the EPSG four-three-two-six canonical spelling recognizes")]
    [DataRow(GmlTestDocuments.WebMercator, 2, DisplayName = "the web mercator canonical spelling recognizes")]
    [DataRow("urn:ogc:def:crs:OGC:1.3:CRS84", 0, DisplayName = "the CRS84 urn twin recognizes")]
    [DataRow("urn:ogc:def:crs:EPSG::4326", 1, DisplayName = "the EPSG four-three-two-six urn twin recognizes")]
    [DataRow("urn:ogc:def:crs:EPSG::3857", 2, DisplayName = "the web mercator urn twin recognizes")]
    public void EveryRosterSpellingRecognizesItsSystem(string spelling, int systemIndex)
    {
        CoordinateReferenceSystem expected = systemIndex switch
        {
            0 => CoordinateReferenceSystem.Crs84,
            1 => CoordinateReferenceSystem.Epsg4326,
            _ => CoordinateReferenceSystem.WebMercator,
        };
        using FlatGeometry geometry = GmlAssert.Accepts(GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody, spelling), expected);
        Assert.AreEqual(new Point2d(1.0, 2.0), geometry.Vertices[0], "the ordinates ride as written — the reader never reorders");
    }

    /// <summary>A latitude-longitude document materializes latitude into the first ordinate — the axis-order fact row.</summary>
    [TestMethod]
    public void TheLatitudeLongitudeSystemMaterializesLatitudeFirst()
    {
        using FlatGeometry geometry = GmlAssert.Accepts(GmlTestDocuments.Root("Point", "<gml:pos>60.17 24.94</gml:pos>", GmlTestDocuments.Epsg4326), CoordinateReferenceSystem.Epsg4326);
        Assert.AreEqual(new Point2d(60.17, 24.94), geometry.Vertices[0], "latitude rides first under the latitude-longitude system");
    }

    /// <summary>The third dimension reads from the declaration walk: on the root, on the carrier, or inferred from a bare position.</summary>
    [TestMethod]
    [DataRow(" srsDimension=\"3\"", "<gml:pos>1 2 5</gml:pos>", DisplayName = "the root declaration carries the third dimension")]
    [DataRow("", "<gml:pos srsDimension=\"3\">1 2 5</gml:pos>", DisplayName = "the carrier declaration carries the third dimension")]
    [DataRow("", "<gml:pos>1 2 5</gml:pos>", DisplayName = "a bare three-token position infers the third dimension")]
    public void TheThirdDimensionReadsFromTheDeclarationWalk(string rootAttributes, string body)
    {
        string document = GmlTestDocuments.Root("Point", body, GmlTestDocuments.Crs84, rootAttributes);
        GmlAssert.MatchesWkt(document, CoordinateReferenceSystem.Crs84, "POINT Z (1 2 5)");
    }

    /// <summary>A nested declaration repeating the root spelling accepts; the same system in a different roster spelling refuses.</summary>
    [TestMethod]
    public void NestedDeclarationsMustRepeatTheRootSpelling()
    {
        string same = GmlTestDocuments.Root("MultiGeometry", $"<gml:geometryMember><gml:Point srsName=\"{GmlTestDocuments.Crs84}\"><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember>");
        using FlatGeometry accepted = GmlAssert.Accepts(same, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.GeometryCollection, accepted.Kind, "the same-spelling nested declaration is tolerated");

        string different = GmlTestDocuments.Root("MultiGeometry", "<gml:geometryMember><gml:Point srsName=\"urn:ogc:def:crs:OGC:1.3:CRS84\"><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember>");
        GmlAssert.RefusesAt(different, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "urn:ogc:def:crs:OGC:1.3:CRS84");
    }

    /// <summary>The curve joins its segments at the shared position, emitted once, and a break refuses at the diverging position's carrier.</summary>
    [TestMethod]
    public void TheCurveJoinsItsSegmentsAtTheSharedPosition()
    {
        using FlatGeometry joined = GmlAssert.Accepts(GmlTestDocuments.Root("Curve", GmlTestDocuments.JoinedCurveBody), CoordinateReferenceSystem.Crs84);
        int vertexCount = joined.Vertices.Length;
        Assert.AreEqual(514, vertexCount, "two linear vertices plus the half-open quarter-gap linearization, the joined vertex once");
        Assert.AreEqual(new Point2d(-2.0, 0.0), joined.Vertices[0], "the run opens at the first segment's first position");
        Assert.AreEqual(new Point2d(0.0, -1.0), joined.Vertices[1], "the joined vertex is the earlier segment's copy");
        Assert.AreEqual(new Point2d(0.0, 1.0), joined.Vertices[^1], "the run closes at the arc's end control point verbatim");

        string broken = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>-2 0 0 -1</gml:posList></gml:LineStringSegment><gml:Arc><gml:posList>9 9 1 0 0 1</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(broken, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>9 9");
    }

    /// <summary>A ring bounded by one full circle closes bitwise and carries the certified linearization.</summary>
    [TestMethod]
    public void TheCircleBoundedRingClosesBitwise()
    {
        using FlatGeometry polygon = GmlAssert.Accepts(GmlTestDocuments.Root("Polygon", GmlTestDocuments.CircleRingPolygonBody), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.Polygon, polygon.Kind, "the ring bounds a polygon");

        int vertexCount = polygon.Vertices.Length;
        Assert.AreEqual(1025, vertexCount, "the circle linearizes at the published bound");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(polygon.Vertices[0].X), BitConverter.DoubleToInt64Bits(polygon.Vertices[^1].X), "the ring closes on the opening vertex bit for bit");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(polygon.Vertices[0].Y), BitConverter.DoubleToInt64Bits(polygon.Vertices[^1].Y), "the ring closes on the opening vertex bit for bit on the second ordinate");
    }

    /// <summary>The center-and-radius circle reads under its system's own unit and refuses every other unit — the profile's own example refuses.</summary>
    [TestMethod]
    public void TheCenterRadiusCircleRulesItsUnit()
    {
        using FlatGeometry degrees = GmlAssert.Accepts(GmlTestDocuments.Root("Curve", GmlTestDocuments.CenterRadiusCurveBody), CoordinateReferenceSystem.Crs84);
        int vertexCount = degrees.Vertices.Length;
        Assert.AreEqual(1025, vertexCount, "the cardinal-seeded circle linearizes at the published bound");

        string metreUnderWebMercator = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"m\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>", GmlTestDocuments.WebMercator);
        using FlatGeometry metres = GmlAssert.Accepts(metreUnderWebMercator, CoordinateReferenceSystem.WebMercator);
        Assert.AreEqual(GeometryKind.LineString, metres.Kind, "the metre radius reads under the metre system");

        string metreUnderDegrees = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>51.389 30.099</gml:pos><gml:radius uom=\"m\">20000</gml:radius></gml:CircleByCenterPoint></gml:segments>", GmlTestDocuments.Epsg4326);
        GmlAssert.RefusesAt(metreUnderDegrees, GeometryCodecRefusalKind.StructuralViolation, "m\">20000");

        string unknownUnit = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"km\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(unknownUnit, GeometryCodecRefusalKind.StructuralViolation, "km");

        string absentUnit = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius>2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(absentUnit, GeometryCodecRefusalKind.StructuralViolation, ">2</gml:radius>");
    }

    /// <summary>The center-and-radius circle's structural matrix: the required arc count, the removed bearing angles, the sole-segment rule, and the radius value rules.</summary>
    [TestMethod]
    public void TheCenterRadiusCircleStructuralMatrix()
    {
        string absentNumArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(absentNumArc, GeometryCodecRefusalKind.StructuralViolation, "><gml:pos>10 20");

        string wrongNumArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"2\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(wrongNumArc, GeometryCodecRefusalKind.StructuralViolation, "2\"><gml:pos>");

        string paddedNumArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"+1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(paddedNumArc, GeometryCodecRefusalKind.StructuralViolation, "+1");

        string bearing = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius><gml:startAngle uom=\"deg\">0</gml:startAngle></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(bearing, GeometryCodecRefusalKind.StructuralViolation, "<gml:startAngle");

        string midList = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(midList, GeometryCodecRefusalKind.StructuralViolation, "<gml:CircleByCenterPoint");

        string negativeRadius = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">-2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(negativeRadius, GeometryCodecRefusalKind.StructuralViolation, "-2");

        string infiniteRadius = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">INF</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(infiniteRadius, GeometryCodecRefusalKind.NonFiniteCoordinate, "INF");

        string duplicatedCenter = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:pos>11 21</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(duplicatedCenter, GeometryCodecRefusalKind.StructuralViolation, "<gml:pos>11 21");
    }

    /// <summary>Arc degeneracies refuse at their control points, and position counts other than three refuse structurally.</summary>
    [TestMethod]
    public void ArcDegeneraciesAndCountsRefuse()
    {
        string collinear = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 0 1 1 2 2</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(collinear, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 1 1 2 2");

        string coincident = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 0 0 0 2 2</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(coincident, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 0 0 2 2");

        string fourPositions = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 0 1 1 2 0 3 1</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(fourPositions, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 1 1 2 0 3 1");

        string twoPositions = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 0 1 1</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(twoPositions, GeometryCodecRefusalKind.StructuralViolation, "</gml:Arc>");

        string sixTokensUnderThree = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>", GmlTestDocuments.Crs84, " srsDimension=\"3\"");
        GmlAssert.RefusesAt(sixTokensUnderThree, GeometryCodecRefusalKind.StructuralViolation, "</gml:Arc>");
    }

    /// <summary>The fixed interpolation tokens validate per segment type and the ring aggregation admits only its sequence value.</summary>
    [TestMethod]
    public void FixedAttributeTokensValidate()
    {
        string wrongInterpolation = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment interpolation=\"circularArc3Points\"><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(wrongInterpolation, GeometryCodecRefusalKind.StructuralViolation, "circularArc3Points");

        string rightInterpolation = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment interpolation=\"linear\"><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment></gml:segments>");
        using FlatGeometry accepted = GmlAssert.Accepts(rightInterpolation, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.LineString, accepted.Kind, "the fixed token in its fixed spelling is tolerated");

        string wrongAggregation = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring aggregationType=\"set\"><gml:curveMember><gml:Curve><gml:segments><gml:Circle><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Circle></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(wrongAggregation, GeometryCodecRefusalKind.StructuralViolation, "set");
    }

    /// <summary>The ring restriction: one curve member with one curve, no line-string member, no empty curve, and the cycle rule.</summary>
    [TestMethod]
    public void TheRingRestrictionRefusesItsViolations()
    {
        string lineStringMember = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:LineString><gml:posList>0 0 1 0 1 1 0 0</gml:posList></gml:LineString></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(lineStringMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:LineString>");

        string orientableMember = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:OrientableCurve></gml:OrientableCurve></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(orientableMember, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:OrientableCurve");

        string emptyCurve = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(emptyCurve, GeometryCodecRefusalKind.StructuralViolation, "</gml:Curve>");

        string openCurve = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>0 0 1 0 1 1 2 2</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(openCurve, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 1 0 1 1 2 2");

        string ringLessBoundary = GmlTestDocuments.Root("Polygon", "<gml:exterior></gml:exterior>");
        GmlAssert.RefusesAt(ringLessBoundary, GeometryCodecRefusalKind.StructuralViolation, "</gml:exterior>");
    }

    /// <summary>Polygon boundary ordering and cardinality: interiors before an exterior refuse, a second exterior refuses, and unclosed or short linear rings refuse.</summary>
    [TestMethod]
    public void PolygonBoundaryRulesRefuse()
    {
        string interiorFirst = GmlTestDocuments.Root("Polygon", "<gml:interior><gml:LinearRing><gml:posList>1 1 2 1 2 2 1 1</gml:posList></gml:LinearRing></gml:interior>");
        GmlAssert.RefusesAt(interiorFirst, GeometryCodecRefusalKind.StructuralViolation, "<gml:interior>");

        string twoExteriors = GmlTestDocuments.Root("Polygon", GmlTestDocuments.SquarePolygonBody + GmlTestDocuments.SquarePolygonBody);
        int secondExterior = GmlTestDocuments.Root("Polygon", GmlTestDocuments.SquarePolygonBody).Length - "</gml:Polygon>".Length;
        GmlAssert.Refuses(twoExteriors, GeometryCodecRefusalKind.StructuralViolation, secondExterior);

        string unclosed = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 4 4 1 1</gml:posList></gml:LinearRing></gml:exterior>");
        GmlAssert.RefusesAt(unclosed, GeometryCodecRefusalKind.StructuralViolation, "</gml:LinearRing>");

        string short3 = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 0 0</gml:posList></gml:LinearRing></gml:exterior>");
        GmlAssert.RefusesAt(short3, GeometryCodecRefusalKind.StructuralViolation, "</gml:LinearRing>");
    }

    /// <summary>The declaration walk's dimension rules: out-of-range values, conflicts along one path, non-divisible lists, and the count attribute's split defects.</summary>
    [TestMethod]
    public void DimensionAndCountRulesRefuse()
    {
        string fourDimensional = GmlTestDocuments.Root("Point", "<gml:pos srsDimension=\"4\">1 2 3 4</gml:pos>");
        GmlAssert.RefusesAt(fourDimensional, GeometryCodecRefusalKind.DimensionMismatch, "4\">1 2 3 4");

        string conflict = GmlTestDocuments.Root("Point", "<gml:pos srsDimension=\"3\">1 2 5</gml:pos>", GmlTestDocuments.Crs84, " srsDimension=\"2\"");
        GmlAssert.RefusesAt(conflict, GeometryCodecRefusalKind.DimensionMismatch, "3\">1 2 5");

        string nonDivisible = GmlTestDocuments.Root("LineString", "<gml:posList>0 0 1 1 2</gml:posList>");
        GmlAssert.RefusesAt(nonDivisible, GeometryCodecRefusalKind.DimensionMismatch, "</gml:posList>");

        string countWithoutDimension = GmlTestDocuments.Root("LineString", "<gml:posList count=\"2\">0 0 1 1</gml:posList>");
        GmlAssert.RefusesAt(countWithoutDimension, GeometryCodecRefusalKind.DimensionMismatch, "2\">0 0");

        string countMismatch = GmlTestDocuments.Root("LineString", "<gml:posList srsDimension=\"2\" count=\"3\">0 0 1 1</gml:posList>");
        GmlAssert.RefusesAt(countMismatch, GeometryCodecRefusalKind.StructuralViolation, "3\">0 0");

        string mixedMembers = GmlTestDocuments.Root("MultiPoint", "<gml:pointMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:pointMember><gml:pointMember><gml:Point><gml:pos>1 2 5</gml:pos></gml:Point></gml:pointMember>");
        GmlAssert.RefusesAt(mixedMembers, GeometryCodecRefusalKind.DimensionMismatch, "</gml:pos></gml:Point></gml:pointMember></gml:MultiPoint>");
    }

    /// <summary>The system declaration is required on the root and recognized against the closed roster.</summary>
    [TestMethod]
    public void TheRootSystemDeclarationIsRequiredAndClosed()
    {
        string absent = GmlTestDocuments.RootWithoutSystem("Point", GmlTestDocuments.PointBody);
        GmlAssert.RefusesAt(absent, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "><gml:pos>");

        string unrecognized = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody, "http://www.opengis.net/def/crs/EPSG/0/25833");
        GmlAssert.RefusesAt(unrecognized, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "http://www.opengis.net/def/crs/EPSG/0/25833");
    }

    /// <summary>Recognized non-simple-features vocabulary refuses as unsupported; foreign namespaces and unknown names refuse too.</summary>
    [TestMethod]
    [DataRow("Tin", DisplayName = "the triangulated network refuses as unsupported")]
    [DataRow("Solid", DisplayName = "the solid refuses as unsupported")]
    [DataRow("MultiLineString", DisplayName = "the removed legacy aggregate spelling refuses as unsupported")]
    [DataRow("MultiPolygon", DisplayName = "the removed legacy surface aggregate spelling refuses as unsupported")]
    [DataRow("OrientableCurve", DisplayName = "the orientable curve refuses as unsupported")]
    public void RefusedVocabularyRefusesAtTheRoot(string localName)
    {
        string document = GmlTestDocuments.Root(localName, "");
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.UnsupportedGeometry, $"<gml:{localName}");
    }

    /// <summary>The legacy namespace root refuses as unsupported; the deprecated coordinates carrier and the by-reference carriers refuse inside recognized elements.</summary>
    [TestMethod]
    public void ForeignNamespacesAndDeclinedCarriersRefuse()
    {
        string legacyNamespace = $"<gml:Point xmlns:gml=\"http://www.opengis.net/gml\" srsName=\"{GmlTestDocuments.Crs84}\"><gml:pos>1 2</gml:pos></gml:Point>";
        GmlAssert.RefusesAt(legacyNamespace, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:Point");

        string deprecated = GmlTestDocuments.Root("Point", "<gml:coordinates>1,2</gml:coordinates>");
        GmlAssert.RefusesAt(deprecated, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:coordinates>");

        string byReference = GmlTestDocuments.Root("LineString", "<gml:pointProperty></gml:pointProperty>");
        GmlAssert.RefusesAt(byReference, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:pointProperty>");
    }

    /// <summary>A remote reference on a member or boundary property refuses as prohibited.</summary>
    [TestMethod]
    public void RemoteReferencesRefuseAsProhibited()
    {
        string document = $"<gml:MultiGeometry {GmlTestDocuments.NamespaceDeclaration} xmlns:xlink=\"http://www.w3.org/1999/xlink\" srsName=\"{GmlTestDocuments.Crs84}\"><gml:geometryMember xlink:href=\"#远\"></gml:geometryMember></gml:MultiGeometry>";
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.ProhibitedConstruct, "xlink:href");
    }

    /// <summary>The wrong-category member matrix: a point under a curve member is structural, non-simple-features vocabulary under an admitting container is unsupported, and empty members refuse.</summary>
    [TestMethod]
    public void MemberCategoryAndEmptyMemberRulesRefuse()
    {
        string pointUnderCurveMember = GmlTestDocuments.Root("MultiCurve", "<gml:curveMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:curveMember>");
        GmlAssert.RefusesAt(pointUnderCurveMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:Point>");

        string splineUnderCurveMember = GmlTestDocuments.Root("MultiCurve", "<gml:curveMember><gml:CompositeCurve></gml:CompositeCurve></gml:curveMember>");
        GmlAssert.RefusesAt(splineUnderCurveMember, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:CompositeCurve>");

        string emptyCurveMember = GmlTestDocuments.Root("MultiCurve", "<gml:curveMember><gml:Curve><gml:segments></gml:segments></gml:Curve></gml:curveMember>");
        GmlAssert.RefusesAt(emptyCurveMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:Curve>");

        string emptySurfaceMember = GmlTestDocuments.Root("MultiSurface", "<gml:surfaceMember><gml:Surface><gml:patches></gml:patches></gml:Surface></gml:surfaceMember>");
        GmlAssert.RefusesAt(emptySurfaceMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:Surface>");

        string childlessMember = GmlTestDocuments.Root("MultiPoint", "<gml:pointMember></gml:pointMember>");
        GmlAssert.RefusesAt(childlessMember, GeometryCodecRefusalKind.StructuralViolation, "</gml:pointMember>");
    }

    /// <summary>Non-linear segment vocabulary outside the profile's four refuses as unsupported inside the segment container.</summary>
    [TestMethod]
    public void NonProfileSegmentsRefuse()
    {
        string arcString = GmlTestDocuments.Root("Curve", "<gml:segments><gml:ArcString><gml:posList>0 0 1 1 2 0 3 1 4 0</gml:posList></gml:ArcString></gml:segments>");
        GmlAssert.RefusesAt(arcString, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:ArcString>");

        string arcByCenter = GmlTestDocuments.Root("Curve", "<gml:segments><gml:ArcByCenterPoint numArc=\"1\"><gml:pos>0 0</gml:pos><gml:radius uom=\"deg\">1</gml:radius></gml:ArcByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(arcByCenter, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:ArcByCenterPoint");

        string absentSegments = GmlTestDocuments.Root("Curve", "");
        GmlAssert.RefusesAt(absentSegments, GeometryCodecRefusalKind.StructuralViolation, "</gml:Curve>");
    }

    /// <summary>The geometry bound: a nest of thirty-one wrappers around a leaf accepts, and the thirty-second wrapper's leaf refuses.</summary>
    [TestMethod]
    public void TheGeometryBoundAcceptsThirtyOneWrappersAndRefusesTheThirtySecond()
    {
        using FlatGeometry deep = GmlAssert.Accepts(GmlTestDocuments.NestedCollections(31), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.GeometryCollection, deep.Kind, "thirty-one wrappers and the leaf sit at the bound");

        string past = GmlTestDocuments.NestedCollections(32);
        GmlAssert.RefusesAt(past, GeometryCodecRefusalKind.NestingTooDeep, "<gml:Point>");
    }

    /// <summary>The security floor fires through the public reader: the document type declaration and the processing instruction refuse before any geometry parses.</summary>
    [TestMethod]
    public void TheSecurityFloorFiresThroughThePublicReader()
    {
        string doctype = "<!DOCTYPE gml [<!ENTITY x \"y\">]>" + GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody);
        GmlAssert.RefusesAt(doctype, GeometryCodecRefusalKind.ProhibitedConstruct, "<!DOCTYPE");

        string processingInstruction = GmlTestDocuments.Root("Point", "<?ordinate 1?><gml:pos>1 2</gml:pos>");
        GmlAssert.RefusesAt(processingInstruction, GeometryCodecRefusalKind.ProhibitedConstruct, "<?ordinate");
    }

    /// <summary>Trailing content refuses at its first byte and truncation at the input length.</summary>
    [TestMethod]
    public void TrailingContentAndTruncationRefuse()
    {
        string trailing = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody) + "junk";
        GmlAssert.RefusesAt(trailing, GeometryCodecRefusalKind.TrailingContent, "junk");

        string whole = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody);
        string truncated = whole[..(whole.Length - 4)];
        GmlAssert.Refuses(truncated, GeometryCodecRefusalKind.MalformedDocument, Encoding.UTF8.GetByteCount(truncated));
    }

    /// <summary>Non-finite ordinate tokens the double grammar legally admits refuse as non-finite values, and lexical garbage as malformed.</summary>
    [TestMethod]
    public void OrdinateTokenOffensesRefuseAtTheirBytes()
    {
        string notANumber = GmlTestDocuments.Root("Point", "<gml:pos>NaN 2</gml:pos>");
        GmlAssert.RefusesAt(notANumber, GeometryCodecRefusalKind.NonFiniteCoordinate, "NaN");

        string infinity = GmlTestDocuments.Root("Point", "<gml:pos>1 -INF</gml:pos>");
        GmlAssert.RefusesAt(infinity, GeometryCodecRefusalKind.NonFiniteCoordinate, "-INF");

        string overflow = GmlTestDocuments.Root("Point", "<gml:pos>1 1e999</gml:pos>");
        GmlAssert.RefusesAt(overflow, GeometryCodecRefusalKind.NonFiniteCoordinate, "1e999");

        string garbage = GmlTestDocuments.Root("Point", "<gml:pos>1 north</gml:pos>");
        GmlAssert.RefusesAt(garbage, GeometryCodecRefusalKind.MalformedDocument, "north");

        string positiveSigned = GmlTestDocuments.Root("Point", "<gml:pos>+1 .5</gml:pos>");
        using FlatGeometry accepted = GmlAssert.Accepts(positiveSigned, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(new Point2d(1.0, 0.5), accepted.Vertices[0], "the double grammar's signed and bare-fraction spellings parse");
    }

    /// <summary>Ignorable whitespace is tolerated everywhere between elements; non-whitespace text where members are expected refuses at its first byte.</summary>
    [TestMethod]
    public void WhitespaceToleranceAndTextRefusal()
    {
        string indented = $"<gml:Point {GmlTestDocuments.NamespaceDeclaration} srsName=\"{GmlTestDocuments.Crs84}\">\n  <gml:pos>1 2</gml:pos>\n</gml:Point>";
        using FlatGeometry accepted = GmlAssert.Accepts(indented, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.Point, accepted.Kind, "indentation between elements is ignorable");

        string text = GmlTestDocuments.Root("MultiPoint", "stray");
        GmlAssert.RefusesAt(text, GeometryCodecRefusalKind.MalformedDocument, "stray");
    }

    /// <summary>The character-span convenience reports offsets into the transcoded UTF-8 representation.</summary>
    [TestMethod]
    public void TheCharacterOverloadReportsTranscodedOffsets()
    {
        string document = $"<gml:Point {GmlTestDocuments.NamespaceDeclaration} id=\"ä\" srsName=\"{GmlTestDocuments.Crs84}\"><gml:pos>1 north</gml:pos></gml:Point>";
        bool accepted = GmlGeometryReader.TryRead(document.AsSpan(), out FlatGeometry geometry, out CoordinateReferenceSystem system, out GeometryCodecRefusal refusal);
        Assert.IsFalse(accepted, "the garbage ordinate must refuse through the character overload");
        Assert.AreEqual(GeometryCodecRefusalKind.MalformedDocument, refusal.Kind, "the refusal kind is the token offense");
        Assert.AreEqual(GmlAssert.ByteOffsetOf(document, "north"), refusal.ByteOffset, "the offset indexes the transcoded representation, where the two-byte character widens it");
        Assert.AreEqual(default, geometry, "the geometry out is default");
        Assert.AreEqual(default(CoordinateReferenceSystem), system, "the system out is default");
    }

    /// <summary>A refusal after successful recognition still leaves the system out at its default — recognition never leaks through a refusal.</summary>
    [TestMethod]
    public void LateRefusalsLeaveTheSystemOutAtDefault()
    {
        string document = GmlTestDocuments.Root("Point", "<gml:pos>1</gml:pos>");
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.DimensionMismatch, "</gml:pos>");
    }

    /// <summary>The surface patch rules: recognized non-planar patch vocabulary is unsupported, a foreign-namespace patch and an absent patch container are structural, and the patch interpolation admits only its planar token.</summary>
    [TestMethod]
    public void SurfacePatchRulesRefuse()
    {
        string trianglePatch = GmlTestDocuments.Root("Surface", "<gml:patches><gml:Triangle></gml:Triangle></gml:patches>");
        GmlAssert.RefusesAt(trianglePatch, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:Triangle");

        string foreignPatch = GmlTestDocuments.Root("Surface", "<gml:patches><f:PolygonPatch xmlns:f=\"urn:example:patches\"></f:PolygonPatch></gml:patches>");
        GmlAssert.RefusesAt(foreignPatch, GeometryCodecRefusalKind.StructuralViolation, "<f:PolygonPatch");

        string absentPatches = GmlTestDocuments.Root("Surface", "");
        GmlAssert.RefusesAt(absentPatches, GeometryCodecRefusalKind.StructuralViolation, "</gml:Surface>");

        string sphericalPatch = GmlTestDocuments.Root("Surface", "<gml:patches><gml:PolygonPatch interpolation=\"spherical\">" + GmlTestDocuments.SquarePolygonBody + "</gml:PolygonPatch></gml:patches>");
        GmlAssert.RefusesAt(sphericalPatch, GeometryCodecRefusalKind.StructuralViolation, "spherical");
    }

    /// <summary>The planar interpolation token on a patch is tolerated and reads identically to the attribute-less form.</summary>
    [TestMethod]
    public void ThePlanarPatchInterpolationTokenIsTolerated()
    {
        string declared = GmlTestDocuments.Root("Surface", "<gml:patches><gml:PolygonPatch interpolation=\"planar\">" + GmlTestDocuments.SquarePolygonBody + "</gml:PolygonPatch></gml:patches>");
        using FlatGeometry withToken = GmlAssert.Accepts(declared, CoordinateReferenceSystem.Crs84);

        using FlatGeometry bare = GmlAssert.Accepts(GmlTestDocuments.Root("Surface", GmlTestDocuments.OnePatchSurfaceBody), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(bare, withToken, "the fixed token in its fixed spelling changes nothing");
    }

    /// <summary>A surface member with several patches flattens beside a polygon member into one multi polygon.</summary>
    [TestMethod]
    public void AMultiPatchSurfaceMemberFlattensBesideAPolygonMember()
    {
        string body = "<gml:surfaceMember><gml:Polygon>" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon></gml:surfaceMember>"
            + "<gml:surfaceMember><gml:Surface><gml:patches>"
            + "<gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>10 10 14 10 14 14 10 10</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch>"
            + "<gml:PolygonPatch><gml:exterior><gml:LinearRing><gml:posList>20 20 24 20 24 24 20 20</gml:posList></gml:LinearRing></gml:exterior></gml:PolygonPatch>"
            + "</gml:patches></gml:Surface></gml:surfaceMember>";
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("MultiSurface", body), CoordinateReferenceSystem.Crs84, "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)), ((20 20, 24 20, 24 24, 20 20)))");
    }

    /// <summary>The curve-bounded ring's membership rules: a second curve member, a memberless ring, a remote member reference, and the four-position floor after closure.</summary>
    [TestMethod]
    public void CurveRingMembershipAndClosureRefuse()
    {
        string secondMember = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:Circle><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Circle></gml:segments></gml:Curve></gml:curveMember><gml:curveMember></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(secondMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:curveMember></gml:curveMember>");

        string memberless = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(memberless, GeometryCodecRefusalKind.StructuralViolation, "</gml:Ring>");

        string remoteMember = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember xlink:href=\"#c\"></gml:curveMember></gml:Ring></gml:exterior>", GmlTestDocuments.Crs84, " xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
        GmlAssert.RefusesAt(remoteMember, GeometryCodecRefusalKind.ProhibitedConstruct, "xlink:href");

        string threePositions = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>0 0 1 0 0 0</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        GmlAssert.RefusesAt(threePositions, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 1 0 0 0");
    }

    /// <summary>The ring closure predicate is planar: a ring closed in the first two ordinates accepts with the seam open in the third.</summary>
    [TestMethod]
    [DataRow("<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList srsDimension=\"3\">0 0 5 4 0 5 4 4 5 0 0 9</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>", DisplayName = "the curve-bounded ring closes on the planar predicate")]
    [DataRow("<gml:exterior><gml:LinearRing><gml:posList srsDimension=\"3\">0 0 5 4 0 5 4 4 5 0 0 9</gml:posList></gml:LinearRing></gml:exterior>", DisplayName = "the linear ring closes on the planar predicate")]
    public void PlanarClosureAcceptsRingsOpenInTheThirdDimension(string body)
    {
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("Polygon", body), CoordinateReferenceSystem.Crs84, "POLYGON Z ((0 0 5, 4 0 5, 4 4 5, 0 0 9))");
    }

    /// <summary>An accepted curve ring closing on a negative-zero twin keeps the document's own spelling in the seam vertex — closure compares by value, the vertex run stays verbatim.</summary>
    [TestMethod]
    public void TheCurveRingSeamKeepsTheDocumentsOwnZeroSpelling()
    {
        string document = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:LineStringSegment><gml:posList>0 0 4 0 4 4 -0 0</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        using FlatGeometry polygon = GmlAssert.Accepts(document, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(polygon.Vertices[0].X), "the opening vertex carries the positive zero");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(polygon.Vertices[^1].X), "the closing vertex carries the document's negative zero, never a copy of the opening vertex");
    }

    /// <summary>Nothing follows the center-and-radius circle — not even a segment opening exactly at the east cardinal, which the join rule alone would admit.</summary>
    [TestMethod]
    public void NothingFollowsTheCenterRadiusCircle()
    {
        string follower = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint><gml:LineStringSegment><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(follower, GeometryCodecRefusalKind.StructuralViolation, "<gml:LineStringSegment>");

        string eastCardinalFollower = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint><gml:LineStringSegment><gml:posList>12 20 13 21</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(eastCardinalFollower, GeometryCodecRefusalKind.StructuralViolation, "<gml:LineStringSegment>");
    }

    /// <summary>The linear segment's two-position floor refuses at the segment's close: alone with one position, alone empty, empty between joined segments, and restating only the join.</summary>
    [TestMethod]
    public void LinearSegmentPositionFloorsRefuseAtTheSegmentClose()
    {
        string onePosition = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:pos>1 2</gml:pos></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(onePosition, GeometryCodecRefusalKind.StructuralViolation, "</gml:LineStringSegment>");

        string empty = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(empty, GeometryCodecRefusalKind.StructuralViolation, "</gml:LineStringSegment>");

        string emptyBetween = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc><gml:LineStringSegment></gml:LineStringSegment><gml:LineStringSegment><gml:posList>0 1 2 2</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(emptyBetween, GeometryCodecRefusalKind.StructuralViolation, "</gml:LineStringSegment>");

        string joinOnly = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc><gml:LineStringSegment><gml:pos>0 1</gml:pos></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(joinOnly, GeometryCodecRefusalKind.StructuralViolation, "</gml:LineStringSegment>");
    }

    /// <summary>The arc-count token: the canonical one is tolerated on a three-point arc, any other value refuses at the token, and the zero-padded spelling is not canonical.</summary>
    [TestMethod]
    public void TheArcCountTokenValidatesLexically()
    {
        string countedArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>-2 0 0 -1</gml:posList></gml:LineStringSegment><gml:Arc numArc=\"1\"><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>");
        using FlatGeometry counted = GmlAssert.Accepts(countedArc, CoordinateReferenceSystem.Crs84);

        using FlatGeometry bare = GmlAssert.Accepts(GmlTestDocuments.Root("Curve", GmlTestDocuments.JoinedCurveBody), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(bare, counted, "the canonical arc count changes nothing — the vertex run is the un-attributed reading's");

        string wrongCount = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc numArc=\"2\"><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(wrongCount, GeometryCodecRefusalKind.StructuralViolation, "2\"><gml:posList>");

        string paddedCount = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"01\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(paddedCount, GeometryCodecRefusalKind.StructuralViolation, "01");
    }

    /// <summary>The fixed interpolation tokens cross-checked: a linear token on an arc and a three-point token on the center-and-radius circle each refuse at the value.</summary>
    [TestMethod]
    public void WrongFixedInterpolationTokensRefusePerSegmentType()
    {
        string linearArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc interpolation=\"linear\"><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(linearArc, GeometryCodecRefusalKind.StructuralViolation, "linear");

        string threePointCircle = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint interpolation=\"circularArc3Points\" numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(threePointCircle, GeometryCodecRefusalKind.StructuralViolation, "circularArc3Points");
    }

    /// <summary>The sequence aggregation token on a ring is tolerated and reads identically to the attribute-less form.</summary>
    [TestMethod]
    public void TheSequenceAggregationTokenIsTolerated()
    {
        string declared = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring aggregationType=\"sequence\"><gml:curveMember><gml:Curve><gml:segments><gml:Circle><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Circle></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        using FlatGeometry withToken = GmlAssert.Accepts(declared, CoordinateReferenceSystem.Crs84);

        using FlatGeometry bare = GmlAssert.Accepts(GmlTestDocuments.Root("Polygon", GmlTestDocuments.CircleRingPolygonBody), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(bare, withToken, "the admitted aggregation token changes nothing");
    }

    /// <summary>The center-and-radius circle's value rules at the reader tier: a zero radius refuses at its token and a bare three-token center refuses at the carrier.</summary>
    [TestMethod]
    public void CenterRadiusValueRulesRefuse()
    {
        string zeroRadius = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">0</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(zeroRadius, GeometryCodecRefusalKind.StructuralViolation, "0</gml:radius>");

        string threeTokenCenter = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20 5</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(threeTokenCenter, GeometryCodecRefusalKind.DimensionMismatch, "<gml:pos>10 20 5");
    }

    /// <summary>A position list carrying two positions refuses where exactly one position is required — the circle center and the point alike, at the carrier's run terminator.</summary>
    [TestMethod]
    public void TheSinglePositionRuleRefusesMultiPositionLists()
    {
        string listCenter = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:posList>10 20 11 21</gml:posList><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(listCenter, GeometryCodecRefusalKind.StructuralViolation, "</gml:posList>");

        string listPoint = GmlTestDocuments.Root("Point", "<gml:posList>1 2 3 4</gml:posList>");
        GmlAssert.RefusesAt(listPoint, GeometryCodecRefusalKind.StructuralViolation, "</gml:posList>");
    }

    /// <summary>Circle degeneracies refuse at their control points exactly like the arc's.</summary>
    [TestMethod]
    public void CircleDegeneraciesRefuseAtTheirControlPoints()
    {
        string collinear = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Circle><gml:posList>0 0 1 1 2 2</gml:posList></gml:Circle></gml:segments>");
        GmlAssert.RefusesAt(collinear, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 1 1 2 2");

        string coincident = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Circle><gml:posList>0 0 0 0 2 2</gml:posList></gml:Circle></gml:segments>");
        GmlAssert.RefusesAt(coincident, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList>0 0 0 0 2 2");
    }

    /// <summary>The degeneracy anchor names the offending control point: a repeated second position anchors at the second carrier, not the first.</summary>
    [TestMethod]
    public void TheArcAnchorNamesTheOffendingControlPoint()
    {
        string repeatedSecond = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>0 0</gml:pos><gml:pos>0.0 0</gml:pos><gml:pos>2 2</gml:pos></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(repeatedSecond, GeometryCodecRefusalKind.StructuralViolation, "<gml:pos>0.0 0");
    }

    /// <summary>The certified kernel's magnitude and drift walls surface through the reader as unsupported geometry at the document anchors.</summary>
    [TestMethod]
    public void KernelWallsSurfaceThroughTheReader()
    {
        string hugeOrdinate = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>1e200 0 1 1 2 0</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(hugeOrdinate, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:posList>1e200");

        string offsetGrid = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>20000000 6000000</gml:pos><gml:radius uom=\"m\">2.4e-4</gml:radius></gml:CircleByCenterPoint></gml:segments>", GmlTestDocuments.WebMercator);
        GmlAssert.RefusesAt(offsetGrid, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:CircleByCenterPoint");
    }

    /// <summary>The third dimension reaches the circular segments through every curve-level arrival mode — carrier declaration, root inheritance, bare inference — and linearizes through the plane-embedded certified kernel, the control points entering the output verbatim in all three ordinates; the former degeneracy-leg payload rides as the genuinely non-collinear tilted-plane witness.</summary>
    [TestMethod]
    [DataRow("", "<gml:segments><gml:Circle><gml:posList srsDimension=\"3\">0 -1 0 1 0 0 0 1 0</gml:posList></gml:Circle></gml:segments>", 1025, 0.0, -1.0, 0.0, 0.0, -1.0, 0.0, DisplayName = "a carrier-declared three-dimensional circle linearizes and closes verbatim on its first control point")]
    [DataRow(" srsDimension=\"3\"", "<gml:segments><gml:Arc><gml:posList>0 -1 0 1 0 0 0 1 0</gml:posList></gml:Arc></gml:segments>", 513, 0.0, -1.0, 0.0, 0.0, 1.0, 0.0, DisplayName = "a root-inherited three-dimensional arc linearizes to its end control point verbatim")]
    [DataRow("", "<gml:segments><gml:Arc><gml:pos>0 -1 5</gml:pos><gml:pos>1 0 5</gml:pos><gml:pos>0 1 5</gml:pos></gml:Arc></gml:segments>", 513, 0.0, -1.0, 5.0, 0.0, 1.0, 5.0, DisplayName = "a bare three-token arc position infers the third dimension and linearizes")]
    [DataRow("", "<gml:segments><gml:Arc><gml:posList srsDimension=\"3\">0 0 0 1 1 1 2 0 0</gml:posList></gml:Arc></gml:segments>", 513, 0.0, 0.0, 0.0, 2.0, 0.0, 0.0, DisplayName = "the former degeneracy-leg payload linearizes in its tilted plane")]
    public void ThreeDimensionalArcsLinearizeThroughEveryArrivalMode(string rootAttributes, string body, int expectedCount, double openingX, double openingY, double openingZ, double closingX, double closingY, double closingZ)
    {
        string document = GmlTestDocuments.Root("Curve", body, GmlTestDocuments.Crs84, rootAttributes);
        using FlatGeometry linearized = GmlAssert.Accepts(document, CoordinateReferenceSystem.Crs84);
        int vertexCount = linearized.Vertices.Length;
        Assert.AreEqual(GeometryKind.LineString, linearized.Kind, "the circular curve materializes as a line string");
        Assert.AreEqual(expectedCount, vertexCount, "the certified linearization emits its derived subdivision, the seeds verbatim");
        Assert.AreEqual(new Point2d(openingX, openingY), linearized.Vertices[0], "the run opens on the first control point verbatim");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(openingZ), BitConverter.DoubleToInt64Bits(linearized.ZOrdinates[0]), "the opening vertex carries the first control point's third ordinate bit for bit");
        Assert.AreEqual(new Point2d(closingX, closingY), linearized.Vertices[^1], "the run closes on the closing control point verbatim");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(closingX), BitConverter.DoubleToInt64Bits(linearized.Vertices[^1].X), "the closing first ordinate keeps the document's own bits");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(closingZ), BitConverter.DoubleToInt64Bits(linearized.ZOrdinates[^1]), "the closing vertex carries the document's own third ordinate bit for bit");
    }

    /// <summary>The ancestor declaration reaches the segment from a non-root element: the nested curve's own third-dimension declaration, under a silent root, reaches the circle through the ring chain, and the arc-bounded ring closes bitwise in all three ordinates on the circle's verbatim seam.</summary>
    [TestMethod]
    public void TheAncestorDeclarationClosesTheArcBoundedRing()
    {
        string document = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:Ring><gml:curveMember><gml:Curve srsDimension=\"3\"><gml:segments><gml:Circle><gml:posList>0 -1 5 1 0 5 0 1 5</gml:posList></gml:Circle></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior>");
        using FlatGeometry polygon = GmlAssert.Accepts(document, CoordinateReferenceSystem.Crs84);
        int vertexCount = polygon.Vertices.Length;
        Assert.AreEqual(GeometryKind.Polygon, polygon.Kind, "the curve-bounded ring materializes as a polygon");
        Assert.AreEqual(1025, vertexCount, "the certified circle linearizes its three seed gaps at the published bound");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(polygon.Vertices[0].X), BitConverter.DoubleToInt64Bits(polygon.Vertices[^1].X), "the ring closes bitwise on the first ordinate");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(polygon.Vertices[0].Y), BitConverter.DoubleToInt64Bits(polygon.Vertices[^1].Y), "the ring closes bitwise on the second ordinate");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(polygon.ZOrdinates[0]), BitConverter.DoubleToInt64Bits(polygon.ZOrdinates[^1]), "the ring closes bitwise on the third ordinate");
        Assert.AreEqual(5.0, polygon.ZOrdinates[0], "the ancestor-declared third ordinate rides the ring");
    }

    /// <summary>Two three-dimensional arcs join arc-to-arc through the one per-parse scratch carrier: the first arc writes its closing altitude to the seam, the second consumes it, and both linearize on the reused carrier.</summary>
    [TestMethod]
    public void ThreeDimensionalArcsJoinArcToArcOnOneCarrier()
    {
        string document = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList srsDimension=\"3\">0 -1 5 1 0 5 0 1 5</gml:posList></gml:Arc><gml:Arc><gml:posList srsDimension=\"3\">0 1 5 -1 0 5 0 -1 5</gml:posList></gml:Arc></gml:segments>");
        using FlatGeometry joined = GmlAssert.Accepts(document, CoordinateReferenceSystem.Crs84);
        int vertexCount = joined.Vertices.Length;
        Assert.AreEqual(1025, vertexCount, "two half-open half-circle linearizations, the joined vertex once");
        Assert.AreEqual(new Point2d(0.0, 1.0), joined.Vertices[512], "the seam vertex is the earlier arc's closing control point");
        Assert.AreEqual(5.0, joined.ZOrdinates[512], "the seam carries the shared third ordinate");
        Assert.AreEqual(new Point2d(0.0, -1.0), joined.Vertices[^1], "the run closes at the second arc's end control point verbatim");
    }

    /// <summary>Three-dimensional segments of both kinds join at the shared position over every effective ordinate, the joined vertex emitted once with the earlier segment's ordinates.</summary>
    [TestMethod]
    public void ThreeDimensionalCurvesJoinAcrossSegmentKinds()
    {
        string document = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList srsDimension=\"3\">-2 0 5 0 -1 5</gml:posList></gml:LineStringSegment><gml:Arc><gml:posList srsDimension=\"3\">0 -1 5 1 0 5 0 1 5</gml:posList></gml:Arc></gml:segments>");
        using FlatGeometry joined = GmlAssert.Accepts(document, CoordinateReferenceSystem.Crs84);
        int vertexCount = joined.Vertices.Length;
        Assert.AreEqual(514, vertexCount, "two linear vertices plus the half-open arc linearization, the joined vertex once");
        Assert.AreEqual(new Point2d(-2.0, 0.0), joined.Vertices[0], "the run opens at the first segment's first position");
        Assert.AreEqual(new Point2d(0.0, -1.0), joined.Vertices[1], "the joined vertex is the earlier segment's copy");
        Assert.AreEqual(5.0, joined.ZOrdinates[1], "the joined vertex carries the shared third ordinate");
        Assert.AreEqual(new Point2d(0.0, 1.0), joined.Vertices[^1], "the run closes at the arc's end control point verbatim");
        Assert.AreEqual(5.0, joined.ZOrdinates[^1], "the closing vertex carries the arc's own third ordinate");
    }

    /// <summary>A seam differing only in its third ordinate refuses at the joining carrier in both segment orders: the arc consuming the linear segment's altitude, and the linear segment consuming the arc's written one.</summary>
    [TestMethod]
    public void TheThreeDimensionalJoinRefusesOnTheThirdOrdinateInBothOrders()
    {
        string arcAfterLinear = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList srsDimension=\"3\">-2 0 5 0 -1 5</gml:posList></gml:LineStringSegment><gml:Arc><gml:posList srsDimension=\"3\">0 -1 9 1 0 9 0 1 9</gml:posList></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(arcAfterLinear, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList srsDimension=\"3\">0 -1 9");

        string linearAfterArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList srsDimension=\"3\">0 -1 5 1 0 5 0 1 5</gml:posList></gml:Arc><gml:LineStringSegment><gml:posList srsDimension=\"3\">0 1 9 2 2 9</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(linearAfterArc, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList srsDimension=\"3\">0 1 9");
    }

    /// <summary>The arc join keeps the earlier copy verbatim in the third ordinate: a negative-zero altitude seam joins on value equality and the seam vertex keeps the earlier spelling's sign bit.</summary>
    [TestMethod]
    public void TheArcJoinKeepsTheEarlierCopysThirdOrdinateBits()
    {
        string negativeZeroSeam = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList srsDimension=\"3\">-2 0 -0 0 -1 -0</gml:posList></gml:LineStringSegment><gml:Arc><gml:posList srsDimension=\"3\">0 -1 0 1 0 0 0 1 0</gml:posList></gml:Arc></gml:segments>");
        using FlatGeometry joined = GmlAssert.Accepts(negativeZeroSeam, CoordinateReferenceSystem.Crs84);
        int vertexCount = joined.Vertices.Length;
        Assert.AreEqual(514, vertexCount, "the value-equal seam joins once");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(joined.ZOrdinates[1]), "the joined vertex keeps the earlier segment's negative-zero third ordinate");
    }

    /// <summary>Dimension disagreements at a circular segment refuse through the linear lane's own machinery at its anchors: a carrier declaration disagreeing with the ancestor at its value byte, and a bare inference disagreeing with the running collection where its token run ends.</summary>
    [TestMethod]
    public void CircularDimensionDisagreementsRefuseAtTheirAnchors()
    {
        string declaredAgainstAncestor = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList srsDimension=\"2\">0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>", GmlTestDocuments.Crs84, " srsDimension=\"3\"");
        GmlAssert.RefusesAt(declaredAgainstAncestor, GeometryCodecRefusalKind.DimensionMismatch, "2\">0 -1");

        string bareAgainstRunning = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>0 -1 5</gml:pos><gml:pos>1 0</gml:pos><gml:pos>0 1 5</gml:pos></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(bareAgainstRunning, GeometryCodecRefusalKind.DimensionMismatch, "</gml:pos><gml:pos>0 1 5");
    }

    /// <summary>The three-dimensional kernel's refusals surface through the reader's exhaustive mapping: degeneracies structurally at their control points, and walls and planarity as unsupported at the named seed's carrier or the segment. The planar-drift document is the kernel tranche's own center-gate exemplar (<see cref="CircularArcLinearization3dTests"/>), its decimal ordinates parsing bit-exactly to the pinned seeds — that pairing is this row's representability record.</summary>
    [TestMethod]
    public void ThreeDimensionalKernelRefusalsSurfaceThroughTheReader()
    {
        string collinear = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>0 0 5</gml:pos><gml:pos>1 1 5</gml:pos><gml:pos>2 2 5</gml:pos></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(collinear, GeometryCodecRefusalKind.StructuralViolation, "<gml:pos>2 2 5");

        string coincident = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>4 4 5</gml:pos><gml:pos>4.0 4 5</gml:pos><gml:pos>2 2 5</gml:pos></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(coincident, GeometryCodecRefusalKind.StructuralViolation, "<gml:pos>4.0 4 5");

        string altitudeWall = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>0 -1 10000000000000000000000000000000000000000000000</gml:pos><gml:pos>1 0 0</gml:pos><gml:pos>0 1 0</gml:pos></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(altitudeWall, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:pos>0 -1 1");

        string planarDrift = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>0.0078125 0 134217728</gml:pos><gml:pos>0 0.0078125 134217728</gml:pos><gml:pos>-0.0078125 0 134217728.0000000298023223876953125</gml:pos></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(planarDrift, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:Arc>");
    }

    /// <summary>The center-and-radius circle stays two-dimensional: the schema pins the representation, and its third-dimension arms refuse exactly as before the three-point lift.</summary>
    [TestMethod]
    [DataRow(" srsDimension=\"3\"", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>", "><gml:pos>10 20", DisplayName = "the center-and-radius circle under a three-dimensional root refuses at its start-tag close")]
    [DataRow("", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos srsDimension=\"3\">10 20 5</gml:pos><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>", "<gml:pos srsDimension=\"3\">10 20 5", DisplayName = "a carrier-declared three-dimensional center refuses at the carrier")]
    public void TheCenterRadiusCircleStaysTwoDimensional(string rootAttributes, string body, string offendingMarker)
    {
        string document = GmlTestDocuments.Root("Curve", body, GmlTestDocuments.Crs84, rootAttributes);
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.DimensionMismatch, offendingMarker);
    }

    /// <summary>Wrong-category member children and wrong member properties refuse structurally at their opening brackets.</summary>
    [TestMethod]
    public void MemberCategoryViolationsRefuseAtTheirBrackets()
    {
        string lineUnderPointMember = GmlTestDocuments.Root("MultiPoint", "<gml:pointMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:pointMember>");
        GmlAssert.RefusesAt(lineUnderPointMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:LineString>");

        string pointUnderSurfaceMember = GmlTestDocuments.Root("MultiSurface", "<gml:surfaceMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:surfaceMember>");
        GmlAssert.RefusesAt(pointUnderSurfaceMember, GeometryCodecRefusalKind.StructuralViolation, "<gml:Point>");

        string curveMemberInMultiPoint = GmlTestDocuments.Root("MultiPoint", "<gml:curveMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:curveMember>");
        GmlAssert.RefusesAt(curveMemberInMultiPoint, GeometryCodecRefusalKind.StructuralViolation, "<gml:curveMember>");

        string pointMemberInCollection = GmlTestDocuments.Root("MultiGeometry", "<gml:pointMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:pointMember>");
        GmlAssert.RefusesAt(pointMemberInCollection, GeometryCodecRefusalKind.StructuralViolation, "<gml:pointMember>");
    }

    /// <summary>One heterogeneous collection carries every value the roster admits as members.</summary>
    [TestMethod]
    public void TheFullRosterCollectionCarriesAllEightValues()
    {
        string body = "<gml:geometryMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember>"
            + "<gml:geometryMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:geometryMember>"
            + "<gml:geometryMember><gml:Curve>" + GmlTestDocuments.LinearCurveBody + "</gml:Curve></gml:geometryMember>"
            + "<gml:geometryMember><gml:Polygon>" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon></gml:geometryMember>"
            + "<gml:geometryMember><gml:Surface>" + GmlTestDocuments.OnePatchSurfaceBody + "</gml:Surface></gml:geometryMember>"
            + "<gml:geometryMember><gml:MultiPoint><gml:pointMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:pointMember></gml:MultiPoint></gml:geometryMember>"
            + "<gml:geometryMember><gml:MultiCurve><gml:curveMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:curveMember></gml:MultiCurve></gml:geometryMember>"
            + "<gml:geometryMember><gml:MultiSurface><gml:surfaceMember><gml:Polygon>" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon></gml:surfaceMember></gml:MultiSurface></gml:geometryMember>";
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("MultiGeometry", body), CoordinateReferenceSystem.Crs84, "GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1), LINESTRING (0 0, 1 1, 2 0), POLYGON ((0 0, 4 0, 4 4, 0 0)), POLYGON ((0 0, 4 0, 4 4, 0 0)), MULTIPOINT ((1 2)), MULTILINESTRING ((0 0, 1 1)), MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0))))");
    }

    /// <summary>The plural member containers read exactly like their singular twins, and a collection may mix a singular and a plural property.</summary>
    [TestMethod]
    [DataRow("MultiCurve", "<gml:curveMembers><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString><gml:LineString><gml:posList>2 2 3 3</gml:posList></gml:LineString></gml:curveMembers>", "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", DisplayName = "the plural curve member container aggregates")]
    [DataRow("MultiSurface", "<gml:surfaceMembers><gml:Polygon>" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon><gml:Polygon><gml:exterior><gml:LinearRing><gml:posList>10 10 14 10 14 14 10 10</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></gml:surfaceMembers>", "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))", DisplayName = "the plural surface member container aggregates")]
    [DataRow("MultiGeometry", "<gml:geometryMembers><gml:Point><gml:pos>1 2</gml:pos></gml:Point><gml:Point><gml:pos>3 4</gml:pos></gml:Point></gml:geometryMembers>", "GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))", DisplayName = "the plural geometry member container aggregates")]
    [DataRow("MultiGeometry", "<gml:geometryMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember><gml:geometryMembers><gml:Point><gml:pos>3 4</gml:pos></gml:Point></gml:geometryMembers>", "GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))", DisplayName = "a singular and a plural property mix in one collection")]
    public void PluralMemberContainersReadLikeTheirSingularTwins(string localName, string body, string wkt)
    {
        GmlAssert.MatchesWkt(GmlTestDocuments.Root(localName, body), CoordinateReferenceSystem.Crs84, wkt);
    }

    /// <summary>Typed empties read as collection members: the zero-segment curve and the patch-less surface keep their kinds inside the heterogeneous collection.</summary>
    [TestMethod]
    public void TypedEmptyMembersReadInsideTheCollection()
    {
        string body = "<gml:geometryMember><gml:Curve><gml:segments></gml:segments></gml:Curve></gml:geometryMember><gml:geometryMember><gml:Surface><gml:patches></gml:patches></gml:Surface></gml:geometryMember>";
        GmlAssert.MatchesWkt(GmlTestDocuments.Root("MultiGeometry", body), CoordinateReferenceSystem.Crs84, "GEOMETRYCOLLECTION (LINESTRING EMPTY, POLYGON EMPTY)");
    }

    /// <summary>A legacy-namespace member child refuses as unsupported at its opening bracket, under the heterogeneous and the typed member alike.</summary>
    [TestMethod]
    public void LegacyNamespaceMembersRefuseAsUnsupported()
    {
        string underCollection = GmlTestDocuments.Root("MultiGeometry", "<gml:geometryMember><old:Point xmlns:old=\"http://www.opengis.net/gml\"><old:pos>1 2</old:pos></old:Point></gml:geometryMember>");
        GmlAssert.RefusesAt(underCollection, GeometryCodecRefusalKind.UnsupportedGeometry, "<old:Point");

        string underPointMember = GmlTestDocuments.Root("MultiPoint", "<gml:pointMember><old:Point xmlns:old=\"http://www.opengis.net/gml\"><old:pos>1 2</old:pos></old:Point></gml:pointMember>");
        GmlAssert.RefusesAt(underPointMember, GeometryCodecRefusalKind.UnsupportedGeometry, "<old:Point");
    }

    /// <summary>A remote reference refuses as prohibited on the typed point member and on the exterior boundary property.</summary>
    [TestMethod]
    public void RemoteReferencesRefuseOnTypedMembersAndBoundaries()
    {
        string memberReference = GmlTestDocuments.Root("MultiPoint", "<gml:pointMember xlink:href=\"#p\"></gml:pointMember>", GmlTestDocuments.Crs84, " xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
        GmlAssert.RefusesAt(memberReference, GeometryCodecRefusalKind.ProhibitedConstruct, "xlink:href");

        string boundaryReference = GmlTestDocuments.Root("Polygon", "<gml:exterior xlink:href=\"#r\"></gml:exterior>", GmlTestDocuments.Crs84, " xmlns:xlink=\"http://www.w3.org/1999/xlink\"");
        GmlAssert.RefusesAt(boundaryReference, GeometryCodecRefusalKind.ProhibitedConstruct, "xlink:href");
    }

    /// <summary>A point without its position refuses where the element ends — at the root and as a member alike.</summary>
    [TestMethod]
    public void TheChildlessPointRefusesAtItsClose()
    {
        string atRoot = GmlTestDocuments.Root("Point", "");
        GmlAssert.RefusesAt(atRoot, GeometryCodecRefusalKind.StructuralViolation, "</gml:Point>");

        string asMember = GmlTestDocuments.Root("MultiPoint", "<gml:pointMember><gml:Point></gml:Point></gml:pointMember>");
        GmlAssert.RefusesAt(asMember, GeometryCodecRefusalKind.StructuralViolation, "</gml:Point>");
    }

    /// <summary>The deliberately declined carriers refuse as unsupported wherever a position is expected: inside an arc, inside a linear segment, and inside a line string.</summary>
    [TestMethod]
    public void DeclinedCarriersRefuseInEveryPositionContext()
    {
        string coordinatesInArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:coordinates>0,0 1,1 2,0</gml:coordinates></gml:Arc></gml:segments>");
        GmlAssert.RefusesAt(coordinatesInArc, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:coordinates>");

        string pointPropertyInSegment = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:pointProperty></gml:pointProperty></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(pointPropertyInSegment, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:pointProperty>");

        string pointRepInLineString = GmlTestDocuments.Root("LineString", "<gml:pointRep></gml:pointRep>");
        GmlAssert.RefusesAt(pointRepInLineString, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:pointRep>");
    }

    /// <summary>The legacy boundary property inside a polygon refuses at its element: the boundary walk admits only the two profile properties, so the offense is structural before any vocabulary roster is consulted.</summary>
    [TestMethod]
    public void TheLegacyBoundaryPropertyRefusesInsideThePolygon()
    {
        string document = GmlTestDocuments.Root("Polygon", "<gml:outerBoundaryIs><gml:LinearRing><gml:posList>0 0 4 0 4 4 0 0</gml:posList></gml:LinearRing></gml:outerBoundaryIs>");
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.StructuralViolation, "<gml:outerBoundaryIs>");
    }

    /// <summary>The metadata property inside a point is not a position carrier and refuses structurally at its opening bracket.</summary>
    [TestMethod]
    public void TheMetadataPropertyRefusesInsideThePoint()
    {
        string document = GmlTestDocuments.Root("Point", "<gml:metaDataProperty></gml:metaDataProperty><gml:pos>1 2</gml:pos>");
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.StructuralViolation, "<gml:metaDataProperty>");
    }

    /// <summary>A namespace-less root and a foreign-schema root refuse as unsupported at the first byte.</summary>
    [TestMethod]
    public void NamespacelessAndForeignSchemaRootsRefuse()
    {
        string namespaceless = $"<Point srsName=\"{GmlTestDocuments.Crs84}\"><pos>1 2</pos></Point>";
        GmlAssert.RefusesAt(namespaceless, GeometryCodecRefusalKind.UnsupportedGeometry, "<Point");

        string schemaRoot = "<xsd:schema xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"/>";
        GmlAssert.RefusesAt(schemaRoot, GeometryCodecRefusalKind.UnsupportedGeometry, "<xsd:schema");
    }

    /// <summary>The nested-spelling rule holds on every kind: the byte-for-byte repeat accepts, any other roster spelling of the same system refuses at the nested value.</summary>
    [TestMethod]
    public void NestedDeclarationsRepeatTheRootSpellingOnEveryKind()
    {
        string urnRepeated = GmlTestDocuments.Root("MultiGeometry", "<gml:geometryMember><gml:Point srsName=\"urn:ogc:def:crs:EPSG::4326\"><gml:pos>60.17 24.94</gml:pos></gml:Point></gml:geometryMember>", "urn:ogc:def:crs:EPSG::4326");
        using FlatGeometry accepted = GmlAssert.Accepts(urnRepeated, CoordinateReferenceSystem.Epsg4326);
        Assert.AreEqual(GeometryKind.GeometryCollection, accepted.Kind, "the byte-for-byte urn repeat is tolerated");

        string iriUnderUrn = GmlTestDocuments.Root("MultiGeometry", $"<gml:geometryMember><gml:Point srsName=\"{GmlTestDocuments.Epsg4326}\"><gml:pos>60.17 24.94</gml:pos></gml:Point></gml:geometryMember>", "urn:ogc:def:crs:EPSG::4326");
        GmlAssert.RefusesAt(iriUnderUrn, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, GmlTestDocuments.Epsg4326);

        string urnOnLineMember = GmlTestDocuments.Root("MultiCurve", "<gml:curveMember><gml:LineString srsName=\"urn:ogc:def:crs:OGC:1.3:CRS84\"><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:curveMember>");
        GmlAssert.RefusesAt(urnOnLineMember, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "urn:ogc:def:crs:OGC:1.3:CRS84");

        string canonicalOnSurfaceMember = GmlTestDocuments.Root("MultiSurface", $"<gml:surfaceMember><gml:Polygon srsName=\"{GmlTestDocuments.Crs84}\">" + GmlTestDocuments.SquarePolygonBody + "</gml:Polygon></gml:surfaceMember>");
        using FlatGeometry surface = GmlAssert.Accepts(canonicalOnSurfaceMember, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.MultiPolygon, surface.Kind, "the canonical repeat on a surface member is tolerated");
    }

    /// <summary>The carrier-level declaration must repeat the root spelling byte for byte: a same-system urn refuses at the carrier value, the byte-for-byte repeat accepts.</summary>
    [TestMethod]
    public void CarrierDeclarationsRepeatTheRootSpelling()
    {
        string urnOnCarrier = GmlTestDocuments.Root("Point", "<gml:pos srsName=\"urn:ogc:def:crs:OGC:1.3:CRS84\">1 2</gml:pos>");
        GmlAssert.RefusesAt(urnOnCarrier, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "urn:ogc:def:crs:OGC:1.3:CRS84");

        string repeatedOnCarrier = GmlTestDocuments.Root("Point", $"<gml:pos srsName=\"{GmlTestDocuments.Crs84}\">1 2</gml:pos>");
        using FlatGeometry accepted = GmlAssert.Accepts(repeatedOnCarrier, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(new Point2d(1.0, 2.0), accepted.Vertices[0], "the byte-for-byte carrier repeat is tolerated");
    }

    /// <summary>Near-miss system spellings refuse at the value — recognition never folds case and never trims.</summary>
    [TestMethod]
    [DataRow("HTTP://www.opengis.net/def/crs/OGC/1.3/CRS84", DisplayName = "the upper-cased scheme is not the canonical spelling")]
    [DataRow("http://www.opengis.net/def/crs/OGC/1.3/CRS84 ", DisplayName = "the trailing space is not the canonical spelling")]
    public void NearMissSystemSpellingsRefuse(string spelling)
    {
        string document = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody, spelling);
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, spelling);
    }

    /// <summary>A character-reference spelling of the canonical system decodes before recognition.</summary>
    [TestMethod]
    public void CharacterReferenceSpellingsDecodeBeforeRecognition()
    {
        string document = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody, "http://www.opengis.net/def/crs/OGC/1.3/CRS8&#52;");
        using FlatGeometry geometry = GmlAssert.Accepts(document, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(new Point2d(1.0, 2.0), geometry.Vertices[0], "the decoded spelling recognizes and the value reads");
    }

    /// <summary>The required root declaration is enforced on every root kind, refusing at the start-tag close.</summary>
    [TestMethod]
    public void UndeclaredRootsRefuseBeyondThePoint()
    {
        string collection = GmlTestDocuments.RootWithoutSystem("MultiGeometry", "<gml:geometryMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember>");
        GmlAssert.RefusesAt(collection, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "><gml:geometryMember>");

        string curve = GmlTestDocuments.RootWithoutSystem("Curve", GmlTestDocuments.LinearCurveBody);
        GmlAssert.RefusesAt(curve, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "><gml:segments>");
    }

    /// <summary>The dimension token must be the integer two or three and the count token must be canonical: the out-of-range, the fractional, and the padded spellings refuse at their values.</summary>
    [TestMethod]
    public void DimensionAndCountLexicalRulesRefuse()
    {
        string dimensionOne = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody, GmlTestDocuments.Crs84, " srsDimension=\"1\"");
        GmlAssert.RefusesAt(dimensionOne, GeometryCodecRefusalKind.DimensionMismatch, "1\"><gml:pos>");

        string fractionalDimension = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody, GmlTestDocuments.Crs84, " srsDimension=\"2.5\"");
        GmlAssert.RefusesAt(fractionalDimension, GeometryCodecRefusalKind.DimensionMismatch, "2.5");

        string paddedCount = GmlTestDocuments.Root("LineString", "<gml:posList srsDimension=\"2\" count=\"2 \">0 0 1 1</gml:posList>");
        GmlAssert.RefusesAt(paddedCount, GeometryCodecRefusalKind.StructuralViolation, "2 \">0 0");
    }

    /// <summary>A bare four-token position refuses as a dimension mismatch at the run terminator.</summary>
    [TestMethod]
    public void TheBareFourTokenPositionRefusesAtTheRunTerminator()
    {
        string document = GmlTestDocuments.Root("Point", "<gml:pos>1 2 3 4</gml:pos>");
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.DimensionMismatch, "</gml:pos>");
    }

    /// <summary>The root dimension declaration walks into every kind: the curve, the polygon, and the line string all read their third ordinates.</summary>
    [TestMethod]
    [DataRow("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>0 0 5 1 1 5</gml:posList></gml:LineStringSegment></gml:segments>", "LINESTRING Z (0 0 5, 1 1 5)", DisplayName = "the curve reads the root's third dimension")]
    [DataRow("Polygon", "<gml:exterior><gml:LinearRing><gml:posList>0 0 5 4 0 5 4 4 5 0 0 5</gml:posList></gml:LinearRing></gml:exterior>", "POLYGON Z ((0 0 5, 4 0 5, 4 4 5, 0 0 5))", DisplayName = "the polygon reads the root's third dimension")]
    [DataRow("LineString", "<gml:posList>0 0 5 1 1 5</gml:posList>", "LINESTRING Z (0 0 5, 1 1 5)", DisplayName = "the line string reads the root's third dimension")]
    public void TheDimensionWalkExtendsBeyondThePoint(string localName, string body, string wkt)
    {
        string document = GmlTestDocuments.Root(localName, body, GmlTestDocuments.Crs84, " srsDimension=\"3\"");
        GmlAssert.MatchesWkt(document, CoordinateReferenceSystem.Crs84, wkt);
    }

    /// <summary>Mixed arity along one geometry refuses at the disagreeing declaration: a third dimension entering at a later segment or an interior ring.</summary>
    [TestMethod]
    public void MixedArityRefusesAtTheDisagreeingDeclaration()
    {
        string secondSegment = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList srsDimension=\"3\">1 1 5 2 2 5</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(secondSegment, GeometryCodecRefusalKind.DimensionMismatch, "3\">1 1 5");

        string interiorRing = GmlTestDocuments.Root("Polygon", GmlTestDocuments.SquarePolygonBody + "<gml:interior><gml:LinearRing><gml:posList srsDimension=\"3\">1 1 5 2 1 5 2 2 5 1 1 5</gml:posList></gml:LinearRing></gml:interior>");
        GmlAssert.RefusesAt(interiorRing, GeometryCodecRefusalKind.DimensionMismatch, "3\">1 1 5");
    }

    /// <summary>A counted position list whose count agrees accepts and reads exactly like the reference.</summary>
    [TestMethod]
    public void TheAgreeingCountedListAccepts()
    {
        string document = GmlTestDocuments.Root("LineString", "<gml:posList srsDimension=\"2\" count=\"2\">0 0 1 1</gml:posList>");
        GmlAssert.MatchesWkt(document, CoordinateReferenceSystem.Crs84, "LINESTRING (0 0, 1 1)");
    }

    /// <summary>Under the latitude-longitude system all three ordinates ride as written — the axis-order fact extended to the third dimension.</summary>
    [TestMethod]
    public void ThreeOrdinatesRideAsWrittenUnderTheLatitudeLongitudeSystem()
    {
        string document = GmlTestDocuments.Root("Point", "<gml:pos>60.17 24.94 12.5</gml:pos>", GmlTestDocuments.Epsg4326);
        GmlAssert.MatchesWkt(document, CoordinateReferenceSystem.Epsg4326, "POINT Z (60.17 24.94 12.5)");
    }

    /// <summary>The character overload accepts a canonical document and reports the declared system.</summary>
    [TestMethod]
    public void TheCharacterOverloadAcceptsAndReportsTheSystem()
    {
        string document = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody);
        bool accepted = GmlGeometryReader.TryRead(document.AsSpan(), out FlatGeometry geometry, out CoordinateReferenceSystem system, out GeometryCodecRefusal refusal);

        using(geometry)
        {
            Assert.IsTrue(accepted, "the canonical document must be accepted through the character overload");
            Assert.AreEqual(GeometryCodecRefusal.None, refusal, "an accepted read reports the no-offense sentinel");
            Assert.AreEqual(CoordinateReferenceSystem.Crs84, system, "the returned system equals the declared one");
            Assert.AreEqual(GeometryKind.Point, geometry.Kind, "the value materializes");
        }
    }

    /// <summary>Repeated single positions read everywhere the list does: the linear ring and the arc control points widen identically.</summary>
    [TestMethod]
    public void RepeatedPositionsWidenLikeTheirLists()
    {
        string repeatedRing = GmlTestDocuments.Root("Polygon", "<gml:exterior><gml:LinearRing><gml:pos>0 0</gml:pos><gml:pos>4 0</gml:pos><gml:pos>4 4</gml:pos><gml:pos>0 0</gml:pos></gml:LinearRing></gml:exterior>");
        using FlatGeometry fromRepeatedRing = GmlAssert.Accepts(repeatedRing, CoordinateReferenceSystem.Crs84);

        using FlatGeometry fromRingList = GmlAssert.Accepts(GmlTestDocuments.Root("Polygon", GmlTestDocuments.SquarePolygonBody), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(fromRingList, fromRepeatedRing, "the repeated positions equal the list reading");

        string repeatedArc = GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:pos>0 -1</gml:pos><gml:pos>1 0</gml:pos><gml:pos>0 1</gml:pos></gml:Arc></gml:segments>");
        using FlatGeometry fromRepeatedArc = GmlAssert.Accepts(repeatedArc, CoordinateReferenceSystem.Crs84);

        using FlatGeometry fromArcList = GmlAssert.Accepts(GmlTestDocuments.Root("Curve", "<gml:segments><gml:Arc><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc></gml:segments>"), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(fromArcList, fromRepeatedArc, "the repeated arc control points equal the list reading");
    }

    /// <summary>The line string's own two-position floor refuses at the run terminator, with one position and with none.</summary>
    [TestMethod]
    public void LineStringFloorsRefuseAtTheRunTerminator()
    {
        string onePosition = GmlTestDocuments.Root("LineString", "<gml:pos>1 2</gml:pos>");
        GmlAssert.RefusesAt(onePosition, GeometryCodecRefusalKind.StructuralViolation, "</gml:LineString>");

        string empty = GmlTestDocuments.Root("LineString", "");
        GmlAssert.RefusesAt(empty, GeometryCodecRefusalKind.StructuralViolation, "</gml:LineString>");
    }

    /// <summary>A raw control byte inside position text refuses as malformed at its own byte through the public reader.</summary>
    [TestMethod]
    public void ARawControlByteRefusesAtItsOwnByte()
    {
        string document = GmlTestDocuments.Root("Point", "<gml:pos>1 \u00012</gml:pos>");
        GmlAssert.RefusesAt(document, GeometryCodecRefusalKind.MalformedDocument, "\u0001");
    }

    /// <summary>Token offenses inside a joining position beat the join rule: the position is no operand until its tokens are values.</summary>
    [TestMethod]
    public void TokenOffensesBeatTheJoinRule()
    {
        string nanInJoin = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList>NaN 9 2 2</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(nanInJoin, GeometryCodecRefusalKind.NonFiniteCoordinate, "NaN");

        string nanBeforeBreak = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>0 0 1 1</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList>1 1 NaN 3</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList>9 9 4 4</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(nanBeforeBreak, GeometryCodecRefusalKind.NonFiniteCoordinate, "NaN");
    }

    /// <summary>Axis and unit label attributes are tolerated and ignored on the root and on a carrier.</summary>
    [TestMethod]
    public void LabelAttributesAreToleratedAndIgnored()
    {
        string labeled = GmlTestDocuments.Root("Point", "<gml:pos axisLabels=\"Lon Lat\" uomLabels=\"deg deg\">1 2</gml:pos>", GmlTestDocuments.Crs84, " axisLabels=\"Lon Lat\" uomLabels=\"deg deg\"");
        using FlatGeometry withLabels = GmlAssert.Accepts(labeled, CoordinateReferenceSystem.Crs84);

        using FlatGeometry bare = GmlAssert.Accepts(GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(bare, withLabels, "the labels change nothing");
    }

    /// <summary>A defect after the geometry value completes still rents nothing — materialization is deferred past the whole document.</summary>
    [TestMethod]
    public void ARefusalAfterACleanGeometryRentsNothing()
    {
        string document = GmlTestDocuments.Root("Point", GmlTestDocuments.PointBody) + "junk";
        CountingAllocatorSpy spy = new();
        FlatGeometryAllocators allocators = new(spy.RentVertexColumn, spy.RentOrdinateColumn);
        bool accepted = GmlGeometryReader.TryRead(Encoding.UTF8.GetBytes(document), allocators, out _, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the trailing content must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.TrailingContent, refusal.Kind, "the post-value defect is the refusal");
        Assert.AreEqual(0, spy.RentalCount, "a refused read rents nothing");
    }

    /// <summary>Thirty-one collection wrappers around a curve-ring surface leaf accept — the geometry bound and the transport bound carry the worst-cost chain together.</summary>
    [TestMethod]
    public void TheDepthBoundsCarryTheWorstCostLeafInLockstep()
    {
        string leaf = "<gml:Surface><gml:patches><gml:PolygonPatch><gml:exterior><gml:Ring><gml:curveMember><gml:Curve><gml:segments><gml:Arc><gml:posList>0 -1 1 0 0 1</gml:posList></gml:Arc><gml:LineStringSegment><gml:posList>0 1 0 -1</gml:posList></gml:LineStringSegment></gml:segments></gml:Curve></gml:curveMember></gml:Ring></gml:exterior></gml:PolygonPatch></gml:patches></gml:Surface>";
        using FlatGeometry deep = GmlAssert.Accepts(GmlTestDocuments.NestedCollectionsAround(31, leaf), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.GeometryCollection, deep.Kind, "the wrapped leaf sits exactly at the geometry bound");
    }

    /// <summary>The adjudication order at the root: the system gate precedes dispatch, so an out-of-roster declaration wins over the envelope's unsupported kind and an absent declaration wins over refused vocabulary.</summary>
    [TestMethod]
    public void TheSystemGatePrecedesRootDispatch()
    {
        string envelopeUnderRoster = GmlTestDocuments.Root("Envelope", "<gml:lowerCorner>0 0</gml:lowerCorner><gml:upperCorner>1 1</gml:upperCorner>");
        GmlAssert.RefusesAt(envelopeUnderRoster, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:Envelope");

        string envelopeOutOfRoster = GmlTestDocuments.Root("Envelope", "<gml:lowerCorner>0 0</gml:lowerCorner><gml:upperCorner>1 1</gml:upperCorner>", "http://www.opengis.net/def/crs/EPSG/0/25833");
        GmlAssert.RefusesAt(envelopeOutOfRoster, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "http://www.opengis.net/def/crs/EPSG/0/25833");

        string undeclaredComposite = GmlTestDocuments.RootWithoutSystem("CompositeCurve", "");
        GmlAssert.RefusesAt(undeclaredComposite, GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, "></gml:CompositeCurve>");
    }

    /// <summary>The four long-form unit spellings accept under their systems, the compact symbols' roster twins — the profile's own example accepts once its unit matches its system.</summary>
    [TestMethod]
    public void TheUnitRosterSpellingsAcceptUnderTheirSystems()
    {
        string degreeUrn = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"urn:ogc:def:uom:EPSG::9102\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        using FlatGeometry viaUrn = GmlAssert.Accepts(degreeUrn, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.LineString, viaUrn.Kind, "the degree urn reads under the degree system");

        string degreeIri = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"http://www.opengis.net/def/uom/EPSG/0/9102\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        using FlatGeometry viaIri = GmlAssert.Accepts(degreeIri, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(GeometryKind.LineString, viaIri.Kind, "the degree reference reads under the degree system");

        string metreUrn = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>10 20</gml:pos><gml:radius uom=\"urn:ogc:def:uom:EPSG::9001\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>", GmlTestDocuments.WebMercator);
        using FlatGeometry viaMetreUrn = GmlAssert.Accepts(metreUrn, CoordinateReferenceSystem.WebMercator);
        Assert.AreEqual(GeometryKind.LineString, viaMetreUrn.Kind, "the metre urn reads under the metre system");

        string correctedExample = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pos>51.389 30.099</gml:pos><gml:radius uom=\"deg\">0.18</gml:radius></gml:CircleByCenterPoint></gml:segments>", GmlTestDocuments.Epsg4326);
        using FlatGeometry corrected = GmlAssert.Accepts(correctedExample, CoordinateReferenceSystem.Epsg4326);
        Assert.AreEqual(GeometryKind.LineString, corrected.Kind, "the degree radius reads under the latitude-longitude system");
    }

    /// <summary>Three-dimensional segments join over every effective ordinate: an agreeing seam joins once, a seam differing only in its third ordinate refuses at the joining carrier.</summary>
    [TestMethod]
    public void ThreeDimensionalSegmentsJoinOverEveryOrdinate()
    {
        string agreeing = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList srsDimension=\"3\">0 0 5 1 1 6</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList srsDimension=\"3\">1 1 6 2 2 7</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.MatchesWkt(agreeing, CoordinateReferenceSystem.Crs84, "LINESTRING Z (0 0 5, 1 1 6, 2 2 7)");

        string altitudeBreak = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList srsDimension=\"3\">0 0 5 1 1 6</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList srsDimension=\"3\">1 1 9 2 2 7</gml:posList></gml:LineStringSegment></gml:segments>");
        GmlAssert.RefusesAt(altitudeBreak, GeometryCodecRefusalKind.StructuralViolation, "<gml:posList srsDimension=\"3\">1 1 9");
    }

    /// <summary>The joined vertex is the earlier segment's copy verbatim: a negative-zero seam keeps the earlier spelling's sign bit.</summary>
    [TestMethod]
    public void TheJoinKeepsTheEarlierCopysBits()
    {
        string negativeZeroSeam = GmlTestDocuments.Root("Curve", "<gml:segments><gml:LineStringSegment><gml:posList>-2 0 -0 0</gml:posList></gml:LineStringSegment><gml:LineStringSegment><gml:posList>0 0 5 5</gml:posList></gml:LineStringSegment></gml:segments>");
        using FlatGeometry joined = GmlAssert.Accepts(negativeZeroSeam, CoordinateReferenceSystem.Crs84);
        int vertexCount = joined.Vertices.Length;
        Assert.AreEqual(3, vertexCount, "the value-equal seam joins once");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(joined.Vertices[1].X), "the joined vertex keeps the earlier segment's negative-zero spelling");
    }

    /// <summary>The declined point-valued carriers refuse at the center site exactly as they do at every other position context.</summary>
    [TestMethod]
    public void DeclinedCarriersRefuseAtTheCenterSite()
    {
        string byReferenceCenter = GmlTestDocuments.Root("Curve", "<gml:segments><gml:CircleByCenterPoint numArc=\"1\"><gml:pointProperty/><gml:radius uom=\"deg\">2</gml:radius></gml:CircleByCenterPoint></gml:segments>");
        GmlAssert.RefusesAt(byReferenceCenter, GeometryCodecRefusalKind.UnsupportedGeometry, "<gml:pointProperty");
    }

    /// <summary>
    /// A counting allocator seam: heap-backed columns whose rental count the
    /// rent-late rows assert.
    /// </summary>
    private sealed class CountingAllocatorSpy
    {
        /// <summary>The number of column rentals taken through the seam.</summary>
        public int RentalCount { get; private set; }

        /// <summary>Rents a heap-backed vertex column and counts it.</summary>
        public System.Buffers.IMemoryOwner<Point2d> RentVertexColumn(int length)
        {
            RentalCount++;

            return FlatGeometryAllocators.Default.VertexColumns(length);
        }

        /// <summary>Rents a heap-backed ordinate column and counts it.</summary>
        public System.Buffers.IMemoryOwner<double> RentOrdinateColumn(int length)
        {
            RentalCount++;

            return FlatGeometryAllocators.Default.OrdinateColumns(length);
        }
    }
}
