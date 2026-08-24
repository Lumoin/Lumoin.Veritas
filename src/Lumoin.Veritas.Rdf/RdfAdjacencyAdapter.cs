using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Algebra;
using Lumoin.Veritas.Core.Encoding;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Adapts a <see cref="StorageDelegates.MatchTriplesAsync"/> to the
/// generic adjacency shapes expected by
/// <see cref="Lumoin.Veritas.Core.Algebra"/> primitives.
/// </summary>
/// <remarks>
/// <para>
/// The adapter holds the match delegate in a typed property and
/// exposes methods whose signatures match
/// <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/> and
/// <see cref="AdjacencyAsync{TNode}"/>. Callers supply these as
/// delegates via method-group conversion (for example,
/// <c>adapter.ForwardAsync</c>), which produces a delegate bound to
/// the adapter instance and its captured match delegate. This is not
/// a lambda closure over a parameter — the captured state is an
/// explicit field on a named type.
/// </para>
/// <para>
/// Three adjacency methods are offered corresponding to the three
/// traversal directions needed by the SHACL and SKOS layers above:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <see cref="ForwardAsync"/> — forward along a single predicate,
///   yielding objects. Used for <c>rdfs:subClassOf</c> closures,
///   <c>skos:broader</c> walks, property-path <c>+</c> evaluation.
///   </description></item>
///   <item><description>
///   <see cref="BackwardAsync"/> — backward along a single predicate,
///   yielding subjects. Used for <c>skos:narrower</c>-style walks and
///   inverse property paths.
///   </description></item>
///   <item><description>
///   <see cref="AnyForwardAsync"/> — forward along any predicate,
///   yielding objects. Used for general reachability queries and
///   shape-graph walks that do not discriminate by predicate.
///   </description></item>
/// </list>
/// <para>
/// The adapter is a <see langword="record"/> for value equality on
/// the captured match delegate, which is occasionally useful in tests
/// and cache keys. Construction is cheap and allocation-light; there
/// is no per-call cost beyond the method-group delegate creation at
/// the consuming site.
/// </para>
/// </remarks>
/// <param name="Match">The storage match delegate to wrap.</param>
public sealed record RdfAdjacencyAdapter(StorageDelegates.MatchTriplesAsync Match)
{
    /// <summary>
    /// Labeled forward adjacency. Matches
    /// <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/> with
    /// <c>TNode = TermId</c>, <c>TLabel = IriId</c>.
    /// </summary>
    /// <param name="source">The subject to walk from.</param>
    /// <param name="predicate">The predicate to follow.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of object term identifiers.</returns>
    public async IAsyncEnumerable<TermId> ForwardAsync(
        TermId source,
        IriId predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple triple in Match(source, predicate.Value, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            yield return triple.Object;
        }
    }

    /// <summary>
    /// Labeled backward adjacency. Matches
    /// <see cref="LabeledAdjacencyAsync{TNode, TLabel}"/> with
    /// <c>TNode = TermId</c>, <c>TLabel = IriId</c>, walking from
    /// object to subject.
    /// </summary>
    /// <param name="target">The object to walk back from.</param>
    /// <param name="predicate">The predicate to follow.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of subject term identifiers.</returns>
    public async IAsyncEnumerable<TermId> BackwardAsync(
        TermId target,
        IriId predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple triple in Match(TermId.None, predicate.Value, target, cancellationToken).ConfigureAwait(false))
        {
            yield return triple.Subject;
        }
    }

    /// <summary>
    /// Unlabeled forward adjacency — follow any predicate. Matches
    /// <see cref="AdjacencyAsync{TNode}"/> with <c>TNode = TermId</c>.
    /// </summary>
    /// <param name="source">The subject to walk from.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of object term identifiers.</returns>
    public async IAsyncEnumerable<TermId> AnyForwardAsync(
        TermId source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple triple in Match(source, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            yield return triple.Object;
        }
    }
}
