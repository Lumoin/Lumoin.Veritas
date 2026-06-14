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
/// The snapshot tableau engine runs no solver, so its
/// <see cref="SolveCount"/> is zero and its <see cref="SolverTotals"/> is
/// <see cref="SatSolveStatistics.Empty"/>; the SAT-backed engine fills both.
/// Wall-clock cost is not carried here — the engine has no clock — so the
/// emitting site times the decision and records the elapsed alongside.
/// </remarks>
/// <param name="ModuleAxiomCount">The number of axioms in the decided module.</param>
/// <param name="SolveCount">The number of world solves the decision ran; zero for the snapshot tableau engine.</param>
/// <param name="SolverTotals">The solver counters summed across the decision's world solves; <see cref="SatSolveStatistics.Empty"/> for the snapshot tableau engine.</param>
public readonly record struct ReasoningDecisionStatistics(
    int ModuleAxiomCount,
    int SolveCount,
    SatSolveStatistics SolverTotals)
{
    /// <summary>The zero statistics: a decision that ran no solve.</summary>
    public static ReasoningDecisionStatistics Empty => default;
}
