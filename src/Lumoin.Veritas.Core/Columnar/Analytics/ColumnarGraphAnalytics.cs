using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// Graph-analytics metrics computed directly on a <see cref="ColumnarTripleIndex"/>'s compressed-sparse order
/// columns: degree and degree distribution, weakly and strongly connected components, triangle count and clustering
/// coefficients, k-core decomposition, PageRank, fixed-size clique enumeration, single-source shortest paths, and
/// closeness, betweenness, and eigenvector centrality. The index already IS the CSR adjacency a
/// graph-analytics library normally materialises separately: in SPO order a subject's (predicate, object) run is
/// contiguous — its out-adjacency — and in OSP order an object's run is its in-adjacency. So a node's degree is a
/// constant number of offset reads, a degree distribution is one pass over the distinct nodes, and the
/// connectivity, triangle, clustering, and clique metrics read the same order columns through a shared edge scan,
/// with no separate projection step.
/// </summary>
/// <remarks>
/// <para>
/// The metrics read the order columns directly, which a pending base-plus-delta would not reflect, so an index
/// carrying one is refused — compact it to a single generation first. Degree counts edges, so parallel edges
/// between the same pair under different predicates each count; edge weights are not modelled (RDF 1.2
/// triple-term territory).
/// </para>
/// </remarks>
public sealed class ColumnarGraphAnalytics
{
    /// <summary>The SPO permutation index: subjects at level 0, predicates at level 1, objects at level 2 — the out-adjacency.</summary>
    private const int SpoPermutation = 0;

    /// <summary>The OSP permutation index: objects at level 0, subjects at level 1, predicates at level 2 — the in-adjacency.</summary>
    private const int OspPermutation = 4;

    /// <summary>The delta-free index whose order columns the metrics read.</summary>
    private readonly ColumnarTripleIndex index;

    /// <summary>Creates an analytics view over a delta-free columnar index.</summary>
    /// <param name="index">The columnar triple index; it must carry no pending base-plus-delta, since the metrics read the order columns directly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="index"/> carries a pending delta.</exception>
    public ColumnarGraphAnalytics(ColumnarTripleIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if(index.HasDelta)
        {
            throw new ArgumentException("Graph analytics read the compressed order columns directly, which a pending base-plus-delta would not reflect; compact the index to one generation first.", nameof(index));
        }

        this.index = index;
    }

