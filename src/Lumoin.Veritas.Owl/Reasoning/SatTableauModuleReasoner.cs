using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;
using Lumoin.Veritas.Owl.Datatypes;

namespace Lumoin.Veritas.Owl.Reasoning;

/// <summary>
/// The SAT-backed sibling of <see cref="AlcModuleReasoner"/>: the same
/// ALC(H) fragment over the same shared translation, decided as per-world
/// propositional satisfiability checks against one growing CNF instead of
/// an in-place tableau with copy-on-branch snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape.</b> The internalized TBox asserts once into a shared
/// <see cref="ConceptCnf"/>, so every world carries its clauses. A world is
/// a canonical assumption set — the sorted distinct literals of its label
/// concepts — checked by <see cref="SatSolver.SolveUnderAssumptions"/>. A
/// satisfying model passes a greedy top-down shrink that flips
/// gratuitously-true variables — connectives and the modal atoms they
/// force — to false; each existential atom still true spawns a
/// successor world carrying the filler plus every universal filler whose
/// role the existential's role reaches through the told role hierarchy.
/// Worlds explore over an explicit frame stack with a memo table over
/// canonical assumption sets — no call recursion.
/// </para>
/// <para>
/// <b>Conflict clauses instead of snapshots.</b> A successor proved
/// unsatisfiable learns a modal conflict clause over exactly the atoms that
/// built it — the existential and the contributing universals. The clause
/// is TBox-entailed, so it appends to the shared CNF and the parent
/// re-solves its unchanged assumptions against the grown formula; the loop
/// terminates because each learned clause eliminates the model's current
/// modal-atom combination. A world whose assumption set is a subset of an
/// in-progress ancestor's set (equal sets included) is blocked and counts
/// satisfiable — blocking only affirms, clauses are learned only from
/// genuine refutations, so the two devices cannot interact unsoundly.
/// Blocking makes a verdict TENTATIVE, though: it holds only if the
/// blocking ancestor itself ends satisfiable, so a satisfiable world whose
/// subtree blocked against a strictly shallower ancestor stays out of the
/// memo table — satisfiability is downward-monotone over assumption sets,
/// which firms the verdict when the ancestor closes satisfiable, but a
/// later refutation of the ancestor would leave a cached tentative verdict
/// wrong. Unsatisfiable verdicts are unconditional and always cached.
/// </para>
/// <para>
/// <b>Scope.</b> <see cref="DecideConsistency"/> decides consistency
/// through a single anonymous root world when no individual carries
/// assertions, one independent world per named individual when concepts
/// alone are asserted — sound because without asserted role edges no
/// constraint travels between named individuals — and one joint SAT
/// instance over the whole named forest when asserted role edges are
/// present: the template variable space replicates per individual in
/// blocks of a width frozen per solve, every template clause instantiates
/// into every block, each asserted edge contributes one propagation
/// clause per universal atom its role reaches through the told hierarchy,
/// and each individual's asserted concepts assume into its block. The
/// joint model's true existential atoms spawn per-block successor chains
/// in template space; a chain failure teaches the template its modal
/// clause and the joint root rebuilds and re-solves. The joint root spans
/// blocks, so it never enters the template-space memo table.
/// <see cref="Decide"/> adds the module-local subsumption sweep when the
/// named-class signature is within
/// <see cref="AlcModuleReasoner.SubsumptionSignatureCap"/>: each ordered
/// pair of distinct named classes probes the shared CNF as one world
/// under the assumptions <c>{+sub, −super}</c> — unsatisfiable means
/// subsumed — with the memo table and every learned modal clause carrying
/// across the whole sweep.
/// </para>
/// <para>
/// <b>Fragment honesty.</b> The verdict names the beyond-fragment
/// remainder through the shared translation, identically to
/// <see cref="AlcModuleReasoner"/>: a non-empty
/// <see cref="ModuleVerdict.UnsupportedConstructs"/> scopes consistency to
/// the supported fragment.
/// </para>
/// </remarks>
internal sealed class SatTableauModuleReasoner: IDisposable
{
    /// <summary>The module's shared translation: the internalized TBox, the asserted forest, the role hierarchy, and the beyond-fragment remainder. Built once per decision.</summary>
    private readonly AlcModuleReasoner.Translation translation;

    /// <summary>The shared CNF every world carries: the internalized TBox asserted once, grown by the decision's learned modal clauses.</summary>
    private readonly ConceptCnf cnf;

    /// <summary>The settled-verdict memo over canonical assumption-set keys, shared across every world of the decision.</summary>
    private readonly WorldMemo memo;

    /// <summary>How the solver prunes within one world's boolean structure; fixed for the decision.</summary>
    private readonly SatSearchMode searchMode;

    /// <summary>The token that aborts the decision between solver calls.</summary>
    private readonly CancellationToken cancellationToken;

    /// <summary>The work-based bound on the decision; the engine abstains rather than start a world solve that would exceed it.</summary>
    private readonly ReasoningBudget budget;

    /// <summary>The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains; <see cref="DatatypeRegistry.Empty"/> when the host registered none.</summary>
    private readonly DatatypeRegistry registry;

    /// <summary>The number of axioms in the decided module, reported on the decision statistics.</summary>
    private readonly int moduleAxiomCount;

    /// <summary>The running count of world solves the decision has spent.</summary>
    private int solveCount;

    /// <summary>The solver counters accumulated across the decision's world solves.</summary>
    private SatSolveStatistics runningTally;

    /// <summary>Whether the decision has abstained on its budget; once set it stays set and unwinds the search.</summary>
    private bool abstained;

    /// <summary>Whether a decided world carried a concrete-domain obligation the checker could not decide; once set it stays set and scopes a satisfiable verdict to the modelled datatype subset.</summary>
    private bool dataUndecided;

    /// <summary>Whether a delegate-backed registered datatype decided one of a world's obligations; once set it stays set and names the self-certified provenance on the module remainder.</summary>
    private bool dataSelfCertified;

    /// <summary>
    /// Whether the shared-CNF world solves reuse one incremental <see cref="SatSolverSession"/>
    /// across the decision instead of a stateless per-solve engine. Off by default: reusing one
    /// session across a decision's dissimilar solves over a growing formula, with an unbounded
    /// learned-clause database, can make a large decision far slower; incremental reuse helps
    /// only when many similar solves share a fixed formula.
    /// </summary>
    private readonly bool useIncrementalSession;

    /// <summary>The incremental solver over the shared CNF, built lazily on the first shared-CNF world solve when <see cref="useIncrementalSession"/> is set and reused across every world solve of the decision so its learned clauses, variable order, and saved phases carry across the whole search; <see langword="null"/> otherwise.</summary>
    private SatSolverSession? sharedSession;

    /// <summary>The number of shared-CNF clauses already ingested into <see cref="sharedSession"/>; the sync before each solve appends only the clauses the CNF has grown since.</summary>
    private int ingestedClauseCount;

    /// <summary>
    /// The decision-scoped clause arena the stateless world solves run over, built on
    /// the first stateless solve and synced incrementally as the CNF grows — the CNF
    /// is append-only, so each solve ingests only the new clauses instead of
    /// rebuilding the arena from the clause lists. The borrowed-arena solver entry
    /// restores it to the ingested boundary after every call, so no learned clause —
    /// a consequence of the formula together with one call's assumptions — ever
    /// reaches another solve.
    /// </summary>
    private SatSolver.ClauseArena? statelessArena;

    /// <summary>The number of shared-CNF clauses already ingested into <see cref="statelessArena"/>.</summary>
    private int statelessIngestedCount;

    /// <summary>Per-variable clause indices holding the variable's positive literal — the world path's persistent shrink occurrence index, grown with the decision and never rebuilt per solve.</summary>
    private List<int>?[] shrinkPositiveOccurrences = [];

    /// <summary>Per-variable clause indices holding the variable's negative literal, parallel to <see cref="shrinkPositiveOccurrences"/>.</summary>
    private List<int>?[] shrinkNegativeOccurrences = [];

    /// <summary>The per-clause true-literal counts of the world path's shrink, refilled against the model on every shrink; the buffer persists and grows with the clause count.</summary>
    private int[] shrinkTrueCounts = [];

    /// <summary>The number of shared-CNF clauses the shrink occurrence index has ingested. Independent of every solve lane — the index syncs at shrink time, so both the stateless and the session path shrink against the full clause set.</summary>
    private int shrinkIngestedCount;

    /// <summary>The reusable pinned-variable set of the world path's shrink, cleared per use.</summary>
    private readonly HashSet<int> shrinkPinnedScratch = [];

    /// <summary>
    /// The reusable label-literal list the probe-first world checks fill, cleared at
    /// each fill. Never live across a nested fill: every consumer canonicalizes and
    /// materializes before the world check it feeds starts running.
    /// </summary>
    private readonly List<SatLiteral> labelLiteralsScratch = [];

    /// <summary>The reusable encoded-code scratch of the canonicalization, cleared per call.</summary>
    private readonly List<int> canonicalCodesScratch = [];

    /// <summary>The reusable canonical-key scratch; holds the most recent canonicalization's sorted distinct codes until the next one runs.</summary>
    private readonly List<int> canonicalKeyScratch = [];

    /// <summary>The reusable true-data-atom list of the model datatype check, cleared per call; the checker consumes it within the call and the learned conflict clause is built fresh.</summary>
    private readonly List<AlcConcept> checkModelDataActiveScratch = [];

    /// <summary>The reusable true-existential snapshot of the successor planner, cleared per call.</summary>
    private readonly List<(AlcExists Concept, int Variable)> planExistentialsScratch = [];

    /// <summary>The reusable true-universal snapshot of the successor planner, cleared per call.</summary>
    private readonly List<(AlcForAll Concept, int Variable)> planUniversalsScratch = [];

    /// <summary>
    /// Opens a decision session over a module: translates it, asserts the
    /// internalized TBox into the shared CNF once, and fixes the search mode
    /// and token for the decision. The session is single-use and
    /// decision-scoped — one per <see cref="Decide"/> or
    /// <see cref="DecideConsistency"/> call — so the shared CNF, memo, and
    /// learned clauses never leak between decisions.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The work-based bound on the decision.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="useIncrementalSession">Whether the shared-CNF world solves reuse one incremental session across the decision; off by default — see <see cref="SolveShared"/> for when reuse helps.</param>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults where the family classifier abstains.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    private SatTableauModuleReasoner(ReasoningModule module, ReasoningBudget budget, SatSearchMode searchMode, bool useIncrementalSession, DatatypeRegistry registry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

        //Fold the reserved-vocabulary constant shapes before translating, so
        //this engine shares the folded fragment view with the other SAT decision
        //surfaces and the FragmentGaps census stays engine-independent.
        module = ReservedVocabularyFold.Apply(module);

        this.registry = registry;
        translation = AlcModuleReasoner.Translate(module);

        //The shared CNF: every world carries the internalized TBox, so its
        //conjuncts assert as facts once and worlds differ only in their
        //assumption sets.
        cnf = new ConceptCnf();
        foreach(AlcConcept conjunct in translation.TBox)
        {
            cnf.AssertFact(conjunct);
        }

