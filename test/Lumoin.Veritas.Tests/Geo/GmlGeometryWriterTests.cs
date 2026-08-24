using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Transforms;
using Lumoin.Veritas.Geo.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The GML writer family: canonical emission pinned as exact strings — the
/// generated identifier sequence included — the system parameter adjudicating
/// before everything, the per-carrier third-dimension declaration, the empty
/// matrix with the degradation row, the refusal rows at minus one with the
/// destination untouched, and the certified round trips both ways. Writer refusal
/// rows assert the kind AND the minus-one offset.
/// </summary>
[TestClass]
internal sealed class GmlGeometryWriterTests
{
    /// <summary>The canonical root opening under the CRS84 system, shared by the exact-string expectations.</summary>
    private const string Crs84RootAttributes = " xmlns:gml=\"http://www.opengis.net/gml/3.2\" gml:id=\"g0\" srsName=\"http://www.opengis.net/def/crs/OGC/1.3/CRS84\"";

    /// <summary>The canonical root opening under the latitude-longitude system.</summary>
    private const string Epsg4326RootAttributes = " xmlns:gml=\"http://www.opengis.net/gml/3.2\" gml:id=\"g0\" srsName=\"http://www.opengis.net/def/crs/EPSG/0/4326\"";

    /// <summary>The canonical root opening under the metre system.</summary>
    private const string WebMercatorRootAttributes = " xmlns:gml=\"http://www.opengis.net/gml/3.2\" gml:id=\"g0\" srsName=\"http://www.opengis.net/def/crs/EPSG/0/3857\"";

    /// <summary>Parses reference text into a value for the writer.</summary>
    private static FlatGeometry FromWkt(string wkt)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(wkt);
        bool parsed = WktGeometryReader.TryRead(bytes, out FlatGeometry geometry, out _);
        Assert.IsTrue(parsed, $"the reference text '{wkt}' must parse");

