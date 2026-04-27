using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// Generic graph traversal primitives parameterised over node and,
/// optionally, edge-label types.
/// </summary>
/// <remarks>
/// <para>
/// Each primitive is provided in two forms: a <em>labeled</em> form
/// taking a <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/> and an
/// explicit label, and an <em>unlabeled</em> form taking an
/// <see cref="AdjacencyAsync{TNode}"/>. Prefer the labeled form for
/// graphs whose edges carry a discriminating label — RDF predicates,
/// SKOS relations, OWL roles — because it preserves label-based
/// pushdown to the storage layer. Use the unlabeled form for graphs
/// where the traversal topology is fully determined by the node type
/// itself, such as shape-graph recursion or tree walks.
/// </para>
/// <para>
/// All primitives use explicit <see cref="Stack{T}"/> or
/// <see cref="Queue{T}"/> for frontier management with no recursion,
/// and de-duplicate through a <see cref="HashSet{T}"/> of visited
/// nodes so cycles in the graph do not cause non-termination. The
/// adjacency delegate is invoked at most once per distinct node (per
/// label, in the labeled form).
/// </para>
/// <para>
/// The <c>where TNode : IEquatable&lt;TNode&gt;</c> constraint ensures
/// <see cref="HashSet{T}"/> and <see cref="Dictionary{TKey, TValue}"/>
/// use the fast <c>IEquatable</c>-based comparer rather than reflecting
/// over <see cref="object.Equals(object)"/>. Record structs
/// auto-satisfy this.
/// </para>
/// </remarks>
public static class TraversalPrimitives
{
    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="start"/> by
    /// following edges labeled <paramref name="label"/>, excluding the
    /// start node itself. Breadth-first.
    /// </summary>
    /// <remarks>
    /// Equivalent to the SPARQL / SHACL property path <c>label+</c>
    /// restricted to the component rooted at <paramref name="start"/>.
    /// For the reflexive-transitive closure <c>label*</c>, prepend
    /// <paramref name="start"/> to the result.
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TLabel">The edge label type.</typeparam>
    /// <param name="start">The starting node.</param>
    /// <param name="label">The edge label to follow.</param>
    /// <param name="adjacency">The labeled adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of reachable nodes, excluding <paramref name="start"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> TransitiveClosureAsync<TNode, TLabel>(
        TNode start,
        TLabel label,
        LabeledAdjacencyAsync<TNode, TLabel> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(adjacency);
        return LabeledTransitiveClosureCore(start, label, adjacency, cancellationToken);
    }

    /// <summary>
    /// Enumerates all nodes reachable from <paramref name="start"/>
    /// through the given <paramref name="adjacency"/>, excluding the
    /// start node itself. Breadth-first.
    /// </summary>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="start">The starting node.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of reachable nodes, excluding <paramref name="start"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<TNode> TransitiveClosureAsync<TNode>(
        TNode start,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(adjacency);
        return UnlabeledTransitiveClosureCore(start, adjacency, cancellationToken);
    }

    /// <summary>
    /// Determines whether <paramref name="target"/> is reachable from
    /// <paramref name="start"/> by following edges labeled
    /// <paramref name="label"/>.
    /// </summary>
    /// <remarks>
    /// The start node is never considered reachable from itself, even
    /// when <paramref name="target"/> equals <paramref name="start"/>
    /// and a cycle exists that returns to it. For reflexive
    /// reachability, test equality against <paramref name="start"/>
    /// separately before calling.
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TLabel">The edge label type.</typeparam>
    /// <param name="start">The starting node.</param>
    /// <param name="target">The target node.</param>
    /// <param name="label">The edge label to follow.</param>
    /// <param name="adjacency">The labeled adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns><c>true</c> if <paramref name="target"/> is reachable; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <c>null</c>.</exception>
    public static async ValueTask<bool> IsReachableAsync<TNode, TLabel>(
        TNode start,
        TNode target,
        TLabel label,
        LabeledAdjacencyAsync<TNode, TLabel> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(adjacency);
        await foreach(TNode reached in LabeledTransitiveClosureCore(start, label, adjacency, cancellationToken).ConfigureAwait(false))
        {
            if(reached.Equals(target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether <paramref name="target"/> is reachable from
    /// <paramref name="start"/> through the given
    /// <paramref name="adjacency"/>.
    /// </summary>
    /// <remarks>
    /// See the labeled overload's remarks for reflexivity semantics.
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="start">The starting node.</param>
    /// <param name="target">The target node.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns><c>true</c> if <paramref name="target"/> is reachable; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <c>null</c>.</exception>
    public static async ValueTask<bool> IsReachableAsync<TNode>(
        TNode start,
        TNode target,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(adjacency);
        await foreach(TNode reached in UnlabeledTransitiveClosureCore(start, adjacency, cancellationToken).ConfigureAwait(false))
        {
            if(reached.Equals(target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the shortest path from <paramref name="start"/> to
    /// <paramref name="target"/> along edges labeled
    /// <paramref name="label"/>, as the sequence of nodes visited, or
    /// <c>null</c> if no path exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The returned list begins with <paramref name="start"/> and ends
    /// with <paramref name="target"/>. A length-1 list is returned
    /// when <paramref name="start"/> equals <paramref name="target"/>.
    /// Breadth-first search with a parent map for reconstruction.
    /// </para>
    /// </remarks>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <typeparam name="TLabel">The edge label type.</typeparam>
    /// <param name="start">The starting node.</param>
    /// <param name="target">The target node.</param>
    /// <param name="label">The edge label to follow.</param>
    /// <param name="adjacency">The labeled adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>The shortest path, or <c>null</c> if unreachable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <c>null</c>.</exception>
    public static async ValueTask<IReadOnlyList<TNode>?> ShortestPathAsync<TNode, TLabel>(
        TNode start,
        TNode target,
        TLabel label,
        LabeledAdjacencyAsync<TNode, TLabel> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        if(start.Equals(target))
        {
            return [start];
        }

        Dictionary<TNode, TNode> parent = new() { [start] = start };
        Queue<TNode> frontier = new();
        frontier.Enqueue(start);

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TNode current = frontier.Dequeue();

            await foreach(TNode next in adjacency(current, label, cancellationToken).ConfigureAwait(false))
            {
                if(parent.ContainsKey(next))
                {
                    continue;
                }

                parent[next] = current;

                if(next.Equals(target))
                {
                    return ReconstructPath(parent, start, target);
                }

                frontier.Enqueue(next);
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the shortest path from <paramref name="start"/> to
    /// <paramref name="target"/> through the given
    /// <paramref name="adjacency"/>.
    /// </summary>
    /// <typeparam name="TNode">The node identifier type.</typeparam>
    /// <param name="start">The starting node.</param>
    /// <param name="target">The target node.</param>
    /// <param name="adjacency">The adjacency delegate.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>The shortest path, or <c>null</c> if unreachable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="adjacency"/> is <c>null</c>.</exception>
    public static async ValueTask<IReadOnlyList<TNode>?> ShortestPathAsync<TNode>(
        TNode start,
        TNode target,
        AdjacencyAsync<TNode> adjacency,
        CancellationToken cancellationToken = default)
        where TNode : IEquatable<TNode>
    {
        ArgumentNullException.ThrowIfNull(adjacency);

        if(start.Equals(target))
        {
            return [start];
        }

        Dictionary<TNode, TNode> parent = new() { [start] = start };
        Queue<TNode> frontier = new();
        frontier.Enqueue(start);

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TNode current = frontier.Dequeue();

            await foreach(TNode next in adjacency(current, cancellationToken).ConfigureAwait(false))
            {
                if(parent.ContainsKey(next))
                {
                    continue;
                }

                parent[next] = current;

                if(next.Equals(target))
                {
                    return ReconstructPath(parent, start, target);
                }

                frontier.Enqueue(next);
            }
        }

        return null;
    }

    //Core breadth-first traversal for the labeled form. The visited
    //set seeds with the start node so it is never re-yielded even if
    //a cycle returns to it.
    private static async IAsyncEnumerable<TNode> LabeledTransitiveClosureCore<TNode, TLabel>(
        TNode start,
        TLabel label,
        LabeledAdjacencyAsync<TNode, TLabel> adjacency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TNode : IEquatable<TNode>
    {
        HashSet<TNode> visited = [start];
        Queue<TNode> frontier = new();
        frontier.Enqueue(start);

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TNode current = frontier.Dequeue();

            await foreach(TNode next in adjacency(current, label, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(next))
                {
                    frontier.Enqueue(next);
                    yield return next;
                }
            }
        }
    }

    //Core breadth-first traversal for the unlabeled form.
    private static async IAsyncEnumerable<TNode> UnlabeledTransitiveClosureCore<TNode>(
        TNode start,
        AdjacencyAsync<TNode> adjacency,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TNode : IEquatable<TNode>
    {
        HashSet<TNode> visited = [start];
        Queue<TNode> frontier = new();
        frontier.Enqueue(start);

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TNode current = frontier.Dequeue();

            await foreach(TNode next in adjacency(current, cancellationToken).ConfigureAwait(false))
            {
                if(visited.Add(next))
                {
                    frontier.Enqueue(next);
                    yield return next;
                }
            }
        }
    }

    //Walks the parent map backwards from target to start, then reverses
    //through a Stack so the returned list reads from start to target.
    private static List<TNode> ReconstructPath<TNode>(Dictionary<TNode, TNode> parent, TNode start, TNode target)
        where TNode : IEquatable<TNode>
    {
        Stack<TNode> reversed = new();
        TNode cursor = target;
        while(!cursor.Equals(start))
        {
            reversed.Push(cursor);
            cursor = parent[cursor];
        }

        List<TNode> path = new(reversed.Count + 1) { start };
        while(reversed.Count > 0)
        {
            path.Add(reversed.Pop());
        }

        return path;
    }
}
