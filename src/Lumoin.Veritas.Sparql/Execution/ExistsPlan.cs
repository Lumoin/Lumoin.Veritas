using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution.Streaming;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// One <c>EXISTS</c>/<c>NOT EXISTS</c> site's compile-once state, held by the evaluation-scoped
/// <see cref="ExistsRegistry"/> and shared by every outer row the site is evaluated under: the normalized
/// synthetic <c>SELECT * { pattern }</c> (its strings owned by <see cref="Pool"/>), the once-translated
/// inner algebra, the emptiness-preserving core the on-mode pipeline compiles over, and (where sound) the
/// seeding plan for the indexed per-binding probe. Disposal is deterministic: the owning registry's
/// <see cref="ExistsRegistry.DisposeAsync"/> runs in the evaluation's <c>finally</c> on the evaluating
/// thread, disposing the pool and awaiting the on-mode pipeline's teardown.
/// </summary>
internal sealed class ExistsPlan : IAsyncDisposable
{
    /// <summary>Constructs the plan; ownership of <paramref name="pool"/> transfers to it.</summary>
    /// <param name="pool">The pool that owns the normalized AST's strings.</param>
    /// <param name="normalizedQuery">The normalized synthetic <c>SELECT * { pattern }</c> with no trailing <c>VALUES</c>.</param>
    /// <param name="innerAlgebra">The once-translated algebra of <paramref name="normalizedQuery"/>.</param>
    /// <param name="coreAlgebra">The emptiness-preserving core under the synthesized star projection (see <see cref="CoreAlgebra"/>).</param>
    /// <param name="seedPlan">The seeding plan when the core is a seedable bare BGP, else <see langword="null"/>.</param>
    public ExistsPlan(Utf8StringPool pool, SparqlQuery normalizedQuery, AlgebraOperator innerAlgebra, AlgebraOperator coreAlgebra, BgpSeedPlan? seedPlan)
    {
        Pool = pool;
        NormalizedQuery = normalizedQuery;
        InnerAlgebra = innerAlgebra;
        CoreAlgebra = coreAlgebra;
        SeedPlan = seedPlan;
    }

    /// <summary>The pool that owns the normalized AST's strings; disposed with the plan.</summary>
    public Utf8StringPool Pool { get; }

    /// <summary>
    /// The normalized synthetic <c>SELECT * { pattern }</c> with <c>Values = null</c>. The off-mode path
    /// rebuilds the per-solution query from it (<c>with { Values = … }</c>) and re-runs only the pure
    /// translation, so the evaluated algebra is byte-equivalent to a fresh per-solution
    /// synthesize/normalize/translate while the parse, normalization, and per-solution pool are paid once.
    /// </summary>
    public SparqlQuery NormalizedQuery { get; }

    /// <summary>The once-translated algebra of <see cref="NormalizedQuery"/> — the empty-pre-binding evaluation form.</summary>
    public AlgebraOperator InnerAlgebra { get; }

    /// <summary>
    /// The subtree under the synthesized top star-<see cref="Project"/> (the translator always wraps one).
    /// The on-mode pipeline compiles over THIS: the probe only needs emptiness and per-row compatibility,
    /// and the synthesized star projection projects every visible variable, so dropping it changes neither —
    /// the only variables a core row carries beyond the projected set are internal fresh variables whose
    /// minted names can never collide with a pre-binding's user variables.
    /// </summary>
    public AlgebraOperator CoreAlgebra { get; }

    /// <summary>The seeding plan for the indexed per-binding probe, or <see langword="null"/> when the core shape declines seeding at the plan level.</summary>
    public BgpSeedPlan? SeedPlan { get; }

    /// <summary>The site's reusable on-mode probe pipeline, compiled once on first use and re-armed per binding through <see cref="Streaming.SolutionCursor.ResetAsync"/>; <see langword="null"/> until compiled (or when the budget declined it).</summary>
    public StreamingPipeline? Pipeline { get; set; }

    /// <summary>Whether the on-mode pipeline compile was attempted (a budget decline is remembered so the site does not retry per binding).</summary>
    public bool PipelineCompileAttempted { get; set; }

    /// <summary>Disposes the on-mode pipeline (when one compiled) and the owned pool, exactly once, on the evaluating thread.</summary>
    /// <returns>A task completing when the plan's resources are released.</returns>
    public async ValueTask DisposeAsync()
    {
        if(Pipeline is StreamingPipeline pipeline)
        {
            Pipeline = null;
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }

        Pool.Dispose();
    }
}

