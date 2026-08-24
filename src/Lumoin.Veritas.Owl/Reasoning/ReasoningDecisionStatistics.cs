using Lumoin.Veritas.Core.Sat;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The work one description-logic decision spent: the size of the module it
/// decided, the number of world solves it ran, and the solver counters
/// summed across those solves. It is the decision-level telemetry payload —
/// the counterpart to <c>GraphStatistics</c> and <c>ColumnarStatistics</c> —
/// that the decision trace event carries so "is the reasoner the cost, and
/// which decision" is answered from data.
/// </summary>
/// <remarks>
/// The engines fill complementary halves: the SAT-backed engine reports
/// <see cref="SolveCount"/> and <see cref="SolverTotals"/> and leaves
/// <see cref="TableauTotals"/> empty; the snapshot tableau engine runs no
/// solver, so it reports <see cref="TableauTotals"/> and leaves
/// <see cref="SolveCount"/> zero and <see cref="SolverTotals"/> empty; the
/// EL-coupled engine reports <see cref="ElTotals"/> when its fast-path decided
/// the module, and otherwise carries the totals of the tableau oracle it
/// delegated to; the context-saturation engine reports <see cref="ContextTotals"/>
/// when it decided the module and zeroes the rest, mirroring the EL arm's
/// discipline. One composed case carries two engines' totals at once: when the
/// context tier exhausts its budget behind the seam and delegates, the fallback's
/// <see cref="SolverTotals"/> (or <see cref="TableauTotals"/>) are non-zero
/// ALONGSIDE the spent <see cref="ContextTotals"/> whose
/// <see cref="ContextSaturationStatistics.ContextDecided"/> is <see langword="false"/>.
/// Wall-clock cost is not carried here — the engine has no clock — so the emitting
/// site times the decision and records the elapsed alongside.
/// </remarks>
/// <param name="ModuleAxiomCount">The number of axioms in the decided module.</param>
/// <param name="SolveCount">The number of world solves the decision ran; zero for the snapshot tableau engine.</param>
/// <param name="SolverTotals">The solver counters summed across the decision's world solves; <see cref="SatSolveStatistics.Empty"/> for the snapshot tableau engine.</param>
/// <param name="TableauTotals">The tableau counters summed across the decision's tableau runs; <see cref="AlcTableauStatistics.Empty"/> for the SAT-backed engine.</param>
/// <param name="ElTotals">The EL fast-path counters; <see cref="ElSaturationStatistics.Empty"/> unless the EL-coupled engine decided the module by saturation rather than delegating to the tableau.</param>
/// <param name="ContextTotals">The context-saturation counters; <see cref="ContextSaturationStatistics.Empty"/> unless the context-saturation engine decided the module, or its saturation exhausted the budget behind the seam before delegating — in the latter case the totals are the spent saturation's with <see cref="ContextSaturationStatistics.ContextDecided"/> <see langword="false"/>, carried beside the fallback's own totals.</param>
public readonly record struct ReasoningDecisionStatistics(
    int ModuleAxiomCount,
    int SolveCount,
    SatSolveStatistics SolverTotals,
    AlcTableauStatistics TableauTotals = default,
    ElSaturationStatistics ElTotals = default,
    ContextSaturationStatistics ContextTotals = default)
{
    /// <summary>The zero statistics: a decision that ran no solve.</summary>
    public static ReasoningDecisionStatistics Empty => default;
}
