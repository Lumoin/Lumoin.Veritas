using System.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Configuration for a <see cref="QueryEngineRendezvous"/>: which
/// queries qualify for the derived columnar view, and whether the
/// view may be materialised on demand. This is load-time
/// configuration in the sense that it governs index materialisation
/// — the per-query engine choice it parameterises remains fully
/// dynamic.
/// </summary>
/// <param name="MinimumPatternsForColumnar">The pattern count at or above which a query routes to the columnar view. Join-bearing queries amortise the view's cost; single-pattern lookups stay on the system of record.</param>
/// <param name="BuildViewOnDemand">Whether the first qualifying query may materialise the view, paying the build once. When <c>false</c>, qualifying queries fall back to the system of record until a view exists.</param>
/// <param name="DegreeOfParallelism">The number of HyperCube cells a columnar-routed query runs across; one evaluates sequentially. Platforms without threads (browser WASM without shared memory) stay at one.</param>
[DebuggerDisplay("QueryEnginePolicy MinPatterns={MinimumPatternsForColumnar} OnDemand={BuildViewOnDemand} Dop={DegreeOfParallelism}")]
public readonly record struct QueryEnginePolicy(
    int MinimumPatternsForColumnar,
    bool BuildViewOnDemand,
    int DegreeOfParallelism)
{
    /// <summary>
    /// The default policy: queries with two or more patterns route
    /// to the columnar view, which is materialised on first demand
    /// and evaluated sequentially; parallelism is opt-in
    /// configuration.
    /// </summary>
    public static QueryEnginePolicy Default { get; } = new(MinimumPatternsForColumnar: 2, BuildViewOnDemand: true, DegreeOfParallelism: 1);
}
