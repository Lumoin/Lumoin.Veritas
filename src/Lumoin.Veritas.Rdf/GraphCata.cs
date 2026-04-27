using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Unified entry point for catamorphisms over the graph.
/// </summary>
/// <remarks>
/// <para>
/// <c>GraphCata</c> offers two overloads of <see cref="FoldAsync"/>, each
/// accepting a different algebra type. C# overload resolution picks the
/// matching implementation:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       Pass a <see cref="GraphAlgebras.GraphAlgebra{TResult}"/> for plain
///       full-evaluation fold. All children are reduced before each node's
///       algebra runs. Lightest per-call overhead; use when every child
///       result is always needed.
///     </description>
///   </item>
///   <item>
///     <description>
///       Pass a <see cref="GraphAlgebras.GraphKAlgebra{TResult}"/> for
///       selective child evaluation via <see cref="ForceRequest"/> yields.
///       Use when some children can be skipped (SHACL boolean combinators,
///       early termination, etc.). One iterator state-machine allocation
///       per node; worth it when short-circuiting saves more work than it
///       costs.
///     </description>
///   </item>
/// </list>
/// <para>
/// A single algebra that always forces every child produces a result
/// identical to the plain fold. This means consumers can start with
/// <see cref="GraphAlgebras.GraphKAlgebra{TResult}"/> when unsure and
/// switch to the plain algebra later as an optimization with no semantic
/// change.
/// </para>
/// </remarks>
public static class GraphCata
{
    /// <summary>
    /// Folds the reachable subgraph using a plain
    /// <see cref="GraphAlgebras.GraphAlgebra{TResult}"/>. Delegates to
    /// <see cref="GraphFold.FoldAsync"/>.
    /// </summary>
    /// <typeparam name="TResult">The fold result type.</typeparam>
    /// <param name="rootNodeId">The encoded identifier of the root node.</param>
    /// <param name="algebra">The per-node reduction step.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">Cancellation token for the whole operation.</param>
    /// <returns>The algebra's result for the root node.</returns>
    public static ValueTask<TResult> FoldAsync<TResult>(
        TermId rootNodeId,
        GraphAlgebras.GraphAlgebra<TResult> algebra,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        return GraphFold.FoldAsync(rootNodeId, algebra, match, cancellationToken);
    }

    /// <summary>
    /// Folds the reachable subgraph using a
    /// <see cref="GraphAlgebras.GraphKAlgebra{TResult}"/> with selective
    /// child evaluation. Delegates to <see cref="GraphKFold.FoldAsync"/>.
    /// </summary>
    /// <typeparam name="TResult">The fold result type.</typeparam>
    /// <param name="rootNodeId">The encoded identifier of the root node.</param>
    /// <param name="algebra">The iterator-based algebra.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="pool">
    /// Optional memory pool reserved for future driver-side allocations.
    /// May be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the whole operation.</param>
    /// <returns>The algebra's result for the root node.</returns>
    public static ValueTask<TResult> FoldAsync<TResult>(
        TermId rootNodeId,
        GraphAlgebras.GraphKAlgebra<TResult> algebra,
        StorageDelegates.MatchTriplesAsync match,
        VeritasMemoryPool<byte>? pool = null,
        CancellationToken cancellationToken = default)
    {
        return GraphKFold.FoldAsync(rootNodeId, algebra, match, pool, cancellationToken);
    }
}
