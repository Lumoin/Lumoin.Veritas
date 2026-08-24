using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// A structured trace event the shard-difference client emits when it converts a fault into a value decline:
/// the shard whose fetch faulted and the fault's class. It rides the Core diagnostics bus beside the storage
/// and replication events and shares their correlation id, so a consumer stitches a declined shard fetch into
/// the repair round that drove it — the diagnosis is never laundered into decode incompleteness. Scalar-only,
/// so emitting it is allocation-free under the <see langword="in"/> parameter.
/// </summary>
/// <param name="SequenceNumber">The monotonic stream sequence number the emitter assigns; one fetch emits at most one fault event, so it is 0 unless the caller sequences a stream.</param>
/// <param name="TimestampTicks">The emit timestamp in <see cref="DateTime.Ticks"/> units, from the caller-injected time provider.</param>
/// <param name="CorrelationId">The correlation id shared with the repair round that drove the fetch.</param>
/// <param name="ShardIndex">The shard whose fetch faulted.</param>
/// <param name="Kind">The fault's class.</param>
public readonly record struct ShardDifferenceFaultEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    int ShardIndex,
    ShardDifferenceFaultKind Kind): ITraceEvent;
