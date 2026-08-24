using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// A non-empty-or-empty wrapper around an <see cref="EncodedTriple"/>
/// array whose construction enforces the invariant "distinct and
/// lexicographically sorted by (Subject, Predicate, Object)". The
/// wrapper has no setters and exposes the array only through
/// <see cref="AsSpan"/>, so once a callee accepts a
/// <see cref="DistinctSortedTriples"/> it can take dedup and sort for
/// granted without re-checking.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a dedicated type rather than passing a sorted array.</b> The
/// build path uses three orderings (S-first, P-first, O-first), each
/// derived from the same canonical SPO ordering. A bare
/// <see cref="EncodedTriple"/>[] gives no compile-time signal about
/// which ordering — if any — the caller has materialised, so every
/// recipient ends up paranoia-deduping or paranoia-sorting "just in
/// case". The named type pushes that guarantee into the type system
/// and lets recipients elide redundant work.
/// </para>
/// <para>
/// <b>Construction cost.</b> <see cref="Create(IEnumerable{EncodedTriple})"/>
/// allocates a <see cref="HashSet{T}"/> for dedup, copies its members
/// into an array, then sorts the array via
/// <see cref="EncodedTriple.CompareTo(EncodedTriple)"/>. The cost is
/// O(n) average for dedup plus O(n log n) for the sort, with one
/// transient hashset's worth of allocations.
/// </para>
/// </remarks>
[DebuggerDisplay("DistinctSortedTriples Count={Count,nq}")]
internal sealed class DistinctSortedTriples
{
    private readonly EncodedTriple[] sorted;

    private DistinctSortedTriples(EncodedTriple[] sorted)
    {
        this.sorted = sorted;
    }

    /// <summary>The number of distinct triples in the wrapper.</summary>
    public int Count => sorted.Length;

    /// <summary><c>true</c> when the wrapper holds no triples.</summary>
    public bool IsEmpty => sorted.Length == 0;

    /// <summary>
    /// Returns a read-only view over the sorted, distinct triple
    /// array. The span is valid for the lifetime of this wrapper.
    /// </summary>
    /// <returns>A span over the underlying array, ordered by (Subject, Predicate, Object).</returns>
    public ReadOnlySpan<EncodedTriple> AsSpan() => sorted;

    /// <summary>
    /// Returns the underlying sorted array as a reference.
    /// <b>Caller contract:</b> never mutate the returned array — the
    /// wrapper's distinct-and-sorted invariants are baked into every
    /// downstream recipient. This accessor exists so internal build
    /// paths that need to derive index permutations through struct
    /// comparators can capture the array reference; comparators
    /// implementing <see cref="IComparer{T}"/> cannot capture a
    /// <see cref="ReadOnlySpan{T}"/> in a field.
    /// </summary>
    /// <returns>The underlying array, ordered by (Subject, Predicate, Object).</returns>
    internal EncodedTriple[] AsArray() => sorted;

    /// <summary>
    /// Materialises a sorted, deduplicated triple set from any
    /// triple enumeration. The input is enumerated exactly once.
    /// </summary>
    /// <param name="triples">The triples to deduplicate and sort.</param>
    /// <returns>A wrapper holding the distinct triples in lexicographic SPO order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    public static DistinctSortedTriples Create(IEnumerable<EncodedTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(triples);

        HashSet<EncodedTriple> seen = new(triples);
        if(seen.Count == 0)
        {
            return new DistinctSortedTriples([]);
        }

        EncodedTriple[] sorted = new EncodedTriple[seen.Count];
        seen.CopyTo(sorted);
        Array.Sort(sorted);

        return new DistinctSortedTriples(sorted);
    }

    /// <summary>
    /// Materialises a sorted, deduplicated triple set from a span.
    /// Equivalent to the enumerable overload, but available to
    /// callers that already hold the triples in a contiguous buffer.
    /// </summary>
    /// <param name="triples">The triples to deduplicate and sort.</param>
    /// <returns>A wrapper holding the distinct triples in lexicographic SPO order.</returns>
    public static DistinctSortedTriples Create(ReadOnlySpan<EncodedTriple> triples)
    {
        if(triples.IsEmpty)
        {
            return new DistinctSortedTriples([]);
        }

        HashSet<EncodedTriple> seen = new(capacity: triples.Length);
        foreach(EncodedTriple triple in triples)
        {
            seen.Add(triple);
        }

        EncodedTriple[] sorted = new EncodedTriple[seen.Count];
        seen.CopyTo(sorted);
        Array.Sort(sorted);

        return new DistinctSortedTriples(sorted);
    }
}
