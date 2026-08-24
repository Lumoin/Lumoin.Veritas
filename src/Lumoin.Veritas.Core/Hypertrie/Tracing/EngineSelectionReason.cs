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

    /// <summary>The query pinned a store the rendezvous has advanced past — a snapshot taken before a later commit — so it ran on that pinned store directly, preserving snapshot isolation.</summary>
    SnapshotSuperseded = 3,

    /// <summary>The query's join shape is rotation-incompatible with the policy's reduced columnar order set (a cyclic shape under three rotations), so it ran on the system of record, which carries every order.</summary>
    RotationIncompatible = 4,

    /// <summary>The query qualified for a derived view that is being materialised off the serve path on the compute lane; this query ran on the system of record while the build is in flight, and a later query reuses the view once it lands.</summary>
    ViewBuilding = 5,
}
