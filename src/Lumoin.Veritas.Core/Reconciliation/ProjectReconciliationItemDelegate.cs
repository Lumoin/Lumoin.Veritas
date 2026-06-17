using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// Projects one triple to the fixed-width content-key item the anti-entropy reconciliation tier
/// exchanges. The projection must be a PURE function of the triple's canonical identity — no clock,
/// no node identity, no mutable state — so two replicas that hold the same triple always produce the
/// same item. That determinism is what lets the rateless symmetric-difference recovery cancel matched
/// items and what any cross-replica content comparison (or a whole-store commitment over the items)
/// depends on.
/// </summary>
/// <param name="triple">The triple to project.</param>
/// <returns>The triple's reconciliation item.</returns>
public delegate ContentKey128 ProjectReconciliationItemDelegate(EncodedTriple triple);
