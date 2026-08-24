using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The boundary family of the geometry substrate: canonical-per-dimension result
/// kinds with the endpoint-parity rule for lineal input, no cardinality collapse, and
/// the undefined answer for heterogeneous collections.
/// </summary>
[TestClass]
internal sealed class GeometryBoundaryTests
{
    /// <summary>The boundary answers the canonical result kind of the input's dimension.</summary>
    /// <param name="text">The WKT text under test.</param>
    /// <param name="expected">The expected boundary text.</param>
    [TestMethod]
    [DataRow("POINT(1 2)", "GEOMETRYCOLLECTION EMPTY")]
    [DataRow("POINT EMPTY", "GEOMETRYCOLLECTION EMPTY")]
    [DataRow("MULTIPOINT((1 2), (3 4))", "GEOMETRYCOLLECTION EMPTY")]
    [DataRow("LINESTRING(0 0, 1 1, 2 0)", "MULTIPOINT ((0 0), (2 0))")]
    [DataRow("LINESTRING(0 0, 1 1, 1 0, 0 0)", "MULTIPOINT EMPTY")]
    [DataRow("LINESTRING EMPTY", "MULTIPOINT EMPTY")]
    [DataRow("MULTILINESTRING((0 0, 1 1), (1 1, 2 0))", "MULTIPOINT ((0 0), (2 0))")]
    [DataRow("MULTILINESTRING((1 1, 0 0), (1 1, 2 2), (1 1, 2 0))", "MULTIPOINT ((0 0), (1 1), (2 0), (2 2))")]
    [DataRow("POLYGON((0 0, 4 0, 4 4, 0 0))", "MULTILINESTRING ((0 0, 4 0, 4 4, 0 0))")]
    [DataRow("POLYGON((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 1))", "MULTILINESTRING ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 2 1, 2 2, 1 1))")]
    [DataRow("POLYGON EMPTY", "MULTILINESTRING EMPTY")]
    [DataRow("MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", "MULTILINESTRING ((0 0, 1 0, 1 1, 0 0), (5 5, 6 5, 6 6, 5 5))")]
    public void BoundaryAnswersTheCanonicalKindPerDimension(string text, string expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(text, out FlatGeometry geometry, out _), $"'{text}' must parse.");
        Assert.IsTrue(GeometryBoundary.TryCompute(in geometry, out FlatGeometry boundary), "Boundary is defined for non-collection kinds.");

        Assert.AreEqual(expected, WktGeometryWriter.WriteString(in boundary),
            "Puntal answers the empty collection; lineal the parity multipoint; polygonal every ring as a multilinestring.");
    }

    /// <summary>A valence-two shared endpoint is interior under the parity rule.</summary>
    [TestMethod]
    public void SharedEndpointWithEvenValenceDropsFromTheBoundary()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("MULTILINESTRING((0 0, 1 1), (1 1, 2 0))", out FlatGeometry geometry, out _));
        Assert.IsTrue(GeometryBoundary.TryCompute(in geometry, out FlatGeometry boundary));

        Assert.DoesNotContain("1 1", WktGeometryWriter.WriteString(in boundary), System.StringComparison.Ordinal,
            "A valence-two endpoint is interior under the parity rule.");
    }

    /// <summary>The boundary of a heterogeneous collection answers false, never throws.</summary>
    [TestMethod]
    public void CollectionBoundaryIsUndefined()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("GEOMETRYCOLLECTION(POINT(1 2))", out FlatGeometry geometry, out _));

        Assert.IsFalse(GeometryBoundary.TryCompute(in geometry, out _),
            "The boundary of a heterogeneous collection is undefined and answers false, never throws.");
    }
}
