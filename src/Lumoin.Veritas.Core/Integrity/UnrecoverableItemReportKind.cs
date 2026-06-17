namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The shape of an unrecoverable loss the persistence layer names rather than hides
/// (<see cref="PersistenceInvariant.LossIsNamed"/>). A fenced store reports exactly what it could not
/// recover; the kind says at what granularity.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="OperationRange"/> is produced today — by the durable journal when a torn or corrupt
/// tail truncates the log to its last intact operation. <see cref="ItemSet"/> and
/// <see cref="Contested"/> are named here so the contract is whole; they are produced as the repair
/// ladder (item-set loss beyond repair capacity) and the replication arc (a value two replicas cannot
/// agree on) land.
/// </para>
/// </remarks>
public enum UnrecoverableItemReportKind
{
    /// <summary>A contiguous range of operations was lost — the log was recovered through its last intact operation and everything after a torn or corrupt boundary was discarded.</summary>
    OperationRange,

    /// <summary>A specific set of items could not be reconstructed by any repair source up to its capacity. Produced by the repair ladder (a later tier).</summary>
    ItemSet,

    /// <summary>A value two replicas hold differently and cannot adjudicate. Produced by the replication arc (a later tier).</summary>
    Contested,
}
