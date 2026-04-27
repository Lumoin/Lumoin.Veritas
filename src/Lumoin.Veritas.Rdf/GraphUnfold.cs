using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Anamorphism (unfold) that generates triples from a seed value by repeated
/// application of a <see cref="GraphAlgebras.GraphCoalgebra{TSeed}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The unfold expands the initial seed into a triple and a list of neighbour
/// seeds, then expands those neighbours, and so on until no further seeds are
/// produced. Triples are yielded as they are discovered, enabling streaming
/// consumption without materialising the entire subgraph.
/// </para>
/// <para>
/// The traversal is breadth-first by default, using an explicit queue. A seed
/// that the coalgebra maps to a <see cref="GraphExpansion{TSeed}"/> with a null
/// triple still has its neighbour seeds enqueued, allowing logical grouping
/// nodes that do not themselves produce triples.
/// </para>
/// <para>
/// No cycle detection is performed on seeds. If the coalgebra can produce the
/// same seed more than once the unfold will emit the same subtree more than
/// once. Consumers that need deduplication should carry a visited set inside
/// the seed or wrap the output stream with a <c>Distinct</c> operator.
/// </para>
/// </remarks>
public static class GraphUnfold
{
    /// <summary>
    /// Unfolds <paramref name="seed"/> into a stream of triples by applying
    /// <paramref name="coalgebra"/> breadth-first.
    /// </summary>
    /// <typeparam name="TSeed">The seed value type.</typeparam>
    /// <param name="seed">The initial seed.</param>
    /// <param name="coalgebra">The per-seed expansion step.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of generated triples.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="coalgebra"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<EncodedTriple> UnfoldAsync<TSeed>(
        TSeed seed,
        GraphAlgebras.GraphCoalgebra<TSeed> coalgebra,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coalgebra);
        return UnfoldCore(seed, coalgebra, cancellationToken);
    }

    private static async IAsyncEnumerable<EncodedTriple> UnfoldCore<TSeed>(
        TSeed seed,
        GraphAlgebras.GraphCoalgebra<TSeed> coalgebra,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Queue<TSeed> frontier = new();
        frontier.Enqueue(seed);

        while(frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TSeed current = frontier.Dequeue();

            GraphExpansion<TSeed> expansion = coalgebra(current);

            if(expansion.Triple.HasValue)
            {
                yield return expansion.Triple.Value;
            }

            foreach(TSeed next in expansion.Seeds)
            {
                frontier.Enqueue(next);
            }
        }

        //The method is `async` because of the IAsyncEnumerable return type with
        //[EnumeratorCancellation]; a trivial completed-task await keeps the state
        //machine happy when no asynchronous work is performed by the coalgebra.
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
