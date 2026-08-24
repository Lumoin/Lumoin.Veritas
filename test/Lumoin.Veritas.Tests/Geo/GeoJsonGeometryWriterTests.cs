using System.Buffers;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.Json.Stj;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The RFC 7946 writer of the codec family: the canonical byte form (no
/// whitespace, type member first, shortest-round-trip invariant numbers),
/// the writer as a fixed point over its own canon, bit-exact number carriage
/// including negative zero, the degradation of an uninitialized carrier, and
/// the refusal contract — every refusal decided before the first destination
/// write, so a refused call leaves the destination untouched, with the one
/// caller-contract exception thrown rather than refused.
/// </summary>
[TestClass]
internal sealed class GeoJsonGeometryWriterTests
{

    /// <summary>A parsed well-known-text fixture writes to its canonical GeoJSON byte form: no whitespace, the type member first, and no digits beyond the shortest round-trip form.</summary>
    [TestMethod]
    [DataRow("POINT (1 2)", "{\"type\":\"Point\",\"coordinates\":[1,2]}", DisplayName = "a point")]
    [DataRow("POINT Z (1 2 3)", "{\"type\":\"Point\",\"coordinates\":[1,2,3]}", DisplayName = "a point carrying Z")]
    [DataRow("POINT EMPTY", "{\"type\":\"Point\",\"coordinates\":[]}", DisplayName = "the typed empty point")]
    [DataRow("LINESTRING (1 2, 3 4)", "{\"type\":\"LineString\",\"coordinates\":[[1,2],[3,4]]}", DisplayName = "a linestring")]
    [DataRow("POLYGON ((0 0, 1 0, 1 1, 0 0))", "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}", DisplayName = "a polygon")]
    [DataRow("POLYGON ((0 0, 3 0, 3 3, 0 0), (1 1, 2 1, 2 2, 1 1))", "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[3,0],[3,3],[0,0]],[[1,1],[2,1],[2,2],[1,1]]]}", DisplayName = "a polygon with a hole")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", "{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],[3,4]]}", DisplayName = "a multipoint")]
    [DataRow("MULTILINESTRING ((0 0, 1 1))", "{\"type\":\"MultiLineString\",\"coordinates\":[[[0,0],[1,1]]]}", DisplayName = "a multilinestring")]
    [DataRow("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((2 2, 3 2, 3 3, 2 2)))", "{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]],[[[2,2],[3,2],[3,3],[2,2]]]]}", DisplayName = "a multipolygon")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "{\"type\":\"GeometryCollection\",\"geometries\":[]}", DisplayName = "the typed empty collection")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2))", "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}]}", DisplayName = "a collection of one member")]
    [DataRow("GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (3 4, 5 6))", "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"LineString\",\"coordinates\":[[3,4],[5,6]]}]}", DisplayName = "a collection of two members")]
    [DataRow("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (1 2)), POINT (3 4))", "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]}]},{\"type\":\"Point\",\"coordinates\":[3,4]}]}", DisplayName = "a nested collection closes its members in order")]
    [DataRow("POINT (-0 0)", "{\"type\":\"Point\",\"coordinates\":[-0,0]}", DisplayName = "negative zero survives the writer")]
    [DataRow("POLYGON ((0 0, 0 1, 1 1, 0 0))", "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[0,1],[1,1],[0,0]]]}", DisplayName = "a clockwise exterior ring survives verbatim")]
    [DataRow("POINT Z (1 2 -1234.5678)", "{\"type\":\"Point\",\"coordinates\":[1,2,-1234.5678]}", DisplayName = "a below-ellipsoid height writes with its sign")]
    [DataRow("POINT (24.9384 0.1)", "{\"type\":\"Point\",\"coordinates\":[24.9384,0.1]}", DisplayName = "no digits beyond the shortest round-trip form")]
    public void WriterEmitsTheCanonicalForm(string wellKnownText, string expected)
    {
        bool parsed = WktGeometryReader.TryRead(wellKnownText, out FlatGeometry geometry, out _);

        Assert.IsTrue(parsed, $"the fixture '{wellKnownText}' must parse");

        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);

        Assert.IsTrue(written, $"'{wellKnownText}' must write");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "a written geometry reports the success sentinel");
        Assert.AreEqual(expected, text, "the canonical form");
    }

    /// <summary>Every document in the canonical corpus reads and rewrites to itself — the writer is a fixed point over its own canon.</summary>
    [TestMethod]
    public void CanonicalTextIsAWriterFixedPoint()
    {
        foreach(string canonical in GeoJsonTestDocuments.CanonicalCorpus)
        {
            bool parsed = GeoJsonGeometryReader.TryRead(canonical, out FlatGeometry geometry, out GeometryCodecRefusal readRefusal);

            Assert.IsTrue(parsed, $"'{canonical}' must parse");
            Assert.AreEqual(GeometryCodecRefusal.None, readRefusal, "the canon parses without refusal");

            bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out _);

            Assert.IsTrue(written, $"'{canonical}' must write");
            Assert.AreEqual(canonical, text, "canonical text rewrites to itself");
        }
    }

    /// <summary>A parsed document, rewritten, and reparsed preserves the model structurally and bitwise.</summary>
    [TestMethod]
    public void TextModelTextPreservesTheModelStructurally()
    {
        const string Canonical = "{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2,3]},{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}]}";
        bool parsedFirst = GeoJsonGeometryReader.TryRead(Canonical, out FlatGeometry first, out _);

        Assert.IsTrue(parsedFirst, "the fixture must parse");

        bool written = GeoJsonGeometryWriter.TryWriteString(in first, out string text, out _);

        Assert.IsTrue(written, "the fixture must write");

        bool parsedSecond = GeoJsonGeometryReader.TryRead(text, out FlatGeometry second, out _);

        Assert.IsTrue(parsedSecond, "the written text must parse back");
        Assert.AreEqual(first, second, "the round trip preserves the model structurally and bitwise");
    }

    /// <summary>
    /// Doubles built from a deterministic sixty-four-bit sweep round-trip bit
    /// for bit through the writer and reader: bit patterns rather than value
    /// ranges, so a sign-normalizing or precision-losing writer cannot hide
    /// behind value equality. The sweep skips the bit patterns that decode to
    /// a non-finite double — no format in this family carries one.
    /// </summary>
    [TestMethod]
    public void ScrambledDoubleBitsRoundTripBitExactly()
    {
        ulong state = 1207334451UL;

        for(int iteration = 0; iteration < 500; iteration++)
        {
            double x = BitConverter.Int64BitsToDouble(unchecked((long)DeterministicBitMixer.NextBitPattern(ref state)));
            double y = BitConverter.Int64BitsToDouble(unchecked((long)DeterministicBitMixer.NextBitPattern(ref state)));

            if(!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            FlatGeometry geometry = PointOf(x, y);
            bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out _);

            Assert.IsTrue(written, "the point must write");

            bool parsed = GeoJsonGeometryReader.TryRead(text, out FlatGeometry reparsed, out _);

            Assert.IsTrue(parsed, $"'{text}' must parse back");
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(reparsed.Vertices[0].X), "X round-trips bit for bit");
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(reparsed.Vertices[0].Y), "Y round-trips bit for bit");
        }
    }

    /// <summary>The extreme fixed fixtures — signed zeros, the extreme finite magnitudes, and the smallest subnormals — round-trip bit for bit through the writer and reader.</summary>
    [TestMethod]
    [DataRow(-0.0, 0.0, DisplayName = "signed zeros")]
    [DataRow(double.MaxValue, double.MinValue, DisplayName = "the extreme magnitudes")]
    [DataRow(double.Epsilon, -double.Epsilon, DisplayName = "the smallest subnormals")]
    public void ExtremeFixturesRoundTripBitExactly(double x, double y)
    {
        FlatGeometry geometry = PointOf(x, y);
        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out _);

        Assert.IsTrue(written, "the point must write");

        bool parsed = GeoJsonGeometryReader.TryRead(text, out FlatGeometry reparsed, out _);

        Assert.IsTrue(parsed, $"'{text}' must parse back");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(reparsed.Vertices[0].X), "X round-trips bit for bit");
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(y), BitConverter.DoubleToInt64Bits(reparsed.Vertices[0].Y), "Y round-trips bit for bit");
    }

    /// <summary>Every document in the canonical corpus writes as internet-JSON-clean text: every byte below 0x80, and no member name repeated within any one object.</summary>
    [TestMethod]
    public void EmittedTextIsInternetJsonClean()
    {
        //The two I-JSON properties a geometry writer can actually break: every byte
        //below 0x80, and no member name repeated within any one object.
        foreach(string canonical in GeoJsonTestDocuments.CanonicalCorpus)
        {
            bool parsed = GeoJsonGeometryReader.TryRead(canonical, out FlatGeometry geometry, out _);

            Assert.IsTrue(parsed, $"'{canonical}' must parse");

            var destination = new ArrayBufferWriter<byte>();
            bool written = GeoJsonGeometryWriter.TryWrite(in geometry, destination, out _);

            Assert.IsTrue(written, $"'{canonical}' must write");

            InternetJsonAssert.IsClean(destination.WrittenSpan);
        }
    }

    /// <summary>The writer never wraps a non-collection kind in a gratuitous GeometryCollection.</summary>
    [TestMethod]
    [DataRow("POINT (1 2)", DisplayName = "a point is not wrapped")]
    [DataRow("MULTIPOINT ((1 2), (3 4))", DisplayName = "a multipoint is not wrapped")]
    public void NoGratuitousCollectionWrapping(string wellKnownText)
    {
        bool parsed = WktGeometryReader.TryRead(wellKnownText, out FlatGeometry geometry, out _);

        Assert.IsTrue(parsed, $"'{wellKnownText}' must parse");

        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out _);

        Assert.IsTrue(written, $"'{wellKnownText}' must write");
        Assert.DoesNotContain("GeometryCollection", text, StringComparison.Ordinal, "the writer never wraps a non-collection kind");
    }

    /// <summary>An uninitialized (default) carrier degrades to the typed empty collection rather than refusing, and the round trip lands on that typed empty value.</summary>
    [TestMethod]
    public void AnUninitialisedCarrierDegradesToTheEmptyCollection()
    {
        FlatGeometry geometry = default;
        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);

        Assert.IsTrue(written, "the uninitialised carrier must write");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "the degradation is not a refusal");
        Assert.AreEqual(GeoJsonTestDocuments.CanonicalEmptyCollection, text, "the uninitialised carrier degrades to the empty collection");

        bool parsed = GeoJsonGeometryReader.TryRead(text, out FlatGeometry reparsed, out _);

        Assert.IsTrue(parsed, "the degraded text must parse back");
        Assert.AreEqual(FlatGeometry.Empty(GeometryKind.GeometryCollection), reparsed, "the round trip lands on the typed empty collection, not on the default carrier");
    }

    /// <summary>A measured geometry refuses with the measure-unrepresentable kind, naming no input byte and yielding no text — no format in this family carries a measure.</summary>
    [TestMethod]
    public void AMeasuredGeometryRefuses()
    {
        bool parsed = WktGeometryReader.TryRead("POINT M (1 2 3)", out FlatGeometry geometry, out _);

        Assert.IsTrue(parsed, "the measured fixture must parse");
        Assert.IsTrue(geometry.IsMeasured, "the fixture must actually carry a measure");

        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out string text, out GeometryCodecRefusal refusal);

        Assert.IsFalse(written, "a measured geometry must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MeasureUnrepresentable, refusal.Kind, "no format in this family carries a measure");
        Assert.AreEqual(-1, refusal.ByteOffset, "a writer refusal names no input byte");
        Assert.AreEqual(string.Empty, text, "a refused write yields no text");
    }

    /// <summary>A declared Z slot holding a non-finite value has no encoding in any codec format, so the writer refuses rather than emit a hole.</summary>
    [TestMethod]
    public void ANonFiniteOrdinateUnderADeclaringNodeRefuses()
    {
        //A declared Z whose slot is not a number has no encoding in any codec format; the
        //model can express it, so the writer must refuse rather than emit a hole.
        var builder = new FlatGeometryBuilder();
        builder.AddVertex(new Point2d(1, 2), double.NaN, double.NaN);
        builder.AddPart(new FlatGeometryPart(0, 1, FlatGeometryPartRole.Point));
        builder.RootIndex = builder.AddNode(GeometryKind.Point, hasZ: true, hasM: false, firstPart: 0, partCount: 1);

        FlatGeometry geometry = builder.ToGeometry();
        bool written = GeoJsonGeometryWriter.TryWriteString(in geometry, out _, out GeometryCodecRefusal refusal);

        Assert.IsFalse(written, "a NaN slot under a declaring node must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.NonFiniteCoordinate, refusal.Kind, "the ordinate is the offense");
        Assert.AreEqual(-1, refusal.ByteOffset, "a writer refusal names no input byte");
    }

    /// <summary>The writer enforces the same certified nesting bound its own reader enforces, so it never emits a document its reader would refuse.</summary>
    [TestMethod]
    public void ModelNestingBeyondTheCertifiedBoundRefuses()
    {
        //Without a writer-side bound the writers would emit documents their own readers
        //refuse, and the round-trip certification would be false for constructible input.
        FlatGeometry atBound = NestedCollections(31);
        FlatGeometry beyondBound = NestedCollections(32);

        bool writtenAtBound = GeoJsonGeometryWriter.TryWriteString(in atBound, out _, out GeometryCodecRefusal boundRefusal);
        bool writtenBeyond = GeoJsonGeometryWriter.TryWriteString(in beyondBound, out _, out GeometryCodecRefusal beyondRefusal);

        Assert.IsTrue(writtenAtBound, "thirty-one wrapping collections must write");
        Assert.AreEqual(GeometryCodecRefusal.None, boundRefusal, "the bound itself is not a refusal");
        Assert.IsFalse(writtenBeyond, "thirty-two wrapping collections must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.NestingTooDeep, beyondRefusal.Kind, "the bound is the offense");
    }

    /// <summary>A refused write leaves the destination bytes exactly as they were, appending nothing.</summary>
    [TestMethod]
    public void ARefusedWriteLeavesTheDestinationUntouched()
    {
        var destination = new ArrayBufferWriter<byte>();
        destination.Write("PRELOADED"u8);
        byte[] before = destination.WrittenSpan.ToArray();

        bool parsed = WktGeometryReader.TryRead("POINT M (1 2 3)", out FlatGeometry geometry, out _);

        Assert.IsTrue(parsed, "the measured fixture must parse");

        bool written = GeoJsonGeometryWriter.TryWrite(in geometry, destination, out GeometryCodecRefusal refusal);

        Assert.IsFalse(written, "the measured geometry must refuse");
        Assert.AreEqual(GeometryCodecRefusalKind.MeasureUnrepresentable, refusal.Kind, "the measure is the offense");
        Assert.AreEqual(before.Length, destination.WrittenCount, "a refused write appends nothing");
        Assert.IsTrue(before.AsSpan().SequenceEqual(destination.WrittenSpan), "a refused write leaves the destination bytes unchanged");
    }

    /// <summary>A null destination is a caller-contract violation and throws rather than refusing.</summary>
    [TestMethod]
    public void ANullDestinationIsACallerContractViolation()
    {
        bool parsed = WktGeometryReader.TryRead("POINT (1 2)", out FlatGeometry geometry, out _);

        Assert.IsTrue(parsed, "the fixture must parse");

        try
        {
            GeoJsonGeometryWriter.TryWrite(in geometry, destination: null!, out _);
            Assert.Fail("a null destination must throw rather than refuse");
        }
        catch(ArgumentNullException)
        {
            //Expected.
        }
    }

    /// <summary>Builds a planar point carrying the given ordinates.</summary>
    private static FlatGeometry PointOf(double x, double y)
    {
        var builder = new FlatGeometryBuilder();
        builder.AddVertex(new Point2d(x, y));
        builder.AddPart(new FlatGeometryPart(0, 1, FlatGeometryPartRole.Point));
        builder.RootIndex = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, firstPart: 0, partCount: 1);

        return builder.ToGeometry();
    }

    /// <summary>Builds the given number of nested collections around one point leaf.</summary>
    private static FlatGeometry NestedCollections(int depth)
    {
        var builder = new FlatGeometryBuilder();
        builder.AddVertex(new Point2d(1, 2));
        builder.AddPart(new FlatGeometryPart(0, 1, FlatGeometryPartRole.Point));
        int current = builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, firstPart: 0, partCount: 1);

        for(int index = 0; index < depth; index++)
        {
            int collection = builder.AddNode(GeometryKind.GeometryCollection, hasZ: false, hasM: false, firstPart: builder.PartCount, partCount: 0);
            builder.SetChildren(collection, [current]);
            current = collection;
        }

        builder.RootIndex = current;

        return builder.ToGeometry();
    }
}
