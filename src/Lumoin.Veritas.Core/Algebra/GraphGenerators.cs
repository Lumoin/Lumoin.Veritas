using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// Parametric factories that produce a
/// <see cref="GraphSource{TNode}"/> from a small set of arguments.
/// </summary>
/// <remarks>
/// <para>
/// Two construction strategies, one output type:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Stateless.</b> <see cref="Path"/>, <see cref="Cycle"/>,
///   <see cref="BinaryTree"/>, <see cref="Tree"/>,
///   <see cref="CompleteGraph"/>, <see cref="Grid"/>, and
///   <see cref="ErdosRenyi"/> compute edges on demand from their
///   parameters. Memory cost is O(1); arbitrary-scale graphs (millions
///   to billions of nodes) work without materialisation. The two
///   <see cref="GraphSource{TNode}"/> views agree because both compute
///   the same pure function.
///   </description></item>
///   <item><description>
///   <b>Stateful.</b> <see cref="BarabasiAlbert"/> and
///   <see cref="WattsStrogatz"/> require the graph in memory during
///   construction (preferential attachment is sequential; small-world
///   rewiring needs the full ring). They materialise an
///   <see cref="AdjacencyList{TNode}"/> once at construction and
///   expose both views over that — memory cost is O(graph), but the
///   persistence path still streams.
///   </description></item>
/// </list>
/// <para>
/// <b>No closures over parameters.</b> Each stateless generator
/// packages its parameters into a <see langword="record struct"/>
/// (<c>PathSpec</c>, <c>ErdosRenyiSpec</c>, etc.) and binds the
/// resulting <see cref="GraphSource{TNode}"/>'s delegates to the
/// struct's instance methods via method group. The delegates capture
/// only the struct itself — never a method parameter — satisfying
/// the project convention.
/// </para>
/// </remarks>
public static class GraphGenerators
{
    /// <summary>
    /// Linear chain <c>0 → 1 → 2 → … → length-1</c>. Pure function;
    /// stateless; <c>O(1)</c> memory.
    /// </summary>
    public static GraphSource<int> Path(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        PathSpec spec = new(length);
        return new GraphSource<int>(
            Adjacency: spec.AdjacencyAsync,
            Edges: spec.EdgesAsync,
            KnownOrder: length,
            KnownSize: length > 0 ? length - 1 : 0);
    }

    /// <summary>
    /// Cycle <c>0 → 1 → … → length-1 → 0</c>.
    /// </summary>
    public static GraphSource<int> Cycle(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        CycleSpec spec = new(length);
        return new GraphSource<int>(
            Adjacency: spec.AdjacencyAsync,
            Edges: spec.EdgesAsync,
            KnownOrder: length,
            KnownSize: length);
    }

    /// <summary>
    /// Complete binary tree of the given depth, root 0.
    /// </summary>
    public static GraphSource<int> BinaryTree(int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        return Tree(branchingFactor: 2, depth: depth);
    }

    /// <summary>
    /// Complete K-ary tree of the given branching factor and depth.
    /// </summary>
    public static GraphSource<int> Tree(int branchingFactor, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(branchingFactor, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        long totalNodes = TotalNodesInTree(branchingFactor, depth);
        TreeSpec spec = new(branchingFactor, totalNodes);

        return new GraphSource<int>(
            Adjacency: spec.AdjacencyAsync,
            Edges: spec.EdgesAsync,
            KnownOrder: totalNodes,
            KnownSize: totalNodes - 1);
    }

    /// <summary>
    /// Complete directed graph K_n.
    /// </summary>
    public static GraphSource<int> CompleteGraph(int order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        CompleteGraphSpec spec = new(order);
        return new GraphSource<int>(
            Adjacency: spec.AdjacencyAsync,
            Edges: spec.EdgesAsync,
            KnownOrder: order,
            KnownSize: (long)order * (order - 1));
    }

    /// <summary>
    /// Two-dimensional grid <c>rows × cols</c>.
    /// </summary>
    public static GraphSource<(int Row, int Col)> Grid(int rows, int cols)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(cols);

        long order = (long)rows * cols;
        long size = (long)rows * (cols - 1 > 0 ? cols - 1 : 0)
                  + (long)(rows - 1 > 0 ? rows - 1 : 0) * cols;

        GridSpec spec = new(rows, cols);
        return new GraphSource<(int Row, int Col)>(
            Adjacency: spec.AdjacencyAsync,
            Edges: spec.EdgesAsync,
            KnownOrder: order,
            KnownSize: size);
    }

