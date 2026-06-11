using System;
using System.Collections.Generic;
using System.Threading;
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
/// satisfying model passes a greedy shrink that flips gratuitously-true
/// modal atoms to false; each existential atom still true spawns a
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
/// </para>
/// <para>
/// <b>Fragment honesty.</b> The verdict names the beyond-fragment
/// remainder through the shared translation, identically to
/// <see cref="AlcModuleReasoner"/>: a non-empty
/// <see cref="ModuleVerdict.UnsupportedConstructs"/> scopes consistency to
/// the supported fragment.
/// </para>
/// </remarks>
internal static class SatTableauModuleReasoner
{
    /// <summary>The settled verdicts the memo table holds; a world still on the frame stack has no entry — blocking reads the stack, not the table.</summary>
    private enum WorldState
    {
        /// <summary>The world and every successor reachable from it are satisfiable, established without depending on any then-unresolved ancestor.</summary>
        Satisfiable,

        /// <summary>No model of the shared CNF honours the world's assumptions.</summary>
        Unsatisfiable,
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
        ArgumentNullException.ThrowIfNull(module);

        AlcModuleReasoner.Translation translation = AlcModuleReasoner.Translate(module);

        //The shared CNF: every world carries the internalized TBox, so its
        //conjuncts assert as facts once and worlds differ only in their
        //assumption sets.
        ConceptCnf cnf = new();
        foreach(AlcConcept conjunct in translation.TBox)
        {
            cnf.AssertFact(conjunct);
        }

        Dictionary<int[], WorldState> memo = new(CanonicalKeyComparer.Instance);

        bool isConsistent = translation.Edges.Count > 0
            ? CheckJointRoot(cnf, translation, memo, searchMode, cancellationToken)
            : CheckAllSeedWorlds(cnf, translation, memo, searchMode, cancellationToken);

        return new ModuleVerdict(isConsistent, Subsumptions: [])
        {
            UnsupportedConstructs = translation.Unsupported,
        };
    }

