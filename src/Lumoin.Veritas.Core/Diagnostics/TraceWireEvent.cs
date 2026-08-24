using System;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// A trace event projected to its transport-neutral wire shape: the correlation and ordering of the
/// underlying <see cref="ITraceEvent"/> with the payload flattened to display strings. Hosts encode this
/// one shape onto their medium — a Server-Sent-Events stream, a JS-interop callback, a message channel —
/// so every consuming surface reads the same contract regardless of which host backs it.
/// </summary>
/// <param name="CorrelationId">The correlation id linking the events of one logical operation (<see cref="ITraceEvent.CorrelationId"/>).</param>
/// <param name="Sequence">The event's monotonic sequence number within its trace stream (<see cref="ITraceEvent.SequenceNumber"/>).</param>
/// <param name="Kind">The event's kind discriminator, a fixed lowercase token (for example <c>operator</c> or <c>rewrite-applied</c>).</param>
/// <param name="Term">The term the event centres on — an operator, rule, or interception name — or <see langword="null"/> when the kind carries none.</param>
/// <param name="Detail">The human-readable payload summary.</param>
public readonly record struct TraceWireEvent(Guid CorrelationId, long Sequence, string Kind, string? Term, string Detail);
