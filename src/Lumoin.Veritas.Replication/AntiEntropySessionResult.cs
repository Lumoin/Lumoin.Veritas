using System;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The outcome of one <see cref="AntiEntropySession"/> reconciliation: what the rateless peel recovered,
/// whether it was complete, and the resulting local index. When the peel is complete the difference was applied
/// as repair-as-ingest and <see cref="ConvergedIndex"/> is the local replica grown to the union of both; when it
/// is not — the peer was unavailable, the budget could not peel the whole difference, or the recovered set
/// overflowed the sink — nothing was applied and <see cref="ConvergedIndex"/> is the unchanged local index, so a
/// declined session never half-applies a difference.
/// </summary>
/// <param name="RecoveredCount">The number of symmetric-difference items the decoder peeled; on an incomplete peel this is the partial or needed count and nothing was applied.</param>
/// <param name="ConvergedIndex">The local index after the session: the union of both replicas on a complete peel, otherwise the unchanged input index.</param>
/// <param name="IsComplete">Whether the whole symmetric difference was peeled and applied; <see langword="false"/> means the session declined and applied nothing.</param>
/// <param name="AbsorbedSymbols">The number of combined coded symbols the decoder absorbed before converging or hitting the budget; zero when no peer was reached.</param>
/// <param name="Elapsed">The wall-clock time the session took, measured against the injected time provider.</param>
public readonly record struct AntiEntropySessionResult(
    int RecoveredCount,
    ColumnarTripleIndex ConvergedIndex,
    bool IsComplete,
    int AbsorbedSymbols,
    TimeSpan Elapsed);
