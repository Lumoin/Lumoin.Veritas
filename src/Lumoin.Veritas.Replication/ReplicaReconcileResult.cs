using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of a bounded reconcile loop: the local index after the loop, the last round's outcome, how many
/// rounds ran, and whether the replica converged within the bound. When <see cref="Converged"/>, <see cref="Index"/>
/// is the converged union; otherwise it is the last round's index (unchanged across declining rounds).
/// </summary>
/// <param name="Index">The local index after the loop — the converged union when <paramref name="Converged"/>, else the last round's index.</param>
/// <param name="LastOutcome">The outcome of the final round run.</param>
/// <param name="Rounds">The number of rounds run.</param>
/// <param name="Converged">Whether the replica converged (or was already consistent) within the round bound.</param>
public readonly record struct ReplicaReconcileResult(ColumnarTripleIndex Index, AntiEntropyOutcome LastOutcome, int Rounds, bool Converged);
