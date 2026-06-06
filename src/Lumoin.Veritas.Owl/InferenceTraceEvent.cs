using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// One derivation step on the inference trace stream: the rule that
/// fired, the premise triples it matched, and the conclusion it
/// produced. Together with the journal entry that commits the
/// derived triples, this is the provenance chain a tracking surface
/// renders — "this triple is here because rule R applied to premises
/// P" — and it shares the <see cref="ITraceEvent.CorrelationId"/>
/// stream with query and validation events so per-operation views
/// reassemble across subsystems.
/// </summary>
/// <param name="SequenceNumber">Monotonic sequence number within this materialization's trace stream.</param>
/// <param name="TimestampTicks">UTC timestamp in <see cref="DateTime.Ticks"/> units.</param>
/// <param name="CorrelationId">The materialization run's correlation id.</param>
/// <param name="Rule">The entailment rule that produced the conclusion — a name from <see cref="EntailmentRules"/>.</param>
/// <param name="Premises">The triples the rule matched. A rule answered through a precomputed closure carries its data premises; the collapsed schema steps are implied by the rule.</param>
/// <param name="Conclusion">The derived triple.</param>
[DebuggerDisplay("InferenceTraceEvent {Rule,nq} Seq={SequenceNumber}")]
public readonly record struct InferenceTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    string Rule,
    ImmutableArray<EncodedTriple> Premises,
    EncodedTriple Conclusion): ITraceEvent;
