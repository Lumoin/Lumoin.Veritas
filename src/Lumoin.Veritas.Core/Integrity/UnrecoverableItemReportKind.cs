namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The shape of an unrecoverable loss the persistence layer names rather than hides
/// (<see cref="PersistenceInvariant.LossIsNamed"/>). A fenced store reports exactly what it could not
/// recover; the kind says at what granularity.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OperationRange"/> is produced by the durable journal when a torn or corrupt tail truncates the
/// log to its last intact operation; <see cref="ItemSet"/> and <see cref="WholeArtifact"/> are produced by the
/// repair ladder when a corrupt system-of-record segment (the default graph or a named graph) or a whole
/// non-re-derivable artifact (the term dictionary) has no restoring source. <see cref="Contested"/> is named
/// here so the contract is whole; it is produced as the replication arc (a value two replicas cannot agree on)
/// lands.
/// </para>
/// </remarks>
public enum UnrecoverableItemReportKind
{
    /// <summary>A contiguous range of operations was lost — the log was recovered through its last intact operation and everything after a torn or corrupt boundary was discarded.</summary>
    OperationRange,

    /// <summary>A specific set of items in a named system-of-record segment could not be reconstructed by any repair source up to its capacity. Produced by the repair ladder (the default graph's segment or a named graph's segment).</summary>
    ItemSet,

    /// <summary>An entire non-re-derivable artifact was lost and no repair source can restore it — the term dictionary (the decode key), or a named-graph segment whose whole image cannot be trusted and which no parity or peer rung protects. Produced by the repair ladder.</summary>
    WholeArtifact,

    /// <summary>A value two replicas hold differently and cannot adjudicate. Produced by the replication arc (a later tier).</summary>
    Contested,
}
