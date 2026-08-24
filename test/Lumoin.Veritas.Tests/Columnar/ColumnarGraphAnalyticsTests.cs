using System;
using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// Degree metrics over a <see cref="ColumnarTripleIndex"/>'s order columns: out-degree (SPO), in-degree (OSP),
/// the undirected sum, a predicate-projection filter in both directions, and the one-pass degree distribution.
/// The metrics read the compressed-sparse adjacency directly — no separate projection — so a known small graph
/// pins the exact counts.
/// </summary>
[TestClass]
internal sealed class ColumnarGraphAnalyticsTests
{
    /// <summary>
    /// A fixed five-triple graph: subject 1 has two p10 objects (100, 101) and one p11 object (102); subject 2
    /// has one p10 object (100) and one p12 object (103). So 100 is a shared object (in-degree 2), 1 is a
    /// subject-only node, and 100/101/102/103 are object-only nodes.
    /// </summary>
    /// <returns>The built index.</returns>
    private static ColumnarTripleIndex SampleIndex()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(1, 10, 101),
            EncodedTriple.FromEncoded(1, 11, 102),
            EncodedTriple.FromEncoded(2, 10, 100),
            EncodedTriple.FromEncoded(2, 12, 103),
        ];

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>A term id from its raw encoded value.</summary>
    /// <param name="value">The encoded id.</param>
    /// <returns>The term id.</returns>
    private static TermId Id(uint value)
    {
        return TermId.FromEncoded(value);
    }

    /// <summary>Out-degree counts every outgoing edge of a subject; an object-only or absent node has out-degree zero.</summary>
    [TestMethod]
    public void OutDegreeCountsOutgoingEdges()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());
        GraphProjection forward = GraphProjection.AllPredicates();

        Assert.AreEqual(3L, analytics.Degree(Id(1), forward), "Subject 1 has three outgoing edges.");
        Assert.AreEqual(2L, analytics.Degree(Id(2), forward), "Subject 2 has two outgoing edges.");
        Assert.AreEqual(0L, analytics.Degree(Id(100), forward), "An object-only node has out-degree zero.");
        Assert.AreEqual(0L, analytics.Degree(Id(999), forward), "An absent node has out-degree zero.");
    }

    /// <summary>In-degree counts every incoming edge of an object; a subject-only node has in-degree zero.</summary>
    [TestMethod]
    public void InDegreeCountsIncomingEdges()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());
        GraphProjection reverse = GraphProjection.AllPredicates(GraphEdgeDirection.Reverse);

        Assert.AreEqual(2L, analytics.Degree(Id(100), reverse), "Object 100 is reached by two triples.");
        Assert.AreEqual(1L, analytics.Degree(Id(101), reverse), "Object 101 is reached by one triple.");
        Assert.AreEqual(0L, analytics.Degree(Id(1), reverse), "A subject-only node has in-degree zero.");
    }

    /// <summary>The undirected degree is the sum of out- and in-degree.</summary>
    [TestMethod]
    public void UndirectedDegreeSumsOutAndIn()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());
        GraphProjection undirected = GraphProjection.AllPredicates(GraphEdgeDirection.Undirected);

        Assert.AreEqual(3L, analytics.Degree(Id(1), undirected), "Node 1 is out-degree 3, in-degree 0.");
        Assert.AreEqual(2L, analytics.Degree(Id(100), undirected), "Node 100 is out-degree 0, in-degree 2.");
    }

    /// <summary>A predicate projection restricts the counted out-edges to the chosen predicates.</summary>
    [TestMethod]
    public void PredicateProjectionFiltersForwardEdges()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        Assert.AreEqual(2L, analytics.Degree(Id(1), GraphProjection.ForPredicates([Id(10)])), "Subject 1 has two p10 edges.");
        Assert.AreEqual(1L, analytics.Degree(Id(1), GraphProjection.ForPredicates([Id(11)])), "Subject 1 has one p11 edge.");
        Assert.AreEqual(3L, analytics.Degree(Id(1), GraphProjection.ForPredicates([Id(10), Id(11)])), "Subject 1's p10 and p11 edges are all of them.");
        Assert.AreEqual(0L, analytics.Degree(Id(1), GraphProjection.ForPredicates([Id(12)])), "Subject 1 has no p12 edge.");
    }

    /// <summary>A predicate projection restricts the counted in-edges (the predicate sits at the deepest level of the in-adjacency).</summary>
    [TestMethod]
    public void PredicateProjectionFiltersReverseEdges()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        Assert.AreEqual(2L, analytics.Degree(Id(100), GraphProjection.ForPredicates([Id(10)], GraphEdgeDirection.Reverse)), "Object 100 has two incoming p10 edges.");
        Assert.AreEqual(1L, analytics.Degree(Id(103), GraphProjection.ForPredicates([Id(12)], GraphEdgeDirection.Reverse)), "Object 103 has one incoming p12 edge.");
        Assert.AreEqual(0L, analytics.Degree(Id(100), GraphProjection.ForPredicates([Id(12)], GraphEdgeDirection.Reverse)), "Object 100 has no incoming p12 edge.");
    }

    /// <summary>The degree distribution maps each degree to the number of nodes that have it, over one adjacency.</summary>
    [TestMethod]
    public void DegreeDistributionCountsNodesPerDegree()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        IReadOnlyDictionary<long, long> forward = analytics.DegreeDistribution(GraphProjection.AllPredicates());
        Assert.HasCount(3, forward, "Out-degrees 3, 2, and 0 occur.");
        Assert.AreEqual(1L, forward[3L], "One subject has out-degree 3.");
        Assert.AreEqual(1L, forward[2L], "One subject has out-degree 2.");
        Assert.AreEqual(4L, forward[0L], "The four pure-object nodes have out-degree 0.");

        IReadOnlyDictionary<long, long> reverse = analytics.DegreeDistribution(GraphProjection.AllPredicates(GraphEdgeDirection.Reverse));
        Assert.HasCount(3, reverse, "In-degrees 2, 1, and 0 occur.");
        Assert.AreEqual(1L, reverse[2L], "One object has in-degree 2.");
        Assert.AreEqual(3L, reverse[1L], "Three objects have in-degree 1.");
        Assert.AreEqual(2L, reverse[0L], "The two pure-subject nodes have in-degree 0.");
    }

    /// <summary>The degree stream yields every node of the edge-induced vertex set once, the pure objects with out-degree zero.</summary>
    [TestMethod]
    public void DegreesStreamsEachNodeOnce()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        Dictionary<uint, long> outDegrees = analytics
            .Degrees(GraphProjection.AllPredicates())
            .ToDictionary(pair => pair.Node.Encoded, pair => pair.Degree);

        Assert.HasCount(6, outDegrees, "Two subjects and four objects are streamed.");
        Assert.AreEqual(3L, outDegrees[1]);
        Assert.AreEqual(2L, outDegrees[2]);
        Assert.AreEqual(0L, outDegrees[100], "A pure-object node has out-degree zero.");
        Assert.AreEqual(0L, outDegrees[103]);
    }

    /// <summary>A degree distribution over an undirected projection is refused — its node set is the subject-object union, a later increment.</summary>
    [TestMethod]
    public void UndirectedDistributionIsRefused()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        Assert.ThrowsExactly<ArgumentException>(() => analytics.DegreeDistribution(GraphProjection.AllPredicates(GraphEdgeDirection.Undirected)));
    }

    /// <summary>
    /// Two predicate-10 clusters — {1,100,101} and {2,200,201} (200 appears as both a subject and an object) —
    /// joined only by a single predicate-99 edge 1→2, so they are one component under every predicate and two
    /// under a predicate-10-only projection.
    /// </summary>
    /// <returns>The built index.</returns>
    private static ColumnarTripleIndex BridgeIndex()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(1, 10, 101),
            EncodedTriple.FromEncoded(2, 10, 200),
            EncodedTriple.FromEncoded(200, 10, 201),
            EncodedTriple.FromEncoded(1, 99, 2),
        ];

        return ColumnarTripleIndex.Build(triples);
    }

    /// <summary>A component's nodes as their raw encoded ids (already ascending in the result).</summary>
    /// <param name="component">The component.</param>
    /// <returns>The encoded ids.</returns>
    private static uint[] Encoded(IReadOnlyList<TermId> component)
    {
        return component.Select(node => node.Encoded).ToArray();
    }

    /// <summary>Ignoring direction, every node reaches every other through the predicate-99 bridge, so the whole graph is one component.</summary>
    [TestMethod]
    public void ConnectedComponentsMergeAcrossPredicates()
    {
        ColumnarGraphAnalytics analytics = new(BridgeIndex());

        IReadOnlyList<IReadOnlyList<TermId>> components = analytics.ConnectedComponents(GraphProjection.AllPredicates());

        Assert.HasCount(1, components, "The predicate-99 bridge joins the two clusters into one component.");
        Assert.AreSequenceEqual(new uint[] { 1, 2, 100, 101, 200, 201 }, Encoded(components[0]));
    }

    /// <summary>Restricting the projection to predicate 10 drops the bridge, leaving the two clusters as separate components ordered by their smallest node.</summary>
    [TestMethod]
    public void PredicateProjectionSplitsComponents()
    {
        ColumnarGraphAnalytics analytics = new(BridgeIndex());

        IReadOnlyList<IReadOnlyList<TermId>> components = analytics.ConnectedComponents(GraphProjection.ForPredicates([Id(10)]));

        Assert.HasCount(2, components, "Without the predicate-99 bridge the two clusters are separate.");
        Assert.AreSequenceEqual(new uint[] { 1, 100, 101 }, Encoded(components[0]));
        Assert.AreSequenceEqual(new uint[] { 2, 200, 201 }, Encoded(components[1]));
    }

    /// <summary>An empty graph has no nodes and so no components.</summary>
    [TestMethod]
    public void EmptyGraphHasNoComponents()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build([]));

        Assert.IsEmpty(analytics.ConnectedComponents(GraphProjection.AllPredicates()), "An empty graph has no components.");
    }

    /// <summary>A self-loop is one node, one component.</summary>
    [TestMethod]
    public void SelfLoopIsOneComponent()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build([EncodedTriple.FromEncoded(1, 10, 1)]));

        IReadOnlyList<IReadOnlyList<TermId>> components = analytics.ConnectedComponents(GraphProjection.AllPredicates());

        Assert.HasCount(1, components);
        Assert.AreSequenceEqual(new uint[] { 1 }, Encoded(components[0]));
    }

    /// <summary>A single undirected triangle is counted once and is fully clustered.</summary>
    [TestMethod]
    public void SingleTriangleCountedOnce()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
        ]));

        Assert.AreEqual(1L, analytics.TriangleCount(GraphProjection.AllPredicates()));
        Assert.AreEqual(1.0, analytics.GlobalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9, "A triangle is fully clustered.");
    }

    /// <summary>A path has no triangle and zero clustering.</summary>
    [TestMethod]
    public void PathHasNoTriangle()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
        ]));

        Assert.AreEqual(0L, analytics.TriangleCount(GraphProjection.AllPredicates()));
        Assert.AreEqual(0.0, analytics.GlobalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9);
    }

    /// <summary>A four-node clique has the four triangles of C(4,3) and is fully clustered.</summary>
    [TestMethod]
    public void CliqueOfFourHasFourTriangles()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 4),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(2, 10, 4),
            EncodedTriple.FromEncoded(3, 10, 4),
        ]));

        Assert.AreEqual(4L, analytics.TriangleCount(GraphProjection.AllPredicates()));
        Assert.AreEqual(1.0, analytics.GlobalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9);
    }

    /// <summary>Edge direction and parallel edges (a pair under several predicates) collapse to one undirected edge, and a self-loop forms no triangle.</summary>
    [TestMethod]
    public void DirectionParallelEdgesAndSelfLoopsCollapse()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 11, 2),
            EncodedTriple.FromEncoded(3, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 1),
        ]));

        //Undirected edges 1-2, 2-3, 1-3 are one triangle; the parallel 1-2 and the self-loop do not change it.
        Assert.AreEqual(1L, analytics.TriangleCount(GraphProjection.AllPredicates()));
    }

    /// <summary>Global clustering is the transitivity ratio: a triangle with a pendant edge has three length-two paths from the closed triple and two more from the pendant's centre, so 3 / 5.</summary>
    [TestMethod]
    public void GlobalClusteringIsTheTransitivityRatio()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 4),
        ]));

        Assert.AreEqual(1L, analytics.TriangleCount(GraphProjection.AllPredicates()));
        Assert.AreEqual(0.6, analytics.GlobalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9, "One triangle over five length-two paths gives 3 x 1 / 5.");
    }

    /// <summary>A predicate projection restricts triangles to edges of the chosen predicates.</summary>
    [TestMethod]
    public void PredicateProjectionFiltersTriangles()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 99, 4),
            EncodedTriple.FromEncoded(2, 99, 4),
        ]));

        Assert.AreEqual(2L, analytics.TriangleCount(GraphProjection.AllPredicates()), "The p10 triangle {1,2,3} and the mixed triangle {1,2,4}.");
        Assert.AreEqual(1L, analytics.TriangleCount(GraphProjection.ForPredicates([Id(10)])), "Only the p10 triangle {1,2,3}.");
        Assert.AreEqual(0L, analytics.TriangleCount(GraphProjection.ForPredicates([Id(99)])), "The p99 edges 1-4 and 2-4 alone form no triangle.");
    }

    /// <summary>A two-node cycle is symmetric, so both nodes carry half the rank.</summary>
    [TestMethod]
    public void SymmetricCycleSplitsRankEvenly()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 1),
        ]));

        IReadOnlyDictionary<TermId, double> ranks = analytics.PageRank(GraphProjection.AllPredicates());

        Assert.AreEqual(0.5, ranks[Id(1)], 1e-6);
        Assert.AreEqual(0.5, ranks[Id(2)], 1e-6);
    }

    /// <summary>PageRank stays a probability distribution: the ranks sum to one even with a dangling node.</summary>
    [TestMethod]
    public void RanksSumToOne()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 4),
        ]));

        IReadOnlyDictionary<TermId, double> ranks = analytics.PageRank(GraphProjection.AllPredicates());

        Assert.AreEqual(1.0, ranks.Values.Sum(), 1e-9, "The dangling mass is redistributed, so the ranks sum to one.");
    }

    /// <summary>A node with more in-links ranks above its sources, which rank equally by symmetry.</summary>
    [TestMethod]
    public void MoreInLinksRankHigher()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(2, 10, 3),
        ]));

        IReadOnlyDictionary<TermId, double> ranks = analytics.PageRank(GraphProjection.AllPredicates());

        Assert.IsGreaterThan(ranks[Id(1)], ranks[Id(3)], "The hub with two in-links ranks above a source.");
        Assert.IsGreaterThan(ranks[Id(2)], ranks[Id(3)], "The hub ranks above the other source too.");
        Assert.AreEqual(ranks[Id(1)], ranks[Id(2)], 1e-9, "The two symmetric sources rank equally.");
    }

    /// <summary>PageRank rejects a damping factor outside (0, 1) and a non-positive iteration count.</summary>
    [TestMethod]
    public void PageRankValidatesArguments()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => analytics.PageRank(GraphProjection.AllPredicates(), dampingFactor: 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => analytics.PageRank(GraphProjection.AllPredicates(), dampingFactor: 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => analytics.PageRank(GraphProjection.AllPredicates(), iterations: 0));
    }

    /// <summary>Every node of a triangle has all its neighbor pairs connected, so local — and average — clustering is one.</summary>
    [TestMethod]
    public void LocalClusteringOfTriangleIsOne()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
        ]));

        Assert.AreEqual(1.0, analytics.LocalClusteringCoefficient(Id(1), GraphProjection.AllPredicates()), 1e-9);
        Assert.AreEqual(1.0, analytics.AverageLocalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9);
    }

    /// <summary>A star centre's leaves are unconnected, so its local clustering — and a degree-one leaf's — is zero.</summary>
    [TestMethod]
    public void LocalClusteringOfStarIsZero()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 4),
        ]));

        Assert.AreEqual(0.0, analytics.LocalClusteringCoefficient(Id(1), GraphProjection.AllPredicates()), 1e-9, "The centre's leaves are unconnected.");
        Assert.AreEqual(0.0, analytics.LocalClusteringCoefficient(Id(2), GraphProjection.AllPredicates()), 1e-9, "A degree-one leaf has no neighbor pair.");
        Assert.AreEqual(0.0, analytics.AverageLocalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9);
    }

    /// <summary>A node absent from the graph has local clustering zero.</summary>
    [TestMethod]
    public void LocalClusteringOfAbsentNodeIsZero()
    {
        ColumnarGraphAnalytics analytics = new(SampleIndex());

        Assert.AreEqual(0.0, analytics.LocalClusteringCoefficient(Id(999), GraphProjection.AllPredicates()), 1e-9);
    }

    /// <summary>Average local clustering is the mean of each node's coefficient — a triangle with a pendant gives 1, 1, 1/3, 0 over four nodes.</summary>
    [TestMethod]
    public void AverageLocalClusteringAveragesPerNode()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 4),
        ]));

        Assert.AreEqual(1.0 / 3.0, analytics.LocalClusteringCoefficient(Id(3), GraphProjection.AllPredicates()), 1e-9, "Node 3's neighbors {1,2,4} close one of their three pairs.");
        Assert.AreEqual(7.0 / 12.0, analytics.AverageLocalClusteringCoefficient(GraphProjection.AllPredicates()), 1e-9, "The mean of 1, 1, 1/3, 0.");
    }
}
