using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// Exact-value tests for the directed and decomposition graph algorithms over a <see cref="ColumnarTripleIndex"/>'s
/// order columns: strongly connected components (Tarjan, directed), the k-core decomposition (min-degree peeling,
/// undirected), and unweighted single-source shortest paths (breadth-first, directed). Each pins the result on a
/// small graph whose structure makes the answer hand-checkable.
/// </summary>
[TestClass]
internal sealed class ColumnarGraphAlgorithmsTests
{
    /// <summary>The edge predicate the fixtures use.</summary>
    private const uint Predicate = 10;

    /// <summary>A term id from its raw encoded value.</summary>
    /// <param name="value">The encoded id.</param>
    /// <returns>The term id.</returns>
    private static TermId Id(uint value)
    {
        return TermId.FromEncoded(value);
    }

    /// <summary>An analytics view over a graph of directed <c>(subject, object)</c> edges under the single fixture predicate.</summary>
    /// <param name="edges">The directed edges as (subject, object) encoded-id pairs.</param>
    /// <returns>The analytics view.</returns>
    private static ColumnarGraphAnalytics Graph(params (uint Subject, uint Object)[] edges)
    {
        EncodedTriple[] triples = new EncodedTriple[edges.Length];
        for(int i = 0; i < edges.Length; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(edges[i].Subject, Predicate, edges[i].Object);
        }

        return new ColumnarGraphAnalytics(ColumnarTripleIndex.Build(triples));
    }