        return geometry;
    }

    /// <summary>Writes a value that must succeed, returning the canonical text.</summary>
    private static string Writes(in FlatGeometry geometry, CoordinateReferenceSystem system)
    {
        bool written = GmlGeometryWriter.TryWriteString(in geometry, system, out string text, out GeometryCodecRefusal refusal);
        Assert.IsTrue(written, $"the value must write, but refused {refusal.Kind} at {refusal.ByteOffset}");

        return text;
    }

    /// <summary>Canonical forms are exact strings, the identifier sequence included.</summary>
    [TestMethod]
    [DataRow("POINT (1 2)", "<gml:Point" + Crs84RootAttributes + "><gml:pos>1 2</gml:pos></gml:Point>", DisplayName = "the point emits its position")]
    [DataRow("POINT Z (1 2 5)", "<gml:Point" + Crs84RootAttributes + "><gml:pos srsDimension=\"3\">1 2 5</gml:pos></gml:Point>", DisplayName = "the third dimension declares on the carrier")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "<gml:LineString" + Crs84RootAttributes + "><gml:posList>0 0 1 1 2 0</gml:posList></gml:LineString>", DisplayName = "the line string emits its position list")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "<gml:Polygon" + Crs84RootAttributes + "><gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 4 4 0 0</gml:posList></gml:LinearRing></gml:exterior><gml:interior><gml:LinearRing><gml:posList>1 1 2 1 2 2 1 1</gml:posList></gml:LinearRing></gml:interior></gml:Polygon>", DisplayName = "the polygon emits its exterior then its interior, the rings without identifiers")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "<gml:MultiPoint" + Crs84RootAttributes + "><gml:pointMember><gml:Point gml:id=\"g1\"><gml:pos>1 2</gml:pos></gml:Point></gml:pointMember><gml:pointMember><gml:Point gml:id=\"g2\"><gml:pos>3 4</gml:pos></gml:Point></gml:pointMember></gml:MultiPoint>", DisplayName = "the multi point emits singular members with the identifier sequence")]
    [DataRow("MULTILINESTRING ((0 0, 1 1))", "<gml:MultiCurve" + Crs84RootAttributes + "><gml:curveMember><gml:LineString gml:id=\"g1\"><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:curveMember></gml:MultiCurve>", DisplayName = "the multi curve emits line-string members")]
    [DataRow("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))", "<gml:MultiSurface" + Crs84RootAttributes + "><gml:surfaceMember><gml:Polygon gml:id=\"g1\"><gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 4 4 0 0</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></gml:surfaceMember><gml:surfaceMember><gml:Polygon gml:id=\"g2\"><gml:exterior><gml:LinearRing><gml:posList>10 10 14 10 14 14 10 10</gml:posList></gml:LinearRing></gml:exterior></gml:Polygon></gml:surfaceMember></gml:MultiSurface>", DisplayName = "the two-polygon multi surface pins the identifier sequence")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", "<gml:MultiGeometry" + Crs84RootAttributes + "><gml:geometryMember><gml:Point gml:id=\"g1\"><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember><gml:geometryMember><gml:LineString gml:id=\"g2\"><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:geometryMember></gml:MultiGeometry>", DisplayName = "the collection emits geometry members")]
    [DataRow("MULTIPOINT EMPTY", "<gml:MultiPoint" + Crs84RootAttributes + "/>", DisplayName = "the empty aggregate emits the self-closing memberless form")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "<gml:MultiGeometry" + Crs84RootAttributes + "/>", DisplayName = "the empty collection emits the self-closing memberless form")]
    [DataRow("MULTILINESTRING EMPTY", "<gml:MultiCurve" + Crs84RootAttributes + "/>", DisplayName = "the empty multi curve emits the self-closing memberless form")]
    [DataRow("MULTIPOLYGON EMPTY", "<gml:MultiSurface" + Crs84RootAttributes + "/>", DisplayName = "the empty multi surface emits the self-closing memberless form")]
    [DataRow("LINESTRING Z (0 0 1, 1 1 2)", "<gml:LineString" + Crs84RootAttributes + "><gml:posList srsDimension=\"3\">0 0 1 1 1 2</gml:posList></gml:LineString>", DisplayName = "the third dimension declares on the position-list carrier")]
    [DataRow("MULTIPOINT Z ((1 2 5), (3 4 6))", "<gml:MultiPoint" + Crs84RootAttributes + "><gml:pointMember><gml:Point gml:id=\"g1\"><gml:pos srsDimension=\"3\">1 2 5</gml:pos></gml:Point></gml:pointMember><gml:pointMember><gml:Point gml:id=\"g2\"><gml:pos srsDimension=\"3\">3 4 6</gml:pos></gml:Point></gml:pointMember></gml:MultiPoint>", DisplayName = "the third dimension declares on every member's carrier")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5))", "<gml:MultiGeometry" + Crs84RootAttributes + "><gml:geometryMember><gml:Point gml:id=\"g1\"><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember><gml:geometryMember><gml:Point gml:id=\"g2\"><gml:pos srsDimension=\"3\">3 4 5</gml:pos></gml:Point></gml:geometryMember></gml:MultiGeometry>", DisplayName = "the mixed collection declares the third dimension on only the member that carries it")]
    public void CanonicalFormsAreExactStrings(string wkt, string expected)
    {
        using FlatGeometry geometry = FromWkt(wkt);
        Assert.AreEqual(expected, Writes(in geometry, CoordinateReferenceSystem.Crs84), "the canonical form");
    }

    /// <summary>The uninitialized carrier degrades to the memberless heterogeneous aggregate under a recognized system.</summary>
    [TestMethod]
    public void TheUninitializedCarrierDegradesToTheMemberlessAggregate()
    {
        FlatGeometry geometry = default;
        Assert.AreEqual("<gml:MultiGeometry" + Crs84RootAttributes + "/>", Writes(in geometry, CoordinateReferenceSystem.Crs84), "the degradation form");
    }

    /// <summary>The system parameter adjudicates before everything: the default system refuses at minus one for real values AND for empties, the destination untouched.</summary>
    [TestMethod]
    public void TheDefaultSystemRefusesBeforeEverything()
    {
        using FlatGeometry point = FromWkt("POINT (1 2)");
        ArrayBufferWriter<byte> destination = new();
        destination.Write("preloaded"u8);
        bool written = GmlGeometryWriter.TryWrite(in point, default, destination, out GeometryCodecRefusal refusal);
        Assert.IsFalse(written, "the default system must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, refusal.Kind, "the refusal kind");
        Assert.AreEqual(-1, refusal.ByteOffset, "a writer refusal names no input byte");
        Assert.AreEqual(9, destination.WrittenCount, "the destination is untouched beyond its preload");

        FlatGeometry empty = default;
        bool emptyWritten = GmlGeometryWriter.TryWriteString(in empty, default, out string text, out GeometryCodecRefusal emptyRefusal);
        Assert.IsFalse(emptyWritten, "the empty short-circuit never runs under a bad system — the empty form itself carries the declaration");
        Assert.AreEqual(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, emptyRefusal.Kind, "the parameter check precedes the empty short-circuit");
        Assert.AreEqual(-1, emptyRefusal.ByteOffset, "the refusal is a writer refusal");
        Assert.AreEqual(string.Empty, text, "the text is empty on refusal");

        using FlatGeometry measured = FromWkt("POINT M (1 2 7)");
        bool measuredWritten = GmlGeometryWriter.TryWriteString(in measured, default, out _, out GeometryCodecRefusal orderRefusal);
        Assert.IsFalse(measuredWritten, "the call refuses");
        Assert.AreEqual(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, orderRefusal.Kind, "the parameter check beats the measure defect in the same call");
    }

    /// <summary>Empty primitives have no encoding and refuse; the measure and the missing third ordinate refuse through the shared walk.</summary>
    [TestMethod]
    [DataRow("POINT EMPTY", (int)GeometryCodecRefusalKind.EmptyUnrepresentable, DisplayName = "the empty point refuses")]
    [DataRow("LINESTRING EMPTY", (int)GeometryCodecRefusalKind.EmptyUnrepresentable, DisplayName = "the empty line string refuses")]
    [DataRow("POLYGON EMPTY", (int)GeometryCodecRefusalKind.EmptyUnrepresentable, DisplayName = "the empty polygon refuses")]
    [DataRow("POINT M (1 2 7)", (int)GeometryCodecRefusalKind.MeasureUnrepresentable, DisplayName = "the measured value refuses rather than dropping its measure")]
    public void UnrepresentableValuesRefuseAtMinusOne(string wkt, int expectedKind)
    {
        using FlatGeometry geometry = FromWkt(wkt);
        bool written = GmlGeometryWriter.TryWriteString(in geometry, CoordinateReferenceSystem.Crs84, out string text, out GeometryCodecRefusal refusal);
        Assert.IsFalse(written, $"'{wkt}' must refuse");
        Assert.AreEqual((GeometryCodecRefusalKind)expectedKind, refusal.Kind, "the refusal kind");
        Assert.AreEqual(-1, refusal.ByteOffset, "a writer refusal names no input byte");
        Assert.AreEqual(string.Empty, text, "the text is empty on refusal");
    }

    /// <summary>A refusal from the shared validation walk leaves the destination untouched: the measure defect and the depth defect both refuse before the first destination write.</summary>
    [TestMethod]
    public void TheSharedWalkRefusalLeavesTheDestinationUntouched()
    {
        ArrayBufferWriter<byte> destination = new();
        destination.Write("preloaded"u8);

        using FlatGeometry measured = FromWkt("POINT M (1 2 7)");
        bool measuredWritten = GmlGeometryWriter.TryWrite(in measured, CoordinateReferenceSystem.Crs84, destination, out GeometryCodecRefusal measuredRefusal);
        Assert.IsFalse(measuredWritten, "the measured value must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MeasureUnrepresentable, measuredRefusal.Kind, "the refusal kind");
        Assert.AreEqual(-1, measuredRefusal.ByteOffset, "a writer refusal names no input byte");
        Assert.AreEqual(9, destination.WrittenCount, "the destination is untouched beyond its preload");

        using FlatGeometry pastBound = NestedCollections(32);
        bool deepWritten = GmlGeometryWriter.TryWrite(in pastBound, CoordinateReferenceSystem.Crs84, destination, out GeometryCodecRefusal deepRefusal);
        Assert.IsFalse(deepWritten, "the thirty-three-deep nest must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.NestingTooDeep, deepRefusal.Kind, "the refusal kind");
        Assert.AreEqual(9, destination.WrittenCount, "the depth refusal writes nothing either");
    }

    /// <summary>Model nesting the format's own reader would refuse is refused by the writer too — the boundary pair both directions.</summary>
    [TestMethod]
    public void TheWriterDepthBoundMirrorsTheReader()
    {
        using FlatGeometry atBound = NestedCollections(31);
        string text = Writes(atBound, CoordinateReferenceSystem.Crs84);
        Assert.IsGreaterThan(0, text.Length, "thirty-one wrappers and the leaf write");

        using FlatGeometry pastBound = NestedCollections(32);
        bool written = GmlGeometryWriter.TryWriteString(in pastBound, CoordinateReferenceSystem.Crs84, out _, out GeometryCodecRefusal refusal);
        Assert.IsFalse(written, "the thirty-second wrapper's leaf refuses");
        Assert.AreEqual(GeometryCodecRefusalKind.NestingTooDeep, refusal.Kind, "the refusal kind");
        Assert.AreEqual(-1, refusal.ByteOffset, "a writer refusal names no input byte");
    }

    /// <summary>The written form reads back to the identical value — structural and bitwise — and canonical text is a writer fixed point.</summary>
    [TestMethod]
    [DataRow("POINT (1 2)", DisplayName = "the point round-trips")]
    [DataRow("POINT Z (-0 2.5e-10 5)", DisplayName = "negative zero and exponent forms round-trip bitwise")]
    [DataRow("LINESTRING (0 0, 1.7976931348623157e308 -1.7976931348623157e308)", DisplayName = "the extreme magnitudes round-trip bitwise")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "the holed polygon round-trips")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", DisplayName = "the multi point round-trips")]
    [DataRow("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))", DisplayName = "the multi polygon round-trips")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))", DisplayName = "the nested collection round-trips through the reader tolerance")]
    [DataRow("LINESTRING Z (0 0 1, 1 1 2)", DisplayName = "the three-dimensional line string round-trips")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5))", DisplayName = "the mixed-dimension collection round-trips")]
    public void WrittenFormsReadBackIdentically(string wkt)
    {
        using FlatGeometry original = FromWkt(wkt);
        string text = Writes(in original, CoordinateReferenceSystem.Crs84);
        byte[] document = Encoding.UTF8.GetBytes(text);
        bool read = GmlGeometryReader.TryRead(document, out FlatGeometry reread, out CoordinateReferenceSystem system, out GeometryCodecRefusal refusal);
        Assert.IsTrue(read, $"the written form must read back, but refused {refusal.Kind} at {refusal.ByteOffset}");

        using(reread)
        {
            Assert.AreEqual(CoordinateReferenceSystem.Crs84, system, "the declared system reads back");
            Assert.AreEqual(original, reread, "the round trip preserves the value structurally and bitwise");
        }

        string again = Writes(in reread, CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(text, again, "canonical text is a writer fixed point");
    }

    /// <summary>The canonical spelling of every roster system rides the root declaration.</summary>
    [TestMethod]
    public void EveryRosterSystemDeclaresItsCanonicalSpelling()
    {
        using FlatGeometry point = FromWkt("POINT (1 2)");
        Assert.Contains("srsName=\"http://www.opengis.net/def/crs/EPSG/0/4326\"", Writes(in point, CoordinateReferenceSystem.Epsg4326), "the latitude-longitude system spells canonically");
        Assert.Contains("srsName=\"http://www.opengis.net/def/crs/EPSG/0/3857\"", Writes(in point, CoordinateReferenceSystem.WebMercator), "the metre system spells canonically");
    }

    /// <summary>The non-default roster systems emit as exact canonical strings — the ordinates verbatim, never reordered for the declared system.</summary>
    [TestMethod]
    public void RosterSystemEmissionsAreExactStrings()
    {
        using FlatGeometry latitudeFirst = FromWkt("POINT (60.17 24.94)");
        Assert.AreEqual("<gml:Point" + Epsg4326RootAttributes + "><gml:pos>60.17 24.94</gml:pos></gml:Point>", Writes(in latitudeFirst, CoordinateReferenceSystem.Epsg4326), "the latitude-longitude canonical form");

        using FlatGeometry metres = FromWkt("POINT (2776123.5 8437451.25)");
        Assert.AreEqual("<gml:Point" + WebMercatorRootAttributes + "><gml:pos>2776123.5 8437451.25</gml:pos></gml:Point>", Writes(in metres, CoordinateReferenceSystem.WebMercator), "the metre canonical form");
    }

    /// <summary>Under the latitude-longitude system latitude rides first in the document AND in the reread value — the axis-order fact pinned through the writer, both directions.</summary>
    [TestMethod]
    public void TheLatitudeLongitudeRoundTripPinsLatitudeFirst()
    {
        using FlatGeometry original = FromWkt("POINT (60.17 24.94)");
        string text = Writes(in original, CoordinateReferenceSystem.Epsg4326);
        Assert.Contains("<gml:pos>60.17 24.94</gml:pos>", text, "latitude rides first in the document");

        byte[] document = Encoding.UTF8.GetBytes(text);
        bool read = GmlGeometryReader.TryRead(document, out FlatGeometry reread, out CoordinateReferenceSystem system, out GeometryCodecRefusal refusal);
        Assert.IsTrue(read, $"the written form must read back, but refused {refusal.Kind} at {refusal.ByteOffset}");

        using(reread)
        {
            Assert.AreEqual(CoordinateReferenceSystem.Epsg4326, system, "the declared system reads back");
            Assert.AreEqual(new Point2d(60.17, 24.94), reread.Vertices[0], "latitude rides first in the model too");
        }
    }

    /// <summary>A null destination throws the caller-contract exception rather than refusing.</summary>
    [TestMethod]
    public void ANullDestinationThrows()
    {
        using FlatGeometry point = FromWkt("POINT (1 2)");
        bool threw = false;

        try
        {
            GmlGeometryWriter.TryWrite(in point, CoordinateReferenceSystem.Crs84, destination: null!, out _);
            Assert.Fail("a null destination must throw rather than refuse");
        }
        catch(ArgumentNullException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "the caller-contract exception surfaced");
    }

    /// <summary>A null destination throws even under the default system — the caller-contract exception beats the parameter refusal.</summary>
    [TestMethod]
    public void ANullDestinationThrowsUnderTheDefaultSystem()
    {
        using FlatGeometry point = FromWkt("POINT (1 2)");
        bool threw = false;

        try
        {
            GmlGeometryWriter.TryWrite(in point, default, destination: null!, out _);
            Assert.Fail("a null destination must throw before the system parameter adjudicates");
        }
        catch(ArgumentNullException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "the caller-contract exception surfaced ahead of the refusal channel");
    }

    /// <summary>Builds a collection nest of the given wrapper count around a point leaf.</summary>
    private static FlatGeometry NestedCollections(int wrappers)
    {
        FlatGeometryBuilder builder = new();
        int previous = -1;

        for(int level = 0; level < wrappers; level++)
        {
            int node = builder.AddNode(GeometryKind.GeometryCollection, hasZ: false, hasM: false, firstPart: 0, partCount: 0);

            if(previous >= 0)
            {
                builder.SetChildren(previous, [node]);
            }
            else
            {
                builder.RootIndex = node;
            }

            previous = node;
        }

        int start = builder.VertexCount;
        builder.AddVertex(new Point2d(1.0, 2.0));
        builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));

        int leaf = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, firstPart: builder.PartCount - 1, partCount: 1);
        builder.SetChildren(previous, [leaf]);

        return builder.ToGeometry();
    }
}
