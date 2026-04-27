using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Delegate definitions for pluggable graph storage operations.
/// </summary>
/// <remarks>
/// <para>
/// These delegates form the storage abstraction layer. Each delegate represents
/// a single operation. Storage providers compose these into complete backends.
/// Consumers depend only on the delegate signatures, not on provider types.
/// </para>
/// <para>
/// This design enables independently testable "parameters in, results out" functions.
/// A PostgreSQL provider, an in-memory provider, and a FASTER-backed provider
/// all implement the same delegate signatures with entirely different internals.
/// </para>
/// <para>
/// <b>Pattern positions.</b> Match and remove delegates take nullable
/// <see cref="TermId"/> values — <c>null</c> means "any," a value means "must
/// match this encoded identifier." Typing the pattern positions as
/// <see cref="TermId"/> rather than raw <see cref="long"/> makes it explicit
/// at call sites that these values must come from a <see cref="TermDictionary"/>.
/// </para>
/// </remarks>
public static class StorageDelegates
{
    /// <summary>
    /// Matches encoded triples against a pattern. Unbound positions are <c>null</c>.
    /// </summary>
    /// <param name="subject">The subject to match, or <c>null</c> for any subject.</param>
    /// <param name="predicate">The predicate to match, or <c>null</c> for any predicate.</param>
    /// <param name="object">The object to match, or <c>null</c> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    public delegate IAsyncEnumerable<EncodedTriple> MatchTriplesAsync(
        TermId? subject,
        TermId? predicate,
        TermId? @object,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a batch of encoded triples into the store.
    /// </summary>
    /// <param name="triples">The triples to insert.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of triples actually inserted (excluding duplicates).</returns>
    public delegate ValueTask<long> InsertTriplesAsync(
        ReadOnlyMemory<EncodedTriple> triples,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes all triples matching the given pattern.
    /// </summary>
    /// <param name="subject">The subject to match, or <c>null</c> for any subject.</param>
    /// <param name="predicate">The predicate to match, or <c>null</c> for any predicate.</param>
    /// <param name="object">The object to match, or <c>null</c> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of triples removed.</returns>
    public delegate ValueTask<long> RemoveTriplesAsync(
        TermId? subject,
        TermId? predicate,
        TermId? @object,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the total number of triples in the store.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The triple count.</returns>
    public delegate ValueTask<long> CountTriplesAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Filters triples based on authorization context. Returns <c>true</c> if the
    /// triple should be visible to the current consumer.
    /// </summary>
    /// <param name="subject">The subject identifier of the triple.</param>
    /// <param name="predicate">The predicate identifier of the triple.</param>
    /// <param name="object">The object identifier of the triple.</param>
    /// <returns><c>true</c> if the triple passes the visibility check; otherwise, <c>false</c>.</returns>
    public delegate bool TripleVisibilityFilter(
        TermId subject,
        TermId predicate,
        TermId @object);

    /// <summary>
    /// Wraps a <see cref="MatchTriplesAsync"/> delegate with a visibility filter.
    /// </summary>
    /// <remarks>
    /// When <paramref name="filter"/> is <c>null</c>, the original delegate is returned
    /// with zero overhead. When present, every yielded triple is checked against the filter.
    /// </remarks>
    /// <param name="inner">The underlying match delegate.</param>
    /// <param name="filter">The visibility filter, or <c>null</c> for no filtering.</param>
    /// <returns>A filtered match delegate.</returns>
    public static MatchTriplesAsync WithFilter(MatchTriplesAsync inner, TripleVisibilityFilter? filter)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if(filter is null)
        {
            return inner;
        }

        return (s, p, o, ct) => FilteredMatch(inner, filter, s, p, o, ct);
    }

    private static async IAsyncEnumerable<EncodedTriple> FilteredMatch(
        MatchTriplesAsync inner,
        TripleVisibilityFilter filter,
        TermId? subject,
        TermId? predicate,
        TermId? @object,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple triple in inner(subject, predicate, @object, cancellationToken).ConfigureAwait(false))
        {
            if(filter(triple.Subject, triple.Predicate, triple.Object))
            {
                yield return triple;
            }
        }
    }
}
