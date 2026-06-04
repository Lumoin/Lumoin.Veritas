namespace Lumoin.Veritas.Core.Hypertrie.Tracing;

/// <summary>
/// Why a query rendezvous selected the engine it did, carried by
/// <see cref="QueryTraceEventKind.EngineSelected"/> events.
/// Consumers joining these with
/// <see cref="QueryTraceEventKind.QueryCompleted"/> by correlation
/// id can attribute observed cost to the decision inputs — the
/// feedback an adaptive selection policy learns from.
/// </summary>
public enum EngineSelectionReason
{
    /// <summary>The policy routed the query to the system of record — the query did not qualify for a derived view.</summary>
    SystemOfRecord = 0,

    /// <summary>An existing derived view answered the query; no build cost was paid.</summary>
    ViewReused = 1,

    /// <summary>A derived view was materialised for this query; the event's value field carries the build cost in milliseconds.</summary>
    ViewBuilt = 2,
}
