using System;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The trace of one description-logic decision: how it ended, the size of the
/// module it decided, and the solver work it spent — the
/// <see cref="ReasoningDecisionStatistics"/> flattened onto the event — plus
/// the wall-clock cost. Emitted once per delegated beyond-RL module, it
/// answers "is the reasoner the cost, which decision, and did it abstain"
/// from a joinable record rather than a hunt, the per-decision companion to
/// the per-materialization <see cref="ReasoningTraceEvent"/>.
/// </summary>
/// <param name="SequenceNumber">The event's monotonic sequence number within the trace stream.</param>
/// <param name="TimestampTicks">The emission time in UTC ticks.</param>
/// <param name="CorrelationId">The correlation id linking this event to the materialization it belongs to.</param>
/// <param name="Outcome">How the decision ended — decided, or abstained on its budget.</param>
/// <param name="ModuleAxiomCount">The number of axioms in the decided module.</param>
/// <param name="SolveCount">The number of world solves the decision ran.</param>
/// <param name="SolverDecisions">The branch decisions summed across the decision's world solves.</param>
/// <param name="SolverPropagations">The unit propagations summed across the decision's world solves.</param>
/// <param name="SolverConflicts">The conflicts summed across the decision's world solves.</param>
/// <param name="SolverLearnedClauses">The first-UIP clauses learned across the decision's world solves.</param>
/// <param name="SolverMaxDecisionLevel">The deepest decision level reached by any of the decision's world solves.</param>
/// <param name="TableauRuns">The tableau runs the snapshot engine started; zero for the SAT-backed engine.</param>
/// <param name="TableauRuleApplications">The tableau rule applications summed across the decision's runs.</param>
/// <param name="TableauBranches">The disjunction branches the snapshot engine opened across the decision's runs.</param>
/// <param name="TableauClashes">The clashes the snapshot engine backtracked from across the decision's runs.</param>
/// <param name="TableauMaxNodes">The largest node forest any of the decision's tableau runs reached.</param>
/// <param name="ElapsedMilliseconds">The decision's wall-clock cost in fractional milliseconds, measured on a monotonic clock so a sub-millisecond decision is not lost to zero.</param>
public readonly record struct ReasoningDecisionTraceEvent(
    long SequenceNumber,
    long TimestampTicks,
    Guid CorrelationId,
    ReasoningDecisionOutcome Outcome,
    int ModuleAxiomCount,
    int SolveCount,
    int SolverDecisions,
    long SolverPropagations,
    int SolverConflicts,
    int SolverLearnedClauses,
    int SolverMaxDecisionLevel,
    int TableauRuns,
    long TableauRuleApplications,
    int TableauBranches,
    int TableauClashes,
    int TableauMaxNodes,
    double ElapsedMilliseconds): ITraceEvent
{
    /// <summary>Builds the event from a decision's outcome, statistics, and measured cost.</summary>
    /// <param name="sequenceNumber">The event's sequence number.</param>
    /// <param name="timestampTicks">The emission time in UTC ticks.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="outcome">How the decision ended.</param>
    /// <param name="statistics">The work the decision spent.</param>
    /// <param name="elapsedMilliseconds">The decision's wall-clock cost.</param>
    /// <returns>The trace event.</returns>
    public static ReasoningDecisionTraceEvent From(
        long sequenceNumber,
        long timestampTicks,
        Guid correlationId,
        ReasoningDecisionOutcome outcome,
        in ReasoningDecisionStatistics statistics,
        double elapsedMilliseconds)
    {
        return new ReasoningDecisionTraceEvent(
            sequenceNumber,
            timestampTicks,
            correlationId,
            outcome,
            statistics.ModuleAxiomCount,
            statistics.SolveCount,
            statistics.SolverTotals.Decisions,
            statistics.SolverTotals.Propagations,
            statistics.SolverTotals.Conflicts,
            statistics.SolverTotals.LearnedClauses,
            statistics.SolverTotals.MaxDecisionLevel,
            statistics.TableauTotals.TableauRuns,
            statistics.TableauTotals.RuleApplications,
            statistics.TableauTotals.Branches,
            statistics.TableauTotals.Clashes,
            statistics.TableauTotals.MaxNodes,
            elapsedMilliseconds);
    }
}
