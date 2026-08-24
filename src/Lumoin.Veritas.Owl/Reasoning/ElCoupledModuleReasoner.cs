using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.El;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The EL pay-as-you-go engine behind the <see cref="DescriptionLogicDelegate"/>
/// seam: a module wholly within the EL⊥ fragment is decided by consequence-based
/// saturation alone — no tableau search — and any module outside it is delegated
/// whole to a tableau oracle (the snapshot engine by default).
/// </summary>
/// <remarks>
/// <para>
/// <b>Verdict-preserving.</b> The engine routes; it never changes an answer.
/// <see cref="ElModuleSurvey"/> admits only modules EL saturation decides soundly
/// and completely, and EL⊥ is a strict fragment of the tableau's ALC(H)+S, so the
/// EL verdict equals the tableau's on every admitted module. A differential sweep
/// against the snapshot engine over the conformance suite and the random module
/// generator is the standing proof.
/// </para>
/// <para>
/// <b>The gain.</b> For the large EL-heavy ontologies that dominate real
/// workloads, EL saturation decides consistency without the tableau and
/// classifies past the snapshot engine's pairwise signature cap — the
/// pay-as-you-go shape whose front-end captures the headline classification cost.
/// The finer residual / critical-node fallback (seeding the completion graph with
/// EL labels and expanding only the residual) is the next increment; the
/// saturation already produces completion-graph-compatible per-node labels.
/// </para>
/// <para>
/// <b>Sibling by seam.</b> The fallback oracle is injectable, so EL + snapshot and
/// EL + SAT are both expressible — the whole-engine seam rule that lets the three
/// arms be measured against one another behind the same delegate.
/// </para>
/// <para>
/// <b>The front door folds first.</b> The reserved-vocabulary constant fold
/// (<see cref="ReservedVocabularyFold"/>) runs on the module before the survey
/// sees it, so a restriction over a fixed-extension reserved property —
/// semantically <c>owl:Thing</c> or <c>owl:Nothing</c> at every element of every
/// interpretation — is routed as that constant rather than declined for its
/// reserved role. Every decision-bearing tier folds at its own front door, so
/// this tier and the tier a delegated module lands on read the same folded
/// module and answer alike.
/// </para>
/// <para>
/// <b>The boundary is layered, and delegation is not a wall.</b> Three tiers sit
/// behind the seam. The <i>asserted ground graph</i> — where every edge over an
/// inverse-paired, symmetric, or functional role is a ground fact between concrete
/// individuals — is decided here directly (the saturation mirror and the pre-merge
/// union, on indexes that cost nothing when the module has no such role). One tier
/// out is an <i>existential restriction over an inverse-related role</i>: its
/// successor would have to be a per-occurrence node rather than the shared filler,
/// so that a constraint propagated backward across the inverse cannot contaminate
/// the shared filler — the same per-occurrence rewrite the property-range rule
/// already applies (<see cref="ElClassifier"/>), but over a cyclic TBox it must also
/// bound the resulting successor forest. That tier is delegated today, a bounded
/// extension of the polynomial calculus rather than a fixed limit, and its own
/// natural fallback is a scoped consequence-based sub-engine, not necessarily the
/// general decider. Disjunction, full negation, and qualified cardinality above
/// one leave the EL fragment entirely: those modules fall to the injected
/// fallback, where the production composition's context-saturation tier decides
/// them by ordered resolution over disjunctive heads.
/// </para>
/// </remarks>
public static class ElCoupledModuleReasoner
{
    /// <summary>
    /// Wraps the EL fast-path as the seam delegate, delegating non-EL modules to
    /// <paramref name="fallback"/> (the snapshot engine when <see langword="null"/>).
    /// </summary>
    /// <param name="fallback">The tableau oracle for non-EL modules; the snapshot engine when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DescriptionLogicDelegate? fallback = null)
    {
        return CreateDelegate(DatatypeRegistry.Empty, fallback);
    }

