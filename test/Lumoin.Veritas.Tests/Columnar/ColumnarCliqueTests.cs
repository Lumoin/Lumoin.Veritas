using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// Fixed-size clique enumeration over a <see cref="ColumnarTripleIndex"/> by the leapfrog worst-case-optimal join:
/// exact clique sets and counts on known graphs, the binomial counts of complete graphs, the cross-check that the
/// undirected size-three count equals <see cref="ColumnarGraphAnalytics.TriangleCount"/>, the predicate and
/// direction/parallel/self-loop collapse rules, and the stricter mutual (both-ways) connectivity.
/// </summary>
[TestClass]
internal sealed class ColumnarCliqueTests
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

    /// <summary>The cliques as their encoded-id arrays, in enumeration order.</summary>
    /// <param name="analytics">The analytics view.</param>
    /// <param name="projection">The projection.</param>
    /// <param name="cliqueSize">The clique size.</param>
    /// <param name="connectivity">The clique connectivity.</param>
    /// <returns>Each clique's vertices as encoded ids.</returns>
    private List<uint[]> CliqueList(ColumnarGraphAnalytics analytics, GraphProjection projection, int cliqueSize, CliqueConnectivity connectivity = CliqueConnectivity.Undirected)
    {
        return analytics
            .Cliques(projection, cliqueSize, connectivity, TestContext.CancellationToken)
            .Select(clique => clique.Select(vertex => vertex.Encoded).ToArray())
            .ToList();
    }

    /// <summary>The clique count under the test's cancellation token.</summary>
    /// <param name="analytics">The analytics view.</param>
    /// <param name="projection">The projection.</param>
    /// <param name="cliqueSize">The clique size.</param>
    /// <param name="connectivity">The clique connectivity.</param>
    /// <returns>The clique count.</returns>
    private long CountCliques(ColumnarGraphAnalytics analytics, GraphProjection projection, int cliqueSize, CliqueConnectivity connectivity = CliqueConnectivity.Undirected)
    {
        return analytics.CliqueCount(projection, cliqueSize, connectivity, TestContext.CancellationToken);
    }

    /// <summary>Asserts the enumerated cliques are exactly <paramref name="expected"/>, in order.</summary>
    /// <param name="actual">The enumerated cliques as encoded-id arrays.</param>
    /// <param name="expected">The expected cliques as encoded-id arrays, in order.</param>
    private static void AssertCliques(List<uint[]> actual, params uint[][] expected)
    {
        Assert.HasCount(expected.Length, actual, "The clique count differs from expected.");
        for(int i = 0; i < expected.Length; i++)
        {
            Assert.AreSequenceEqual(expected[i], actual[i], $"Clique {i} differs from expected.");
        }
    }

    /// <summary>A complete graph on nodes 1..<paramref name="order"/> under predicate 10 (every pair i&lt;j as one triple).</summary>
    /// <param name="order">The number of nodes.</param>
    /// <returns>The built index.</returns>
    private static ColumnarTripleIndex Complete(uint order)
    {
        List<EncodedTriple> triples = [];
        for(uint i = 1; i <= order; i++)
        {
            for(uint j = i + 1; j <= order; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(i, 10, j));
            }
        }

        return ColumnarTripleIndex.Build([.. triples]);
    }

    /// <summary>A single triangle on nodes 1, 2, 3.</summary>
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

    /// <summary>The lone triangle is the single size-three clique, and its three edges are the size-two cliques.</summary>
    [TestMethod]
    public void SingleTriangleIsOneCliqueAndThreeEdges()
    {
        ColumnarGraphAnalytics analytics = new(Triangle());
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 3), [1, 2, 3]);
        Assert.AreEqual(1L, CountCliques(analytics, all, 3), "One triangle.");
        AssertCliques(CliqueList(analytics, all, 2), [1, 2], [1, 3], [2, 3]);
        Assert.AreEqual(3L, CountCliques(analytics, all, 2), "Three undirected edges.");
    }

    /// <summary>Edges (size-two cliques) are listed once with the lower node first, ascending.</summary>
    [TestMethod]
    public void EdgesAreListedOnceAscending()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(2, 10, 1),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 2),
        ]));

        //Direction collapses: the edge 2->1 is the undirected pair {1, 2}.
        AssertCliques(CliqueList(analytics, GraphProjection.AllPredicates(), 2), [1, 2], [1, 3], [2, 3]);
    }

    /// <summary>A complete graph on four nodes has the binomial clique counts and the four ascending triangles.</summary>
    [TestMethod]
    public void CompleteGraphOfFourHasBinomialCliques()
    {
        ColumnarGraphAnalytics analytics = new(Complete(4));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.AreEqual(6L, CountCliques(analytics, all, 2), "C(4,2) edges.");
        Assert.AreEqual(4L, CountCliques(analytics, all, 3), "C(4,3) triangles.");
        Assert.AreEqual(1L, CountCliques(analytics, all, 4), "C(4,4) is the whole clique.");

        AssertCliques(CliqueList(analytics, all, 3), [1, 2, 3], [1, 2, 4], [1, 3, 4], [2, 3, 4]);
        AssertCliques(CliqueList(analytics, all, 4), [1, 2, 3, 4]);
    }

    /// <summary>A complete graph on five nodes has the binomial clique count at every size.</summary>
    [TestMethod]
    public void CompleteGraphOfFiveHasBinomialCliques()
    {
        ColumnarGraphAnalytics analytics = new(Complete(5));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.AreEqual(10L, CountCliques(analytics, all, 2), "C(5,2).");
        Assert.AreEqual(10L, CountCliques(analytics, all, 3), "C(5,3).");
        Assert.AreEqual(5L, CountCliques(analytics, all, 4), "C(5,4).");
        Assert.AreEqual(1L, CountCliques(analytics, all, 5), "C(5,5).");
        Assert.AreEqual(0L, CountCliques(analytics, all, 6), "No clique larger than the graph.");
    }

    /// <summary>A path has no triangle, only its consecutive edges.</summary>
    [TestMethod]
    public void PathHasNoTriangleClique()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.AreEqual(0L, CountCliques(analytics, all, 3), "A path closes no triangle.");
        AssertCliques(CliqueList(analytics, all, 2), [1, 2], [2, 3]);
    }

    /// <summary>Two triangles sharing the edge 1-2 but with 3 and 4 unconnected give two triangles and no four-clique.</summary>
    [TestMethod]
    public void DiamondHasTwoTrianglesButNoFourClique()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 4),
            EncodedTriple.FromEncoded(2, 10, 4),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 3), [1, 2, 3], [1, 2, 4]);
        Assert.AreEqual(0L, CountCliques(analytics, all, 4), "Nodes 3 and 4 are not connected, so there is no four-clique.");
    }

    /// <summary>Two disjoint triangles are two separate cliques, ordered by their smallest node.</summary>
    [TestMethod]
    public void DisjointTrianglesAreSeparateCliques()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(4, 10, 5),
            EncodedTriple.FromEncoded(5, 10, 6),
            EncodedTriple.FromEncoded(4, 10, 6),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 3), [1, 2, 3], [4, 5, 6]);
        Assert.AreEqual(6L, CountCliques(analytics, all, 2), "Three edges in each triangle.");
    }

    /// <summary>Edge direction and parallel edges collapse to one undirected edge, and a self-loop forms no clique.</summary>
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
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 3), [1, 2, 3]);
        Assert.AreEqual(1L, CountCliques(analytics, all, 3), "The parallel edge and the self-loop do not change the single triangle.");
    }

    /// <summary>A predicate projection restricts cliques to edges of the chosen predicates.</summary>
    [TestMethod]
    public void PredicateProjectionFiltersCliques()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 99, 4),
            EncodedTriple.FromEncoded(2, 99, 4),
        ]));

        Assert.AreEqual(2L, CountCliques(analytics, GraphProjection.AllPredicates(), 3), "The p10 triangle {1,2,3} and the mixed triangle {1,2,4}.");
        Assert.AreEqual(1L, CountCliques(analytics, GraphProjection.ForPredicates([Id(10)]), 3), "Only the p10 triangle.");
        Assert.AreEqual(0L, CountCliques(analytics, GraphProjection.ForPredicates([Id(99)]), 3), "The p99 edges alone close no triangle.");
    }

    /// <summary>A clique size above the largest clique yields nothing.</summary>
    [TestMethod]
    public void SizeAboveLargestCliqueIsEmpty()
    {
        ColumnarGraphAnalytics analytics = new(Triangle());
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.IsEmpty(CliqueList(analytics, all, 4), "A triangle has no four-clique.");
        Assert.AreEqual(0L, CountCliques(analytics, all, 4));
    }

    /// <summary>An empty graph has no cliques of any size.</summary>
    [TestMethod]
    public void EmptyGraphHasNoCliques()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build([]));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.IsEmpty(CliqueList(analytics, all, 2));
        Assert.AreEqual(0L, CountCliques(analytics, all, 3));
    }

    /// <summary>A clique size below two is rejected — a clique needs at least one edge.</summary>
    [TestMethod]
    public void CliqueSizeBelowTwoIsRejected()
    {
        ColumnarGraphAnalytics analytics = new(Triangle());
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CliqueList(analytics, all, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CountCliques(analytics, all, 0));
    }

    /// <summary>An undefined connectivity value is rejected.</summary>
    [TestMethod]
    public void UndefinedConnectivityIsRejected()
    {
        ColumnarGraphAnalytics analytics = new(Triangle());
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CliqueList(analytics, all, 3, (CliqueConnectivity)99));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CountCliques(analytics, all, 3, (CliqueConnectivity)99));
    }

    /// <summary>The undirected size-three clique count equals the independently computed triangle count on every sample graph.</summary>
    [TestMethod]
    public void UndirectedTripleCountMatchesTriangleCount()
    {
        ColumnarTripleIndex[] samples =
        [
            Triangle(),
            Complete(4),
            Complete(5),
            ColumnarTripleIndex.Build(
            [
                EncodedTriple.FromEncoded(1, 10, 2),
                EncodedTriple.FromEncoded(2, 10, 3),
                EncodedTriple.FromEncoded(1, 10, 3),
                EncodedTriple.FromEncoded(3, 10, 4),
            ]),
            ColumnarTripleIndex.Build(
            [
                EncodedTriple.FromEncoded(1, 10, 2),
                EncodedTriple.FromEncoded(2, 10, 3),
            ]),
        ];

        GraphProjection all = GraphProjection.AllPredicates();
        foreach(ColumnarTripleIndex sample in samples)
        {
            ColumnarGraphAnalytics analytics = new(sample);

            Assert.AreEqual(analytics.TriangleCount(all), CountCliques(analytics, all, 3), "The leapfrog triple count must equal the node-iterator triangle count.");
        }
    }

    /// <summary>A directed three-cycle has an undirected triangle but no reciprocal edge, so no mutual clique.</summary>
    [TestMethod]
    public void DirectedCycleHasNoMutualClique()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 1),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.AreEqual(1L, CountCliques(analytics, all, 3, CliqueConnectivity.Undirected), "Ignoring direction the cycle is a triangle.");
        Assert.AreEqual(0L, CountCliques(analytics, all, 2, CliqueConnectivity.Mutual), "No pair is connected both ways.");
        Assert.AreEqual(0L, CountCliques(analytics, all, 3, CliqueConnectivity.Mutual), "So there is no mutual triangle.");
    }

    /// <summary>A triangle whose every pair has edges both ways is a mutual clique.</summary>
    [TestMethod]
    public void ReciprocalTriangleIsAMutualClique()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 1),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(3, 10, 1),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 3, CliqueConnectivity.Mutual), [1, 2, 3]);
        Assert.AreEqual(3L, CountCliques(analytics, all, 2, CliqueConnectivity.Mutual), "Three reciprocal edges.");
    }

    /// <summary>Mutual connectivity keeps only the reciprocally connected pairs, so it is stricter than undirected.</summary>
    [TestMethod]
    public void MutualConnectivityIsStricterThanUndirected()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(2, 10, 1),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 3),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        Assert.AreEqual(1L, CountCliques(analytics, all, 3, CliqueConnectivity.Undirected), "Ignoring direction {1,2,3} is a triangle.");
        AssertCliques(CliqueList(analytics, all, 2, CliqueConnectivity.Mutual), [1, 2]);
        Assert.AreEqual(0L, CountCliques(analytics, all, 3, CliqueConnectivity.Mutual), "Only 1-2 is reciprocal, so no mutual triangle.");
    }

    /// <summary>
    /// A four-clique {1,2,3,4} embedded in a larger non-complete graph, with two distractors — node 5 adjacent to
    /// 1, 2, 3 and node 6 adjacent to 2, 3, 4 — exercises the three-cursor leapfrog intersection rejecting a
    /// candidate present in some neighbor runs but not all. The four-cliques are exactly {1,2,3,4}, {1,2,3,5} and
    /// {2,3,4,6}.
    /// </summary>
    [TestMethod]
    public void FourCliquesInNonCompleteGraphRejectNonCommonCandidates()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 4),
            EncodedTriple.FromEncoded(2, 10, 3),
            EncodedTriple.FromEncoded(2, 10, 4),
            EncodedTriple.FromEncoded(3, 10, 4),
            EncodedTriple.FromEncoded(1, 10, 5),
            EncodedTriple.FromEncoded(2, 10, 5),
            EncodedTriple.FromEncoded(3, 10, 5),
            EncodedTriple.FromEncoded(2, 10, 6),
            EncodedTriple.FromEncoded(3, 10, 6),
            EncodedTriple.FromEncoded(4, 10, 6),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 4), [1, 2, 3, 4], [1, 2, 3, 5], [2, 3, 4, 6]);
        Assert.AreEqual(3L, CountCliques(analytics, all, 4));
        Assert.AreEqual(analytics.TriangleCount(all), CountCliques(analytics, all, 3), "The triple count still matches the triangle count on a denser non-complete graph.");
    }

    /// <summary>
    /// A graph where extending {1, 2} makes one neighbor cursor seek past the other's candidate (an overshoot)
    /// exercises the leapfrog's target-raise-and-restart branch; the only triangle is {1, 2, 5}.
    /// </summary>
    [TestMethod]
    public void LeapfrogSeekOvershootIsHandled()
    {
        ColumnarGraphAnalytics analytics = new(ColumnarTripleIndex.Build(
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 10, 3),
            EncodedTriple.FromEncoded(1, 10, 5),
            EncodedTriple.FromEncoded(2, 10, 4),
            EncodedTriple.FromEncoded(2, 10, 5),
        ]));
        GraphProjection all = GraphProjection.AllPredicates();

        AssertCliques(CliqueList(analytics, all, 3), [1, 2, 5]);
    }

    /// <summary>A pre-cancelled token surfaces an <see cref="OperationCanceledException"/> through both clique entry points.</summary>
    [TestMethod]
    public void CancellationIsHonoured()
    {
        ColumnarGraphAnalytics analytics = new(Complete(5));
        GraphProjection all = GraphProjection.AllPredicates();
        using CancellationTokenSource source = new();
        source.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => analytics.CliqueCount(all, 3, CliqueConnectivity.Undirected, source.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => analytics.Cliques(all, 3, CliqueConnectivity.Undirected, source.Token).ToList());
    }
}