    /// <summary>
    /// Erdős–Rényi random graph <c>G(n, p)</c>. Stateless and
    /// streaming despite being random: the random source is consulted
    /// once for a salt, and every edge decision is the deterministic
    /// hash <c>Hash64(source, target, salt)</c>. Both views agree by
    /// construction.
    /// </summary>
    public static GraphSource<int> ErdosRenyi(int n, double edgeProbability, RandomSourceDelegate random)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        ArgumentOutOfRangeException.ThrowIfLessThan(edgeProbability, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(edgeProbability, 1.0);
        ArgumentNullException.ThrowIfNull(random);

        ulong salt = random();
        ulong threshold = ProbabilityToThreshold(edgeProbability);
        ErdosRenyiSpec spec = new(n, salt, threshold);

        return new GraphSource<int>(
            Adjacency: spec.AdjacencyAsync,
            Edges: spec.EdgesAsync,
            KnownOrder: n,
            //Exact edge count would require a full pass; expected
            //value is n*(n-1)*p but reporting null avoids a misleading
            //exact-looking figure.
            KnownSize: null);
    }

    /// <summary>
    /// Barabási–Albert preferential-attachment graph. Stateful;
    /// materialises an adjacency list during construction.
    /// </summary>
    public static GraphSource<int> BarabasiAlbert(int n, int edgesPerNewNode, RandomSourceDelegate random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(edgesPerNewNode, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(n, edgesPerNewNode);
        ArgumentNullException.ThrowIfNull(random);

        AdjacencyList<int> built = BuildBarabasiAlbert(n, edgesPerNewNode, random);

        return built.AsGraphSource();
    }

    /// <summary>
    /// Watts–Strogatz small-world graph. Stateful.
    /// </summary>
    public static GraphSource<int> WattsStrogatz(
        int n, int neighborsPerNode, double rewiringProbability, RandomSourceDelegate random)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(neighborsPerNode, 2);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(n, neighborsPerNode);
        ArgumentOutOfRangeException.ThrowIfLessThan(rewiringProbability, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rewiringProbability, 1.0);
        ArgumentNullException.ThrowIfNull(random);

        if((neighborsPerNode & 1) != 0)
        {
            throw new ArgumentException("Neighbors per node must be even.", nameof(neighborsPerNode));
        }

        AdjacencyList<int> built = BuildWattsStrogatz(n, neighborsPerNode, rewiringProbability, random);

        return built.AsGraphSource();
    }

    /// <summary>
    /// Escape hatch: lift an arbitrary children function into a
    /// <see cref="GraphSource{TNode}"/>. Caller supplies the node
    /// enumeration that drives the edge view.
    /// </summary>
    public static GraphSource<TNode> FromChildrenFunction<TNode>(
        Adjacency<TNode> children,
        IEnumerable<TNode> allNodes)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(allNodes);

        FromChildrenSpec<TNode> spec = new(children, allNodes);

