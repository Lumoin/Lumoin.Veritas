using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A structured trace event one <see cref="AntiEntropySession"/> reconciliation emits on the diagnostics
/// <see cref="TraceHandler{TEvent}"/> channel: the value-based outcome of the reconcile plus the budget and the
/// recovered/absorbed counts behind it. It rides the same Core diagnostics bus as the storage and query trace
/// events and shares their <see cref="ITraceEvent.CorrelationId"/>, so a consumer can stitch a peer-reconciliation
/// reconcile into the same per-operation timeline as the scrub that triggered it. Scalar-only, so emitting it is
/// allocation-free under the <c>in</c> parameter.
/// </summary>
/// <param name="SequenceNumber">The monotonic stream sequence number the emitter assigns; a reconcile emits one event, so it is 0 unless the caller sequences a stream of reconciles.</param>
/// <param name="TimestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units, from the caller-injected time provider.</param>
/// <param name="CorrelationId">The reconcile's correlation id, shared with the operation that drove it.</param>
/// <param name="Outcome">How the reconcile ended — the value-based decline or completion reason.</param>
/// <param name="LocalItemCount">The local replica's item count the budget was sized from.</param>
/// <param name="SymbolBudget">The symbol budget both sketches were built and the recovery was capped at.</param>
/// <param name="RecoveredCount">The number of symmetric-difference items the decoder peeled; on a decline this is the partial or needed count, and nothing was applied.</param>
/// <param name="AbsorbedSymbols">The number of combined coded symbols the decoder absorbed before converging or hitting the budget; zero when no peer was reached or its sketch was refused.</param>
public readonly record struct ReplicationTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    AntiEntropyOutcome Outcome,
    int LocalItemCount,
    int SymbolBudget,
    int RecoveredCount,
    int AbsorbedSymbols): ITraceEvent;
