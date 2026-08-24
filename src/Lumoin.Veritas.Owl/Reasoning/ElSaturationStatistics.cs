namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The work the EL pay-as-you-go fast-path spent on one module decision: whether
/// EL saturation decided the module or it was delegated to the tableau oracle,
/// and — when decided — the completion-rule applications and role edges the
/// saturation ran. It is the EL-coupled engine's counterpart to
/// <see cref="AlcTableauStatistics"/> and
/// <see cref="Lumoin.Veritas.Core.Sat.SatSolveStatistics"/>: a decision delegated
/// to the tableau leaves this empty and carries the oracle's own totals instead.
/// </summary>
/// <param name="ElDecided">Whether EL saturation decided the module; <see langword="false"/> when the module fell outside the EL fragment and the decision was delegated to the tableau oracle.</param>
/// <param name="CompletionRuleApplications">The completion-rule applications the EL saturation ran; zero when delegated.</param>
/// <param name="CompletionEdges">The role edges the EL saturation derived; zero when delegated.</param>
public readonly record struct ElSaturationStatistics(
    bool ElDecided,
    long CompletionRuleApplications,
    int CompletionEdges)
{
    /// <summary>The empty statistics: no EL saturation decided the module.</summary>
    public static ElSaturationStatistics Empty => default;
}
