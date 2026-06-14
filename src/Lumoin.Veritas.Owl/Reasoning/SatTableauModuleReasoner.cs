using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sat;

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
internal sealed class SatTableauModuleReasoner
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

    /// <summary>The number of axioms in the decided module, reported on the decision statistics.</summary>
    private readonly int moduleAxiomCount;

    /// <summary>The running count of world solves the decision has spent.</summary>
    private int solveCount;

    /// <summary>The solver counters accumulated across the decision's world solves.</summary>
    private SatSolveStatistics runningTally;

    /// <summary>
    /// Opens a decision session over a module: translates it, asserts the
    /// internalized TBox into the shared CNF once, and fixes the search mode
    /// and token for the decision. The session is single-use and
    /// decision-scoped — one per <see cref="Decide"/> or
    /// <see cref="DecideConsistency"/> call — so the shared CNF, memo, and
    /// learned clauses never leak between decisions.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    private SatTableauModuleReasoner(ReasoningModule module, SatSearchMode searchMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(module);

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
        this.searchMode = searchMode;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>Folds one world solve's statistics into the decision's running totals.</summary>
    /// <param name="statistics">The solve's reported statistics.</param>
    private void RecordSolve(in SatSolveStatistics statistics)
    {
        solveCount++;
        runningTally = SatSolveStatistics.Combine(runningTally, statistics);
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
    /// <returns>The delegate for <see cref="ReasoningRendezvous"/> wiring.</returns>
    public static DescriptionLogicDelegate CreateDelegate(SatSearchMode searchMode = SatSearchMode.ConflictLearning)
    {
        return (module, cancellationToken) => ValueTask.FromResult(DecideModule(module, searchMode, cancellationToken));
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
        return new SatTableauModuleReasoner(module, searchMode, cancellationToken).RunDecision(includeSubsumptions: true).Verdict!;
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
        return new SatTableauModuleReasoner(module, searchMode, cancellationToken).RunDecision(includeSubsumptions: false).Verdict!;
    }

    /// <summary>
    /// Decides the module as a full <see cref="ModuleDecision"/> — the
    /// verdict plus the work the decision spent — the form the
    /// <see cref="DescriptionLogicDelegate"/> seam returns. Includes the
    /// module-local subsumption sweep, mirroring <see cref="Decide"/>.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The decision: the verdict and the work it spent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleDecision DecideModule(
        ReasoningModule module,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        CancellationToken cancellationToken = default)
    {
        return new SatTableauModuleReasoner(module, searchMode, cancellationToken).RunDecision(includeSubsumptions: true);
    }

    /// <summary>
    /// Runs the consistency check over the shared CNF — the joint instance
    /// when asserted role edges are present, per-seed worlds otherwise —
    /// optionally following with the module-local subsumption sweep against
    /// the same CNF and memo table, and wraps the verdict with the work the
    /// decision spent.
    /// </summary>
    /// <param name="includeSubsumptions">Whether to enumerate module-local subsumptions when the signature qualifies.</param>
    /// <returns>The decision.</returns>
    private ModuleDecision RunDecision(bool includeSubsumptions)
    {
        bool isConsistent = translation.Edges.Count > 0
            ? CheckJointRoot()
            : CheckAllSeedWorlds();

        List<(NamedNode SubClass, NamedNode SuperClass)> subsumptions =
            includeSubsumptions && isConsistent && translation.SignatureClasses.Count <= AlcModuleReasoner.SubsumptionSignatureCap
                ? SweepSubsumptions()
                : [];

        ModuleVerdict verdict = new(isConsistent, subsumptions)
        {
            UnsupportedConstructs = translation.Unsupported,
        };

        return ModuleDecision.Decided(verdict, new ReasoningDecisionStatistics(moduleAxiomCount, solveCount, runningTally));
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

                List<SatLiteral> literals =
                [
                    cnf.GetLiteral(new AlcAtom(subClass)),
                    cnf.GetLiteral(new AlcNot(new AlcAtom(superClass))),
                ];
                (SatLiteral[] assumptions, int[] key) = Canonicalize(literals);
                if(!CheckWorld(assumptions, key, minimizeCores: true))
                {
                    subsumptions.Add((new NamedNode(subClass), new NamedNode(superClass)));
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

            List<SatLiteral> literals = new(entry.Value.Count);
            foreach(AlcConcept concept in entry.Value)
            {
                literals.Add(cnf.GetLiteral(concept));
            }

            (SatLiteral[] assumptions, int[] key) = Canonicalize(literals);
            if(!CheckWorld(assumptions, key, minimizeCores: true))
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

            JointInstance joint = BuildJointInstance(individuals, blockOf, edges);
            SatVerdict verdict = SatSolver.SolveUnderAssumptions(
                joint.Clauses,
                joint.VariableCount,
                joint.Assumptions,
                pool: null,
                searchMode,
                cancellationToken);
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
                if(RoleReaches(role, forAll.Role, translation.SuperRoles))
                {
                    cnf.GetLiteral(forAll.Filler);

                    break;
                }
            }
        }

        int blockWidth = cnf.VariableCount;
        int blockCount = individuals.Count;

        List<IReadOnlyList<SatLiteral>> clauses = new(cnf.Clauses.Count * blockCount);
        for(int block = 0; block < blockCount; block++)
        {
            foreach(IReadOnlyList<SatLiteral> template in cnf.Clauses)
            {
                clauses.Add(ConceptCnf.InstantiateInBlock(template, block, blockWidth));
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
                if(atom.Concept is not AlcForAll forAll || !RoleReaches(role, forAll.Role, translation.SuperRoles))
                {
                    continue;
                }

                SatLiteral filler = cnf.GetLiteral(forAll.Filler);
                clauses.Add(
                [
                    new SatLiteral(ConceptCnf.BlockVariable(fromBlock, atom.Variable, blockWidth), IsPositive: false),
                    new SatLiteral(ConceptCnf.BlockVariable(toBlock, filler.Variable, blockWidth), filler.IsPositive),
                ]);
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

        List<int> modalVariables = new(cnf.ModalAtoms.Count);
        foreach(ConceptCnf.ModalAtom atom in cnf.ModalAtoms)
        {
            modalVariables.Add(atom.Variable);
        }

        return new JointInstance(clauses, blockWidth * blockCount, assumptions, blockWidth, blockCount, modalVariables);
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

        ShrinkInPlace(joint.Clauses, model, pinned);

        return model;
    }

    /// <summary>
    /// The shared greedy shrink core: walks the variables in descending
    /// order and flips each unpinned true variable to false when every
    /// clause keeps a true literal, tracked incrementally through
    /// per-clause true-literal counts over positive and negative occurrence
    /// lists. Descending order is top-down — definitional clauses only
    /// point from a connective to its lower-id operands, so one pass undoes
    /// a gratuitously-true forcing chain parent-first, freeing the modal
    /// operands for their own flips. The reserved always-true variable and
    /// its block replicas stay through their unit clauses.
    /// </summary>
    /// <param name="clauses">The clauses the shrunk model must keep satisfied.</param>
    /// <param name="model">The model to shrink in place; must satisfy <paramref name="clauses"/> on entry.</param>
    /// <param name="pinned">The variables that are never flipped.</param>
    private static void ShrinkInPlace(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses, bool[] model, HashSet<int> pinned)
    {
        int[] trueCounts = new int[clauses.Count];
        List<int>?[] positiveOccurrences = new List<int>?[model.Length];
        List<int>?[] negativeOccurrences = new List<int>?[model.Length];
        for(int clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            foreach(SatLiteral literal in clauses[clauseIndex])
            {
                if(model[literal.Variable] == literal.IsPositive)
                {
                    trueCounts[clauseIndex]++;
                }

                List<int>?[] occurrences = literal.IsPositive ? positiveOccurrences : negativeOccurrences;
                (occurrences[literal.Variable] ??= []).Add(clauseIndex);
            }
        }

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
            WorldFrame frame = stack[^1];

            if(frame.Successors is null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SatVerdict verdict = SatSolver.SolveUnderAssumptions(
                    cnf.Clauses,
                    cnf.VariableCount,
                    frame.Assumptions,
                    pool: null,
                    searchMode,
                    cancellationToken);
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

            int blockingAncestor = BlockingAncestorIndex(stack, successor.Key);
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

    /// <summary>Whether one sorted canonical key is a subset of another, by a two-pointer merge walk.</summary>
    /// <param name="candidate">The candidate subset, sorted ascending.</param>
    /// <param name="container">The candidate superset, sorted ascending.</param>
    /// <returns><see langword="true"/> when every element of <paramref name="candidate"/> occurs in <paramref name="container"/>.</returns>
    private static bool IsSubsetOf(int[] candidate, int[] container)
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

        HashSet<int> pinned = [];
        foreach(SatLiteral assumption in assumptions)
        {
            pinned.Add(assumption.Variable);
        }

        ShrinkInPlace(cnf.Clauses, model, pinned);

        return model;
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
        //freeze — carries no truth in it and reads false.
        List<(AlcExists Concept, int Variable)> trueExistentials = [];
        List<(AlcForAll Concept, int Variable)> trueUniversals = [];
        foreach(ConceptCnf.ModalAtom atom in cnf.ModalAtoms)
        {
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
                if(RoleReaches(exists.Role, forAll.Role, translation.SuperRoles))
                {
                    SatLiteral filler = cnf.GetLiteral(forAll.Filler);
                    SatLiteral negatedAtom = new(forAllVariable, IsPositive: false);
                    literals.Add(filler);
                    conflict.Add(negatedAtom);
                    contributors.Add(new SuccessorContributor(filler, negatedAtom));
                }
            }

            (SatLiteral[] assumptions, int[] key) = Canonicalize(literals);
            plans.Add(new SuccessorPlan(key, assumptions, [.. conflict], [.. contributors]));
        }

        return plans;
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
            //remaining label.
            for(int candidate = kept.Count - 1; candidate >= 0 && kept.Count > 1; candidate--)
            {
                List<SatLiteral> literals = new(kept.Count - 1);
                for(int i = 0; i < kept.Count; i++)
                {
                    if(i != candidate)
                    {
                        literals.Add(kept[i].Filler);
                    }
                }

                (SatLiteral[] assumptions, int[] key) = Canonicalize(literals);
                if(!CheckWorld(assumptions, key, minimizeCores: false))
                {
                    kept.RemoveAt(candidate);
                }
            }

            List<SatLiteral> coreLiterals = new(kept.Count);
            foreach(SuccessorContributor contributor in kept)
            {
                coreLiterals.Add(contributor.Filler);
            }

            (SatLiteral[] _, core) = Canonicalize(coreLiterals);
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
            if(Array.BinarySearch(core, Encode(contributor.Filler)) >= 0)
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
    /// indexes by.
    /// </summary>
    /// <param name="literals">The assumption literals, possibly duplicated and unordered.</param>
    /// <returns>The canonical assumptions and their key.</returns>
    private static (SatLiteral[] Assumptions, int[] Key) Canonicalize(List<SatLiteral> literals)
    {
        List<int> codes = new(literals.Count);
        foreach(SatLiteral literal in literals)
        {
            codes.Add(Encode(literal));
        }

        codes.Sort();

        List<int> key = new(codes.Count);
        foreach(int code in codes)
        {
            if(key.Count == 0 || key[^1] != code)
            {
                key.Add(code);
            }
        }

        SatLiteral[] assumptions = new SatLiteral[key.Count];
        for(int i = 0; i < key.Count; i++)
        {
            assumptions[i] = Decode(key[i]);
        }

        return (assumptions, [.. key]);
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

        /// <summary>The settled satisfiable keys, scanned for superset answers.</summary>
        private readonly List<int[]> satisfiableKeys = [];

        /// <summary>The settled unsatisfiable keys, scanned for subset answers.</summary>
        private readonly List<int[]> unsatisfiableKeys = [];

        /// <summary>The minimized conflict cores by refuted-label key: the canonical key of the label subset whose unsatisfiability the minimization probes established.</summary>
        private readonly Dictionary<int[], int[]> conflictCores = new(CanonicalKeyComparer.Instance);

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
        /// <see langword="null"/> when nothing settled answers it.
        /// </summary>
        /// <param name="key">The canonical key to answer.</param>
        /// <returns>The answered verdict, or <see langword="null"/>.</returns>
        public WorldState? Answer(int[] key)
        {
            if(settled.TryGetValue(key, out WorldState exact))
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

    /// <summary>Element-wise equality and hashing over canonical key arrays.</summary>
    private sealed class CanonicalKeyComparer: IEqualityComparer<int[]>
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
    }
}
