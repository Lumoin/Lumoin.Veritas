using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// A mutable, in-memory directed graph as an adjacency list.
/// Imperative counterpart to the parametric
/// <c>GraphGenerators</c> factories: hand-built with
/// <see cref="AddEdge"/>, exported as a
/// <see cref="GraphSource{TNode}"/> for traversal or persistence.
/// </summary>
/// <remarks>
/// <para>
/// Both this type and the parametric generators target the same
/// output — <see cref="GraphSource{TNode}"/> — so downstream code
/// (traversal primitives, persistence writers) does not care which
/// constructor was used. The split is intentional: this type carries
/// state and supports incremental construction; the generators are
/// stateless functions of their parameters. Trying to fuse them into
/// a single class would mean every parametric graph either
/// materialises into a dictionary upfront (wasteful) or pretends to
/// support <c>AddEdge</c> on top of a function (incoherent).
/// </para>
/// <para>
/// <b>Concurrency.</b> Mutating <see cref="AddEdge"/> while a
/// <see cref="GraphSource{TNode}"/> view is being enumerated is not
/// supported. The intended pattern is "build, then traverse or
/// persist". Concurrent traversal of an immutable graph is fine
/// because the underlying dictionary lookups are safe for read.
/// </para>
/// <para>
/// <b>Backing store.</b> A <see cref="Dictionary{TKey, TValue}"/>
/// keyed on source nodes, with <see cref="List{T}"/> values for
/// targets. The <c>where TNode : IEquatable&lt;TNode&gt;</c>
/// constraint lets the dictionary use the fast equatable comparer.
/// </para>
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
[DebuggerDisplay("AdjacencyList Order={NodeCount} Size={EdgeCount}")]
public sealed class AdjacencyList<TNode>
    where TNode : IEquatable<TNode>
{
    private readonly Dictionary<TNode, List<TNode>> outgoing = [];
    private long edgeCount;

    /// <summary>The number of distinct source nodes (i.e. nodes with at least one outgoing edge).</summary>
    public long NodeCount => outgoing.Count;

    /// <summary>The total number of edges added.</summary>
    public long EdgeCount => edgeCount;

    /// <summary>
    /// Adds a directed edge from <paramref name="source"/> to
    /// <paramref name="target"/>. Duplicate edges are not deduplicated
    /// — adding the same pair twice produces two edges in the
    /// enumerator, which is the right semantic for multigraphs.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <param name="target">The target node.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public AdjacencyList<TNode> AddEdge(TNode source, TNode target)
    {
        if(!outgoing.TryGetValue(source, out List<TNode>? targets))
        {
            targets = [];
            outgoing[source] = targets;
        }
        targets.Add(target);
        edgeCount++;
        return this;
    }

    /// <summary>
    /// Returns a <see cref="GraphSource{TNode}"/> view over this
    /// adjacency list. Both the adjacency and edge-enumeration views
    /// read from the same backing dictionary, so they are consistent
    /// with each other and reflect any prior <see cref="AddEdge"/>
    /// calls.
    /// </summary>
    public GraphSource<TNode> AsGraphSource()
        => new(
            Adjacency: AdjacencyAsync,
            Edges: EdgesAsync,
            KnownOrder: NodeCount,
            KnownSize: EdgeCount);

    //AdjacencyAsync — yields the targets stored for the given node.
    //Method-group converted to the AdjacencyAsync<TNode> delegate.
    private async IAsyncEnumerable<TNode> AdjacencyAsync(
        TNode node, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        if(outgoing.TryGetValue(node, out List<TNode>? targets))
        {
            foreach(TNode target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return target;
            }
        }
    }

    //EdgesAsync — yields every edge across every source. Streams
    //directly from the backing dictionary so the persistence path
    //pays no extra memory cost beyond the dictionary itself.
    private async IAsyncEnumerable<(TNode Source, TNode Target)> EdgesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach(KeyValuePair<TNode, List<TNode>> entry in outgoing)
        {
            foreach(TNode target in entry.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (entry.Key, target);
            }
        }
    }
}
