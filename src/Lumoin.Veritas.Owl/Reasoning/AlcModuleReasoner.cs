using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The in-library default behind the <see cref="DescriptionLogicDelegate"/>
/// seam: a worklist tableau deciding the ALC(H) fragment of a locality
/// module — consistency over TBox and ABox, plus module-local subsumptions
/// between named classes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Calculus.</b> Concepts normalise to negation normal form; the TBox
/// internalises into one concept every node carries (each inclusion
/// <c>C ⊑ D</c> contributes <c>¬C ⊔ D</c>); the tableau expands
/// conjunctions, branches disjunctions over an explicit choice stack with
/// state snapshots (copy-on-branch — module-bounded inputs make the copies
/// cheap), creates existential successors under dynamic equality double
/// (pairwise) blocking (the termination device for cyclic TBoxes, recomputed
/// as labels grow so a block never latches), and propagates universal
/// restrictions over the told role hierarchy across a role-directioned
/// bidirectional completion graph. A clash is an atom meeting
/// its negation, or ⊥. No call-stack recursion anywhere.
/// </para>
/// <para>
/// <b>Fragment honesty.</b> Axioms or expressions beyond ALC(H) —
/// cardinalities, inverses, chains, nominals, self-restrictions, keys,
/// data ranges, negative assertions, and any role position naming the
/// reserved <c>owl:topObjectProperty</c>/<c>owl:bottomObjectProperty</c>
/// (whose fixed universal/empty semantics lies outside the calculus) —
/// are named on
/// <see cref="ModuleVerdict.UnsupportedConstructs"/> and excluded: an
/// inconsistency found in the supported fragment condemns the whole
/// module; a consistency verdict is fragment-relative.
/// <c>SameIndividual</c> merges nodes up front, and
/// <c>DifferentIndividuals</c> clashes exactly when that pre-merge collapsed
/// an asserted-distinct pair onto one representative; otherwise it is vacuous.
/// </para>
/// <para>
/// <b>Sibling by seam.</b> This engine branches in-place; the SAT-backed
/// variant (per-world propositional abstraction over
/// <c>Core.Sat.SatSolver</c>, conflict clauses instead of snapshots) is
/// the recorded sibling for workloads where batched subsumption checking
/// dominates — behind this same delegate, per the whole-engine seam rule.
/// </para>
/// </remarks>
public static class AlcModuleReasoner
{
    /// <summary>The largest named-class signature for which module-local subsumptions enumerate; beyond it the verdict carries consistency only. Shared with the SAT-backed sibling so both engines sweep the same modules.</summary>
    internal const int SubsumptionSignatureCap = 16;

    /// <summary>
    /// Wraps <see cref="Decide"/> as the seam delegate. The delegate carries no
    /// work budget, so the snapshot engine — which runs no solver, hence reports
    /// zero solves and empty solver totals — decides every module or throws on
    /// cancellation, and never abstains. The budget-carrying
    /// <see cref="CreateDelegate(ReasoningBudget)"/> overload produces a delegate
    /// that abstains with a reason when the tableau's rule applications reach the
    /// bound.
    /// </summary>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate()
    {
        return CreateDelegate(DatatypeRegistry.Empty, ReasoningBudget.Unbounded);
    }

