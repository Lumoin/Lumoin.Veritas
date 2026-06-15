using System;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The byte source backing a column's packed payload — the seam that
/// decouples a column from where its bytes live, so the payload can
/// come from process memory or another byte source behind one read
/// contract.
/// </summary>
/// <remarks>
/// <para>
/// The seam is a concrete base with sealed cases rather than an
/// interface so the read path devirtualizes and a backend can own its
/// bytes' lifetime.
/// </para>
/// <para>
/// The read op is <see cref="TryGetMemory"/>: it hands the whole
/// column as one <see cref="ReadOnlyMemory{T}"/> of <c>ulong</c>
/// words, resolved once when the column opens. A column stores that
/// handle and slices <see cref="ReadOnlyMemory{T}.Span"/> at each
/// read — on a managed backing that span is the array fast path, so
/// no virtual call falls on the per-value path. <c>Try</c> is part of
/// the contract: a source that cannot hand out a single contiguous
/// view returns <see langword="false"/>, and the caller reads per
/// block instead.
/// </para>
/// </remarks>
public abstract class ColumnSource
{
    /// <summary>An empty source — no words, a zero-length view in every accessor.</summary>
    public static readonly ColumnSource Empty = new InMemoryColumnSource([]);

    /// <summary>The number of payload bytes this source backs.</summary>
    public abstract int LengthInBytes { get; }

    /// <summary>The payload as one contiguous view of raw bytes.</summary>
    public abstract ReadOnlySpan<byte> Bytes { get; }

    /// <summary>Hands the whole column as one contiguous <c>ulong</c> view, resolved once at column-open.</summary>
    /// <param name="memory">Receives the whole-column view, or an empty view when the source cannot provide one.</param>
    /// <returns><see langword="true"/> when the source handed a contiguous view; <see langword="false"/> when the caller must read per block instead.</returns>
    public abstract bool TryGetMemory(out ReadOnlyMemory<ulong> memory);
}