        memo = new WorldMemo();
        moduleAxiomCount = module.Axioms.Count;
        this.budget = budget;
        this.searchMode = searchMode;
        this.useIncrementalSession = useIncrementalSession;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>Folds one world solve's statistics into the decision's running totals.</summary>
    /// <param name="statistics">The solve's reported statistics.</param>
    private void RecordSolve(in SatSolveStatistics statistics)
    {
        solveCount++;
        runningTally = SatSolveStatistics.Combine(runningTally, statistics);
    }

    /// <summary>
    /// Whether the decision must abstain — it has already been marked
    /// abstaining, or it has reached its work bound. Checked at each
    /// world-solve loop head before the solve, so an exhausted budget stops
    /// the search before the next solve. Once the bound is reached every
    /// loop head returns the benign satisfiable value and the search unwinds
    /// without a further solve; the only learned clause a post-bound step can
    /// still append is the conservative (un-minimized) one a minimization
    /// probe leaves, which is sound. <see cref="RunDecision"/> turns the mark
    /// into an <see cref="ReasoningDecisionOutcome.AbstainedBudget"/>
    /// decision. The mark is monotone — the work counters only grow — so it
    /// never clears.
    /// </summary>
    /// <returns><c>true</c> when the decision must abstain.</returns>
    private bool ShouldAbstain()
    {
        if(abstained || budget.IsExhaustedBy(solveCount, runningTally))
        {
            abstained = true;

            return true;
        }

        return false;
    }

    /// <summary>The decision's statistics as they stand: the module size, the world solves spent, and the accumulated solver totals.</summary>
    /// <returns>The statistics.</returns>
    private ReasoningDecisionStatistics Statistics()
    {
        return new ReasoningDecisionStatistics(moduleAxiomCount, solveCount, runningTally);
    }

    /// <summary>The settled verdicts the memo table holds; a world still on the frame stack has no entry — blocking reads the stack, not the table.</summary>
    private enum WorldState
    {
        /// <summary>The world and every successor reachable from it are satisfiable, established without depending on any then-unresolved ancestor.</summary>
        Satisfiable,

        /// <summary>No model of the shared CNF honours the world's assumptions.</summary>
        Unsatisfiable,
    }

    /// <summary>
    /// Wraps <see cref="Decide"/> as the seam delegate, the SAT-backed
    /// counterpart of <see cref="AlcModuleReasoner.CreateDelegate"/>: a
    /// rendezvous constructed with this delegate routes beyond-RL modules
    /// through the SAT-backed engine instead of the snapshot tableau, while
    /// the default — the snapshot engine — stays whatever the caller wires.
    /// Both engines decide the same ALC(H) fragment and name the same
    /// beyond-fragment remainder, so the choice is one of search strategy,
    /// not of answer.
    /// </summary>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure; both modes decide the same satisfiability.</param>
    /// <param name="budget">The work-based bound applied to each decision the delegate makes; the default is unbounded.</param>
    /// <param name="useIncrementalSession">Whether each decision reuses one incremental session across its world solves; off by default — see <see cref="SolveShared"/> for when reuse helps.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(SatSearchMode searchMode = SatSearchMode.ConflictLearning, ReasoningBudget budget = default, bool useIncrementalSession = false)
    {
        return CreateDelegate(DatatypeRegistry.Empty, searchMode, budget, useIncrementalSession);
    }

