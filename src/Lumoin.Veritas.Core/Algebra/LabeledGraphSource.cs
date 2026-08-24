using System.Diagnostics;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// Labeled counterpart to <see cref="GraphSource{TNode}"/>. Pairs a
/// <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/> with a
/// <see cref="LabeledEdgeEnumeratorAsync{TNode, TLabel}"/>.
/// </summary>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <typeparam name="TLabel">The edge label type.</typeparam>
/// <param name="Adjacency">Query-style labeled adjacency view.</param>
/// <param name="Edges">Streaming labeled-edge enumeration view.</param>
/// <param name="KnownOrder">Node count, if known cheaply.</param>
/// <param name="KnownSize">Edge count, if known cheaply.</param>
[DebuggerDisplay("LabeledGraphSource Order={KnownOrder} Size={KnownSize}")]
public sealed record LabeledGraphSource<TNode, TLabel>(
    LabeledAdjacencyAsync<TNode, TLabel> Adjacency,
    LabeledEdgeEnumeratorAsync<TNode, TLabel> Edges,
    long? KnownOrder = null,
    long? KnownSize = null);
