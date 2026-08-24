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
/// <param name="Outcome">How the decision ended — decided whole, decided relative to the interpreted fragment, or abstained on its budget.</param>
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
/// <param name="ElDecided">Whether the EL fast-path decided the module; <see langword="false"/> when the module fell outside the EL fragment and the decision was delegated.</param>
/// <param name="ElRuleApplications">The completion-rule applications the EL saturation ran; zero when the EL fast-path did not decide the module.</param>
/// <param name="ElCompletionEdges">The role edges the EL saturation derived; zero when the EL fast-path did not decide the module.</param>
/// <param name="ContextDecided">Whether the context-saturation engine produced the module's verdict; <see langword="false"/> when the module was delegated to the fallback or its saturation abstained on the inference budget.</param>
/// <param name="ContextRuleApplications">The budget-checked total the context saturation spent — the rule applications together with the datatype-sidecar oracle ticks the inference budget bounds; zero when the context engine did not run.</param>
/// <param name="ContextsCreated">The contexts the context saturation created; zero when the context engine did not run.</param>
/// <param name="ContextsReused">The contexts the context saturation reused from the registry; zero when the context engine did not run.</param>
/// <param name="ContextClausesDerived">The context clauses the context saturation added; zero when the context engine did not run.</param>
/// <param name="ContextClausesEliminated">The context clauses the context saturation removed by backward subsumption; zero when the context engine did not run.</param>
/// <param name="ContextMaxClauses">The largest clause count any single context held; zero when the context engine did not run.</param>
/// <param name="GroundContextsCreated">The ground contexts the context setup minted, one per individual representative in an admitted ABox axiom; zero when the context engine did not run or the module carried no admitted ABox axiom.</param>
/// <param name="GroundEdgesSeeded">The designated ground-target function edges the Succ rule added for asserted object-property edges; zero when the context engine did not run or no asserted edge fired.</param>
/// <param name="GroundClashes">The ground clashes the context decision latched; zero when no ground clash fired.</param>
/// <param name="ContextJoinApplications">The Join-rule applications the context saturation ran; zero without nominal jurisdiction.</param>
/// <param name="ContextRootSuccApplications">The r-Succ applications the context saturation ran; zero without nominal jurisdiction.</param>
/// <param name="ContextRootPredApplications">The r-Pred applications the context saturation ran; zero without nominal jurisdiction.</param>
/// <param name="ContextNomApplications">The Nom-rule applications the context saturation ran; zero without nominal jurisdiction or the rule's co-occurrence trigger.</param>
/// <param name="ContextGeneratedNominals">The generated nominals the Nom rule minted; zero when the rule never fired.</param>
/// <param name="ContextMaxNominalLabelDepth">The deepest generated-nominal label minted; zero without minting.</param>
/// <param name="ContextRootClauses">The largest live clause count any root-class context reached — a watermark over the whole root class; zero without nominal jurisdiction.</param>
/// <param name="ContextRootEdges">The nominal-labelled root edges the r-Succ rule added, a module total across every root-class target; zero without nominal jurisdiction.</param>
/// <param name="ContextRedundantConclusions">The conclusions the context saturation offered but that were already contained up to redundancy — the derivation-funnel churn signal whose ratio against <see cref="ContextRuleApplications"/> separates a productive saturation from re-derivation; zero when the context engine did not run.</param>
/// <param name="ContextTautologyDrops">The conclusions the context saturation dropped at head normalization as tautologies; zero when the context engine did not run.</param>
/// <param name="ContextOutOfGrammarConclusions">The conclusions the context saturation refused because a derived head literal left the context-kind grammar; zero when the context engine did not run.</param>
/// <param name="ContextWorklistEnqueues">The clause-landed events the context saturation enqueued on the worklist — the head of the derivation funnel, the conclusions that survived every gate and were inserted; zero when the context engine did not run.</param>
/// <param name="ContextNominalCountingInverseCooccurrence">Whether nominals, object number restrictions, and inverse roles co-occur in the decided module — the Nom rule's trigger census; <see langword="false"/> when the module was delegated without a survey pass or lacks any of the three legs.</param>
/// <param name="ContextEnumerationHabitat">The enumeration-CSP habitat class the census-first recognizer assigned the module at survey time — the census label on every context-arm decision and abstention; <see cref="EnumerationHabitatClass.None"/> when the module was delegated without a survey pass or carries no habitat shape.</param>
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
    bool ElDecided,
    long ElRuleApplications,
    int ElCompletionEdges,
    bool ContextDecided,
    long ContextRuleApplications,
    int ContextsCreated,
    int ContextsReused,
    int ContextClausesDerived,
    int ContextClausesEliminated,
    int ContextMaxClauses,
    int GroundContextsCreated,
    int GroundEdgesSeeded,
    int GroundClashes,
    long ContextJoinApplications,
    long ContextRootSuccApplications,
    long ContextRootPredApplications,
    long ContextNomApplications,
    int ContextGeneratedNominals,
    int ContextMaxNominalLabelDepth,
    int ContextRootClauses,
    int ContextRootEdges,
    long ContextRedundantConclusions,
    long ContextTautologyDrops,
    long ContextOutOfGrammarConclusions,
    long ContextWorklistEnqueues,
    bool ContextNominalCountingInverseCooccurrence,
    EnumerationHabitatClass ContextEnumerationHabitat,
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
            statistics.ElTotals.ElDecided,
            statistics.ElTotals.CompletionRuleApplications,
            statistics.ElTotals.CompletionEdges,
            statistics.ContextTotals.ContextDecided,
            statistics.ContextTotals.RuleApplications,
            statistics.ContextTotals.ContextsCreated,
            statistics.ContextTotals.ContextsReused,
            statistics.ContextTotals.ClausesDerived,
            statistics.ContextTotals.ClausesEliminated,
            statistics.ContextTotals.MaxContextClauses,
            statistics.ContextTotals.GroundContextsCreated,
            statistics.ContextTotals.GroundEdgesSeeded,
            statistics.ContextTotals.GroundClashes,
            statistics.ContextTotals.JoinApplications,
            statistics.ContextTotals.RootSuccApplications,
            statistics.ContextTotals.RootPredApplications,
            statistics.ContextTotals.NomApplications,
            statistics.ContextTotals.GeneratedNominals,
            statistics.ContextTotals.MaxNominalLabelDepth,
            statistics.ContextTotals.RootContextClauses,
            statistics.ContextTotals.RootEdges,
            statistics.ContextTotals.RedundantConclusions,
            statistics.ContextTotals.TautologyDrops,
            statistics.ContextTotals.OutOfGrammarConclusions,
            statistics.ContextTotals.WorklistEnqueues,
            statistics.ContextTotals.NominalCountingInverseCooccurrence,
            statistics.ContextTotals.EnumerationHabitat,
            elapsedMilliseconds);
    }
}