    /// <summary>
    /// Wraps the SAT-backed decision as the seam delegate carrying a registered-datatype set the
    /// concrete-domain sidecar consults — the registry-carrying counterpart of
    /// <see cref="CreateDelegate(SatSearchMode, ReasoningBudget, bool)"/>.
    /// </summary>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="budget">The work-based bound applied to each decision the delegate makes; the default is unbounded.</param>
    /// <param name="useIncrementalSession">Whether each decision reuses one incremental session across its world solves.</param>
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(DatatypeRegistry registry, SatSearchMode searchMode = SatSearchMode.ConflictLearning, ReasoningBudget budget = default, bool useIncrementalSession = false)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return new SatDecisionSeam(searchMode, budget, useIncrementalSession, registry).Decide;
    }

    /// <summary>
    /// Wraps <see cref="DecideModule(ReasoningModule, DatatypeRegistry, ReasoningBudget, SatSearchMode, bool, CancellationToken)"/>
    /// as the SAT-backed <see cref="DescriptionLogicDelegate"/>, carrying the search mode, budget,
    /// incremental-session choice, and registered-datatype set as explicit state so the delegate is a bound
    /// method group rather than a lambda closing over the enclosing parameters.
    /// </summary>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="budget">The work-based bound applied to each decision.</param>
    /// <param name="useIncrementalSession">Whether each decision reuses one incremental session across its world solves.</param>
    /// <param name="registry">The registered-datatype set the concrete-domain sidecar consults.</param>
    private sealed class SatDecisionSeam(SatSearchMode searchMode, ReasoningBudget budget, bool useIncrementalSession, DatatypeRegistry registry)
    {
        /// <summary>How the solver prunes within one world's boolean structure.</summary>
        private SatSearchMode SearchMode { get; } = searchMode;

        /// <summary>The work-based bound applied to each decision.</summary>
        private ReasoningBudget Budget { get; } = budget;

        /// <summary>Whether each decision reuses one incremental session across its world solves.</summary>
        private bool UseIncrementalSession { get; } = useIncrementalSession;

        /// <summary>The registered-datatype set the concrete-domain sidecar consults.</summary>
        private DatatypeRegistry Registry { get; } = registry;

        /// <summary>Decides the module's ALC(H) fragment over the shared CNF.</summary>
        /// <param name="module">The module to decide.</param>
        /// <param name="cancellationToken">A token to cancel the decision.</param>
        /// <returns>The module decision.</returns>
        public ValueTask<ModuleDecision> Decide(ReasoningModule module, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(DecideModule(module, Registry, Budget, SearchMode, UseIncrementalSession, cancellationToken));
        }
    }

    /// <summary>
    /// Decides the module's ALC(H) fragment over the shared CNF:
    /// consistency, module-local subsumptions between named classes when
    /// the signature is within
    /// <see cref="AlcModuleReasoner.SubsumptionSignatureCap"/>, and the
    /// beyond-fragment remainder named on
    /// <see cref="ModuleVerdict.UnsupportedConstructs"/> exactly as
    /// <see cref="AlcModuleReasoner"/> does.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure; both modes decide the same satisfiability.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict Decide(
        ReasoningModule module,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        CancellationToken cancellationToken = default)
    {
        using SatTableauModuleReasoner reasoner = new(module, ReasoningBudget.Unbounded, searchMode, useIncrementalSession: false, DatatypeRegistry.Empty, cancellationToken);

        return reasoner.RunDecision(includeSubsumptions: true).Verdict!;
    }

    /// <summary>
    /// Decides the module's ALC(H) consistency over the shared CNF: the
    /// subsumption list stays empty, and
    /// <see cref="ModuleVerdict.UnsupportedConstructs"/> names the
    /// beyond-fragment remainder exactly as <see cref="AlcModuleReasoner"/>
    /// does. Without asserted role edges each named individual is an
    /// independent world; with them the named forest checks as one joint
    /// SAT instance over per-individual variable blocks.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure; both modes decide the same satisfiability.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(
        ReasoningModule module,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        CancellationToken cancellationToken = default)
    {
        using SatTableauModuleReasoner reasoner = new(module, ReasoningBudget.Unbounded, searchMode, useIncrementalSession: false, DatatypeRegistry.Empty, cancellationToken);

        return reasoner.RunDecision(includeSubsumptions: false).Verdict!;
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> — the
    /// verdict plus the work the decision spent, or an abstention when the
    /// budget runs out before a verdict — the form the
    /// <see cref="DescriptionLogicDelegate"/> seam returns. Includes the
    /// module-local subsumption sweep, mirroring <see cref="Decide"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="budget">The work-based bound on the decision; the default is unbounded.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="useIncrementalSession">Whether the shared-CNF world solves reuse one incremental session across the decision; off by default — see <see cref="SolveShared"/> for when reuse helps.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The decision: the verdict and the work it spent, or a budget abstention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(
        ReasoningModule module,
        ReasoningBudget budget = default,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        bool useIncrementalSession = false,
        CancellationToken cancellationToken = default)
    {
        return DecideModule(module, DatatypeRegistry.Empty, budget, searchMode, useIncrementalSession, cancellationToken);
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> consulting a registered-datatype set at the
    /// concrete-domain leaves — the registry-carrying counterpart the SAT-backed seam returns.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="budget">The work-based bound on the decision; the default is unbounded.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="useIncrementalSession">Whether the shared-CNF world solves reuse one incremental session across the decision.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The decision: the verdict and the work it spent, or a budget abstention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(
        ReasoningModule module,
        DatatypeRegistry registry,
        ReasoningBudget budget = default,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        bool useIncrementalSession = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using SatTableauModuleReasoner reasoner = new(module, budget, searchMode, useIncrementalSession, registry, cancellationToken);

        return reasoner.RunDecision(includeSubsumptions: true);
    }

    /// <summary>
    /// Decides the module's ALC(H) consistency over the shared CNF consulting a registered-datatype set at
    /// the concrete-domain leaves — the registry-carrying counterpart of
    /// <see cref="DecideConsistency(ReasoningModule, SatSearchMode, CancellationToken)"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="registry">The registered-datatype set consulted where the family classifier abstains.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="registry"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(
        ReasoningModule module,
        DatatypeRegistry registry,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);

        using SatTableauModuleReasoner reasoner = new(module, ReasoningBudget.Unbounded, searchMode, useIncrementalSession: false, registry, cancellationToken);

        return reasoner.RunDecision(includeSubsumptions: false).Verdict!;
    }

    /// <summary>
    /// Runs the consistency check over the shared CNF — the joint instance
    /// when asserted role edges are present, per-seed worlds otherwise —
    /// optionally following with the module-local subsumption sweep against
    /// the same CNF and memo table, and wraps the verdict with the work the
    /// decision spent. A solve site that finds the budget exhausted unwinds
    /// the search through <see cref="BudgetExhaustedSignal"/>, and the
    /// decision abstains with the work spent so far.
    /// </summary>
    /// <param name="includeSubsumptions">Whether to enumerate module-local subsumptions when the signature qualifies.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision RunDecision(bool includeSubsumptions)
    {
        bool isConsistent = translation.Edges.Count > 0
            ? CheckJointRoot()
            : CheckAllSeedWorlds();
        if(abstained)
        {
            return ModuleDecision.AbstainedOnBudget(Statistics());
        }

        //The consistency check's datatype abstention is captured before the
        //subsumption sweep runs its own checks: only the consistency verdict's
        //fragment-relativity belongs on the module verdict.
        bool consistencyUndecided = dataUndecided;

        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions =
            includeSubsumptions && isConsistent && translation.SignatureClasses.Count <= AlcModuleReasoner.SubsumptionSignatureCap
                ? SweepSubsumptions()
                : [];
        if(abstained)
        {
            return ModuleDecision.AbstainedOnBudget(Statistics());
        }

        //An undecided concrete-domain obligation on a consistent completion
        //names itself on the remainder, exactly as the snapshot engine does; a
        //delegate-backed registered datatype that decided an obligation names its
        //self-certified provenance on the same channel.
        List<string> unsupported = [.. translation.Unsupported];
        if(isConsistent && consistencyUndecided)
        {
            unsupported.Add(DataRestrictionConsistency.UndecidedMarker);
        }

        if(dataSelfCertified)
        {
            unsupported.Add(DataRestrictionConsistency.SelfCertifiedMarker);
        }

        ModuleVerdict verdict = new(isConsistent, subsumptions)
        {
            UnsupportedConstructs = unsupported,
        };

        return ModuleDecision.Decided(verdict, Statistics());
    }

    /// <summary>
    /// Returns the decision's pooled solver buffers by disposing the shared
    /// incremental session, if one was built. The reasoner is single-use — one
    /// instance per decision — so this runs once the decision's verdict is in hand.
    /// Idempotent.
    /// </summary>
    public void Dispose()
    {
        sharedSession?.Dispose();
        sharedSession = null;
    }

    /// <summary>
    /// The module-local subsumption sweep over the shared CNF: for every
    /// ordered pair of distinct named signature classes, <c>A ⊑ B</c>
    /// holds exactly when the single-world check under the assumptions
    /// <c>{+x_A, −x_B}</c> is unsatisfiable. Every probe runs against the
    /// same CNF and memo table, so modal clauses and settled verdicts
    /// accumulate across the whole sweep.
    /// </summary>
    /// <returns>The subsumed pairs, in pair-enumeration order.</returns>
    private List<(NamedNode SubClass, NamedNode SuperClass)> SweepSubsumptions()
    {
        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions = [];
        foreach(Utf8String subClass in translation.SignatureClasses)
        {
            foreach(Utf8String superClass in translation.SignatureClasses)
            {
                if(subClass.Equals(superClass))
                {
                    continue;
                }

                labelLiteralsScratch.Clear();
                labelLiteralsScratch.Add(cnf.GetLiteral(new AlcAtom(subClass)));
                labelLiteralsScratch.Add(cnf.GetLiteral(new AlcNot(new AlcAtom(superClass))));
                if(!CheckWorldForLabel(labelLiteralsScratch, minimizeCores: true))
                {
                    subsumptions.Add((new NamedNode(subClass), new NamedNode(superClass)));
                }

                if(abstained)
                {
                    return subsumptions;
                }
            }
        }

        return subsumptions;
    }

    /// <summary>
    /// Checks every seed world against the shared CNF: a single anonymous
    /// root with no extra assumptions when no individual carries
    /// assertions, else one world per asserted individual. The memo table
    /// and every learned clause carry across seeds.
    /// </summary>
    /// <returns><see langword="true"/> when every seed world is satisfiable.</returns>
    private bool CheckAllSeedWorlds()
    {
        List<(SatLiteral[] Assumptions, int[] Key)> seeds = [];
        if(translation.AssertedConcepts.Count == 0)
        {
            seeds.Add(([], []));
        }
        else
        {
            foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in translation.AssertedConcepts)
            {
                List<SatLiteral> literals = new(entry.Value.Count);
                foreach(AlcConcept concept in entry.Value)
                {
                    literals.Add(cnf.GetLiteral(concept));
                }

                seeds.Add(Canonicalize(literals));
            }
        }

        foreach((SatLiteral[] assumptions, int[] key) in seeds)
        {
            if(!CheckWorld(assumptions, key, minimizeCores: true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks the named forest when asserted role edges are present, one
    /// joint SAT instance per connected component of the asserted edge
    /// graph: in ALC(H) without inverses or nominals, constraints travel
    /// between named individuals only along asserted edges, so disjoint
    /// components compose models independently under the shared TBox.
    /// Asserted individuals no edge touches check as independent per-seed
    /// worlds, exactly as in the edge-free case.
    /// </summary>
    /// <returns><see langword="true"/> when every component's joint instance and every spawned chain are satisfiable.</returns>
    private bool CheckJointRoot()
    {
        Dictionary<Utf8String, Utf8String> componentParent = [];
        foreach((Utf8String from, Utf8String _, Utf8String to) in translation.Edges)
        {
            Unite(componentParent, from, to);
        }

        //Edge components group by their representative; each becomes one
        //joint instance over exactly its own individuals and edges. An
        //asserted individual no edge touches is an independent world — the
        //same argument that makes the edge-free case per-seed: in ALC(H)
        //without inverses or nominals, constraints travel between named
        //individuals only along asserted edges, so disjoint components
        //compose models independently under the shared TBox.
        Dictionary<Utf8String, List<(Utf8String From, Utf8String Role, Utf8String To)>> componentEdges = [];
        foreach((Utf8String from, Utf8String role, Utf8String to) edge in translation.Edges)
        {
            Utf8String root = Find(componentParent, edge.from);
            if(!componentEdges.TryGetValue(root, out List<(Utf8String From, Utf8String Role, Utf8String To)>? edges))
            {
                edges = [];
                componentEdges[root] = edges;
            }

            edges.Add(edge);
        }

        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in translation.AssertedConcepts)
        {
            if(componentParent.ContainsKey(entry.Key))
            {
                continue;
            }

            labelLiteralsScratch.Clear();
            foreach(AlcConcept concept in entry.Value)
            {
                labelLiteralsScratch.Add(cnf.GetLiteral(concept));
            }

            if(!CheckWorldForLabel(labelLiteralsScratch, minimizeCores: true))
            {
                return false;
            }
        }

        foreach(KeyValuePair<Utf8String, List<(Utf8String From, Utf8String Role, Utf8String To)>> component in componentEdges)
        {
            List<Utf8String> individuals = [];
            Dictionary<Utf8String, int> blockOf = [];
            foreach((Utf8String from, Utf8String _, Utf8String to) in component.Value)
            {
                BlockFor(individuals, blockOf, from);
                BlockFor(individuals, blockOf, to);
            }

            if(!CheckJointComponent(individuals, blockOf, component.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The union-find root of an individual in the edge-component forest, with path compression; an individual no union ever named is its own root.</summary>
    /// <param name="parent">The parent map.</param>
    /// <param name="individual">The individual to resolve.</param>
    /// <returns>The component representative.</returns>
    private static Utf8String Find(Dictionary<Utf8String, Utf8String> parent, Utf8String individual)
    {
        Utf8String current = individual;
        while(parent.TryGetValue(current, out Utf8String next) && !next.Equals(current))
        {
            current = next;
        }

        if(!individual.Equals(current))
        {
            parent[individual] = current;
        }

        return current;
    }

    /// <summary>Unites two individuals' edge components in the union-find forest.</summary>
    /// <param name="parent">The parent map.</param>
    /// <param name="first">The first individual.</param>
    /// <param name="second">The second individual.</param>
    private static void Unite(Dictionary<Utf8String, Utf8String> parent, Utf8String first, Utf8String second)
    {
        Utf8String firstRoot = Find(parent, first);
        Utf8String secondRoot = Find(parent, second);
        parent[firstRoot] = secondRoot;
        if(!parent.ContainsKey(secondRoot))
        {
            parent[secondRoot] = secondRoot;
        }
    }

    /// <summary>
    /// Runs one edge component's joint check to a verdict: build the
    /// component's joint instance from the template as it stands, solve,
    /// shrink, spawn every block's successor chains, learn every failing
    /// chain's modal clause into the template in the same round, and
    /// rebuild until a round completes with no failure or the joint
    /// instance itself refutes. The joint root spans blocks, so it never
    /// enters the template-space memo table — only the chains do.
    /// </summary>
    /// <remarks>
    /// The rebuild-per-solve discipline is the width-freeze design: every
    /// concept that can contribute to the joint root — the component's
    /// asserted concepts and, to a fixpoint, every universal filler one of
    /// its asserted edges can reach — encodes into the template before the
    /// block width freezes at the template's variable count, and successor
    /// chains that grow the template between solves invalidate nothing
    /// because the next build re-derives the block layout and
    /// re-instantiates every clause, learned ones included.
    /// </remarks>
    /// <param name="individuals">The component's individuals in block order.</param>
    /// <param name="blockOf">The component's individual-to-block index.</param>
    /// <param name="edges">The component's asserted edges.</param>
    /// <returns><see langword="true"/> when the component's joint instance and every spawned chain are satisfiable.</returns>
    private bool CheckJointComponent(
        List<Utf8String> individuals,
        Dictionary<Utf8String, int> blockOf,
        List<(Utf8String From, Utf8String Role, Utf8String To)> edges)
    {
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(ShouldAbstain())
            {
                return true;
            }

            JointInstance joint = BuildJointInstance(individuals, blockOf, edges);
            long jointSolveStart = ReasoningInstrumentation.Begin();
            SatVerdict verdict = SatSolver.SolveUnderAssumptions(
                joint.Clauses,
                joint.VariableCount,
                joint.Assumptions,
                pool: null,
                searchMode,
                cancellationToken);
            ReasoningInstrumentation.End(ReasoningPhase.SatSolve, jointSolveStart);
            RecordSolve(verdict.Statistics);
            if(!verdict.IsSatisfiable)
            {
                return false;
            }

            bool[] model = ShrinkJointModel(joint, verdict.Assignment!);
            bool learned = false;
            HashSet<int[]> learnedThisRound = new(CanonicalKeyComparer.Instance);
            for(int block = 0; block < individuals.Count; block++)
            {
                bool[] blockModel = ExtractBlockModel(model, block, joint.BlockWidth);

                //A block's datatype clash teaches the TEMPLATE its conflict clause
                //— sound in every block — and the one rebuild instantiates it into
                //each, exactly as a failed chain teaches its modal clause.
                if(CheckModelData(blockModel, out IReadOnlyList<SatLiteral> dataConflict) == DataConsistencyStatus.Clash)
                {
                    cnf.Append(dataConflict);
                    learned = true;

                    continue;
                }

                foreach(SuccessorPlan plan in PlanSuccessors(blockModel))
                {
                    if(!CheckWorld(plan.Assumptions, plan.Key, minimizeCores: true))
                    {
                        //Every chain failure of the round teaches the
                        //TEMPLATE its modal conflict core — TBox-entailed,
                        //so sound in every block — before the one rebuild
                        //instantiates them all into every block. Distinct
                        //blocks repeating the same failed plan contribute
                        //the clause once.
                        if(learnedThisRound.Add(plan.Key))
                        {
                            cnf.Append(MinimizeConflictClause(plan));
                        }

                        learned = true;
                    }
                }
            }

            if(!learned)
            {
                return true;
            }
        }
    }

    /// <summary>The individual's block index, allocated densely on first encounter.</summary>
    /// <param name="individuals">The individuals in block order.</param>
    /// <param name="blockOf">The individual-to-block index.</param>
    /// <param name="individual">The individual key.</param>
    private static void BlockFor(List<Utf8String> individuals, Dictionary<Utf8String, int> blockOf, Utf8String individual)
    {
        if(!blockOf.ContainsKey(individual))
        {
            blockOf[individual] = individuals.Count;
            individuals.Add(individual);
        }
    }

    /// <summary>
    /// Builds the joint instance from the template as it stands: encodes
    /// every contributor into the template — the asserted concepts and, to
    /// a fixpoint, the filler of every universal atom some asserted edge's
    /// role reaches — freezes the block width at the template's variable
    /// count, instantiates every template clause into every block, adds one
    /// propagation clause per asserted edge and reachable universal atom,
    /// and offsets each individual's asserted-concept literals into its
    /// block as the joint assumptions.
    /// </summary>
    /// <param name="individuals">The component's individuals in block order.</param>
    /// <param name="blockOf">The component's individual-to-block index.</param>
    /// <param name="edges">The component's asserted edges.</param>
    /// <returns>The joint instance.</returns>
    private JointInstance BuildJointInstance(
        List<Utf8String> individuals,
        Dictionary<Utf8String, int> blockOf,
        List<(Utf8String From, Utf8String Role, Utf8String To)> edges)
    {
        //Every joint contributor encodes BEFORE the width freezes. Asserted
        //concepts first; then every edge-reachable universal filler, walked
        //by index to a fixpoint because encoding a filler can register
        //further universal atoms that themselves need edge clauses.
        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in translation.AssertedConcepts)
        {
            if(!blockOf.ContainsKey(entry.Key))
            {
                continue;
            }

            foreach(AlcConcept concept in entry.Value)
            {
                cnf.GetLiteral(concept);
            }
        }

        for(int i = 0; i < cnf.ModalAtoms.Count; i++)
        {
            if(cnf.ModalAtoms[i].Concept is not AlcForAll forAll)
            {
                continue;
            }

            foreach((Utf8String _, Utf8String role, Utf8String _) in edges)
            {
                if(!RoleReaches(role, forAll.Role.Iri, translation.SuperRoles))
                {
                    continue;
                }

                cnf.GetLiteral(forAll.Filler);

                //The ∀⁺-carried universals are edge-role-dependent, so every
                //reaching edge is visited to register their atoms before the
                //width freeze; without transitive roles the filler alone, once.
                if(translation.TransitiveRoles.Count == 0)
                {
                    break;
                }

                List<Utf8String> carriedRoles = [];
                CollectTransitiveCarryRoles(role, forAll, carriedRoles);
                foreach(Utf8String carriedRole in carriedRoles)
                {
                    cnf.GetLiteral(new AlcForAll(AlcRole.Forward(carriedRole), forAll.Filler));
                }
            }
        }

        int blockWidth = cnf.VariableCount;
        int blockCount = individuals.Count;

        IReadOnlyList<IReadOnlyList<SatLiteral>> templates = cnf.Clauses;
        List<IReadOnlyList<SatLiteral>> clauses = new(templates.Count * blockCount);
        for(int block = 0; block < blockCount; block++)
        {
            for(int templateIndex = 0; templateIndex < templates.Count; templateIndex++)
            {
                clauses.Add(ConceptCnf.InstantiateInBlock(templates[templateIndex], block, blockWidth));
            }
        }

        //Asserted-edge universal propagation: for every edge a →r→ b and
        //every universal atom ∀s.D whose role r reaches, the clause
        //(¬x_a,∀sD ∨ lit_b(D)) — fully propositional, so propagation holds
        //transitively through edge cycles by unit propagation alone.
        foreach((Utf8String from, Utf8String role, Utf8String to) in edges)
        {
            int fromBlock = blockOf[from];
            int toBlock = blockOf[to];
            for(int i = 0; i < cnf.ModalAtoms.Count; i++)
            {
                ConceptCnf.ModalAtom atom = cnf.ModalAtoms[i];
                if(atom.Concept is not AlcForAll forAll || !RoleReaches(role, forAll.Role.Iri, translation.SuperRoles))
                {
                    continue;
                }

                SatLiteral filler = cnf.GetLiteral(forAll.Filler);
                clauses.Add(
                [
                    new SatLiteral(ConceptCnf.BlockVariable(fromBlock, atom.Variable, blockWidth), IsPositive: false),
                    new SatLiteral(ConceptCnf.BlockVariable(toBlock, filler.Variable, blockWidth), filler.IsPositive),
                ]);
                AppendTransitiveEdgeCarry(role, forAll, atom.Variable, fromBlock, toBlock, blockWidth, clauses);
            }
        }

        List<SatLiteral> assumptionLiterals = [];
        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in translation.AssertedConcepts)
        {
            if(!blockOf.TryGetValue(entry.Key, out int block))
            {
                continue;
            }

            foreach(AlcConcept concept in entry.Value)
            {
                SatLiteral literal = cnf.GetLiteral(concept);
                assumptionLiterals.Add(new SatLiteral(ConceptCnf.BlockVariable(block, literal.Variable, blockWidth), literal.IsPositive));
            }
        }

        (SatLiteral[] assumptions, int[] _) = Canonicalize(assumptionLiterals);

        IReadOnlyList<ConceptCnf.ModalAtom> registeredAtoms = cnf.ModalAtoms;
        List<int> modalVariables = new(registeredAtoms.Count);
        for(int atomIndex = 0; atomIndex < registeredAtoms.Count; atomIndex++)
        {
            modalVariables.Add(registeredAtoms[atomIndex].Variable);
        }

        return new JointInstance(clauses, blockWidth * blockCount, assumptions, blockWidth, blockCount, modalVariables);
    }

    /// <summary>
    /// The ∀⁺-rule across an asserted edge: for an edge whose role reaches
    /// <paramref name="forAll"/>'s role, adds a propagation clause carrying
    /// <c>∀R.C</c> into the target block for every transitive role <c>R</c> on
    /// the told chain <c>edgeRole ⊑* R ⊑* forAll.Role</c>, so the universal
    /// chains across transitive asserted edges by unit propagation. The carried
    /// atoms were registered before the width freeze, so their template
    /// variables index within a block.
    /// </summary>
    /// <param name="edgeRole">The asserted edge's role.</param>
    /// <param name="forAll">The universal whose role the edge reaches.</param>
    /// <param name="fromAtomVariable">The template variable of the ∀S.C atom in the source block.</param>
    /// <param name="fromBlock">The source individual's block index.</param>
    /// <param name="toBlock">The target individual's block index.</param>
    /// <param name="blockWidth">The frozen per-block variable count.</param>
    /// <param name="clausesToAppendTo">The joint clause list the carry clauses append to.</param>
    private void AppendTransitiveEdgeCarry(
        Utf8String edgeRole,
        AlcForAll forAll,
        int fromAtomVariable,
        int fromBlock,
        int toBlock,
        int blockWidth,
        List<IReadOnlyList<SatLiteral>> clausesToAppendTo)
    {
        if(translation.TransitiveRoles.Count == 0)
        {
            return;
        }

        List<Utf8String> carriedRoles = [];
        CollectTransitiveCarryRoles(edgeRole, forAll, carriedRoles);
        foreach(Utf8String carriedRole in carriedRoles)
        {
            SatLiteral carried = cnf.GetLiteral(new AlcForAll(AlcRole.Forward(carriedRole), forAll.Filler));
            clausesToAppendTo.Add(
            [
                new SatLiteral(ConceptCnf.BlockVariable(fromBlock, fromAtomVariable, blockWidth), IsPositive: false),
                new SatLiteral(ConceptCnf.BlockVariable(toBlock, carried.Variable, blockWidth), carried.IsPositive),
            ]);
        }
    }

    /// <summary>
    /// The greedy model-shrink pass over the joint model: for each block
    /// and each true variable replica in descending within-block order,
    /// tentatively flip it to false and keep the flip when every joint
    /// clause stays satisfied. Connective replicas shrink alongside modal
    /// ones — a gratuitously-true connective is what forces its modal
    /// operands true. Assumption literals are never flipped.
    /// </summary>
    /// <param name="joint">The joint instance, read for the clauses, the assumptions, and the block layout.</param>
    /// <param name="assignment">The solver's satisfying assignment.</param>
    /// <returns>The shrunk joint model.</returns>
    private static bool[] ShrinkJointModel(JointInstance joint, IReadOnlyList<bool> assignment)
    {
        bool[] model = new bool[assignment.Count];
        for(int i = 0; i < model.Length; i++)
        {
            model[i] = assignment[i];
        }

        HashSet<int> pinned = [];
        foreach(SatLiteral assumption in joint.Assumptions)
        {
            pinned.Add(assumption.Variable);
        }

        //The joint clause list is rebuilt per round by design, so its occurrence
        //index and counts build per call; the flip walk itself is the one shared
        //core the world path's persistent buffers also feed.
        int[] trueCounts = new int[joint.Clauses.Count];
        List<int>?[] positiveOccurrences = new List<int>?[model.Length];
        List<int>?[] negativeOccurrences = new List<int>?[model.Length];
        for(int clauseIndex = 0; clauseIndex < joint.Clauses.Count; clauseIndex++)
        {
            IReadOnlyList<SatLiteral> clause = joint.Clauses[clauseIndex];
            for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
            {
                SatLiteral literal = clause[literalIndex];
                if(model[literal.Variable] == literal.IsPositive)
                {
                    trueCounts[clauseIndex]++;
                }

                List<int>?[] occurrences = literal.IsPositive ? positiveOccurrences : negativeOccurrences;
                (occurrences[literal.Variable] ??= []).Add(clauseIndex);
            }
        }

        ShrinkInPlace(model, pinned, trueCounts, positiveOccurrences, negativeOccurrences);

        return model;
    }

    /// <summary>
    /// The shared greedy shrink core: walks the variables in descending
    /// order and flips each unpinned true variable to false when every
    /// clause keeps a true literal, tracked incrementally through the
    /// supplied per-clause true-literal counts over positive and negative
    /// occurrence lists. Descending order is top-down — definitional clauses only
    /// point from a connective to its lower-id operands, so one pass undoes
    /// a gratuitously-true forcing chain parent-first, freeing the modal
    /// operands for their own flips. The reserved always-true variable and
    /// its block replicas stay through their unit clauses. The flip decisions are
    /// a function of the buffers' content alone, so a persistent index and a
    /// per-call build shrink identically.
    /// </summary>
    /// <param name="model">The model to shrink in place; must satisfy the indexed clauses on entry.</param>
    /// <param name="pinned">The variables that are never flipped.</param>
    /// <param name="trueCounts">The per-clause count of literals true under <paramref name="model"/>, maintained through the flips.</param>
    /// <param name="positiveOccurrences">Per variable, the clauses holding its positive literal; entries past the model's length are never read.</param>
    /// <param name="negativeOccurrences">Per variable, the clauses holding its negative literal, parallel to <paramref name="positiveOccurrences"/>.</param>
    private static void ShrinkInPlace(bool[] model, HashSet<int> pinned, int[] trueCounts, List<int>?[] positiveOccurrences, List<int>?[] negativeOccurrences)
    {
        for(int variable = model.Length - 1; variable >= 1; variable--)
        {
            if(!model[variable] || pinned.Contains(variable))
            {
                continue;
            }

            //Flipping true→false removes a true literal from every clause
            //holding the positive literal; any of them down to its last
            //true literal blocks the flip.
            bool blocked = false;
            if(positiveOccurrences[variable] is List<int> positives)
            {
                foreach(int clauseIndex in positives)
                {
                    if(trueCounts[clauseIndex] == 1)
                    {
                        blocked = true;

                        break;
                    }
                }
            }

            if(blocked)
            {
                continue;
            }

            model[variable] = false;
            if(positiveOccurrences[variable] is List<int> lostClauses)
            {
                foreach(int clauseIndex in lostClauses)
                {
                    trueCounts[clauseIndex]--;
                }
            }

            if(negativeOccurrences[variable] is List<int> gainedClauses)
            {
                foreach(int clauseIndex in gainedClauses)
                {
                    trueCounts[clauseIndex]++;
                }
            }
        }
    }

    /// <summary>The block's slice of the joint model, re-indexed to template variable space.</summary>
    /// <param name="jointModel">The shrunk joint model.</param>
    /// <param name="blockIndex">The block to extract.</param>
    /// <param name="blockWidth">The frozen per-block variable count.</param>
    /// <returns>The block's model over template variables.</returns>
    private static bool[] ExtractBlockModel(bool[] jointModel, int blockIndex, int blockWidth)
    {
        bool[] blockModel = new bool[blockWidth];
        for(int variable = 0; variable < blockWidth; variable++)
        {
            blockModel[variable] = jointModel[ConceptCnf.BlockVariable(blockIndex, variable, blockWidth)];
        }

        return blockModel;
    }

    /// <summary>
    /// Solves one world's assumptions against the shared CNF, either through a stateless
    /// per-solve engine or, when <see cref="useIncrementalSession"/> is set, through one
    /// incremental session reused across the whole decision. The session keeps the clauses
    /// its conflicts learned, the variable order, and the watch structure between world
    /// solves, and grows to match the CNF as successor and subsumption exploration mints
    /// fresh atoms and appends entailed modal clauses. It does NOT carry saved phases: a
    /// decision drives dissimilar solves whose models must stay history-independent, and
    /// carried phases lock successive worlds into one model neighborhood so successor
    /// exploration wanders off the refutation-driven trajectory the world search needs to
    /// converge (the measured <c>WebOnt-description-logic-208</c>/<c>-209</c> solve-count
    /// blowup); phase carry stays available on the session primitive for genuinely similar
    /// solve streams. The CNF only ever grows and every appended clause is TBox-entailed,
    /// so a learned clause stays a consequence of the formula and reusing it across the
    /// growth is sound — both paths decide the same satisfiability. Which path is faster
    /// depends on the decision's solve profile: reuse pays off when many similar solves
    /// share one formula, and loses when a decision drives dissimilar solves over the
    /// growing formula. That profile — module size, world-solve count, signature — is what
    /// a chooser would key on to select the path per decision; absent one, the choice is
    /// the caller's, and defaults to the stateless engine.
    /// </summary>
    /// <param name="assumptions">The world's assumption literals, canonical.</param>
    /// <returns>The verdict.</returns>
    private SatVerdict SolveShared(SatLiteral[] assumptions)
    {
        if(!useIncrementalSession)
        {
            //The stateless lane keeps one arena for the decision and ingests only
            //the clauses the CNF has grown since the last solve; the borrowed-arena
            //entry restores the ingested boundary on every exit, so each solve sees
            //exactly the CNF's clauses and nothing learned under another world's
            //assumptions.
            statelessArena ??= new SatSolver.ClauseArena();
            IReadOnlyList<IReadOnlyList<SatLiteral>> statelessClauses = cnf.Clauses;
            for(int clauseIndex = statelessIngestedCount; clauseIndex < statelessClauses.Count; clauseIndex++)
            {
                statelessArena.Add(statelessClauses[clauseIndex]);
            }

            statelessIngestedCount = statelessClauses.Count;

            return SatSolver.SolveUnderAssumptionsOnArena(statelessArena, cnf.VariableCount, assumptions, pool: null, searchMode, cancellationToken);
        }

        if(sharedSession is null)
        {
            sharedSession = new SatSolverSession(cnf.Clauses, cnf.VariableCount, reduceThreshold: SatSolverSession.DeletionDisabled, carryPhases: false);
            ingestedClauseCount = cnf.Clauses.Count;
        }
        else
        {
            //Sync the session to the CNF as it now stands: register the fresh atoms
            //before the clauses that reference them, then append every clause the CNF
            //has grown since the last solve — the definitional clauses of the new
            //atoms and the learned modal and datatype conflict clauses alike.
            sharedSession.EnsureVariableCount(cnf.VariableCount);
            for(int index = ingestedClauseCount; index < cnf.Clauses.Count; index++)
            {
                sharedSession.AddClause(cnf.Clauses[index]);
            }

            ingestedClauseCount = cnf.Clauses.Count;
        }

        return sharedSession.Solve(assumptions, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs one seed world's check to a verdict over the explicit frame
    /// stack: solve under the world's assumptions, shrink the model, spawn
    /// successor worlds, learn a modal conflict clause from every
    /// unsatisfiable successor and re-solve, with the memo table answering
    /// settled sets and their monotone consequences and the ancestor walk
    /// blocking affirmatively.
    /// </summary>
    /// <param name="seedAssumptions">The seed world's assumption literals, canonical.</param>
    /// <param name="seedKey">The seed world's canonical key.</param>
    /// <param name="minimizeCores">Whether learned modal clauses shrink to conflict cores; the minimization probes themselves run without it, which bounds the probe recursion at one level.</param>
    /// <returns><see langword="true"/> when the seed world is satisfiable.</returns>
    private bool CheckWorld(
        SatLiteral[] seedAssumptions,
        int[] seedKey,
        bool minimizeCores)
    {
        //The memo holds settled verdicts only, so an exact or monotone
        //answer settles the seed.
        if(memo.Answer(seedKey) is WorldState settled)
        {
            return settled == WorldState.Satisfiable;
        }

        return CheckWorldCore(seedAssumptions, seedKey, minimizeCores);
    }

    /// <summary>
    /// Runs one seed world's check from its label literals: canonicalizes them into
    /// the shared scratch, answers from the memo by the scratch-backed key span, and
    /// materializes the canonical assumption and key arrays only when the memo has
    /// no answer and the world must actually run — so a label the memo settles
    /// allocates nothing.
    /// </summary>
    /// <param name="labelLiterals">The world's label literals, possibly duplicated and unordered; consumed before the check runs, so a scratch-backed list is safe.</param>
    /// <param name="minimizeCores">Whether learned modal clauses shrink to conflict cores.</param>
    /// <returns><see langword="true"/> when the world is satisfiable.</returns>
    private bool CheckWorldForLabel(List<SatLiteral> labelLiterals, bool minimizeCores)
    {
        CanonicalizeIntoScratch(labelLiterals);
        if(memo.Answer(CollectionsMarshal.AsSpan(canonicalKeyScratch)) is WorldState settled)
        {
            return settled == WorldState.Satisfiable;
        }

        (SatLiteral[] assumptions, int[] key) = MaterializeCanonicalScratch();

        return CheckWorldCore(assumptions, key, minimizeCores);
    }

    /// <summary>
    /// The world check's frame-stack machine, entered once the seed has no memo
    /// answer: solve under the world's assumptions, shrink the model, spawn
    /// successor worlds, learn a modal conflict clause from every unsatisfiable
    /// successor and re-solve, with the memo table answering settled sets and their
    /// monotone consequences and the ancestor walk blocking affirmatively.
    /// </summary>
    /// <param name="seedAssumptions">The seed world's assumption literals, canonical.</param>
    /// <param name="seedKey">The seed world's canonical key.</param>
    /// <param name="minimizeCores">Whether learned modal clauses shrink to conflict cores.</param>
    /// <returns><see langword="true"/> when the seed world is satisfiable.</returns>
    private bool CheckWorldCore(
        SatLiteral[] seedAssumptions,
        int[] seedKey,
        bool minimizeCores)
    {
        List<WorldFrame> stack = [new WorldFrame(seedKey, seedAssumptions)];

        //Tentative satisfiable verdicts, invocation-scoped: a world that
        //popped satisfiable conditional on a still-live ancestor, keyed to
        //the stack index it depends on. A repeat of the same set answers
        //from here exactly like a blocking hit — the consumer inherits the
        //dependency — and the entries promote into the settled memo when
        //their ancestor discharges satisfiably, or discard when it is
        //refuted. The settled memo never holds a conditional verdict.
        Dictionary<int[], int> tentativeDepth = new(CanonicalKeyComparer.Instance);

        while(stack.Count > 0)
        {
            if(ShouldAbstain())
            {
                return true;
            }

            WorldFrame frame = stack[^1];

            if(frame.Successors is null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long worldSolveStart = ReasoningInstrumentation.Begin();
                SatVerdict verdict = SolveShared(frame.Assumptions);
                ReasoningInstrumentation.End(ReasoningPhase.SatSolve, worldSolveStart);
                RecordSolve(verdict.Statistics);
                if(!verdict.IsSatisfiable)
                {
                    memo.Record(frame.Key, WorldState.Unsatisfiable);

                    //Tentative verdicts conditional on this world's
                    //satisfiability lose their premise with the refutation.
                    if(frame.AttachedTentatives is not null)
                    {
                        foreach(int[] dependent in frame.AttachedTentatives)
                        {
                            tentativeDepth.Remove(dependent);
                        }
                    }

                    stack.RemoveAt(stack.Count - 1);
                    if(stack.Count == 0)
                    {
                        return false;
                    }

                    //The parent learns the modal conflict clause over the
                    //failed successor's conflict core — TBox-entailed, so
                    //sound in every world — and re-solves its unchanged
                    //assumptions against the grown CNF.
                    WorldFrame parent = stack[^1];
                    SuccessorPlan failedPlan = parent.Successors![parent.NextSuccessor];
                    cnf.Append(minimizeCores
                        ? MinimizeConflictClause(failedPlan)
                        : failedPlan.ConflictClause);
                    parent.Successors = null;

                    continue;
                }

                bool[] model = Shrink(verdict.Assignment!, frame.Assumptions);

                //Theory check: a datatype clash among the model's true data atoms
                //teaches the shared formula its conflict clause and the world
                //re-solves, exactly as a failed modal successor does.
                if(CheckModelData(model, out IReadOnlyList<SatLiteral> dataConflict) == DataConsistencyStatus.Clash)
                {
                    cnf.Append(dataConflict);

                    continue;
                }

                frame.Successors = PlanSuccessors(model);
                frame.NextSuccessor = 0;

                continue;
            }

            if(frame.NextSuccessor >= frame.Successors.Count)
            {
                //Every successor satisfiable: the world is satisfiable. The
                //verdict enters the memo only when it stands on its own —
                //a subtree blocked against a STRICTLY SHALLOWER in-progress
                //ancestor makes it conditional on that ancestor ending
                //satisfiable, and a conditional verdict cached past its
                //context could outlive the ancestor's refutation. A
                //dependency at the frame's own depth is discharged by this
                //very pop. The conditional dependency propagates to the
                //parent either way.
                if(frame.DependencyMin >= stack.Count - 1)
                {
                    memo.Record(frame.Key, WorldState.Satisfiable);

                    //The verdicts that were conditional on this world
                    //discharge with it and settle.
                    if(frame.AttachedTentatives is not null)
                    {
                        foreach(int[] dependent in frame.AttachedTentatives)
                        {
                            memo.Record(dependent, WorldState.Satisfiable);
                            tentativeDepth.Remove(dependent);
                        }
                    }
                }
                else
                {
                    //A conditional verdict parks on the frame it depends on
                    //— reusable as a tentative answer while that ancestor
                    //lives, settled or discarded at its pop. Verdicts that
                    //were conditional on THIS frame transfer with it: this
                    //world ends satisfiable only if the ancestor does.
                    WorldFrame holder = stack[frame.DependencyMin];
                    holder.AttachedTentatives ??= [];
                    holder.AttachedTentatives.Add(frame.Key);
                    tentativeDepth[frame.Key] = frame.DependencyMin;
                    if(frame.AttachedTentatives is not null)
                    {
                        foreach(int[] dependent in frame.AttachedTentatives)
                        {
                            holder.AttachedTentatives.Add(dependent);
                            tentativeDepth[dependent] = frame.DependencyMin;
                        }
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                if(stack.Count > 0)
                {
                    WorldFrame parent = stack[^1];
                    parent.DependencyMin = Math.Min(parent.DependencyMin, frame.DependencyMin);
                    parent.NextSuccessor++;
                }

                continue;
            }

            SuccessorPlan successor = frame.Successors[frame.NextSuccessor];
            if(memo.Answer(successor.Key) is WorldState known)
            {
                if(known == WorldState.Unsatisfiable)
                {
                    //The memo answers without a solver call, but the modal
                    //clause for this parent's atom combination is still
                    //learned: it is what forces the re-solve to move on.
                    cnf.Append(minimizeCores
                        ? MinimizeConflictClause(successor)
                        : successor.ConflictClause);
                    frame.Successors = null;

                    continue;
                }

                //A settled satisfiable answer affirms unconditionally.
                frame.NextSuccessor++;

                continue;
            }

            long blockingStart = ReasoningInstrumentation.Begin();
            int blockingAncestor = BlockingAncestorIndex(stack, successor.Key);
            ReasoningInstrumentation.End(ReasoningPhase.Blocking, blockingStart);
            if(blockingAncestor >= 0)
            {
                //Subset blocking affirms tentatively: the affirmation is
                //conditional on the blocking ancestor itself ending
                //satisfiable, so the frame records the dependency for the
                //memo-write decision at its own pop.
                frame.DependencyMin = Math.Min(frame.DependencyMin, blockingAncestor);
                frame.NextSuccessor++;

                continue;
            }

            if(tentativeDepth.TryGetValue(successor.Key, out int tentativeAncestor))
            {
                //A repeat of a set that already popped satisfiable
                //conditional on a live ancestor affirms exactly like a
                //blocking hit: the consumer inherits the dependency on
                //that ancestor.
                frame.DependencyMin = Math.Min(frame.DependencyMin, tentativeAncestor);
                frame.NextSuccessor++;

                continue;
            }

            stack.Add(new WorldFrame(successor.Key, successor.Assumptions));
        }

        return true;
    }

    /// <summary>
    /// The stack index of the shallowest in-progress ancestor whose
    /// assumption set is a superset of the candidate's — the subset
    /// blocking that terminates cyclic TBox expansion, equal sets
    /// included. Every frame on the stack is an in-progress ancestor of
    /// the candidate, and the index identifies which frame the tentative
    /// affirmation depends on.
    /// </summary>
    /// <param name="stack">The frame stack.</param>
    /// <param name="candidateKey">The candidate world's canonical key.</param>
    /// <returns>The blocking ancestor's stack index, or <c>-1</c> when no ancestor's set subsumes the candidate's.</returns>
    private static int BlockingAncestorIndex(List<WorldFrame> stack, int[] candidateKey)
    {
        for(int i = 0; i < stack.Count; i++)
        {
            if(IsSubsetOf(candidateKey, stack[i].Key))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether one sorted canonical key is a subset of another, by a two-pointer merge walk; span-typed so array-backed and scratch-backed keys compare alike.</summary>
    /// <param name="candidate">The candidate subset, sorted ascending.</param>
    /// <param name="container">The candidate superset, sorted ascending.</param>
    /// <returns><see langword="true"/> when every element of <paramref name="candidate"/> occurs in <paramref name="container"/>.</returns>
    private static bool IsSubsetOf(ReadOnlySpan<int> candidate, ReadOnlySpan<int> container)
    {
        int containerIndex = 0;
        foreach(int element in candidate)
        {
            while(containerIndex < container.Length && container[containerIndex] < element)
            {
                containerIndex++;
            }

            if(containerIndex >= container.Length || container[containerIndex] != element)
            {
                return false;
            }

            containerIndex++;
        }

        return true;
    }

    /// <summary>
    /// The greedy model-shrink pass: for each true variable in descending
    /// variable order, tentatively flip it to false and keep the flip when
    /// every clause stays satisfied. Connective variables shrink alongside
    /// modal atoms — a gratuitously-true connective is what forces its
    /// modal operands true, so freeing it first lets the operands shrink in
    /// the same pass. Assumption literals and the reserved variable 0 are
    /// never flipped.
    /// </summary>
    /// <param name="assignment">The solver's satisfying assignment.</param>
    /// <param name="assumptions">The world's assumption literals, whose variables stay pinned.</param>
    /// <returns>The shrunk model.</returns>
    private bool[] Shrink(IReadOnlyList<bool> assignment, SatLiteral[] assumptions)
    {
        bool[] model = new bool[assignment.Count];
        for(int i = 0; i < model.Length; i++)
        {
            model[i] = assignment[i];
        }

        shrinkPinnedScratch.Clear();
        foreach(SatLiteral assumption in assumptions)
        {
            shrinkPinnedScratch.Add(assumption.Variable);
        }

        //The occurrence index syncs against the CNF here — at shrink time, on its
        //own cursor — so both solve lanes and every interleaved clause append see
        //exactly the clause set a per-call rebuild would; only the per-clause
        //true-literal counts depend on the model, so only they refill per shrink.
        SyncShrinkIndex();
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses = cnf.Clauses;
        for(int clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            IReadOnlyList<SatLiteral> clause = clauses[clauseIndex];
            int trueCount = 0;
            for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
            {
                SatLiteral literal = clause[literalIndex];
                if(model[literal.Variable] == literal.IsPositive)
                {
                    trueCount++;
                }
            }

            shrinkTrueCounts[clauseIndex] = trueCount;
        }

        ShrinkInPlace(model, shrinkPinnedScratch, shrinkTrueCounts, shrinkPositiveOccurrences, shrinkNegativeOccurrences);

        return model;
    }

    /// <summary>
    /// Syncs the persistent shrink occurrence index to the shared CNF: grows the
    /// per-variable occurrence arrays to the current variable count and ingests the
    /// clauses appended since the last sync, so the index's content stays exactly
    /// what a per-call rebuild over the whole clause list would hold.
    /// </summary>
    private void SyncShrinkIndex()
    {
        if(cnf.VariableCount > shrinkPositiveOccurrences.Length)
        {
            int newWidth = Math.Max(cnf.VariableCount, shrinkPositiveOccurrences.Length * 2);
            Array.Resize(ref shrinkPositiveOccurrences, newWidth);
            Array.Resize(ref shrinkNegativeOccurrences, newWidth);
        }

        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses = cnf.Clauses;
        if(clauses.Count > shrinkTrueCounts.Length)
        {
            shrinkTrueCounts = new int[Math.Max(clauses.Count, shrinkTrueCounts.Length * 2)];
        }

        for(int clauseIndex = shrinkIngestedCount; clauseIndex < clauses.Count; clauseIndex++)
        {
            IReadOnlyList<SatLiteral> clause = clauses[clauseIndex];
            for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
            {
                SatLiteral literal = clause[literalIndex];
                List<int>?[] occurrences = literal.IsPositive ? shrinkPositiveOccurrences : shrinkNegativeOccurrences;
                (occurrences[literal.Variable] ??= []).Add(clauseIndex);
            }
        }

        shrinkIngestedCount = clauses.Count;
    }

    /// <summary>
    /// Checks the data atoms a world's model leaves true for datatype
    /// consistency against the module's data-property RBox, building the conflict
    /// clause to learn on a clash: the negations of exactly the atoms whose joint
    /// presence the checker refutes. An undecided obligation sets the decision's
    /// fragment-relative mark and is otherwise treated as no information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The learned clause is sound and reusable across the whole decision. Both
    /// the RBox and the shared CNF are decision-scoped: the box is built once from
    /// the module axioms and constant for the decision, its constraints
    /// (functionality, disjointness, sub-property ranges) are global axioms that
    /// hold of every individual, and the CNF, the world memo, and any incremental
    /// session are constructed fresh in the constructor and disposed at the
    /// decision's end. A box-driven clash's clause — the negation of the pooled
    /// demand atoms — is therefore a consequence of the module's TBox-plus-RBox,
    /// so it holds in every world the decision explores and in every per-individual
    /// block of a joint instance, and it cannot outlive the decision to reach a
    /// module whose box would not force it. So there is no box-driven versus
    /// demand-only distinction to draw: every datatype-conflict clash learns its
    /// clause exactly as before.
    /// </para>
    /// </remarks>
    /// <param name="model">The world's shrunk model over the variable space the data atoms index.</param>
    /// <param name="conflictClause">The clause to learn when the result is a clash, otherwise empty.</param>
    /// <returns>The datatype-consistency verdict of the model.</returns>
    private DataConsistencyStatus CheckModelData(bool[] model, out IReadOnlyList<SatLiteral> conflictClause)
    {
        conflictClause = [];
        List<AlcConcept> active = checkModelDataActiveScratch;
        active.Clear();
        IReadOnlyList<ConceptCnf.DataAtom> dataAtoms = cnf.DataAtoms;
        for(int atomIndex = 0; atomIndex < dataAtoms.Count; atomIndex++)
        {
            ConceptCnf.DataAtom atom = dataAtoms[atomIndex];
            if(atom.Variable < model.Length && model[atom.Variable])
            {
                active.Add(atom.Concept);
            }
        }

        DataConsistencyStatus status = DataRestrictionConsistency.Decide(active, translation.Box, gate: null, registry, out IReadOnlyList<AlcConcept> conflict, out bool selfCertified);
        dataSelfCertified |= selfCertified;
        if(status == DataConsistencyStatus.Undecided)
        {
            dataUndecided = true;
        }
        else if(status == DataConsistencyStatus.Clash)
        {
            List<SatLiteral> clause = new(conflict.Count);
            foreach(AlcConcept concept in conflict)
            {
                clause.Add(new SatLiteral(cnf.GetLiteral(concept).Variable, IsPositive: false));
            }

            conflictClause = clause;
        }

        return status;
    }

    /// <summary>
    /// Plans one successor world per existential atom true in the shrunk
    /// model: the successor carries the existential's filler plus the
    /// filler of every universal atom true in the model whose role the
    /// existential's role reaches through the told role hierarchy, and its
    /// conflict clause negates exactly those atoms.
    /// </summary>
    /// <param name="model">The shrunk model.</param>
    /// <returns>The successor plans, in modal-atom allocation order.</returns>
    private List<SuccessorPlan> PlanSuccessors(bool[] model)
    {
        //Snapshot the model's true modal atoms before encoding any filler:
        //encoding may register fresh modal atoms, which belong to deeper
        //worlds, not this model. An atom registered after the model solved
        //— a per-block joint model covers only the variables of its width
        //freeze — carries no truth in it and reads false. The snapshot lists
        //are cleared instance scratch; the planner never nests, and the
        //returned plans are built fresh.
        List<(AlcExists Concept, int Variable)> trueExistentials = planExistentialsScratch;
        List<(AlcForAll Concept, int Variable)> trueUniversals = planUniversalsScratch;
        trueExistentials.Clear();
        trueUniversals.Clear();
        IReadOnlyList<ConceptCnf.ModalAtom> modalAtoms = cnf.ModalAtoms;
        for(int atomIndex = 0; atomIndex < modalAtoms.Count; atomIndex++)
        {
            ConceptCnf.ModalAtom atom = modalAtoms[atomIndex];
            if(atom.Variable >= model.Length || !model[atom.Variable])
            {
                continue;
            }

            switch(atom.Concept)
            {
                case AlcExists exists:
                {
                    trueExistentials.Add((exists, atom.Variable));

                    break;
                }

                case AlcForAll forAll:
                {
                    trueUniversals.Add((forAll, atom.Variable));

                    break;
                }

                default:
                {
                    break;
                }
            }
        }

        List<SuccessorPlan> plans = new(trueExistentials.Count);
        foreach((AlcExists exists, int existsVariable) in trueExistentials)
        {
            List<SatLiteral> literals = [cnf.GetLiteral(exists.Filler)];
            List<SatLiteral> conflict = [new SatLiteral(existsVariable, IsPositive: false)];
            List<SuccessorContributor> contributors = [new SuccessorContributor(literals[0], conflict[0])];
            foreach((AlcForAll forAll, int forAllVariable) in trueUniversals)
            {
                if(RoleReaches(exists.Role.Iri, forAll.Role.Iri, translation.SuperRoles))
                {
                    SatLiteral filler = cnf.GetLiteral(forAll.Filler);
                    SatLiteral negatedAtom = new(forAllVariable, IsPositive: false);
                    literals.Add(filler);
                    conflict.Add(negatedAtom);
                    contributors.Add(new SuccessorContributor(filler, negatedAtom));
                    AppendTransitiveUniversals(exists.Role.Iri, forAll, negatedAtom, literals, contributors);
                }
            }

            (SatLiteral[] assumptions, int[] key) = Canonicalize(literals);
            plans.Add(new SuccessorPlan(key, assumptions, [.. conflict], [.. contributors]));
        }

        return plans;
    }

    /// <summary>
    /// The ∀⁺-rule for a spawned successor: carries <c>∀R.C</c> into the
    /// successor's label for every transitive role <c>R</c> on the told chain
    /// <c>edgeRole ⊑* R ⊑* forAll.Role</c> (the caller has established that
    /// <paramref name="edgeRole"/> reaches <paramref name="forAll"/>'s role),
    /// so the universal re-propagates down the transitive chain. Each carried
    /// universal is a CONSEQUENCE of <paramref name="forAll"/>, so its
    /// contributor pairs the carried label literal with the SAME premise
    /// (<paramref name="premise"/>, the negated ∀S.C atom) the universal
    /// itself contributes: the learned conflict clause stays the negation of
    /// the real premises, while the conflict-core minimization still sees the
    /// carried universal in the probed label.
    /// </summary>
    /// <param name="edgeRole">The existential's role — the spawned edge's role.</param>
    /// <param name="forAll">The universal being propagated into the successor.</param>
    /// <param name="premise">The negated ∀S.C atom literal the carried universals share as their conflict contribution.</param>
    /// <param name="literalsToAppendTo">The successor's label literals the carried universals append to.</param>
    /// <param name="contributorsToAppendTo">The successor's contributors the carried universals append to.</param>
    private void AppendTransitiveUniversals(
        Utf8String edgeRole,
        AlcForAll forAll,
        SatLiteral premise,
        List<SatLiteral> literalsToAppendTo,
        List<SuccessorContributor> contributorsToAppendTo)
    {
        if(translation.TransitiveRoles.Count == 0)
        {
            return;
        }

        List<Utf8String> carriedRoles = [];
        CollectTransitiveCarryRoles(edgeRole, forAll, carriedRoles);
        foreach(Utf8String carriedRole in carriedRoles)
        {
            SatLiteral carried = cnf.GetLiteral(new AlcForAll(AlcRole.Forward(carriedRole), forAll.Filler));
            literalsToAppendTo.Add(carried);
            contributorsToAppendTo.Add(new SuccessorContributor(carried, premise));
        }
    }

    /// <summary>
    /// The transitive roles <c>R</c> on the told chain
    /// <c>edgeRole ⊑* R ⊑* forAll.Role</c> — the roles the ∀⁺-rule
    /// re-propagates <paramref name="forAll"/> along. The caller has already
    /// established that <paramref name="edgeRole"/> reaches
    /// <paramref name="forAll"/>'s role, so <paramref name="edgeRole"/> itself
    /// qualifies whenever it is transitive; its told super-roles qualify when
    /// they are transitive and still reach the universal's role.
    /// </summary>
    /// <param name="edgeRole">The edge's role.</param>
    /// <param name="forAll">The universal being propagated.</param>
    /// <param name="rolesToAppendTo">The list the qualifying transitive roles append to.</param>
    private void CollectTransitiveCarryRoles(Utf8String edgeRole, AlcForAll forAll, List<Utf8String> rolesToAppendTo)
    {
        if(translation.TransitiveRoles.Contains(edgeRole))
        {
            rolesToAppendTo.Add(edgeRole);
        }

        if(translation.SuperRoles.TryGetValue(edgeRole, out HashSet<Utf8String>? supers))
        {
            foreach(Utf8String candidate in supers)
            {
                if(translation.TransitiveRoles.Contains(candidate) && RoleReaches(candidate, forAll.Role.Iri, translation.SuperRoles))
                {
                    rolesToAppendTo.Add(candidate);
                }
            }
        }
    }

    /// <summary>
    /// The modal conflict clause a failed successor teaches the shared CNF,
    /// shrunk to a conflict core by greedy contributor deletion: each
    /// contributor in turn is dropped and the remaining label re-checked —
    /// still unsatisfiable means the contributor was not load-bearing and
    /// stays dropped. Every retained probe verdict enters the shared memo,
    /// and every candidate label's unsatisfiability is a genuine
    /// TBox-relative refutation. The emitted clause keeps the existential
    /// trigger unconditionally — its entailment argument runs through the
    /// successor the trigger forces, never through universal atoms alone,
    /// which hold vacuously in a world without one — plus the universal
    /// contributors the core needs, so it is TBox-entailed exactly as the
    /// full-set clause is while pruning whole families of modal-atom
    /// combinations instead of one.
    /// </summary>
    /// <param name="plan">The failed successor's plan.</param>
    /// <returns>The minimized conflict clause.</returns>
    private SatLiteral[] MinimizeConflictClause(SuccessorPlan plan)
    {
        //The core is a property of the refuted LABEL, not of the parent
        //combination that built it, so distinct parents reaching the same
        //label share the probe work through the memo's core cache.
        int[]? core = memo.CoreFor(plan.Key);
        if(core is null)
        {
            List<SuccessorContributor> kept = [.. plan.Contributors];

            //Universal contributors drop first (the existential is index 0
            //and most often load-bearing), each by one world check over the
            //remaining label. Each probe's label fills the shared scratch and a
            //memoized answer materializes nothing.
            for(int candidate = kept.Count - 1; candidate >= 0 && kept.Count > 1; candidate--)
            {
                labelLiteralsScratch.Clear();
                for(int i = 0; i < kept.Count; i++)
                {
                    if(i != candidate)
                    {
                        labelLiteralsScratch.Add(kept[i].Filler);
                    }
                }

                if(!CheckWorldForLabel(labelLiteralsScratch, minimizeCores: false))
                {
                    kept.RemoveAt(candidate);
                }
            }

            labelLiteralsScratch.Clear();
            foreach(SuccessorContributor contributor in kept)
            {
                labelLiteralsScratch.Add(contributor.Filler);
            }

            (SatLiteral[] _, core) = Canonicalize(labelLiteralsScratch);
            memo.RecordCore(plan.Key, core);
        }

        //The existential trigger always enters the clause: the clause's
        //entailment runs through the successor the trigger forces to carry
        //the core, and a world whose universal atoms all hold vacuously
        //asserts no successor, so it lies outside the clause's claim.
        //Universal contributors enter only when the core needs their
        //filler.
        List<SatLiteral> clause = new(plan.Contributors.Length)
        {
            plan.Contributors[0].Conflict,
        };
        for(int i = 1; i < plan.Contributors.Length; i++)
        {
            SuccessorContributor contributor = plan.Contributors[i];

            //A universal and the ∀⁺-carried universals it forces share one
            //premise literal, so the clause dedups: a premise enters once
            //however many of its consequences the core retains.
            if(Array.BinarySearch(core, Encode(contributor.Filler)) >= 0 && !clause.Contains(contributor.Conflict))
            {
                clause.Add(contributor.Conflict);
            }
        }

        return [.. clause];
    }

    /// <summary>Whether the existential's role reaches the universal's role through the told hierarchy, reflexively.</summary>
    /// <param name="edgeRole">The existential's role — the role of the successor edge.</param>
    /// <param name="restrictionRole">The universal's role.</param>
    /// <param name="superRoles">Per-role reflexive-transitive told super-roles.</param>
    /// <returns><see langword="true"/> when the universal applies over the edge.</returns>
    private static bool RoleReaches(Utf8String edgeRole, Utf8String restrictionRole, Dictionary<Utf8String, HashSet<Utf8String>> superRoles)
    {
        return edgeRole.Equals(restrictionRole)
            || (superRoles.TryGetValue(edgeRole, out HashSet<Utf8String>? supers) && supers.Contains(restrictionRole));
    }

    /// <summary>
    /// The canonical form of a world's assumption literals: distinct and
    /// sorted by encoded value, paired with the encoded key the memo table
    /// indexes by. The composed form for the sites whose pair is retained by
    /// construction — seed lists, successor plans, joint assumptions, the
    /// minimized core; a probe-then-maybe-solve site canonicalizes into the
    /// scratch instead and materializes only past the memo.
    /// </summary>
    /// <param name="literals">The assumption literals, possibly duplicated and unordered.</param>
    /// <returns>The canonical assumptions and their key.</returns>
    private (SatLiteral[] Assumptions, int[] Key) Canonicalize(List<SatLiteral> literals)
    {
        CanonicalizeIntoScratch(literals);

        return MaterializeCanonicalScratch();
    }

    /// <summary>
    /// Canonicalizes a world's assumption literals into the shared key scratch —
    /// sorted distinct encoded codes — allocating nothing; the scratch holds the
    /// key until the next canonicalization runs.
    /// </summary>
    /// <param name="literals">The assumption literals, possibly duplicated and unordered.</param>
    private void CanonicalizeIntoScratch(List<SatLiteral> literals)
    {
        canonicalCodesScratch.Clear();
        for(int literalIndex = 0; literalIndex < literals.Count; literalIndex++)
        {
            canonicalCodesScratch.Add(Encode(literals[literalIndex]));
        }

        canonicalCodesScratch.Sort();

        canonicalKeyScratch.Clear();
        for(int codeIndex = 0; codeIndex < canonicalCodesScratch.Count; codeIndex++)
        {
            int code = canonicalCodesScratch[codeIndex];
            if(canonicalKeyScratch.Count == 0 || canonicalKeyScratch[^1] != code)
            {
                canonicalKeyScratch.Add(code);
            }
        }
    }

    /// <summary>
    /// Materializes the exact-size canonical assumption and key arrays from the
    /// key scratch the last <see cref="CanonicalizeIntoScratch"/> filled — the
    /// allocation a probe-first site pays only when the memo had no answer and the
    /// pair is actually retained.
    /// </summary>
    /// <returns>The canonical assumptions and their key.</returns>
    private (SatLiteral[] Assumptions, int[] Key) MaterializeCanonicalScratch()
    {
        SatLiteral[] assumptions = new SatLiteral[canonicalKeyScratch.Count];
        int[] key = new int[canonicalKeyScratch.Count];
        for(int i = 0; i < canonicalKeyScratch.Count; i++)
        {
            key[i] = canonicalKeyScratch[i];
            assumptions[i] = Decode(key[i]);
        }

        return (assumptions, key);
    }

    /// <summary>The literal's canonical integer code: the variable shifted left once, with the polarity in the low bit.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns>The code.</returns>
    private static int Encode(SatLiteral literal)
    {
        return (literal.Variable << 1) | (literal.IsPositive ? 1 : 0);
    }

    /// <summary>The literal a canonical integer code stands for.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The literal.</returns>
    private static SatLiteral Decode(int code)
    {
        return new SatLiteral(code >> 1, IsPositive: (code & 1) != 0);
    }

    /// <summary>
    /// The settled-verdict memo over canonical assumption-set keys. A lookup
    /// answers exact hits and monotone consequences: satisfiability is
    /// downward-monotone over assumption sets — a subset of a settled
    /// satisfiable set is satisfiable — and unsatisfiability is
    /// upward-monotone — a superset of a settled unsatisfiable set is
    /// unsatisfiable. Both directions hold at the world level (the verdict
    /// covers the modal subtree, and removing label concepts weakens the
    /// world) and survive clause growth, because every appended clause is
    /// TBox-entailed. Only settled verdicts enter; tentative blocked-
    /// dependent affirmations stay on the frame stack.
    /// </summary>
    private sealed class WorldMemo
    {
        /// <summary>The settled verdicts by exact canonical key.</summary>
        private readonly Dictionary<int[], WorldState> settled = new(CanonicalKeyComparer.Instance);

        /// <summary>The span-keyed view over <see cref="settled"/>, so a scratch-backed key answers without materializing an array.</summary>
        private readonly Dictionary<int[], WorldState>.AlternateLookup<ReadOnlySpan<int>> settledBySpan;

        /// <summary>The settled satisfiable keys, scanned for superset answers.</summary>
        private readonly List<int[]> satisfiableKeys = [];

        /// <summary>The settled unsatisfiable keys, scanned for subset answers.</summary>
        private readonly List<int[]> unsatisfiableKeys = [];

        /// <summary>The minimized conflict cores by refuted-label key: the canonical key of the label subset whose unsatisfiability the minimization probes established.</summary>
        private readonly Dictionary<int[], int[]> conflictCores = new(CanonicalKeyComparer.Instance);

        /// <summary>Binds the span-keyed lookup view to the settled table.</summary>
        public WorldMemo()
        {
            settledBySpan = settled.GetAlternateLookup<ReadOnlySpan<int>>();
        }

        /// <summary>Records a settled verdict for the key; a key already settled keeps its verdict (clause growth never flips a settled verdict).</summary>
        /// <param name="key">The canonical key.</param>
        /// <param name="state">The settled verdict.</param>
        public void Record(int[] key, WorldState state)
        {
            if(settled.TryAdd(key, state))
            {
                List<int[]> keys = state == WorldState.Satisfiable ? satisfiableKeys : unsatisfiableKeys;
                keys.Add(key);
            }
        }

        /// <summary>
        /// The settled verdict the memo can answer for the key: an exact
        /// hit, a settled satisfiable superset (downward monotonicity), or
        /// a settled unsatisfiable subset (upward monotonicity);
        /// <see langword="null"/> when nothing settled answers it. Span-typed
        /// so a scratch-backed key probes without materializing an array; an
        /// <c>int[]</c> key converts implicitly.
        /// </summary>
        /// <param name="key">The canonical key to answer.</param>
        /// <returns>The answered verdict, or <see langword="null"/>.</returns>
        public WorldState? Answer(ReadOnlySpan<int> key)
        {
            if(settledBySpan.TryGetValue(key, out WorldState exact))
            {
                return exact;
            }

            foreach(int[] satisfiable in satisfiableKeys)
            {
                if(IsSubsetOf(key, satisfiable))
                {
                    return WorldState.Satisfiable;
                }
            }

            foreach(int[] unsatisfiable in unsatisfiableKeys)
            {
                if(IsSubsetOf(unsatisfiable, key))
                {
                    return WorldState.Unsatisfiable;
                }
            }

            return null;
        }

        /// <summary>The cached minimized conflict core for a refuted label key, or <see langword="null"/> when none was recorded.</summary>
        /// <param name="key">The refuted label's canonical key.</param>
        /// <returns>The core's canonical key, or <see langword="null"/>.</returns>
        public int[]? CoreFor(int[] key)
        {
            return conflictCores.TryGetValue(key, out int[]? core) ? core : null;
        }

        /// <summary>Records the minimized conflict core for a refuted label key.</summary>
        /// <param name="key">The refuted label's canonical key.</param>
        /// <param name="core">The core's canonical key.</param>
        public void RecordCore(int[] key, int[] core)
        {
            conflictCores[key] = core;
        }
    }

    /// <summary>One in-progress world check on the explicit frame stack.</summary>
    private sealed class WorldFrame
    {
        /// <summary>Captures the world's canonical identity.</summary>
        /// <param name="key">The canonical key.</param>
        /// <param name="assumptions">The assumption literals, canonical.</param>
        public WorldFrame(int[] key, SatLiteral[] assumptions)
        {
            Key = key;
            Assumptions = assumptions;
        }

        /// <summary>The world's canonical key.</summary>
        public int[] Key { get; }

        /// <summary>The world's assumption literals, sorted and distinct.</summary>
        public SatLiteral[] Assumptions { get; }

        /// <summary>The successors planned from the current model, or <see langword="null"/> when the world needs solving or re-solving.</summary>
        public List<SuccessorPlan>? Successors { get; set; }

        /// <summary>
        /// The canonical keys of worlds whose satisfiable verdicts are
        /// conditional on this world ending satisfiable: they settle into
        /// the memo when this frame pops satisfiable on its own, transfer
        /// to a shallower holder when this frame pops conditionally, and
        /// discard when this world is refuted.
        /// </summary>
        public List<int[]>? AttachedTentatives { get; set; }

        /// <summary>The index of the successor currently being checked.</summary>
        public int NextSuccessor { get; set; }

        /// <summary>
        /// The shallowest stack index this frame's subtree blocked against,
        /// directly or through a satisfiable child; <see cref="int.MaxValue"/>
        /// when the subtree stands on its own. A satisfiable verdict enters
        /// the memo only when this is no shallower than the frame's own
        /// index — a tentative affirmation must not outlive the ancestor it
        /// depends on.
        /// </summary>
        public int DependencyMin { get; set; } = int.MaxValue;
    }

    /// <summary>One modal atom's contribution to a successor world: the filler literal it adds to the successor's label and the negated atom literal it adds to the conflict clause.</summary>
    /// <param name="Filler">The contributor's filler literal in the successor's label.</param>
    /// <param name="Conflict">The contributor's negated modal-atom literal in the conflict clause.</param>
    private readonly record struct SuccessorContributor(SatLiteral Filler, SatLiteral Conflict);

    /// <summary>One planned successor world and the modal conflict clause its failure would teach the shared CNF.</summary>
    /// <param name="Key">The successor's canonical key.</param>
    /// <param name="Assumptions">The successor's assumption literals, canonical.</param>
    /// <param name="ConflictClause">The negations of exactly the modal atoms that built the successor.</param>
    /// <param name="Contributors">The per-atom contributions, the existential first — the unit the conflict-core minimization deletes over.</param>
    private sealed record SuccessorPlan(int[] Key, SatLiteral[] Assumptions, SatLiteral[] ConflictClause, SuccessorContributor[] Contributors);

    /// <summary>One build of the joint instance over the named forest: the template instantiated per individual block as the template stood at the width freeze.</summary>
    /// <param name="Clauses">The joint clauses — every template clause in every block, plus the asserted-edge propagation clauses.</param>
    /// <param name="VariableCount">The joint variable count, the block width times the block count.</param>
    /// <param name="Assumptions">The joint assumptions — each individual's asserted-concept literals offset into its block, canonical.</param>
    /// <param name="BlockWidth">The frozen per-block variable count.</param>
    /// <param name="BlockCount">The number of individual blocks.</param>
    /// <param name="ModalVariables">The template variables of the modal atoms registered at the freeze, ascending.</param>
    private sealed record JointInstance(
        List<IReadOnlyList<SatLiteral>> Clauses,
        int VariableCount,
        SatLiteral[] Assumptions,
        int BlockWidth,
        int BlockCount,
        List<int> ModalVariables);

    /// <summary>Element-wise equality and hashing over canonical key arrays, with the span face that lets a scratch-backed key probe the settled table without materializing; both faces hash by the same element fold, so array and span forms of one key always agree.</summary>
    private sealed class CanonicalKeyComparer: IEqualityComparer<int[]>, IAlternateEqualityComparer<ReadOnlySpan<int>, int[]>
    {
        /// <summary>The shared instance.</summary>
        public static CanonicalKeyComparer Instance { get; } = new();

        /// <inheritdoc/>
        public bool Equals(int[]? x, int[]? y)
        {
            if(ReferenceEquals(x, y))
            {
                return true;
            }

            return x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        }

        /// <inheritdoc/>
        public int GetHashCode(int[] obj)
        {
            HashCode hash = new();
            foreach(int element in obj)
            {
                hash.Add(element);
            }

            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(ReadOnlySpan<int> alternate, int[] other)
        {
            return alternate.SequenceEqual(other);
        }

        /// <inheritdoc/>
        public int GetHashCode(ReadOnlySpan<int> alternate)
        {
            HashCode hash = new();
            foreach(int element in alternate)
            {
                hash.Add(element);
            }

            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public int[] Create(ReadOnlySpan<int> alternate)
        {
            return alternate.ToArray();
        }
    }
}
