using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The shared result-construction discipline: the promoted builder's
/// breadth-first layout and loud bookkeeping throws, and the factory growth the
/// constructive set rides — polygon with holes, multipolygon positional roles,
/// collection assembly — every result heap-backed, planar XY, and round-trippable
/// through the writer.
/// </summary>
[TestClass]
internal sealed class FlatGeometryBuilderTests
{
    /// <summary>A polygon with a hole writes canonically and round-trips bitwise.</summary>
    [TestMethod]
    public void PolygonWithHoleRoundTrips()
    {
        Point2d[] shell = [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)];
        Point2d[] hole = [new(2, 2), new(2, 4), new(4, 4), new(4, 2), new(2, 2)];

        FlatGeometry polygon = FlatGeometryFactory.CreatePolygon([shell, hole]);

        Assert.AreEqual(
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 4, 4 4, 4 2, 2 2))",
            WktGeometryWriter.WriteString(in polygon),
            "The shell-then-holes ring list writes canonically.");
        Assert.IsTrue(WktGeometryReader.TryRead(WktGeometryWriter.WriteString(in polygon), out FlatGeometry reparsed, out _), "The written form must parse.");
        Assert.IsTrue(polygon.Equals(reparsed), "Build, write, parse closes bitwise.");
    }

    /// <summary>A multipolygon carries the positional role convention across members.</summary>
    [TestMethod]
    public void MultiPolygonCarriesThePositionalRoleConvention()
    {
        Point2d[] firstShell = [new(0, 0), new(4, 0), new(4, 4), new(0, 0)];
        Point2d[] firstHole = [new(1, 1), new(1, 2), new(2, 2), new(1, 1)];
        Point2d[] secondShell = [new(10, 0), new(14, 0), new(14, 4), new(10, 0)];

        FlatGeometry multi = FlatGeometryFactory.CreateMultiPolygon([[firstShell, firstHole], [secondShell]]);

        Assert.AreEqual(
            "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 0), (1 1, 1 2, 2 2, 1 1)), ((10 0, 14 0, 14 4, 10 0)))",
            WktGeometryWriter.WriteString(in multi),
            "Every exterior ring opens a polygon; interior rings follow theirs.");
        Assert.AreEqual(FlatGeometryPartRole.ExteriorRing, multi.Parts[2].Role, "The second polygon's shell re-opens with the exterior role.");
    }

    /// <summary>Collection assembly lays members breadth-first under one shared column.</summary>
    [TestMethod]
    public void CollectionAssemblyLaysMembersBreadthFirst()
    {
        FlatGeometry point = FlatGeometryFactory.CreatePoint(new Point2d(1, 1));
        FlatGeometry line = FlatGeometryFactory.CreateLineString([new(0, 0), new(1, 0)]);
        Point2d[] shell = [new(5, 5), new(6, 5), new(6, 6), new(5, 5)];
        FlatGeometry polygon = FlatGeometryFactory.CreatePolygon([shell]);

        FlatGeometry collection = FlatGeometryFactory.CreateCollection([point, line, polygon]);

        Assert.AreEqual(
            "GEOMETRYCOLLECTION (POINT (1 1), LINESTRING (0 0, 1 0), POLYGON ((5 5, 6 5, 6 6, 5 5)))",
            WktGeometryWriter.WriteString(in collection),
            "Members assemble in order under one shared column.");
        Assert.AreEqual(GeometryKind.GeometryCollection, collection.Nodes[0].Kind, "The root is the collection node.");
        Assert.AreEqual(1, collection.Nodes[0].FirstChild, "Children occupy the contiguous run after the root.");
        Assert.AreEqual(3, collection.Nodes[0].ChildCount, "Every member becomes one child node.");
    }

    /// <summary>Empty factory inputs collapse to typed empties.</summary>
    [TestMethod]
    public void EmptyInputsCollapseToTypedEmpties()
    {
        FlatGeometry polygon = FlatGeometryFactory.CreatePolygon([]);
        FlatGeometry multi = FlatGeometryFactory.CreateMultiPolygon([]);
        FlatGeometry collection = FlatGeometryFactory.CreateCollection([]);

        Assert.AreEqual("POLYGON EMPTY", WktGeometryWriter.WriteString(in polygon), "No rings is the typed empty polygon.");
        Assert.AreEqual("MULTIPOLYGON EMPTY", WktGeometryWriter.WriteString(in multi), "No polygons is the typed empty multipolygon.");
        Assert.AreEqual("GEOMETRYCOLLECTION EMPTY", WktGeometryWriter.WriteString(in collection), "No members is the typed empty collection.");
    }

    /// <summary>A collection member inside collection assembly is a bookkeeping violation and throws.</summary>
    [TestMethod]
    public void CollectionMembersMustNotBeCollections()
    {
        FlatGeometry inner = FlatGeometryFactory.CreateCollection([]);

        Assert.Throws<InvalidOperationException>(
            () => FlatGeometryFactory.CreateCollection([inner]),
            "A collection member inside collection assembly is a bookkeeping violation, not an input outcome.");
    }

    /// <summary>A part run escaping the vertex column throws loudly at build time.</summary>
    [TestMethod]
    public void EscapedPartRunThrowsLoudly()
    {
        var builder = new FlatGeometryBuilder();
        builder.AddVertex(new Point2d(0, 0));
        builder.AddPart(new FlatGeometryPart(0, 5, FlatGeometryPartRole.Line));
        builder.RootIndex = builder.AddNode(GeometryKind.LineString, hasZ: false, hasM: false, firstPart: 0, partCount: 1);

        Assert.Throws<InvalidOperationException>(
            () => builder.ToGeometry(),
            "A part run escaping the vertex column is the builder's own bookkeeping bug and throws loudly.");
    }

    /// <summary>An unset root index throws loudly at build time.</summary>
    [TestMethod]
    public void UnsetRootThrowsLoudly()
    {
        var builder = new FlatGeometryBuilder();
        builder.AddNode(GeometryKind.Point, hasZ: false, hasM: false, firstPart: 0, partCount: 0);

        Assert.Throws<InvalidOperationException>(
            () => builder.ToGeometry(),
            "An unset root index is the builder's own bookkeeping bug and throws loudly.");
    }

    /// <summary>Constructed results are planar XY and allocate no ordinate columns.</summary>
    [TestMethod]
    public void ConstructedResultsCarryNoOrdinates()
    {
        FlatGeometry polygon = FlatGeometryFactory.CreatePolygon([[new(0, 0), new(1, 0), new(1, 1), new(0, 0)]]);
        FlatGeometry collection = FlatGeometryFactory.CreateCollection([polygon]);

        Assert.IsFalse(collection.Is3D, "Constructed results carry no Z.");
        Assert.IsFalse(collection.IsMeasured, "Constructed results carry no M.");
        Assert.HasCount(0, collection.ZOrdinates, "No Z column is allocated on a constructed result.");
    }

    /// <summary>Collection assembly copies member columns rather than aliasing them.</summary>
    [TestMethod]
    public void CollectionAssemblyCopiesRatherThanAliases()
    {
        FlatGeometry member = FlatGeometryFactory.CreatePoint(new Point2d(7, 8));

        FlatGeometry collection = FlatGeometryFactory.CreateCollection([member]);
        member.Dispose();

        Assert.AreEqual(
            "GEOMETRYCOLLECTION (POINT (7 8))",
            WktGeometryWriter.WriteString(in collection),
            "The assembled collection owns fresh columns, never the member's.");
    }
}
