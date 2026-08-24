using System;
using System.Text;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Transforms;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.Json.Stj;
using Lumoin.Veritas.Geo.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The cross-format face of the codec family: one model corpus through every
/// writer and re-reader with equivalence asserted modulo the documented kind
/// collapses, the well-known-text anchor pinning every reader's materialization
/// of one value, the KML collapse proven as the contract rather than an accident
/// of one pair, the mixed-altitude collection riding every format with its
/// per-member dimension markers, and one double corpus emitting identical number
/// bytes from all four writers.
/// </summary>
[TestClass]
internal sealed class GeometryCodecCrossFormatTests
{
    /// <summary>The canonical GML root attribute run under the CRS84 system, shared by the exact-string expectations.</summary>
    private const string GmlRootAttributes = " xmlns:gml=\"http://www.opengis.net/gml/3.2\" gml:id=\"g0\" srsName=\"http://www.opengis.net/def/crs/OGC/1.3/CRS84\"";

    /// <summary>The root namespace declaration every canonical KML root carries.</summary>
    private const string KmlRootAttributes = " xmlns=\"http://www.opengis.net/kml/2.2\"";

    /// <summary>
    /// One model corpus rides every pair: GeoJSON and GML preserve the value
    /// structurally and bitwise, and KML lands exactly on its documented image —
    /// the value itself everywhere except the typed multis, whose image is the
    /// collection twin stated per row.
    /// </summary>
    [TestMethod]
    [DataRow("POINT (1 2)", "POINT (1 2)", DisplayName = "a point")]
    [DataRow("POINT Z (1 2 5)", "POINT Z (1 2 5)", DisplayName = "a point with altitude")]
    [DataRow("POINT (-0 0)", "POINT (-0 0)", DisplayName = "a negative-zero ordinate survives every format")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "LINESTRING (0 0, 1 1, 2 0)", DisplayName = "a line string")]
    [DataRow("LINESTRING Z (0 0 1, 1 1 2)", "LINESTRING Z (0 0 1, 1 1 2)", DisplayName = "a line string with altitudes")]
    [DataRow("LINESTRING (0 0, 4 0, 4 4, 0 0)", "LINESTRING (0 0, 4 0, 4 4, 0 0)", DisplayName = "a closed line stays a line string in every format")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0))", "POLYGON ((0 0, 4 0, 4 4, 0 0))", DisplayName = "a shell-only polygon")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", DisplayName = "a holed polygon")]
    [DataRow("POLYGON Z ((0 0 5, 4 0 5, 4 4 5, 0 0 5))", "POLYGON Z ((0 0 5, 4 0 5, 4 4 5, 0 0 5))", DisplayName = "a polygon with altitudes")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))", DisplayName = "a multi point collapses to its collection twin through KML only")]
    [DataRow("MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))", "GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1), LINESTRING (2 2, 3 3))", DisplayName = "a multi line string collapses to its collection twin through KML only")]
    [DataRow("MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1)), ((10 10, 14 10, 14 14, 10 10)))", "GEOMETRYCOLLECTION (POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1)), POLYGON ((10 10, 14 10, 14 14, 10 10)))", DisplayName = "a multi polygon with a holed member collapses to its collection twin through KML only")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", "GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", DisplayName = "a collection keeps its kind in every format")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))", "GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))", DisplayName = "a nested collection rides every format")]
    [DataRow("GEOMETRYCOLLECTION (MULTIPOINT ((1 2), (3 4)))", "GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4)))", DisplayName = "a typed multi at member position collapses through KML only")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5), LINESTRING Z (0 0 1, 1 1 2))", "GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5), LINESTRING Z (0 0 1, 1 1 2))", DisplayName = "a mixed-altitude collection rides every format per member")]
    public void OneCorpusRoundTripsThroughEveryCodec(string wellKnownText, string kmlImageText)
    {
        using FlatGeometry value = FromWkt(wellKnownText);
        using FlatGeometry kmlImage = FromWkt(kmlImageText);

        using FlatGeometry fromGeoJson = ReadsGeoJson(WritesGeoJson(in value));
        Assert.AreEqual(value, fromGeoJson, "the GeoJSON pair preserves the value structurally and bitwise");

        using FlatGeometry fromGml = GmlAssert.Accepts(WritesGml(in value), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(value, fromGml, "the GML pair preserves the value structurally and bitwise");

        using FlatGeometry fromKml = KmlAssert.Accepts(WritesKml(in value));
        Assert.AreEqual(kmlImage, fromKml, "the KML pair lands exactly on the documented image");
    }

    /// <summary>
    /// The well-known-text anchor: hand-authored canonical documents of one
    /// abstract geometry, one per format, all materialize the value the
    /// well-known-text reader materializes — structurally and bitwise equal.
    /// </summary>
    [TestMethod]
    [DataRow("POINT (1 2)", "{\"type\":\"Point\",\"coordinates\":[1,2]}", "Point", "<gml:pos>1 2</gml:pos>", "Point", "<coordinates>1,2</coordinates>", DisplayName = "the point anchor")]
    [DataRow("POINT Z (1 2 5)", "{\"type\":\"Point\",\"coordinates\":[1,2,5]}", "Point", "<gml:pos srsDimension=\"3\">1 2 5</gml:pos>", "Point", "<altitudeMode>absolute</altitudeMode><coordinates>1,2,5</coordinates>", DisplayName = "the altitude-carrying point anchor")]
    [DataRow("LINESTRING (0 0, 1 1, 2 0)", "{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1],[2,0]]}", "LineString", "<gml:posList>0 0 1 1 2 0</gml:posList>", "LineString", "<coordinates>0,0 1,1 2,0</coordinates>", DisplayName = "the line string anchor")]
    [DataRow("POLYGON ((0 0, 4 0, 4 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[4,0],[4,4],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}", "Polygon", "<gml:exterior><gml:LinearRing><gml:posList>0 0 4 0 4 4 0 0</gml:posList></gml:LinearRing></gml:exterior><gml:interior><gml:LinearRing><gml:posList>1 1 2 1 2 2 1 1</gml:posList></gml:LinearRing></gml:interior>", "Polygon", "<outerBoundaryIs><LinearRing><coordinates>0,0 4,0 4,4 0,0</coordinates></LinearRing></outerBoundaryIs><innerBoundaryIs><LinearRing><coordinates>1,1 2,1 2,2 1,1</coordinates></LinearRing></innerBoundaryIs>", DisplayName = "the holed polygon anchor")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))", "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1]]}]}", "MultiGeometry", "<gml:geometryMember><gml:Point><gml:pos>1 2</gml:pos></gml:Point></gml:geometryMember><gml:geometryMember><gml:LineString><gml:posList>0 0 1 1</gml:posList></gml:LineString></gml:geometryMember>", "MultiGeometry", "<Point><coordinates>1,2</coordinates></Point><LineString><coordinates>0,0 1,1</coordinates></LineString>", DisplayName = "the collection anchor")]
    public void TheCanonicalDocumentsOfOneValueAgreeAcrossEveryReader(string wellKnownText, string geoJsonDocument, string gmlLocalName, string gmlContent, string kmlLocalName, string kmlContent)
    {
        using FlatGeometry anchor = FromWkt(wellKnownText);

        using FlatGeometry fromGeoJson = ReadsGeoJson(geoJsonDocument);
        Assert.AreEqual(anchor, fromGeoJson, "the GeoJSON reader materializes the anchor value");

        using FlatGeometry fromGml = GmlAssert.Accepts(GmlTestDocuments.Root(gmlLocalName, gmlContent), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(anchor, fromGml, "the GML reader materializes the anchor value");

        using FlatGeometry fromKml = KmlAssert.Accepts(KmlTestDocuments.Root(kmlLocalName, kmlContent));
        Assert.AreEqual(anchor, fromKml, "the KML reader materializes the anchor value");
    }

    /// <summary>
    /// The KML kind collapse is the contract, not an accident: a typed multi and
    /// its collection twin emit one identical document, the document reads back
    /// as the collection with the typed kind genuinely lost — while the GeoJSON
    /// and GML pairs preserve the very kind KML collapses.
    /// </summary>
    [TestMethod]
    public void TheKmlKindCollapseIsTheContractNotAnAccident()
    {
        using FlatGeometry multi = FromWkt("MULTIPOINT ((1 2), (3 4))");
        using FlatGeometry collection = FromWkt("GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4))");

        string fromMulti = WritesKml(in multi);
        Assert.AreEqual(WritesKml(in collection), fromMulti, "the typed multi and its collection twin emit one identical document");

        using FlatGeometry reread = KmlAssert.Accepts(fromMulti);
        Assert.AreEqual(collection, reread, "the collapsed document materializes the collection value");
        Assert.AreNotEqual(multi, reread, "the typed kind is genuinely lost through the pair");

        using FlatGeometry fromGeoJson = ReadsGeoJson(WritesGeoJson(in multi));
        Assert.AreEqual(multi, fromGeoJson, "the GeoJSON pair preserves the typed kind KML collapses");

        using FlatGeometry fromGml = GmlAssert.Accepts(WritesGml(in multi), CoordinateReferenceSystem.Crs84);
        Assert.AreEqual(multi, fromGml, "the GML pair preserves the typed kind KML collapses");
    }

    /// <summary>
    /// The collapse binds at member position through the pair: a collection
    /// holding a typed multi and a collection holding the collection twin emit
    /// one identical nested document, and it reads back as the nested
    /// collection value.
    /// </summary>
    [TestMethod]
    public void TheCollapseBindsAtMemberPositionThroughThePair()
    {
        using FlatGeometry nestedMulti = FromWkt("GEOMETRYCOLLECTION (MULTIPOINT ((1 2), (3 4)))");
        using FlatGeometry nestedCollection = FromWkt("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (1 2), POINT (3 4)))");

        string fromNestedMulti = WritesKml(in nestedMulti);
        Assert.AreEqual(WritesKml(in nestedCollection), fromNestedMulti, "the member-position collapse emits the identical nested document");

        using FlatGeometry reread = KmlAssert.Accepts(fromNestedMulti);
        Assert.AreEqual(nestedCollection, reread, "the nested document materializes the collection-of-collection value");
    }

    /// <summary>
    /// A typed-empty member following a part-carrying member reads to one value
    /// from the well-known-text and GeoJSON readers and round-trips through the
    /// GeoJSON pair — the one codec pair able to carry it (GML refuses the empty
    /// primitive on write and KML has no empty form at all).
    /// </summary>
    [TestMethod]
    public void AnEmptyMemberAfterACarryingMemberAgreesAcrossItsReaders()
    {
        using FlatGeometry value = FromWkt("GEOMETRYCOLLECTION (POINT (1 2), POINT EMPTY)");

        using FlatGeometry fromGeoJson = ReadsGeoJson("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"Point\",\"coordinates\":[]}]}");
        Assert.AreEqual(value, fromGeoJson, "the GeoJSON reader materializes the anchor value");

        using FlatGeometry roundTripped = ReadsGeoJson(WritesGeoJson(in value));
        Assert.AreEqual(value, roundTripped, "the GeoJSON pair preserves the value structurally and bitwise");
    }

    /// <summary>
    /// A mixed-altitude collection rides every format with the dimension marked
    /// per member in each format's own vocabulary: the GeoJSON member carries a
    /// third position element, GML declares the dimension on exactly the
    /// altitude-carrying carriers, and KML emits the absolute mode on exactly
    /// the altitude-carrying members.
    /// </summary>
    [TestMethod]
    public void AMixedAltitudeCollectionMarksExactlyItsCarryingMembers()
    {
        using FlatGeometry value = FromWkt("GEOMETRYCOLLECTION (POINT (1 2), POINT Z (3 4 5), LINESTRING Z (0 0 1, 1 1 2))");

        string geoJson = WritesGeoJson(in value);
        Assert.Contains("\"coordinates\":[1,2]}", geoJson, "the planar member emits exactly its two position elements");
        Assert.Contains("[3,4,5]", geoJson, "the altitude-carrying point emits its third element");
        Assert.Contains("[[0,0,1],[1,1,2]]", geoJson, "the altitude-carrying line emits its third elements");

        string gml = WritesGml(in value);
        Assert.AreEqual(2, CountOf(gml, "srsDimension=\"3\""), "the dimension declaration rides exactly the two altitude-carrying carriers");

        string kml = WritesKml(in value);
        Assert.AreEqual(2, CountOf(kml, "<altitudeMode>absolute</altitudeMode>"), "the absolute mode rides exactly the two altitude-carrying members");
    }

    /// <summary>
    /// One double corpus emits identical number bytes from all four writers: the
    /// number text extracted from the well-known-text emission reassembles every
    /// other format's canonical document exactly, negative zero and the extreme
    /// magnitudes included.
    /// </summary>
    [TestMethod]
    [DataRow("-0", DisplayName = "negative zero")]
    [DataRow("0.1", DisplayName = "a plain fraction")]
    [DataRow("24.9384", DisplayName = "a longitude-grade fraction")]
    [DataRow("-1234.5678", DisplayName = "a signed fraction")]
    [DataRow("3.141592653589793", DisplayName = "a seventeen-significant-digit value")]
    [DataRow("5E-324", DisplayName = "the smallest subnormal")]
    [DataRow("1.7976931348623157E+308", DisplayName = "the largest finite double")]
    [DataRow("1E+21", DisplayName = "a value whose shortest form is exponent notation")]
    public void EveryWriterEmitsIdenticalNumberBytes(string token)
    {
        using FlatGeometry point = FromWkt($"POINT ({token} {token})");

        string wkt = WktGeometryWriter.WriteString(in point);
        int open = wkt.IndexOf('(', StringComparison.Ordinal) + 1;
        int separator = wkt.IndexOf(' ', open);
        string number = wkt[open..separator];
        Assert.AreEqual($"POINT ({number} {number})", wkt, "the well-known-text writer emits the number twice in its canonical frame");
        Assert.AreEqual(token, number, "the emitted number is the corpus spelling — the shortest round-trip form");

        Assert.AreEqual("{\"type\":\"Point\",\"coordinates\":[" + number + "," + number + "]}", WritesGeoJson(in point), "the GeoJSON writer emits the identical number bytes");
        Assert.AreEqual("<gml:Point" + GmlRootAttributes + "><gml:pos>" + number + " " + number + "</gml:pos></gml:Point>", WritesGml(in point), "the GML writer emits the identical number bytes");
        Assert.AreEqual("<Point" + KmlRootAttributes + "><coordinates>" + number + "," + number + "</coordinates></Point>", WritesKml(in point), "the KML writer emits the identical number bytes");
    }

    /// <summary>Parses the well-known-text fixture into the model value.</summary>
    private static FlatGeometry FromWkt(string wellKnownText)
    {
        bool parsed = WktGeometryReader.TryRead(Encoding.UTF8.GetBytes(wellKnownText), out FlatGeometry geometry, out _);
        Assert.IsTrue(parsed, $"the fixture '{wellKnownText}' must parse");

        return geometry;
    }

    /// <summary>Writes the value as GeoJSON, asserting the write succeeds.</summary>
    private static string WritesGeoJson(in FlatGeometry geometry)
    {
        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);
        Assert.IsTrue(written, $"the value must write as GeoJSON, but refused {refusal.Kind} at {refusal.ByteOffset}");

        return text;
    }

    /// <summary>Writes the value as GML under the CRS84 system, asserting the write succeeds.</summary>
    private static string WritesGml(in FlatGeometry geometry)
    {
        bool written = GmlGeometryWriter.TryWriteString(in geometry, CoordinateReferenceSystem.Crs84, out string text, out GeometryCodecRefusal refusal);
        Assert.IsTrue(written, $"the value must write as GML, but refused {refusal.Kind} at {refusal.ByteOffset}");

        return text;
    }

    /// <summary>Writes the value as KML, asserting the write succeeds.</summary>
    private static string WritesKml(in FlatGeometry geometry)
    {
        bool written = KmlGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);
        Assert.IsTrue(written, $"the value must write as KML, but refused {refusal.Kind} at {refusal.ByteOffset}");

        return text;
    }

    /// <summary>Reads a GeoJSON document, asserting acceptance and the no-offense sentinel.</summary>
    private static FlatGeometry ReadsGeoJson(string document)
    {
        bool accepted = GeoJsonGeometryReader.TryRead(Encoding.UTF8.GetBytes(document), out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        Assert.IsTrue(accepted, $"'{document}' must be accepted, but refused {refusal.Kind} at {refusal.ByteOffset}");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "an accepted read reports the no-offense sentinel");

        return geometry;
    }

    /// <summary>Counts the non-overlapping occurrences of a marker in the emitted text.</summary>
    private static int CountOf(string text, string marker)
    {
        int count = 0;
        int index = text.IndexOf(marker, StringComparison.Ordinal);
        while(index >= 0)
        {
            count++;
            index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
