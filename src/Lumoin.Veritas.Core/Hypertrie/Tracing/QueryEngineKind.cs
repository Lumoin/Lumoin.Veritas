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
}
