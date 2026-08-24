namespace Lumoin.Veritas.Replication;

/// <summary>
/// Supplies one pinned serve binding per accepted dotted-difference serve: the host snapshots its dotted commit
/// ledger and hands back the projection AND the apply seams bound to that same snapshot instant, so a
/// long-lived endpoint always serves its latest committed causal state, each serve pins exactly one set
/// version, and classification and application cannot read two different snapshots.
/// </summary>
/// <returns>The pinned serve binding for one serve.</returns>
public delegate DottedDifferenceServeBinding ProvideDottedServeBindingDelegate();
