using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// Iterative depth-first, breadth-first, and post-order traversal
/// primitives over arbitrary graphs. Stack / Queue driven with no
/// recursion, yielding each distinct node exactly once in traversal
/// order.
/// </summary>
/// <remarks>
/// <para>
/// These primitives sit below <see cref="TraversalPrimitives"/>. Where
/// <see cref="TraversalPrimitives"/> answers specific questions
/// (transitive closure, reachability, shortest path) from a single
/// start node, <see cref="IterativeTraversal"/> exposes the raw
/// stack-or-queue-driven walk itself, accepting multiple seeds and
/// letting the caller observe every distinct node it reaches. It is
/// the primitive other graph-walking code composes over when it needs
/// finer control than a single closure operation provides.
/// </para>
/// <para>
/// Each method is offered in two overloads. The <em>key-based</em>
/// overload accepts an explicit <c>Func&lt;TNode, TKey&gt; keyOf</c>
/// and deduplicates on the returned key, which allows the visited set
/// to track a stable identity even when <typeparamref name="TNode"/>
/// itself is a composite value (for example, a pair of a shape and a
/// focus node, where visitation is properly keyed on the shape id).
/// The <em>equality-based</em> overload deduplicates on
/// <typeparamref name="TNode"/> directly and is available when
/// <typeparamref name="TNode"/> implements
/// <see cref="IEquatable{T}"/>.
/// </para>
/// <para>
/// The <c>where TKey : notnull</c> constraint prevents
/// <see cref="HashSet{T}"/> from silently coalescing null keys on
/// reference types. For record structs and value types this is
/// automatic; for reference types the caller is responsible for
/// returning non-null keys.
/// </para>
/// <para>
/// Methods are offered as both <em>async</em> (taking
/// <see cref="AdjacencyAsync{TNode}"/>) and <em>sync</em> (taking
/// <see cref="Adjacency{TNode}"/>). The async shape is for graphs
/// whose adjacency requires I/O; the sync shape is for graphs held
/// entirely in memory, where forcing a walk through async would
/// propagate async coloring through synchronous code paths for no
/// benefit. Pick the shape that matches the adjacency the caller
/// already has.
/// </para>
/// </remarks>
public static class IterativeTraversal
{
    /// <summary>
    /// Depth-first traversal from a set of seeds through the given
    /// adjacency, yielding each distinct node (by
    /// <paramref name="keyOf"/>) exactly once in DFS discovery order.
    /// </summary>
    /// <remarks>
    /// Seeds are pushed in the order given; processing order is
    /// determined by the <see cref="Stack{T}"/>'s LIFO discipline,
    /// which means the last-pushed seed's subtree is explored first.
    /// Seeds themselves are yielded on first discovery and their
    /// neighbours are then pushed.
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TKey">The dedupe key type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="keyOf">Extracts the dedupe key from a node.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of distinct nodes in DFS discovery order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> DepthFirstAsync<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(adjacency);
        return DepthFirstCore(seeds, keyOf, adjacency, cancellationToken);
    }

    /// <summary>
    /// Depth-first traversal from a set of seeds through the given
    /// adjacency, using <typeparamref name="TNode"/>'s own equality
    /// for de-duplication.
    /// </summary>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of distinct nodes in DFS discovery order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> DepthFirstAsync<TNode>(
        IEnumerable<TNode> seeds,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(adjacency);
        return DepthFirstCore(seeds, IdentityKey, adjacency, cancellationToken);
    }

    /// <summary>
    /// Breadth-first traversal from a set of seeds through the given
    /// adjacency, yielding each distinct node (by
    /// <paramref name="keyOf"/>) exactly once in BFS discovery order.
    /// </summary>
    /// <remarks>
    /// All seeds are enqueued first, in the order given, so seeds
    /// themselves form level zero and are yielded before any seed's
    /// neighbour.
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TKey">The dedupe key type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="keyOf">Extracts the dedupe key from a node.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of distinct nodes in BFS discovery order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> BreadthFirstAsync<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(adjacency);
        return BreadthFirstCore(seeds, keyOf, adjacency, cancellationToken);
    }

    /// <summary>
    /// Breadth-first traversal from a set of seeds through the given
    /// adjacency, using <typeparamref name="TNode"/>'s own equality
    /// for de-duplication.
    /// </summary>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of distinct nodes in BFS discovery order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> BreadthFirstAsync<TNode>(
        IEnumerable<TNode> seeds,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(adjacency);
        return BreadthFirstCore(seeds, IdentityKey, adjacency, cancellationToken);
    }

    /// <summary>
    /// Post-order traversal from a set of seeds through the given
    /// async adjacency, yielding each distinct node (by
    /// <paramref name="keyOf"/>) exactly once after every reachable
    /// descendant of that node has been yielded. Suitable for
    /// bottom-up computations whose value at a node depends on the
    /// already-computed values at every reachable descendant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented as the canonical two-stack idiom: a discovery
    /// pass walks every reachable node onto an intermediate stack,
    /// dedup-ing during discovery so a heavily-shared DAG visits
    /// each node once; the intermediate stack is then drained in
    /// reverse-discovery order, which is a valid post-order for the
    /// reachable subgraph. For DAGs (not just trees) the order is a
    /// topological order with sinks first.
    /// </para>
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TKey">The dedupe key type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="keyOf">Extracts the dedupe key from a node.</param>
    /// <param name="adjacency">The async adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of distinct nodes in post-order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> PostOrderAsync<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(adjacency);
        return PostOrderAsyncCore(seeds, keyOf, adjacency, cancellationToken);
    }

    /// <summary>
    /// Post-order traversal from a set of seeds through the given
    /// async adjacency, using <typeparamref name="TNode"/>'s own
    /// equality for de-duplication.
    /// </summary>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="adjacency">The async adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of distinct nodes in post-order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> PostOrderAsync<TNode>(
        IEnumerable<TNode> seeds,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(adjacency);
        return PostOrderAsyncCore(seeds, IdentityKey, adjacency, cancellationToken);
    }

    /// <summary>
    /// Post-order traversal from a set of seeds through the given
    /// synchronous adjacency, yielding each distinct node (by
    /// <paramref name="keyOf"/>) exactly once after every reachable
    /// descendant of that node has been yielded. Use this overload
    /// for graphs held entirely in memory where the adjacency is a
    /// pure function of the node and async coloring would buy
    /// nothing.
    /// </summary>
    /// <remarks>
    /// See the remarks on
    /// <see cref="PostOrderAsync{TNode, TKey}(IEnumerable{TNode}, Func{TNode, TKey}, AdjacencyAsync{TNode}, CancellationToken)"/>
    /// for the two-stack idiom this overload also implements; the
    /// only difference is the synchronous adjacency contract.
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TKey">The dedupe key type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="keyOf">Extracts the dedupe key from a node.</param>
    /// <param name="adjacency">The synchronous adjacency delegate.</param>
    /// <returns>A sequence of distinct nodes in post-order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IEnumerable<TNode> PostOrder<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        Adjacency<TNode> adjacency)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(adjacency);
        return PostOrderCore(seeds, keyOf, adjacency);
    }

    /// <summary>
    /// Post-order traversal from a set of seeds through the given
    /// synchronous adjacency, using <typeparamref name="TNode"/>'s
    /// own equality for de-duplication.
    /// </summary>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="seeds">The seed nodes to start from.</param>
    /// <param name="adjacency">The synchronous adjacency delegate.</param>
    /// <returns>A sequence of distinct nodes in post-order.</returns>
    /// <exception cref="ArgumentNullException">Any required parameter is <c>null</c>.</exception>
    public static IEnumerable<TNode> PostOrder<TNode>(
        IEnumerable<TNode> seeds,
        Adjacency<TNode> adjacency)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(adjacency);
        return PostOrderCore(seeds, IdentityKey, adjacency);
    }

    //Returns the node itself as the dedupe key. Used when TNode
    //implements IEquatable<TNode> and the caller does not want to
    //provide an explicit keyOf delegate.
    private static TNode IdentityKey<TNode>(TNode node) where TNode : IEquatable<TNode> => node;

    //Core DFS loop. Pushes each seed (skipping duplicates), then pops
    //and yields each node on its first discovery, pushing its
    //neighbours in enumeration order. LIFO ordering means the
    //last-pushed child is popped and expanded first.
    private static async IAsyncEnumerable<TNode> DepthFirstCore<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        AdjacencyAsync<TNode> adjacency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TKey : notnull
    {
        HashSet<TKey> visited = [];
        Stack<TNode> stack = new();

        foreach(TNode seed in seeds)
        {
            if(visited.Add(keyOf(seed)))
            {
                stack.Push(seed);
            }
        }

        while(stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TNode current = stack.Pop();
            yield return current;

            await foreach(TNode next in adjacency(current, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(keyOf(next)))
                {
                    stack.Push(next);
                }
            }
        }
    }

    //Core BFS loop. All seeds are enqueued first so they form level
    //zero; dequeue-and-expand proceeds level by level through the
    //FIFO queue.
    private static async IAsyncEnumerable<TNode> BreadthFirstCore<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        AdjacencyAsync<TNode> adjacency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TKey : notnull
    {
        HashSet<TKey> visited = [];
        Queue<TNode> queue = new();

        foreach(TNode seed in seeds)
        {
            if(visited.Add(keyOf(seed)))
            {
                queue.Enqueue(seed);
            }
        }

        while(queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TNode current = queue.Dequeue();
            yield return current;

            await foreach(TNode next in adjacency(current, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(keyOf(next)))
                {
                    queue.Enqueue(next);
                }
            }
        }
    }

    //Core async post-order loop. Marker-based traversal: each
    //node is pushed twice — once as a pre-marker and once as a
    //post-marker. Pre-markers expand the node's adjacency; post-
    //markers yield the node. Pushing the post-marker before the
    //children's pre-markers ensures the children's post-markers
    //pop (and thus yield) before this node's. Discovery-time
    //dedup on the visited set keeps stack space at O(N) for a
    //DAG with N reachable nodes.
    //
    //A naive variant that yields directly on pre-marker pop and
    //relies on a second stack for emit-order would produce the
    //wrong order on a DAG: a node reachable via two paths gets
    //pushed onto the emit stack at first discovery, but a parent
    //reached later sits ABOVE it in the stack and would be
    //yielded first. The marker idiom avoids this by deferring the
    //yield to the moment all of the node's children are
    //demonstrably processed (their post-markers have all popped).
    private static async IAsyncEnumerable<TNode> PostOrderAsyncCore<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        AdjacencyAsync<TNode> adjacency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TKey : notnull
    {
        HashSet<TKey> visited = [];
        Stack<(TNode Node, bool PostMarker)> stack = new();

        foreach(TNode seed in seeds)
        {
            if(visited.Add(keyOf(seed)))
            {
                stack.Push((seed, PostMarker: false));
            }
        }

        while(stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (TNode node, bool isPostMarker) = stack.Pop();

            if(isPostMarker)
            {
                yield return node;
                continue;
            }

            //Pre-marker: schedule this node's post-marker so it
            //pops after every child's post-marker, then push every
            //unseen child as a pre-marker.
            stack.Push((node, PostMarker: true));

            await foreach(TNode next in adjacency(node, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(keyOf(next)))
                {
                    stack.Push((next, PostMarker: false));
                }
            }
        }
    }

    //Core sync post-order loop. Same marker-based traversal as
    //PostOrderAsyncCore but with a synchronous adjacency contract,
    //so neither the discovery walk nor the yield involves any
    //async machinery. See PostOrderAsyncCore's remarks for the
    //correctness argument.
    private static IEnumerable<TNode> PostOrderCore<TNode, TKey>(
        IEnumerable<TNode> seeds,
        KeyOfDelegate<TNode, TKey> keyOf,
        Adjacency<TNode> adjacency)
        where TKey : notnull
    {
        HashSet<TKey> visited = [];
        Stack<(TNode Node, bool PostMarker)> stack = new();

        foreach(TNode seed in seeds)
        {
            if(visited.Add(keyOf(seed)))
            {
                stack.Push((seed, PostMarker: false));
            }
        }

        while(stack.Count > 0)
        {
            (TNode node, bool isPostMarker) = stack.Pop();

            if(isPostMarker)
            {
                yield return node;
                continue;
            }

            stack.Push((node, PostMarker: true));

            foreach(TNode next in adjacency(node))
            {
                if(visited.Add(keyOf(next)))
                {
                    stack.Push((next, PostMarker: false));
                }
            }
        }
    }
}
