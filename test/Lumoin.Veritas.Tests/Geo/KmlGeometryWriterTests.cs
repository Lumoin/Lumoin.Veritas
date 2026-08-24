using System;
using System.Buffers;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The KML writer family: canonical forms as exact strings, the one-way kind
/// collapses asserted as collapses, the absolute altitude mode exactly on
/// third-dimension nodes, the no-empty-semantics refusals across every kind, the
/// pinned validation walk order, bitwise round trips through the reader, and the
/// destination contract. Writer refusals always carry the minus-one offset — no
/// caller-visible document exists to name a byte of.
/// </summary>
[TestClass]
internal sealed class KmlGeometryWriterTests
{
    /// <summary>The root namespace declaration every canonical root carries.</summary>
    private const string RootAttributes = " xmlns=\"http://www.opengis.net/kml/2.2\"";

    /// <summary>Canonical forms are exact strings: the schema's child order, paired tags, the strict tuple form, the collapses.</summary>
    [TestMethod]
    [DataRow("POINT (1 2)", "<Point" + RootAttributes + "><coordinates>1,2</coordinates></Point>", DisplayName = "the point emits its tuple")]
    [DataRow("POINT Z (1 2 5)", "<Point" + RootAttributes + "><altitudeMode>absolute</altitudeMode><coordinates>1,2,5</coordinates></Point>", DisplayName = "the third dimension emits the absolute mode before the run")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "<LineString" + RootAttributes + "><coordinates>0,0 1,1 2,0</coordinates></LineString>", DisplayName = "the line string emits its run with one space between tuples")]
    [DataRow("LINESTRING (0 0, 4 0, 4 4, 0 0)", "<LineString" + RootAttributes + "><coordinates>0,0 4,0 4,4 0,0</coordinates></LineString>", DisplayName = "a closed line emits as the line string — the ring identity is never reconstructed")]
    [DataRow("GEOMETRYCOLLECTION (LINESTRING (0 0, 4 0, 4 4, 0 0))", "<MultiGeometry" + RootAttributes + "><LineString><coordinates>0,0 4,0 4,4 0,0</coordinates></LineString></MultiGeometry>", DisplayName = "a closed line at member position emits as the line string too — the collapse is one-way at every position")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "<Polygon" + RootAttributes + "><outerBoundaryIs><LinearRing><coordinates>0,0 4,0 4,4 0,0</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>1,1 2,1 2,2 1,1</coordinates></LinearRing></innerBoundaryIs></Polygon>", DisplayName = "the polygon emits its exterior before its interior")]
    [DataRow("POLYGON Z ((0 0 5, 4 0 5, 4 4 5, 0 0 5))", "<Polygon" + RootAttributes + "><altitudeMode>absolute</altitudeMode><outerBoundaryIs><LinearRing><coordinates>0,0,5 4,0,5 4,4,5 0,0,5</coordinates></LinearRing></outerBoundaryIs></Polygon>", DisplayName = "the third dimension emits the absolute mode before the polygon's boundaries")]
    [DataRow("POLYGON Z ((0 0 1, 4 0 2, 4 4 3, 0 0 1), (1 1 5, 2 1 6, 2 2 7, 1 1 5))", "<Polygon" + RootAttributes + "><altitudeMode>absolute</altitudeMode><outerBoundaryIs><LinearRing><coordinates>0,0,1 4,0,2 4,4,3 0,0,1</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>1,1,5 2,1,6 2,2,7 1,1,5</coordinates></LinearRing></innerBoundaryIs></Polygon>", DisplayName = "the third dimension emits the mode before the boundaries and the altitudes inside every ring")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "<MultiGeometry" + RootAttributes + "><Point><coordinates>1,2</coordinates></Point><Point><coordinates>3,4</coordinates></Point></MultiGeometry>", DisplayName = "the multi point collapses into the aggregate")]
    [DataRow("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", "<MultiGeometry" + RootAttributes + "><LineString><coordinates>0,0 1,1</coordinates></LineString><LineString><coordinates>2,2 3,3</coordinates></LineString></MultiGeometry>", DisplayName = "the multi line string collapses into the aggregate")]
    [DataRow("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))", "<MultiGeometry" + RootAttributes + "><Polygon><outerBoundaryIs><LinearRing><coordinates>0,0 4,0 4,4 0,0</coordinates></LinearRing></outerBoundaryIs></Polygon><Polygon><outerBoundaryIs><LinearRing><coordinates>10,10 14,10 14,14 10,10</coordinates></LinearRing></outerBoundaryIs></Polygon></MultiGeometry>", DisplayName = "the multi polygon collapses into the aggregate")]
    [DataRow("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1)), ((10 10, 14 10, 14 14, 10 10)))", "<MultiGeometry" + RootAttributes + "><Polygon><outerBoundaryIs><LinearRing><coordinates>0,0 4,0 4,4 0,0</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>1,1 2,1 2,2 1,1</coordinates></LinearRing></innerBoundaryIs></Polygon><Polygon><outerBoundaryIs><LinearRing><coordinates>10,10 14,10 14,14 10,10</coordinates></LinearRing></outerBoundaryIs></Polygon></MultiGeometry>", DisplayName = "a holed member keeps its hole inside its own polygon — the interior never opens one of its own")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", "<MultiGeometry" + RootAttributes + "><Point><coordinates>1,2</coordinates></Point><LineString><coordinates>0,0 1,1</coordinates></LineString></MultiGeometry>", DisplayName = "the collection is the aggregate")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (POINT (3 4)))", "<MultiGeometry" + RootAttributes + "><Point><coordinates>1,2</coordinates></Point><MultiGeometry><Point><coordinates>3,4</coordinates></Point></MultiGeometry></MultiGeometry>", DisplayName = "a nested collection emits nested aggregates — natively schema-valid")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5))", "<MultiGeometry" + RootAttributes + "><Point><coordinates>1,2</coordinates></Point><Point><altitudeMode>absolute</altitudeMode><coordinates>3,4,5</coordinates></Point></MultiGeometry>", DisplayName = "mixed dimensions emit the mode per member")]
    public void CanonicalFormsAreExactStrings(string wkt, string expected)
    {
        using FlatGeometry geometry = FromWkt(wkt);
        Assert.AreEqual(expected, Writes(in geometry), "the canonical form is the exact string");
    }

    /// <summary>A planar node never emits the altitude mode — the element appears exactly on the nodes that carry the third dimension.</summary>
    [TestMethod]
    public void TheAbsoluteModeRidesExactlyTheThirdDimension()
    {
        using FlatGeometry planar = FromWkt("LINESTRING (0 0, 1 1)");
        Assert.IsFalse(Writes(in planar).Contains("altitudeMode", StringComparison.Ordinal), "a planar node emits no mode");

        using FlatGeometry spatial = FromWkt("LINESTRING Z (0 0 1, 1 1 2)");
        Assert.Contains("<altitudeMode>absolute</altitudeMode><coordinates>", Writes(in spatial), "the third dimension emits the absolute mode before the run");
    }

    /// <summary>The written form reads back through the format's own reader — identity asserted against the collapsed forms where a collapse is the contract.</summary>
    [TestMethod]
    [DataRow("POINT (1 2)", "POINT (1 2)", DisplayName = "the point round-trips")]
    [DataRow("POINT Z (-0 2.5e-10 5)", "POINT Z (-0 2.5e-10 5)", DisplayName = "negative zero and exponent forms round-trip bitwise")]
    [DataRow("LINESTRING (0 0, 1.7976931348623157e308 -1.7976931348623157e308)", "LINESTRING (0 0, 1.7976931348623157e308 -1.7976931348623157e308)", DisplayName = "the extreme magnitudes round-trip bitwise")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "the holed polygon round-trips")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))", DisplayName = "the multi point reads back as the collection — the collapse is the contract")]
    [DataRow("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0)), ((10 10, 14 10, 14 14, 10 10)))", "GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 0)), POLYGON ((10 10, 14 10, 14 14, 10 10)))", DisplayName = "the multi polygon reads back as the collection of polygons")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))", "GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))", DisplayName = "the nested collection round-trips whole")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5))", "GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5))", DisplayName = "the mixed-dimension collection round-trips")]
    public void WrittenFormsReadBackThroughTheCollapse(string wkt, string expectedWkt)
    {
        using FlatGeometry original = FromWkt(wkt);
        string text = Writes(in original);
        byte[] document = Encoding.UTF8.GetBytes(text);
        bool read = KmlGeometryReader.TryRead(document, out FlatGeometry reread, out GeometryCodecRefusal refusal);
        Assert.IsTrue(read, $"the written form must read back, but refused {refusal.Kind} at {refusal.ByteOffset}");

        using FlatGeometry expected = FromWkt(expectedWkt);

        using(reread)
        {
            Assert.AreEqual(expected, reread, "the round trip lands on the collapsed contract value, structurally and bitwise");

            string again = Writes(in reread);
            Assert.AreEqual(text, again, "canonical text is a writer fixed point through the collapse");
        }
    }

    /// <summary>The format has no empty semantics: the uninitialized carrier and every typed empty refuse as unrepresentable at minus one.</summary>
    [TestMethod]
    [DataRow((int)GeometryKind.Point, DisplayName = "the empty point refuses")]
    [DataRow((int)GeometryKind.LineString, DisplayName = "the empty line string refuses")]
    [DataRow((int)GeometryKind.Polygon, DisplayName = "the empty polygon refuses")]
    [DataRow((int)GeometryKind.MultiPoint, DisplayName = "the empty multi point refuses")]
    [DataRow((int)GeometryKind.MultiLineString, DisplayName = "the empty multi line string refuses")]
    [DataRow((int)GeometryKind.MultiPolygon, DisplayName = "the empty multi polygon refuses")]
    [DataRow((int)GeometryKind.GeometryCollection, DisplayName = "the empty collection refuses")]
    public void EveryTypedEmptyRefuses(int kind)
    {
        using FlatGeometry empty = FlatGeometry.Empty((GeometryKind)kind);
        AssertRefuses(in empty, GeometryCodecRefusalKind.EmptyUnrepresentable);
    }

    /// <summary>The uninitialized carrier refuses before the shared walk ever runs — the format degrades nothing.</summary>
    [TestMethod]
    public void TheUninitializedCarrierRefuses()
    {
        FlatGeometry uninitialized = default;
        AssertRefuses(in uninitialized, GeometryCodecRefusalKind.EmptyUnrepresentable);
    }

    /// <summary>An empty member deep inside a collection refuses — every node is checked, not only the root.</summary>
    [TestMethod]
    public void AnEmptyMemberRefuses()
    {
        using FlatGeometry holed = FromWkt("GEOMETRYCOLLECTION (POINT (1 2), POINT EMPTY)");
        AssertRefuses(in holed, GeometryCodecRefusalKind.EmptyUnrepresentable);
    }

    /// <summary>A measured value refuses rather than silently dropping the measure.</summary>
    [TestMethod]
    public void AMeasuredValueRefuses()
    {
        using FlatGeometry measured = FromWkt("POINT M (1 2 7)");
        AssertRefuses(in measured, GeometryCodecRefusalKind.MeasureUnrepresentable);
    }

    /// <summary>A not-a-number altitude slot under a third-dimension node refuses as non-finite.</summary>
    [TestMethod]
    public void ANotANumberAltitudeRefuses()
    {
        using FlatGeometry poisoned = PointWithNotANumberAltitude();
        AssertRefuses(in poisoned, GeometryCodecRefusalKind.NonFiniteCoordinate);
    }

    /// <summary>A not-a-number planar ordinate refuses as non-finite — the writer never emits a hole its own reader would refuse.</summary>
    [TestMethod]
    public void APlanarNotANumberOrdinateRefuses()
    {
        using FlatGeometry poisoned = PointWithNotANumberLongitude();
        AssertRefuses(in poisoned, GeometryCodecRefusalKind.NonFiniteCoordinate);
    }

    /// <summary>Model nesting the format's own reader would refuse is refused by the writer too — the boundary pair both directions.</summary>
    [TestMethod]
    public void TheWriterDepthBoundMirrorsTheReader()
    {
        using FlatGeometry atBound = NestedCollections(31);
        string text = Writes(in atBound);
        Assert.IsGreaterThan(0, text.Length, "thirty-one wrappers and the leaf write");

        using FlatGeometry pastBound = NestedCollections(32);
        AssertRefuses(in pastBound, GeometryCodecRefusalKind.NestingTooDeep);
    }

    /// <summary>The pinned walk order: depth beats a node's emptiness, and within the node walk a measured node beats a later empty one.</summary>
    [TestMethod]
    public void MixedDefectsFollowThePinnedWalkOrder()
    {
        using FlatGeometry deepWithEmpty = NestedCollectionsAroundEmptyPoint(32);
        AssertRefuses(in deepWithEmpty, GeometryCodecRefusalKind.NestingTooDeep);

        using FlatGeometry measuredBeforeEmpty = FromWkt("GEOMETRYCOLLECTION (POINT M (1 2 7), POINT EMPTY)");
        AssertRefuses(in measuredBeforeEmpty, GeometryCodecRefusalKind.MeasureUnrepresentable);

        using FlatGeometry emptyBeforeMeasured = FromWkt("GEOMETRYCOLLECTION (POINT EMPTY, POINT M (1 2 7))");
        AssertRefuses(in emptyBeforeMeasured, GeometryCodecRefusalKind.EmptyUnrepresentable);
    }

    /// <summary>Defects piled inside one node follow the same pinned order: the measure beats a non-finite ordinate, and the format's own emptiness beats the measure.</summary>
    [TestMethod]
    public void WithinNodeDefectsFollowThePinnedWalkOrder()
    {
        using FlatGeometry measuredAndNonFinite = MeasuredPointWithNotANumberAltitude();
        AssertRefuses(in measuredAndNonFinite, GeometryCodecRefusalKind.MeasureUnrepresentable);

        using FlatGeometry emptyAndMeasured = MeasuredEmptyPoint();
        AssertRefuses(in emptyAndMeasured, GeometryCodecRefusalKind.EmptyUnrepresentable);
    }

    /// <summary>A refusal leaves the destination untouched: the walk validates the whole geometry before the first destination write.</summary>
    [TestMethod]
    public void ARefusalLeavesTheDestinationUntouched()
    {
        ArrayBufferWriter<byte> destination = new();
        destination.Write("preloaded"u8);

        using FlatGeometry measured = FromWkt("POINT M (1 2 7)");
        bool measuredWritten = KmlGeometryWriter.TryWrite(in measured, destination, out GeometryCodecRefusal measuredRefusal);
        Assert.IsFalse(measuredWritten, "the measured value must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MeasureUnrepresentable, measuredRefusal.Kind, "the refusal kind");
        Assert.AreEqual(9, destination.WrittenCount, "the destination is untouched beyond its preload");

        using FlatGeometry pastBound = NestedCollections(32);
        bool deepWritten = KmlGeometryWriter.TryWrite(in pastBound, destination, out GeometryCodecRefusal deepRefusal);
        Assert.IsFalse(deepWritten, "the deep nest must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.NestingTooDeep, deepRefusal.Kind, "the refusal kind");
        Assert.AreEqual(9, destination.WrittenCount, "the depth refusal writes nothing either");
    }

    /// <summary>A null destination throws the caller-contract exception rather than refusing.</summary>
    [TestMethod]
    public void ANullDestinationThrows()
    {
        using FlatGeometry point = FromWkt("POINT (1 2)");
        bool threw = false;

        try
        {
            KmlGeometryWriter.TryWrite(in point, destination: null!, out _);
            Assert.Fail("a null destination must throw rather than refuse");
        }
        catch(ArgumentNullException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "the caller-contract exception surfaced");
    }

    /// <summary>A null destination throws even under the uninitialized carrier — the caller-contract exception beats the empty refusal.</summary>
    [TestMethod]
    public void ANullDestinationThrowsUnderTheDefaultCarrier()
    {
        FlatGeometry uninitialized = default;
        bool threw = false;

        try
        {
            KmlGeometryWriter.TryWrite(in uninitialized, destination: null!, out _);
            Assert.Fail("a null destination must throw before the empty short-circuit refuses");
        }
        catch(ArgumentNullException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "the caller-contract exception surfaced ahead of the refusal channel");
    }

    /// <summary>Reads a well-known-text value into the flat model.</summary>
    private static FlatGeometry FromWkt(string wkt)
    {
        bool parsed = WktGeometryReader.TryRead(Encoding.UTF8.GetBytes(wkt), out FlatGeometry geometry, out _);
        Assert.IsTrue(parsed, $"the fixture '{wkt}' must parse");

        return geometry;
    }

    /// <summary>Writes a value that must be accepted, returning the canonical text.</summary>
    private static string Writes(in FlatGeometry geometry)
    {
        bool written = KmlGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);
        Assert.IsTrue(written, $"the value must write, but refused {refusal.Kind} at {refusal.ByteOffset}");

        return text;
    }

    /// <summary>Asserts a value refuses with the kind and the writer's minus-one offset, the text empty.</summary>
    private static void AssertRefuses(in FlatGeometry geometry, GeometryCodecRefusalKind kind)
    {
        bool written = KmlGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);
        Assert.IsFalse(written, "the value must refuse");
        Assert.AreEqual(kind, refusal.Kind, "the refusal kind");
        Assert.AreEqual(-1, refusal.ByteOffset, "a writer refusal names no input byte");
        Assert.AreEqual(string.Empty, text, "the text is empty on refusal");
    }

    /// <summary>Builds a collection nest of the given wrapper count around a point leaf.</summary>
    private static FlatGeometry NestedCollections(int wrappers)
    {
        return NestedCollectionsAround(wrappers, emptyLeaf: false);
    }

    /// <summary>Builds a collection nest of the given wrapper count around an EMPTY point leaf — the depth-beats-emptiness fixture.</summary>
    private static FlatGeometry NestedCollectionsAroundEmptyPoint(int wrappers)
    {
        return NestedCollectionsAround(wrappers, emptyLeaf: true);
    }

    /// <summary>Builds a collection nest around a point leaf, empty or carried.</summary>
    private static FlatGeometry NestedCollectionsAround(int wrappers, bool emptyLeaf)
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

        int leaf;

        if(emptyLeaf)
        {
            leaf = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, firstPart: 0, partCount: 0);
        }
        else
        {
            int start = builder.VertexCount;
            builder.AddVertex(new Point2d(1.0, 2.0));
            builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));
            leaf = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, builder.PartCount - 1, 1);
        }

        builder.SetChildren(previous, [leaf]);

        return builder.ToGeometry();
    }

    /// <summary>Builds a third-dimension point whose altitude slot is not a number.</summary>
    private static FlatGeometry PointWithNotANumberAltitude()
    {
        FlatGeometryBuilder builder = new();
        int start = builder.VertexCount;
        builder.AddVertex(new Point2d(1.0, 2.0), double.NaN, double.NaN);
        builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));
        int node = builder.AddNode(GeometryKind.Point, hasZ: true, hasM: false, builder.PartCount - 1, 1);
        builder.RootIndex = node;

        return builder.ToGeometry();
    }

    /// <summary>Builds a planar point whose longitude is not a number.</summary>
    private static FlatGeometry PointWithNotANumberLongitude()
    {
        FlatGeometryBuilder builder = new();
        int start = builder.VertexCount;
        builder.AddVertex(new Point2d(double.NaN, 2.0));
        builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));
        int node = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, builder.PartCount - 1, 1);
        builder.RootIndex = node;

        return builder.ToGeometry();
    }

    /// <summary>Builds a measured third-dimension point whose altitude slot is not a number — the measure-before-finiteness fixture.</summary>
    private static FlatGeometry MeasuredPointWithNotANumberAltitude()
    {
        FlatGeometryBuilder builder = new();
        int start = builder.VertexCount;
        builder.AddVertex(new Point2d(1.0, 2.0), double.NaN, 7.0);
        builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));
        int node = builder.AddNode(GeometryKind.Point, hasZ: true, hasM: true, builder.PartCount - 1, 1);
        builder.RootIndex = node;

        return builder.ToGeometry();
    }

    /// <summary>Builds a measured point carrying no part at all — the representability-before-measure fixture.</summary>
    private static FlatGeometry MeasuredEmptyPoint()
    {
        FlatGeometryBuilder builder = new();
        int node = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: true, firstPart: 0, partCount: 0);
        builder.RootIndex = node;

        return builder.ToGeometry();
    }
}