    /// <summary>A directed cycle is a single strongly connected component holding all its nodes.</summary>
    [TestMethod]
    public void StronglyConnectedComponentsCollapsesADirectedCycle()
    {
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 3), (3, 1));

        IReadOnlyList<IReadOnlyList<TermId>> components = analytics.StronglyConnectedComponents(GraphProjection.AllPredicates());

        Assert.HasCount(1, components, "The 1->2->3->1 cycle is one strong component.");
        AssertComponent(components[0], Id(1), Id(2), Id(3));
    }

    /// <summary>An acyclic directed chain has every node in its own singleton strong component.</summary>
    [TestMethod]
    public void StronglyConnectedComponentsAreSingletonsInADag()
    {
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 3));

        IReadOnlyList<IReadOnlyList<TermId>> components = analytics.StronglyConnectedComponents(GraphProjection.AllPredicates());

        Assert.HasCount(3, components, "The acyclic chain 1->2->3 has three singleton strong components.");
        Assert.HasCount(1, components[0]);
        Assert.AreEqual(Id(1), components[0][0], "Components are ordered by their smallest node.");
        Assert.AreEqual(Id(2), components[1][0]);
        Assert.AreEqual(Id(3), components[2][0]);
    }

    /// <summary>A one-way bridge between two cycles does not merge their strong components.</summary>
    [TestMethod]
    public void StronglyConnectedComponentsKeepCyclesSeparateAcrossAOneWayBridge()
    {
        //Cycle {1,2}, cycle {3,4}, and a one-way bridge 2->3. The bridge cannot be traversed back, so 3 and 4 are
        //not reachable-and-back from 1 and 2.
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 1), (3, 4), (4, 3), (2, 3));

        IReadOnlyList<IReadOnlyList<TermId>> components = analytics.StronglyConnectedComponents(GraphProjection.AllPredicates());

        Assert.HasCount(2, components, "The two two-cycles stay distinct across the one-way bridge.");
        AssertComponent(components[0], Id(1), Id(2));
        AssertComponent(components[1], Id(3), Id(4));
    }

    /// <summary>Asserts a component holds exactly the expected node ids in the given order.</summary>
    /// <param name="component">The component under test.</param>
    /// <param name="expected">The expected node ids, ascending.</param>
    private static void AssertComponent(IReadOnlyList<TermId> component, params TermId[] expected)
    {
        Assert.HasCount(expected.Length, component, "The component holds the expected number of nodes.");
        for(int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], component[i], $"Component member {i} matches.");
        }
    }

    /// <summary>Every node of a four-clique has core number three; the degeneracy is three.</summary>
    [TestMethod]
    public void CoreNumbersOfACompleteGraphAreOneLessThanItsSize()
    {
        //K4 over {1,2,3,4}: one directed edge per pair is enough — the k-core is undirected.
        ColumnarGraphAnalytics analytics = Graph((1, 2), (1, 3), (1, 4), (2, 3), (2, 4), (3, 4));

        IReadOnlyDictionary<TermId, long> core = analytics.CoreNumbers(GraphProjection.AllPredicates());

        Assert.AreEqual(3L, core[Id(1)], "Every node of K4 sits in the 3-core.");
        Assert.AreEqual(3L, core[Id(2)]);
        Assert.AreEqual(3L, core[Id(3)]);
        Assert.AreEqual(3L, core[Id(4)]);
    }

    /// <summary>A triangle's nodes have core two; a pendant attached to it drops to core one.</summary>
    [TestMethod]
    public void CoreNumbersSeparateATriangleFromItsPendant()
    {
        //Triangle {1,2,3} plus a pendant 4 attached to 1.
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 3), (1, 3), (1, 4));

        IReadOnlyDictionary<TermId, long> core = analytics.CoreNumbers(GraphProjection.AllPredicates());

        Assert.AreEqual(2L, core[Id(1)], "Triangle nodes sit in the 2-core.");
        Assert.AreEqual(2L, core[Id(2)]);
        Assert.AreEqual(2L, core[Id(3)]);
        Assert.AreEqual(1L, core[Id(4)], "The pendant sits only in the 1-core.");
    }

    /// <summary>Shortest paths along a directed chain are the hop counts; the source is at distance zero.</summary>
    [TestMethod]
    public void ShortestPathLengthsAlongADirectedChain()
    {
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 3), (3, 4));

        IReadOnlyDictionary<TermId, long> distance = analytics.ShortestPathLengths(Id(1), GraphProjection.AllPredicates());

        Assert.HasCount(4, distance.Keys, "All four chain nodes are reachable from the head.");
        Assert.AreEqual(0L, distance[Id(1)]);
        Assert.AreEqual(1L, distance[Id(2)]);
        Assert.AreEqual(2L, distance[Id(3)]);
        Assert.AreEqual(3L, distance[Id(4)]);
    }

    /// <summary>From a star's centre every spoke is one hop; the directed search does not reach a disconnected node.</summary>
    [TestMethod]
    public void ShortestPathLengthsReachOnlyTheReachableNodes()
    {
        //1 -> 2, 1 -> 3 (a star from 1), and a disconnected directed edge 4 -> 5.
        ColumnarGraphAnalytics analytics = Graph((1, 2), (1, 3), (4, 5));

        IReadOnlyDictionary<TermId, long> distance = analytics.ShortestPathLengths(Id(1), GraphProjection.AllPredicates());

        Assert.HasCount(3, distance.Keys, "Only the centre and its two spokes are reachable.");
        Assert.AreEqual(0L, distance[Id(1)]);
        Assert.AreEqual(1L, distance[Id(2)]);
        Assert.AreEqual(1L, distance[Id(3)]);
        Assert.IsFalse(distance.ContainsKey(Id(4)), "A node in a disconnected component is unreachable.");
        Assert.IsFalse(distance.ContainsKey(Id(5)));
    }

    /// <summary>Under an undirected projection the search follows edges either way, so a middle source reaches both ends.</summary>
    [TestMethod]
    public void ShortestPathLengthsFollowUndirectedEdgesBothWays()
    {
        //The directed chain 1 -> 2 -> 3, read undirected, from the middle node 2.
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 3));

        IReadOnlyDictionary<TermId, long> distance = analytics.ShortestPathLengths(Id(2), GraphProjection.AllPredicates(GraphEdgeDirection.Undirected));

        Assert.HasCount(3, distance.Keys);
        Assert.AreEqual(0L, distance[Id(2)]);
        Assert.AreEqual(1L, distance[Id(1)], "The undirected search reaches the predecessor.");
        Assert.AreEqual(1L, distance[Id(3)]);
    }

    /// <summary>A source absent from the graph reaches only itself, at distance zero.</summary>
    [TestMethod]
    public void ShortestPathLengthsFromAnAbsentSourceReachOnlyItself()
    {
        ColumnarGraphAnalytics analytics = Graph((1, 2), (2, 3));

        IReadOnlyDictionary<TermId, long> distance = analytics.ShortestPathLengths(Id(999), GraphProjection.AllPredicates());

        Assert.HasCount(1, distance.Keys, "An absent source reaches only itself.");
        Assert.AreEqual(0L, distance[Id(999)]);
    }
}
