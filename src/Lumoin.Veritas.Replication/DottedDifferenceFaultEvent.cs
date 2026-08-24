using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A structured trace event a dotted-difference endpoint emits when it converts a fault into a value outcome:
/// the fault's class, on the Core diagnostics bus beside the storage and replication events, sharing their
/// correlation id — so a consumer stitches a refused or interrupted dotted exchange into the reconcile that
/// drove it, and the diagnosis is never laundered into decode incompleteness. Scalar-only, so emitting it is
/// allocation-free under the <see langword="in"/> parameter.
/// </summary>
/// <param name="SequenceNumber">The monotonic stream sequence number the emitter assigns; one exchange emits at most one fault event, so it is 0 unless the caller sequences a stream.</param>
/// <param name="TimestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units, from the caller-injected time provider.</param>
/// <param name="CorrelationId">The correlation id shared with the reconcile that drove the exchange.</param>
/// <param name="Kind">The fault's class.</param>
public readonly record struct DottedDifferenceFaultEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    DottedDifferenceFaultKind Kind): ITraceEvent;
