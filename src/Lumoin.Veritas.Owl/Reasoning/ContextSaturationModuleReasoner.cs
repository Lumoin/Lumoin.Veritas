using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Contexts;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The consequence-based context-saturation engine behind the
/// <see cref="DescriptionLogicDelegate"/> seam: a module wholly within the
/// disjunctive SRIQ slice (<see cref="ContextModuleSurvey"/>) is decided by
/// consequence-based saturation alone — no tableau search — and any module
/// outside it is delegated whole to the fallback oracle (the snapshot engine by
/// default), reference-identical.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verdict-preserving.</b> The engine routes; it never changes an answer.
/// The survey admits only modules the saturation decides soundly and completely,
/// and a second gate over the clausification result — an empty remainder, no
/// automaton-budget rejection, no fresh role other than a DL4 counting role, and
/// every head literal within the per-literal context grammar (central concept
/// atoms in disjunctive heads; equality heads only in the neighbour / function
/// grammar) — is the belt-and-suspenders guard that turns any survey/clausifier
/// drift into an honest delegation rather than a fragment-relative context verdict.
/// </para>
/// <para>
/// <b>The decision.</b> An admitted, gate-passing module is clausified once;
/// the saturation seeds the trivial consistency context <c>v_⊤</c> and, when the
/// signature is within the shared subsumption cap, one query context per
/// signature class, then saturates to its fixpoint under the inference budget.
/// Inconsistency reads off <c>v_⊤</c>; the module-local subsumptions read off
/// the per-class query contexts. On the standalone decision surfaces a run that
/// exhausts the inference budget abstains with a reason — the abstention is the
/// answer, not a delegation; behind the seam the same exhaustion is composed into
/// a delegation so a budget-explosive module still reaches the fallback oracle.
/// Each deciding leg bounds its own work by the budget, so a module that falls
/// from the context tier to the snapshot oracle may spend up to the bound in each.
/// </para>
/// <para>
/// <b>The production tier.</b> The engine is composed as a sibling behind the
/// same seam the EL-coupled and tableau engines use, so the composition root
/// wires <c>ElCoupled(ContextSaturation(SatBacked(budget)))</c>: the EL fast path
/// takes the EL⊥ modules, the context tier takes the disjunctive-SRIQ modules the
/// EL arm declines, and the SAT-backed oracle takes the rest and any
/// budget-exhausted context module.
/// </para>
/// </remarks>
public static class ContextSaturationModuleReasoner
{
    /// <summary>
    /// Wraps the context-saturation engine as the seam delegate with an unbounded
    /// inference budget, delegating non-admitted modules to
    /// <paramref name="fallback"/> (the snapshot engine when <see langword="null"/>).
    /// </summary>
    /// <param name="fallback">The oracle for modules the engine does not admit; the snapshot engine when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DescriptionLogicDelegate? fallback = null)
    {
        return CreateDelegate(ReasoningBudget.Unbounded, fallback);
    }

    /// <summary>
    /// Wraps the context-saturation engine as the seam delegate under a given
    /// inference budget, delegating non-admitted modules to
    /// <paramref name="fallback"/>. When no fallback is supplied the snapshot engine
    /// is used, bounded by the same budget so a fallback-omitted composition inherits
    /// a bounded snapshot oracle rather than an unbounded one. An admitted module
    /// whose saturation exhausts the budget abstains rather than delegating.
    /// </summary>
    /// <param name="budget">The inference budget bounding each admitted module's saturation and, when no fallback is supplied, the default snapshot oracle; <see cref="ReasoningBudget.Unbounded"/> places no bound.</param>
    /// <param name="fallback">The oracle for modules the engine does not admit; the snapshot engine bounded by <paramref name="budget"/> when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(ReasoningBudget budget, DescriptionLogicDelegate? fallback = null)
    {
        return CreateDelegate(DatatypeRegistry.Empty, budget, fallback);
    }