/// <summary>
/// The evaluation-scoped <c>EXISTS</c> plan registry: one instance per
/// <c>SparqlQueryEngine.EvaluateInGraphAsync</c> invocation (sub-evaluations re-entering the driver create
/// their own; entries never alias across registries), keyed by the
/// <see cref="ExistsExpression"/>/<see cref="NotExistsExpression"/> node's REFERENCE identity paired with
/// the active graph — a variable-designated <c>GRAPH</c> form combines the same site under each named
/// graph, and a compiled probe pipeline binds its graph, so the graph is part of the key exactly as in the
/// driver's result map. Disposed in the owning invocation's <c>finally</c> — deterministic, on the
/// evaluating thread; no GC- or finalizer-timed release anywhere.
/// </summary>
internal sealed class ExistsRegistry : IAsyncDisposable
{
    private Dictionary<(ExpressionNode Site, TermId Graph), ExistsPlan>? plans;

    /// <summary>Constructs the registry with the evaluation-scoped rewrite frame its plan builds read.</summary>
    /// <param name="rewrites">The evaluation's resolved rewrite pipeline; a plan build applies it once to the site's translated inner algebra.</param>
    /// <param name="trace">The spawning evaluation's trace sink plan-build rewrite events emit into, or <see langword="null"/> when suppressed.</param>
    public ExistsRegistry(Algebra.Rewriting.AlgebraRewritePipeline rewrites, SparqlExecutionTrace? trace)
    {
        System.ArgumentNullException.ThrowIfNull(rewrites);

        Rewrites = rewrites;
        Trace = trace;
    }

    /// <summary>The evaluation's resolved rewrite pipeline — applied once per site at plan build, so the compiled inner algebra is the rewritten form.</summary>
    public Algebra.Rewriting.AlgebraRewritePipeline Rewrites { get; }

    /// <summary>The spawning evaluation's trace sink plan-build rewrite events emit into, or <see langword="null"/> when suppressed.</summary>
    public SparqlExecutionTrace? Trace { get; }

    /// <summary>Equality over registry keys by site reference identity and active-graph value.</summary>
    private sealed class SiteKeyComparer : IEqualityComparer<(ExpressionNode Site, TermId Graph)>
    {
        /// <summary>The shared comparer instance.</summary>
        public static SiteKeyComparer Instance { get; } = new();

        /// <summary>Returns whether two keys share a site instance and an active graph.</summary>
        /// <param name="x">The first key.</param>
        /// <param name="y">The second key.</param>
        /// <returns><see langword="true"/> when both refer to the same site instance under the same graph.</returns>
        public bool Equals((ExpressionNode Site, TermId Graph) x, (ExpressionNode Site, TermId Graph) y)
        {
            return ReferenceEquals(x.Site, y.Site) && x.Graph.Equals(y.Graph);
        }

        /// <summary>Computes a reference-identity hash of the site combined with the active-graph hash.</summary>
        /// <param name="key">The key to hash.</param>
        /// <returns>The hash code.</returns>
        public int GetHashCode((ExpressionNode Site, TermId Graph) key)
        {
            return System.HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Site), key.Graph);
        }
    }

    /// <summary>Looks up the site's plan under the active graph.</summary>
    /// <param name="site">The <c>EXISTS</c>/<c>NOT EXISTS</c> expression node (reference identity).</param>
    /// <param name="graph">The active graph the site is evaluated under.</param>
    /// <param name="plan">Receives the plan when present.</param>
    /// <returns><see langword="true"/> when the site already compiled under this graph.</returns>
    public bool TryGet(ExpressionNode site, TermId graph, out ExistsPlan plan)
    {
        if(plans is not null && plans.TryGetValue((site, graph), out ExistsPlan? existing))
        {
            plan = existing;

            return true;
        }

        plan = null!;

        return false;
    }

    /// <summary>Records the site's plan under the active graph; the registry takes ownership of its disposal.</summary>
    /// <param name="site">The <c>EXISTS</c>/<c>NOT EXISTS</c> expression node.</param>
    /// <param name="graph">The active graph the site is evaluated under.</param>
    /// <param name="plan">The compiled plan.</param>
    public void Add(ExpressionNode site, TermId graph, ExistsPlan plan)
    {
        plans ??= new Dictionary<(ExpressionNode Site, TermId Graph), ExistsPlan>(SiteKeyComparer.Instance);
        plans.Add((site, graph), plan);
    }

    /// <summary>Disposes every registered plan (pool + on-mode pipeline), exactly once, on the evaluating thread.</summary>
    /// <returns>A task completing when every plan is released.</returns>
    public async ValueTask DisposeAsync()
    {
        if(plans is null)
        {
            return;
        }

        Dictionary<(ExpressionNode Site, TermId Graph), ExistsPlan> owned = plans;
        plans = null;
        foreach(KeyValuePair<(ExpressionNode Site, TermId Graph), ExistsPlan> entry in owned)
        {
            await entry.Value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
