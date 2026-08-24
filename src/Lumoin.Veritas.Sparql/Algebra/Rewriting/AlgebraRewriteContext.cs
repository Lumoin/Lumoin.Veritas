using Lumoin.Veritas.Core.Statistics;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// The read-only facts a rewrite rule may consult: the engine's execution policy, the (optional) graph
/// statistics a cost-aware rule reads, and the pipeline pass the rule is running in. Computed once per
/// <see cref="AlgebraRewritePipeline.Rewrite(AlgebraOperator, in AlgebraRewriteContext)"/> call and
/// re-stamped per pass; rules receive it by <c>in</c> reference and bind no other state.
/// </summary>
/// <param name="Policy">The engine's execution-strategy policy.</param>
/// <param name="Statistics">The graph statistics a cost-aware rule reads, or <see langword="null"/> when none are wired.</param>
/// <param name="Pass">The zero-based pipeline pass this rule invocation runs in; passes beyond the first run only fixpoint-participating rules.</param>
public readonly record struct AlgebraRewriteContext(SparqlEnginePolicy Policy, GraphStatistics? Statistics, int Pass);
