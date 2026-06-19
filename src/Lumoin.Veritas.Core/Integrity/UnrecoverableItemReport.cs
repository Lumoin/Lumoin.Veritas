using System;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// A named report of state the persistence layer could not recover — the concrete face of
/// <see cref="PersistenceInvariant.LossIsNamed"/>. A fenced store surfaces one of these rather than
/// returning damaged data as if it were intact, so a caller learns exactly what was lost and at what
/// granularity (<see cref="UnrecoverableItemReportKind"/>).
/// </summary>
/// <remarks>
/// <para>
/// The durable journal produces the <see cref="UnrecoverableItemReportKind.OperationRange"/> case on
/// replay when a torn or corrupt tail truncates the log: the recovered prefix is intact and the
/// boundary names what was dropped. The item-set and contested cases are produced by later tiers (the
/// repair ladder and the replication arc); their fields are introduced with those tiers.
/// </para>
/// </remarks>
public sealed class UnrecoverableItemReport
{
    /// <summary>Creates a report.</summary>
    /// <param name="kind">The granularity of the loss.</param>
    /// <param name="recoveredThroughSequence">For an operation-range loss, the sequence number of the last intact operation, or -1 when none was recovered.</param>
    /// <param name="discardedByteCount">For an operation-range loss, the number of trailing bytes discarded past the intact boundary.</param>
    /// <param name="commitGeneration">For an item-set loss, the manifest commit generation the lost items belonged to, or -1 when not applicable.</param>
    /// <param name="lostItemStart">For an item-set loss, the index of the first lost system-of-record item, or -1 when not applicable.</param>
    /// <param name="lostItemCount">For an item-set loss, the number of contiguous system-of-record items lost, or 0 when not applicable.</param>
    private UnrecoverableItemReport(UnrecoverableItemReportKind kind, long recoveredThroughSequence, long discardedByteCount, long commitGeneration, long lostItemStart, long lostItemCount)
    {
        Kind = kind;
        RecoveredThroughSequence = recoveredThroughSequence;
        DiscardedByteCount = discardedByteCount;
        CommitGeneration = commitGeneration;
        LostItemStart = lostItemStart;
        LostItemCount = lostItemCount;
    }

    /// <summary>The granularity of the loss.</summary>
    public UnrecoverableItemReportKind Kind { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.OperationRange"/> loss, the sequence number of the last intact operation the log was recovered through, or -1 when no operation survived.</summary>
    public long RecoveredThroughSequence { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.OperationRange"/> loss, the number of trailing bytes discarded past the intact boundary.</summary>
    public long DiscardedByteCount { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.ItemSet"/> loss, the manifest commit generation the lost items belonged to; -1 for other kinds.</summary>
    public long CommitGeneration { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.ItemSet"/> loss, the index of the first lost system-of-record item; -1 for other kinds.</summary>
    public long LostItemStart { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.ItemSet"/> loss, the number of contiguous system-of-record items lost; 0 for other kinds.</summary>
    public long LostItemCount { get; }

    /// <summary>Creates an operation-range loss report: the log was recovered through <paramref name="recoveredThroughSequence"/> and <paramref name="discardedByteCount"/> trailing bytes past the intact boundary were discarded.</summary>
    /// <param name="recoveredThroughSequence">The sequence number of the last intact operation, or -1 when none survived.</param>
    /// <param name="discardedByteCount">The number of trailing bytes discarded; must be positive (an operation-range loss discarded something).</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recoveredThroughSequence"/> is below -1, or <paramref name="discardedByteCount"/> is not positive.</exception>
    public static UnrecoverableItemReport OperationRange(long recoveredThroughSequence, long discardedByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recoveredThroughSequence, -1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discardedByteCount);

        return new UnrecoverableItemReport(UnrecoverableItemReportKind.OperationRange, recoveredThroughSequence, discardedByteCount, commitGeneration: -1, lostItemStart: -1, lostItemCount: 0);
    }

    /// <summary>Creates an item-set loss report: <paramref name="lostItemCount"/> contiguous system-of-record items starting at <paramref name="lostItemStart"/>, belonging to commit generation <paramref name="commitGeneration"/>, could not be reconstructed by any repair source up to its capacity. The terminal rung of the repair ladder names this rather than dropping the items silently.</summary>
    /// <param name="commitGeneration">The manifest commit generation the lost items belonged to; not negative.</param>
    /// <param name="lostItemStart">The index of the first lost item; not negative.</param>
    /// <param name="lostItemCount">The number of contiguous items lost; must be positive (an item-set loss lost something).</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="commitGeneration"/> or <paramref name="lostItemStart"/> is negative, or <paramref name="lostItemCount"/> is not positive.</exception>
    public static UnrecoverableItemReport ItemSet(long commitGeneration, long lostItemStart, long lostItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commitGeneration);
        ArgumentOutOfRangeException.ThrowIfNegative(lostItemStart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lostItemCount);

        return new UnrecoverableItemReport(UnrecoverableItemReportKind.ItemSet, recoveredThroughSequence: -1, discardedByteCount: 0, commitGeneration, lostItemStart, lostItemCount);
    }
}