    /// <summary>
    /// Wraps <see cref="DecideModule(ReasoningModule, DatatypeRegistry, CancellationToken)"/> as the seam
    /// delegate, carrying the registered-datatype set as instance state on a <see cref="SnapshotDecisionSeam"/>
    /// so the delegate is a bound method group rather than a lambda closing over the registry. The delegate
    /// carries no work budget, so the decision never abstains.
    /// </summary>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DatatypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return CreateDelegate(registry, ReasoningBudget.Unbounded);
    }

    /// <summary>
    /// Wraps the snapshot decision as the seam delegate under a work budget, the budget-carrying counterpart
    /// of <see cref="CreateDelegate()"/>: a decision the delegate makes abstains with a reason when the
    /// tableau's rule applications reach <paramref name="budget"/>'s inference bound rather than search
    /// without end. <see cref="ReasoningBudget.Unbounded"/> reproduces the never-abstaining delegate.
    /// </summary>
    /// <param name="budget">The work-based bound applied to each decision the delegate makes.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(ReasoningBudget budget)
    {
        return CreateDelegate(DatatypeRegistry.Empty, budget);
    }

    /// <summary>
    /// Wraps the snapshot decision as the seam delegate carrying both a registered-datatype set and a work
    /// budget — the registry- and budget-carrying counterpart of <see cref="CreateDelegate(ReasoningBudget)"/>.
    /// The budget is instance state on a <see cref="SnapshotDecisionSeam"/> beside the registry, so the
    /// delegate is a bound method group rather than a lambda closing over the enclosing parameters.
    /// </summary>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains.</param>
    /// <param name="budget">The work-based bound applied to each decision the delegate makes; <see cref="ReasoningBudget.Unbounded"/> never abstains.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DatatypeRegistry registry, ReasoningBudget budget)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return new SnapshotDecisionSeam(registry, budget).Decide;
    }

    /// <summary>
    /// Wraps <see cref="DecideModule(ReasoningModule, DatatypeRegistry, ReasoningBudget, CancellationToken)"/>
    /// as the snapshot <see cref="DescriptionLogicDelegate"/>, carrying the registered-datatype set and the
    /// work budget as explicit state so the delegate is a bound method group rather than a lambda closing over
    /// the enclosing parameters.
    /// </summary>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults.</param>
    /// <param name="budget">The work-based bound applied to each decision.</param>
    private sealed class SnapshotDecisionSeam(DatatypeRegistry registry, ReasoningBudget budget)
    {
        /// <summary>The registered-datatype set the concrete-domain sidecar consults.</summary>
        private DatatypeRegistry Registry { get; } = registry;

        /// <summary>The work-based bound applied to each decision.</summary>
        private ReasoningBudget Budget { get; } = budget;

        /// <summary>Decides the module's ALC(H) fragment by the snapshot tableau under the seam's budget.</summary>
        /// <param name="module">The module to decide.</param>
        /// <param name="cancellationToken">A token to cancel the decision.</param>
        /// <returns>The module decision, or an abstention when the budget ran out before a verdict.</returns>
        public ValueTask<ModuleDecision> Decide(ReasoningModule module, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(DecideModule(module, Registry, Budget, cancellationToken));
        }
    }

    /// <summary>
    /// Decides the module's ALC(H) fragment: consistency, module-local
    /// subsumptions, and the named remainder beyond the calculus.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        //A verdict surface is unbounded: the zero inference bound never fires, so
        //the tableau never abstains and the discarded flag is statically false.
        return DecideCore(module, includeSubsumptions: true, DatatypeRegistry.Empty, ReasoningBudget.Unbounded, out _, out _, cancellationToken);
    }

    /// <summary>
    /// Decides the module's ALC(H) fragment consulting a registered-datatype set at the concrete-domain
    /// leaves — the registry-carrying counterpart of <see cref="Decide(ReasoningModule, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(ReasoningModule module, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        //A verdict surface is unbounded: the zero inference bound never fires, so
        //the tableau never abstains and the discarded flag is statically false.
        return DecideCore(module, includeSubsumptions: true, registry, ReasoningBudget.Unbounded, out _, out _, cancellationToken);
    }

    /// <summary>
    /// Decides the module's ALC(H) fragment for consistency only: the
    /// verdict's subsumption list stays empty whatever the signature size,
    /// and no per-pair subsumption tableaux run. The entry for callers that
    /// consume the consistency bit alone — a satisfiability or refutation
    /// check — where the pairwise sweep would be pure overhead.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        //A verdict surface is unbounded: the zero inference bound never fires, so
        //the tableau never abstains and the discarded flag is statically false.
        return DecideCore(module, includeSubsumptions: false, DatatypeRegistry.Empty, ReasoningBudget.Unbounded, out _, out _, cancellationToken);
    }

    /// <summary>
    /// Decides the module's ALC(H) consistency consulting a registered-datatype set at the concrete-domain
    /// leaves — the registry-carrying counterpart of
    /// <see cref="DecideConsistency(ReasoningModule, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(ReasoningModule module, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        //A verdict surface is unbounded: the zero inference bound never fires, so
        //the tableau never abstains and the discarded flag is statically false.
        return DecideCore(module, includeSubsumptions: false, registry, ReasoningBudget.Unbounded, out _, out _, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> — the
    /// verdict together with the tableau work it spent — the form the
    /// <see cref="DescriptionLogicDelegate"/> seam returns. The snapshot
    /// engine runs no solver, so the statistics carry no world solves and
    /// empty solver totals; the tableau totals carry the work.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The decision: the verdict and the tableau work it spent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, DatatypeRegistry.Empty, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> consulting a registered-datatype set at the
    /// concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="DecideModule(ReasoningModule, CancellationToken)"/>, the form the snapshot seam returns.
    /// Unbounded: the tableau decides or throws on cancellation, never abstains.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The decision: the verdict and the tableau work it spent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, DatatypeRegistry registry, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, registry, ReasoningBudget.Unbounded, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under a work budget — the verdict together
    /// with the tableau work it spent, or an abstention when the tableau's rule applications reach the budget's
    /// inference bound before a verdict. The budget-carrying counterpart of
    /// <see cref="DecideModule(ReasoningModule, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The work-based bound on the decision; <see cref="ReasoningBudget.Unbounded"/> never abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The decision: the verdict and the tableau work it spent, or a budget abstention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        return DecideModule(module, DatatypeRegistry.Empty, budget, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> under a work budget consulting a
    /// registered-datatype set at the concrete-domain leaves — the registry- and budget-carrying counterpart
    /// the snapshot seam returns. When the tableau's rule applications — across the consistency check and the
    /// subsumption sweep together — reach the budget's inference bound before a verdict, the whole decision
    /// abstains carrying the work it spent, never a partial verdict.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The work-based bound on the decision; <see cref="ReasoningBudget.Unbounded"/> never abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The decision: the verdict and the tableau work it spent, or a budget abstention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(ReasoningModule module, DatatypeRegistry registry, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(registry);

        ModuleVerdict verdict = DecideCore(module, includeSubsumptions: true, registry, budget, out AlcTableauStatistics statistics, out bool abstained, cancellationToken);

        //An exhausted budget makes the whole decision an abstention carrying the
        //spent tableau counters — the reason's evidence — never the placeholder
        //verdict DecideCore returns on that path.
        if(abstained)
        {
            return ModuleDecision.AbstainedOnBudget(new ReasoningDecisionStatistics(module.Axioms.Count, SolveCount: 0, SatSolveStatistics.Empty, statistics));
        }

        return ModuleDecision.Decided(verdict, new ReasoningDecisionStatistics(module.Axioms.Count, SolveCount: 0, SatSolveStatistics.Empty, statistics));
    }

    /// <summary>
    /// Decides the module for consistency only as a full <see cref="ModuleDecision"/> under a work budget —
    /// the consistency-only counterpart of <see cref="DecideModule(ReasoningModule, ReasoningBudget, CancellationToken)"/>.
    /// The subsumption list stays empty whatever the signature size — no per-pair subsumption sweep runs — so
    /// the tableau spends only its consistency-check rule applications. When those reach the budget's inference
    /// bound before a verdict, the decision abstains carrying the work it spent, never a partial verdict.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The work-based bound on the consistency check; <see cref="ReasoningBudget.Unbounded"/> never abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The consistency decision with an empty subsumption list, or a budget abstention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideConsistencyModule(ReasoningModule module, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        return DecideConsistencyModule(module, DatatypeRegistry.Empty, budget, cancellationToken);
    }

    /// <summary>
    /// Decides the module for consistency only as a full <see cref="ModuleDecision"/> under a work budget
    /// consulting a registered-datatype set at the concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="DecideConsistencyModule(ReasoningModule, ReasoningBudget, CancellationToken)"/>. The subsumption
    /// list stays empty whatever the signature size — no per-pair subsumption sweep runs — so the tableau spends
    /// only its consistency-check rule applications. When those reach the budget's inference bound before a
    /// verdict, the decision abstains carrying the work it spent, never a partial verdict.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The work-based bound on the consistency check; <see cref="ReasoningBudget.Unbounded"/> never abstains.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The consistency decision with an empty subsumption list, or a budget abstention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideConsistencyModule(ReasoningModule module, DatatypeRegistry registry, ReasoningBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(registry);

        ModuleVerdict verdict = DecideCore(module, includeSubsumptions: false, registry, budget, out AlcTableauStatistics statistics, out bool abstained, cancellationToken);

        //An exhausted budget makes the whole decision an abstention carrying the
        //spent tableau counters — the reason's evidence — never the placeholder
        //verdict DecideCore returns on that path.
        if(abstained)
        {
            return ModuleDecision.AbstainedOnBudget(new ReasoningDecisionStatistics(module.Axioms.Count, SolveCount: 0, SatSolveStatistics.Empty, statistics));
        }

        return ModuleDecision.Decided(verdict, new ReasoningDecisionStatistics(module.Axioms.Count, SolveCount: 0, SatSolveStatistics.Empty, statistics));
    }

    /// <summary>
    /// Names the module's beyond-fragment remainder without deciding
    /// satisfiability: the translation runs, the tableau does not. An empty
    /// result means every axiom lies inside the ALC(H) calculus, so a
    /// subsequent decision is whole-module rather than fragment-relative.
    /// </summary>
    /// <param name="module">The module to survey.</param>
    /// <returns>The named beyond-fragment constructs, empty when the module is wholly within the fragment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<string> Survey(ReasoningModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        //Survey names the beyond-fragment remainder, so it must present the
        //folded fragment view: a reserved shape the fold turns into a constant
        //is inside the fragment and must not be named as a gap.
        return Translate(ReservedVocabularyFold.Apply(module)).Unsupported;
    }

    /// <summary>Runs the tableau over the module's translation under a work budget, optionally following with the module-local subsumption sweep.</summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="includeSubsumptions">Whether to enumerate module-local subsumptions when the signature qualifies.</param>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains.</param>
    /// <param name="budget">The work-based bound on the decision, applied across the consistency check and the subsumption sweep together; <see cref="ReasoningBudget.Unbounded"/> never trips.</param>
    /// <param name="statistics">The tableau statistics the decision's runs accumulate into.</param>
    /// <param name="abstained">Set when the tableau's rule applications reached the budget before a verdict; the returned verdict is then a benign placeholder the caller must not surface — it belongs to an abstention.</param>
    /// <param name="cancellationToken">A token that aborts the tableau between rule applications.</param>
    /// <returns>The verdict, or a benign placeholder when <paramref name="abstained"/> is set.</returns>
    private static ModuleVerdict DecideCore(ReasoningModule module, bool includeSubsumptions, DatatypeRegistry registry, in ReasoningBudget budget, out AlcTableauStatistics statistics, out bool abstained, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        //Fold the reserved-vocabulary constant shapes before translating: a
        //restriction whose fixed-extension reserved property makes it
        //semantically owl:Thing or owl:Nothing becomes that constant, so the
        //decision reads a plain named-class reference instead of a reserved role
        //the translation would drop out of the fragment.
        module = ReservedVocabularyFold.Apply(module);

        Translation translation = Translate(module);

        statistics = AlcTableauStatistics.Empty;
        abstained = false;
        bool isConsistent = IsSatisfiable(translation.TBox, translation.AssertedConcepts, translation.Edges, translation.SuperRoles, translation.TransitiveRoles, translation.Box, registry, budget, ref statistics, ref abstained, out bool dataUndecided, out bool selfCertifiedDecided, cancellationToken);

        if(abstained)
        {
            //The consistency check reached the budget before a verdict: skip the
            //sweep and return a benign placeholder the caller discards for an
            //abstention carrying the spent statistics.
            return new ModuleVerdict(isConsistent, []);
        }

        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions = [];
        if(includeSubsumptions && isConsistent && translation.SignatureClasses.Count <= SubsumptionSignatureCap)
        {
            //A ⊑ B holds exactly when A ⊓ ¬B is unsatisfiable under the
            //TBox; every check is a fresh single-root tableau.
            foreach(Utf8String subClass in translation.SignatureClasses)
            {
                foreach(Utf8String superClass in translation.SignatureClasses)
                {
                    if(subClass.Equals(superClass))
                    {
                        continue;
                    }

                    Dictionary<Utf8String, List<AlcConcept>> root = new()
                    {
                        [Utf8Strings.From("?")] = [new AlcAtom(subClass), new AlcNot(new AlcAtom(superClass))],
                    };

                    if(!IsSatisfiable(translation.TBox, root, edges: [], translation.SuperRoles, translation.TransitiveRoles, translation.Box, registry, budget, ref statistics, ref abstained, out bool _, out bool _, cancellationToken))
                    {
                        subsumptions.Add((new NamedNode(subClass), new NamedNode(superClass)));
                    }

                    //A pair probe that reached the budget abstains the whole
                    //decision: break both sweep loops so no partial subsumption
                    //list is surfaced.
                    if(abstained)
                    {
                        break;
                    }
                }

                if(abstained)
                {
                    break;
                }
            }
        }

        //An undecided concrete-domain obligation on a consistent completion
        //scopes the verdict to the modelled fragment — the unmodelled datatype
        //decision could still make the module inconsistent — so it is named on
        //the remainder, never folded silently into a decided "consistent". A
        //delegate-backed registered datatype that decided an obligation names its
        //self-certified provenance on the same remainder channel.
        List<string> unsupported = [.. translation.Unsupported];
        if(isConsistent && dataUndecided)
        {
            unsupported.Add(DataRestrictionConsistency.UndecidedMarker);
        }

        if(selfCertifiedDecided)
        {
            unsupported.Add(DataRestrictionConsistency.SelfCertifiedMarker);
        }

        return new ModuleVerdict(isConsistent, subsumptions)
        {
            UnsupportedConstructs = unsupported,
        };
    }

    /// <summary>The module's ALC(H)+S reading: the internalized TBox, the ABox by individual, the role edges, the role hierarchy, the transitive roles, the named-class signature, and the remainder.</summary>
    /// <param name="TBox">The internalized TBox conjuncts every node carries.</param>
    /// <param name="AssertedConcepts">Per-individual asserted concepts.</param>
    /// <param name="Edges">The asserted role edges.</param>
    /// <param name="SuperRoles">Per-role reflexive-transitive told super-roles.</param>
    /// <param name="TransitiveRoles">The roles declared transitive (named, non-reserved) — the ∀⁺-rule re-propagates a universal along these.</param>
    /// <param name="SignatureClasses">The named classes of the supported fragment, in first-appearance order.</param>
    /// <param name="Unsupported">The named remainder beyond the calculus.</param>
    /// <param name="Box">The module's data-property RBox, decided at the sidecar leaves against the property hierarchy, functionality, and disjointness.</param>
    internal sealed record Translation(
        List<AlcConcept> TBox,
        Dictionary<Utf8String, List<AlcConcept>> AssertedConcepts,
        List<(Utf8String From, Utf8String Role, Utf8String To)> Edges,
        Dictionary<Utf8String, HashSet<Utf8String>> SuperRoles,
        HashSet<Utf8String> TransitiveRoles,
        List<Utf8String> SignatureClasses,
        List<string> Unsupported,
        DataPropertyBox Box);

    /// <summary>Translates the module's axioms into the ALC(H) reading, collecting what falls outside.</summary>
    /// <param name="module">The module.</param>
    /// <returns>The translation.</returns>
    internal static Translation Translate(ReasoningModule module)
    {
        List<AlcConcept> tbox = [];
        Dictionary<Utf8String, List<AlcConcept>> asserted = [];
        List<(Utf8String, Utf8String, Utf8String)> edges = [];
        List<(Utf8String Sub, Utf8String Super)> rolePairs = [];
        HashSet<Utf8String> transitiveRoles = [];
        List<string> unsupported = [];
        List<Utf8String> signature = [];
        HashSet<Utf8String> signatureSeen = [];
        Dictionary<Utf8String, Utf8String> merges = [];

        //The module's data-property RBox, built once from exactly the five
        //data-property axiom types plus DataPropertyRange. Empty when the module
        //carries none, so every sidecar leaf reduces to the property-in-isolation
        //check; the domain axiom reads its sub-property closure below.
        DataPropertyBox box = DataPropertyBox.Build(module.Axioms);

        //SameIndividual pre-merge: a tiny union-find over individual keys.
        foreach(OwlAxiom axiom in module.Axioms)
        {
            if(axiom is OwlSameIndividualAxiom same
                && TryIndividualKey(same.First, out Utf8String first)
                && TryIndividualKey(same.Second, out Utf8String second))
            {
                merges[Find(merges, first)] = Find(merges, second);
            }
        }

        foreach(OwlAxiom axiom in module.Axioms)
        {
            switch(axiom)
            {
                case OwlDeclarationAxiom or OwlImportAxiom or OwlAnnotationAssertionAxiom
                    or OwlSubAnnotationPropertyOfAxiom or OwlAnnotationPropertyDomainAxiom
                    or OwlAnnotationPropertyRangeAxiom or OwlSameIndividualAxiom:
                    break;
                case OwlDifferentIndividualsAxiom different:
                    //Asserted distinctness clashes exactly when the SameIndividual
                    //pre-merge collided the pair onto one representative; otherwise
                    //vacuous, no other merge source existing in ALC(H).
                    SeedDistinctnessClash(different, merges, asserted);

                    break;
                case OwlSubClassOfAxiom subClass when TryInclusion(subClass.SubClass, subClass.SuperClass, tbox, signature, signatureSeen):
                    break;
                case OwlEquivalentClassesAxiom equivalent
                    when TryInclusion(equivalent.First, equivalent.Second, tbox, signature, signatureSeen)
                        && TryInclusion(equivalent.Second, equivalent.First, tbox, signature, signatureSeen):
                    break;
                case OwlDisjointClassesAxiom disjoint when TryDisjointness(disjoint.Operands, tbox, signature, signatureSeen):
                    break;
                case OwlDisjointUnionAxiom disjointUnion when TryDisjointUnion(disjointUnion.Class, disjointUnion.Operands, tbox, signature, signatureSeen):
                    break;
                case OwlObjectPropertyDomainAxiom { Property.IsInverse: false } domain
                    when !IsReservedRole(domain.Property.Property.Iri)
                        && TryTranslate(domain.Domain, negate: false, signature, signatureSeen, out AlcConcept? domainConcept):
                    //domain(r, C) is ∃r.⊤ ⊑ C, internalized ∀r.⊥ ⊔ C.
                    tbox.Add(new AlcOr([new AlcForAll(AlcRole.Forward(domain.Property.Property.Iri), AlcBottom.Instance), domainConcept]));

                    break;
                case OwlObjectPropertyRangeAxiom { Property.IsInverse: false } range
                    when !IsReservedRole(range.Property.Property.Iri)
                        && TryTranslate(range.Range, negate: false, signature, signatureSeen, out AlcConcept? rangeConcept):
                    //range(r, C) is ⊤ ⊑ ∀r.C.
                    tbox.Add(new AlcForAll(AlcRole.Forward(range.Property.Property.Iri), rangeConcept));

                    break;
                case OwlSubObjectPropertyOfAxiom { SubProperty.IsInverse: false, SuperProperty.IsInverse: false } subRole
                    when !IsReservedRole(subRole.SubProperty.Property.Iri) && !IsReservedRole(subRole.SuperProperty.Property.Iri):
                    rolePairs.Add((subRole.SubProperty.Property.Iri, subRole.SuperProperty.Property.Iri));

                    break;
                case OwlEquivalentObjectPropertiesAxiom { First.IsInverse: false, Second.IsInverse: false } equivalentRoles
                    when !IsReservedRole(equivalentRoles.First.Property.Iri) && !IsReservedRole(equivalentRoles.Second.Property.Iri):
                    //Equivalent object properties are mutual told sub-roles: each
                    //includes the other, so every universal and existential
                    //restriction reaches across both through the role hierarchy.
                    rolePairs.Add((equivalentRoles.First.Property.Iri, equivalentRoles.Second.Property.Iri));
                    rolePairs.Add((equivalentRoles.Second.Property.Iri, equivalentRoles.First.Property.Iri));

                    break;
                case OwlObjectPropertyCharacteristicAxiom { Characteristic: OwlPropertyCharacteristic.Transitive, Property.IsInverse: false } transitive
                    when !IsReservedRole(transitive.Property.Property.Iri):
                    //A transitive role: recorded so the ∀⁺-rule re-propagates a
                    //universal restriction along it.
                    transitiveRoles.Add(transitive.Property.Property.Iri);
                    break;
                case OwlClassAssertionAxiom assertion
                    when TryIndividualKey(assertion.Individual, out Utf8String individual)
                        && TryTranslate(assertion.Class, negate: false, signature, signatureSeen, out AlcConcept? assertedConcept):
                    Append(asserted, Find(merges, individual), assertedConcept);

                    break;
                case OwlObjectPropertyAssertionAxiom roleAssertion
                    when !IsReservedRole(roleAssertion.Property.Iri)
                        && TryIndividualKey(roleAssertion.Source, out Utf8String source) && TryIndividualKey(roleAssertion.Target, out Utf8String target):
                    edges.Add((Find(merges, source), roleAssertion.Property.Iri, Find(merges, target)));

                    break;
                case OwlDataPropertyRangeAxiom dataRange when !IsReservedDataProperty(dataRange.Property.Iri):
                    //range(dp, R) is ⊤ ⊑ ∀dp.R — a global universal every node carries.
                    tbox.Add(new AlcDataAll(dataRange.Property.Iri, dataRange.Range));

                    break;
                case OwlDataPropertyAssertionAxiom dataAssertion
                    when !IsReservedDataProperty(dataAssertion.Property.Iri) && TryIndividualKey(dataAssertion.Source, out Utf8String dataSource):
                    //The individual has a dp-value equal to the asserted literal.
                    Append(asserted, Find(merges, dataSource), new AlcDataSome(dataAssertion.Property.Iri, new OwlDataOneOf([dataAssertion.Target])));

                    break;
                case OwlNegativeDataPropertyAssertionAxiom negativeData
                    when !IsReservedDataProperty(negativeData.Property.Iri) && TryIndividualKey(negativeData.Source, out Utf8String negativeSource):
                    //The individual has no dp-value equal to the literal: every dp-value avoids it.
                    Append(asserted, Find(merges, negativeSource), new AlcDataAll(negativeData.Property.Iri, new OwlDataComplementOf(new OwlDataOneOf([negativeData.Target]))));

                    break;
                case OwlDataPropertyDomainAxiom dataDomain
                    when !IsReservedDataProperty(dataDomain.Property.Iri)
                        && TryAddDataDomainGcis(dataDomain.Property.Iri, dataDomain.Domain, box, tbox, signature, signatureSeen):
                    //domain(dp, C) reasons in the DL core, not the sidecar, as the
                    //GCI SubClassOf(DataSomeValuesFrom(dp, rdfs:Literal), C) — added
                    //per sub-closure source below.
                    break;
                case OwlSubDataPropertyOfAxiom or OwlEquivalentDataPropertiesAxiom
                    or OwlFunctionalDataPropertyAxiom or OwlDisjointDataPropertiesAxiom:
                    //Recorded into the module's DataPropertyBox and decided at the
                    //sidecar leaves through the §1.3 procedure; no concept lowering.
                    //A DisjointDataProperties configuration outside the decided slice
                    //surfaces the sidecar's UndecidedMarker at verdict time, never a
                    //silent decisive consistent.
                    break;
                default:
                    unsupported.Add(axiom.GetType().Name);

                    break;
            }
        }

        //Reflexive-transitive told super-roles, iteratively to fixpoint.
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles = [];
        foreach((Utf8String sub, Utf8String super) in rolePairs)
        {
            SuperSetOf(superRoles, sub).Add(super);
        }

        bool grew = true;
        while(grew)
        {
            grew = false;
            foreach(KeyValuePair<Utf8String, HashSet<Utf8String>> entry in superRoles)
            {
                List<Utf8String> reachable = [];
                foreach(Utf8String super in entry.Value)
                {
                    if(superRoles.TryGetValue(super, out HashSet<Utf8String>? next))
                    {
                        reachable.AddRange(next);
                    }
                }

                foreach(Utf8String super in reachable)
                {
                    grew |= entry.Value.Add(super);
                }
            }
        }

        return new Translation(tbox, asserted, edges, superRoles, transitiveRoles, signature, unsupported, box);
    }

    /// <summary>
    /// Adds the domain GCIs for a <c>DataPropertyDomain(dp, C)</c> axiom: one
    /// internalized <c>SubClassOf(DataSomeValuesFrom(d′, rdfs:Literal), C)</c> per
    /// sub-closure source <c>d′ ⊑* dp</c>, reusing the existing negative-polarity
    /// <c>DataSomeValuesFrom</c> lowering. A node carrying any <c>d′</c>-demand
    /// closes the <c>∃d′.¬rdfs:Literal</c> disjunct through the sidecar — a demand's
    /// range conjoined with the negated <c>rdfs:Literal</c> is decided unsatisfiable
    /// by the checker's negated-<c>rdfs:Literal</c> rule — forcing <c>C</c>, so the
    /// domain fires through the demand's own property universal without a new tableau
    /// rule. Sources are staged so a domain outside the fragment leaves the TBox
    /// untouched and the axiom falls to the named remainder whole.
    /// </summary>
    /// <param name="property">The domain axiom's data property.</param>
    /// <param name="domain">The domain class expression.</param>
    /// <param name="box">The module's data-property RBox, read for the sub-property closure.</param>
    /// <param name="tbox">The TBox sink.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns><see langword="true"/> when the domain is within the fragment and the GCIs were added.</returns>
    private static bool TryAddDataDomainGcis(
        Utf8String property,
        OwlClassExpression domain,
        DataPropertyBox box,
        List<AlcConcept> tbox,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen)
    {
        List<Utf8String> sources = [];
        box.CollectSubClosureSources(property, sources);

        List<AlcConcept> staged = [];
        foreach(Utf8String source in sources)
        {
            OwlDataSomeValuesFrom literalDemand = new([new NamedNode(source)], RdfsLiteralRange);
            if(!TryInclusion(literalDemand, domain, staged, signature, signatureSeen))
            {
                return false;
            }
        }

        tbox.AddRange(staged);

        return true;
    }

    /// <summary>Adds the internalized inclusion <c>¬Sub ⊔ Super</c> when both sides translate.</summary>
    /// <param name="subClass">The subclass expression.</param>
    /// <param name="superClass">The superclass expression.</param>
    /// <param name="tbox">The TBox sink.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns><see langword="true"/> when both sides are in the fragment.</returns>
    private static bool TryInclusion(
        OwlClassExpression subClass,
        OwlClassExpression superClass,
        List<AlcConcept> tbox,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen)
    {
        if(TryTranslate(subClass, negate: true, signature, signatureSeen, out AlcConcept? negatedSub)
            && TryTranslate(superClass, negate: false, signature, signatureSeen, out AlcConcept? super))
        {
            tbox.Add(new AlcOr([negatedSub, super]));

            return true;
        }

        return false;
    }

    /// <summary>Adds the pairwise internalized disjointness <c>¬Ci ⊔ ¬Cj</c> when every operand translates.</summary>
    /// <param name="operands">The mutually disjoint expressions.</param>
    /// <param name="tbox">The TBox sink.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns><see langword="true"/> when the axiom is in the fragment.</returns>
    private static bool TryDisjointness(
        IReadOnlyList<OwlClassExpression> operands,
        List<AlcConcept> tbox,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen)
    {
        List<AlcConcept> negated = [];
        foreach(OwlClassExpression operand in operands)
        {
            if(!TryTranslate(operand, negate: true, signature, signatureSeen, out AlcConcept? negatedOperand))
            {
                return false;
            }

            negated.Add(negatedOperand);
        }

        for(int i = 0; i < negated.Count; i++)
        {
            for(int j = i + 1; j < negated.Count; j++)
            {
                tbox.Add(new AlcOr([negated[i], negated[j]]));
            }
        }

        return true;
    }

    /// <summary>
    /// Translates a disjoint union <c>C ≡ D₁ ⊔ … ⊔ Dₙ</c> whose members are
    /// pairwise disjoint: the equivalence each way (so an instance of the class
    /// is one of the members and each member is the class) and the pairwise
    /// disjointness of the members, all staged into a private TBox so an operand
    /// outside the fragment leaves the shared TBox untouched and the axiom falls
    /// to the named remainder whole rather than half-internalized.
    /// </summary>
    /// <param name="unionClass">The named class the union defines.</param>
    /// <param name="operands">The union's member expressions.</param>
    /// <param name="tbox">The TBox sink.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns><see langword="true"/> when every member is within the fragment.</returns>
    private static bool TryDisjointUnion(
        NamedNode unionClass,
        IReadOnlyList<OwlClassExpression> operands,
        List<AlcConcept> tbox,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen)
    {
        OwlClassReference classReference = new(unionClass);
        OwlObjectUnionOf union = new(operands);

        List<AlcConcept> staged = [];
        if(TryInclusion(classReference, union, staged, signature, signatureSeen)
            && TryInclusion(union, classReference, staged, signature, signatureSeen)
            && TryDisjointness(operands, staged, signature, signatureSeen))
        {
            tbox.AddRange(staged);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Translates a class expression to NNF with an explicit post-order
    /// stack: polarity flows down, concepts assemble on the way up.
    /// </summary>
    /// <param name="root">The expression to translate.</param>
    /// <param name="negate">Whether the expression occurs under negation.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <param name="concept">The translated concept.</param>
    /// <returns><see langword="false"/> when the expression leaves the ALC fragment.</returns>
    private static bool TryTranslate(
        OwlClassExpression root,
        bool negate,
        List<Utf8String> signature,
        HashSet<Utf8String> signatureSeen,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AlcConcept? concept)
    {
        Dictionary<(OwlClassExpression, bool), AlcConcept> results = [];
        Stack<(OwlClassExpression Node, bool Negate, bool ChildrenDone)> work = new();
        work.Push((root, negate, false));

        while(work.Count > 0)
        {
            (OwlClassExpression node, bool polarity, bool childrenDone) = work.Pop();
            if(results.ContainsKey((node, polarity)))
            {
                continue;
            }

            if(!childrenDone)
            {
                work.Push((node, polarity, true));
                switch(node)
                {
                    case OwlObjectIntersectionOf intersection:
                        foreach(OwlClassExpression operand in intersection.Operands)
                        {
                            work.Push((operand, polarity, false));
                        }

                        break;
                    case OwlObjectUnionOf union:
                        foreach(OwlClassExpression operand in union.Operands)
                        {
                            work.Push((operand, polarity, false));
                        }

                        break;
                    case OwlObjectComplementOf complement:
                        work.Push((complement.Operand, !polarity, false));

                        break;
                    case OwlObjectSomeValuesFrom { Property.IsInverse: false } some:
                        work.Push((some.Filler, polarity, false));

                        break;
                    case OwlObjectAllValuesFrom { Property.IsInverse: false } all:
                        work.Push((all.Filler, polarity, false));

                        break;
                    default:
                        break;
                }

                continue;
            }

            AlcConcept? translated = node switch
            {
                OwlClassReference reference => TranslateReference(reference, polarity, signature, signatureSeen),
                OwlObjectIntersectionOf intersection => CombineOperands(intersection.Operands, polarity, isIntersection: true, results),
                OwlObjectUnionOf union => CombineOperands(union.Operands, polarity, isIntersection: false, results),
                OwlObjectComplementOf complement => results.TryGetValue((complement.Operand, !polarity), out AlcConcept? inner) ? inner : null,
                OwlObjectSomeValuesFrom { Property.IsInverse: false } some
                    when !IsReservedRole(some.Property.Property.Iri)
                        && results.TryGetValue((some.Filler, polarity), out AlcConcept? filler) =>
                        polarity ? new AlcForAll(AlcRole.Forward(some.Property.Property.Iri), filler) : new AlcExists(AlcRole.Forward(some.Property.Property.Iri), filler),
                OwlObjectAllValuesFrom { Property.IsInverse: false } all
                    when !IsReservedRole(all.Property.Property.Iri)
                        && results.TryGetValue((all.Filler, polarity), out AlcConcept? filler) =>
                        polarity ? new AlcExists(AlcRole.Forward(all.Property.Property.Iri), filler) : new AlcForAll(AlcRole.Forward(all.Property.Property.Iri), filler),
                OwlDataSomeValuesFrom { Properties.Count: 1 } dataSome when !IsReservedDataProperty(dataSome.Properties[0].Iri) =>
                    polarity
                        ? new AlcDataAll(dataSome.Properties[0].Iri, new OwlDataComplementOf(dataSome.Range))
                        : new AlcDataSome(dataSome.Properties[0].Iri, dataSome.Range),
                OwlDataAllValuesFrom { Properties.Count: 1 } dataAll when !IsReservedDataProperty(dataAll.Properties[0].Iri) =>
                    polarity
                        ? new AlcDataSome(dataAll.Properties[0].Iri, new OwlDataComplementOf(dataAll.Range))
                        : new AlcDataAll(dataAll.Properties[0].Iri, dataAll.Range),
                OwlDataHasValue dataHas when !IsReservedDataProperty(dataHas.Property.Iri) =>
                    polarity
                        ? new AlcDataAll(dataHas.Property.Iri, new OwlDataComplementOf(new OwlDataOneOf([dataHas.Value])))
                        : new AlcDataSome(dataHas.Property.Iri, new OwlDataOneOf([dataHas.Value])),
                //The positive minimum, maximum, and exact forms enter the fragment;
                //an exact bound splits into its two halves, and the sidecar's
                //max-slot pool discharges the maximum. A negated data cardinality
                //forces value merging (the choose-rule cliff) and stays out.
                OwlDataCardinality { Kind: OwlCardinalityKind.Min } dataMin
                    when !polarity && dataMin.Cardinality >= 0 && !IsReservedDataProperty(dataMin.Property.Iri) =>
                    new AlcDataMinCard(dataMin.Cardinality, dataMin.Property.Iri, dataMin.Range ?? RdfsLiteralRange),
                OwlDataCardinality { Kind: OwlCardinalityKind.Max } dataMax
                    when !polarity && dataMax.Cardinality >= 1 && !IsReservedDataProperty(dataMax.Property.Iri) =>
                    new AlcDataMaxCard(dataMax.Cardinality, dataMax.Property.Iri, dataMax.Range ?? RdfsLiteralRange),
                OwlDataCardinality { Kind: OwlCardinalityKind.Exact } dataExact
                    when !polarity && dataExact.Cardinality >= 1 && !IsReservedDataProperty(dataExact.Property.Iri) =>
                    new AlcAnd(
                    [
                        new AlcDataMinCard(dataExact.Cardinality, dataExact.Property.Iri, dataExact.Range ?? RdfsLiteralRange),
                        new AlcDataMaxCard(dataExact.Cardinality, dataExact.Property.Iri, dataExact.Range ?? RdfsLiteralRange),
                    ]),
                _ => null
            };

            if(translated is null)
            {
                concept = null;

                return false;
            }

            results[(node, polarity)] = translated;
        }

        concept = results[(root, negate)];

        return true;
    }

    /// <summary>Translates a named class under the polarity, recording it in the signature; the built-ins keep their fixed reading.</summary>
    /// <param name="reference">The class reference.</param>
    /// <param name="negate">Whether the reference occurs under negation.</param>
    /// <param name="signature">The named-class signature sink.</param>
    /// <param name="signatureSeen">The signature dedup set.</param>
    /// <returns>The concept.</returns>
    private static AlcConcept TranslateReference(OwlClassReference reference, bool negate, List<Utf8String> signature, HashSet<Utf8String> signatureSeen)
    {
        Utf8String iri = reference.Class.Iri;
        if(iri.Equals(OwlVocabulary.Thing))
        {
            return negate ? AlcBottom.Instance : AlcTop.Instance;
        }

        if(iri.Equals(OwlVocabulary.Nothing))
        {
            return negate ? AlcTop.Instance : AlcBottom.Instance;
        }

        if(signatureSeen.Add(iri))
        {
            signature.Add(iri);
        }

        AlcAtom atom = new(iri);

        return negate ? new AlcNot(atom) : atom;
    }

    /// <summary>Assembles a boolean node from its translated operands; polarity has already dualized the connective.</summary>
    /// <param name="operands">The source operands.</param>
    /// <param name="polarity">The polarity the operands translated under.</param>
    /// <param name="isIntersection">Whether the source connective is an intersection.</param>
    /// <param name="results">The memo of translated children.</param>
    /// <returns>The combined concept, or <see langword="null"/> when a child fell outside the fragment.</returns>
    private static AlcConcept? CombineOperands(
        IReadOnlyList<OwlClassExpression> operands,
        bool polarity,
        bool isIntersection,
        Dictionary<(OwlClassExpression, bool), AlcConcept> results)
    {
        List<AlcConcept> translated = [];
        foreach(OwlClassExpression operand in operands)
        {
            if(!results.TryGetValue((operand, polarity), out AlcConcept? child))
            {
                return null;
            }

            translated.Add(child);
        }

        //De Morgan: a negated intersection is the union of negations. An
        //empty operand list is no class expression at all.
        bool conjunction = isIntersection != polarity;

        return translated.Count switch
        {
            0 => null,
            1 => translated[0],
            _ => conjunction ? new AlcAnd(translated) : new AlcOr(translated)
        };
    }

    /// <summary>
    /// The tableau: deterministic rule scan, copy-on-branch disjunctions,
    /// dynamic equality double (pairwise) blocking on existential expansion,
    /// universal propagation over the told role hierarchy across a
    /// role-directioned bidirectional completion graph, and the ∀⁺-rule that
    /// re-propagates a universal along transitive roles. The run abstains —
    /// setting <paramref name="abstained"/>, folding its spent counters into
    /// <paramref name="statistics"/> as either settled exit does, and returning
    /// the benign satisfiable value — when its rule applications added to the
    /// accumulator's reach <paramref name="budget"/>'s inference bound, so the
    /// decision stops rather than search without end.
    /// </summary>
    /// <param name="tbox">The internalized TBox conjuncts.</param>
    /// <param name="assertedConcepts">Per-individual asserted concepts.</param>
    /// <param name="edges">The asserted role edges.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <param name="transitiveRoles">The roles declared transitive, driving the ∀⁺-rule.</param>
    /// <param name="box">The module's data-property RBox, decided at the concrete-domain leaves.</param>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains.</param>
    /// <param name="budget">The work-based bound on the whole decision; the accumulated rule applications this run checks against it span the prior runs recorded in <paramref name="statistics"/> plus this run's own. <see cref="ReasoningBudget.Unbounded"/> never trips.</param>
    /// <param name="statistics">The running tableau statistics this run folds its counters into, and the prior-run accumulator the budget check reads.</param>
    /// <param name="abstained">Carries the decision's abstention latch across runs; already set on entry short-circuits this run to the benign satisfiable value, and this run sets it when it reaches the budget.</param>
    /// <param name="dataUndecided">Set when the accepted completion carried a concrete-domain obligation the checker could not decide, so a satisfiable verdict is fragment-relative.</param>
    /// <param name="selfCertifiedDecided">Set when a delegate-backed registered datatype decided one of the completion's obligations, so the verdict names <see cref="DataRestrictionConsistency.SelfCertifiedMarker"/> on the remainder.</param>
    /// <param name="cancellationToken">A token that aborts between rule applications.</param>
    /// <returns><see langword="true"/> when a clash-free fully expanded tableau exists, or the benign <see langword="true"/> when the run abstained.</returns>
    private static bool IsSatisfiable(
        List<AlcConcept> tbox,
        Dictionary<Utf8String, List<AlcConcept>> assertedConcepts,
        List<(Utf8String From, Utf8String Role, Utf8String To)> edges,
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles,
        HashSet<Utf8String> transitiveRoles,
        DataPropertyBox box,
        DatatypeRegistry registry,
        in ReasoningBudget budget,
        ref AlcTableauStatistics statistics,
        ref bool abstained,
        out bool dataUndecided,
        out bool selfCertifiedDecided,
        CancellationToken cancellationToken)
    {
        dataUndecided = false;
        selfCertifiedDecided = false;

        //Seed the forest: one node per asserted individual (or a single
        //anonymous root for a pure TBox check), every label opening with
        //the TBox conjuncts. The completion graph is bidirectional —
        //each node carries its role-directioned neighbours (an asserted
        //edge contributes a forward entry at its source and the inverse
        //entry at its target) so a universal can propagate either way once
        //inverse roles enter the fragment; parentRole records the directioned
        //role of each tree node's parent edge, which double blocking compares.
        Dictionary<Utf8String, int> nodeOf = [];
        List<List<AlcConcept>> labels = [];
        List<int> parents = [];
        List<AlcRole> parentRole = [];
        List<List<(int Target, AlcRole Role)>> neighbours = [];

        int NodeFor(Utf8String individual)
        {
            if(!nodeOf.TryGetValue(individual, out int index))
            {
                index = labels.Count;
                nodeOf[individual] = index;
                labels.Add([.. tbox]);
                parents.Add(-1);
                parentRole.Add(default);
                neighbours.Add([]);
            }

            return index;
        }

        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in assertedConcepts)
        {
            int node = NodeFor(entry.Key);
            foreach(AlcConcept concept in entry.Value)
            {
                AddConcept(labels[node], concept);
            }
        }

        foreach((Utf8String from, Utf8String role, Utf8String to) in edges)
        {
            int fromNode = NodeFor(from);
            int toNode = NodeFor(to);
            AlcRole forward = AlcRole.Forward(role);
            neighbours[fromNode].Add((toNode, forward));
            neighbours[toNode].Add((fromNode, forward.Inverse()));
        }

        if(labels.Count == 0)
        {
            labels.Add([.. tbox]);
            parents.Add(-1);
            parentRole.Add(default);
            neighbours.Add([]);
        }

        //The choice stack: each entry restores its snapshot and tries the
        //next disjunct of the branch that created it. The snapshot copies
        //every piece of forest state — labels, tree parents, parent-edge
        //roles, and the bidirectional neighbour graph — so a backtrack
        //restores the branch point whole rather than over stale adjacency.
        List<(List<List<AlcConcept>> Labels, List<int> Parents, List<AlcRole> ParentRole, List<List<(int Target, AlcRole Role)>> Neighbours, int Node, List<AlcConcept> Remaining)> choices = [];

        //The run's counters, folded into the running total at either exit.
        long ruleApplications = 0;
        int branches = 0;
        int clashes = 0;
        int maxNodes = labels.Count;

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //Budget check at the loop head: the accumulated rule applications span
            //the whole decision — the prior runs folded into statistics plus this
            //run's own — so an exhausted budget latches the abstention, folds this
            //run's spent counters exactly as either settled exit does, and returns
            //the benign satisfiable value.
            if(abstained || budget.IsExhaustedByInferences(statistics.RuleApplications + ruleApplications))
            {
                abstained = true;
                statistics = AlcTableauStatistics.Combine(statistics, new AlcTableauStatistics(TableauRuns: 1, ruleApplications, branches, clashes, maxNodes));

                return true;
            }

            if(labels.Count > maxNodes)
            {
                maxNodes = labels.Count;
            }

            //Blocking is dynamic: recomputed from the current labels every
            //iteration, never latched, so a label that grows past a block
            //reopens the node and one that settles into a pairwise repeat
            //closes it.
            long blockingStart = ReasoningInstrumentation.Begin();
            byte[] blockingStatus = ComputeBlockingStatus(labels, parents, parentRole);
            ReasoningInstrumentation.End(ReasoningPhase.Blocking, blockingStart);

            bool clashed = HasClash(labels, blockingStatus);
            if(!clashed)
            {
                int choicesBefore = choices.Count;
                long ruleStart = ReasoningInstrumentation.Begin();
                bool ruleApplied = ApplyOneRule(labels, parents, parentRole, neighbours, superRoles, transitiveRoles, tbox, blockingStatus, choices);
                ReasoningInstrumentation.End(ReasoningPhase.TableauRule, ruleStart);
                if(ruleApplied)
                {
                    ruleApplications++;
                    if(choices.Count > choicesBefore)
                    {
                        branches++;
                    }

                    continue;
                }

                //No logical rule applies: the completion's concrete-domain
                //obligations decide whether it is a model. An unsatisfiable data
                //node clashes and backtracks; an undecided one does not close the
                //branch but scopes the satisfiable verdict to the modelled subset.
                //An indirectly-blocked node lies under a blocked ancestor and is
                //not part of the model, so its data obligations are inert.
                long dataStart = ReasoningInstrumentation.Begin();
                DataConsistencyStatus dataStatus = DataRestrictionConsistency.DecideForest(ActiveLabels(labels, blockingStatus), box, registry, out bool nodeSelfCertified);
                ReasoningInstrumentation.End(ReasoningPhase.DataConsistency, dataStart);
                clashed = dataStatus == DataConsistencyStatus.Clash;
                dataUndecided |= dataStatus == DataConsistencyStatus.Undecided;
                selfCertifiedDecided |= nodeSelfCertified;

                if(!clashed)
                {
                    statistics = AlcTableauStatistics.Combine(statistics, new AlcTableauStatistics(TableauRuns: 1, ruleApplications, branches, clashes, maxNodes));

                    return true;
                }
            }

            //A logical or concrete-domain clash: backtrack to the last open choice.
            clashes++;
            bool restored = false;
            while(choices.Count > 0)
            {
                //The same budget check at the backtrack loop head: a long backtrack
                //chain otherwise invisible between loop-A iterations still stops on
                //an exhausted budget, folding this run's counters and returning the
                //benign satisfiable value.
                if(abstained || budget.IsExhaustedByInferences(statistics.RuleApplications + ruleApplications))
                {
                    abstained = true;
                    statistics = AlcTableauStatistics.Combine(statistics, new AlcTableauStatistics(TableauRuns: 1, ruleApplications, branches, clashes, maxNodes));

                    return true;
                }

                (List<List<AlcConcept>> snapshotLabels, List<int> snapshotParents, List<AlcRole> snapshotParentRole, List<List<(int Target, AlcRole Role)>> snapshotNeighbours, int node, List<AlcConcept> remaining) = choices[^1];
                if(remaining.Count == 0)
                {
                    choices.RemoveAt(choices.Count - 1);

                    continue;
                }

                AlcConcept next = remaining[0];
                remaining.RemoveAt(0);
                (labels, parents, parentRole, neighbours) = Clone(snapshotLabels, snapshotParents, snapshotParentRole, snapshotNeighbours);
                AddConcept(labels[node], next);
                restored = true;

                break;
            }

            if(!restored)
            {
                statistics = AlcTableauStatistics.Combine(statistics, new AlcTableauStatistics(TableauRuns: 1, ruleApplications, branches, clashes, maxNodes));

                return false;
            }
        }
    }

    /// <summary>The labels of the nodes that are part of the model being built — every node except the indirectly-blocked ones, whose obligations are inert because they lie under a blocked ancestor.</summary>
    /// <param name="labels">Per-node labels.</param>
    /// <param name="blockingStatus">Per-node blocking status: 0 not blocked, 1 directly blocked, 2 indirectly blocked.</param>
    /// <returns>The active node labels.</returns>
    private static List<List<AlcConcept>> ActiveLabels(List<List<AlcConcept>> labels, byte[] blockingStatus)
    {
        List<List<AlcConcept>> active = new(labels.Count);
        for(int node = 0; node < labels.Count; node++)
        {
            if(blockingStatus[node] != IndirectlyBlocked)
            {
                active.Add(labels[node]);
            }
        }

        return active;
    }

    /// <summary>
    /// Applies the first applicable rule in deterministic node and label
    /// order: conjunction decomposition, disjunction branching (recording
    /// the choice point), universal propagation including the ∀⁺-rule over
    /// transitive roles, then blocked-aware existential expansion. The
    /// non-generating rules (⊓, ⊔, ∀, ∀⁺) skip a node only when it is
    /// indirectly blocked; the generating existential rule additionally skips
    /// a directly-blocked node — its obligations are mirrored by its blocker.
    /// </summary>
    /// <param name="labels">Per-node concept labels.</param>
    /// <param name="parents">Per-node tree parents; -1 for roots.</param>
    /// <param name="parentRole">Per-node directioned parent-edge role; unread for roots.</param>
    /// <param name="neighbours">Per-node role-directioned neighbours, both directions.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <param name="transitiveRoles">The roles declared transitive, driving the ∀⁺-rule.</param>
    /// <param name="tbox">The internalized TBox conjuncts new nodes open with.</param>
    /// <param name="blockingStatus">Per-node blocking status for this scan: 0 not blocked, 1 directly blocked, 2 indirectly blocked.</param>
    /// <param name="choices">The choice stack disjunction branching records onto.</param>
    /// <returns><see langword="false"/> when no rule applies — the tableau is fully expanded.</returns>
    private static bool ApplyOneRule(
        List<List<AlcConcept>> labels,
        List<int> parents,
        List<AlcRole> parentRole,
        List<List<(int Target, AlcRole Role)>> neighbours,
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles,
        HashSet<Utf8String> transitiveRoles,
        List<AlcConcept> tbox,
        byte[] blockingStatus,
        List<(List<List<AlcConcept>> Labels, List<int> Parents, List<AlcRole> ParentRole, List<List<(int Target, AlcRole Role)>> Neighbours, int Node, List<AlcConcept> Remaining)> choices)
    {
        for(int node = 0; node < labels.Count; node++)
        {
            //An indirectly-blocked node lies under a blocked ancestor and is
            //not part of the model: no rule applies to it.
            if(blockingStatus[node] == IndirectlyBlocked)
            {
                continue;
            }

            bool directlyBlocked = blockingStatus[node] == DirectlyBlocked;
            List<AlcConcept> label = labels[node];
            for(int i = 0; i < label.Count; i++)
            {
                switch(label[i])
                {
                    case AlcAnd and:
                    {
                        bool added = false;
                        foreach(AlcConcept operand in and.Operands)
                        {
                            added |= AddConcept(label, operand);
                        }

                        if(added)
                        {
                            return true;
                        }

                        break;
                    }
                    case AlcOr or:
                    {
                        bool anyPresent = false;
                        foreach(AlcConcept operand in or.Operands)
                        {
                            anyPresent |= Contains(label, operand);
                        }

                        if(!anyPresent)
                        {
                            //Branch: snapshot the state, apply the first
                            //disjunct, queue the rest.
                            List<AlcConcept> remaining = [.. or.Operands];
                            AlcConcept first = remaining[0];
                            remaining.RemoveAt(0);
                            (List<List<AlcConcept>> snapshotLabels, List<int> snapshotParents, List<AlcRole> snapshotParentRole, List<List<(int Target, AlcRole Role)>> snapshotNeighbours) = Clone(labels, parents, parentRole, neighbours);
                            choices.Add((snapshotLabels, snapshotParents, snapshotParentRole, snapshotNeighbours, node, remaining));
                            AddConcept(label, first);

                            return true;
                        }

                        break;
                    }
                    case AlcForAll forAll:
                    {
                        foreach((int target, AlcRole role) in neighbours[node])
                        {
                            if(!Reaches(role, forAll.Role, superRoles))
                            {
                                continue;
                            }

                            if(AddConcept(labels[target], forAll.Filler))
                            {
                                return true;
                            }

                            if(transitiveRoles.Count > 0 && PropagateTransitiveUniversal(role, forAll, labels[target], superRoles, transitiveRoles))
                            {
                                return true;
                            }
                        }

                        break;
                    }
                    case AlcExists exists when !directlyBlocked:
                    {
                        bool satisfiedByExisting = false;
                        foreach((int target, AlcRole role) in neighbours[node])
                        {
                            if(Reaches(role, exists.Role, superRoles) && Contains(labels[target], exists.Filler))
                            {
                                satisfiedByExisting = true;

                                break;
                            }
                        }

                        if(!satisfiedByExisting)
                        {
                            int successor = labels.Count;
                            List<AlcConcept> successorLabel = [.. tbox];
                            AddConcept(successorLabel, exists.Filler);
                            labels.Add(successorLabel);
                            parents.Add(node);
                            parentRole.Add(exists.Role);
                            neighbours.Add([]);

                            //The ordered pair (node, successor) realises the role;
                            //the successor reaches node back over the inverse role.
                            neighbours[node].Add((successor, exists.Role));
                            neighbours[successor].Add((node, exists.Role.Inverse()));

                            return true;
                        }

                        break;
                    }
                    default:
                        break;
                }
            }
        }

        return false;
    }

    /// <summary>The blocking status of a node that is not blocked: every rule may apply to it.</summary>
    private const byte NotBlocked = 0;

    /// <summary>The blocking status of a directly-blocked node: a pairwise-equal ancestor witnesses it, so it generates no successors but its label is still completed.</summary>
    private const byte DirectlyBlocked = 1;

    /// <summary>The blocking status of an indirectly-blocked node: a blocked ancestor lies above it, so it is not part of the model and no rule applies to it.</summary>
    private const byte IndirectlyBlocked = 2;

    /// <summary>
    /// Computes each node's blocking status for the current labels — the
    /// dynamic equality double (pairwise) blocking that terminates cyclic TBox
    /// expansion with inverse-safe semantics. A blockable (non-root) node is
    /// <see cref="DirectlyBlocked"/> by a proper ancestor when their labels are
    /// equal, their parents' labels are equal, and their parent-edge roles
    /// match; it is <see cref="IndirectlyBlocked"/> when a proper ancestor is
    /// itself blocked. Roots never block. Nodes are processed in index order, so
    /// every ancestor's status is settled before its descendant's — index order
    /// is the tree's topological order because a successor always takes a higher
    /// index than its parent.
    /// </summary>
    /// <param name="labels">Per-node labels.</param>
    /// <param name="parents">Per-node tree parents; -1 for roots.</param>
    /// <param name="parentRole">Per-node directioned parent-edge role; unread for roots.</param>
    /// <returns>Per-node status: <see cref="NotBlocked"/>, <see cref="DirectlyBlocked"/>, or <see cref="IndirectlyBlocked"/>.</returns>
    private static byte[] ComputeBlockingStatus(List<List<AlcConcept>> labels, List<int> parents, List<AlcRole> parentRole)
    {
        byte[] status = new byte[labels.Count];
        for(int node = 0; node < labels.Count; node++)
        {
            if(parents[node] < 0)
            {
                status[node] = NotBlocked;

                continue;
            }

            //Indirectly blocked when a proper ancestor is blocked at all.
            bool ancestorBlocked = false;
            for(int ancestor = parents[node]; ancestor >= 0; ancestor = parents[ancestor])
            {
                if(status[ancestor] != NotBlocked)
                {
                    ancestorBlocked = true;

                    break;
                }
            }

            if(ancestorBlocked)
            {
                status[node] = IndirectlyBlocked;

                continue;
            }

            //Directly blocked when an unblocked, blockable proper ancestor
            //pairwise-matches: equal labels, equal parent labels, equal
            //parent-edge role. The ancestors here are all unblocked, so each is
            //a sound blocker candidate.
            byte resolved = NotBlocked;
            for(int ancestor = parents[node]; ancestor >= 0; ancestor = parents[ancestor])
            {
                if(parents[ancestor] >= 0
                    && parentRole[node].Equals(parentRole[ancestor])
                    && LabelsEqual(labels[node], labels[ancestor])
                    && LabelsEqual(labels[parents[node]], labels[parents[ancestor]]))
                {
                    resolved = DirectlyBlocked;

                    break;
                }
            }

            status[node] = resolved;
        }

        return status;
    }

    /// <summary>
    /// Whether two node labels denote the same set of concepts. Every label
    /// opens with an identical copy of the internalized TBox conjuncts and grows
    /// only through <see cref="AddConcept"/>, which appends a concept solely when
    /// the label does not already contain it; so a label is that shared prefix
    /// followed by additions disjoint from it. Because the prefix is identical
    /// across labels, equal element count with one-way containment is set
    /// equality — even when the shared prefix repeats a conjunct the translation
    /// internalized more than once.
    /// </summary>
    /// <param name="left">The first label.</param>
    /// <param name="right">The second label.</param>
    /// <returns><see langword="true"/> when the labels are equal as sets.</returns>
    private static bool LabelsEqual(List<AlcConcept> left, List<AlcConcept> right)
    {
        if(left.Count != right.Count)
        {
            return false;
        }

        foreach(AlcConcept concept in left)
        {
            if(!Contains(right, concept))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the edge's role IRI reaches the restriction's role IRI through the told hierarchy, reflexively.</summary>
    /// <param name="edgeRole">The edge's role IRI.</param>
    /// <param name="restrictionRole">The restriction's role IRI.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <returns><see langword="true"/> when the universal applies over the edge.</returns>
    private static bool RoleReaches(Utf8String edgeRole, Utf8String restrictionRole, Dictionary<Utf8String, HashSet<Utf8String>> superRoles)
    {
        return edgeRole.Equals(restrictionRole)
            || (superRoles.TryGetValue(edgeRole, out HashSet<Utf8String>? supers) && supers.Contains(restrictionRole));
    }

    /// <summary>
    /// Whether a directioned edge role reaches a directioned restriction role
    /// through the told hierarchy, reflexively. The told sub-role relation is
    /// inverse-closed (<c>r ⊑ s</c> implies <c>r⁻ ⊑ s⁻</c>), so two roles
    /// compare only when their direction agrees and the named IRIs reach
    /// through the forward hierarchy. With only forward roles in the fragment
    /// this is exactly the IRI reachability; an inverse edge reaches an inverse
    /// restriction by the same forward chain.
    /// </summary>
    /// <param name="edgeRole">The directioned role of the edge.</param>
    /// <param name="restrictionRole">The directioned role of the restriction.</param>
    /// <param name="superRoles">Per-role told super-roles, keyed by IRI.</param>
    /// <returns><see langword="true"/> when the universal applies over the edge.</returns>
    private static bool Reaches(AlcRole edgeRole, AlcRole restrictionRole, Dictionary<Utf8String, HashSet<Utf8String>> superRoles)
    {
        return edgeRole.IsInverse == restrictionRole.IsInverse
            && RoleReaches(edgeRole.Iri, restrictionRole.Iri, superRoles);
    }

    /// <summary>
    /// The ∀⁺-rule. For a successor reached over <paramref name="edgeRole"/>
    /// under the universal <paramref name="forAll"/> (the caller has already
    /// established <c>edgeRole ⊑* forAll.Role</c>), adds <c>∀R.C</c> to the
    /// successor for a transitive role <c>R</c> on the told chain
    /// <c>edgeRole ⊑* R ⊑* forAll.Role</c>. Re-propagating the universal
    /// itself — not just its filler — is what carries it down the transitive
    /// chain; one application adds at most one such restriction, the scan
    /// returns and the worklist revisits for the rest.
    /// </summary>
    /// <param name="edgeRole">The role of the edge to the successor.</param>
    /// <param name="forAll">The universal restriction being propagated.</param>
    /// <param name="targetLabel">The successor's label.</param>
    /// <param name="superRoles">Per-role told super-roles.</param>
    /// <param name="transitiveRoles">The roles declared transitive.</param>
    /// <returns><see langword="true"/> when the successor's label grew.</returns>
    private static bool PropagateTransitiveUniversal(
        AlcRole edgeRole,
        AlcForAll forAll,
        List<AlcConcept> targetLabel,
        Dictionary<Utf8String, HashSet<Utf8String>> superRoles,
        HashSet<Utf8String> transitiveRoles)
    {
        //A directioned role is transitive exactly when its named property is —
        //the inverse of a transitive role is transitive.
        //R = edgeRole itself: edgeRole ⊑* forAll.Role already holds.
        if(transitiveRoles.Contains(edgeRole.Iri) && AddConcept(targetLabel, new AlcForAll(edgeRole, forAll.Filler)))
        {
            return true;
        }

        //R = a told super-role of edgeRole that is transitive and still
        //reaches forAll.Role, carried in the same direction as the edge.
        if(superRoles.TryGetValue(edgeRole.Iri, out HashSet<Utf8String>? supers))
        {
            foreach(Utf8String candidate in supers)
            {
                AlcRole candidateRole = new(candidate, edgeRole.IsInverse);
                if(transitiveRoles.Contains(candidate)
                    && Reaches(candidateRole, forAll.Role, superRoles)
                    && AddConcept(targetLabel, new AlcForAll(candidateRole, forAll.Filler)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any node that is part of the model clashes: ⊥, or an atom
    /// meeting its negation. An indirectly-blocked node lies under a blocked
    /// ancestor and contributes no individual to the model, so its label is
    /// inert and a clash within it does not close the branch — propagating a
    /// universal into it must not spuriously condemn a satisfiable module.
    /// </summary>
    /// <param name="labels">Per-node labels.</param>
    /// <param name="blockingStatus">Per-node blocking status: 0 not blocked, 1 directly blocked, 2 indirectly blocked.</param>
    /// <returns><see langword="true"/> on a clash at an active node.</returns>
    private static bool HasClash(List<List<AlcConcept>> labels, byte[] blockingStatus)
    {
        for(int node = 0; node < labels.Count; node++)
        {
            if(blockingStatus[node] == IndirectlyBlocked)
            {
                continue;
            }

            List<AlcConcept> label = labels[node];
            foreach(AlcConcept concept in label)
            {
                if(concept is AlcBottom)
                {
                    return true;
                }

                if(concept is AlcNot negation && Contains(label, negation.Operand))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Adds a concept to a label unless present; list-backed for deterministic iteration.</summary>
    /// <param name="label">The label.</param>
    /// <param name="concept">The concept.</param>
    /// <returns><see langword="true"/> when the label grew.</returns>
    private static bool AddConcept(List<AlcConcept> label, AlcConcept concept)
    {
        if(Contains(label, concept))
        {
            return false;
        }

        label.Add(concept);

        return true;
    }

    /// <summary>Whether the label contains the concept; labels stay small enough for the linear scan.</summary>
    /// <param name="label">The label.</param>
    /// <param name="concept">The concept.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool Contains(List<AlcConcept> label, AlcConcept concept)
    {
        foreach(AlcConcept candidate in label)
        {
            if(candidate.Equals(concept))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Deep-copies the whole forest state for a choice point — labels, tree parents, parent-edge roles, and the bidirectional neighbour graph — so a backtrack restores the branch point without sharing mutable state.</summary>
    /// <param name="labels">Per-node labels.</param>
    /// <param name="parents">Per-node tree parents.</param>
    /// <param name="parentRole">Per-node directioned parent-edge roles.</param>
    /// <param name="neighbours">Per-node role-directioned neighbours.</param>
    /// <returns>The independent copy.</returns>
    private static (List<List<AlcConcept>> Labels2, List<int> Parents2, List<AlcRole> ParentRole2, List<List<(int Target, AlcRole Role)>> Neighbours2) Clone(
        List<List<AlcConcept>> labels,
        List<int> parents,
        List<AlcRole> parentRole,
        List<List<(int Target, AlcRole Role)>> neighbours)
    {
        List<List<AlcConcept>> labelsCopy = new(labels.Count);
        foreach(List<AlcConcept> label in labels)
        {
            labelsCopy.Add([.. label]);
        }

        return (labelsCopy, [.. parents], [.. parentRole], CloneNeighbours(neighbours));
    }

    /// <summary>Deep-copies the neighbour lists; the directioned-role entries are value structs, so a per-node list copy is independent.</summary>
    /// <param name="neighbours">Per-node neighbours.</param>
    /// <returns>The independent copy.</returns>
    private static List<List<(int Target, AlcRole Role)>> CloneNeighbours(List<List<(int Target, AlcRole Role)>> neighbours)
    {
        List<List<(int Target, AlcRole Role)>> copy = new(neighbours.Count);
        foreach(List<(int Target, AlcRole Role)> edges in neighbours)
        {
            copy.Add([.. edges]);
        }

        return copy;
    }

    /// <summary>Whether a role IRI is one of the reserved built-ins, <c>owl:topObjectProperty</c> or <c>owl:bottomObjectProperty</c>, whose fixed universal/empty semantics the calculus does not interpret.</summary>
    /// <param name="role">The role IRI.</param>
    /// <returns><see langword="true"/> for a reserved role.</returns>
    private static bool IsReservedRole(Utf8String role)
    {
        return role.Equals(OwlVocabulary.TopObjectProperty) || role.Equals(OwlVocabulary.BottomObjectProperty);
    }

    /// <summary>Whether a data-property IRI is one of the reserved built-ins, <c>owl:topDataProperty</c> or <c>owl:bottomDataProperty</c>, whose fixed full/empty extension the calculus does not interpret; a restriction naming one stays out of the fragment.</summary>
    /// <param name="property">The data-property IRI.</param>
    /// <returns><see langword="true"/> for a reserved data property.</returns>
    private static bool IsReservedDataProperty(Utf8String property)
    {
        return property.Equals(OwlVocabulary.TopDataProperty) || property.Equals(OwlVocabulary.BottomDataProperty);
    }

    /// <summary>The <c>rdfs:Literal</c> data range — the whole data domain — used as the default range of an unqualified data cardinality.</summary>
    private static OwlDataRange RdfsLiteralRange { get; } = new OwlDatatypeReference(new NamedNode(Lumoin.Veritas.Rdf.RdfVocabulary.Rdfs.LiteralClass));

    /// <summary>The individual key: a named individual by IRI, an anonymous one by label, an engine-minted one by its deterministic Skolem IRI.</summary>
    /// <param name="individual">The individual term.</param>
    /// <param name="key">The key.</param>
    /// <returns><see langword="false"/> for a term that is not an individual.</returns>
    private static bool TryIndividualKey(RdfTerm individual, out Utf8String key)
    {
        switch(individual)
        {
            case NamedNode named:
                key = named.Iri;

                return true;
            case BlankNode blank:
                key = blank.Label;

                return true;
            case EngineNode engine:
                key = engine.SkolemIri();

                return true;
            default:
                key = default;

                return false;
        }
    }

    /// <summary>Resolves a key through the merge map, path-compressing as it goes.</summary>
    /// <param name="merges">The merge parent map.</param>
    /// <param name="key">The key to resolve.</param>
    /// <returns>The representative key.</returns>
    private static Utf8String Find(Dictionary<Utf8String, Utf8String> merges, Utf8String key)
    {
        Utf8String current = key;
        while(merges.TryGetValue(current, out Utf8String parent) && !parent.Equals(current))
        {
            current = parent;
        }

        return current;
    }

    /// <summary>The role's super-set, created on first contact.</summary>
    /// <param name="superRoles">The hierarchy index.</param>
    /// <param name="role">The role.</param>
    /// <returns>The mutable super-set.</returns>
    private static HashSet<Utf8String> SuperSetOf(Dictionary<Utf8String, HashSet<Utf8String>> superRoles, Utf8String role)
    {
        if(!superRoles.TryGetValue(role, out HashSet<Utf8String>? supers))
        {
            supers = [];
            superRoles[role] = supers;
        }

        return supers;
    }

    /// <summary>Appends a concept to an individual's asserted list, creating it on first contact.</summary>
    /// <param name="asserted">The per-individual index.</param>
    /// <param name="individual">The individual key.</param>
    /// <param name="concept">The concept.</param>
    private static void Append(Dictionary<Utf8String, List<AlcConcept>> asserted, Utf8String individual, AlcConcept concept)
    {
        if(!asserted.TryGetValue(individual, out List<AlcConcept>? list))
        {
            list = [];
            asserted[individual] = list;
        }

        list.Add(concept);
    }

    /// <summary>
    /// Seeds <see cref="AlcBottom"/> on any two individuals a <c>DifferentIndividuals</c>
    /// axiom asserts distinct that the <c>SameIndividual</c> pre-merge has nonetheless
    /// collapsed to one representative: the told identity forcing the pair equal contradicts
    /// their asserted distinctness, so the collided node is unsatisfiable and the module
    /// inconsistent. The collision is visible only after the union-find closure, so it is a
    /// representative comparison, not a syntactic operand check; a non-colliding pair stays
    /// vacuous, no other merge source existing in ALC(H).
    /// </summary>
    /// <param name="axiom">The <c>DifferentIndividuals</c> axiom.</param>
    /// <param name="merges">The <c>SameIndividual</c> union-find parent map.</param>
    /// <param name="asserted">The per-individual asserted-concept index the seed lands in.</param>
    private static void SeedDistinctnessClash(OwlDifferentIndividualsAxiom axiom, Dictionary<Utf8String, Utf8String> merges, Dictionary<Utf8String, List<AlcConcept>> asserted)
    {
        IReadOnlyList<RdfTerm> individuals = axiom.Individuals;
        for(int i = 0; i < individuals.Count; i++)
        {
            if(!TryIndividualKey(individuals[i], out Utf8String keyI))
            {
                continue;
            }

            Utf8String representative = Find(merges, keyI);
            for(int j = i + 1; j < individuals.Count; j++)
            {
                if(TryIndividualKey(individuals[j], out Utf8String keyJ) && representative.Equals(Find(merges, keyJ)))
                {
                    Append(asserted, representative, AlcBottom.Instance);
                }
            }
        }
    }
}
