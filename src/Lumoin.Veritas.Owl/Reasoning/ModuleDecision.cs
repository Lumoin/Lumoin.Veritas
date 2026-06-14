namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The result of a description-logic decision: the outcome, the verdict when
/// one was reached, and the work the decision spent. It is what
/// <see cref="DescriptionLogicDelegate"/> returns — the verdict and its
/// telemetry together — so a caller both learns the answer and can attribute
/// the cost without a second channel.
/// </summary>
/// <remarks>
/// <see cref="Verdict"/> is present exactly when <see cref="Outcome"/> is
/// <see cref="ReasoningDecisionOutcome.Decided"/>; an abstention carries no
/// verdict and the <see cref="ReasoningDecisionOutcome"/> says why. The two
/// factories construct the consistent pairings, so a caller never has to
/// keep the outcome and the verdict's presence in step by hand.
/// </remarks>
/// <param name="Outcome">How the decision ended.</param>
/// <param name="Verdict">The verdict when <paramref name="Outcome"/> is <see cref="ReasoningDecisionOutcome.Decided"/>; otherwise <c>null</c>.</param>
/// <param name="Statistics">The work the decision spent.</param>
public sealed record ModuleDecision(
    ReasoningDecisionOutcome Outcome,
    ModuleVerdict? Verdict,
    ReasoningDecisionStatistics Statistics)
{
    /// <summary>Builds a decided result carrying the reached verdict.</summary>
    /// <param name="verdict">The reached verdict.</param>
    /// <param name="statistics">The work the decision spent.</param>
    /// <returns>The decided result.</returns>
    public static ModuleDecision Decided(ModuleVerdict verdict, ReasoningDecisionStatistics statistics)
    {
        return new ModuleDecision(ReasoningDecisionOutcome.Decided, verdict, statistics);
    }

    /// <summary>Builds an abstaining result for a decision whose budget ran out before a verdict.</summary>
    /// <param name="statistics">The work the decision spent before abstaining.</param>
    /// <returns>The abstaining result.</returns>
    public static ModuleDecision AbstainedOnBudget(ReasoningDecisionStatistics statistics)
    {
        return new ModuleDecision(ReasoningDecisionOutcome.AbstainedBudget, Verdict: null, statistics);
    }
}
