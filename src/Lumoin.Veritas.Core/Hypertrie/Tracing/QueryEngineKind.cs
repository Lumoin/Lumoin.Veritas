namespace Lumoin.Veritas.Core.Hypertrie.Tracing;

/// <summary>
/// The execution engine a query rendezvous selected, carried by
/// <see cref="QueryTraceEventKind.EngineSelected"/> events.
/// </summary>
public enum QueryEngineKind
{
    /// <summary>The hypertrie worst-case-optimal join engine — the journaled system of record.</summary>
    Hypertrie = 0,

    /// <summary>The columnar worst-case-optimal join engine — a derived read-optimised view.</summary>
    Columnar = 1,

    /// <summary>The columnar batched scan-and-hash pipeline — the acyclic-shape half of the join hybrid, over the same derived view.</summary>
    ColumnarBatched = 2,

    /// <summary>The Free Join generic join — the unifying executor over generalized hash tries, over the same derived view. The default join-route selector chooses it for cyclic-core and disconnected shapes on a six-order view, and an explicit policy force fixes it for every qualifying shape.</summary>
    FreeJoin = 3,

    /// <summary>The worst-case-optimal join over the succinct triple self-index — opt-in, serving the rotation-incompatible shapes a reduced order set cannot plan.</summary>
    SelfIndex = 4,
}
