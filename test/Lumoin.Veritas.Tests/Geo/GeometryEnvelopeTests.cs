using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The envelope family of the geometry substrate: bounds fold over every position,
/// the query is undefined on empties (no sentinel box), and the geometry-returning
/// variant applies the degenerate collapse with a counter-clockwise shell.
/// </summary>
[TestClass]
internal sealed class GeometryEnvelopeTests
{
    /// <summary>The bounds fold min and max over every member's positions.</summary>
    [TestMethod]
    public void BoundsFoldOverEveryPosition()
    {
        Assert.IsTrue(WktGeometryReader.TryRead(
            "GEOMETRYCOLLECTION(POINT(-5 40), LINESTRING(0 0, 10 3), POLYGON((1 1, 2 1, 2 8, 1 1)))",
            out FlatGeometry geometry, out _));

        Assert.IsTrue(GeometryEnvelope.TryComputeBounds(in geometry, out BoundingBox bounds), "A non-empty geometry has bounds.");
        Assert.AreEqual(new BoundingBox(-5, 0, 10, 40), bounds, "The bounds fold min/max over all members.");
    }

    /// <summary>The bounds query is undefined on the empty point set.</summary>
    /// <param name="text">The WKT text under test.</param>
    [TestMethod]
    [DataRow("POINT EMPTY")]
    [DataRow("MULTIPOLYGON EMPTY")]
    [DataRow("GEOMETRYCOLLECTION(POINT EMPTY)")]
    public void BoundsAreUndefinedOnEmpties(string text)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.IsFalse(GeometryEnvelope.TryComputeBounds(in geometry, out _),
            "The empty point set has no bounds — no sentinel box exists in this model.");
    }

    /// <summary>The envelope geometry applies the degenerate collapse.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The expected envelope geometry text.</param>
    [TestMethod]
    [DataRow("POINT EMPTY", "POINT EMPTY")]
    [DataRow("POINT(3 4)", "POINT (3 4)")]
    [DataRow("MULTIPOINT((2 1), (2 9))", "LINESTRING (2 1, 2 9)")]
    [DataRow("LINESTRING(0 0, 10 5)", "POLYGON ((0 0, 10 0, 10 5, 0 5, 0 0))")]
    public void EnvelopeGeometryAppliesTheDegenerateCollapse(string text, string expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");

        FlatGeometry envelope = GeometryEnvelope.ComputeEnvelopeGeometry(in geometry);

        Assert.AreEqual(expected, WktGeometryWriter.WriteString(in envelope),
            "Empty input collapses to the empty point, degenerate boxes to point/linestring, real boxes to the counter-clockwise ring.");
    }
}
