namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// One rewrite rule, invoked per operator position by the pipeline's single bottom-up walk: the rule
/// pattern-matches <paramref name="node"/> (reading its subtree only through the cached
/// <see cref="AlgebraOperator.Children"/>) and returns a value-based verdict with the operator that flows
/// onward. Named rather than a bare functional so the binding is a discoverable type; implementors bind any
/// configuration in an explicit frame and pass a method group, never a capturing lambda. Every applied
/// replacement must stay inside the engine's executable operator set and preserve the semantics the
/// rule's kind declares — SPARQL multiset answer identity for a plan rule, the specified BGP semantics of
/// its declared entailment extension for a semantic rule — the seam's certification obligations.
/// </summary>
/// <param name="node">The operator position under consideration; its children are already rewritten by the current pass.</param>
/// <param name="context">The read-only facts the rule may consult.</param>
/// <returns>The rule's outcome at this position.</returns>
public delegate AlgebraRewriteOutcome AlgebraRewriteDelegate(AlgebraOperator node, in AlgebraRewriteContext context);
