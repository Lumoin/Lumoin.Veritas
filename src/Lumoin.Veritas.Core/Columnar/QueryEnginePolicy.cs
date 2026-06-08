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
/// <param name="PreferFactorizedStar">Whether a batched star — three or more patterns all joining on one shared key — runs through the factorising join (<see cref="ColumnarBatchPipeline"/>), keeping the intermediates product-of-unions until the final flatten rather than materialising each cross product. Answer-identical to the streamed join (the flatten reproduces the flat product), and takes precedence over semijoin reduction on a star. <b>Off by default</b>: factorisation is a specialised optimisation — its compression is <c>fan²/3</c>, so it wins on fan-out stars but is a regression at low fan-out (the common property-table shape, fan≈1, where it stores more and runs slower). It stays opt-in until a cost-based selector chooses it per shape (a deferred research issue); the trace bus records the feedback that selector will learn from.</param>
/// <param name="PreferFactorizedChain">Whether a batched three-pattern chain — the third pattern joining on a branch variable of the first join — runs through the join then the nesting step (<see cref="ColumnarBatchPipeline"/>), keeping the chain factorised one level deeper across the branch-variable join. Answer-identical to the streamed join. <b>Off by default</b>, for the same reason as <paramref name="PreferFactorizedStar"/>: its compression is <c>(fanA·S)/(fanA+S)</c> with <c>S=fanB·fanC</c>, so it needs fan-out on both the independent arm and the sub-tree and is otherwise a regression. Opt-in until the cost-based selector chooses it per shape.</param>
[DebuggerDisplay("QueryEnginePolicy MinPatterns={MinimumPatternsForColumnar} OnDemand={BuildViewOnDemand} Dop={DegreeOfParallelism} Orders={OrderSetMode} Batched={PreferBatchedForAcyclic} Semijoin={PreferSemijoinReduction} FactorizedStar={PreferFactorizedStar} FactorizedChain={PreferFactorizedChain}")]
public readonly record struct QueryEnginePolicy(
    int MinimumPatternsForColumnar,
    bool BuildViewOnDemand,
    int DegreeOfParallelism,
    ColumnarOrderSetMode OrderSetMode,
    bool PreferBatchedForAcyclic = true,
    bool PreferSemijoinReduction = true,
    bool PreferFactorizedStar = false,
    bool PreferFactorizedChain = false)
{
    /// <summary>
    /// The default policy: queries with two or more patterns route
    /// to the columnar view, materialised on first demand with all
    /// six permutations; acyclic shapes take the batched
    /// scan-and-hash pipeline (the measured winner) and, at three or
    /// more patterns, its Yannakakis semijoin reduction, while cyclic
    /// shapes leapfrog. Parallelism, the three-rotation memory
    /// profile, and the factorising star join stay opt-in — the last
    /// because it is a specialised optimisation a future cost-based
    /// selector should engage per shape, not a default-on win.
    /// </summary>
    public static QueryEnginePolicy Default { get; } = new(
        MinimumPatternsForColumnar: 2,
        BuildViewOnDemand: true,
        DegreeOfParallelism: 1,
        OrderSetMode: ColumnarOrderSetMode.AllSixOrders);
}
