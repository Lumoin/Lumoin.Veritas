using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.Json.Stj;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The RFC 7946 reader of the codec family: the acceptance matrix over the
/// seven case-sensitive type tags, the rejection matrix with every refusal
/// kind live-fired, the offset rules (first offending byte; the byte at
/// which an absence or shortfall became inevitable; input length for
/// truncation), the free member order the RFC allows, the closed recognition
/// of the removed legacy crs member, the foreign-member and bbox
/// tolerances, and the certified nesting bound pinned at both sides.
/// </summary>
[TestClass]
internal sealed class GeoJsonGeometryReaderTests
{
    /// <summary>A well-formed document parses to its expected root kind, part count, and vertex count across the acceptance matrix of type tags, member orders, and tolerated foreign content.</summary>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "a point carries one part and one vertex")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2,3]}", GeometryKind.Point, 1, 1, DisplayName = "a three-element position carries Z")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[]}", GeometryKind.Point, 0, 0, DisplayName = "empty coordinates are the typed empty point")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[1,2],[3,4]]}", GeometryKind.LineString, 1, 2, DisplayName = "a linestring carries one part of two positions")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[]}", GeometryKind.LineString, 0, 0, DisplayName = "the typed empty linestring")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}", GeometryKind.Polygon, 1, 4, DisplayName = "a polygon carries its closed shell")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[3,0],[3,3],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}", GeometryKind.Polygon, 2, 8, DisplayName = "a polygon carries shell then hole")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[]}", GeometryKind.Polygon, 0, 0, DisplayName = "the typed empty polygon")]
    [DataRow("{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],[3,4]]}", GeometryKind.MultiPoint, 2, 2, DisplayName = "a multipoint carries one part per member")]
    [DataRow("{\"type\":\"MultiLineString\",\"coordinates\":[[[0,0],[1,1]]]}", GeometryKind.MultiLineString, 1, 2, DisplayName = "a multilinestring carries one part per member")]
    [DataRow("{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]]]}", GeometryKind.MultiPolygon, 1, 4, DisplayName = "a multipolygon carries its member rings")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[]}", GeometryKind.GeometryCollection, 0, 0, DisplayName = "the typed empty collection")]
    [DataRow("{\"coordinates\":[1,2],\"type\":\"Point\"}", GeometryKind.Point, 1, 1, DisplayName = "the type member may follow the coordinates")]
    [DataRow("{\"coordinates\":[[1,2],[3,4]],\"type\":\"LineString\"}", GeometryKind.LineString, 1, 2, DisplayName = "the type member may follow a linestring's coordinates")]
    [DataRow("{\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]],\"type\":\"Polygon\"}", GeometryKind.Polygon, 1, 4, DisplayName = "the type member may follow a polygon's coordinates")]
    [DataRow("{\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}],\"type\":\"GeometryCollection\"}", GeometryKind.GeometryCollection, 1, 1, DisplayName = "the type member may follow the geometries")]
    [DataRow("{\"geometries\":[],\"type\":\"GeometryCollection\"}", GeometryKind.GeometryCollection, 0, 0, DisplayName = "the typed empty collection with the type member last")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"coordinates\":[1,2],\"type\":\"Point\"}]}", GeometryKind.GeometryCollection, 1, 1, DisplayName = "a collection member may carry its type last")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[0,1],[1,1],[0,0]]]}", GeometryKind.Polygon, 1, 4, DisplayName = "a clockwise exterior ring parses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0,1],[1,0,1],[1,1,1],[0,0,9]]]}", GeometryKind.Polygon, 1, 4, DisplayName = "a ring closing on XY with a differing Z parses under the documented tolerance")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,1,2],\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "a valid bbox member is validated and discarded")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,0,1,2,10],\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "a six-element bbox member is validated and discarded")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[170,-20,-178,-16],\"coordinates\":[179,-18]}", GeometryKind.Point, 1, 1, DisplayName = "an antimeridian-spanning bbox with west exceeding east is accepted")]
    [DataRow("{\"type\":\"Point\",\"title\":\"anything\",\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "a foreign member is ignored")]
    [DataRow("{\"type\":\"Point\",\"id\":7,\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "an id member on a geometry stays foreign")]
    [DataRow("{\"type\":\"Point\",\"title\":\"a\",\"title\":\"b\",\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "a duplicated foreign member is tolerated")]
    [DataRow("{\"type\":\"Point\",\"centerline\":{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1]]},\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "a geometry-shaped foreign member is skipped whole")]
    [DataRow("{\"type\":\"Point\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}},\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "the legacy crs member naming CRS84 is tolerated")]
    [DataRow("{\"type\":\"Point\",\"crs\":{\"type\":\"name\",\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}},\"coordinates\":[1,2]}", GeometryKind.Point, 1, 1, DisplayName = "duplicates inside the crs object are not adjudicated")]
    public void WellFormedGeometryParsesToTheExpectedShape(string text, GeometryKind expectedKind, int expectedParts, int expectedVertices)
    {
        bool accepted = GeoJsonGeometryReader.TryRead(text, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsTrue(accepted, $"'{text}' must parse");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "an accepted document reports the success sentinel");
        Assert.AreEqual(expectedKind, geometry.Kind, "the root kind");
        Assert.HasCount(expectedParts, geometry.Parts.ToArray(), "the part count");
        Assert.HasCount(expectedVertices, geometry.Vertices.ToArray(), "the vertex count");
    }

    /// <summary>A three-element position carries Z in the Z column while a two-element position carries none, and no reader in this family ever produces a measure.</summary>
    [TestMethod]
    public void ThreeElementPositionsCarryZAndTwoElementPositionsDoNot()
    {
        bool acceptedThree = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2,3]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry three, out _);
        bool acceptedTwo = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry two, out _);

        Assert.IsTrue(acceptedThree, "the three-element position must parse");
        Assert.IsTrue(acceptedTwo, "the two-element position must parse");
        Assert.IsTrue(three.Is3D, "a three-element position carries Z");
        Assert.AreEqual(3.0, three.ZOrdinates[0], "the third element lands in the Z column");
        Assert.IsFalse(two.Is3D, "a two-element position carries no Z");
        Assert.IsFalse(three.IsMeasured, "no reader in this family ever produces a measure");
    }

    /// <summary>Each collection member materializes its own node, so a mixed-dimension collection is representable even though one coordinate carrier must be uniform in itself.</summary>
    [TestMethod]
    public void CollectionMembersMayDifferInDimension()
    {
        //Each collection member materializes its own node, so per-node carriage makes a
        //mixed-dimension collection representable; only one coordinate carrier must be
        //uniform in itself.
        const string Text = "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"Point\",\"coordinates\":[3,4,5]}]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsTrue(accepted, $"'{Text}' must parse");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "an accepted document reports the success sentinel");
        Assert.HasCount(3, geometry.Nodes.ToArray(), "the collection and its two members");
        Assert.IsFalse(geometry.Nodes[1].HasZ, "the first member carries no Z");
        Assert.IsTrue(geometry.Nodes[2].HasZ, "the second member carries Z");
    }

    /// <summary>Every rejection matrix row refuses with its pinned refusal kind: case-sensitive type tags, structural violations, dimension mismatches, non-finite coordinates, unrecognized coordinate reference systems, and malformed documents.</summary>
    [TestMethod]
    [DataRow("{\"type\":\"point\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the type tag is case sensitive")]
    [DataRow("{\"type\":\"linestring\",\"coordinates\":[[1,2],[3,4]]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the linestring tag is case sensitive")]
    [DataRow("{\"type\":\"polygon\",\"coordinates\":[]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the polygon tag is case sensitive")]
    [DataRow("{\"type\":\"multipoint\",\"coordinates\":[]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the multipoint tag is case sensitive")]
    [DataRow("{\"type\":\"multilinestring\",\"coordinates\":[]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the multilinestring tag is case sensitive")]
    [DataRow("{\"type\":\"multipolygon\",\"coordinates\":[]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the multipolygon tag is case sensitive")]
    [DataRow("{\"type\":\"geometrycollection\",\"geometries\":[]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "the geometrycollection tag is case sensitive")]
    [DataRow("{\"type\":\"POINT\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "an uppercase tag refuses")]
    [DataRow("{\"type\":\"MultiLineString\",\"coordinates\":[[[1,2]]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a one-position member inside a multilinestring refuses")]
    [DataRow("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[1,2]}}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "a feature envelope refuses")]
    [DataRow("{\"type\":\"FeatureCollection\",\"features\":[]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "a feature collection refuses")]
    [DataRow("{\"type\":\"Circle\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.UnsupportedGeometry, DisplayName = "an unknown type tag refuses")]
    [DataRow("{\"type\":\"Point\"}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a geometry without coordinates refuses")]
    [DataRow("{\"coordinates\":[1,2]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a geometry without a type refuses")]
    [DataRow("{\"type\":\"GeometryCollection\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a collection carrying coordinates refuses")]
    [DataRow("{\"type\":\"Point\",\"geometries\":[]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a leaf carrying geometries refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "a one-element position refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2,3,4]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "a fourth position element refuses")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[1,2,3],[4,5]]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "non-uniform arity within one carrier refuses")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[1,2]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a one-position linestring refuses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[0,0]]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a three-position ring refuses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[9,9]]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "an unclosed ring refuses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[3,0],[3,3],[0,0]],[[1,1],[2,1],[1,1]]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a three-position hole refuses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[3,0],[3,3],[0,0]],[[1,1],[2,1],[2,2],[9,9]]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "an unclosed hole refuses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[0,0],[1,0],[1,1],[0,0]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a polygon whose ring list carries bare positions refuses")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "an over-nested polygon refuses")]
    [DataRow("{\"type\":5,\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a numeric type value refuses")]
    [DataRow("{\"type\":null,\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a null type value refuses")]
    [DataRow("{\"type\":true,\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a boolean type value refuses")]
    [DataRow("{\"type\":[\"Point\"],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "an array type value refuses")]
    [DataRow("{\"type\":{\"name\":\"Point\"},\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "an object type value refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,1,2],\"bbox\":[1,2,1,2],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a duplicated bbox member refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":\"nonsense\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a bbox value that is not an array refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,3],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "a three-element bbox array refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,3,4,5],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "a five-element bbox array refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,3,4,5,6,7],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "a seven-element bbox array refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,\"2\",3,4],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a non-number bbox element refuses")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1,2,3,4,5,6,\"7\"],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a seventh non-number bbox element refuses as structure before length")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[1e999,2,3,4],\"coordinates\":[1,2]}", GeometryCodecRefusalKind.NonFiniteCoordinate, DisplayName = "an overflowing bbox value refuses")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"bbox\":[1,2,3],\"coordinates\":[1,2]}]}", GeometryCodecRefusalKind.DimensionMismatch, DisplayName = "a defective bbox on a nested collection member refuses")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[5]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a non-object geometries element refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"properties\":{}}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a geometry carrying properties refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"geometry\":{\"type\":\"Point\",\"coordinates\":[3,4]}}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a geometry carrying a geometry member refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"features\":[]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a geometry carrying features refuses")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[],\"properties\":{}}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a collection carrying properties refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]/*placeholder*/}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a block comment refuses")]
    [DataRow("{/*placeholder*/\"type\":\"Point\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a leading block comment refuses")]
    [DataRow("{\"type\":\"Point\",//comment\n\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a line comment refuses")]
    [DataRow("{\"type\":\"MultiLineString\",\"coordinates\":[[]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "an empty member inside a multi kind refuses")]
    [DataRow("{\"type\":\"MultiPolygon\",\"coordinates\":[[]]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "an empty polygon inside a multipolygon refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":\"1,2\"}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "a non-array coordinates member refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[\"1\",\"2\"]}", GeometryCodecRefusalKind.StructuralViolation, DisplayName = "quoted ordinates refuse")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1e999,2]}", GeometryCodecRefusalKind.NonFiniteCoordinate, DisplayName = "an overflowing ordinate refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"type\":\"Point\"}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a duplicated recognized member refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"coordinates\":[3,4]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "duplicated coordinates refuse")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a truncated document refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a trailing comma refuses")]
    [DataRow("[1,2]", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a bare array root refuses")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]} trailing", GeometryCodecRefusalKind.TrailingContent, DisplayName = "content after the geometry refuses")]
    [DataRow("{\"type\":\"Point\",\"crs\":null,\"coordinates\":[1,2]}", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, DisplayName = "a null crs member refuses")]
    [DataRow("{\"type\":\"Point\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:EPSG::3857\"}},\"coordinates\":[1,2]}", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, DisplayName = "a crs member naming another system refuses")]
    [DataRow("{\"type\":\"Point\",\"crs\":{\"type\":\"link\",\"properties\":{\"href\":\"http://example.invalid/crs\"}},\"coordinates\":[1,2]}", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, DisplayName = "a link-form crs member refuses")]
    [DataRow("{\"type\":\"Point\",\"crs\":\"CRS84\",\"coordinates\":[1,2]}", GeometryCodecRefusalKind.MalformedDocument, DisplayName = "a non-object non-null crs member refuses")]
    public void MalformedGeometryRefusesWithTheExpectedKind(string text, GeometryCodecRefusalKind expectedKind)
    {
        bool accepted = GeoJsonGeometryReader.TryRead(text, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, $"'{text}' must refuse");
        Assert.AreEqual(expectedKind, refusal.Kind, $"'{text}' must refuse with the pinned kind");
        Assert.AreEqual(default, geometry, "a refused read yields the default carrier");
    }

    /// <summary>The zero-length input refuses as a malformed document at byte offset zero.</summary>
    [TestMethod]
    public void TheEmptyInputRefusesAtTheFirstByte()
    {
        bool accepted = GeoJsonGeometryReader.TryRead(ReadOnlySpan<byte>.Empty, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the zero-length input must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MalformedDocument, refusal.Kind, "the zero-length input is a transport violation");
        Assert.AreEqual(0, refusal.ByteOffset, "the zero-length input reports offset zero");
    }

    /// <summary>A byte-order mark ahead of the document refuses as a malformed document at its own first byte.</summary>
    [TestMethod]
    public void AByteOrderMarkRefusesAtTheFirstByte()
    {
        byte[] document = [0xEF, 0xBB, 0xBF, .. "{\"type\":\"Point\",\"coordinates\":[1,2]}"u8];
        bool accepted = GeoJsonGeometryReader.TryRead(document, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "a byte-order mark must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MalformedDocument, refusal.Kind, "the mark is a transport violation");
        Assert.AreEqual(0, refusal.ByteOffset, "the mark refuses at its own first byte");
    }

    /// <summary>Content following a complete geometry refuses with the trailing-content kind at the first non-whitespace byte after the geometry.</summary>
    [TestMethod]
    public void TrailingContentRefusesAtItsFirstByte()
    {
        const string Text = "{\"type\":\"Point\",\"coordinates\":[1,2]} trailing";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "content after the geometry must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.TrailingContent, refusal.Kind, "the kind names the trailing content");
        Assert.AreEqual(Text.IndexOf('t', Text.IndexOf('}', StringComparison.Ordinal)), refusal.ByteOffset, "the offset names the first non-whitespace byte after the geometry");
    }

    /// <summary>A one-element position refuses at the byte that closes the array and makes the missing second element certain — an absence has no offending byte of its own.</summary>
    [TestMethod]
    public void AShortPositionRefusesWhereTheShortfallBecameInevitable()
    {
        //An absence has no offending byte of its own; the offset names the byte that
        //closed the run and made the shortfall certain.
        const string Text = "{\"type\":\"Point\",\"coordinates\":[1]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "a one-element position must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.DimensionMismatch, refusal.Kind, "the kind names the dimension");
        Assert.AreEqual(Text.IndexOf(']', StringComparison.Ordinal), refusal.ByteOffset, "the offset names the byte closing the position");
    }

    /// <summary>A fourth position element refuses with the dimension-mismatch kind at its own byte.</summary>
    [TestMethod]
    public void AFourthPositionElementRefusesAtItsOwnByte()
    {
        const string Text = "{\"type\":\"Point\",\"coordinates\":[1,2,3,4]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "a fourth element must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.DimensionMismatch, refusal.Kind, "the kind names the dimension");
        Assert.AreEqual(Text.LastIndexOf('4'), refusal.ByteOffset, "the offset names the offending element itself");
    }

    /// <summary>A five-element bbox array refuses at the bracket that closes it, where the shortfall to six elements becomes inevitable — the array could still have grown to six until that byte.</summary>
    [TestMethod]
    public void AShortBoundingBoxRefusesAtItsClosingBracket()
    {
        //A five-element array could still have grown to six, so the shortfall
        //becomes inevitable only at the bracket that closes it.
        const string Text = "{\"type\":\"Point\",\"bbox\":[1,2,3,4,5],\"coordinates\":[1,2]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "a five-element bbox must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.DimensionMismatch, refusal.Kind, "the kind names the length");
        Assert.AreEqual(Text.IndexOf(']', StringComparison.Ordinal), refusal.ByteOffset, "the offset names the byte closing the array");
    }

    /// <summary>A non-number bbox element refuses with the structural-violation kind at its own byte.</summary>
    [TestMethod]
    public void ANonNumberBoundingBoxElementRefusesAtItsOwnByte()
    {
        const string Text = "{\"type\":\"Point\",\"bbox\":[1,\"2\",3,4],\"coordinates\":[1,2]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "a non-number bbox element must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.StructuralViolation, refusal.Kind, "the kind names the structure");
        Assert.AreEqual(Text.IndexOf("\"2\"", StringComparison.Ordinal), refusal.ByteOffset, "the offset names the offending element itself");
    }

    /// <summary>A document truncated mid-array refuses as malformed at the input length, where the missing byte would have appeared.</summary>
    [TestMethod]
    public void ATruncatedDocumentRefusesAtTheInputLength()
    {
        const string Text = "{\"type\":\"Point\",\"coordinates\":[1,2]";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "a truncated document must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MalformedDocument, refusal.Kind, "truncation is a transport violation");
        Assert.AreEqual(Text.Length, refusal.ByteOffset, "the offset names where the missing byte would have appeared");
    }

    /// <summary>When a document carries two defects, the earlier one in document order decides both the reported kind and the reported offset.</summary>
    [TestMethod]
    public void TheEarlierOffenseWinsWhenDefectsAreMixed()
    {
        //Two defects, the dimension one earlier in document order: the reported refusal
        //names the earlier byte, so mixed-defect documents are deterministic.
        const string Text = "{\"type\":\"LineString\",\"coordinates\":[[1],[2,3],[4,5],[6,7,8]]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the mixed-defect document must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.DimensionMismatch, refusal.Kind, "the earlier defect decides the kind");
        Assert.AreEqual(Text.IndexOf(']', StringComparison.Ordinal), refusal.ByteOffset, "the earlier defect decides the offset");
    }

    /// <summary>A shape offense that is only decidable once the type is known still reports at its own document-order byte inside the coordinates, not at the type member that revealed it.</summary>
    [TestMethod]
    public void AKindDependentOffenseReportsInsideTheCoordinatesEvenWhenTheTypeArrivesLast()
    {
        //The shape offense is only decidable once the type is known, but it is reported
        //at its own document-order byte, not at the type member that revealed it.
        const string Text = "{\"coordinates\":[[0,0],[1,0],[0,0]],\"type\":\"Polygon\"}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "the shape offense must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.StructuralViolation, refusal.Kind, "the ring shape is the offense");
        Assert.IsLessThanOrEqualTo(Text.IndexOf("\"type\"", StringComparison.Ordinal), refusal.ByteOffset, "the offense reports inside the coordinates, before the type member");
    }

    /// <summary>The character-span convenience overload reports offsets indexed into the transcoded UTF-8 form, which diverges from the character index once non-ASCII content precedes the offense.</summary>
    [TestMethod]
    public void CharacterOverloadOffsetsIndexTheTranscodedForm()
    {
        //The convenience overload transcodes, so the reported offset indexes UTF-8 bytes
        //and diverges from the character index once non-ASCII precedes the offense.
        const string Text = "{\"nöte\":\"ä\",\"type\":\"Point\",\"coordinates\":[1]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out _, out GeometryCodecRefusal refusal);
        int characterIndex = Text.IndexOf(']', StringComparison.Ordinal);
        int byteIndex = Encoding.UTF8.GetByteCount(Text.AsSpan(0, characterIndex));

        Assert.IsFalse(accepted, "the short position must refuse");
        Assert.AreNotEqual(characterIndex, byteIndex, "the fixture must actually differ between the two frames");
        Assert.AreEqual(byteIndex, refusal.ByteOffset, "the offset indexes the transcoded UTF-8 form");
    }

    /// <summary>A document and its member-order permutation parse to the same value, bitwise coordinates included, across every geometry kind.</summary>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]}", "{\"coordinates\":[1,2],\"type\":\"Point\"}", DisplayName = "a point permuted")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[1,2],[3,4]]}", "{\"coordinates\":[[1,2],[3,4]],\"type\":\"LineString\"}", DisplayName = "a linestring permuted")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}", "{\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]],\"type\":\"Polygon\"}", DisplayName = "a polygon permuted")]
    [DataRow("{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],[3,4]]}", "{\"coordinates\":[[1,2],[3,4]],\"type\":\"MultiPoint\"}", DisplayName = "a multipoint permuted")]
    [DataRow("{\"type\":\"MultiLineString\",\"coordinates\":[[[0,0],[1,1]]]}", "{\"coordinates\":[[[0,0],[1,1]]],\"type\":\"MultiLineString\"}", DisplayName = "a multilinestring permuted")]
    [DataRow("{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]]]}", "{\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]]],\"type\":\"MultiPolygon\"}", DisplayName = "a multipolygon permuted")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}]}", "{\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}],\"type\":\"GeometryCollection\"}", DisplayName = "a collection permuted")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]}", "{\"bbox\":[1,2,1,2],\"coordinates\":[1,2],\"title\":\"anything\",\"type\":\"Point\"}", DisplayName = "interleaved recognized and foreign members permute freely")]
    public void MemberOrderIsIrrelevantForEveryKind(string canonical, string permuted)
    {
        //The literal expression of the clause: the permuted document parses to the SAME
        //VALUE as the canonical one, bitwise coordinates included — counts alone would
        //let a mis-bound kind slip through on shape.
        bool parsedCanonical = GeoJsonGeometryReader.TryRead(canonical, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry first, out _);
        bool parsedPermuted = GeoJsonGeometryReader.TryRead(permuted, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry second, out _);

        Assert.IsTrue(parsedCanonical, $"'{canonical}' must parse");
        Assert.IsTrue(parsedPermuted, $"'{permuted}' must parse");
        Assert.AreEqual(first, second, "member order must be irrelevant to the parsed value");
    }

    /// <summary>The first ring of a polygon is the exterior and every later ring is interior, assigned by position, with ring order carrying verbatim into the vertex column.</summary>
    [TestMethod]
    public void PolygonRingRolesAreAssignedPositionally()
    {
        const string Text = "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[3,0],[3,3],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out _);

        Assert.IsTrue(accepted, "the two-ring polygon must parse");
        Assert.AreEqual(FlatGeometryPartRole.ExteriorRing, geometry.Parts[0].Role, "the first ring is the exterior");
        Assert.AreEqual(FlatGeometryPartRole.InteriorRing, geometry.Parts[1].Role, "every later ring is interior");
        Assert.AreEqual(4, geometry.Parts[1].Start, "ring order carries verbatim into the vertex column");
    }

    /// <summary>A position's first element lands in X, its second in Y, and its third in Z — longitude, latitude, and altitude in that order.</summary>
    [TestMethod]
    public void PositionElementsCarryLongitudeLatitudeAltitudeInOrder()
    {
        //The row that turns red on a longitude/latitude reinterpretation every
        //shape-count assertion survives.
        bool accepted = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2,3]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out _);

        Assert.IsTrue(accepted, "the position must parse");
        Assert.AreEqual(1.0, geometry.Vertices[0].X, "the first element is the longitude and lands in X");
        Assert.AreEqual(2.0, geometry.Vertices[0].Y, "the second element is the latitude and lands in Y");
        Assert.AreEqual(3.0, geometry.ZOrdinates[0], "the third element is the altitude and lands in Z");
    }

    /// <summary>A negative altitude below the ellipsoid keeps its sign and magnitude bit for bit through the reader.</summary>
    [TestMethod]
    public void ABelowEllipsoidHeightCarriesItsSignAndMagnitudeBitForBit()
    {
        //Every other Z fixture is the integer 3, a fixed point of Math.Abs and of float
        //narrowing; this row is the one that catches both.
        bool accepted = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2,-1234.5678]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out _);

        Assert.IsTrue(accepted, "the below-ellipsoid position must parse");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(-1234.5678), BitConverter.DoubleToInt64Bits(geometry.ZOrdinates[0]), "a below-ellipsoid height keeps its sign and magnitude bit for bit");
    }

    /// <summary>Differing digit counts and exponent spellings that denote the same double value parse to the identical value.</summary>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1.0,2.000]}", DisplayName = "trailing zeros carry no meaning")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1.00000000000000000000,2e0]}", DisplayName = "digit count carries no meaning")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1E0,2.0e+0]}", DisplayName = "exponent spellings carry no meaning")]
    public void DigitCountCarriesNoSemantics(string variant)
    {
        bool parsedReference = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry reference, out _);
        bool parsedVariant = GeoJsonGeometryReader.TryRead(variant, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry parsed, out _);

        Assert.IsTrue(parsedReference, "the reference must parse");
        Assert.IsTrue(parsedVariant, $"'{variant}' must parse");
        Assert.AreEqual(reference, parsed, "the digit count changes the value only, never an uncertainty reading");
    }

    /// <summary>A digit that changes the represented double value changes the parsed bits.</summary>
    [TestMethod]
    public void ADigitThatChangesTheValueChangesTheParse()
    {
        bool parsedReference = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry reference, out _);
        bool parsedVariant = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1.0000000000000002,2]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry parsed, out _);

        Assert.IsTrue(parsedReference, "the reference must parse");
        Assert.IsTrue(parsedVariant, "the variant must parse");
        Assert.AreNotEqual(BitConverter.DoubleToInt64Bits(reference.Vertices[0].X), BitConverter.DoubleToInt64Bits(parsed.Vertices[0].X), "a digit that changes the value changes the bits");
    }

    /// <summary>A foreign member of any shape — scalar, geometry-shaped, array-valued, or duplicated — never changes the semantics of any recognized member.</summary>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"title\":\"anything\",\"coordinates\":[1,2]}", DisplayName = "a scalar foreign member")]
    [DataRow("{\"type\":\"Point\",\"meta\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]},\"coordinates\":[1,2]}", DisplayName = "a geometry-shaped foreign member is skipped whole")]
    [DataRow("{\"type\":\"Point\",\"tags\":[{\"a\":1},[2,3],\"x\"],\"coordinates\":[1,2]}", DisplayName = "an array-valued foreign member")]
    [DataRow("{\"type\":\"Point\",\"altitude\":9,\"coordinates\":[1,2]}", DisplayName = "a tempting altitude member sets no Z")]
    [DataRow("{\"type\":\"Point\",\"srid\":3857,\"coordinates\":[1,2]}", DisplayName = "a tempting srid member changes no system")]
    [DataRow("{\"type\":\"Point\",\"title\":\"a\",\"title\":\"b\",\"coordinates\":[1,2]}", DisplayName = "a duplicated foreign member")]
    [DataRow("{\"type\":\"Point\",\"bbox\":[9,9,9,9],\"coordinates\":[1,2]}", DisplayName = "a contradicting bbox is not honored")]
    public void ForeignMembersLeaveTheParseIdenticalToTheDocumentWithoutThem(string variant)
    {
        bool parsedBaseline = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry baseline, out _);
        bool parsedVariant = GeoJsonGeometryReader.TryRead(variant, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry parsed, out _);

        Assert.IsTrue(parsedBaseline, "the baseline must parse");
        Assert.IsTrue(parsedVariant, $"'{variant}' must parse");
        Assert.AreEqual(baseline, parsed, "a foreign member must not change the semantics of any recognized member");
    }

    /// <summary>A foreign member inside a collection member object does not change the parsed value.</summary>
    [TestMethod]
    public void AForeignMemberInsideACollectionMemberIsTolerated()
    {
        bool parsedBaseline = GeoJsonGeometryReader.TryRead("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry baseline, out _);
        bool parsedVariant = GeoJsonGeometryReader.TryRead("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"note\":\"x\",\"coordinates\":[1,2]}]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry parsed, out _);

        Assert.IsTrue(parsedBaseline, "the baseline must parse");
        Assert.IsTrue(parsedVariant, "the variant must parse");
        Assert.AreEqual(baseline, parsed, "a foreign member inside a member object must not change the parse");
    }

    /// <summary>All four JSON whitespace bytes between tokens, including trailing whitespace after the root, are insignificant to the parsed value.</summary>
    [TestMethod]
    [DataRow("{\n\t\"type\" : \"Point\" ,\n\t\"coordinates\" : [ 1 , 2 ]\n}", "{\"type\":\"Point\",\"coordinates\":[1,2]}", DisplayName = "pretty-printed whitespace is insignificant")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[ {\"type\":\"Point\",\"coordinates\":[1,2]} ]}\t\r\n", "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}]}", DisplayName = "trailing whitespace after the root is insignificant")]
    public void WhitespaceBetweenTokensIsInsignificant(string spaced, string minified)
    {
        bool parsedSpaced = GeoJsonGeometryReader.TryRead(spaced, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry first, out _);
        bool parsedMinified = GeoJsonGeometryReader.TryRead(minified, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry second, out _);

        Assert.IsTrue(parsedSpaced, "the spaced document must parse");
        Assert.IsTrue(parsedMinified, "the minified document must parse");
        Assert.AreEqual(first, second, "all four JSON whitespace bytes are insignificant at token boundaries");
    }

    /// <summary>Collection member order and multiplicity, duplicates included, carry verbatim into the node and vertex sequence.</summary>
    [TestMethod]
    public void CollectionMemberOrderAndMultiplicityCarryVerbatim()
    {
        //Three same-kind members, one duplicated, in non-ascending order: any dedup,
        //sort, promotion or unwrap changes the node count or the vertex sequence.
        const string Text = "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[3,4]},{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"Point\",\"coordinates\":[3,4]}]}";
        bool accepted = GeoJsonGeometryReader.TryRead(Text, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out _);

        Assert.IsTrue(accepted, "the collection must parse");
        Assert.HasCount(4, geometry.Nodes.ToArray(), "the collection and its three members, duplicate included");
        Assert.AreEqual(new Point2d(3, 4), geometry.Vertices[0], "member order carries verbatim");
        Assert.AreEqual(new Point2d(1, 2), geometry.Vertices[1], "member order carries verbatim");
        Assert.AreEqual(new Point2d(3, 4), geometry.Vertices[2], "the duplicate member survives");
    }

    /// <summary>An invalid UTF-8 sequence inside content the reader skips as foreign is never decoded or adjudicated, and the document parses identically to its clean twin.</summary>
    [TestMethod]
    public void InvalidUtf8InsideSkippedForeignContentIsNotAdjudicated()
    {
        //Pinned as fact, not aspiration: the tokenizer never decodes content it skips,
        //and the codec compares its recognized strings ordinally, so encoding validity
        //of string CONTENT is the lexical layer's seam — the structural layer
        //adjudicates numbers and structure only. A document whose sole flaw is an
        //invalid sequence inside skipped foreign content parses to the same value as
        //its clean twin.
        byte[] document = [.. "{\"type\":\"Point\",\"title\":\""u8, 0xFF, .. "\",\"coordinates\":[1,2]}"u8];
        bool accepted = GeoJsonGeometryReader.TryRead(document, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry parsed, out GeometryCodecRefusal refusal);
        bool parsedBaseline = GeoJsonGeometryReader.TryRead("{\"type\":\"Point\",\"coordinates\":[1,2]}", out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry baseline, out _);

        Assert.IsTrue(accepted, "skipped content is never decoded, so the sequence is not adjudicated here");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "the accepted document reports the success sentinel");
        Assert.IsTrue(parsedBaseline, "the baseline must parse");
        Assert.AreEqual(baseline, parsed, "the skipped content leaves the value untouched");
    }

    /// <summary>Thirty-one wrapping collections around a leaf parse at the certified nesting bound.</summary>
    [TestMethod]
    public void NestingAtTheCertifiedBoundParses()
    {
        string text = NestedCollections(31);
        bool accepted = GeoJsonGeometryReader.TryRead(text, out Lumoin.Veritas.Geo.SimpleFeatures.FlatGeometry geometry, out GeometryCodecRefusal refusal);

        Assert.IsTrue(accepted, "thirty-one wrapping collections around a leaf must parse");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "an accepted document reports the success sentinel");
        Assert.HasCount(32, geometry.Nodes.ToArray(), "the wrappers and the leaf");
    }

    /// <summary>Thirty-two wrapping collections exceed the certified nesting bound and refuse with the nesting-too-deep kind.</summary>
    [TestMethod]
    public void NestingBeyondTheCertifiedBoundRefuses()
    {
        string text = NestedCollections(32);
        bool accepted = GeoJsonGeometryReader.TryRead(text, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(accepted, "thirty-two wrapping collections must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.NestingTooDeep, refusal.Kind, "the bound is the offense");
    }

    /// <summary>Builds the given number of nested collections around one point leaf.</summary>
    private static string NestedCollections(int depth)
    {
        var builder = new StringBuilder();

        for(int index = 0; index < depth; index++)
        {
            builder.Append("{\"type\":\"GeometryCollection\",\"geometries\":[");
        }

        builder.Append("{\"type\":\"Point\",\"coordinates\":[1,2]}");

        for(int index = 0; index < depth; index++)
        {
            builder.Append("]}");
        }

        return builder.ToString();
    }
}
