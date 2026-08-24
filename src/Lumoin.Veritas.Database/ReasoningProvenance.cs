using System;
using System.Collections.Generic;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Reasoning;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The provenance of the served default graph's entailments: which reasoning strategy ran over the opened
/// default graph, what it decided, and exactly what its verdict does not cover. A host inspects this after an
/// immutable open on <see cref="VeritasEngine.ReasoningProvenance"/> so it is never left treating a
/// fragment-relative or inconsistent outcome as whole-truth — a partial closure served after a derived
/// inconsistency, or a consistency claim scoped to the fragment the deciding calculus could read, is never
/// silently indistinguishable from a whole-module verdict.
/// </summary>
/// <remarks>
/// <see cref="IsDecisive"/> encodes the reading contract once: read <see cref="IsConsistent"/> as covering the
/// mapped content whole only when it is <see langword="true"/>. A consistency claim is scoped — never whole —
/// in three shapes: a delegated verdict with a named remainder (<see cref="UndecidedConstructs"/> non-empty),
/// a budget abstention (<see cref="DecisionOutcome"/> is
/// <see cref="ReasoningDecisionOutcome.AbstainedBudget"/> and <see cref="UndecidedConstructs"/> stays empty —
/// the module went wholly undecided, not decided), and a beyond-ceiling module reported without a delegate
/// (<see cref="Reason"/> is <see cref="ReasoningSelectionReason.BeyondRlReported"/>). An
/// <see cref="IsConsistent"/> of <see langword="false"/> is a decided inconsistency — a fired falsity rule
/// (named on <see cref="InconsistencyRule"/>) or a delegated condemnation — and condemnation covers the
/// module whole regardless of any remainder.
/// </remarks>
/// <param name="IsConsistent">Whether no contradiction was derived (and the module verdict, when one was obtained, agreed) — scoped to less than the whole mapped content when <see cref="IsDecisive"/> is <see langword="false"/>.</param>
/// <param name="InconsistencyRule">The falsity rule that fired, or <see langword="null"/> when consistent or when a delegated verdict — not a rule — condemned the module.</param>
/// <param name="DerivedCount">The number of triples the materialisation derived and committed into the served default graph.</param>
/// <param name="Strategy">The reasoning strategy the rendezvous selected.</param>
/// <param name="Reason">The expressiveness rung that selected the strategy.</param>
/// <param name="DetectedProfiles">The profile floor the default graph's TBox was detected at.</param>
/// <param name="ModuleAxiomCount">The axiom count of the beyond-ceiling module handed to the description-logic delegate; <c>0</c> when the content stayed within the in-engine calculi.</param>
/// <param name="DecisionOutcome">How the delegated decision ended, or <see langword="null"/> when no delegate ran.</param>
/// <param name="DecisionStatistics">The work the delegated decision spent, or <see langword="null"/> when no delegate ran.</param>
public sealed record ReasoningProvenance(
    bool IsConsistent,
    string? InconsistencyRule,
    int DerivedCount,
    ReasoningStrategy Strategy,
    ReasoningSelectionReason Reason,
    OwlProfiles DetectedProfiles,
    int ModuleAxiomCount,
    ReasoningDecisionOutcome? DecisionOutcome,
    ReasoningDecisionStatistics? DecisionStatistics)
{
    /// <summary>
    /// The named constructs of the beyond-ceiling module the delegated calculus excluded from its verdict.
    /// A non-empty list scopes <see cref="IsConsistent"/> to the supported fragment, so it says nothing about
    /// these. Empty when no module was delegated, when the verdict covers the module whole, when the verdict
    /// itself is inconsistent (condemnation covers the module regardless), or when the decision abstained on
    /// its budget. Carried verbatim from <see cref="ReasoningResult.UndecidedConstructs"/>.
    /// </summary>
    public IReadOnlyList<string> UndecidedConstructs { get; init; } = [];

    /// <summary>
    /// The maintained-lane decisiveness the commit already folded, or <see langword="null"/> to derive it from
    /// the fields. The immutable lane leaves this <see langword="null"/> so <see cref="IsDecisive"/> is computed
    /// exactly as before; the mutable lane sets it from the commit so a delegated whole-module verdict that has
    /// DECAYED past its decided generation reads as fragment-relative even though its inherited
    /// <see cref="DecisionOutcome"/> still names the whole-module decision.
    /// </summary>
    private bool? MaintainedDecisive { get; init; }

    /// <summary>
    /// Whether <see cref="IsConsistent"/> covers the mapped content whole. A derived inconsistency is always
    /// decisive — condemnation is monotone, so a falsity found in any decided fragment condemns the whole. A
    /// consistency claim is decisive only when nothing went unexamined: no beyond-ceiling module reported
    /// without a delegate, no budget abstention, and no fragment-relative remainder — any delegated decision
    /// other than a whole-module <see cref="ReasoningDecisionOutcome.Decided"/> leaves the claim scoped. A
    /// structure the RDF mapping cannot read yields no axiom and is outside every verdict this record carries.
    /// On the mutable lane a maintained commit supplies its own folded verdict (<see cref="MaintainedDecisive"/>),
    /// which additionally reads a decayed whole-module verdict as fragment-relative.
    /// </summary>
    public bool IsDecisive =>
        MaintainedDecisive
        ?? (!IsConsistent
            || (Reason != ReasoningSelectionReason.BeyondRlReported
                && DecisionOutcome is null or ReasoningDecisionOutcome.Decided));

    /// <summary>
    /// Maps a reasoning result onto the facade provenance. It retains neither the pre-materialisation
    /// <see cref="ReasoningResult.Store"/> — which would otherwise stay pinned — nor the
    /// <see cref="ReasoningResult.Module"/>; only the module's axiom count is carried, as
    /// <see cref="ModuleAxiomCount"/>.
    /// </summary>
    /// <param name="result">The reasoning result the open's materialisation produced.</param>
    /// <returns>The facade provenance of the served entailments.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public static ReasoningProvenance From(ReasoningResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ReasoningProvenance(
            result.IsConsistent,
            result.InconsistencyRule,
            result.DerivedCount,
            result.Strategy,
            result.Reason,
            result.DetectedProfiles,
            result.Module?.Axioms.Count ?? 0,
            result.DecisionOutcome,
            result.DecisionStatistics)
        {
            UndecidedConstructs = result.UndecidedConstructs,
        };
    }

    /// <summary>
    /// Maps one maintained commit's folded facts onto the facade provenance — the per-landed-generation
    /// provenance a reasoned MUTABLE engine surfaces after each commit. It carries neither the served store nor
    /// the beyond-RL module; only the module's axiom count reaches <see cref="ModuleAxiomCount"/>.
    /// <see cref="DerivedCount"/> is the served derived-set size only while the commit's overlay is on and
    /// <c>0</c> when it is withdrawn (an inconsistent generation serves the asserted graph, so it reports the
    /// count it serves — never the closure's raw derived set). The decayed inheritance rule the commit already
    /// applied (a non-re-decided beyond-RL generation carries its last-landed decision facts and scopes its
    /// consistency claim to the RL fragment) is preserved verbatim, so a delegated whole-module verdict that has
    /// decayed reads through <see cref="IsDecisive"/> as fragment-relative.
    /// </summary>
    /// <param name="commit">The maintained commit whose folded verdict, floor, and decision facts to map.</param>
    /// <returns>The facade provenance of the commit's served entailments.</returns>
    public static ReasoningProvenance From(ReasoningMaintainedCommit commit)
    {
        return new ReasoningProvenance(
            commit.IsConsistent,
            commit.InconsistencyRule,
            commit.OverlayOn ? commit.DerivedCount : 0,
            commit.Strategy,
            commit.Reason,
            commit.DetectedProfiles,
            commit.ModuleAxiomCount,
            commit.DecisionOutcome,
            commit.DecisionStatistics)
        {
            UndecidedConstructs = commit.UndecidedConstructs,
            MaintainedDecisive = commit.IsDecisive,
        };
    }
}
