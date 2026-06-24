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
public readonly record struct PeerReconcileOutcome(bool Converged, int Rounds, WriteBackOutcome WriteBack);