        return new GraphSource<TNode>(Adjacency: spec.AdjacencyAsync, Edges: spec.EdgesAsync);
    }

    //ProbabilityToThreshold — map p ∈ [0, 1] onto the ulong range so
    //per-edge tests are a single integer comparison. Edge exists iff
    //hash < threshold.
    private static ulong ProbabilityToThreshold(double p) => p switch
        {
            <= 0.0 => 0UL,
            >= 1.0 => ulong.MaxValue,
            _ => (ulong)(p * ulong.MaxValue),
        };


    //HashEdge — xxHash64(source ‖ target ‖ salt). Deterministic across
    //platforms and architectures.
    private static ulong HashEdge(int source, int target, ulong salt)
    {
        Span<byte> buffer = stackalloc byte[20];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer[..4], source);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), target);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(8, 8), salt);
        //Trailing 4 bytes are stable padding.
        return XxHash64.HashToUInt64(buffer);
    }

    //Sum of geometric series 1 + k + k^2 + ... + k^depth.
    private static long TotalNodesInTree(int branchingFactor, int depth)
    {
        if(branchingFactor == 1)
        {
            return depth + 1;
        }

        long power = 1;
        for(int i = 0; i <= depth; i++)
        {
            power *= branchingFactor;
        }

        return (power - 1) / (branchingFactor - 1);
    }

    //Builds the BA graph. Standard urn-based preferential attachment:
    //each existing-edge endpoint occupies one urn slot, so sampling
    //uniformly gives degree-weighted probability.
    private static AdjacencyList<int> BuildBarabasiAlbert(int n, int m, RandomSourceDelegate random)
    {
        AdjacencyList<int> graph = new();
        List<int> attachmentUrn = new(2 * n * m);

        //Seed: complete graph on m nodes.
        for(int i = 0; i < m; i++)
        {
            for(int j = 0; j < m; j++)
            {
                if(i == j)
                {
                    continue;
                }
                graph.AddEdge(i, j);
            }

            for(int k = 0; k < m - 1; k++)
            {
                attachmentUrn.Add(i);
            }
        }

        HashSet<int> chosen = new(capacity: m);
        for(int newNode = m; newNode < n; newNode++)
        {
            chosen.Clear();
            while(chosen.Count < m)
            {
                ulong roll = random();
                int target = attachmentUrn[(int)(roll % (ulong)attachmentUrn.Count)];
                if(target == newNode || !chosen.Add(target))
                {
                    continue;
                }
            }

            foreach(int target in chosen)
            {
                graph.AddEdge(newNode, target);
                graph.AddEdge(target, newNode);
                attachmentUrn.Add(newNode);
                attachmentUrn.Add(target);
            }
        }

        return graph;
    }

    //Builds the WS graph: ring lattice plus per-edge rewiring.
    private static AdjacencyList<int> BuildWattsStrogatz(int n, int k, double rewiringProbability, RandomSourceDelegate random)
    {
        int half = k / 2;

        List<(int Source, int Target)> edges = new(n * k);
        for(int i = 0; i < n; i++)
        {
            for(int offset = 1; offset <= half; offset++)
            {
                int neighbor = (i + offset) % n;
                edges.Add((i, neighbor));
                edges.Add((neighbor, i));
            }
        }

        ulong threshold = ProbabilityToThreshold(rewiringProbability);
        HashSet<(int, int)> existing = new(edges.Count);
        foreach((int s, int t) in edges)
        {
            existing.Add((s, t));
        }

        AdjacencyList<int> graph = new();
        for(int e = 0; e < edges.Count; e++)
        {
            (int source, int target) = edges[e];
            ulong roll = random();
            if(roll < threshold)
            {
                int attempts = 0;
                while(attempts < 32)
                {
                    int candidate = (int)(random() % (ulong)n);
                    if(candidate != source && !existing.Contains((source, candidate)))
                    {
                        existing.Remove((source, target));
                        existing.Add((source, candidate));
                        target = candidate;
                        break;
                    }
                    attempts++;
                }
            }
            graph.AddEdge(source, target);
        }

        return graph;
    }

    //Parameter-holding record structs. Each carries the generator's
    //arguments and exposes AdjacencyAsync + EdgesAsync instance
    //methods matching the AdjacencyAsync<TNode> /
    //EdgeEnumeratorAsync<TNode> delegate shapes. The factory binds
    //these via method group, so the delegates capture only the struct
    //itself — never a method parameter.

    private readonly record struct PathSpec(int Length)
    {
        public async IAsyncEnumerable<int> AdjacencyAsync(int node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            if(node >= 0 && node < Length - 1)
            {
                yield return node + 1;
            }
        }

        public async IAsyncEnumerable<(int Source, int Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            for(int i = 0; i < Length - 1; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (i, i + 1);
            }
        }
    }

    private readonly record struct CycleSpec(int Length)
    {
        public async IAsyncEnumerable<int> AdjacencyAsync(
            int node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if(node >= 0 && node < Length)
            {
                yield return (node + 1) % Length;
            }
        }

        public async IAsyncEnumerable<(int Source, int Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            for(int i = 0; i < Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (i, (i + 1) % Length);
            }
        }
    }

    private readonly record struct TreeSpec(int BranchingFactor, long TotalNodes)
    {
        public async IAsyncEnumerable<int> AdjacencyAsync(
            int node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(node < 0 || node >= TotalNodes)
            {
                yield break;
            }

            long firstChild = (long)BranchingFactor * node + 1;
            for(int i = 0; i < BranchingFactor; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long child = firstChild + i;
                if(child >= TotalNodes)
                {
                    yield break;
                }

                yield return (int)child;
            }
        }

        public async IAsyncEnumerable<(int Source, int Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            for(long parent = 0; parent < TotalNodes; parent++)
            {
                long firstChild = (long)BranchingFactor * parent + 1;
                for(int i = 0; i < BranchingFactor; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long child = firstChild + i;
                    if(child >= TotalNodes)
                    {
                        break;
                    }

                    yield return ((int)parent, (int)child);
                }
            }
        }
    }

    private readonly record struct CompleteGraphSpec(int Order)
    {
        public async IAsyncEnumerable<int> AdjacencyAsync(
            int node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(node < 0 || node >= Order)
            {
                yield break;
            }

            for(int j = 0; j < Order; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(j != node)
                {
                    yield return j;
                }
            }
        }

        public async IAsyncEnumerable<(int Source, int Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            for(int i = 0; i < Order; i++)
            {
                for(int j = 0; j < Order; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(i != j)
                    {
                        yield return (i, j);
                    }
                }
            }
        }
    }

    private readonly record struct GridSpec(int Rows, int Cols)
    {
        public async IAsyncEnumerable<(int Row, int Col)> AdjacencyAsync(
            (int Row, int Col) node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(node.Row < 0 || node.Row >= Rows || node.Col < 0 || node.Col >= Cols)
            {
                yield break;
            }
            cancellationToken.ThrowIfCancellationRequested();

            if(node.Col + 1 < Cols)
            {
                yield return (node.Row, node.Col + 1);
            }

            if(node.Row + 1 < Rows)
            {
                yield return (node.Row + 1, node.Col);
            }
        }

        public async IAsyncEnumerable<((int Row, int Col) Source, (int Row, int Col) Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            for(int r = 0; r < Rows; r++)
            {
                for(int c = 0; c < Cols; c++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(c + 1 < Cols)
                    {
                        yield return ((r, c), (r, c + 1));
                    }

                    if(r + 1 < Rows)
                    {
                        yield return ((r, c), (r + 1, c));
                    }
                }
            }
        }
    }

    private readonly record struct ErdosRenyiSpec(int N, ulong Salt, ulong Threshold)
    {
        public async IAsyncEnumerable<int> AdjacencyAsync(
            int node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(node < 0 || node >= N)
            {
                yield break;
            }

            for(int j = 0; j < N; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if(j == node)
                {
                    continue;
                }
                if(HashEdge(node, j, Salt) < Threshold)
                {
                    yield return j;
                }
            }
        }

        public async IAsyncEnumerable<(int Source, int Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            for(int i = 0; i < N; i++)
            {
                for(int j = 0; j < N; j++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if(i == j)
                    {
                        continue;
                    }
                    if(HashEdge(i, j, Salt) < Threshold)
                    {
                        yield return (i, j);
                    }
                }
            }
        }
    }

    //FromChildrenSpec is a sealed record class because the IEnumerable
    //and Func references force boxing anyway; record struct gains
    //nothing here.
    private sealed record FromChildrenSpec<TNode>(
        Adjacency<TNode> Children,
        IEnumerable<TNode> AllNodes)
        where TNode : IEquatable<TNode>
    {
        public async IAsyncEnumerable<TNode> AdjacencyAsync(
            TNode node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            foreach(TNode child in Children(node))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return child;
            }
        }

        public async IAsyncEnumerable<(TNode Source, TNode Target)> EdgesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            foreach(TNode source in AllNodes)
            {
                foreach(TNode target in Children(source))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return (source, target);
                }
            }
        }
    }
}
