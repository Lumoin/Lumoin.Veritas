namespace Lumoin.Veritas.Database;

/// <summary>
/// The value-based outcome of a mutable open's explicit causality baseline request
/// (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>), surfaced on
/// <see cref="VeritasEngine.ReplicationBaseline"/> the way recovery fidelity is surfaced on the provenance
/// surfaces. A refusal is an expected operational condition and is reported here as a value — the open still
/// serves the store in its add-only standing; it never throws for it.
/// </summary>
public enum ReplicationBaselineOutcome
{
    /// <summary>The explicit baseline step was not requested at open.</summary>
    NotRequested,

    /// <summary>The step was requested and the store is remove-aware without it running: a consistent causality pair was recovered, or the store was created at this open and its creation baseline covers the request.</summary>
    AlreadyRemoveAware,

    /// <summary>The step ran at this open: the causality-only baseline entry committed, dotting every present committed triple on the host identity's axis, and the store is remove-aware from here.</summary>
    Baselined,

    /// <summary>The step was requested but refused: recovery saw a causality trace (a refused or torn causality artifact, or annotated journal entries — a broken causal lineage included) without a recoverable pair, and a fresh baseline's counters could re-issue dots surviving history already names for other events. The store serves in its awaiting-baseline standing; the remedy is operator-level — re-clone from a healthy remove-aware replica.</summary>
    RefusedCausalityTrace
}
