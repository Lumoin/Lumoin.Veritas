using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The peer-reconciliation restoring source a repair pass borrows: a peer replica's verified integrity sketch
/// and the host-bound seams to recover the symmetric difference between it and the local survivors and invert a
/// recovered item back to its triple. It is the replication-tier analog of <see cref="ParityRepairSource"/> — a
/// data holder the coordinator reads from, not the restore itself; the coordinator builds the local survivor
/// sketch, recovers the difference through <see cref="Recover"/>, and re-ingests the healed system-of-record.
/// The peer's sketch is a <see cref="VerifiedSketch"/>, so its load-time verification (the
/// <c>DetectionPrecedesXor</c> invariant) happened in the host before it crossed this boundary — no unverified
/// peer bytes ever reach the decoder. The transport that fetched the peer's sketch is the host's concern; only
/// the verified sketch and the two pure seams cross into the core, so the core takes no replication or network
/// dependency.
/// </summary>
/// <remarks>
/// This source restores a single lost block from a faithful peer replica of the same dictionary epoch: the
/// coordinator's rung gates on exactly one lost block and on the recovered difference matching that block's item
/// count, so a multi-block loss or a peer that has diverged beyond the lost block declines to a named loss rather
/// than half-healing. Broadening to a multi-block or arbitrarily-diverged peer is a later rung; the cap lives on
/// the rung, not on this holder, so lifting it needs no change here.
/// </remarks>
/// <param name="PeerSketch">The peer replica's verified sketch, over the peer's full item set.</param>
/// <param name="Recover">The host-bound seam that combines the local and peer sketches and recovers their symmetric difference, reporting whether the peel was complete.</param>
/// <param name="Invert">The inverse of the reconciliation projection — recovers a triple from a recovered item — for the same invertible (structural) domain the local sketch is projected under.</param>
/// <param name="SymbolCap">The symbol budget both the local survivor sketch is built at and the recovery is capped to; the peer sketch carries at least this many symbols.</param>
public readonly record struct PeerReconciliationSource(
    VerifiedSketch PeerSketch,
    SketchReconciliationDelegates.RecoverSketchDifference Recover,
    ReconciliationItemInverseDelegate Invert,
    int SymbolCap);
