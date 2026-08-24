using System;

namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// One rewrite rule's outcome at one operator position: the <see cref="Application"/> verdict paired with
/// the operator that flows onward — the replacement when <see cref="AlgebraRewriteApplication.Applied"/>,
/// the untouched input otherwise. Constructed only through the static factories, so an applied outcome
/// always carries a replacement.
/// </summary>
public readonly record struct AlgebraRewriteOutcome
{
    /// <summary>Constructs an outcome; reachable only through the static factories.</summary>
    /// <param name="application">The rule's verdict.</param>
    /// <param name="algebra">The operator that flows onward.</param>
    private AlgebraRewriteOutcome(AlgebraRewriteApplication application, AlgebraOperator algebra)
    {
        Application = application;
        Algebra = algebra;
    }

    /// <summary>The rule's verdict at this position.</summary>
    public AlgebraRewriteApplication Application { get; }

    /// <summary>The operator that flows onward — the replacement when applied, the input otherwise.</summary>
    public AlgebraOperator Algebra { get; }

    /// <summary>The rule's pattern did not match; <paramref name="node"/> passes through untouched.</summary>
    /// <param name="node">The unmatched input operator.</param>
    /// <returns>The pass-through outcome.</returns>
    public static AlgebraRewriteOutcome NotApplicable(AlgebraOperator node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new AlgebraRewriteOutcome(AlgebraRewriteApplication.NotApplicable, node);
    }

    /// <summary>The rule matched and replaces the position with <paramref name="replacement"/>, which must preserve the semantics the rule's kind declares — answer identity for a plan rule, its declared entailment extension's specified semantics for a semantic rule.</summary>
    /// <param name="replacement">The replacement operator.</param>
    /// <returns>The applied outcome.</returns>
    public static AlgebraRewriteOutcome Applied(AlgebraOperator replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        return new AlgebraRewriteOutcome(AlgebraRewriteApplication.Applied, replacement);
    }

    /// <summary>The rule's pattern matched but the rule declined; <paramref name="node"/> passes through untouched and the decline is traceable.</summary>
    /// <param name="node">The matched-but-declined input operator.</param>
    /// <returns>The abstained outcome.</returns>
    public static AlgebraRewriteOutcome Abstained(AlgebraOperator node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new AlgebraRewriteOutcome(AlgebraRewriteApplication.Abstained, node);
    }
}
