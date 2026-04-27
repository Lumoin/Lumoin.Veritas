using System.Collections.Generic;
using System.Threading;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// A generic graph adjacency — given a node, yields its outward neighbours.
/// </summary>
/// <remarks>
/// <para>
/// This is the minimal interface a graph must satisfy for the traversal
/// primitives in this namespace to operate on it. No edge label, no edge
/// payload, just "from here, where can I go next." Suitable for graphs
/// whose edges are not distinguished by label — for example, a shape
/// graph where the traversal topology is fixed by the shape operators
/// (<c>sh:node</c>, <c>sh:property</c>, <c>sh:and</c>, …) and there is
/// no separate label space to parameterize over.
/// </para>
/// <para>
/// For labeled graphs, prefer <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/>.
/// The labeled form preserves the label as a first-class parameter,
/// which is essential for storage layers that can push a label
/// predicate down to an index — a plain <see cref="AdjacencyAsync{TNode}"/>
/// forces such a storage layer to materialise every outgoing edge and
/// post-filter, losing the pushdown opportunity.
/// </para>
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <param name="node">The node whose outward neighbours to enumerate.</param>
/// <param name="cancellationToken">A token to cancel the enumeration.</param>
/// <returns>An async sequence of neighbour node identifiers.</returns>
public delegate IAsyncEnumerable<TNode> AdjacencyAsync<TNode>(
    TNode node, CancellationToken cancellationToken);

/// <summary>
/// The synchronous counterpart of <see cref="AdjacencyAsync{TNode}"/>:
/// given a node, yields its outward neighbours without any I/O hop.
/// </summary>
/// <remarks>
/// <para>
/// Used by traversal primitives that walk a graph held entirely in
/// memory — adjacency is a pure function of the node, with no I/O
/// along the way. Forcing such a walk through the async-typed
/// <see cref="AdjacencyAsync{TNode}"/> would propagate async coloring
/// into purely synchronous code paths for no benefit. The sync
/// shape exists alongside the async shape so traversal primitives
/// can be picked by evaluation discipline rather than by API
/// availability.
/// </para>
/// <para>
/// For graphs whose adjacency genuinely requires I/O — pulling
/// triples from a snapshot, dereferencing a remote resource, or
/// reading from a journal — use <see cref="AdjacencyAsync{TNode}"/>
/// instead.
/// </para>
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <param name="node">The node whose outward neighbours to enumerate.</param>
/// <returns>A sequence of neighbour node identifiers.</returns>
public delegate IEnumerable<TNode> Adjacency<TNode>(TNode node);

/// <summary>
/// A labeled graph adjacency — given a node and an edge label, yields
/// the outward neighbours reachable along edges with that label.
/// </summary>
/// <remarks>
/// <para>
/// This is the primary adjacency shape for RDF, SKOS, OWL, and any
/// other graph whose edges carry a discriminating label (a predicate,
/// an association type, a role). The label is surfaced as a parameter
/// rather than folded into the graph structure so that storage layers
/// can use it directly — a POS-indexed triple store, a Parquet zone
/// map sorted by predicate, and a per-predicate k2-tree all route the
/// query differently depending on the label, and wrapping the label
/// into the delegate instance would hide that opportunity from the
/// storage layer.
/// </para>
/// <para>
/// For graphs without a meaningful label space, see
/// <see cref="AdjacencyAsync{TNode}"/>.
/// </para>
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <typeparam name="TLabel">The edge label type.</typeparam>
/// <param name="source">The node whose outward neighbours to enumerate.</param>
/// <param name="label">The edge label to follow.</param>
/// <param name="cancellationToken">A token to cancel the enumeration.</param>
/// <returns>An async sequence of neighbour node identifiers.</returns>
public delegate IAsyncEnumerable<TNode> LabeledAdjacencyAsync<TNode, TLabel>(TNode source, TLabel label, CancellationToken cancellationToken);
