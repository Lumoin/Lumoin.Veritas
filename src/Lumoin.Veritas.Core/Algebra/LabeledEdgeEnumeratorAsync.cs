using System.Collections.Generic;
using System.Threading;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// Streaming enumeration of all edges in a graph as
/// <c>(source, target)</c> pairs.
/// </summary>
/// <remarks>
/// <para>
/// Counterpart to <see cref="AdjacencyAsync{TNode}"/>: where adjacency
/// answers "given this node, what are its neighbours?", the edge
/// enumerator answers "give me every edge". Both views describe the
/// same graph and must agree — for a stateless generator this is
/// guaranteed by construction (both views compute from the same pure
/// function); for a stateful generator (manually built or
/// preferential-attachment), both views read from the same backing
/// structure.
/// </para>
/// <para>
/// The enumerator is the streaming-write surface. A persistence
/// writer iterates this once and emits one line per edge with no
/// materialisation, so multi-billion-edge graphs stream to disk at
/// I/O speed with constant working memory.
/// </para>
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <param name="cancellationToken">A token to cancel the enumeration.</param>
/// <returns>An async sequence of <c>(source, target)</c> edge pairs.</returns>
public delegate IAsyncEnumerable<(TNode Source, TNode Target)> EdgeEnumeratorAsync<TNode>(
    CancellationToken cancellationToken);

/// <summary>
/// Streaming enumeration of all labeled edges in a graph as
/// <c>(source, label, target)</c> triples.
/// </summary>
/// <remarks>
/// Counterpart to
/// <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/>. See
/// <see cref="EdgeEnumeratorAsync{TNode}"/> for the rationale.
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <typeparam name="TLabel">The edge label type.</typeparam>
/// <param name="cancellationToken">A token to cancel the enumeration.</param>
/// <returns>An async sequence of labeled edge triples.</returns>
public delegate IAsyncEnumerable<(TNode Source, TLabel Label, TNode Target)> LabeledEdgeEnumeratorAsync<TNode, TLabel>(
    CancellationToken cancellationToken);
