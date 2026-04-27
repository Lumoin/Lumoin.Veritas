using System.Diagnostics;

namespace Lumoin.Veritas.Core.Algebra;

/// <summary>
/// A graph as a pair of views: an
/// <see cref="AdjacencyAsync{TNode}"/> for query-style traversal and
/// an <see cref="EdgeEnumeratorAsync{TNode}"/> for streaming
/// enumeration. Optional size hints support pre-sizing buffers and
/// reporting progress.
/// </summary>
/// <remarks>
/// <para>
/// The same <see cref="GraphSource{TNode}"/> instance feeds both
/// traversal primitives (via <see cref="Adjacency"/>) and persistence
/// writers (via <see cref="Edges"/>). Stateless generators
/// (<c>Path</c>, <c>ErdosRenyi</c>, etc.) build both views as pure
/// functions of their parameters, so the two views are guaranteed to
/// describe the same graph by construction. Stateful generators
/// (<c>BarabasiAlbert</c>, <c>WattsStrogatz</c>, manually-built
/// <see cref="AdjacencyList{TNode}"/>) build their internal structure
/// once at construction and expose both views over the same backing
/// storage.
/// </para>
/// <para>
/// <b>Streaming.</b> The persistence writers iterate
/// <see cref="Edges"/> exactly once with constant working memory.
/// Generators that can produce edges on the fly do so without
/// materialisation; generators whose algorithms require the graph in
/// memory (preferential attachment, small-world rewiring) materialise
/// once at construction and stream their output from there. Either
/// way, the writer's memory cost is bounded.
/// </para>
/// <para>
/// <b>Size hints.</b> <see cref="KnownOrder"/> is the node count;
/// <see cref="KnownSize"/> is the edge count. Either may be
/// <see langword="null"/> when the generator cannot cheaply report
/// it. Hints support file-header pre-allocation, progress bars, and
/// dictionary capacity sizing — they are advisory, not authoritative.
/// </para>
/// </remarks>
/// <typeparam name="TNode">The node identifier type.</typeparam>
/// <param name="Adjacency">Query-style adjacency view.</param>
/// <param name="Edges">Streaming enumeration view.</param>
/// <param name="KnownOrder">Node count, if known cheaply; otherwise <see langword="null"/>.</param>
/// <param name="KnownSize">Edge count, if known cheaply; otherwise <see langword="null"/>.</param>
[DebuggerDisplay("GraphSource Order={KnownOrder} Size={KnownSize}")]
public sealed record GraphSource<TNode>(
    AdjacencyAsync<TNode> Adjacency,
    EdgeEnumeratorAsync<TNode> Edges,
    long? KnownOrder = null,
    long? KnownSize = null);

