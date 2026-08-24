using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The writer canon and round-trip families of the geometry substrate: exact
/// canonical strings per kind, idempotence over the canon corpus, structural
/// model→text→model equality, and bit-exact double round-trips including the explicit
/// negative-zero and extreme-magnitude fixtures asserted bitwise — value equality
/// would mask a sign-normalizing writer.
/// </summary>
[TestClass]
internal sealed class WktGeometryWriterTests
{
    /// <summary>The writer emits the canonical form for every kind, marker, and empty.</summary>
    /// <param name="input">The WKT text to parse.</param>
    /// <param name="canonical">The expected canonical emission.</param>
    [TestMethod]
    [DataRow("point( 1 2)", "POINT (1 2)")]
    [DataRow("POINT Z(1 2 3)", "POINT Z (1 2 3)")]
    [DataRow("point m ( 1 2 5 )", "POINT M (1 2 5)")]
    [DataRow("POINT ZM (1 2 3 4)", "POINT ZM (1 2 3 4)")]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    [DataRow("POINT Z EMPTY", "POINT Z EMPTY")]
    [DataRow("linestring(0 0,1 1,2 2)", "LINESTRING (0 0, 1 1, 2 2)")]
    [DataRow("LINESTRING EMPTY", "LINESTRING EMPTY")]
    [DataRow("POLYGON((0 0,0 10,10 10,0 0),(1 1,2 2,1 2,1 1))", "POLYGON ((0 0, 0 10, 10 10, 0 0), (1 1, 2 2, 1 2, 1 1))")]
    [DataRow("POLYGON EMPTY", "POLYGON EMPTY")]
    [DataRow("MULTIPOINT(1 2,3 4)", "MULTIPOINT ((1 2), (3 4))")]
    [DataRow("MULTIPOINT EMPTY", "MULTIPOINT EMPTY")]
    [DataRow("MULTILINESTRING((0 0,1 1),(2 2,3 3))", "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))")]
    [DataRow("MULTIPOLYGON(((0 0,1 0,1 1,0 0)),((5 5,6 5,6 6,5 5)))", "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [DataRow("MULTIPOLYGON EMPTY", "MULTIPOLYGON EMPTY")]
    [DataRow("GEOMETRYCOLLECTION(POINT(1 2),LINESTRING(0 0,1 1))", "GEOMETRYCOLLECTION (POINT (1 2), LINESTRING (0 0, 1 1))")]
    [DataRow("GEOMETRYCOLLECTION(POINT EMPTY)", "GEOMETRYCOLLECTION (POINT EMPTY)")]
    [DataRow("GEOMETRYCOLLECTION EMPTY", "GEOMETRYCOLLECTION EMPTY")]
    [DataRow("TIN(((0 0,1 0,0 1,0 0)))", "MULTIPOLYGON (((0 0, 1 0, 0 1, 0 0)))")]
    public void WriterEmitsTheCanonicalForm(string input, string canonical)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(input, out FlatGeometry geometry, out _), $"'{input}' must parse.");

        Assert.AreEqual(canonical, WktGeometryWriter.WriteString(in geometry),
            "The writer must emit the canonical form.");
    }

    /// <summary>Canonical text round-trips through the codec unchanged.</summary>
    [TestMethod]
    public void CanonicalTextIsAWriterFixedPoint()
    {
        string[] corpus =
        [
            "POINT (1 2)",
            "POINT Z (1 2 3)",
            "POINT EMPTY",
            "LINESTRING (0 0, 1 1, 2 2)",
            "POLYGON ((0 0, 0 10, 10 10, 0 0), (1 1, 2 2, 1 2, 1 1))",
            "MULTIPOINT ((1 2), (3 4))",
            "MULTILINESTRING ((0 0, 1 1), (2 2, 3 3))",
            "MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))",
            "GEOMETRYCOLLECTION (POINT (1 2), GEOMETRYCOLLECTION (LINESTRING (0 0, 1 1)))",
            "GEOMETRYCOLLECTION EMPTY",
        ];

        foreach(string text in corpus)
        {
            Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
            Assert.AreEqual(text, WktGeometryWriter.WriteString(in geometry), "Canonical text round-trips unchanged.");
        }
    }

    /// <summary>Writing and reparsing preserves the model structurally.</summary>
    [TestMethod]
    public void TextModelTextPreservesTheModelStructurally()
    {
        Assert.IsTrue(WktGeometryReader.TryRead(
            "GEOMETRYCOLLECTION(POINT ZM(1.5 -2.25 3 4), MULTIPOLYGON(((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 1))))",
            out FlatGeometry first, out _));

        string text = WktGeometryWriter.WriteString(in first);

        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry second, out _), "The writer's output must reparse.");
        Assert.AreEqual(first, second, "model → text → model is structurally identity.");
    }

    /// <summary>A fixed sweep of assorted double bit patterns round-trips bit-exactly.</summary>
    [TestMethod]
    public void MixedBitPatternDoublesRoundTripBitExactly()
    {
        //A deterministic bit-mixing sweep: raw entropy stays behind the house
        //randomness seams, and the fixture set is identical on every run.
        ulong state = 534287101UL;

        for(int iteration = 0; iteration < 500; iteration++)
        {
            double x = BitConverter.Int64BitsToDouble(NextBitPattern(ref state));
            double y = BitConverter.Int64BitsToDouble(NextBitPattern(ref state));

            if(!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            AssertPointRoundTripsBitExactly(x, y);
        }
    }

    /// <summary>Extreme-magnitude and signed-zero fixtures round-trip bit-exactly.</summary>
    /// <param name="x">The X coordinate fixture.</param>
    /// <param name="y">The Y coordinate fixture.</param>
    [TestMethod]
    [DataRow(-0.0, 0.0)]
    [DataRow(double.MaxValue, double.MinValue)]
    [DataRow(double.Epsilon, -double.Epsilon)]
    public void ExtremeFixturesRoundTripBitExactly(double x, double y)
    {
        AssertPointRoundTripsBitExactly(x, y);
    }

    /// <summary>The sign of negative zero survives the writer.</summary>
    [TestMethod]
    public void NegativeZeroSurvivesTheWriter()
    {
        FlatGeometry geometry = ParsePoint(-0.0, 1.0);

        Assert.Contains("-0", WktGeometryWriter.WriteString(in geometry), StringComparison.Ordinal,
            "The sign of negative zero must not normalize away.");
    }

    /// <summary>Builds a point geometry carrying the exact doubles, then asserts writer → reader is bitwise identity.</summary>
    private static void AssertPointRoundTripsBitExactly(double x, double y)
    {
        FlatGeometry geometry = ParsePoint(x, y);
        string text = WktGeometryWriter.WriteString(in geometry);

        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry reparsed, out _), $"'{text}' must reparse.");
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(x),
            BitConverter.DoubleToInt64Bits(reparsed.Vertices[0].X),
            $"X must round-trip bit-exactly through '{text}'.");
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(y),
            BitConverter.DoubleToInt64Bits(reparsed.Vertices[0].Y),
            $"Y must round-trip bit-exactly through '{text}'.");
    }

    /// <summary>Builds a point geometry carrying the exact doubles by parsing their shortest-round-trip text.</summary>
    private static FlatGeometry ParsePoint(double x, double y)
    {
        string text = FormattableString.Invariant($"POINT({x} {y})");

        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");

        return geometry;
    }

    /// <summary>Advances the deterministic bit-mixing state and returns the next 64-bit pattern.</summary>
    private static long NextBitPattern(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong mixed = state;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;

        return unchecked((long)(mixed ^ (mixed >> 31)));
    }
}
