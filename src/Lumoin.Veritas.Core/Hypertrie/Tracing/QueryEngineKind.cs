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

    /// <summary>The Free Join generic join — the unifying executor over generalized hash tries, opt-in, over the same derived view.</summary>
    FreeJoin = 3,
}