    /// <summary>
    /// Wraps the EL fast-path as the seam delegate consulting a registered-datatype set at the
    /// concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="CreateDelegate(DescriptionLogicDelegate?)"/>. When no fallback is supplied the snapshot
    /// engine that consults the same registry is used, so a delegated module decides identically.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="fallback">The tableau oracle for non-EL modules; the registry-consulting snapshot engine when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DatatypeRegistry registry, DescriptionLogicDelegate? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return new CoupledSeam(fallback ?? AlcModuleReasoner.CreateDelegate(registry), registry).Decide;
    }

    /// <summary>
    /// Decides the module's fragment: consistency and module-local subsumptions.
    /// An EL module is decided by saturation; any other is decided by the
    /// snapshot tableau.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return Decide(module, DatatypeRegistry.Empty, cancellationToken);
    }

    /// <summary>
    /// Decides the module's fragment consulting a registered-datatype set at the concrete-domain leaves —
    /// the registry-carrying counterpart of <see cref="Decide(ReasoningModule, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(ReasoningModule module, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if(TryDecideEl(module, includeSubsumptions: true, registry, out ModuleVerdict? verdict, out _, cancellationToken))
        {
            return verdict;
        }

        return AlcModuleReasoner.Decide(module, registry, cancellationToken);
    }

    /// <summary>
    /// Decides the module's fragment for consistency only — the subsumption list
    /// stays empty whatever the signature size, and no subsumption enumeration
    /// runs.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return DecideConsistency(module, DatatypeRegistry.Empty, cancellationToken);
    }

    /// <summary>
    /// Decides the module's fragment for consistency only, consulting a registered-datatype set at the
    /// concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="DecideConsistency(ReasoningModule, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(ReasoningModule module, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if(TryDecideEl(module, includeSubsumptions: false, registry, out ModuleVerdict? verdict, out _, cancellationToken))
        {
            return verdict;
        }

        return AlcModuleReasoner.DecideConsistency(module, registry, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> — the verdict
    /// together with the work it spent — the form the
    /// <see cref="DescriptionLogicDelegate"/> seam returns. An EL decision carries
    /// the saturation telemetry and empty solver/tableau totals; a delegated one
    /// carries the snapshot tableau's own decision whole.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, DatatypeRegistry.Empty, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> consulting a registered-datatype set at the
    /// concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="DecideModule(ReasoningModule, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if(TryDecideEl(module, includeSubsumptions: true, registry, out ModuleVerdict? verdict, out ElSaturationStatistics statistics, cancellationToken))
        {
            return ModuleDecision.Decided(verdict, ElDecisionStatistics(module.Axioms.Count, statistics));
        }

        return AlcModuleReasoner.DecideModule(module, registry, cancellationToken);
    }

    /// <summary>
    /// Tries to decide the module by the EL fast-path. The module is folded here
    /// first (<see cref="ReservedVocabularyFold"/>) — the tier's front door,
    /// mirroring the tableau and context tiers — so the survey, the saturation,
    /// and the subsumption-sweep signature all read the folded view. The decision
    /// succeeds only when that folded module is EL-decidable
    /// (<see cref="ElModuleSurvey"/>) and the saturation flagged no construct it
    /// could not interpret — the second check is a belt-and-suspenders guard
    /// against any survey/normalizer drift. The fold's reassignment is local to
    /// this method, so a caller that falls through to its tableau fallback hands
    /// the fallback its own raw module, which folds at its own front door.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="includeSubsumptions">Whether to enumerate module-local subsumptions when the signature qualifies.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="verdict">The EL verdict when the decision is taken; otherwise <see langword="null"/>.</param>
    /// <param name="statistics">The EL saturation telemetry when the decision is taken; otherwise <see cref="ElSaturationStatistics.Empty"/>.</param>
    /// <param name="cancellationToken">A token that aborts saturation.</param>
    /// <returns><see langword="true"/> when the EL fast-path decided the module.</returns>
    private static bool TryDecideEl(
        ReasoningModule module,
        bool includeSubsumptions,
        DatatypeRegistry registry,
        [NotNullWhen(true)] out ModuleVerdict? verdict,
        out ElSaturationStatistics statistics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        verdict = null;
        statistics = ElSaturationStatistics.Empty;

        //Fold the reserved-vocabulary constant shapes before the survey: a
        //restriction whose fixed-extension reserved property makes it
        //semantically owl:Thing or owl:Nothing becomes that constant, so the
        //survey, the saturation, and the signature cap read a plain named-class
        //reference instead of a reserved role the survey would decline.
        module = ReservedVocabularyFold.Apply(module);

        if(!ElModuleSurvey.IsElDecidable(module.Axioms))
        {
            return false;
        }

        ElModuleClassification result = ElClassifier.ClassifyModule(module.Axioms, registry, cancellationToken);

        //The survey guarantees the saturation interprets every axiom; a
        //non-empty remainder would mean a survey/normalizer mismatch, so defer
        //to the tableau rather than trust a fragment-relative EL verdict.
        if(result.Classification.UnsupportedConstructs.Count > 0)
        {
            return false;
        }

        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions = [];
        if(includeSubsumptions && result.IsConsistent)
        {
            //A HasSelf-bearing module's consumer/producer classes sit only on
            //ObjectHasSelf occurrences the ALC translation never reaches, so the
            //widened axiom-walk signature surfaces them; a Self-free module keeps
            //the un-widened ALC signature so its certified sweep is unchanged.
            List<Utf8String> signature = ModuleSweepSignature.CarriesHasSelf(module)
                ? ModuleSweepSignature.Build(module)
                : AlcModuleReasoner.Translate(module).SignatureClasses;
            if(signature.Count <= AlcModuleReasoner.SubsumptionSignatureCap)
            {
                //A ⊑ B over the same named-class signature and the same ≤16 cap
                //as the tableau, answered from the EL closure — TBox-only
                //subsumption, identical to the tableau's per-pair check.
                foreach(Utf8String subClass in signature)
                {
                    foreach(Utf8String superClass in signature)
                    {
                        if(subClass.Equals(superClass))
                        {
                            continue;
                        }

                        if(result.Classification.IsSubsumedBy(subClass, superClass))
                        {
                            subsumptions.Add((new NamedNode(subClass), new NamedNode(superClass)));
                        }
                    }
                }
            }
        }

        statistics = new ElSaturationStatistics(ElDecided: true, result.CompletionRuleApplications, result.CompletionEdges);
        verdict = new ModuleVerdict(result.IsConsistent, subsumptions);

        return true;
    }

    /// <summary>Builds the decision statistics for an EL-decided module: the EL totals, with empty solver and tableau totals.</summary>
    /// <param name="moduleAxiomCount">The number of axioms in the decided module.</param>
    /// <param name="statistics">The EL saturation telemetry.</param>
    /// <returns>The decision statistics.</returns>
    private static ReasoningDecisionStatistics ElDecisionStatistics(int moduleAxiomCount, ElSaturationStatistics statistics)
    {
        return new ReasoningDecisionStatistics(moduleAxiomCount, SolveCount: 0, SatSolveStatistics.Empty, AlcTableauStatistics.Empty, statistics);
    }

    /// <summary>The seam: decide an EL module by saturation, or delegate the rest to the fallback oracle.</summary>
    private sealed class CoupledSeam
    {
        /// <summary>The tableau oracle for modules outside the EL fragment.</summary>
        private DescriptionLogicDelegate Fallback { get; }

        /// <summary>The registered-datatype set the EL sidecar consults where the family classifier abstains.</summary>
        private DatatypeRegistry Registry { get; }

        /// <summary>Initialises the seam with its fallback oracle and registered-datatype set.</summary>
        /// <param name="fallback">The tableau oracle for non-EL modules.</param>
        /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
        public CoupledSeam(DescriptionLogicDelegate fallback, DatatypeRegistry registry)
        {
            Fallback = fallback;
            Registry = registry;
        }

        /// <summary>Decides the module by the EL fast-path, or delegates it to the fallback oracle.</summary>
        /// <param name="module">The module to decide.</param>
        /// <param name="cancellationToken">A token that aborts the decision.</param>
        /// <returns>The decision.</returns>
        public ValueTask<ModuleDecision> Decide(ReasoningModule module, CancellationToken cancellationToken)
        {
            if(TryDecideEl(module, includeSubsumptions: true, Registry, out ModuleVerdict? verdict, out ElSaturationStatistics statistics, cancellationToken))
            {
                return ValueTask.FromResult(ModuleDecision.Decided(verdict, ElDecisionStatistics(module.Axioms.Count, statistics)));
            }

            return Fallback(module, cancellationToken);
        }
    }
}
