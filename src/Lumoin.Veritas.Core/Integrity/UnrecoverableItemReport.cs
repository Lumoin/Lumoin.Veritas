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
    private UnrecoverableItemReport(UnrecoverableItemReportKind kind, long recoveredThroughSequence, long discardedByteCount)
    {
        Kind = kind;
        RecoveredThroughSequence = recoveredThroughSequence;
        DiscardedByteCount = discardedByteCount;
    }

    /// <summary>The granularity of the loss.</summary>
    public UnrecoverableItemReportKind Kind { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.OperationRange"/> loss, the sequence number of the last intact operation the log was recovered through, or -1 when no operation survived.</summary>
    public long RecoveredThroughSequence { get; }

    /// <summary>For an <see cref="UnrecoverableItemReportKind.OperationRange"/> loss, the number of trailing bytes discarded past the intact boundary.</summary>
    public long DiscardedByteCount { get; }

    /// <summary>Creates an operation-range loss report: the log was recovered through <paramref name="recoveredThroughSequence"/> and <paramref name="discardedByteCount"/> trailing bytes past the intact boundary were discarded.</summary>
    /// <param name="recoveredThroughSequence">The sequence number of the last intact operation, or -1 when none survived.</param>
    /// <param name="discardedByteCount">The number of trailing bytes discarded; must be positive (an operation-range loss discarded something).</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recoveredThroughSequence"/> is below -1, or <paramref name="discardedByteCount"/> is not positive.</exception>
    public static UnrecoverableItemReport OperationRange(long recoveredThroughSequence, long discardedByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recoveredThroughSequence, -1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(discardedByteCount);

        return new UnrecoverableItemReport(UnrecoverableItemReportKind.OperationRange, recoveredThroughSequence, discardedByteCount);
    }
}