    /// <summary>
    /// Wraps the context-saturation engine as the seam delegate under a given inference budget consulting a
    /// registered-datatype set at the datatype sidecar — the registry-carrying counterpart of
    /// <see cref="CreateDelegate(ReasoningBudget, DescriptionLogicDelegate?)"/>. When no fallback is supplied
    /// the snapshot engine that consults the same registry is used, bounded by the same budget so a
    /// fallback-omitted composition inherits a bounded snapshot oracle rather than an unbounded one.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The inference budget bounding each admitted module's saturation and, when no fallback is supplied, the default snapshot oracle.</param>
    /// <param name="fallback">The oracle for modules the engine does not admit; the registry-consulting snapshot engine bounded by <paramref name="budget"/> when <see langword="null"/>.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DatatypeRegistry registry, ReasoningBudget budget, DescriptionLogicDelegate? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return new ContextSeam(fallback ?? AlcModuleReasoner.CreateDelegate(registry, budget), budget, registry).Decide;
    }

    /// <summary>
    /// Decides the module's fragment: consistency and module-local subsumptions.
    /// An admitted module is decided by saturation, any other by the snapshot
    /// tableau. The verdict surface is unbounded — it runs to a verdict or throws
    /// on cancellation.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: true, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out _, cancellationToken) && decision.Verdict is ModuleVerdict verdict)
        {
            return verdict;
        }

        return AlcModuleReasoner.Decide(module, cancellationToken);
    }

    /// <summary>
    /// Decides the module's fragment for consistency only — the subsumption list
    /// stays empty whatever the signature size, no subsumption enumeration runs,
    /// and no query context is created (the trivial context alone carries the
    /// consistency verdict). The verdict surface is unbounded — it runs to a
    /// verdict or throws on cancellation.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: false, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out _, cancellationToken) && decision.Verdict is ModuleVerdict verdict)
        {
            return verdict;
        }

        return AlcModuleReasoner.DecideConsistency(module, cancellationToken);
    }

    /// <summary>
    /// Decides the module's fragment for consistency only consulting a registered-datatype set at the
    /// datatype sidecar, under an unbounded budget — the registry-carrying counterpart of
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

        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: false, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, registry, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out _, cancellationToken) && decision.Verdict is ModuleVerdict verdict)
        {
            return verdict;
        }

        return AlcModuleReasoner.DecideConsistency(module, registry, cancellationToken);
    }

    /// <summary>
    /// Decides the module's fragment for consistency only as a full
    /// <see cref="ModuleDecision"/>, bounding an admitted module's saturation by
    /// <paramref name="budget"/> — the consistency-only counterpart of
    /// <see cref="DecideModule(ReasoningModule, ReasoningBudget, SaturationProgressSampler, CancellationToken)"/>: no
    /// query context is created, so the trivial context alone carries the verdict.
    /// An admitted module whose saturation exhausts the budget abstains with a
    /// reason — the abstention is the answer, not a delegation. A non-admitted
    /// module delegates to the snapshot tableau, bounded by the same budget: the
    /// fallback leg may spend up to the bound again on its own consistency check
    /// (a per-leg budget), abstaining when it exhausts it.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation and the delegated snapshot tableau alike.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision, with an empty subsumption list on a context-decided module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideConsistencyModule(ReasoningModule module, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, budget, includeSubsumptions: false, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideConsistencyModule(module, budget, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module's fragment for consistency only as a full
    /// <see cref="ModuleDecision"/> consulting a registered-datatype set at the datatype sidecar,
    /// bounding an admitted module's saturation by <paramref name="budget"/> — the registry-carrying
    /// counterpart of
    /// <see cref="DecideConsistencyModule(ReasoningModule, ReasoningBudget, CancellationToken)"/>. An
    /// admitted module whose saturation exhausts the budget abstains with a reason; a non-admitted
    /// module delegates to the registry-consulting snapshot tableau bounded by the same budget, which
    /// may spend up to the bound again on its own consistency check (a per-leg budget).
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation and the delegated snapshot tableau alike.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision, with an empty subsumption list on a context-decided module.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideConsistencyModule(ReasoningModule module, DatatypeRegistry registry, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if(TryDecideContext(module, budget, includeSubsumptions: false, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, registry, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideConsistencyModule(module, registry, budget, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> — the verdict
    /// together with the work it spent — the form the
    /// <see cref="DescriptionLogicDelegate"/> seam returns. Under an unbounded
    /// budget. A context decision carries the saturation telemetry and empty
    /// solver/tableau totals; a delegated one carries the snapshot tableau's own
    /// decision whole.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, ReasoningBudget.Unbounded, progressSampler: null, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/>, bounding an
    /// admitted module's saturation by <paramref name="budget"/>. An admitted
    /// module whose saturation exhausts the budget abstains with a reason — the
    /// abstention is the answer, not a delegation.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="progressSampler">The optional in-saturation progress sampler receiving one mark at every power-of-two inference attempt; it is attached to each round's engine before the creation seeding runs, so the seeding's own attempts are observable. <see langword="null"/> is the zero-cost default. This is the only public route that carries a sampler into the engine, and it always runs without a measurement probe.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, ReasoningBudget budget, SaturationProgressSampler? progressSampler = null, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, budget, includeSubsumptions: true, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe: null, progressSampler, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideModule(module, budget, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under a selected
    /// clausifier equality lowering, under an unbounded budget — the measurement
    /// entry that runs a module through the successor-sharing V-node lowering
    /// beside the default general clause. The two lowerings are verdict- and
    /// subsumption-identical; the selection changes only the saturation work, not
    /// the answer. Internal: the equality lowering is a measured clausifier knob
    /// behind the production defaults.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="lowering">The clausifier's functionality lowering.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, EqualityLowering lowering, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: true, lowering, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under a selected
    /// central-variable-versus-individual paramodulation scope, under an unbounded
    /// budget — the measurement entry that runs a module through the reference
    /// unrestricted mode beside the default query-scoped one. The two scopes are
    /// verdict- and subsumption-identical for the consistency and named-class
    /// subsumption read-off surfaces; the selection changes only the equated-nominal
    /// clause traffic, not the answer. Internal: the paramodulation scope is a
    /// measured engine knob behind the production defaults.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="paramodulationScope">The engine's central-variable-versus-individual paramodulation scope.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, NominalParamodulationScope paramodulationScope, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, paramodulationScope, RootContextTopology.SingleRoot, RootPropagationRelevance.Unrestricted, ReasoningBudget.Unbounded, engineProbe: null, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under a selected
    /// r-Pred ground-relevance mode AND an explicit inference budget — the
    /// measurement entry that runs a module through the filtered root propagation
    /// beside the unrestricted default at any ceiling: no other entry point
    /// carries both the mode and the budget, and a mode-only overload's unbounded
    /// default could never drive the bounded corpus funnels. The two modes are
    /// verdict- and subsumption-identical for both read-off surfaces; the
    /// selection changes only the root-propagation clause traffic, not the
    /// answer. Internal: the relevance mode is a measured engine knob behind
    /// the production defaults.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="propagationRelevance">The engine's r-Pred ground-relevance mode.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, RootPropagationRelevance propagationRelevance, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, RootContextTopology.SingleRoot, propagationRelevance, budget, engineProbe: null, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under a selected
    /// root-tier topology, a selected r-Pred ground-relevance mode, AND an
    /// explicit inference budget — the measurement entry that runs a module
    /// through the fragmented per-individual topology beside the single-root
    /// default at any ceiling. The topologies are verdict- and
    /// subsumption-identical for both read-off surfaces (H-RF-1); the selection
    /// changes only where the root-tier inferences run, not the answer. The
    /// fragmented topology composes with the unrestricted relevance mode only —
    /// the engine rejects the filtered composition at creation. Internal: the
    /// topology is a measured engine knob behind the production defaults.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="topology">The engine's root-tier topology.</param>
    /// <param name="propagationRelevance">The engine's r-Pred ground-relevance mode.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="engineProbe">The optional measurement probe receiving each constructed saturation engine before seeding; <see langword="null"/> outside a measurement run.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fragmented topology is combined with the ground-filtered relevance mode.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, RootContextTopology topology, RootPropagationRelevance propagationRelevance, ReasoningBudget budget, SaturationEngineProbeDelegate? engineProbe = null, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, NominalParamodulationScope.QueryScoped, topology, propagationRelevance, budget, engineProbe, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under every
    /// measured engine axis at once — the paramodulation scope, the root-tier
    /// topology, the r-Pred ground-relevance mode, an explicit inference budget,
    /// and the optional engine probe — the full-matrix measurement entry the
    /// license-scoped instruments drive: no narrower overload carries the scope
    /// beside the topology and the budget, and the scope-only overload's
    /// unbounded default could never drive the bounded corpus funnels. Every
    /// production caller stays on the query-scoped default through the narrower
    /// overloads, which all route here under the production decider faces.
    /// Internal: the axes are measured engine knobs behind the production
    /// defaults.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="paramodulationScope">The engine's central-variable-versus-individual paramodulation scope.</param>
    /// <param name="topology">The engine's root-tier topology.</param>
    /// <param name="propagationRelevance">The engine's r-Pred ground-relevance mode.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="engineProbe">The optional measurement probe receiving each constructed saturation engine before seeding; <see langword="null"/> outside a measurement run.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fragmented topology is combined with the ground-filtered relevance mode.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, NominalParamodulationScope paramodulationScope, RootContextTopology topology, RootPropagationRelevance propagationRelevance, ReasoningBudget budget, SaturationEngineProbeDelegate? engineProbe = null, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, ContextEnumerationDecider, paramodulationScope, topology, propagationRelevance, budget, engineProbe, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> with a
    /// selected enumeration-CSP decider face set AND an explicit inference
    /// budget — the decider battery's narrow measurement entry. A lit face
    /// decides its habitat pre-engine with zero inference attempts;
    /// <see cref="EnumerationDeciderFaces.None"/> is the explicit dark
    /// control, engine-identical on every module. Internal: the faces are
    /// measured knobs behind the production default.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="deciderFaces">The enumeration-CSP decider's face selection.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, EnumerationDeciderFaces deciderFaces, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, deciderFaces, NominalParamodulationScope.QueryScoped, RootContextTopology.SingleRoot, budget, cancellationToken);
    }

    /// <summary>
    /// Decides the module with a selected enumeration-CSP decider face set
    /// under a selected paramodulation scope AND root-tier topology — the
    /// P-S1 measurement entry: a pre-engine face decision never constructs an
    /// engine, so the scope and topology cannot move it, and the battery pins
    /// that upstream-of-every-engine-axis seat across the matrix. Internal:
    /// measured knobs only.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="deciderFaces">The enumeration-CSP decider's face selection.</param>
    /// <param name="paramodulationScope">The engine's paramodulation scope, reached only when the faces stay silent.</param>
    /// <param name="topology">The engine's root-tier topology, reached only when the faces stay silent.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, EnumerationDeciderFaces deciderFaces, NominalParamodulationScope paramodulationScope, RootContextTopology topology, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, deciderFaces, paramodulationScope, topology, RootPropagationRelevance.Unrestricted, budget, engineProbe: null, cancellationToken);
    }

    /// <summary>
    /// Decides the module with a selected enumeration-CSP decider face set
    /// under every measured engine axis at once — the one trunk every
    /// face-or-matrix overload routes through: the production overloads pass
    /// the production faces, the battery and the engine instruments pass an
    /// explicit selection, and an instrument that must reach the engine on a
    /// decider-decidable module drives <see cref="EnumerationDeciderFaces.None"/>
    /// beside its axes and probe. Internal: measured knobs only.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="deciderFaces">The enumeration-CSP decider's face selection, evaluated ahead of any engine axis.</param>
    /// <param name="paramodulationScope">The engine's paramodulation scope, reached only when the faces stay silent.</param>
    /// <param name="topology">The engine's root-tier topology, reached only when the faces stay silent.</param>
    /// <param name="propagationRelevance">The engine's r-Pred ground-relevance mode, reached only when the faces stay silent.</param>
    /// <param name="budget">The inference budget bounding an admitted module's saturation.</param>
    /// <param name="engineProbe">The optional measurement probe receiving each constructed saturation engine before seeding; <see langword="null"/> outside a measurement run.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fragmented topology is combined with the ground-filtered relevance mode.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModule(ReasoningModule module, EnumerationDeciderFaces deciderFaces, NominalParamodulationScope paramodulationScope, RootContextTopology topology, RootPropagationRelevance propagationRelevance, ReasoningBudget budget, SaturationEngineProbeDelegate? engineProbe = null, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, budget, includeSubsumptions: true, EqualityLowering.GeneralClause, paramodulationScope, propagationRelevance, topology, deciderFaces, ContextRootKeyJoin, ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideModule(module, budget, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> with the vr
    /// key-join lift armed — the
    /// battery entry that forces the key-join switch ON and takes the
    /// data-obligation arm at its production default, so a HasKey+nominal module
    /// routes past the <c>KeyOnNominalModule</c> guard into intake and the root key
    /// join decides it. Internal: the entry the key-join battery names.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModuleWithRootKeyJoin(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: true, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, rootKeyJoinEnabled: true, rootDataObligationsEnabled: ContextRootDataObligations, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> with the per-constant
    /// root data-obligation lift armed —
    /// the battery entry that forces the data-obligation switch ON and takes the vr
    /// key-join arm at its production default, so a data demand landing at a constant on
    /// a root context is decided PER ≈-CLASS off the pooled read-time union. Internal:
    /// the entry the data-arm battery names.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModuleWithRootDataObligations(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: true, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, rootKeyJoinEnabled: ContextRootKeyJoin, rootDataObligationsEnabled: true, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> with BOTH root-tier
    /// lifts armed (the co-fire entry) — the vr key join and the per-constant root
    /// data obligations run
    /// on the SAME root-tier substrate, so a key-join-fired <c>⊤ → o ≈ o′</c>
    /// union triggers the lift-2 re-probe on the shared merged class. The COF-1 battery
    /// row reads it: a merge whose pooled demands are jointly unsatisfiable post-merge
    /// decides INCONSISTENT at the post-merge fixpoint. Internal: the entry the co-fire
    /// battery names.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the decision.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static ModuleDecision DecideModuleWithRootLifts(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        if(TryDecideContext(module, ReasoningBudget.Unbounded, includeSubsumptions: true, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, rootKeyJoinEnabled: true, rootDataObligationsEnabled: true, DatatypeRegistry.Empty, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
        {
            return decision;
        }

        return DelegateWithLatchTotals(AlcModuleReasoner.DecideModule(module, ReasoningBudget.Unbounded, cancellationToken), delegatedTotals);
    }

    /// <summary>
    /// Tries to decide the module by context saturation. Succeeds only when the
    /// survey admits the module (<see cref="ContextModuleSurvey"/>) and the second
    /// gate finds the clausification within the engine's clause grammar — the
    /// belt-and-suspenders guard against any survey/clausifier drift. On success
    /// the decision is either the reached verdict or, when the inference budget
    /// ran out first, a budget abstention.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The inference budget bounding the saturation.</param>
    /// <param name="includeSubsumptions">Whether to create query contexts and enumerate module-local subsumptions when the signature qualifies.</param>
    /// <param name="lowering">The clausifier's functionality lowering: the published general clause (the default, verdict-identical) or the successor-sharing V-node reuse.</param>
    /// <param name="paramodulationScope">The engine's central-variable-versus-individual paramodulation scope: the query-scoped default or the reference unrestricted mode (verdict-identical, a measured knob).</param>
    /// <param name="propagationRelevance">The engine's r-Pred ground-relevance mode: the unrestricted default or the ground-filtered mode (verdict-identical, a measured knob).</param>
    /// <param name="topology">The engine's root-tier topology: the single-root default or the fragmented per-individual layout (verdict-identical, a measured knob).</param>
    /// <param name="deciderFaces">The enumeration-CSP decider's face selection: the production default lights every pre-engine face; <see cref="EnumerationDeciderFaces.None"/> is the explicit dark control the decider battery drives, engine-identical on every module.</param>
    /// <param name="rootKeyJoinEnabled">The vr key-join lift's switch: the production default is ON and every reasoner caller threads it on (a HasKey+nominal module routes past the <c>KeyOnNominalModule</c> guard into intake and arms the root key join); off whole-rejects such a module at the guard — the pre-lift dark face, exercised through the clausifier and survey overloads' explicit false, never through this reasoner path.</param>
    /// <param name="rootDataObligationsEnabled">The per-constant root data-obligation lift's switch: the production default is ON and every reasoner caller threads it on (a root demand is decided per ≈-class off the pooled read-time union, and a non-narrowing disjunctive root marker delegates named through <c>DataObligationUndecidedOnRoot</c>); off, a landed root demand records only the <c>RootDataDemandObserved</c> census statistic — the dark face, exercised through the engine's explicit false.</param>
    /// <param name="registry">The registered-datatype set the sidecar consults.</param>
    /// <param name="engineProbe">The optional measurement probe receiving each constructed saturation engine before seeding — invoked once per merge round; <see langword="null"/> outside a measurement run.</param>
    /// <param name="progressSampler">The optional in-saturation progress sampler, handed to <see cref="ContextSaturationEngine.Create(ClausificationResult, DatatypeRegistry, NominalParamodulationScope, RootPropagationRelevance, RootContextTopology, SaturationProgressSampler?)"/> so it is attached before the creation seeding runs; <see langword="null"/> outside a sampled run.</param>
    /// <param name="decision">The context decision when the engine took the module; otherwise <see langword="null"/>.</param>
    /// <param name="delegatedContextTotals">The context totals a delegated module carries to the fallback's decision when the off-fold equality backstop latched: a totals record whose only non-zero field is <see cref="ContextSaturationStatistics.RootEqualityOutsideFoldHeads"/>, for the corpus census the backstop delegation would otherwise leave unmeasured; <see langword="null"/> on a context decision and on every delegation the backstop did not drive, where the fallback's own totals stand unchanged.</param>
    /// <param name="cancellationToken">A token that aborts saturation.</param>
    /// <returns><see langword="true"/> when the context engine decided or abstained on the module; <see langword="false"/> when it must delegate.</returns>
    private static bool TryDecideContext(
        ReasoningModule module,
        ReasoningBudget budget,
        bool includeSubsumptions,
        EqualityLowering lowering,
        NominalParamodulationScope paramodulationScope,
        RootPropagationRelevance propagationRelevance,
        RootContextTopology topology,
        EnumerationDeciderFaces deciderFaces,
        bool rootKeyJoinEnabled,
        bool rootDataObligationsEnabled,
        DatatypeRegistry registry,
        SaturationEngineProbeDelegate? engineProbe,
        SaturationProgressSampler? progressSampler,
        [NotNullWhen(true)] out ModuleDecision? decision,
        out ContextSaturationStatistics? delegatedContextTotals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        decision = null;
        delegatedContextTotals = null;

        //Fold the reserved-vocabulary constant shapes once at the front door: a
        //restriction whose fixed-extension reserved property makes it
        //semantically owl:Thing or owl:Nothing becomes that constant, so the
        //survey, scan, belt, clausifier, and engine downstream see a plain
        //named-class reference rather than a reserved role they would reject.
        module = ReservedVocabularyFold.Apply(module);

        ContextModuleSurveyResult survey = ContextModuleSurvey.Survey(module, rootKeyJoinEnabled);
        if(!survey.Admitted)
        {
            return false;
        }

        //The certifying face, the pair-composition faces, and the Shape E census
        //measurement sit ahead of clausification entirely: the faces need only
        //the module's told axiom surfaces, and a decided module never constructs
        //an engine — zero inference attempts, the rider's discipline. Dark, only
        //the structural counts are measured, so every decision stays
        //byte-identical. Each face gates its own verdict direction, and the
        //deciding mechanism — the block sweep inside the member window, the
        //pair-composition sweep past it — chooses which face must be lit.
        EnumerationCensusMeasurement enumerationMeasurement = default;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.EnumerationAlgebra)
        {
            const EnumerationDeciderFaces algebraFaces = EnumerationDeciderFaces.Certifying | EnumerationDeciderFaces.EnumerationPairClash | EnumerationDeciderFaces.EnumerationPairCertify;
            EnumerationAlgebraOutcome algebraOutcome = (deciderFaces & algebraFaces) != 0
                ? ContextEnumerationAlgebraDecider.Run(module, includeSubsumptions)
                : ContextEnumerationAlgebraDecider.Measure(module);
            enumerationMeasurement = new EnumerationCensusMeasurement(
                algebraOutcome.MemberUniverse,
                algebraOutcome.MemberSilences,
                algebraOutcome.ClassSilences,
                algebraOutcome.PairCount,
                algebraOutcome.PairVectorCount,
                algebraOutcome.PairSilences);
            if(algebraOutcome.Verdict != EnumerationAlgebraVerdict.Silent && (deciderFaces & DecidingAlgebraFace(algebraOutcome)) != 0)
            {
                ContextSaturationStatistics certifyingStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    EnumerationDeciderCertifications = algebraOutcome.Verdict == EnumerationAlgebraVerdict.Consistent ? 1 : 0,
                    EnumerationDeciderRefutations = algebraOutcome.Verdict == EnumerationAlgebraVerdict.Inconsistent ? 1 : 0,
                    EnumerationMemberUniverse = algebraOutcome.MemberUniverse,
                    EnumerationPairCount = algebraOutcome.PairCount,
                    EnumerationPairVectorCount = algebraOutcome.PairVectorCount,
                };
                ModuleVerdict certifyingVerdict = algebraOutcome.Verdict == EnumerationAlgebraVerdict.Consistent
                    ? new ModuleVerdict(true, algebraOutcome.Subsumptions)
                    : new ModuleVerdict(false, []);
                decision = ModuleDecision.Decided(certifyingVerdict, ContextDecisionStatistics(module.Axioms.Count, certifyingStatistics));

                return true;
            }
        }

        //The partition-counting faces sit ahead of clausification for the same
        //reason: the closed form reads told axiom surfaces only, and a decided
        //module constructs no engine — zero inference attempts, zero
        //clausification. The measurement runs whether the faces are lit or dark,
        //so the census numbers are identical on both, and each face gates its
        //own verdict direction: the clash face answers only m > k, the certify
        //face only m <= k. Silence falls through carrying the measurement.
        PartitionCountingWindow partitionWindow = PartitionCountingWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.PartitionCounting)
        {
            const EnumerationDeciderFaces partitionFaces = EnumerationDeciderFaces.PartitionClash | EnumerationDeciderFaces.PartitionCertify;
            PartitionCountingOutcome partitionOutcome = (deciderFaces & partitionFaces) != 0
                ? ContextPartitionCountingDecider.Run(module)
                : ContextPartitionCountingDecider.Measure(module);
            partitionWindow = partitionOutcome.Window;
            if(partitionOutcome.Consistent is bool partitionConsistent
                && (deciderFaces & (partitionConsistent ? EnumerationDeciderFaces.PartitionCertify : EnumerationDeciderFaces.PartitionClash)) != 0)
            {
                ContextSaturationStatistics partitionStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    PartitionDeciderCertifications = partitionConsistent ? 1 : 0,
                    PartitionDeciderClashes = partitionConsistent ? 0 : 1,
                    PartitionAnchorCount = partitionWindow.AnchorCount,
                    PartitionRestrictionCount = partitionWindow.RestrictionCount,
                    PartitionCapBound = partitionWindow.CapBound,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(partitionConsistent, []), ContextDecisionStatistics(module.Axioms.Count, partitionStatistics));

                return true;
            }
        }

        //The gadget faces sit ahead of clausification for the same reason: the
        //propositional compilation reads told axiom surfaces only, and a decided
        //module constructs no engine — zero inference attempts, zero
        //clausification. The measurement runs whether the faces are lit or dark,
        //so the census numbers are identical on both, and each face gates its own
        //verdict direction: the clash face answers only an exhausted assignment
        //space, the certify face only a passing assignment. Silence falls through
        //carrying the measurement.
        BooleanGadgetWindow gadgetWindow = BooleanGadgetWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.BooleanCardinalityGadget)
        {
            const EnumerationDeciderFaces gadgetFaces = EnumerationDeciderFaces.GadgetClash | EnumerationDeciderFaces.GadgetCertify;
            BooleanGadgetOutcome gadgetOutcome = (deciderFaces & gadgetFaces) != 0
                ? ContextBooleanGadgetDecider.Run(module)
                : ContextBooleanGadgetDecider.Measure(module);
            gadgetWindow = gadgetOutcome.Window;
            if(gadgetOutcome.Consistent is bool gadgetConsistent
                && (deciderFaces & (gadgetConsistent ? EnumerationDeciderFaces.GadgetCertify : EnumerationDeciderFaces.GadgetClash)) != 0)
            {
                ContextSaturationStatistics gadgetStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    GadgetDeciderCertifications = gadgetConsistent ? 1 : 0,
                    GadgetDeciderClashes = gadgetConsistent ? 0 : 1,
                    GadgetPropertyAtomCount = gadgetWindow.PropertyAtomCount,
                    GadgetFreeClassAtomCount = gadgetWindow.FreeClassAtomCount,
                    GadgetEvaluatedVectorCount = gadgetWindow.EvaluatedVectorCount,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(gadgetConsistent, []), ContextDecisionStatistics(module.Axioms.Count, gadgetStatistics));

                return true;
            }
        }

        //The spy-point domain-bound face sits ahead of clausification for the same
        //reason: the closed form reads told axiom surfaces only, and a decided
        //module constructs no engine — zero inference attempts, zero
        //clausification. The measurement runs whether the face is lit or dark, so
        //the census numbers are identical on both. The face carries ONE verdict
        //direction: a told demand outrunning the told domain bound refutes the
        //module, and every other reading — a demand inside the bound included — is
        //silence falling through carrying the measurement, because the bound
        //certifies nothing about the surrounding module.
        SpyPointWindow spyPointWindow = SpyPointWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.SpyPointDomainBound)
        {
            SpyPointOutcome spyPointOutcome = (deciderFaces & EnumerationDeciderFaces.SpyPointClash) != 0
                ? ContextSpyPointDecider.Run(module)
                : ContextSpyPointDecider.Measure(module);
            spyPointWindow = spyPointOutcome.Window;
            if(spyPointOutcome.Consistent is false && (deciderFaces & EnumerationDeciderFaces.SpyPointClash) != 0)
            {
                ContextSaturationStatistics spyPointStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    SpyPointDeciderClashes = 1,
                    SpyPointMemberCount = spyPointWindow.MemberCount,
                    SpyPointCapBound = spyPointWindow.CapBound,
                    SpyPointDemandBound = spyPointWindow.DemandBound,
                    SpyPointWindowExceededMembers = spyPointWindow.MemberSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, spyPointStatistics));

                return true;
            }
        }

        //The bijection-chain faces sit ahead of clausification for the same
        //reason: the told-size propagation and the two certificate routes read
        //told axiom surfaces only, and a decided module constructs no engine —
        //zero inference attempts, zero clausification. The measurement runs
        //whether the faces are lit or dark, so the census numbers are identical
        //on both, and each face gates its own verdict direction under its own
        //jurisdiction: the clash face answers a propagation that reached an
        //impossible state over a recognized subset, the certify face only a
        //whole module exactly one certificate route validates. Silence falls
        //through carrying the measurement.
        BijectionChainWindow bijectionChainWindow = BijectionChainWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.BijectionChainArithmetic)
        {
            const EnumerationDeciderFaces bijectionChainFaces = EnumerationDeciderFaces.BijectionChainClash | EnumerationDeciderFaces.BijectionChainCertify;
            BijectionChainOutcome bijectionChainOutcome = (deciderFaces & bijectionChainFaces) != 0
                ? ContextBijectionChainDecider.Run(module)
                : ContextBijectionChainDecider.Measure(module);
            bijectionChainWindow = bijectionChainOutcome.Window;
            if(bijectionChainOutcome.Consistent is false && (deciderFaces & EnumerationDeciderFaces.BijectionChainClash) != 0)
            {
                ContextSaturationStatistics bijectionChainClashStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    BijectionChainDeciderClashes = 1,
                    BijectionChainClassCount = bijectionChainWindow.ClassCount,
                    BijectionChainConstraintCount = bijectionChainWindow.ConstraintCount,
                    BijectionChainWindowExceededClasses = bijectionChainWindow.ClassSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, bijectionChainClashStatistics));

                return true;
            }

            if(bijectionChainOutcome.Consistent is true && (deciderFaces & EnumerationDeciderFaces.BijectionChainCertify) != 0)
            {
                ContextSaturationStatistics bijectionChainCertifyStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    BijectionChainDeciderCertifications = 1,
                    BijectionChainClassCount = bijectionChainWindow.ClassCount,
                    BijectionChainConstraintCount = bijectionChainWindow.ConstraintCount,
                    BijectionChainWindowExceededClasses = bijectionChainWindow.ClassSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(true, []), ContextDecisionStatistics(module.Axioms.Count, bijectionChainCertifyStatistics));

                return true;
            }
        }

        //The told-ground-witness faces sit ahead of clausification for the same
        //reason: the ground-membership derivation and the described-model
        //construction read told axiom surfaces only, and a decided module
        //constructs no engine — zero inference attempts, zero clausification.
        //The measurement runs whether the faces are lit or dark, so the census
        //numbers are identical on both, and each face gates its own verdict
        //direction under its own jurisdiction: the clash face answers a ground
        //derivation that met its own denial over a recognized subset, the
        //certify face only a whole module whose described model satisfies every
        //axiom on re-check. Silence falls through carrying the measurement.
        ToldGroundWitnessWindow toldGroundWitnessWindow = ToldGroundWitnessWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.ToldGroundWitness)
        {
            const EnumerationDeciderFaces toldGroundWitnessFaces = EnumerationDeciderFaces.ToldGroundWitnessClash | EnumerationDeciderFaces.ToldGroundWitnessCertify;
            ToldGroundWitnessOutcome toldGroundWitnessOutcome = (deciderFaces & toldGroundWitnessFaces) != 0
                ? ContextToldGroundWitnessDecider.Run(module)
                : ContextToldGroundWitnessDecider.Measure(module);
            toldGroundWitnessWindow = toldGroundWitnessOutcome.Window;
            if(toldGroundWitnessOutcome.Consistent is false && (deciderFaces & EnumerationDeciderFaces.ToldGroundWitnessClash) != 0)
            {
                ContextSaturationStatistics toldGroundWitnessClashStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    ToldGroundWitnessDeciderClashes = 1,
                    ToldGroundWitnessCarrierCount = toldGroundWitnessWindow.CarrierCount,
                    ToldGroundWitnessEdgeCount = toldGroundWitnessWindow.EdgeCount,
                    ToldGroundWitnessWindowExceededCarriers = toldGroundWitnessWindow.WindowSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, toldGroundWitnessClashStatistics));

                return true;
            }

            if(toldGroundWitnessOutcome.Consistent is true && (deciderFaces & EnumerationDeciderFaces.ToldGroundWitnessCertify) != 0)
            {
                ContextSaturationStatistics toldGroundWitnessCertifyStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    ToldGroundWitnessDeciderCertifications = 1,
                    ToldGroundWitnessCarrierCount = toldGroundWitnessWindow.CarrierCount,
                    ToldGroundWitnessEdgeCount = toldGroundWitnessWindow.EdgeCount,
                    ToldGroundWitnessWindowExceededCarriers = toldGroundWitnessWindow.WindowSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(true, []), ContextDecisionStatistics(module.Axioms.Count, toldGroundWitnessCertifyStatistics));

                return true;
            }
        }

        //The repairing faces sit ahead of clausification for the same reason:
        //the monotone told-only clash pass and the repair-construct-then-verify
        //certificate read told axiom surfaces only, and a decided module
        //constructs no engine — zero inference attempts, zero clausification.
        //The measurement runs whether the faces are lit or dark, so the census
        //numbers are identical on both, and each face gates its own verdict
        //direction under its own jurisdiction: the clash face answers a told
        //ground derivation that met its own denial over a recognized subset, the
        //certify face only a whole module a repaired candidate model satisfies
        //on re-check. The certify face never answers inconsistent, so no
        //refutation direction rides this block. Silence falls through carrying
        //the measurement.
        RepairingWindow repairingWindow = RepairingWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.RestrictionRichGround)
        {
            const EnumerationDeciderFaces repairingFaces = EnumerationDeciderFaces.RepairingGroundClash | EnumerationDeciderFaces.RepairingCertify;
            RepairingOutcome repairingOutcome = (deciderFaces & repairingFaces) != 0
                ? ContextRepairingCertifyDecider.Run(module)
                : ContextRepairingCertifyDecider.Measure(module);
            repairingWindow = repairingOutcome.Window;
            if(repairingOutcome.Consistent is false && (deciderFaces & EnumerationDeciderFaces.RepairingGroundClash) != 0)
            {
                ContextSaturationStatistics repairingClashStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    RepairingDeciderClashes = 1,
                    RepairingCarrierCount = repairingWindow.CarrierCount,
                    RepairingCommittedEdgeCount = repairingWindow.CommittedEdges,
                    RepairingWindowExceededCarriers = repairingWindow.WindowSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, repairingClashStatistics));

                return true;
            }

            if(repairingOutcome.Consistent is true && (deciderFaces & EnumerationDeciderFaces.RepairingCertify) != 0)
            {
                ContextSaturationStatistics repairingCertifyStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    RepairingDeciderCertifications = 1,
                    RepairingCarrierCount = repairingWindow.CarrierCount,
                    RepairingCommittedEdgeCount = repairingWindow.CommittedEdges,
                    RepairingWindowExceededCarriers = repairingWindow.WindowSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(true, []), ContextDecisionStatistics(module.Axioms.Count, repairingCertifyStatistics));

                return true;
            }
        }

        //The modal role-expansion face sits ahead of clausification for the same
        //reason: the bounded skolem expansion reads told axiom surfaces only, and
        //a decided module constructs no engine — zero inference attempts, zero
        //clausification. The measurement runs whether the face is lit or dark,
        //so the census numbers are identical on both — the dark pass compares the
        //window ceilings and expands nothing. The face carries ONE verdict
        //direction: an expansion that reaches a node-local numeric contradiction
        //or an asserted empty class refutes the module, and every other reading —
        //a clash-free fixpoint, a bound trip, an inadmissible axiom and a missing
        //engagement signal alike — is silence falling through carrying the
        //measurement, because a bounded expansion that found no clash certifies
        //nothing about the module it never finished building.
        ModalExpansionWindow modalExpansionWindow = ModalExpansionWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.ModalRoleExpansion)
        {
            ModalExpansionOutcome modalExpansionOutcome = (deciderFaces & EnumerationDeciderFaces.ModalExpansionClash) != 0
                ? ContextModalRoleExpansionDecider.Run(module)
                : ContextModalRoleExpansionDecider.Measure(module);
            modalExpansionWindow = modalExpansionOutcome.Window;
            if(modalExpansionOutcome.Consistent is false && (deciderFaces & EnumerationDeciderFaces.ModalExpansionClash) != 0)
            {
                ContextSaturationStatistics modalExpansionStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    ModalExpansionDeciderClashes = 1,
                    ModalExpansionNodesSpawned = modalExpansionWindow.NodesSpawned,
                    ModalExpansionMaxDepthReached = modalExpansionWindow.MaxDepthReached,
                    ModalExpansionPeakLabelSize = modalExpansionWindow.PeakLabelSize,
                    ModalExpansionEdgesMaterialised = modalExpansionWindow.EdgesMaterialised,
                    ModalExpansionRuleApplications = modalExpansionWindow.RuleApplications,
                    ModalExpansionWindowSilences = modalExpansionWindow.WindowSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, modalExpansionStatistics));

                return true;
            }
        }

        //The two modal-gadget faces sit ahead of clausification for the same
        //reason: both read told axiom surfaces only, and a decided module
        //constructs no engine — zero inference attempts, zero clausification. The
        //measurement runs whether the faces are lit or dark, so the census numbers
        //ride on both — the dark pass compares the window ceilings and neither
        //composes nor constructs anything.
        //The two faces carry OPPOSITE directions and opposite jurisdictions and
        //are entered CLASH FIRST: a monotone composition closure reaching a
        //complemented membership or a bottom refutes the module, and a minted
        //skolem tree that satisfies every admitted axiom on re-check certifies it.
        //A DECIDED CLASH SUPPRESSES THE CERTIFY ENTRY ENTIRELY — the certify face
        //is entered only where the clash face returned silence, so the two faces'
        //verdicts are never both read on one module and no write order can decide
        //which one is reported. Silence on both falls through carrying the merged
        //measurement, in which the clash face contributes its own window silences
        //and nothing else, and no field either face leaves alone is clobbered.
        ModalGadgetWindow modalGadgetWindow = ModalGadgetWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.ModalGadgetTree)
        {
            const EnumerationDeciderFaces modalGadgetFaces = EnumerationDeciderFaces.ModalGadgetClash | EnumerationDeciderFaces.ModalGadgetCertify;
            if((deciderFaces & modalGadgetFaces) == 0)
            {
                modalGadgetWindow = ContextModalGadgetTreeDecider.Measure(module);
            }
            else
            {
                ModalGadgetClashOutcome modalGadgetClash = ContextModalGadgetTreeDecider.RunClash(module);
                modalGadgetWindow = MergesModalGadgetWindow(modalGadgetWindow, modalGadgetClash.Window);
                if(modalGadgetClash.Consistent is false && (deciderFaces & EnumerationDeciderFaces.ModalGadgetClash) != 0)
                {
                    ContextSaturationStatistics modalGadgetClashStatistics = NominalClashStatistics() with
                    {
                        EnumerationHabitat = survey.EnumerationHabitat,
                        NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                        ModalGadgetDeciderClashes = 1,
                        ModalGadgetFreeAtomCount = modalGadgetWindow.FreeAtomCount,
                        ModalGadgetSignatureCount = modalGadgetWindow.SignatureCount,
                        ModalGadgetNodesBuilt = modalGadgetWindow.NodesBuilt,
                        ModalGadgetWindowSilences = modalGadgetWindow.WindowSilences,
                    };
                    decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, modalGadgetClashStatistics));

                    return true;
                }

                ModalGadgetCertifyOutcome modalGadgetCertify = ContextModalGadgetTreeDecider.RunCertify(module);
                modalGadgetWindow = MergesModalGadgetWindow(modalGadgetWindow, modalGadgetCertify.Window);
                if(modalGadgetCertify.Consistent is true && (deciderFaces & EnumerationDeciderFaces.ModalGadgetCertify) != 0)
                {
                    ContextSaturationStatistics modalGadgetCertifyStatistics = NominalClashStatistics() with
                    {
                        EnumerationHabitat = survey.EnumerationHabitat,
                        NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                        ModalGadgetDeciderCertifications = 1,
                        ModalGadgetFreeAtomCount = modalGadgetWindow.FreeAtomCount,
                        ModalGadgetSignatureCount = modalGadgetWindow.SignatureCount,
                        ModalGadgetNodesBuilt = modalGadgetWindow.NodesBuilt,
                        ModalGadgetWindowSilences = modalGadgetWindow.WindowSilences,
                    };
                    decision = ModuleDecision.Decided(new ModuleVerdict(true, []), ContextDecisionStatistics(module.Axioms.Count, modalGadgetCertifyStatistics));

                    return true;
                }
            }
        }

        //The nominal-pinned-role face sits ahead of clausification for the same
        //reason: the closed form reads told axiom surfaces only, and a decided
        //module constructs no engine — zero inference attempts, zero
        //clausification. The measurement runs whether the face is lit or dark, so
        //the census numbers are identical on both. The face carries ONE verdict
        //direction: a told edge whose exact reverse a told denial excludes, under
        //a role whose told inverse-functionality and total told self-loops pin
        //its extension into the identity diagonal, refutes the module, and every
        //other reading — the pinned premise without a denied edge included — is
        //silence falling through carrying the measurement, because a pinned
        //extension certifies nothing about the surrounding module.
        NominalPinnedRoleWindow nominalPinnedRoleWindow = NominalPinnedRoleWindow.Empty;
        if(survey.EnumerationHabitat == EnumerationHabitatClass.NominalPinnedRole)
        {
            NominalPinnedRoleOutcome nominalPinnedRoleOutcome = (deciderFaces & EnumerationDeciderFaces.NominalPinnedRoleClash) != 0
                ? ContextNominalPinnedRoleDecider.Run(module)
                : ContextNominalPinnedRoleDecider.Measure(module);
            nominalPinnedRoleWindow = nominalPinnedRoleOutcome.Window;
            if(nominalPinnedRoleOutcome.Consistent is false && (deciderFaces & EnumerationDeciderFaces.NominalPinnedRoleClash) != 0)
            {
                ContextSaturationStatistics nominalPinnedRoleStatistics = NominalClashStatistics() with
                {
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    NominalPinnedRoleDeciderClashes = 1,
                    NominalPinnedRoleMemberCount = nominalPinnedRoleWindow.MemberCount,
                    NominalPinnedRolePinnedEdgeCount = nominalPinnedRoleWindow.PinnedEdgeCount,
                    NominalPinnedRoleDeniedEdgeCount = nominalPinnedRoleWindow.DeniedEdgeCount,
                    NominalPinnedRoleWindowExceededMembers = nominalPinnedRoleWindow.MemberSilences,
                };
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, nominalPinnedRoleStatistics));

                return true;
            }
        }

        //The derived-merge fixpoint: each round
        //re-clausifies from scratch with the accumulated key-forced unions seeded,
        //saturates, and re-joins over derived-certain memberships; a round that
        //fires a union hands its pairs to the next round. Unions strictly decrease
        //the distinct-representative count, so the loop is bounded (H-HK-2), and
        //the budget accumulates by SUMMING each completed round's exposed
        //statistics against the UNMODIFIED ceiling — never by shrinking the
        //immutable budget, whose zero bound reads as unbounded. Round attempt
        //totals of clash- or abstention-terminated cascades
        //carry forward into the assembled statistics rather than resetting.
        List<(Utf8String First, Utf8String Second)> seeds = [];
        long priorAttempts = 0;

        //The population axis accumulates across rounds the same way the attempt axis
        //does, since each round builds a FRESH engine whose own insertion count starts
        //at zero: the bound governs the accumulated total at a round boundary and the
        //round's own total inside a round. It stays a local rather than joining the
        //assembled statistics — the attempt total is composed onto the reported
        //InferenceAttempts by an existing convention, and adding the same composition
        //to ClausesDerived would move a reported column on every multi-round decision.
        int priorDerived = 0;
        int seededRounds = 0;
        int keyForcedUnions = 0;
        int keyJoinCandidates = 0;

        while(true)
        {
            ClausificationResult clausification = ContextClausifier.Clausify(module, lowering, registry, seeds, ContextGroundCountingRider, (deciderFaces & EnumerationDeciderFaces.ClashOnly) != 0, rootKeyJoinEnabled);

            //The clash-only face's told clash decides AHEAD of the second gate:
            //an inconsistency condemns the module regardless of any remainder,
            //so a delegation entry can never mask the decided verdict; the
            //nominal arm has no counting-edge remainder to suppress. No engine
            //exists on this path; the statistics are the all-zero pre-engine
            //record.
            if(clausification.NominalClash)
            {
                ContextSaturationStatistics nominalStatistics = NominalClashStatistics() with
                {
                    InferenceAttempts = priorAttempts,
                    EnumerationDeciderClashes = 1,
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    NegativePolarityDataMarkersMinted = clausification.NegativePolarityDataMarkers,
                };
                nominalStatistics = WithEnumerationWindow(nominalStatistics, clausification, enumerationMeasurement, partitionWindow, gadgetWindow, spyPointWindow, bijectionChainWindow, toldGroundWitnessWindow, repairingWindow, modalExpansionWindow, modalGadgetWindow, nominalPinnedRoleWindow);
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, nominalStatistics));

                return true;
            }

            //The survey admits chains, transitivity, and the negative role constraints,
            //whose clausifier guards (regularity, simplicity, reserved roles, the
            //automaton budget) legitimately leave a non-empty remainder on a
            //survey-admitted module; the second gate delegates any such module — and any
            //survey/clausifier drift beyond the engine's clause grammar — rather than
            //trust a fragment-relative context verdict.
            if(DelegatesOnSecondGate(clausification))
            {
                return false;
            }

            keyForcedUnions += clausification.KeyForcedUnions;

            //Rounds run so far, this one included when the key machinery engaged at
            //all (seeds and unions exist only under descriptors).
            int mergeRounds = clausification.KeyDescriptors.Count > 0 ? seededRounds + 1 : seededRounds;

            //A ground clash decided at clausification — a pre-merge representative
            //collision, a closure clash over the asserted-edge graph, a told
            //pigeonhole (the rider), or a key-forced merge collision — answers
            //Decided(inconsistent) without spinning up the engine. No engine
            //exists on this path, so the statistics are constructed directly from
            //the clausification counters, with prior rounds' totals carried
            //forward.
            if(clausification.GroundClash)
            {
                ContextSaturationStatistics groundStatistics = GroundClashStatistics(clausification) with
                {
                    InferenceAttempts = priorAttempts,
                    MergeRounds = mergeRounds,
                    KeyForcedUnions = keyForcedUnions,
                    KeyJoinCandidates = keyJoinCandidates,
                    GroundCountingClashes = GroundClashReasons.IsGroundCountingPigeonhole(clausification.GroundClashReason) ? 1 : 0,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NegativePolarityDataMarkersMinted = clausification.NegativePolarityDataMarkers,
                };
                groundStatistics = WithEnumerationWindow(groundStatistics, clausification, enumerationMeasurement, partitionWindow, gadgetWindow, spyPointWindow, bijectionChainWindow, toldGroundWitnessWindow, repairingWindow, modalExpansionWindow, modalGadgetWindow, nominalPinnedRoleWindow);
                decision = ModuleDecision.Decided(new ModuleVerdict(false, []), ContextDecisionStatistics(module.Axioms.Count, groundStatistics));

                return true;
            }

            //Round-0 unions without a collision leave the ground structures built
            //under pre-join roots — the round hands its pairs to a seeded
            //re-clausification instead of saturating a stale structure.
            if(clausification.KeyForcedUnions > 0)
            {
                foreach((Utf8String first, Utf8String second) in clausification.KeyUnionPairs)
                {
                    seeds.Add((first, second));
                }

                seededRounds++;
                continue;
            }

            ContextSaturationEngine engine = ContextSaturationEngine.Create(clausification, registry, paramodulationScope, propagationRelevance, topology, progressSampler);
            engine.EnumerationHabitat = survey.EnumerationHabitat;
            engine.RootDataObligationsEnabled = rootDataObligationsEnabled;
            engineProbe?.Invoke(engine);

            List<Utf8String> signature = [];
            List<int> signatureAtoms = [];
            bool cap = false;
            if(includeSubsumptions)
            {
                signature = ModuleSweepSignature.Build(module);
                cap = signature.Count <= AlcModuleReasoner.SubsumptionSignatureCap;
                if(cap)
                {
                    foreach(Utf8String signatureClass in signature)
                    {
                        int atom = clausification.Symbols.AtomOf(signatureClass);
                        signatureAtoms.Add(atom);
                        engine.EnsureQueryContext(atom);
                    }
                }
            }

            //One logical saturation bracket spans each round of the context decision:
            //it opens before Saturate and closes in the finally, so the
            //ContextSaturation phase counts once per admitted round at EVERY exit —
            //the budget-exhausted early return, the completed run past the ghost pass,
            //and a cancellation or any other exception thrown out of either call —
            //mirroring the EL phase's semantics. The ghost pass's ground re-closure is
            //deliberately attributed to this one saturation phase rather than split
            //off: it is saturation-owned verdict work, and the per-rule split lives on
            //the statistics record, not the phase clock.
            long saturationStart = ReasoningInstrumentation.Begin();
            SaturationOutcome outcome;
            try
            {
                outcome = engine.Saturate(budget, cancellationToken);
                if(outcome != SaturationOutcome.BudgetExhausted)
                {
                    //The post-saturation Self-ghost pass runs ONLY
                    //on a completed saturation — the partial ghost set of a budget stop
                    //is never trusted — and before the verdict is read: a re-closure
                    //clash latches a module inconsistency the widened verdict picks up.
                    engine.RunGroundGhostPass();
                }
            }
            finally
            {
                ReasoningInstrumentation.End(ReasoningPhase.ContextSaturation, saturationStart);
            }

            if(outcome == SaturationOutcome.BudgetExhausted)
            {
                ContextSaturationStatistics exhaustedStatistics = engine.BuildStatistics(contextDecided: false);
                exhaustedStatistics = exhaustedStatistics with
                {
                    InferenceAttempts = exhaustedStatistics.InferenceAttempts + priorAttempts,
                    MergeRounds = mergeRounds,
                    KeyForcedUnions = keyForcedUnions,
                    KeyJoinCandidates = keyJoinCandidates,
                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                    EnumerationHabitat = survey.EnumerationHabitat,
                    NegativePolarityDataMarkersMinted = clausification.NegativePolarityDataMarkers,
                };
                exhaustedStatistics = WithEnumerationWindow(exhaustedStatistics, clausification, enumerationMeasurement, partitionWindow, gadgetWindow, spyPointWindow, bijectionChainWindow, toldGroundWitnessWindow, repairingWindow, modalExpansionWindow, modalGadgetWindow, nominalPinnedRoleWindow);
                decision = ModuleDecision.AbstainedOnBudget(ContextDecisionStatistics(module.Axioms.Count, exhaustedStatistics));

                return true;
            }

            if(!engine.IsInconsistent)
            {
                //The engine latches delegate the module named (the undecided-key
                //discipline): an out-of-grammar derived head was refused insertion, a
                //per-constant root data obligation stayed undecided
                //(DataObligationUndecidedOnRoot —
                //the per-constant arm could size neither a unit class pool nor a
                //non-narrowing disjunctive root marker), or the generated-nominal
                //population outgrew the packed term width — each makes the saturation
                //incomplete, so no whole verdict is claimed. A derived inconsistency
                //stays decisive above (every inserted clause came from a sound rule
                //application; incompleteness is downward-stable).
                if(engine.HasOutOfGrammarDerivation || engine.HasDataObligationUndecidedOnRoot || engine.HasPackedWidthOverflow)
                {
                    return false;
                }

                //The license scope's two-surface blocked-live latch (sound-or-silent
                //while its completeness obligations stand open): a run in which a
                //scopable rewrite was blocked in a consistency read-off context may
                //not assert CONSISTENT, and a query context with a blocked rewrite
                //may not certify a satisfiable or non-subsumption read — either arm
                //delegates the module named instead of claiming the positive
                //verdict, with the blocked counters carrying the attribution. A
                //derived inconsistency stays decisive above (the widened scope only
                //removes inferences); both arms are unreachable outside the
                //license-scoped measurement cells.
                if(engine.HasEqScopeBlockedConsistencyReadOff || engine.HasEqScopeBlockedQueryReadOff)
                {
                    return false;
                }

                //An uncertain key-class membership makes the join incomplete — a
                //forced merge may hide behind a carried disjunct — so the module
                //delegates rather than claim a consistent whole.
                if(engine.HasUndecidedKeyObligation)
                {
                    return false;
                }

                //The root-tier key-obligation latch and the off-fold equality backstop
                //delegate the module named before the root join runs: an uncertain root key-class
                //membership (KeyMembershipUndecidedOnRoot) or an unmerged root ground
                //equality between candidates (RootEqualityOutsideFold) makes the join
                //incomplete. Both self-gate on the armed root key join, so a
                //non-lift module never reaches them (dark). A backstop latch surfaces
                //its head count onto the delegated module's context totals for the
                //corpus census — a decision-time observation that
                //moves no verdict.
                if(engine.HasRootEqualityOutsideFold)
                {
                    delegatedContextTotals = ContextSaturationStatistics.Empty with { RootEqualityOutsideFoldHeads = engine.RootEqualityOutsideFoldHeads };
                }

                if(engine.HasUndecidedRootKeyObligation || engine.HasRootEqualityOutsideFold)
                {
                    return false;
                }

                //The general relay latch (RootEqualityRidesAChoice): a
                //DerivedUnderChoice root equality was refused a guard site during
                //saturation (the ≈-class fold, the Pred relay, the r-Pred broadcast, or
                //the unconditional-head projection), so a merge an unrecorded disjunct
                //drop would have manufactured never fired and the module delegates named
                //rather than claim a whole verdict off the guarded fold. Unlike the
                //key-join backstop, this latch is general — no armed-key-join gate — so it
                //fires for a plain nominal module here, before the key gate below. A
                //derived inconsistency stays decisive above (incompleteness is
                //downward-stable). The head count surfaces onto the delegated module's
                //context totals for the corpus census.
                if(engine.HasRootEqualityRidesAChoice)
                {
                    delegatedContextTotals = ContextSaturationStatistics.Empty with { RootEqualityRidesAChoiceHeads = engine.RootEqualityRidesAChoiceHeads };

                    return false;
                }

                //The post-saturation FULL key join runs at least once on every
                //completed saturation: derived-certain
                //memberships exist only here, and a told object key over a told
                //sub-property is only visible on the closed graph. One loop, two
                //join arms selected by jurisdiction: the root
                //join for a nominal module (option (a), re-saturating the same
                //engine), the ground join for an ordinary one.
                if(clausification.KeyDescriptors.Count > 0)
                {
                    if(clausification.NominalJurisdiction)
                    {
                        RootKeyJoinOutcome rootOutcome = engine.RunPostSaturationRootKeyJoin(budget, cancellationToken, out int rootCandidates, out int rootFired);
                        keyJoinCandidates += rootCandidates;
                        keyForcedUnions += rootFired;
                        if(rootOutcome == RootKeyJoinOutcome.Indeterminate)
                        {
                            return false;
                        }

                        if(rootOutcome == RootKeyJoinOutcome.BudgetExhausted)
                        {
                            ContextSaturationStatistics rootRoundStatistics = engine.BuildStatistics(contextDecided: false) with
                            {
                                InferenceAttempts = engine.BuildStatistics(contextDecided: false).InferenceAttempts + priorAttempts,
                                MergeRounds = mergeRounds,
                                KeyForcedUnions = keyForcedUnions,
                                KeyJoinCandidates = keyJoinCandidates,
                                NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                                EnumerationHabitat = survey.EnumerationHabitat,
                                NegativePolarityDataMarkersMinted = clausification.NegativePolarityDataMarkers,
                            };
                            rootRoundStatistics = WithEnumerationWindow(rootRoundStatistics, clausification, enumerationMeasurement, partitionWindow, gadgetWindow, spyPointWindow, bijectionChainWindow, toldGroundWitnessWindow, repairingWindow, modalExpansionWindow, modalGadgetWindow, nominalPinnedRoleWindow);
                            decision = ModuleDecision.AbstainedOnBudget(ContextDecisionStatistics(module.Axioms.Count, rootRoundStatistics));

                            return true;
                        }

                        //A merge the root join fired may have latched a new root
                        //obligation or forced an off-fold equality on re-saturation; an
                        //inconsistency it forced is decisive and read below by
                        //BuildVerdict, so the re-check delegates only a consistent whole.
                        //A backstop latch on the re-saturation surfaces its head count
                        //the same way, for the corpus census.
                        if(!engine.IsInconsistent && engine.HasRootEqualityOutsideFold)
                        {
                            delegatedContextTotals = ContextSaturationStatistics.Empty with { RootEqualityOutsideFoldHeads = engine.RootEqualityOutsideFoldHeads };
                        }

                        if(!engine.IsInconsistent && (engine.HasUndecidedRootKeyObligation || engine.HasRootEqualityOutsideFold))
                        {
                            return false;
                        }

                        //The general relay latch on the re-saturation, for parity with
                        //the completion-block leg: a merge the root join fired may have
                        //refused a choice-riding root equality at a guard site, so a
                        //consistent whole is delegated named with the head count surfaced.
                        if(!engine.IsInconsistent && engine.HasRootEqualityRidesAChoice)
                        {
                            delegatedContextTotals = ContextSaturationStatistics.Empty with { RootEqualityRidesAChoiceHeads = engine.RootEqualityRidesAChoiceHeads };

                            return false;
                        }
                    }
                    else
                    {
                        PostKeyJoinOutcome joinOutcome = RunPostSaturationKeyJoin(clausification, engine, registry, seeds, out int candidates, out int firedUnions);
                        keyJoinCandidates += candidates;
                        if(joinOutcome == PostKeyJoinOutcome.Indeterminate)
                        {
                            return false;
                        }

                        if(firedUnions > 0)
                        {
                            keyForcedUnions += firedUnions;
                            seededRounds++;
                            priorAttempts += engine.BuildStatistics(contextDecided: false).InferenceAttempts;
                            priorDerived += engine.BuildStatistics(contextDecided: false).ClausesDerived;
                            if(budget.IsExhaustedByInferences(priorAttempts) || budget.IsExhaustedByPopulation(priorDerived))
                            {
                                ContextSaturationStatistics roundStatistics = engine.BuildStatistics(contextDecided: false) with
                                {
                                    InferenceAttempts = priorAttempts,
                                    MergeRounds = mergeRounds,
                                    KeyForcedUnions = keyForcedUnions,
                                    KeyJoinCandidates = keyJoinCandidates,
                                    NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                                    EnumerationHabitat = survey.EnumerationHabitat,
                                    NegativePolarityDataMarkersMinted = clausification.NegativePolarityDataMarkers,
                                };
                                roundStatistics = WithEnumerationWindow(roundStatistics, clausification, enumerationMeasurement, partitionWindow, gadgetWindow, spyPointWindow, bijectionChainWindow, toldGroundWitnessWindow, repairingWindow, modalExpansionWindow, modalGadgetWindow, nominalPinnedRoleWindow);
                                decision = ModuleDecision.AbstainedOnBudget(ContextDecisionStatistics(module.Axioms.Count, roundStatistics));

                                return true;
                            }

                            continue;
                        }
                    }
                }

                //A completed saturation with a derived ⊥ is decisive even under an
                //undecided data obligation (inconsistency is downward-stable). Without
                //a derived ⊥ but with an undecided data obligation the context verdict
                //would be fragment-relative, so the module delegates (§3.4): the
                //dispatcher falls through to the fallback, whose marker-bearing
                //verdict is returned — parity by construction. The root join may have
                //re-saturated the engine into an inconsistency, which stays decisive.
                if(!engine.IsInconsistent && engine.HasUndecidedDataObligation)
                {
                    return false;
                }
            }

            ModuleVerdict verdict = BuildVerdict(engine, signature, signatureAtoms, cap);
            ContextSaturationStatistics statistics = engine.BuildStatistics(contextDecided: true);
            statistics = statistics with
            {
                InferenceAttempts = statistics.InferenceAttempts + priorAttempts,
                MergeRounds = mergeRounds,
                KeyForcedUnions = keyForcedUnions,
                KeyJoinCandidates = keyJoinCandidates,
                NominalCountingInverseCooccurrence = survey.NominalCountingInverseCooccurrence,
                EnumerationHabitat = survey.EnumerationHabitat,
                NegativePolarityDataMarkersMinted = clausification.NegativePolarityDataMarkers,
            };
            statistics = WithEnumerationWindow(statistics, clausification, enumerationMeasurement, partitionWindow, gadgetWindow, spyPointWindow, bijectionChainWindow, toldGroundWitnessWindow, repairingWindow, modalExpansionWindow, modalGadgetWindow, nominalPinnedRoleWindow);
            decision = ModuleDecision.Decided(verdict, ContextDecisionStatistics(module.Axioms.Count, statistics));

            return true;
        }
    }

    /// <summary>Whether the production fixpoint runs the ground-counting pigeonhole rider: the clausifier's bounded told-distinct clique search decides a told counting clash outright instead of delegating it on the counting-edge remainder; every non-clash counting module keeps the remainder and delegates exactly as with the rider off. The engine pins drive both rider faces through the clausifier's explicit parameter.</summary>
    private const bool ContextGroundCountingRider = true;

    /// <summary>The enumeration-CSP decider's production face selection: every face LIT — folded from the census-first recognizer's registry table (<see cref="ContextHabitatRecognizer.EveryFaceLit"/>), so a family that registers a row lights its faces by construction and a face no row owns is never lit. The lit faces decide their habitats pre-engine with zero inference attempts, sound-or-silent behind the locked jurisdiction gates, and every module outside the habitats reaches the engine untouched. The decider battery drives <see cref="EnumerationDeciderFaces.None"/> through the faces-carrying internal overloads as the explicit dark control.</summary>
    private static readonly EnumerationDeciderFaces ContextEnumerationDecider = ContextHabitatRecognizer.EveryFaceLit;

    /// <summary>The vr key-join lift's production switch: ON — a HasKey+nominal module is routed past the <c>KeyOnNominalModule</c> guard into intake, where the root key join arms with the root latch and the off-fold backstop. Every reasoner path threads this const; the faces-carrying internal overload alone threads the switch OFF as the explicit dark control for the dark-face tests.</summary>
    private const bool ContextRootKeyJoin = true;

    /// <summary>The per-constant root data-obligation lift's production switch: ON — a data demand landing at a constant on a root context is decided per ≈-class through the engine's <see cref="ContextSaturationEngine.RootDataObligationsEnabled"/>, the re-probe hook re-decides a merged class, and a non-narrowing disjunctive root marker delegates named through <c>DataObligationUndecidedOnRoot</c>. Independent of <see cref="ContextRootKeyJoin"/>. Every reasoner path threads this const; the internal overload alone threads the switch OFF as the explicit dark control for the dark-face tests.</summary>
    private const bool ContextRootDataObligations = true;

    /// <summary>The post-saturation key join's outcome. Internal for the engine pins, which drive the join below the closed survey.</summary>
    internal enum PostKeyJoinOutcome
    {
        /// <summary>Every comparison was decisive; fired unions, if any, ride the seed list.</summary>
        Clean,

        /// <summary>A data comparison answered <c>Indeterminate</c> — the module delegates.</summary>
        Indeterminate,
    }

    /// <summary>
    /// The post-saturation FULL key join (rounds past
    /// zero): per descriptor, every pair of named-class candidates — membership
    /// read as derived-CERTAIN off the ground context's single-literal live heads
    /// via <see cref="ContextSaturationEngine.IsSubsumedBy"/>, told memberships
    /// included by construction (the marker GCI fires by Hyper) — sharing a named
    /// closed object value on every object key property and a value-space-equal
    /// literal on every data key property appends a union pair to the seed list.
    /// An <c>Indeterminate</c> value comparison delegates the module instead. The
    /// round's representatives are already merged roots, so a fired pair is always
    /// a genuinely new union and the fixpoint strictly descends.
    /// </summary>
    /// <param name="clausification">The round's clausification, carrying the descriptors, stores, graph, and named roots.</param>
    /// <param name="engine">The saturated engine whose ground contexts the membership readout consults.</param>
    /// <param name="registry">The registered-datatype set the value comparisons consult.</param>
    /// <param name="seedsToAppendTo">The accumulated seed list the fired pairs append to.</param>
    /// <param name="candidatesEnumerated">The candidate representatives enumerated across descriptors.</param>
    /// <param name="firedUnions">The union pairs fired.</param>
    /// <returns>The join outcome.</returns>
    internal static PostKeyJoinOutcome RunPostSaturationKeyJoin(
        ClausificationResult clausification,
        ContextSaturationEngine engine,
        DatatypeRegistry registry,
        List<(Utf8String First, Utf8String Second)> seedsToAppendTo,
        out int candidatesEnumerated,
        out int firedUnions)
    {
        candidatesEnumerated = 0;
        firedUnions = 0;
        HashSet<(Utf8String First, Utf8String Second)> fired = [];
        foreach(GroundKeyDescriptor descriptor in clausification.KeyDescriptors)
        {
            List<Utf8String> candidates = [];
            foreach(Utf8String representative in clausification.GroundRepresentatives)
            {
                if(!clausification.NamedRoots.Contains(representative))
                {
                    continue;
                }

                if(!descriptor.ClassIsThing && !engine.IsSubsumedBy(clausification.GroundMarkers[representative], descriptor.ClassAtom))
                {
                    continue;
                }

                candidates.Add(representative);
            }

            candidatesEnumerated += candidates.Count;
            for(int i = 0; i < candidates.Count; i++)
            {
                for(int j = i + 1; j < candidates.Count; j++)
                {
                    bool shared = true;
                    foreach(RawRoleId role in descriptor.ObjectRoles)
                    {
                        if(!SharesNamedClosedTarget(clausification, candidates[i], candidates[j], role))
                        {
                            shared = false;
                            break;
                        }
                    }

                    if(!shared)
                    {
                        continue;
                    }

                    foreach(Utf8String property in descriptor.DataProperties)
                    {
                        DatatypeValueIdentity identity = SharesDataValue(clausification, candidates[i], candidates[j], property, registry);
                        if(identity == DatatypeValueIdentity.Indeterminate)
                        {
                            return PostKeyJoinOutcome.Indeterminate;
                        }

                        if(identity == DatatypeValueIdentity.Distinct)
                        {
                            shared = false;
                            break;
                        }
                    }

                    if(shared && fired.Add((candidates[i], candidates[j])))
                    {
                        seedsToAppendTo.Add((candidates[i], candidates[j]));
                        firedUnions++;
                    }
                }
            }
        }

        return PostKeyJoinOutcome.Clean;
    }

    /// <summary>Whether two representatives share a NAMED closed target over an object key property — the Table 9 named-value requirement, read off the closed graph by the descriptor's raw directioned role id.</summary>
    /// <param name="clausification">The round's clausification.</param>
    /// <param name="first">The first representative.</param>
    /// <param name="second">The second representative.</param>
    /// <param name="role">The object key property's raw directioned role id.</param>
    /// <returns><see langword="true"/> when a shared named target exists.</returns>
    private static bool SharesNamedClosedTarget(ClausificationResult clausification, Utf8String first, Utf8String second, RawRoleId role)
    {
        IReadOnlyList<Utf8String> firstTargets = clausification.GroundGraph.TargetsOf(first, role);
        if(firstTargets.Count == 0)
        {
            return false;
        }

        IReadOnlyList<Utf8String> secondTargets = clausification.GroundGraph.TargetsOf(second, role);
        if(secondTargets.Count == 0)
        {
            return false;
        }

        foreach(Utf8String candidate in firstTargets)
        {
            if(!clausification.NamedRoots.Contains(candidate))
            {
                continue;
            }

            foreach(Utf8String other in secondTargets)
            {
                if(candidate.Equals(other))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The three-valued shared-value judgement of two representatives over a data key property, from the round's value store: <c>Same</c> on some value-space-equal pair, <c>Indeterminate</c> when no pair is equal but a comparison abstains, <c>Distinct</c> otherwise — including a missing value list, which never fires the key.</summary>
    /// <param name="clausification">The round's clausification.</param>
    /// <param name="first">The first representative.</param>
    /// <param name="second">The second representative.</param>
    /// <param name="property">The data key property's IRI.</param>
    /// <param name="registry">The registered-datatype set the comparisons consult.</param>
    /// <returns>The shared-value judgement.</returns>
    private static DatatypeValueIdentity SharesDataValue(ClausificationResult clausification, Utf8String first, Utf8String second, Utf8String property, DatatypeRegistry registry)
    {
        if(!clausification.KeyValueStore.TryGetValue(first, out Dictionary<Utf8String, List<Literal>>? firstProperties) || !firstProperties.TryGetValue(property, out List<Literal>? firstValues))
        {
            return DatatypeValueIdentity.Distinct;
        }

        if(!clausification.KeyValueStore.TryGetValue(second, out Dictionary<Utf8String, List<Literal>>? secondProperties) || !secondProperties.TryGetValue(property, out List<Literal>? secondValues))
        {
            return DatatypeValueIdentity.Distinct;
        }

        bool indeterminate = false;
        foreach(Literal firstValue in firstValues)
        {
            foreach(Literal secondValue in secondValues)
            {
                DatatypeValueIdentity identity = DatatypeSatisfiabilityChecker.CompareValues(firstValue, secondValue, registry);
                if(identity == DatatypeValueIdentity.Same)
                {
                    return DatatypeValueIdentity.Same;
                }

                indeterminate |= identity == DatatypeValueIdentity.Indeterminate;
            }
        }

        return indeterminate ? DatatypeValueIdentity.Indeterminate : DatatypeValueIdentity.Distinct;
    }

    /// <summary>
    /// Reads the module verdict off the saturated structure: an inconsistent
    /// structure yields a consistent-false verdict with no subsumptions; a
    /// consistent one enumerates the module-local subsumptions from the per-class
    /// query contexts when the signature is within the cap.
    /// </summary>
    /// <param name="engine">The saturated engine.</param>
    /// <param name="signature">The signature classes, parallel to <paramref name="signatureAtoms"/>.</param>
    /// <param name="signatureAtoms">The interned atom id of each signature class.</param>
    /// <param name="cap">Whether the signature is within the subsumption cap, so query contexts exist.</param>
    /// <returns>The verdict.</returns>
    private static ModuleVerdict BuildVerdict(ContextSaturationEngine engine, List<Utf8String> signature, List<int> signatureAtoms, bool cap)
    {
        //A delegate-backed registered datatype that decided an obligation names its self-certified
        //provenance on the module remainder, the same channel the undecided marker rides.
        List<string> unsupported = engine.HasSelfCertifiedDataDecision ? [DataRestrictionConsistency.SelfCertifiedMarker] : [];
        if(engine.IsInconsistent)
        {
            return new ModuleVerdict(false, [])
            {
                UnsupportedConstructs = unsupported,
            };
        }

        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions = [];
        if(cap)
        {
            for(int i = 0; i < signature.Count; i++)
            {
                for(int j = 0; j < signature.Count; j++)
                {
                    if(i == j)
                    {
                        continue;
                    }

                    if(engine.IsSubsumedBy(signatureAtoms[i], signatureAtoms[j]))
                    {
                        subsumptions.Add((new NamedNode(signature[i]), new NamedNode(signature[j])));
                    }
                }
            }
        }

        return new ModuleVerdict(true, subsumptions)
        {
            UnsupportedConstructs = unsupported,
        };
    }

    /// <summary>
    /// Whether the clausification falls outside the engine's clause grammar and
    /// the reasoner must delegate: a non-empty remainder, a role automaton that
    /// exceeded its state budget, a fresh role that is not a DL4 counting role
    /// (the fresh-role count diverging from the counting-role count), a head
    /// literal outside the per-literal grammar, a clause carrying a body role
    /// atom with both arguments the central variable <c>x</c> (the <c>S(x,x)</c>
    /// self-loop shape), or a constant in any ontology-clause body slot (the
    /// published DL-clause grammar keeps bodies over <c>Bi(x)</c> /
    /// <c>Si(zj, x)</c> / <c>Si(x, zj)</c> only — a nominal module's ground
    /// premises ride the root-fact seeding, never an ontology body). The
    /// per-literal head grammar: an (in)equality literal
    /// lies over the neighbour/function grammar the engine's equality rules
    /// consume at any head length — widened, under nominal jurisdiction only,
    /// with the central-against-constant EQUALITY <c>x ≈ o</c> (the DL8 and
    /// enumeration heads); a concept literal carries a central,
    /// neighbour, or function term — or, under nominal jurisdiction, a constant
    /// (the DL7 fact <c>B(o)</c>) — alone in its head and, in a disjunctive
    /// head, a central term that is a non-marker atom or a Universal-kind
    /// data-demand marker (the non-value-forcing NNF-dual shape the disjunctive
    /// data rules refute or jointly certify); an Existential or MinCardinality
    /// marker in a disjunctive head stays out of grammar and delegates — the
    /// drift belt against a value-forcing marker reaching an uncommitted
    /// disjunct; a role literal
    /// carries exactly one central argument against a neighbour or function term,
    /// and never shares a disjunctive head. A fresh role from an unvetted source,
    /// a central or context term in an equality literal beyond the jurisdiction
    /// shape, a two-central role atom
    /// (no emission path produces one — the self-variant pass rewrites every
    /// would-be loop atom to a <c>Self_p</c> concept), and an out-of-grammar
    /// disjunct are the drift tripwires that keep a survey/clausifier regression
    /// from feeding the engine an unvetted shape its rule machinery mis-parses.
    /// The root facts of a nominal module take the engine's seeding path, whose
    /// context-kind-aware insertion guard latches any out-of-grammar shape into
    /// the named-delegation latch — the gate here stays the ontology-clause
    /// intake check.
    /// </summary>
    /// <param name="clausification">The clausification result.</param>
    /// <returns><see langword="true"/> when the module must be delegated.</returns>
    internal static bool DelegatesOnSecondGate(ClausificationResult clausification)
    {
        if(clausification.Remainder.Count > 0 || clausification.AutomatonBudgetExceeded || clausification.FreshRoles != clausification.CountingRoles)
        {
            return true;
        }

        foreach(DlClause clause in clausification.Clauses)
        {
            ReadOnlySpan<DlLiteral> body = clause.Body;
            for(int i = 0; i < body.Length; i++)
            {
                if(IsTwoCentralRole(body[i]) || MentionsIndividual(body[i]))
                {
                    return true;
                }
            }

            ReadOnlySpan<DlLiteral> head = clause.Head;
            for(int i = 0; i < head.Length; i++)
            {
                if(!IsInGrammarHeadLiteral(head[i], head.Length > 1, clausification.DataDemandDescriptors, clausification.NominalJurisdiction))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a head literal lies within the per-literal grammar the engine's rules consume: an (in)equality over the neighbour/function grammar at any head length — widened under nominal jurisdiction with the equality between the central variable and a constant (the DL8 and enumeration head <c>x ≈ o</c>, the only constant-bearing (in)equality a Table-1 lowering emits); a concept atom over a central, neighbour, or function term — or, under nominal jurisdiction, a constant (the DL7 fact <c>B(o)</c>) — alone, or over the central variable in a disjunctive head — where a data-demand marker is in grammar exactly when its kind is <see cref="DataDemandKind.Universal"/>, the non-value-forcing NNF-dual shape the disjunctive data rules refute or jointly certify; an Existential, MinCardinality, or MaxCardinality marker in a disjunctive head stays OUT of grammar and delegates, the drift belt against a counting or value-forcing marker reaching an uncommitted disjunct; a role atom with exactly one central argument against a neighbour or function term, never in a disjunctive head.</summary>
    /// <param name="literal">The head literal.</param>
    /// <param name="disjunctive">Whether the literal shares its head with other literals.</param>
    /// <param name="dataDemands">The clausification's demand-marker descriptors, keyed by marker atom.</param>
    /// <param name="nominalJurisdiction">Whether the module is nominal-bearing, admitting the constant-bearing ontology head shapes; a nominal-free module's grammar is byte-identical to the shipped one.</param>
    /// <returns><see langword="true"/> when the literal is in-grammar at its head length.</returns>
    private static bool IsInGrammarHeadLiteral(DlLiteral literal, bool disjunctive, IReadOnlyDictionary<int, DataDemandDescriptor> dataDemands, bool nominalJurisdiction)
    {
        return literal.Kind switch
        {
            DlLiteralKind.Equality or DlLiteralKind.Inequality => IsInGrammarEqualityLiteral(literal, nominalJurisdiction),
            DlLiteralKind.Concept when disjunctive => literal.First.IsCentral
                && (!dataDemands.TryGetValue(literal.Symbol, out DataDemandDescriptor demand) || demand.Kind == DataDemandKind.Universal),
            DlLiteralKind.Concept => literal.First.Kind is DlTermKind.Central or DlTermKind.Neighbour or DlTermKind.Function
                || (nominalJurisdiction && literal.First.IsIndividual),
            DlLiteralKind.Role => !disjunctive && IsInGrammarRoleAtom(literal),
            _ => false,
        };
    }

    /// <summary>Whether a role head literal is one of the context-grammar successor or propagation shapes: exactly one argument is the central variable <c>x</c> and the other is a neighbour variable or function term.</summary>
    /// <param name="literal">The role head literal.</param>
    /// <returns><see langword="true"/> when exactly one argument is central and the other is a neighbour or function term.</returns>
    private static bool IsInGrammarRoleAtom(DlLiteral literal)
    {
        return literal.First.IsCentral
            ? IsNeighbourOrFunction(literal.Second)
            : literal.Second.IsCentral && IsNeighbourOrFunction(literal.First);
    }

    /// <summary>Whether an equality or inequality head literal lies within the context-literal grammar the engine's equality rules consume: both terms are neighbour variables or function terms (the <c>f approx g</c> / <c>f approx y</c> / <c>y approx y</c> shapes), or — an EQUALITY under nominal jurisdiction only — one term is the central variable and the other a constant (the DL8 and enumeration head <c>x ≈ o</c>; no Table-1 lowering emits a constant-bearing inequality or a constant pair into an ontology head, so those stay intake drift). A central or context term outside that shape is out-of-grammar drift the gate delegates on.</summary>
    /// <param name="literal">The equality or inequality head literal.</param>
    /// <param name="nominalJurisdiction">Whether the module is nominal-bearing, admitting the central-against-constant equality.</param>
    /// <returns><see langword="true"/> when the literal is in-grammar.</returns>
    private static bool IsInGrammarEqualityLiteral(DlLiteral literal, bool nominalJurisdiction)
    {
        if(IsNeighbourOrFunction(literal.First) && IsNeighbourOrFunction(literal.Second))
        {
            return true;
        }

        return nominalJurisdiction
            && literal.Kind == DlLiteralKind.Equality
            && ((literal.First.IsCentral && literal.Second.IsIndividual) || (literal.First.IsIndividual && literal.Second.IsCentral));
    }

    /// <summary>Whether a term is a neighbour variable or a function term — the two term kinds an admitted context equality literal ranges over.</summary>
    /// <param name="term">The term to test.</param>
    /// <returns><see langword="true"/> for a neighbour or function term.</returns>
    private static bool IsNeighbourOrFunction(DlTerm term)
    {
        return term.Kind is DlTermKind.Neighbour or DlTermKind.Function;
    }

    /// <summary>Whether a literal is a role atom with both arguments the central variable <c>x</c> — the <c>S(x,x)</c> self-loop shape the neighbour-index machinery aliases to the neighbour <c>z0</c>.</summary>
    /// <param name="literal">The literal to test.</param>
    /// <returns><see langword="true"/> for a two-central role atom.</returns>
    private static bool IsTwoCentralRole(DlLiteral literal)
    {
        return literal.Kind == DlLiteralKind.Role && literal.First.IsCentral && literal.Second.IsCentral;
    }

    /// <summary>Whether a literal carries a constant in either slot — out-of-grammar drift in an ontology-clause BODY at any jurisdiction: the published DL-clause grammar keeps bodies over <c>Bi(x)</c> / <c>Si(zj, x)</c> / <c>Si(x, zj)</c> only, and every constant-bearing ground premise rides the root-fact seeding instead.</summary>
    /// <param name="literal">The body literal to test.</param>
    /// <returns><see langword="true"/> when either term is a constant or a function of one.</returns>
    private static bool MentionsIndividual(DlLiteral literal)
    {
        return literal.First.Kind is DlTermKind.Individual or DlTermKind.FunctionOfIndividual
            || literal.Second.Kind is DlTermKind.Individual or DlTermKind.FunctionOfIndividual;
    }

    /// <summary>Reattaches the off-fold equality backstop's head count onto a delegated module's decision: when the backstop drove the delegation, <paramref name="latchTotals"/> carries the count and it overlays the fallback's context totals for the corpus census; otherwise the fallback's decision stands unchanged, so a delegation the backstop did not drive is byte-identical to the fallback's own.</summary>
    /// <param name="fallback">The fallback oracle's decision on the delegated module.</param>
    /// <param name="latchTotals">The backstop-carrying context totals, or <see langword="null"/> when the backstop did not drive the delegation.</param>
    /// <returns>The fallback's decision, its context totals overlaid with the backstop count when one was carried.</returns>
    private static ModuleDecision DelegateWithLatchTotals(ModuleDecision fallback, ContextSaturationStatistics? latchTotals)
    {
        return latchTotals is ContextSaturationStatistics totals
            ? fallback with { Statistics = fallback.Statistics with { ContextTotals = totals } }
            : fallback;
    }

    /// <summary>The face an enumeration-algebra verdict is attributed to: the pair-composition faces when the pair sweep produced it — that sweep walks at least one vector exactly when it decides — and the certifying face when the block sweep inside the member window did.</summary>
    /// <param name="outcome">The decided outcome.</param>
    /// <returns>The face that must be lit for the verdict to stand.</returns>
    private static EnumerationDeciderFaces DecidingAlgebraFace(EnumerationAlgebraOutcome outcome)
    {
        return (outcome.PairVectorCount > 0, outcome.Verdict) switch
        {
            (true, EnumerationAlgebraVerdict.Consistent) => EnumerationDeciderFaces.EnumerationPairCertify,
            (true, _) => EnumerationDeciderFaces.EnumerationPairClash,
            _ => EnumerationDeciderFaces.Certifying,
        };
    }

    /// <summary>Builds the decision statistics for a context-decided module: the context totals, with empty solver and tableau totals.</summary>
    /// <param name="moduleAxiomCount">The number of axioms in the decided module.</param>
    /// <param name="statistics">The context-saturation telemetry.</param>
    /// <returns>The decision statistics.</returns>
    private static ReasoningDecisionStatistics ContextDecisionStatistics(int moduleAxiomCount, ContextSaturationStatistics statistics)
    {
        return new ReasoningDecisionStatistics(moduleAxiomCount, SolveCount: 0, SatSolveStatistics.Empty, ContextTotals: statistics);
    }

    /// <summary>The Shape E census numbers one decision carries: the member universe measured over the module, the certifying face's window silences when the face ran, and the pair-composition tier's structural reading with its own window silence and the vectors its sweep walked.</summary>
    /// <param name="MemberUniverse">The deduplicated named-individual member universe; zero off the enumeration-algebra habitat.</param>
    /// <param name="MemberSilences">One when the certifying face went silent on the member-universe bound; zero otherwise.</param>
    /// <param name="ClassSilences">One when the certifying face went silent on the signature-class bound; zero otherwise.</param>
    /// <param name="PairCount">The anchor-and-pair composition's pair count past the member window; zero when the composition did not resolve.</param>
    /// <param name="PairVectorCount">The vectors the pair sweep walked; zero on every dark, silent, and measurement-only pass.</param>
    /// <param name="PairSilences">One when the pair-composition tier went silent on the pair bound; zero otherwise.</param>
    private readonly record struct EnumerationCensusMeasurement(int MemberUniverse, int MemberSilences, int ClassSilences, int PairCount, int PairVectorCount, int PairSilences);

    /// <summary>
    /// Merges one modal-gadget face's window into the accumulated one. The
    /// assembly is a MERGE and never last-writer-wins: the window silences are the
    /// SUM of both faces' charges, and a field a face does not charge is left at
    /// its other face's value rather than clobbered with a default. The clash face
    /// charges window silences alone, since the step ceiling is the only bound it
    /// answers to.
    /// </summary>
    /// <param name="accumulated">The window accumulated so far.</param>
    /// <param name="contributed">The window one face contributed.</param>
    /// <returns>The merged window.</returns>
    private static ModalGadgetWindow MergesModalGadgetWindow(ModalGadgetWindow accumulated, ModalGadgetWindow contributed)
    {
        return new ModalGadgetWindow(
            contributed.FreeAtomCount == 0 ? accumulated.FreeAtomCount : contributed.FreeAtomCount,
            contributed.SignatureCount == 0 ? accumulated.SignatureCount : contributed.SignatureCount,
            contributed.NodesBuilt == 0 ? accumulated.NodesBuilt : contributed.NodesBuilt,
            accumulated.WindowSilences + contributed.WindowSilences);
    }

    /// <summary>Overlays the enumeration-CSP census window onto one decision's statistics: the clash-only face's measurement from the clausification, the certifying face's measurement from the pre-clausification pass, and the partition, gadget, spy-point, bijection-chain, told-ground-witness, repairing, modal-expansion, and nominal-pinned-role faces' measurements from the same pre-clausification pass — landed on every post-clausification context-arm exit, decisions and abstentions alike, so the census is always visible. The pre-clausification decided exits hand-build their records instead, having no clausification to read.</summary>
    /// <param name="statistics">The statistics to overlay.</param>
    /// <param name="clausification">The round's clausification, carrying the nominal window.</param>
    /// <param name="measurement">The Shape E census numbers.</param>
    /// <param name="partition">The Shape P census numbers.</param>
    /// <param name="gadget">The Shape G census numbers.</param>
    /// <param name="spyPoint">The Shape S census numbers.</param>
    /// <param name="bijectionChain">The Shape B census numbers.</param>
    /// <param name="toldGroundWitness">The Shape W census numbers.</param>
    /// <param name="repairing">The Shape R census numbers.</param>
    /// <param name="modalExpansion">The Shape M census numbers.</param>
    /// <param name="modalGadget">The Shape K census numbers, already merged across the two modal-gadget faces.</param>
    /// <param name="nominalPinnedRole">The Shape D census numbers.</param>
    /// <returns>The overlaid statistics.</returns>
    private static ContextSaturationStatistics WithEnumerationWindow(ContextSaturationStatistics statistics, ClausificationResult clausification, EnumerationCensusMeasurement measurement, PartitionCountingWindow partition, BooleanGadgetWindow gadget, SpyPointWindow spyPoint, BijectionChainWindow bijectionChain, ToldGroundWitnessWindow toldGroundWitness, RepairingWindow repairing, ModalExpansionWindow modalExpansion, ModalGadgetWindow modalGadget, NominalPinnedRoleWindow nominalPinnedRole)
    {
        return statistics with
        {
            NominalPinnedRoleMemberCount = nominalPinnedRole.MemberCount,
            NominalPinnedRolePinnedEdgeCount = nominalPinnedRole.PinnedEdgeCount,
            NominalPinnedRoleDeniedEdgeCount = nominalPinnedRole.DeniedEdgeCount,
            NominalPinnedRoleWindowExceededMembers = nominalPinnedRole.MemberSilences,
            ModalGadgetFreeAtomCount = modalGadget.FreeAtomCount,
            ModalGadgetSignatureCount = modalGadget.SignatureCount,
            ModalGadgetNodesBuilt = modalGadget.NodesBuilt,
            ModalGadgetWindowSilences = modalGadget.WindowSilences,
            ModalExpansionNodesSpawned = modalExpansion.NodesSpawned,
            ModalExpansionMaxDepthReached = modalExpansion.MaxDepthReached,
            ModalExpansionPeakLabelSize = modalExpansion.PeakLabelSize,
            ModalExpansionEdgesMaterialised = modalExpansion.EdgesMaterialised,
            ModalExpansionRuleApplications = modalExpansion.RuleApplications,
            ModalExpansionWindowSilences = modalExpansion.WindowSilences,
            RepairingCarrierCount = repairing.CarrierCount,
            RepairingCommittedEdgeCount = repairing.CommittedEdges,
            RepairingWindowExceededCarriers = repairing.WindowSilences,
            ToldGroundWitnessCarrierCount = toldGroundWitness.CarrierCount,
            ToldGroundWitnessEdgeCount = toldGroundWitness.EdgeCount,
            ToldGroundWitnessWindowExceededCarriers = toldGroundWitness.WindowSilences,
            BijectionChainClassCount = bijectionChain.ClassCount,
            BijectionChainConstraintCount = bijectionChain.ConstraintCount,
            BijectionChainWindowExceededClasses = bijectionChain.ClassSilences,
            SpyPointMemberCount = spyPoint.MemberCount,
            SpyPointCapBound = spyPoint.CapBound,
            SpyPointDemandBound = spyPoint.DemandBound,
            SpyPointWindowExceededMembers = spyPoint.MemberSilences,
            GadgetPropertyAtomCount = gadget.PropertyAtomCount,
            GadgetFreeClassAtomCount = gadget.FreeClassAtomCount,
            GadgetEvaluatedVectorCount = gadget.EvaluatedVectorCount,
            GadgetWindowExceededAtoms = gadget.AtomSilences,
            EnumerationWindowExceededChainHops = clausification.NominalWindow.ChainHopSilences,
            EnumerationWindowExceededPopulation = clausification.NominalWindow.PopulationSilences,
            EnumerationCountedPopulation = clausification.NominalWindow.CountedPopulation,
            EnumerationDistinctCliqueSize = clausification.NominalWindow.DistinctCliqueSize,
            EnumerationCapBound = clausification.NominalWindow.CapBound,
            EnumerationMemberUniverse = measurement.MemberUniverse,
            EnumerationWindowExceededMembers = measurement.MemberSilences,
            EnumerationWindowExceededClasses = measurement.ClassSilences,
            EnumerationPairCount = measurement.PairCount,
            EnumerationPairVectorCount = measurement.PairVectorCount,
            EnumerationWindowExceededPairs = measurement.PairSilences,
            PartitionAnchorCount = partition.AnchorCount,
            PartitionRestrictionCount = partition.RestrictionCount,
            PartitionCapBound = partition.CapBound,
            PartitionWindowExceededAnchors = partition.AnchorSilences,
            PartitionWindowExceededRestrictions = partition.RestrictionSilences,
        };
    }

    /// <summary>Builds the context statistics for a pre-engine enumeration-decider decision — the clash-only face's told clash or the certifying face's certificate: a context-decided record with every counter zero, no engine ran and no ground slice was populated (the nominal arm bypasses it whole). The face-discriminated counters overlay via <c>with</c>.</summary>
    /// <returns>The all-zero pre-engine statistics.</returns>
    private static ContextSaturationStatistics NominalClashStatistics()
    {
        return new ContextSaturationStatistics(
            ContextDecided: true,
            InferenceAttempts: 0,
            RuleApplications: 0,
            CoreApplications: 0,
            HyperApplications: 0,
            SuccApplications: 0,
            PredApplications: 0,
            ElimApplications: 0,
            EqApplications: 0,
            IneqApplications: 0,
            FactorApplications: 0,
            DataClashApplications: 0,
            JoinApplications: 0,
            RootSuccApplications: 0,
            RootPredApplications: 0,
            NomApplications: 0,
            ContextsCreated: 0,
            ContextsReused: 0,
            ClausesDerived: 0,
            ClausesEliminated: 0,
            MaxContextClauses: 0,
            PreMergeUnions: 0,
            GroundContextsCreated: 0,
            GroundEdgesSeeded: 0,
            GroundClashes: 0,
            GeneratedNominals: 0,
            MaxNominalLabelDepth: 0,
            RootContextClauses: 0,
            RootEdges: 0);
    }

    /// <summary>Builds the context statistics for a clausification-time ground-clash short-circuit: a context-decided record carrying the pre-merge union count and the single ground clash, every rule and context counter zero — no engine ran.</summary>
    /// <param name="clausification">The clausification whose ground clash decided the module.</param>
    /// <returns>The short-circuit statistics.</returns>
    private static ContextSaturationStatistics GroundClashStatistics(ClausificationResult clausification)
    {
        return new ContextSaturationStatistics(
            ContextDecided: true,
            InferenceAttempts: 0,
            RuleApplications: 0,
            CoreApplications: 0,
            HyperApplications: 0,
            SuccApplications: 0,
            PredApplications: 0,
            ElimApplications: 0,
            EqApplications: 0,
            IneqApplications: 0,
            FactorApplications: 0,
            DataClashApplications: 0,
            JoinApplications: 0,
            RootSuccApplications: 0,
            RootPredApplications: 0,
            NomApplications: 0,
            ContextsCreated: 0,
            ContextsReused: 0,
            ClausesDerived: 0,
            ClausesEliminated: 0,
            MaxContextClauses: 0,
            PreMergeUnions: clausification.PreMergeUnions,
            GroundContextsCreated: 0,
            GroundEdgesSeeded: 0,
            GroundClashes: 1,
            GeneratedNominals: 0,
            MaxNominalLabelDepth: 0,
            RootContextClauses: 0,
            RootEdges: 0);
    }

    /// <summary>The seam: decide an admitted module by context saturation, or delegate to the fallback oracle. A reached context verdict is the seam's answer; a non-admitted module and a budget-exhausted admitted module both delegate to the fallback, whose decision is returned reference-identical for a non-admitted module and with the spent saturation's totals reattached for a budget-exhausted one.</summary>
    private sealed class ContextSeam
    {
        /// <summary>The oracle for modules the engine does not admit.</summary>
        private DescriptionLogicDelegate Fallback { get; }

        /// <summary>The inference budget bounding each admitted module's saturation.</summary>
        private ReasoningBudget Budget { get; }

        /// <summary>The registered-datatype set the datatype sidecar consults where the family classifier abstains.</summary>
        private DatatypeRegistry Registry { get; }

        /// <summary>Initialises the seam with its fallback oracle, inference budget, and registered-datatype set.</summary>
        /// <param name="fallback">The oracle for non-admitted modules.</param>
        /// <param name="budget">The inference budget bounding each admitted module's saturation.</param>
        /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
        public ContextSeam(DescriptionLogicDelegate fallback, ReasoningBudget budget, DatatypeRegistry registry)
        {
            Fallback = fallback;
            Budget = budget;
            Registry = registry;
        }

        /// <summary>Decides the module by context saturation when the survey admits it and the second gate passes, or delegates it to the fallback oracle. A reached context verdict is returned as the answer; a non-admitted module delegates and the fallback's decision is returned unchanged; a budget-exhausted admitted module delegates and the fallback's decision is returned with the spent saturation's totals reattached; a module the off-fold equality backstop delegated carries the backstop's head count onto the fallback's decision for the corpus census.</summary>
        /// <param name="module">The module to decide.</param>
        /// <param name="cancellationToken">A token that aborts the decision.</param>
        /// <returns>The decision — the context verdict when the engine decided the module, otherwise the fallback's own (reference-identical for a non-admitted module, ContextTotals-reattached for a budget-exhausted one or a backstop-delegated one).</returns>
        public ValueTask<ModuleDecision> Decide(ReasoningModule module, CancellationToken cancellationToken)
        {
            if(TryDecideContext(module, Budget, includeSubsumptions: true, EqualityLowering.GeneralClause, NominalParamodulationScope.QueryScoped, RootPropagationRelevance.Unrestricted, RootContextTopology.SingleRoot, ContextEnumerationDecider, ContextRootKeyJoin, ContextRootDataObligations, Registry, engineProbe: null, progressSampler: null, out ModuleDecision? decision, out ContextSaturationStatistics? delegatedTotals, cancellationToken))
            {
                //A reached context verdict — or a clausification-time ground clash — is the seam's
                //answer. A budget exhaustion is not: in a composed production chain, foreclosing the
                //fallback on an admitted but budget-explosive module would make the composition
                //strictly weaker than the fallback alone on exactly the shapes the budget exists for,
                //so the seam composes the exhaustion into a delegation and carries the spent
                //saturation's totals onto the fallback's decision for the trace.
                if(decision.Outcome == ReasoningDecisionOutcome.AbstainedBudget)
                {
                    return DelegateWithReattachedTotals(module, decision.Statistics.ContextTotals, cancellationToken);
                }

                return ValueTask.FromResult(decision);
            }

            //A delegation the off-fold equality backstop drove carries its head count onto the
            //fallback's decision for the corpus census; every other delegation
            //returns the fallback's decision reference-identical, so a non-admitted module is
            //untouched.
            if(delegatedTotals is ContextSaturationStatistics latchTotals)
            {
                return DelegateWithReattachedTotals(module, latchTotals, cancellationToken);
            }

            return Fallback(module, cancellationToken);
        }

        /// <summary>Delegates the module to the fallback oracle and reattaches a spent context totals record to the fallback's decision — the spent saturation's own totals on a budget exhaustion, or the backstop's head count on an off-fold equality delegation — so the context observation stays visible on the trace alongside the fallback's own solver totals.</summary>
        /// <param name="module">The module the seam delegated.</param>
        /// <param name="contextTotals">The context totals to reattach (<see cref="ContextSaturationStatistics.ContextDecided"/> is <see langword="false"/>).</param>
        /// <param name="cancellationToken">A token that aborts the delegation.</param>
        /// <returns>The fallback's decision with <see cref="ReasoningDecisionStatistics.ContextTotals"/> set to the reattached totals.</returns>
        private async ValueTask<ModuleDecision> DelegateWithReattachedTotals(ReasoningModule module, ContextSaturationStatistics contextTotals, CancellationToken cancellationToken)
        {
            ModuleDecision fallback = await Fallback(module, cancellationToken).ConfigureAwait(false);

            return fallback with { Statistics = fallback.Statistics with { ContextTotals = contextTotals } };
        }
    }
}
