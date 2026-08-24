using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// Closeness and betweenness centrality over a <see cref="ColumnarTripleIndex"/> by unweighted breadth-first
/// search: exact values on graphs with closed forms — a path, a star, a triangle — pin the BFS distances and
/// Brandes accumulation.
/// </summary>
[TestClass]
internal sealed class ColumnarCentralityTests
{
    /// <summary>The MSTest-supplied per-test context, used for its cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A term id from its raw encoded value.</summary>
    /// <param name="value">The encoded id.</param>
    /// <returns>The term id.</returns>
    private static TermId Id(uint value)
    {
        return TermId.FromEncoded(value);
    }

    /// <summary>A path 1-2-3 (two edges).</summary>
    /// <returns>The built index.</returns>
    private static ColumnarTripleIndex Path()
    {
        return ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
        ]);
    }

    /// <summary>A star: centre 1 with leaves 2, 3, 4.</summary>
    /// <returns>The built index.</returns>
    private static ColumnarTripleIndex Star()
    {
        return ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 4),
        ]);
    }

    /// <summary>A triangle on 1, 2, 3.</summary>
    /// <returns>The built index.</returns>
    private static ColumnarTripleIndex Triangle()
    {
        return ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
        ]);
    }

    /// <summary>On a path the middle node is nearest to the rest; the ends are farther.</summary>
    [TestMethod]
    public void ClosenessOnAPath()
    {
        ColumnarGraphAnalytics analytics = new(Path());

        IReadOnlyDictionary<TermId, double> closeness = analytics.ClosenessCentrality(GraphProjection.AllPredicates(), TestContext.CancellationToken);

        Assert.AreEqual(2.0 / 3.0, closeness[Id(1)], 1e-9, "End node 1 reaches two nodes at total distance 3.");
        Assert.AreEqual(1.0, closeness[Id(2)], 1e-9, "Middle node 2 reaches two nodes at total distance 2.");
        Assert.AreEqual(2.0 / 3.0, closeness[Id(3)], 1e-9, "End node 3 mirrors node 1.");
    }

    /// <summary>On a path only the middle node lies on a shortest path between the ends.</summary>
    [TestMethod]
    public void BetweennessOnAPath()
    {
        ColumnarGraphAnalytics analytics = new(Path());

        IReadOnlyDictionary<TermId, double> betweenness = analytics.BetweennessCentrality(GraphProjection.AllPredicates(), TestContext.CancellationToken);

        Assert.AreEqual(0.0, betweenness[Id(1)], 1e-9);
        Assert.AreEqual(1.0, betweenness[Id(2)], 1e-9, "The single shortest path 1-3 passes through 2.");
        Assert.AreEqual(0.0, betweenness[Id(3)], 1e-9);
    }

    /// <summary>The star centre is one hop from every leaf; a leaf is two hops from the others.</summary>
    [TestMethod]
    public void ClosenessOnAStar()
    {
        ColumnarGraphAnalytics analytics = new(Star());

        IReadOnlyDictionary<TermId, double> closeness = analytics.ClosenessCentrality(GraphProjection.AllPredicates(), TestContext.CancellationToken);

        Assert.AreEqual(1.0, closeness[Id(1)], 1e-9, "The centre reaches three leaves at total distance 3.");
        Assert.AreEqual(3.0 / 5.0, closeness[Id(2)], 1e-9, "A leaf reaches the centre at 1 and the two other leaves at 2 each.");
        Assert.AreEqual(3.0 / 5.0, closeness[Id(3)], 1e-9);
        Assert.AreEqual(3.0 / 5.0, closeness[Id(4)], 1e-9);
    }

    /// <summary>Every shortest path between two leaves passes through the star centre.</summary>
    [TestMethod]
    public void BetweennessOnAStar()
    {
        ColumnarGraphAnalytics analytics = new(Star());

        IReadOnlyDictionary<TermId, double> betweenness = analytics.BetweennessCentrality(GraphProjection.AllPredicates(), TestContext.CancellationToken);

        Assert.AreEqual(3.0, betweenness[Id(1)], 1e-9, "All three leaf pairs route through the centre.");
        Assert.AreEqual(0.0, betweenness[Id(2)], 1e-9);
        Assert.AreEqual(0.0, betweenness[Id(3)], 1e-9);
        Assert.AreEqual(0.0, betweenness[Id(4)], 1e-9);
    }

    /// <summary>In a triangle every node is one hop from the others and lies on no other pair's shortest path.</summary>
    [TestMethod]
    public void TriangleHasUniformCentrality()
    {
        ColumnarGraphAnalytics analytics = new(Triangle());
        GraphProjection all = GraphProjection.AllPredicates();

        IReadOnlyDictionary<TermId, double> closeness = analytics.ClosenessCentrality(all, TestContext.CancellationToken);
        IReadOnlyDictionary<TermId, double> betweenness = analytics.BetweennessCentrality(all, TestContext.CancellationToken);

        foreach(uint node in new uint[] { 1, 2, 3 })
        {
            Assert.AreEqual(1.0, closeness[Id(node)], 1e-9, "Each node reaches the other two at distance 1.");
            Assert.AreEqual(0.0, betweenness[Id(node)], 1e-9, "Every pair is directly connected, so no node is a bridge.");
        }
    }

    /// <summary>On a path the middle node's eigenvector centrality peaks; the equal ends are smaller by a factor of sqrt(2).</summary>
    [TestMethod]
    public void EigenvectorCentralityOnAPath()
    {
        ColumnarGraphAnalytics analytics = new(Path());

        IReadOnlyDictionary<TermId, double> centrality = analytics.EigenvectorCentrality(GraphProjection.AllPredicates());

        Assert.AreEqual(0.5, centrality[Id(1)], 1e-9, "The two ends share the smaller centrality.");
        Assert.AreEqual(1.0 / Math.Sqrt(2.0), centrality[Id(2)], 1e-9, "The middle node is the principal eigenvector's peak.");
        Assert.AreEqual(0.5, centrality[Id(3)], 1e-9, "The far end mirrors the near end.");
    }

    /// <summary>The star centre's eigenvector centrality is sqrt(3) times each leaf's; the leaves are equal. The graph is bipartite, so this exercises the convergence shift.</summary>
    [TestMethod]
    public void EigenvectorCentralityOnAStar()
    {
        ColumnarGraphAnalytics analytics = new(Star());

        IReadOnlyDictionary<TermId, double> centrality = analytics.EigenvectorCentrality(GraphProjection.AllPredicates());

        Assert.AreEqual(1.0 / Math.Sqrt(2.0), centrality[Id(1)], 1e-9, "The centre carries the peak centrality.");
        Assert.AreEqual(1.0 / Math.Sqrt(6.0), centrality[Id(2)], 1e-9, "Each leaf is sqrt(3) times smaller than the centre.");
        Assert.AreEqual(1.0 / Math.Sqrt(6.0), centrality[Id(3)], 1e-9);
        Assert.AreEqual(1.0 / Math.Sqrt(6.0), centrality[Id(4)], 1e-9);
    }

    /// <summary>In a triangle every node has equal eigenvector centrality.</summary>
    [TestMethod]
    public void EigenvectorCentralityIsUniformOnATriangle()
    {
        ColumnarGraphAnalytics analytics = new(Triangle());

        IReadOnlyDictionary<TermId, double> centrality = analytics.EigenvectorCentrality(GraphProjection.AllPredicates());

        double expected = 1.0 / Math.Sqrt(3.0);
        Assert.AreEqual(expected, centrality[Id(1)], 1e-9);
        Assert.AreEqual(expected, centrality[Id(2)], 1e-9);
        Assert.AreEqual(expected, centrality[Id(3)], 1e-9);
    }

    /// <summary>An empty graph has no nodes and so no centrality entries.</summary>
    [TestMethod]
    public void EmptyGraphHasNoCentrality()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build([]));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.IsEmpty(analytics.ClosenessCentrality(all, TestContext.CancellationToken));
        Assert.IsEmpty(analytics.BetweennessCentrality(all, TestContext.CancellationToken));
        Assert.IsEmpty(analytics.EigenvectorCentrality(all));
    }
}
