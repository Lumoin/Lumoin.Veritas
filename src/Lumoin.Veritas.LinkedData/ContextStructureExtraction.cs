using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.LinkedData;

/// <summary>
/// Adjacency and deduplication helpers for
/// <see cref="LinkedDataTermSource"/> graphs in scoped-context walks.
/// Designed for use with the project's
/// <c>Lumoin.Veritas.Core.Algebra.IterativeTraversal</c> primitives:
/// the static methods here can be supplied directly as the
/// <c>adjacency</c> and <c>keyOf</c> arguments to
/// <c>DepthFirstAsync</c> et al.
/// </summary>
public static class ContextStructureExtraction
{
    /// <summary>
    /// Adjacency: the neighbours of a <see cref="LinkedDataTermSource"/>
    /// are the term sources defined inside its scoped context. A term
    /// source with no scoped context (or whose scoped context contains
    /// only remote-URL or reset entries) is a leaf.
    /// </summary>
    /// <param name="node">The term source whose neighbours to enumerate.</param>
    /// <param name="cancellationToken">Cancellation token; honoured between yielded items.</param>
    /// <returns>The scoped-context term sources reachable from <paramref name="node"/>.</returns>
    public static IAsyncEnumerable<LinkedDataTermSource> ScopedTermAdjacencyAsync(
        LinkedDataTermSource node,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ScopedTermAdjacencyCore(node, cancellationToken);
    }

    private static async IAsyncEnumerable<LinkedDataTermSource> ScopedTermAdjacencyCore(
        LinkedDataTermSource node,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        if(node.ScopedContext is null)
        {
            yield break;
        }
        foreach(LinkedDataContextEntry entry in node.ScopedContext)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(entry.Terms is null)
            {
                continue;
            }
            foreach(LinkedDataTermSource scopedTerm in entry.Terms.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return scopedTerm;
            }
        }
    }

    /// <summary>
    /// Deduplication key: returns the term source's
    /// <see cref="LinkedDataTermSource.SyntheticKey"/>. Per the
    /// extraction discipline, synthetic keys are unique within a single
    /// extraction run, so this is a sufficient dedup key for
    /// graph-traversal walks.
    /// </summary>
    /// <param name="node">The term source.</param>
    /// <returns>The synthetic key.</returns>
    public static string ScopedTermKey(LinkedDataTermSource node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.SyntheticKey;
    }
}
