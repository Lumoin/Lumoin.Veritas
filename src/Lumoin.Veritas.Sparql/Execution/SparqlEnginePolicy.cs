using Lumoin.Veritas.Sparql.Algebra.Rewriting;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// Execution-strategy configuration for a <see cref="SparqlQueryEngine"/> — the SPARQL-algebra layer's
/// counterpart of the Core <c>QueryEnginePolicy</c>, which keeps governing the rendezvous below. With the
/// shipped rules the policy selects between evaluation routes only and never changes an answer; the one
/// deliberate exception is a caller-composed SEMANTIC rewrite rule (a rule implementing a declared
/// entailment extension), which changes answers to exactly what that extension specifies and enters a
/// pipeline only by the caller's explicit composition.
/// </summary>
/// <param name="PreferStreamingOperators">Whether eligible plans evaluate through the pull-based streaming
/// operator pipeline (first-row <c>ASK</c> short-circuit over every streamable shape) instead of the default
/// materialising executor. Off preserves today's evaluation paths; a shape the pipeline cannot stream falls
/// back to materialisation in either mode, so the flag only ever moves work between routes.</param>
/// <param name="Rewrites">The algebraic rewrite pipeline every evaluation entry of an engine built under
/// this policy applies between translation and evaluation; <see langword="null"/> resolves to
/// <see cref="AlgebraRewritePipeline.Default"/> (empty — rewriting off). The shipped catalog rules are
/// answer-preserving plan rules; a composed semantic rule implements a declared entailment extension and
/// changes answers exactly as that extension specifies. A per-call pipeline argument on the evaluation
/// entries overrides this engine-wide value for that call.</param>
/// <param name="DisableInterceptions">Whether the evaluation interception registry (the shipped fast paths:
/// bare <c>COUNT(*)</c>, <c>DISTINCT</c> star keys, the <c>LIMIT</c> leaf cap, the streaming window, and the
/// <c>ASK</c> first-solution short-circuit) is switched OFF for engines built under this policy — the
/// differential-isolation arm certifying the fast paths never change an answer. Default off: the entries run.</param>
/// <param name="PreferValueIndexes">Whether a <c>FILTER</c> whose comparison matches a registered value
/// index's declared axis is answered by an index probe instead of the scan. Off preserves today's evaluation
/// paths; a shape the recognizer does not match, a mismatched probe family, or an unbuilt index falls through
/// to the scan in either mode, so the flag only ever selects between evaluation routes — it never changes an
/// answer.</param>
public readonly record struct SparqlEnginePolicy(bool PreferStreamingOperators = false, AlgebraRewritePipeline? Rewrites = null, bool DisableInterceptions = false, bool PreferValueIndexes = false)
{
    /// <summary>The default policy: the materialising executor everywhere (streaming operators off), no rewrite rules, interceptions on, value-index probes off — the record struct's default value, named for call-site clarity.</summary>
    public static SparqlEnginePolicy Default { get; }
}
