using System.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Execution;

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
/// <param name="PreferBatchedForAcyclic">Whether ACYCLIC qualifying queries take the batched scan-and-hash pipeline (<see cref="ColumnarBatchPipeline"/>) instead of leapfrog. On by default — the soak gate measured 4–5× on the acyclic chain at 100k–1M with answers identical to leapfrog; access-controlled queries always stay on the per-candidate-consulting drivers. Cyclic and disconnected shapes are not this flag's business: on a six-order view <paramref name="JoinRouteSelector"/>'s shipped rule routes them to the Free Join generic join, and they reach leapfrog only when that route declines the shape.</param>
/// <param name="PreferSemijoinReduction">Whether a batched acyclic pipeline of three or more patterns reduces dangling tuples by Yannakakis' two semijoin passes before joining (<see cref="ColumnarBatchPipeline"/>). On by default; it bounds the intermediates by the input and output sizes on shapes where a middle join would otherwise blow up, and is answer-identical to the unreduced stream. Two-pattern joins, whose only intermediate is the output, are never reduced.</param>
/// <param name="PreferFactorizedStar">Whether a batched star — three or more patterns all joining on one shared key — runs through the factorising join (<see cref="ColumnarBatchPipeline"/>), keeping the intermediates product-of-unions until the final flatten rather than materialising each cross product. Answer-identical to the streamed join (the flatten reproduces the flat product), and takes precedence over semijoin reduction on a star. <b>Off by default</b>: factorisation is a specialised optimisation — its compression is <c>fan²/3</c>, so it wins on fan-out stars but is a regression at low fan-out (the common property-table shape, fan≈1, where it stores more and runs slower). It stays opt-in as a FORCE: set, it engages the star whatever the statistics say, and unset it leaves the choice to <paramref name="JoinRouteSelector"/>, whose calibrated rule engages the star per shape where the measured compression pays.</param>
/// <param name="PreferFactorizedChain">Whether a batched three-pattern chain — the third pattern joining on a branch variable of the first join — runs through the join then the nesting step (<see cref="ColumnarBatchPipeline"/>), keeping the chain factorised one level deeper across the branch-variable join. Answer-identical to the streamed join. <b>Off by default</b>, for the same reason as <paramref name="PreferFactorizedStar"/>: its compression is <c>(fanA·S)/(fanA+S)</c> with <c>S=fanB·fanC</c>, so it needs fan-out on both the independent arm and the sub-tree and is otherwise a regression. Opt-in as a FORCE, for the same reason: unset leaves the choice to <paramref name="JoinRouteSelector"/>'s calibrated rule.</param>
/// <param name="PreferFreeJoin">Whether a qualifying query runs through the Free Join generic join (<see cref="FreeJoinPipeline"/>) instead of the batched or leapfrog engines, over generalized hash tries built at the depths that route plans — each relation's join-cover depth, extended through its private tail where its own key fan-out justifies hashing it — so a cyclic core runs the worst-case-optimal descent, a low-fan star's satellites run the binary-hash-join shape, and mixed shapes interpolate. <b>Off by default</b>, and off means "not forced", not "not taken": this flag is the explicit force, and it outranks <paramref name="JoinRouteSelector"/> — a set force fixes the route without consulting any selector, while an unset one leaves the per-query choice to the selector, whose shipped rule already takes this route for cyclic-core and disconnected shapes on a six-order view. Answer-identical to the other engines, so the conformance corpus is the oracle; like the batched path it has no per-candidate access-control consultation, so an access-controlled query stays on the leapfrog driver.</param>
/// <param name="FreeJoinTrieBuild">How the Free Join route's generalized hash tries materialise their internal maps (<see cref="Hypertrie.Execution.FreeJoinTrieBuild"/>): eager hashes the whole trie per query at build time, lazy — the column-oriented lazy trie — stores the relation's columns and materialises each map on its first navigation touch, leaving never-descended subtries unbuilt at the price of retaining the column store. Answer-identical either way; <b>eager by default</b> until the benchmark stand's build-versus-drive and retained-footprint measurements rule a winner.</param>
/// <param name="PreferSelfIndex">Whether a qualifying query whose join shape is rotation-incompatible with a reduced order set (a cyclic shape under three rotations) answers from the succinct triple self-index (<see cref="SelfIndexPipeline"/>) instead of falling back to the system of record. The self-index serves every rotation from one structure, so any variable order is evaluable; it is materialised on first such demand (under <paramref name="BuildViewOnDemand"/>) and dropped on every commit, since it rebuilds rather than evolving by delta. <b>Off by default</b>: opt-in for measurement, like the other alternate engines; it has no per-candidate access-control consultation, so an access-controlled query stays on the system of record.</param>
/// <param name="ColumnPayloadBacking">Where the columnar view's block-packed column payloads live — managed GC arrays (default) or 64-byte-aligned off-GC native memory; see <see cref="ColumnPayloadBacking"/>. The default-graph view honours it; the named-graph set is wired separately.</param>
/// <param name="HypertrieResidency">Whether the hypertrie system of record is always resident (<see cref="Columnar.HypertrieResidency.Eager"/>, the default — today's behaviour) or deferred so a present columnar view answers the columnar-capable shapes the trie would otherwise serve (<see cref="Columnar.HypertrieResidency.Deferred"/>, the warm read-serving start). Access-controlled queries always stay on the trie under both, so security is unchanged; the deferred mode trades a possible cold-start trie build for not holding the trie at all on warm read generations.</param>
/// <param name="JoinRouteSelector">The per-query join-route selector: which view-borne route — the Free Join generic join, the batched scan-and-hash pipeline, or the columnar leapfrog driver — serves a qualifying query. <see langword="null"/> uses <see cref="JoinStrategySelectors.Structural"/>, the shipped rule. <paramref name="PreferFreeJoin"/> outranks it: an explicit force is taken without consulting a selector at all. Access-controlled queries are never put to it, so no selector can widen the access boundary.</param>
[DebuggerDisplay("QueryEnginePolicy MinPatterns={MinimumPatternsForColumnar} OnDemand={BuildViewOnDemand} Dop={DegreeOfParallelism} Orders={OrderSetMode} Batched={PreferBatchedForAcyclic} Semijoin={PreferSemijoinReduction} FactorizedStar={PreferFactorizedStar} FactorizedChain={PreferFactorizedChain} FreeJoin={PreferFreeJoin} FreeJoinBuild={FreeJoinTrieBuild} SelfIndex={PreferSelfIndex} Backing={ColumnPayloadBacking} Residency={HypertrieResidency} Selector={JoinRouteSelector != null}")]
public readonly record struct QueryEnginePolicy(
    int MinimumPatternsForColumnar,
    bool BuildViewOnDemand,
    int DegreeOfParallelism,
    ColumnarOrderSetMode OrderSetMode,
    bool PreferBatchedForAcyclic = true,
    bool PreferSemijoinReduction = true,
    bool PreferFactorizedStar = false,
    bool PreferFactorizedChain = false,
    bool PreferFreeJoin = false,
    FreeJoinTrieBuild FreeJoinTrieBuild = FreeJoinTrieBuild.Eager,
    bool PreferSelfIndex = false,
    ColumnPayloadBacking ColumnPayloadBacking = ColumnPayloadBacking.Managed,
    HypertrieResidency HypertrieResidency = HypertrieResidency.Eager,
    JoinStrategySelectorDelegate? JoinRouteSelector = null)
{
    /// <summary>
    /// The default policy: queries with two or more patterns route
    /// to the columnar view, materialised on first demand with all
    /// six permutations; acyclic connected shapes take the batched
    /// scan-and-hash pipeline (the measured winner) and, at three or
    /// more patterns, its Yannakakis semijoin reduction. Carrying no
    /// selector, it uses
    /// <see cref="JoinStrategySelectors.Structural"/>, so a cyclic
    /// core or a disconnected (cartesian) shape takes the Free Join
    /// generic join — the two shapes the batched pipeline declines
    /// and the leapfrog driver serves worst. Parallelism, the
    /// three-rotation memory profile, and the factorising star join
    /// stay opt-in — the last because it is a specialised
    /// optimisation a cost-based selector should engage per shape,
    /// not a default-on win.
    /// </summary>
    public static QueryEnginePolicy Default { get; } = new(
        MinimumPatternsForColumnar: 2,
        BuildViewOnDemand: true,
        DegreeOfParallelism: 1,
        OrderSetMode: ColumnarOrderSetMode.AllSixOrders);
}
