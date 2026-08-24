namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// How a description-logic decision ended: with a verdict covering the module
/// whole, with a verdict scoped to the fragment the calculus interprets, or
/// abstaining because its budget ran out. Abstaining with a reason is the
/// honest answer when the search would otherwise run unbounded — the caller
/// learns the engine did not decide, rather than receiving a guess — and the
/// fragment-relative outcome is the same honesty for a consistency claim that
/// holds only over the constructs the calculus could read.
/// </summary>
public enum ReasoningDecisionOutcome
{
    /// <summary>The engine reached a verdict covering the module whole: either no construct was excluded, or the verdict is inconsistent and condemns the module regardless of any remainder.</summary>
    Decided = 0,

    /// <summary>The reasoning budget was exhausted before a verdict; the decision carries no verdict.</summary>
    AbstainedBudget = 1,

    /// <summary>
    /// The engine reached a verdict scoped to the fragment it interprets: the
    /// module carries constructs the deciding calculus excluded, named on
    /// <see cref="ModuleVerdict.UnsupportedConstructs"/>. A consistency claim
    /// under this outcome says nothing about the named remainder; an
    /// inconsistency is always reported as <see cref="Decided"/> because it
    /// condemns the module whole.
    /// </summary>
    DecidedFragmentRelative = 2,
}
