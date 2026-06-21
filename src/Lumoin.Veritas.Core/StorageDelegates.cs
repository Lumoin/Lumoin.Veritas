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
/// <b>Pattern positions.</b> Match and remove delegates take
/// <see cref="TermId"/> values directly. <see cref="TermId.None"/> means
/// "any value at this position" (the pattern is unbound here); a concrete
/// non-<see cref="TermId.None"/> value means "must match this exact encoded
/// identifier." <see cref="TermId.None"/> equals <c>default(TermId)</c>, so
/// a position parameter that defaults to <c>default</c> is unbound by
/// construction.
/// </para>
/// </remarks>
public static class StorageDelegates
{
    /// <summary>
    /// Matches encoded triples against a pattern. Unbound positions are
    /// <see cref="TermId.None"/>.
    /// </summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any predicate.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    public delegate IAsyncEnumerable<EncodedTriple> MatchTriplesAsync(
        TermId subject,
        TermId predicate,
        TermId @object,
        CancellationToken cancellationToken);

    /// <summary>
    /// Matches encoded triples for the cross-product of a subject set and a
    /// bound predicate, optionally constrained by a bound object. The storage
    /// provider is expected to perform a single predicate-rooted descent
    /// followed by per-subject lookups against the resulting subject mapping,
    /// rather than re-descending per subject.
    /// </summary>
    /// <param name="subjects">The encoded subject identifiers to look up under <paramref name="predicate"/>. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <param name="predicate">The predicate to match. Must be bound (non-<see cref="TermId.None"/>); this primitive exists to amortise a single predicate descent across the subject set.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    /// <remarks>
    /// <para>
    /// <b>Predicate must be bound.</b> Passing <see cref="TermId.None"/> for
    /// <paramref name="predicate"/> has no algorithmic story for this
    /// primitive; the implementation throws
    /// <see cref="ArgumentException"/>. For the all-unbound or
    /// predicate-unbound cases, callers use
    /// <see cref="MatchTriplesAsync"/>.
    /// </para>
    /// <para>
    /// <b>Subject set excludes <see cref="TermId.None"/>.</b> The
    /// <see cref="TermId.None"/> sentinel means "unbound" and has no
    /// meaning as a member of a concrete subject set; the implementation
    /// throws <see cref="ArgumentException"/> on encountering one.
    /// </para>
    /// <para>
    /// <b>Empty set is permitted.</b> An empty
    /// <paramref name="subjects"/> set produces an empty result with no
    /// exception.
    /// </para>
    /// <para>
    /// <b>Duplicates and ordering.</b> The set is not required to be
    /// deduplicated; the implementation is not required to dedupe the
    /// output stream in the presence of duplicate subject keys. Callers
    /// that need distinct results pre-deduplicate the set (a
    /// <see cref="HashSet{T}"/> is sufficient). Output ordering is
    /// unspecified.
    /// </para>
    /// </remarks>
    public delegate IAsyncEnumerable<EncodedTriple> MatchTriplesBySubjectsAsync(
        ReadOnlyMemory<TermId> subjects,
        TermId predicate,
        TermId @object,
        CancellationToken cancellationToken);

    /// <summary>
    /// Matches encoded triples for the cross-product of an object set and a
    /// bound predicate, optionally constrained by a bound subject. Mirror of
    /// <see cref="MatchTriplesBySubjectsAsync"/> across the object position.
    /// </summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The predicate to match. Must be bound (non-<see cref="TermId.None"/>); this primitive exists to amortise a single predicate descent across the object set.</param>
    /// <param name="objects">The encoded object identifiers to look up under <paramref name="predicate"/>. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    /// <remarks>
    /// <para>
    /// The contract on <paramref name="predicate"/>, <paramref name="objects"/>
    /// membership, empty-set tolerance, duplicate-tolerance, and unspecified
    /// ordering matches <see cref="MatchTriplesBySubjectsAsync"/> point for
    /// point; refer to that delegate's remarks.
    /// </para>
    /// </remarks>
    public delegate IAsyncEnumerable<EncodedTriple> MatchTriplesByObjectsAsync(
        TermId subject,
        TermId predicate,
        ReadOnlyMemory<TermId> objects,
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
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any predicate.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of triples removed.</returns>
    public delegate ValueTask<long> RemoveTriplesAsync(
        TermId subject,
        TermId predicate,
        TermId @object,
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

        return new FilteredMatcher(inner, filter).Match;
    }

    /// <summary>
    /// Carries the inner match delegate and visibility filter behind a
    /// <see cref="WithFilter"/> result as explicit state, so the produced
    /// <see cref="MatchTriplesAsync"/> closes over no enclosing local.
    /// </summary>
    /// <param name="inner">The underlying match delegate.</param>
    /// <param name="filter">The visibility filter applied to every yielded triple.</param>
    private sealed class FilteredMatcher(MatchTriplesAsync inner, TripleVisibilityFilter filter)
    {
        /// <summary>The underlying match delegate.</summary>
        private MatchTriplesAsync Inner { get; } = inner;

        /// <summary>The visibility filter applied to every yielded triple.</summary>
        private TripleVisibilityFilter Filter { get; } = filter;

        /// <summary>Matches triples through <see cref="Inner"/>, yielding only those that pass <see cref="Filter"/>.</summary>
        /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
        /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any predicate.</param>
        /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
        /// <param name="cancellationToken">A token to cancel the enumeration.</param>
        /// <returns>An async sequence of visible matching triples.</returns>
        public async IAsyncEnumerable<EncodedTriple> Match(
            TermId subject,
            TermId predicate,
            TermId @object,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach(EncodedTriple triple in Inner(subject, predicate, @object, cancellationToken).ConfigureAwait(false))
            {
                if(Filter(triple.Subject, triple.Predicate, triple.Object))
                {
                    yield return triple;
                }
            }
        }
    }
}
