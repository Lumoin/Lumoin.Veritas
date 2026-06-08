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
/// <param name="PreferSemijoinReduction">Whether a batched acyclic pipeline of three or more patterns reduces dangling tuples by Yannakakis' two semijoin passes before joining (<see cref="ColumnarBatchPipeline"/>). On by default; it bounds the intermediates by the input and output sizes on shapes where a middle join would otherwise blow up, and is answer-identical to the unreduced stream. Two-pattern joins, whose only intermediate is the output, are never reduced.</param>
/// <param name="PreferFactorizedStar">Whether a batched star — three or more patterns all joining on one shared key — runs through the factorising join (<see cref="ColumnarBatchPipeline"/>), keeping the intermediates product-of-unions until the final flatten rather than materialising each cross product. On by default; it is answer-identical to the streamed join (the flatten reproduces the flat product) and takes precedence over semijoin reduction on a star, where it bounds the intermediates more tightly. Non-star shapes are unaffected.</param>
[DebuggerDisplay("QueryEnginePolicy MinPatterns={MinimumPatternsForColumnar} OnDemand={BuildViewOnDemand} Dop={DegreeOfParallelism} Orders={OrderSetMode} Batched={PreferBatchedForAcyclic} Semijoin={PreferSemijoinReduction} FactorizedStar={PreferFactorizedStar}")]
public readonly record struct QueryEnginePolicy(
    int MinimumPatternsForColumnar,
    bool BuildViewOnDemand,
    int DegreeOfParallelism,
    ColumnarOrderSetMode OrderSetMode,
    bool PreferBatchedForAcyclic = true,
    bool PreferSemijoinReduction = true,
    bool PreferFactorizedStar = true)
{
    /// <summary>
    /// The default policy: queries with two or more patterns route
    /// to the columnar view, materialised on first demand with all
    /// six permutations; acyclic shapes take the batched
    /// scan-and-hash pipeline (the measured winner) and, at three or
    /// more patterns, its Yannakakis semijoin reduction — or, for a
    /// star, the factorising join — while cyclic shapes leapfrog.
    /// Parallelism and the three-rotation memory profile stay opt-in.
    /// </summary>
    public static QueryEnginePolicy Default { get; } = new(
        MinimumPatternsForColumnar: 2,
        BuildViewOnDemand: true,
        DegreeOfParallelism: 1,
        OrderSetMode: ColumnarOrderSetMode.AllSixOrders);
}