    /// <summary>
    /// Checks every seed world against the shared CNF: a single anonymous
    /// root with no extra assumptions when no individual carries
    /// assertions, else one world per asserted individual. The memo table
    /// and every learned clause carry across seeds.
    /// </summary>
    /// <param name="cnf">The shared CNF.</param>
    /// <param name="translation">The module's translation.</param>
    /// <param name="memo">The memo table over canonical assumption sets.</param>
    /// <param name="searchMode">The solver's search mode.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns><see langword="true"/> when every seed world is satisfiable.</returns>
    private static bool CheckAllSeedWorlds(
        ConceptCnf cnf,
        AlcModuleReasoner.Translation translation,
        Dictionary<int[], WorldState> memo,
        SatSearchMode searchMode,
        CancellationToken cancellationToken)
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
            if(!CheckWorld(cnf, translation, memo, assumptions, key, searchMode, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks the named forest as one joint SAT instance when asserted role
    /// edges are present: the template variable space replicates per named
    /// individual, every template clause instantiates into every block,
    /// asserted edges contribute universal-propagation clauses, and each
    /// individual's asserted concepts assume into its block. The joint
    /// model's true existential atoms spawn per-block successor chains in
    /// template space through <see cref="CheckWorld"/>; a chain failure
    /// appends its modal conflict clause to the template and the joint
    /// instance rebuilds and re-solves. The joint root spans blocks, so it
    /// never enters the template-space memo table — only the chains do.
    /// </summary>
    /// <remarks>
    /// The rebuild-per-solve discipline is the width-freeze design: every
    /// concept that can contribute to the joint root — the asserted
    /// concepts and, to a fixpoint, every universal filler an asserted edge
    /// can reach — encodes into the template before the block width freezes
    /// at the template's variable count, and successor chains that grow the
    /// template between solves invalidate nothing because the next build
    /// re-derives the block layout and re-instantiates every clause,
    /// learned ones included.
    /// </remarks>
    /// <param name="cnf">The shared template CNF; learned clauses append to it.</param>
    /// <param name="translation">The module's translation.</param>
    /// <param name="memo">The memo table the successor chains share.</param>
    /// <param name="searchMode">The solver's search mode.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns><see langword="true"/> when the joint instance and every spawned chain are satisfiable.</returns>
    private static bool CheckJointRoot(
        ConceptCnf cnf,
        AlcModuleReasoner.Translation translation,
        Dictionary<int[], WorldState> memo,
        SatSearchMode searchMode,
        CancellationToken cancellationToken)
    {
        //The block layout: one block per named individual, asserted
        //individuals first in assertion order, then edge endpoints in edge
        //order — deterministic either way.
        List<Utf8String> individuals = [];
        Dictionary<Utf8String, int> blockOf = [];
        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in translation.AssertedConcepts)
        {
            BlockFor(individuals, blockOf, entry.Key);
        }

        foreach((Utf8String from, Utf8String role, Utf8String to) in translation.Edges)
        {
            BlockFor(individuals, blockOf, from);
            BlockFor(individuals, blockOf, to);
        }

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JointInstance joint = BuildJointInstance(cnf, translation, individuals, blockOf);
            SatVerdict verdict = SatSolver.SolveUnderAssumptions(
                joint.Clauses,
                joint.VariableCount,
                joint.Assumptions,
                pool: null,
                searchMode,
                cancellationToken);
            if(!verdict.IsSatisfiable)
            {
                return false;
            }

            bool[] model = ShrinkJointModel(joint, verdict.Assignment!);
            bool learned = false;
            for(int block = 0; block < individuals.Count && !learned; block++)
            {
                bool[] blockModel = ExtractBlockModel(model, block, joint.BlockWidth);
                foreach(SuccessorPlan plan in PlanSuccessors(cnf, translation, blockModel))
                {
                    if(!CheckWorld(cnf, translation, memo, plan.Assumptions, plan.Key, searchMode, cancellationToken))
                    {
                        //The chain failure teaches the TEMPLATE its modal
                        //conflict clause — TBox-entailed, so sound in every
                        //block — and the joint instance rebuilds against
                        //the grown template, which instantiates the clause
                        //into every block.
                        cnf.Append(plan.ConflictClause);
                        learned = true;

                        break;
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
    /// <param name="cnf">The shared template CNF; contributor encoding grows it before the freeze.</param>
    /// <param name="translation">The module's translation.</param>
    /// <param name="individuals">The individuals in block order.</param>
    /// <param name="blockOf">The individual-to-block index.</param>
    /// <returns>The joint instance.</returns>
    private static JointInstance BuildJointInstance(
        ConceptCnf cnf,
        AlcModuleReasoner.Translation translation,
        List<Utf8String> individuals,
        Dictionary<Utf8String, int> blockOf)
    {
        //Every joint contributor encodes BEFORE the width freezes. Asserted
        //concepts first; then every edge-reachable universal filler, walked
        //by index to a fixpoint because encoding a filler can register
        //further universal atoms that themselves need edge clauses.
        foreach(KeyValuePair<Utf8String, List<AlcConcept>> entry in translation.AssertedConcepts)
        {
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

            foreach((Utf8String _, Utf8String role, Utf8String _) in translation.Edges)
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
        foreach((Utf8String from, Utf8String role, Utf8String to) in translation.Edges)
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
            int block = blockOf[entry.Key];
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
    /// and each true modal-atom replica in descending variable order,
    /// tentatively flip it to false and keep the flip when every joint
    /// clause stays satisfied. Assumption literals are never flipped.
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

        for(int block = joint.BlockCount - 1; block >= 0; block--)
        {
            for(int i = joint.ModalVariables.Count - 1; i >= 0; i--)
            {
                int variable = ConceptCnf.BlockVariable(block, joint.ModalVariables[i], joint.BlockWidth);
                if(!model[variable] || pinned.Contains(variable))
                {
                    continue;
                }

                model[variable] = false;
                if(!Satisfies(joint.Clauses, model))
                {
                    model[variable] = true;
                }
            }
        }

        return model;
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
    /// repeated settled sets and the ancestor walk blocking affirmatively.
    /// </summary>
    /// <param name="cnf">The shared CNF; learned clauses append to it.</param>
    /// <param name="translation">The module's translation, read for the role hierarchy.</param>
    /// <param name="memo">The memo table over canonical assumption sets.</param>
    /// <param name="seedAssumptions">The seed world's assumption literals, canonical.</param>
    /// <param name="seedKey">The seed world's canonical key.</param>
    /// <param name="searchMode">The solver's search mode.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns><see langword="true"/> when the seed world is satisfiable.</returns>
    private static bool CheckWorld(
        ConceptCnf cnf,
        AlcModuleReasoner.Translation translation,
        Dictionary<int[], WorldState> memo,
        SatLiteral[] seedAssumptions,
        int[] seedKey,
        SatSearchMode searchMode,
        CancellationToken cancellationToken)
    {
        //The memo holds settled verdicts only, so a hit answers the seed.
        if(memo.TryGetValue(seedKey, out WorldState settled))
        {
            return settled == WorldState.Satisfiable;
        }

        List<WorldFrame> stack = [new WorldFrame(seedKey, seedAssumptions)];

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
                if(!verdict.IsSatisfiable)
                {
                    memo[frame.Key] = WorldState.Unsatisfiable;
                    stack.RemoveAt(stack.Count - 1);
                    if(stack.Count == 0)
                    {
                        return false;
                    }

                    //The parent learns the modal conflict clause over
                    //exactly the atoms that built the failed successor —
                    //TBox-entailed, so sound in every world — and re-solves
                    //its unchanged assumptions against the grown CNF.
                    WorldFrame parent = stack[^1];
                    cnf.Append(parent.Successors![parent.NextSuccessor].ConflictClause);
                    parent.Successors = null;

                    continue;
                }

                bool[] model = Shrink(cnf, verdict.Assignment!, frame.Assumptions);
                frame.Successors = PlanSuccessors(cnf, translation, model);
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
                    memo[frame.Key] = WorldState.Satisfiable;
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
            if(memo.TryGetValue(successor.Key, out WorldState known))
            {
                if(known == WorldState.Unsatisfiable)
                {
                    //The memo answers without a solver call, but the modal
                    //clause for this parent's atom combination is still
                    //learned: it is what forces the re-solve to move on.
                    cnf.Append(successor.ConflictClause);
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
    /// The greedy model-shrink pass: for each true modal-atom variable in
    /// descending variable order, tentatively flip it to false and keep the
    /// flip when every clause stays satisfied. Assumption literals and the
    /// reserved variable 0 are never flipped.
    /// </summary>
    /// <param name="cnf">The shared CNF, read for the modal-atom registrations and the clauses.</param>
    /// <param name="assignment">The solver's satisfying assignment.</param>
    /// <param name="assumptions">The world's assumption literals, whose variables stay pinned.</param>
    /// <returns>The shrunk model.</returns>
    private static bool[] Shrink(ConceptCnf cnf, IReadOnlyList<bool> assignment, SatLiteral[] assumptions)
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

        for(int i = cnf.ModalAtoms.Count - 1; i >= 0; i--)
        {
            int variable = cnf.ModalAtoms[i].Variable;
            if(variable == 0 || !model[variable] || pinned.Contains(variable))
            {
                continue;
            }

            model[variable] = false;
            if(!Satisfies(cnf.Clauses, model))
            {
                model[variable] = true;
            }
        }

        return model;
    }

    /// <summary>Whether every clause has a literal true under the model.</summary>
    /// <param name="clauses">The clauses.</param>
    /// <param name="model">The model.</param>
    /// <returns><see langword="true"/> when all clauses are satisfied.</returns>
    private static bool Satisfies(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses, bool[] model)
    {
        foreach(IReadOnlyList<SatLiteral> clause in clauses)
        {
            bool satisfied = false;
            foreach(SatLiteral literal in clause)
            {
                if(model[literal.Variable] == literal.IsPositive)
                {
                    satisfied = true;

                    break;
                }
            }

            if(!satisfied)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Plans one successor world per existential atom true in the shrunk
    /// model: the successor carries the existential's filler plus the
    /// filler of every universal atom true in the model whose role the
    /// existential's role reaches through the told role hierarchy, and its
    /// conflict clause negates exactly those atoms.
    /// </summary>
    /// <param name="cnf">The shared CNF; the successor fillers encode into it on demand.</param>
    /// <param name="translation">The module's translation, read for the role hierarchy.</param>
    /// <param name="model">The shrunk model.</param>
    /// <returns>The successor plans, in modal-atom allocation order.</returns>
    private static List<SuccessorPlan> PlanSuccessors(ConceptCnf cnf, AlcModuleReasoner.Translation translation, bool[] model)
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
            foreach((AlcForAll forAll, int forAllVariable) in trueUniversals)
            {
                if(RoleReaches(exists.Role, forAll.Role, translation.SuperRoles))
                {
                    literals.Add(cnf.GetLiteral(forAll.Filler));
                    conflict.Add(new SatLiteral(forAllVariable, IsPositive: false));
                }
            }

            (SatLiteral[] assumptions, int[] key) = Canonicalize(literals);
            plans.Add(new SuccessorPlan(key, assumptions, [.. conflict]));
        }

        return plans;
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

    /// <summary>One planned successor world and the modal conflict clause its failure would teach the shared CNF.</summary>
    /// <param name="Key">The successor's canonical key.</param>
    /// <param name="Assumptions">The successor's assumption literals, canonical.</param>
    /// <param name="ConflictClause">The negations of exactly the modal atoms that built the successor.</param>
    private sealed record SuccessorPlan(int[] Key, SatLiteral[] Assumptions, SatLiteral[] ConflictClause);

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
