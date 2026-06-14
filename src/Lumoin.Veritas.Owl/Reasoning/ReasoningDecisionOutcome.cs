namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// How a description-logic decision ended: with a verdict, or abstaining
/// because its budget ran out. Abstaining with a reason is the honest answer
/// when the search would otherwise run unbounded — the caller learns the
/// engine did not decide, rather than receiving a guess.
/// </summary>
public enum ReasoningDecisionOutcome
{
    /// <summary>The engine reached a verdict; it is fragment-relative when the module names unsupported constructs.</summary>
    Decided = 0,

    /// <summary>The reasoning budget was exhausted before a verdict; the decision carries no verdict.</summary>
    AbstainedBudget = 1,
}
