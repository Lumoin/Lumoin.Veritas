using System.Diagnostics;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Configuration for a <see cref="QueryEngineRendezvous"/>: which
/// queries qualify for the derived columnar view, whether the
/// view may be materialised on demand, and which permutation set
/// it materialises. This is load-time configuration in the sense
/// that it governs index materialisation — the per-query engine
/// choice it parameterises remains fully dynamic.
/// </summary>
/// <param name="MinimumPatternsForColumnar">The pattern count at or above which a query routes to the columnar view. Join-bearing queries amortise the view's cost; single-pattern lookups stay on the system of record.</param>
/// <param name="BuildViewOnDemand">Whether the first qualifying query may materialise the view, paying the build once. When <c>false</c>, qualifying queries fall back to the system of record until a view exists.</param>
/// <param name="DegreeOfParallelism">The number of HyperCube cells a columnar-routed query runs across; one evaluates sequentially. Platforms without threads (browser WASM without shared memory) stay at one.</param>
/// <param name="OrderSetMode">Which permutation set the view materialises. Three rotations roughly halve the view's memory but cannot answer rotation-incompatible (cyclic) join shapes, which route to the system of record instead — see <see cref="ColumnarRotationPlanner"/>.</param>
/// <param name="PreferBatchedForAcyclic">Whether ACYCLIC qualifying queries take the batched scan-and-hash pipeline (<see cref="ColumnarBatchPipeline"/>) instead of leapfrog. On by default — the soak gate measured 4–5× on the acyclic chain at 100k–1M with answers identical to leapfrog; access-controlled queries always stay on the per-candidate-consulting drivers, and cyclic shapes fall through to leapfrog.</param>
[DebuggerDisplay("QueryEnginePolicy MinPatterns={MinimumPatternsForColumnar} OnDemand={BuildViewOnDemand} Dop={DegreeOfParallelism} Orders={OrderSetMode} Batched={PreferBatchedForAcyclic}")]
public readonly record struct QueryEnginePolicy(
    int MinimumPatternsForColumnar,
    bool BuildViewOnDemand,
    int DegreeOfParallelism,
    ColumnarOrderSetMode OrderSetMode,
    bool PreferBatchedForAcyclic = true)
{
    /// <summary>
    /// The default policy: queries with two or more patterns route
    /// to the columnar view, materialised on first demand with all
    /// six permutations; acyclic shapes take the batched
    /// scan-and-hash pipeline (the measured winner), cyclic shapes
    /// leapfrog. Parallelism and the three-rotation memory profile
    /// stay opt-in.
    /// </summary>
    public static QueryEnginePolicy Default { get; } = new(
        MinimumPatternsForColumnar: 2,
        BuildViewOnDemand: true,
        DegreeOfParallelism: 1,
        OrderSetMode: ColumnarOrderSetMode.AllSixOrders);
}
