namespace Lumoin.Veritas.Database;

/// <summary>
/// Where a mutable database stands on the remove-aware replication ladder. The state also answers whether a host
/// replica identity was supplied at open — the three values are mutually exclusive and complete, so no separate
/// identity flag exists for the state to disagree with.
/// </summary>
public enum ReplicationCausalityState
{
    /// <summary>No host replica identity was supplied at open: the database is add-only, byte-identical to the behaviour before the remove-aware lane existed.</summary>
    AddOnly,

    /// <summary>A host replica identity was supplied but no loadable, consistent causality pair was recovered: the database stays add-only until the explicit baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) — never an ambient upgrade.</summary>
    AwaitingBaseline,

    /// <summary>The database keeps a dotted commit ledger: every commit's journal entry carries its causality annotation, a persist writes the at-rest causality artifact, and an observed retraction is protected from resurrection by remove-aware reconciliation.</summary>
    RemoveAware
}
