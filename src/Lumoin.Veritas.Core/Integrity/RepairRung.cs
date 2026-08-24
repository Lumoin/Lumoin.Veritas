namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The ordered, exhaustive set of sources a storage repair descends when a block is detected corrupt, from
/// cheapest and most local to last resort. The set is closed: a repair either restores from one of the three
/// restoring rungs — <see cref="RederiveLocally"/>, <see cref="LocalParity"/>, <see cref="PeerReconciliation"/>
/// — or terminates at <see cref="NamedLoss"/>; there is no other source. A consumer-extensible value type is
/// deliberately NOT used here: the escalation is a fixed protocol the repair logic switches over exhaustively.
/// </summary>
public enum RepairRung
{
    /// <summary>Re-derive the block from a surviving local authority: a corrupt DERIVED artifact (the columnar sidecar or the integrity sketch) is rebuilt from the verified system-of-record. The cheapest rung; it applies whenever the corruption is in a re-derivable structure and the system-of-record is intact.</summary>
    RederiveLocally,

    /// <summary>Reconstruct a lost system-of-record block from a locally-stored parity / erasure prefix, up to its capacity (the optional parity tier).</summary>
    LocalParity,

    /// <summary>Recover the lost items from a peer by reconciliation, then re-ingest them (the replication tier).</summary>
    PeerReconciliation,

    /// <summary>No source could restore the block: the loss is NAMED (an <see cref="UnrecoverableItemReport"/>) rather than hidden — the terminal, always-reachable rung that makes the ladder exhaustive.</summary>
    NamedLoss,
}
