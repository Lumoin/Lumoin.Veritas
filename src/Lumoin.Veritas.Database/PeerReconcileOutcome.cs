using Lumoin.Veritas.Replication;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The outcome of reconciling a mutable database from one peer: whether the replica converged within the round
/// bound, how many rounds ran, and how the converged delta was written back. When <see cref="Converged"/> is
/// <see langword="false"/> nothing was recovered (the peer was unavailable, its sketch was refused, or the budget
/// could not peel the difference); when <see langword="true"/>, <see cref="WriteBack"/> says whether the recovered
/// delta committed (<see cref="WriteBackOutcome.Committed"/>), was empty or already consistent
/// (<see cref="WriteBackOutcome.NoOp"/>), or lost the journal-head race past the retry budget
/// (<see cref="WriteBackOutcome.ConflictExhausted"/>).
/// </summary>
/// <param name="Converged">Whether the reconcile loop converged (or found the replicas already consistent).</param>
/// <param name="Rounds">The number of reconcile rounds run.</param>
/// <param name="WriteBack">How the converged delta was written back into the dataset.</param>
/// <param name="PeerEpochMismatch">Whether the reconcile was refused before it began because the peer's dictionary epoch did not match this database's: the structural sketch transfers term identifiers, so a different epoch denotes different terms and applying its recovered identifiers would silently corrupt. When <see langword="true"/> nothing was reconciled or written (<see cref="Converged"/> is <see langword="false"/>, <see cref="Rounds"/> is zero), and the caller should reconcile such a peer through the content-hash domain instead.</param>
public readonly record struct PeerReconcileOutcome(bool Converged, int Rounds, WriteBackOutcome WriteBack, bool PeerEpochMismatch);
