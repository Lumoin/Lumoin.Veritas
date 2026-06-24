using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of one <see cref="AntiEntropySession"/> reconciliation: how it ended (<see cref="Outcome"/>), what
/// the rateless peel recovered, and the resulting local index. When the peel completes the difference was applied
/// as repair-as-ingest and <see cref="ConvergedIndex"/> is the local replica grown to the union of both; on any
/// decline — the peer was unavailable, its sketch was refused, or the budget could not peel the whole difference —
/// nothing was applied and <see cref="ConvergedIndex"/> is the unchanged local index, so a declined session never
/// half-applies a difference.
/// </summary>
/// <param name="RecoveredCount">The number of symmetric-difference items the decoder peeled; on a decline this is the partial or needed count and nothing was applied.</param>
/// <param name="ConvergedIndex">The local index after the session: the union of both replicas on a complete peel, otherwise the unchanged input index.</param>
/// <param name="Outcome">How the reconcile ended — the value-based completion or decline reason.</param>
/// <param name="AbsorbedSymbols">The number of combined coded symbols the decoder absorbed before converging or hitting the budget; zero when no peer was reached or its sketch was refused.</param>
/// <param name="Elapsed">The wall-clock time the session took, measured against the injected time provider.</param>
/// <param name="RecoveredAdditions">The triples this reconcile applied as additions to converge — the recovered symmetric difference for the structural domain (the local-held items are idempotent no-ops on apply), the verified peer-only triples for the content-hash domain. Empty on every decline and on <see cref="AntiEntropyOutcome.AlreadyConsistent"/> (nothing was applied). The bridge journals these to write a converged delta back through the dataset.</param>
public readonly record struct AntiEntropySessionResult(
    int RecoveredCount,
    ColumnarTripleIndex ConvergedIndex,
    AntiEntropyOutcome Outcome,
    int AbsorbedSymbols,
    TimeSpan Elapsed,
    ReadOnlyMemory<EncodedTriple> RecoveredAdditions)
{
    /// <summary>Whether the session completed — the whole symmetric difference was peeled (and applied when non-empty). <see langword="true"/> for <see cref="AntiEntropyOutcome.Converged"/> and <see cref="AntiEntropyOutcome.AlreadyConsistent"/>; <see langword="false"/> for every decline.</summary>
    public bool IsComplete => Outcome is AntiEntropyOutcome.Converged or AntiEntropyOutcome.AlreadyConsistent;
}