    /// <summary>
    /// The degree of <paramref name="node"/> under <paramref name="projection"/>: its out-edges for
    /// <see cref="GraphEdgeDirection.Forward"/>, its in-edges for <see cref="GraphEdgeDirection.Reverse"/>, and
    /// the sum for <see cref="GraphEdgeDirection.Undirected"/> (a self-loop contributes once each way). A node
    /// absent from the relevant adjacency has degree zero.
    /// </summary>
    /// <param name="node">The node id.</param>
    /// <param name="projection">The projection selecting the edge predicates and direction.</param>
    /// <returns>The node's degree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public long Degree(TermId node, GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return projection.Direction switch
        {
            GraphEdgeDirection.Forward => DirectedDegree(node, projection, SpoPermutation),
            GraphEdgeDirection.Reverse => DirectedDegree(node, projection, OspPermutation),
            _ => DirectedDegree(node, projection, SpoPermutation) + DirectedDegree(node, projection, OspPermutation),
        };
    }

    /// <summary>
    /// Streams each node's degree under <paramref name="projection"/> — every node incident to an included edge
    /// (the edge-induced vertex set, the same node universe the connectivity and triangle metrics use) paired
    /// with its directed degree, which is zero for a node with no edge in the chosen direction (a sink under
    /// Forward, a source under Reverse). One pass over the included edges; each node appears once.
    /// </summary>
    /// <param name="projection">The projection; its direction must be Forward or Reverse — a single adjacency. An undirected distribution over the subject-object union is a later increment.</param>
    /// <returns>Each node paired with its degree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="projection"/> is undirected.</exception>
    public IEnumerable<(TermId Node, long Degree)> Degrees(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        bool forward = projection.Direction switch
        {
            GraphEdgeDirection.Forward => true,
            GraphEdgeDirection.Reverse => false,
            _ => throw new ArgumentException("A degree stream needs a directed projection; an undirected degree distribution over the subject-object union is a later increment.", nameof(projection)),
        };

        return StreamDegrees(projection, forward);
    }

    /// <summary>
    /// The degree distribution under <paramref name="projection"/>: a map from a degree to the number of nodes
    /// that have it, over the edge-induced vertex set (so degree-zero sinks under Forward, or sources under
    /// Reverse, are counted), built in one pass.
    /// </summary>
    /// <param name="projection">The directed projection.</param>
    /// <returns>The degree-to-node-count histogram.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="projection"/> is undirected.</exception>
    public IReadOnlyDictionary<long, long> DegreeDistribution(GraphProjection projection)
    {
        Dictionary<long, long> histogram = [];
        foreach((TermId _, long degree) in Degrees(projection))
        {
            histogram.TryGetValue(degree, out long count);
            histogram[degree] = count + 1;
        }

        return histogram;
    }

    /// <summary>
    /// The weakly connected components under <paramref name="projection"/>: a partition of the nodes incident to
    /// an edge into the maximal sets connected when edge direction is ignored. Connectivity is inherently
    /// undirected, so the projection's direction is not consulted — only its predicate selection — and a node is
    /// in the graph exactly when an edge (under the filter) touches it. Each component lists its nodes ascending,
    /// and the components are ordered by their smallest node, so the result is deterministic.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored for connectivity.</param>
    /// <returns>The components, each the nodes it contains.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<IReadOnlyList<TermId>> ConnectedComponents(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        NodeUnionFind components = new();
        Dictionary<uint, int> denseByNode = [];
        List<uint> nodeByDense = [];

        //Connectivity is undirected and edge-driven, so the filtered node set is exact — a node enters only once
        //an included edge touches it.
        foreach((uint source, uint target) in EnumerateEdges(projection))
        {
            int sourceDense = GetOrAddDense(source, denseByNode, nodeByDense, components);
            int targetDense = GetOrAddDense(target, denseByNode, nodeByDense, components);
            components.Union(sourceDense, targetDense);
        }

        return GroupComponents(components, nodeByDense);
    }

    /// <summary>
    /// The number of triangles in the undirected graph under <paramref name="projection"/> — unordered node
    /// triples pairwise connected, each counted once. Direction and parallel edges (the same pair under several
    /// predicates) collapse to one undirected edge, and a self-loop forms no triangle. Computed with the
    /// node-iterator algorithm over the sorted undirected adjacency the SPO/OSP orders supply.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <returns>The undirected triangle count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public long TriangleCount(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return CountTriangles(BuildUndirectedAdjacency(projection));
    }

    /// <summary>
    /// The global clustering coefficient (transitivity) under <paramref name="projection"/>: three times the
    /// triangle count over the number of length-two paths (connected triples) — the fraction of paths that close
    /// into a triangle. Zero when the graph has no length-two path. Undirected, like <see cref="TriangleCount"/>.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <returns>The transitivity in <c>[0, 1]</c>, or zero when there is no length-two path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public double GlobalClusteringCoefficient(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        Dictionary<uint, SortedSet<uint>> adjacency = BuildUndirectedAdjacency(projection);
        long paths = CountLengthTwoPaths(adjacency);
        if(paths == 0)
        {
            return 0.0;
        }

        return 3.0 * CountTriangles(adjacency) / paths;
    }

    /// <summary>
    /// The local clustering coefficient of <paramref name="node"/> under <paramref name="projection"/>: the
    /// fraction of its neighbor pairs that are themselves connected — twice the edges among its neighbors over
    /// the number of neighbor pairs. Zero for a node of undirected degree below two (it has no neighbor pair),
    /// including a node absent from the graph. Undirected, like <see cref="TriangleCount"/>.
    /// </summary>
    /// <param name="node">The node id.</param>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <returns>The local clustering coefficient in <c>[0, 1]</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public double LocalClusteringCoefficient(TermId node, GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return LocalCoefficient(node.Encoded, BuildUndirectedAdjacency(projection));
    }

    /// <summary>
    /// The average local clustering coefficient under <paramref name="projection"/>: the mean of every node's
    /// <see cref="LocalClusteringCoefficient(TermId, GraphProjection)"/>, with degree-below-two nodes counted as
    /// zero. Zero for a graph with no node. Undirected.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <returns>The average local clustering coefficient in <c>[0, 1]</c>, or zero for an empty graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public double AverageLocalClusteringCoefficient(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        Dictionary<uint, SortedSet<uint>> adjacency = BuildUndirectedAdjacency(projection);
        if(adjacency.Count == 0)
        {
            return 0.0;
        }

        double total = 0.0;
        foreach(uint node in adjacency.Keys)
        {
            total += LocalCoefficient(node, adjacency);
        }

        return total / adjacency.Count;
    }

    /// <summary>
    /// The PageRank of every node under <paramref name="projection"/> by power iteration: rank flows along the
    /// projection's directed edges (subject→object for Forward, object→subject for Reverse, both for Undirected),
    /// with the dangling mass of nodes that have no out-edge redistributed uniformly so the ranks stay summed to
    /// one. Parallel edges (a pair under several predicates) collapse to one directed edge.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges and the rank-flow direction.</param>
    /// <param name="dampingFactor">The probability rank follows an edge rather than teleporting; strictly between 0 and 1 (0.85 by default).</param>
    /// <param name="iterations">The number of power-iteration steps; at least one (30 by default).</param>
    /// <returns>Each node mapped to its PageRank; the values sum to one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="dampingFactor"/> is not in <c>(0, 1)</c>, or <paramref name="iterations"/> is not positive.</exception>
    public IReadOnlyDictionary<TermId, double> PageRank(GraphProjection projection, double dampingFactor = 0.85, int iterations = 30)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dampingFactor, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dampingFactor, 1.0);

        Dictionary<uint, int> denseByNode = [];
        List<uint> nodeByDense = [];
        List<HashSet<int>> outNeighbors = [];

        bool undirected = projection.Direction == GraphEdgeDirection.Undirected;
        bool reverse = projection.Direction == GraphEdgeDirection.Reverse;
        foreach((uint source, uint target) in EnumerateEdges(projection))
        {
            int sourceDense = GetOrAddPageRankNode(source, denseByNode, nodeByDense, outNeighbors);
            int targetDense = GetOrAddPageRankNode(target, denseByNode, nodeByDense, outNeighbors);
            if(undirected || !reverse)
            {
                outNeighbors[sourceDense].Add(targetDense);
            }

            if(undirected || reverse)
            {
                outNeighbors[targetDense].Add(sourceDense);
            }
        }

        Dictionary<TermId, double> ranks = [];
        int nodeCount = nodeByDense.Count;
        if(nodeCount == 0)
        {
            return ranks;
        }

        using IMemoryOwner<double> rankOwner = VeritasMemoryPool<double>.Shared.Rent(nodeCount);
        using IMemoryOwner<double> nextOwner = VeritasMemoryPool<double>.Shared.Rent(nodeCount);
        Span<double> rank = rankOwner.Memory.Span;
        Span<double> next = nextOwner.Memory.Span;
        rank.Fill(1.0 / nodeCount);

        for(int iteration = 0; iteration < iterations; iteration++)
        {
            PageRankStep(rank, next, outNeighbors, dampingFactor);

            //Swap so the just-computed ranks feed the next step; after the final step the settled ranks are in `rank`.
            Span<double> settled = rank;
            rank = next;
            next = settled;
        }

        for(int i = 0; i < nodeCount; i++)
        {
            ranks[TermId.FromEncoded(nodeByDense[i])] = rank[i];
        }

        return ranks;
    }

    /// <summary>
    /// The cliques of size <paramref name="cliqueSize"/> under <paramref name="projection"/> and
    /// <paramref name="connectivity"/> — each a set of that many vertices all pairwise connected, listed once with
    /// its vertices ascending and the cliques in ascending lexicographic order. Connectivity fixes what "connected"
    /// means: <see cref="CliqueConnectivity.Undirected"/> counts a pair connected when an edge runs either way (the
    /// classical clique, so size-three cliques are the triangles <see cref="TriangleCount"/> counts), and
    /// <see cref="CliqueConnectivity.Mutual"/> requires an edge both ways. Parallel edges collapse, self-loops are
    /// dropped, and the projection's direction is not consulted. Found by a worst-case-optimal leapfrog join, the
    /// shape cliques are cheap for; enumeration can be large, so callers bound the size and may cancel.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <param name="cliqueSize">The clique size; at least two (a single edge).</param>
    /// <param name="connectivity">Whether a pair counts as connected by an edge either way (undirected) or both ways (mutual).</param>
    /// <param name="cancellationToken">Cancellation token, honoured once per base vertex and once per leapfrog pass.</param>
    /// <returns>The cliques, each its vertices ascending.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cliqueSize"/> is below two, or <paramref name="connectivity"/> is not a defined value.</exception>
    public IEnumerable<IReadOnlyList<TermId>> Cliques(GraphProjection projection, int cliqueSize, CliqueConnectivity connectivity = CliqueConnectivity.Undirected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfLessThan(cliqueSize, 2);
        ValidateConnectivity(connectivity);

        return EnumerateCliques(projection, cliqueSize, connectivity, cancellationToken);
    }

    /// <summary>
    /// The number of size-<paramref name="cliqueSize"/> cliques under <paramref name="projection"/> and
    /// <paramref name="connectivity"/> — <see cref="Cliques"/> counted without materialising each. With
    /// <see cref="CliqueConnectivity.Undirected"/> and size three this is the worst-case-optimal triangle count,
    /// equal to <see cref="TriangleCount"/>.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <param name="cliqueSize">The clique size; at least two.</param>
    /// <param name="connectivity">Whether a pair counts as connected by an edge either way (undirected) or both ways (mutual).</param>
    /// <param name="cancellationToken">Cancellation token, honoured once per base vertex and once per leapfrog pass.</param>
    /// <returns>The clique count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cliqueSize"/> is below two, or <paramref name="connectivity"/> is not a defined value.</exception>
    public long CliqueCount(GraphProjection projection, int cliqueSize, CliqueConnectivity connectivity = CliqueConnectivity.Undirected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfLessThan(cliqueSize, 2);
        ValidateConnectivity(connectivity);

        LeapfrogCliqueWalker walker = new(BuildSymmetricAdjacency(projection, connectivity), cliqueSize, cancellationToken);

        long count = 0;
        while(walker.MoveNext())
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The closeness centrality of every node under <paramref name="projection"/>: for each node, the number of
    /// other nodes it reaches over the undirected edges divided by the total hop distance to them — a node central
    /// in its component scores near one, a peripheral node lower; zero for a node that reaches no other. Edges are
    /// unweighted (weighted shortest paths await an RDF 1.2 edge-weight model); direction and parallel edges
    /// collapse, like <see cref="TriangleCount"/>. An all-sources breadth-first traversal — the heavier tier — so
    /// callers may cancel.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <param name="cancellationToken">Cancellation token, honoured once per source node.</param>
    /// <returns>Each node mapped to its closeness centrality.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<TermId, double> ClosenessCentrality(GraphProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);

        SymmetricAdjacency adjacency = BuildSymmetricAdjacency(projection, CliqueConnectivity.Undirected);
        int count = adjacency.Count;
        if(count == 0)
        {
            return new Dictionary<TermId, double>();
        }

        using IMemoryOwner<double> closenessOwner = VeritasMemoryPool<double>.Shared.Rent(count);
        Span<double> closeness = closenessOwner.Memory.Span;
        GraphCentrality.Closeness(adjacency, closeness, cancellationToken);

        return ToTermMap(adjacency, closeness);
    }

    /// <summary>
    /// The betweenness centrality of every node under <paramref name="projection"/>: for each node, the number of
    /// shortest paths between other node pairs that pass through it (Brandes' accumulation over unweighted
    /// breadth-first searches, each unordered pair counted once — raw counts, not normalised). A node on many
    /// shortest paths scores high; a leaf scores zero. Edges are unweighted; direction and parallel edges collapse,
    /// like <see cref="TriangleCount"/>. An all-sources traversal — the heavier tier — so callers may cancel.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <param name="cancellationToken">Cancellation token, honoured once per source node.</param>
    /// <returns>Each node mapped to its betweenness centrality.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<TermId, double> BetweennessCentrality(GraphProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);

        SymmetricAdjacency adjacency = BuildSymmetricAdjacency(projection, CliqueConnectivity.Undirected);
        int count = adjacency.Count;
        if(count == 0)
        {
            return new Dictionary<TermId, double>();
        }

        using IMemoryOwner<double> betweennessOwner = VeritasMemoryPool<double>.Shared.Rent(count);
        Span<double> betweenness = betweennessOwner.Memory.Span;
        GraphCentrality.Betweenness(adjacency, betweenness, cancellationToken);

        return ToTermMap(adjacency, betweenness);
    }

    /// <summary>
    /// The eigenvector centrality of every node under <paramref name="projection"/>: a node is central in proportion
    /// to the centrality of its neighbors — the principal (Perron) eigenvector of the undirected adjacency,
    /// L2-normalised (its squared entries sum to one) and non-negative. Computed by power iteration on
    /// <c>A + I</c> rather than <c>A</c>: the shift leaves the eigenvector unchanged (only its eigenvalue moves) but
    /// makes the iteration converge on a bipartite graph too, where the adjacency's symmetric spectrum would
    /// otherwise leave the plain iteration oscillating. Undirected, like <see cref="TriangleCount"/>; direction and
    /// parallel edges collapse and self-loops are dropped. Zero nodes yield an empty result.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <param name="iterations">The number of power-iteration steps; at least one (100 by default — ample for the small, well-connected graphs the metric suits).</param>
    /// <returns>Each node mapped to its eigenvector centrality.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="iterations"/> is not positive.</exception>
    public IReadOnlyDictionary<TermId, double> EigenvectorCentrality(GraphProjection projection, int iterations = 100)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        SymmetricAdjacency adjacency = BuildSymmetricAdjacency(projection, CliqueConnectivity.Undirected);
        int count = adjacency.Count;
        if(count == 0)
        {
            return new Dictionary<TermId, double>();
        }

        using IMemoryOwner<double> centralityOwner = VeritasMemoryPool<double>.Shared.Rent(count);
        using IMemoryOwner<double> nextOwner = VeritasMemoryPool<double>.Shared.Rent(count);
        Span<double> centrality = centralityOwner.Memory.Span;
        Span<double> next = nextOwner.Memory.Span;
        centrality.Fill(1.0 / Math.Sqrt(count));

        for(int iteration = 0; iteration < iterations; iteration++)
        {
            double sumOfSquares = 0.0;
            for(int vertex = 0; vertex < count; vertex++)
            {
                //(A + I)·x: the node's own centrality plus its neighbors' — the shift that keeps the eigenvector but
                //forces convergence on a bipartite graph.
                double weight = centrality[vertex];
                foreach(int neighbor in adjacency.NeighborsOf(vertex))
                {
                    weight += centrality[neighbor];
                }

                next[vertex] = weight;
                sumOfSquares += weight * weight;
            }

            if(sumOfSquares == 0.0)
            {
                break;
            }

            double norm = Math.Sqrt(sumOfSquares);
            for(int vertex = 0; vertex < count; vertex++)
            {
                next[vertex] /= norm;
            }

            //Swap the buffers: the just-computed vector becomes the input to the next step. After the final step the
            //settled vector is in `centrality`.
            Span<double> settled = centrality;
            centrality = next;
            next = settled;
        }

        return ToTermMap(adjacency, centrality);
    }

    /// <summary>
    /// The strongly connected components under <paramref name="projection"/>: a partition of the nodes incident to a
    /// directed edge into the maximal sets in which every node reaches every other along the projection's directed
    /// edges (subject→object for <see cref="GraphEdgeDirection.Forward"/>, object→subject for
    /// <see cref="GraphEdgeDirection.Reverse"/>, both for <see cref="GraphEdgeDirection.Undirected"/>, where a
    /// strong component coincides with a weak one). Found by Tarjan's algorithm. Each component lists its nodes
    /// ascending, and the components are ordered by their smallest node, so the result is deterministic. The directed
    /// counterpart of <see cref="ConnectedComponents"/>.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges and the edge direction.</param>
    /// <returns>The strongly connected components, each the nodes it contains.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<IReadOnlyList<TermId>> StronglyConnectedComponents(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        DirectedAdjacency adjacency = BuildDirectedAdjacency(projection);

        return ShapeComponents(TarjanScc.Compute(adjacency), adjacency);
    }

    /// <summary>
    /// The k-core decomposition under <paramref name="projection"/>: each node mapped to its core number, the largest
    /// <c>k</c> for which it lies in the k-core — the maximal subgraph where every node has at least <c>k</c>
    /// neighbors. The maximum core number is the graph's degeneracy. Undirected, like <see cref="TriangleCount"/>;
    /// direction and parallel edges collapse and self-loops are dropped. Computed by min-degree peeling.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges; its direction is ignored.</param>
    /// <returns>Each node mapped to its core number.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<TermId, long> CoreNumbers(GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        SymmetricAdjacency adjacency = BuildSymmetricAdjacency(projection, CliqueConnectivity.Undirected);
        int count = adjacency.Count;
        if(count == 0)
        {
            return new Dictionary<TermId, long>();
        }

        using IMemoryOwner<int> coreOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> core = coreOwner.Memory.Span;
        GraphCoreNumbers.Compute(adjacency, core);

        Dictionary<TermId, long> result = new(count);
        for(int i = 0; i < count; i++)
        {
            result[TermId.FromEncoded(adjacency.VertexAt(i))] = core[i];
        }

        return result;
    }

    /// <summary>
    /// The unweighted shortest-path length from <paramref name="source"/> to every node it reaches under
    /// <paramref name="projection"/>: each reachable node mapped to its hop distance, the source itself at zero, by
    /// breadth-first search along the projection's directed edges (subject→object for
    /// <see cref="GraphEdgeDirection.Forward"/>, object→subject for <see cref="GraphEdgeDirection.Reverse"/>, both for
    /// <see cref="GraphEdgeDirection.Undirected"/>). Edges are unweighted (weighted shortest paths await an RDF 1.2
    /// edge-weight model). A node the source cannot reach is absent; a source with no outgoing edge under the
    /// projection (including one not in the graph) reaches only itself.
    /// </summary>
    /// <param name="source">The source node id.</param>
    /// <param name="projection">The projection selecting which predicates count as edges and the edge direction.</param>
    /// <returns>Each reachable node mapped to its hop distance from <paramref name="source"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="projection"/> is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<TermId, long> ShortestPathLengths(TermId source, GraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        DirectedAdjacency adjacency = BuildDirectedAdjacency(projection);

        Dictionary<TermId, long> result = [];
        if(!adjacency.TryGetDense(source.Encoded, out int sourceDense))
        {
            //The source has no incident edge under the projection, so it is not in the edge-induced vertex set; it
            //reaches only itself, at distance zero.
            result[source] = 0;

            return result;
        }

        int count = adjacency.Count;
        using IMemoryOwner<int> distanceOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> distance = distanceOwner.Memory.Span;
        BreadthFirstDistances(adjacency, sourceDense, distance);
        for(int i = 0; i < count; i++)
        {
            if(distance[i] >= 0)
            {
                result[TermId.FromEncoded(adjacency.VertexAt(i))] = distance[i];
            }
        }

        return result;
    }

    /// <summary>The degree of one node in a single order: the level-0 value is the node, and its leaf count under the predicate filter is the degree.</summary>
    /// <param name="node">The node id.</param>
    /// <param name="projection">The projection.</param>
    /// <param name="permutation">The order to read the adjacency from.</param>
    /// <returns>The node's degree in that order, or zero when it is absent.</returns>
    private long DirectedDegree(TermId node, GraphProjection projection, int permutation)
    {
        ColumnarOrder order = index.OrderAt(permutation);
        (int lo, int hi) = index.Level0BoundsAt(permutation);

        BlockPackedColumnReader values0 = new(order.ValuesColumnAt(0));
        int i = values0.LowerBound(lo, hi, node.Encoded);
        if(i >= hi || values0.ValueAt(i) != node.Encoded)
        {
            return 0;
        }

        BlockPackedColumnReader offsets0 = new(order.OffsetsColumnAt(0));
        int childStart = (int)offsets0.ValueAt(i);
        int childEnd = (int)offsets0.ValueAt(i + 1);

        BlockPackedColumnReader offsets1 = new(order.OffsetsColumnAt(1));
        BlockPackedColumnReader? predicateValues = projection.IncludesEveryPredicate
            ? null
            : new BlockPackedColumnReader(order.ValuesColumnAt(PredicateLevel(permutation)));

        return NodeDegree(projection, permutation, offsets1, predicateValues, childStart, childEnd);
    }

    /// <summary>One pass over the included edges, accumulating each node's directed degree — the tail (subject for Forward, object for Reverse) counts the edge and the head is registered with degree zero — then yielding every node once.</summary>
    /// <param name="projection">The projection selecting which predicates count as edges.</param>
    /// <param name="forward">Whether the degree is the out-degree (Forward) rather than the in-degree (Reverse).</param>
    /// <returns>Each node and its directed degree.</returns>
    private IEnumerable<(TermId Node, long Degree)> StreamDegrees(GraphProjection projection, bool forward)
    {
        Dictionary<uint, long> degreeByNode = [];
        foreach((uint source, uint target) in EnumerateEdges(projection))
        {
            (uint tail, uint head) = forward ? (source, target) : (target, source);
            degreeByNode.TryGetValue(tail, out long degree);
            degreeByNode[tail] = degree + 1;
            if(!degreeByNode.ContainsKey(head))
            {
                degreeByNode[head] = 0;
            }
        }

        foreach((uint node, long degree) in degreeByNode)
        {
            yield return (TermId.FromEncoded(node), degree);
        }
    }

    /// <summary>
    /// Counts the leaf edges under one level-0 node from its level-1 child range, respecting the predicate
    /// filter. With no filter it is the leaf count — two offset reads. With a filter the predicate sits at
    /// level 1 (SPO: keep each edge predicate's whole leaf run) or level 2 (OSP: count each kept leaf).
    /// </summary>
    /// <param name="projection">The projection carrying the predicate filter.</param>
    /// <param name="permutation">The adjacency order (fixes where the predicate sits).</param>
    /// <param name="offsets1">A reader over the level-1 offset column.</param>
    /// <param name="predicateValues">A reader over the predicate value column, or <see langword="null"/> when every predicate is an edge.</param>
    /// <param name="childStart">The node's inclusive level-1 child start.</param>
    /// <param name="childEnd">The node's exclusive level-1 child end.</param>
    /// <returns>The node's degree under the filter.</returns>
    private static long NodeDegree(GraphProjection projection, int permutation, BlockPackedColumnReader offsets1, BlockPackedColumnReader? predicateValues, int childStart, int childEnd)
    {
        if(predicateValues is null)
        {
            return (long)offsets1.ValueAt(childEnd) - offsets1.ValueAt(childStart);
        }

        if(permutation == SpoPermutation)
        {
            //Predicate at level 1: keep an edge predicate's whole leaf (object) run.
            long degree = 0;
            for(int j = childStart; j < childEnd; j++)
            {
                if(projection.IncludesPredicate(TermId.FromEncoded(predicateValues.ValueAt(j))))
                {
                    degree += (long)offsets1.ValueAt(j + 1) - offsets1.ValueAt(j);
                }
            }

            return degree;
        }

        //OSP: predicate at level 2 — count each kept leaf across the node's level-1 children.
        long count = 0;
        for(int j = childStart; j < childEnd; j++)
        {
            int leafStart = (int)offsets1.ValueAt(j);
            int leafEnd = (int)offsets1.ValueAt(j + 1);
            for(int k = leafStart; k < leafEnd; k++)
            {
                if(projection.IncludesPredicate(TermId.FromEncoded(predicateValues.ValueAt(k))))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>The level the predicate value sits at in the given order: 1 in SPO, 2 in OSP.</summary>
    /// <param name="permutation">The adjacency order.</param>
    /// <returns>The predicate's descent level.</returns>
    private static int PredicateLevel(int permutation)
    {
        return permutation == SpoPermutation ? 1 : 2;
    }

    /// <summary>
    /// Streams every edge under <paramref name="projection"/> as a (source subject, target object) pair in SPO
    /// stored order, with the predicate filter applied — the shared edge scan behind the connectivity and triangle
    /// metrics, which fold each pair both ways for their undirected view.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges.</param>
    /// <returns>Each edge's source and target encoded ids.</returns>
    private IEnumerable<(uint Source, uint Target)> EnumerateEdges(GraphProjection projection)
    {
        ColumnarOrder spo = index.OrderAt(SpoPermutation);
        (int lo, int hi) = index.Level0BoundsAt(SpoPermutation);
        BlockPackedColumnReader subjects = new(spo.ValuesColumnAt(0));
        BlockPackedColumnReader subjectOffsets = new(spo.OffsetsColumnAt(0));
        BlockPackedColumnReader predicateOffsets = new(spo.OffsetsColumnAt(1));
        BlockPackedColumnReader objects = new(spo.ValuesColumnAt(2));
        BlockPackedColumnReader? predicates = projection.IncludesEveryPredicate ? null : new BlockPackedColumnReader(spo.ValuesColumnAt(1));

        for(int i = lo; i < hi; i++)
        {
            uint subject = subjects.ValueAt(i);
            int predicateStart = (int)subjectOffsets.ValueAt(i);
            int predicateEnd = (int)subjectOffsets.ValueAt(i + 1);

            for(int j = predicateStart; j < predicateEnd; j++)
            {
                if(predicates is not null && !projection.IncludesPredicate(TermId.FromEncoded(predicates.ValueAt(j))))
                {
                    continue;
                }

                int objectStart = (int)predicateOffsets.ValueAt(j);
                int objectEnd = (int)predicateOffsets.ValueAt(j + 1);
                for(int k = objectStart; k < objectEnd; k++)
                {
                    yield return (subject, objects.ValueAt(k));
                }
            }
        }
    }

    /// <summary>
    /// Builds the undirected adjacency under <paramref name="projection"/> — each node mapped to its distinct
    /// neighbors, ascending. Direction and parallel edges collapse to one undirected edge, and self-loops are
    /// dropped (they form no triangle).
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges.</param>
    /// <returns>The undirected adjacency.</returns>
    private Dictionary<uint, SortedSet<uint>> BuildUndirectedAdjacency(GraphProjection projection)
    {
        Dictionary<uint, SortedSet<uint>> adjacency = [];
        foreach((uint source, uint target) in EnumerateEdges(projection))
        {
            if(source == target)
            {
                continue;
            }

            Neighbors(adjacency, source).Add(target);
            Neighbors(adjacency, target).Add(source);
        }

        return adjacency;
    }

    /// <summary>The node's neighbor set in the adjacency, created empty on its first sight.</summary>
    /// <param name="adjacency">The adjacency being built.</param>
    /// <param name="node">The node's encoded id.</param>
    /// <returns>The node's neighbor set.</returns>
    private static SortedSet<uint> Neighbors(Dictionary<uint, SortedSet<uint>> adjacency, uint node)
    {
        if(!adjacency.TryGetValue(node, out SortedSet<uint>? neighbors))
        {
            neighbors = [];
            adjacency[node] = neighbors;
        }

        return neighbors;
    }

    /// <summary>Maps a dense-indexed value span back to a term-keyed dictionary, decoding each dense vertex to its term. The span may be a pooled rental, so the map is materialised here while it is still borrowed.</summary>
    /// <param name="adjacency">The adjacency the dense indices number against.</param>
    /// <param name="values">The per-dense-vertex values, one per dense vertex in order.</param>
    /// <returns>The term-keyed map.</returns>
    private static Dictionary<TermId, double> ToTermMap(SymmetricAdjacency adjacency, ReadOnlySpan<double> values)
    {
        Dictionary<TermId, double> map = new(values.Length);
        for(int i = 0; i < values.Length; i++)
        {
            map[TermId.FromEncoded(adjacency.VertexAt(i))] = values[i];
        }

        return map;
    }

    /// <summary>
    /// Builds the directed adjacency under <paramref name="projection"/> over a dense vertex numbering — each node's
    /// distinct out-neighbors as ascending dense indices — for the directed traversals (strong components,
    /// single-source shortest paths). An edge runs subject→object for <see cref="GraphEdgeDirection.Forward"/>,
    /// object→subject for <see cref="GraphEdgeDirection.Reverse"/>, and both ways for
    /// <see cref="GraphEdgeDirection.Undirected"/>; parallel edges collapse. The dense numbering follows first sight.
    /// The result is the flat <see cref="DirectedAdjacency"/> CSR — one contiguous neighbor block, cache-resident on a
    /// scan — not a jagged per-vertex array.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges and the direction.</param>
    /// <returns>The directed adjacency in flat CSR form.</returns>
    private DirectedAdjacency BuildDirectedAdjacency(GraphProjection projection)
    {
        Dictionary<uint, int> denseByNode = [];
        List<uint> nodeByDense = [];
        List<HashSet<int>> outNeighbors = [];

        bool undirected = projection.Direction == GraphEdgeDirection.Undirected;
        bool reverse = projection.Direction == GraphEdgeDirection.Reverse;
        foreach((uint source, uint target) in EnumerateEdges(projection))
        {
            int sourceDense = GetOrAddPageRankNode(source, denseByNode, nodeByDense, outNeighbors);
            int targetDense = GetOrAddPageRankNode(target, denseByNode, nodeByDense, outNeighbors);
            if(undirected || !reverse)
            {
                outNeighbors[sourceDense].Add(targetDense);
            }

            if(undirected || reverse)
            {
                outNeighbors[targetDense].Add(sourceDense);
            }
        }

        return new DirectedAdjacency(outNeighbors, nodeByDense, denseByNode);
    }

    /// <summary>Fills <paramref name="distance"/> with the hop distance from <paramref name="source"/> to every dense vertex by breadth-first search over the directed adjacency — distance zero at the source, <c>-1</c> for a vertex the source cannot reach. Iterative with an explicit pooled queue; no recursion.</summary>
    /// <param name="adjacency">The directed CSR adjacency.</param>
    /// <param name="source">The source dense vertex.</param>
    /// <param name="distance">The per-dense-vertex hop-distance span to fill (one entry per dense vertex); <c>-1</c> marks an unreachable vertex.</param>
    private static void BreadthFirstDistances(DirectedAdjacency adjacency, int source, Span<int> distance)
    {
        int count = adjacency.Count;
        distance.Fill(-1);

        using IMemoryOwner<int> queueOwner = VeritasMemoryPool<int>.Shared.Rent(count);
        Span<int> queue = queueOwner.Memory.Span;

        int head = 0;
        int tail = 0;
        distance[source] = 0;
        queue[tail++] = source;

        while(head < tail)
        {
            int current = queue[head++];
            foreach(int neighbor in adjacency.NeighborsOf(current))
            {
                if(distance[neighbor] < 0)
                {
                    distance[neighbor] = distance[current] + 1;
                    queue[tail++] = neighbor;
                }
            }
        }
    }

    /// <summary>Shapes dense strongly-connected components into a deterministic term-keyed result: each component's nodes ascending, the components ordered by their smallest node.</summary>
    /// <param name="denseComponents">The components as dense vertex indices.</param>
    /// <param name="adjacency">The directed adjacency the dense indices number against.</param>
    /// <returns>The components as term ids, deterministically ordered.</returns>
    private static List<IReadOnlyList<TermId>> ShapeComponents(List<List<int>> denseComponents, DirectedAdjacency adjacency)
    {
        List<IReadOnlyList<TermId>> result = [];
        foreach(List<int> dense in denseComponents)
        {
            List<TermId> members = new(dense.Count);
            foreach(int vertex in dense)
            {
                members.Add(TermId.FromEncoded(adjacency.VertexAt(vertex)));
            }

            members.Sort();
            result.Add(members);
        }

        result.Sort(static (left, right) => left[0].CompareTo(right[0]));

        return result;
    }

    /// <summary>Walks the clique enumeration lazily, mapping each clique's dense indices back to term ids.</summary>
    /// <param name="projection">The projection selecting which predicates count as edges.</param>
    /// <param name="cliqueSize">The clique size.</param>
    /// <param name="connectivity">Whether a pair is connected by an edge either way (undirected) or both ways (mutual).</param>
    /// <param name="cancellationToken">Cancellation token threaded into the walk.</param>
    /// <returns>The cliques, each its vertices ascending.</returns>
    private IEnumerable<IReadOnlyList<TermId>> EnumerateCliques(GraphProjection projection, int cliqueSize, CliqueConnectivity connectivity, CancellationToken cancellationToken)
    {
        SymmetricAdjacency adjacency = BuildSymmetricAdjacency(projection, connectivity);
        LeapfrogCliqueWalker walker = new(adjacency, cliqueSize, cancellationToken);

        while(walker.MoveNext())
        {
            ReadOnlySpan<int> dense = walker.CurrentDense;
            TermId[] clique = new TermId[dense.Length];
            for(int i = 0; i < dense.Length; i++)
            {
                clique[i] = TermId.FromEncoded(adjacency.VertexAt(dense[i]));
            }

            yield return clique;
        }
    }

    /// <summary>Builds the dense symmetric adjacency the clique leapfrog intersects, either-way or both-ways per <paramref name="connectivity"/>.</summary>
    /// <param name="projection">The projection selecting which predicates count as edges.</param>
    /// <param name="connectivity">The clique connectivity.</param>
    /// <returns>The dense CSR adjacency.</returns>
    private SymmetricAdjacency BuildSymmetricAdjacency(GraphProjection projection, CliqueConnectivity connectivity)
    {
        Dictionary<uint, SortedSet<uint>> adjacency = connectivity switch
        {
            CliqueConnectivity.Undirected => BuildUndirectedAdjacency(projection),
            CliqueConnectivity.Mutual => BuildMutualAdjacency(projection),
            _ => throw new ArgumentOutOfRangeException(nameof(connectivity), connectivity, "Clique connectivity must be Undirected or Mutual."),
        };

        return new SymmetricAdjacency(adjacency);
    }

    /// <summary>
    /// Builds the mutual adjacency under <paramref name="projection"/> — each node mapped to the distinct nodes it
    /// is connected to in both directions (an edge each way, under any included predicate), ascending. Self-loops
    /// are dropped, and a node with no reciprocal edge is absent.
    /// </summary>
    /// <param name="projection">The projection selecting which predicates count as edges.</param>
    /// <returns>The mutual adjacency.</returns>
    private Dictionary<uint, SortedSet<uint>> BuildMutualAdjacency(GraphProjection projection)
    {
        Dictionary<uint, HashSet<uint>> outgoing = [];
        foreach((uint source, uint target) in EnumerateEdges(projection))
        {
            if(source == target)
            {
                continue;
            }

            if(!outgoing.TryGetValue(source, out HashSet<uint>? targets))
            {
                targets = [];
                outgoing[source] = targets;
            }

            targets.Add(target);
        }

        Dictionary<uint, SortedSet<uint>> mutual = [];
        foreach((uint source, HashSet<uint> targets) in outgoing)
        {
            foreach(uint target in targets)
            {
                if(outgoing.TryGetValue(target, out HashSet<uint>? back) && back.Contains(source))
                {
                    Neighbors(mutual, source).Add(target);
                    Neighbors(mutual, target).Add(source);
                }
            }
        }

        return mutual;
    }

    /// <summary>Validates the clique connectivity is a defined value.</summary>
    /// <param name="connectivity">The connectivity to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="connectivity"/> is not a defined value.</exception>
    private static void ValidateConnectivity(CliqueConnectivity connectivity)
    {
        if(connectivity is not (CliqueConnectivity.Undirected or CliqueConnectivity.Mutual))
        {
            throw new ArgumentOutOfRangeException(nameof(connectivity), connectivity, "Clique connectivity must be Undirected or Mutual.");
        }
    }

    /// <summary>
    /// Counts the undirected triangles by the node-iterator algorithm: each triangle {u, v, w} with u &lt; v &lt; w
    /// is counted once, at the pair (u, v) where v is a neighbor of u above u, as the neighbors common to u and v
    /// that are above v.
    /// </summary>
    /// <param name="adjacency">The undirected adjacency.</param>
    /// <returns>The triangle count.</returns>
    private static long CountTriangles(Dictionary<uint, SortedSet<uint>> adjacency)
    {
        long triangles = 0;
        foreach((uint node, SortedSet<uint> neighbors) in adjacency)
        {
            foreach(uint neighbor in neighbors)
            {
                if(neighbor > node)
                {
                    triangles += CountCommonAbove(neighbors, adjacency[neighbor], neighbor);
                }
            }
        }

        return triangles;
    }

    /// <summary>The number of values above <paramref name="threshold"/> present in both sets, iterating the smaller and probing the larger.</summary>
    /// <param name="first">A neighbor set.</param>
    /// <param name="second">The other neighbor set.</param>
    /// <param name="threshold">The exclusive lower bound a common value must exceed.</param>
    /// <returns>The count of common values above the threshold.</returns>
    private static long CountCommonAbove(SortedSet<uint> first, SortedSet<uint> second, uint threshold)
    {
        (SortedSet<uint> smaller, SortedSet<uint> larger) = first.Count <= second.Count ? (first, second) : (second, first);
        long count = 0;
        foreach(uint value in smaller)
        {
            if(value > threshold && larger.Contains(value))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The number of length-two paths (connected triples): the sum over nodes of "degree choose two".</summary>
    /// <param name="adjacency">The undirected adjacency.</param>
    /// <returns>The length-two-path count.</returns>
    private static long CountLengthTwoPaths(Dictionary<uint, SortedSet<uint>> adjacency)
    {
        long paths = 0;
        foreach(SortedSet<uint> neighbors in adjacency.Values)
        {
            long degree = neighbors.Count;
            paths += degree * (degree - 1) / 2;
        }

        return paths;
    }

    /// <summary>The local clustering coefficient of one node: twice the edges among its neighbors over the neighbor-pair count, or zero when it has fewer than two neighbors.</summary>
    /// <param name="node">The node's encoded id.</param>
    /// <param name="adjacency">The undirected adjacency.</param>
    /// <returns>The coefficient in <c>[0, 1]</c>.</returns>
    private static double LocalCoefficient(uint node, Dictionary<uint, SortedSet<uint>> adjacency)
    {
        if(!adjacency.TryGetValue(node, out SortedSet<uint>? neighbors) || neighbors.Count < 2)
        {
            return 0.0;
        }

        long links = 0;
        foreach(uint first in neighbors)
        {
            SortedSet<uint> firstNeighbors = adjacency[first];
            foreach(uint second in neighbors)
            {
                if(second > first && firstNeighbors.Contains(second))
                {
                    links++;
                }
            }
        }

        long degree = neighbors.Count;

        return 2.0 * links / (degree * (degree - 1));
    }

    /// <summary>Returns <paramref name="node"/>'s dense id, allocating a fresh one (and a union-find singleton) the first time it is seen.</summary>
    /// <param name="node">The node's encoded id.</param>
    /// <param name="denseByNode">The encoded-id to dense-id map, appended to on a first sight.</param>
    /// <param name="nodeByDense">The dense-id to encoded-id list, kept in step with the union-find ids.</param>
    /// <param name="components">The union-find handing out dense ids.</param>
    /// <returns>The node's dense id.</returns>
    private static int GetOrAddDense(uint node, Dictionary<uint, int> denseByNode, List<uint> nodeByDense, NodeUnionFind components)
    {
        if(denseByNode.TryGetValue(node, out int dense))
        {
            return dense;
        }

        dense = components.Add();
        denseByNode[node] = dense;
        nodeByDense.Add(node);

        return dense;
    }

    /// <summary>Returns <paramref name="node"/>'s dense PageRank id, allocating a fresh one (and an empty out-neighbor set) the first time it is seen.</summary>
    /// <param name="node">The node's encoded id.</param>
    /// <param name="denseByNode">The encoded-id to dense-id map, appended to on a first sight.</param>
    /// <param name="nodeByDense">The dense-id to encoded-id list.</param>
    /// <param name="outNeighbors">The per-node out-neighbor sets, grown in step with the dense ids.</param>
    /// <returns>The node's dense id.</returns>
    private static int GetOrAddPageRankNode(uint node, Dictionary<uint, int> denseByNode, List<uint> nodeByDense, List<HashSet<int>> outNeighbors)
    {
        if(denseByNode.TryGetValue(node, out int dense))
        {
            return dense;
        }

        dense = nodeByDense.Count;
        denseByNode[node] = dense;
        nodeByDense.Add(node);
        outNeighbors.Add([]);

        return dense;
    }

    /// <summary>
    /// One power-iteration step: spread each node's rank over its out-neighbors, redistribute the dangling mass
    /// (nodes with no out-edge) uniformly, and add the teleport base, so the result stays summed to one.
    /// </summary>
    /// <param name="rank">The current per-node rank.</param>
    /// <param name="next">The span the next per-node rank is written into (one entry per node); fully overwritten, so its prior contents are irrelevant.</param>
    /// <param name="outNeighbors">The per-node out-neighbor sets.</param>
    /// <param name="dampingFactor">The rank-follows-an-edge probability.</param>
    private static void PageRankStep(ReadOnlySpan<double> rank, Span<double> next, List<HashSet<int>> outNeighbors, double dampingFactor)
    {
        int nodeCount = rank.Length;
        double danglingMass = 0.0;
        for(int i = 0; i < nodeCount; i++)
        {
            if(outNeighbors[i].Count == 0)
            {
                danglingMass += rank[i];
            }
        }

        double teleport = ((1.0 - dampingFactor) + (dampingFactor * danglingMass)) / nodeCount;
        next.Fill(teleport);

        for(int i = 0; i < nodeCount; i++)
        {
            HashSet<int> targets = outNeighbors[i];
            if(targets.Count == 0)
            {
                continue;
            }

            double share = dampingFactor * rank[i] / targets.Count;
            foreach(int target in targets)
            {
                next[target] += share;
            }
        }
    }

    /// <summary>Groups the dense nodes by their union-find root into deterministic components — each component's nodes ascending, the components ordered by their smallest node.</summary>
    /// <param name="components">The settled union-find.</param>
    /// <param name="nodeByDense">The dense-id to encoded-id mapping.</param>
    /// <returns>The components.</returns>
    private static List<IReadOnlyList<TermId>> GroupComponents(NodeUnionFind components, List<uint> nodeByDense)
    {
        Dictionary<int, List<TermId>> byRoot = [];
        for(int dense = 0; dense < nodeByDense.Count; dense++)
        {
            int root = components.Find(dense);
            if(!byRoot.TryGetValue(root, out List<TermId>? members))
            {
                members = [];
                byRoot[root] = members;
            }

            members.Add(TermId.FromEncoded(nodeByDense[dense]));
        }

        List<IReadOnlyList<TermId>> result = [];
        foreach(List<TermId> members in byRoot.Values)
        {
            members.Sort();
            result.Add(members);
        }

        result.Sort(static (left, right) => left[0].CompareTo(right[0]));

        return result;
    }
}
