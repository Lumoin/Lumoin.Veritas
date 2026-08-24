namespace Lumoin.Veritas.Sparql.Algebra.Rewriting;

/// <summary>
/// How a rewrite rule responded to one operator position: the value-based three-state outcome that
/// distinguishes "the pattern did not match" from "the pattern matched but the rule declined", so tracing
/// and the pipeline's fixpoint decision can tell nothing-to-do from gave-up.
/// </summary>
public enum AlgebraRewriteApplication
{
    /// <summary>The rule's pattern did not match this operator; the input passes through untouched.</summary>
    NotApplicable = 0,

    /// <summary>The rule matched and produced an answer-preserving replacement for this operator.</summary>
    Applied = 1,

    /// <summary>The rule's pattern matched but the rule declined to apply — a guard failed or the rule chose caution; the input passes through untouched and the decline is traceable.</summary>
    Abstained = 2
}
