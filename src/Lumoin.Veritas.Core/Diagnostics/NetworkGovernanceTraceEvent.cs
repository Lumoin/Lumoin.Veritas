using System;
using Lumoin.Veritas.Core.Network;

namespace Lumoin.Veritas.Core.Diagnostics;

/// <summary>
/// A structured trace event a transport decorator emits when a <see cref="NetworkGovernanceDelegate"/> governs a
/// network-boundary call: the boundary, the verdict, and the peer/endpoint it keyed on. It rides the same Core
/// diagnostics bus as the storage, query, and replication trace events and shares their
/// <see cref="ITraceEvent.CorrelationId"/>, so a consumer (the planned observability surface) joins a governance
/// decision into the same per-operation timeline as the query or reconcile that triggered it. Scalar-only, so
/// emitting it is allocation-free under the <c>in</c> parameter; the peer/endpoint travels as a stable hash rather
/// than its bytes.
/// </summary>
/// <param name="SequenceNumber">The monotonic stream sequence number the emitter assigns.</param>
/// <param name="TimestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units, from the caller-injected time provider.</param>
/// <param name="CorrelationId">The correlation id shared with the operation the governed call belongs to.</param>
/// <param name="Boundary">The boundary at which the decision was taken.</param>
/// <param name="Outcome">The verdict the governance seam returned.</param>
/// <param name="PeerKeyHash">A stable hash of the peer/endpoint key the decision keyed on, for joining without carrying the key bytes; 0 when the peer was unidentified. An identified peer can also hash to 0 (probability about 2^-64), so a consumer reads 0 as "unidentified, or the rare identified collision", not a strict identity — the hash is a diagnostics join key, never a decision input.</param>
/// <param name="RetryAfterTicks">The back-off in <see cref="DateTime.Ticks"/> units when the outcome is a delay; 0 otherwise.</param>
public readonly record struct NetworkGovernanceTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    NetworkBoundary Boundary,
    NetworkGovernanceKind Outcome,
    long PeerKeyHash,
    long RetryAfterTicks): ITraceEvent;
