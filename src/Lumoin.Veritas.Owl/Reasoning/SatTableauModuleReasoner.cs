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
/// assertions, else one independent world per named individual whose
/// assumptions are that individual's asserted concepts — sound because
/// without asserted role edges no constraint travels between named
/// individuals. A module with asserted role edges throws
/// <see cref="NotSupportedException"/>.
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
    /// does. Only modules without asserted role edges are decided: each
    /// named individual is an independent world, so an asserted edge —
    /// which would let constraints travel between individuals — throws.
    /// </summary>
    /// <param name="module">The module to decide.</param>
    /// <param name="searchMode">How the solver prunes within one world's boolean structure; both modes decide the same satisfiability.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns>The verdict, with an empty subsumption list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">The module asserts role edges between individuals.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ModuleVerdict DecideConsistency(
        ReasoningModule module,
        SatSearchMode searchMode = SatSearchMode.ConflictLearning,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(module);

        AlcModuleReasoner.Translation translation = AlcModuleReasoner.Translate(module);
        if(translation.Edges.Count > 0)
        {
            throw new NotSupportedException("The module asserts role edges between individuals; this engine decides only modules whose individuals carry concept assertions alone.");
        }

        //The shared CNF: every world carries the internalized TBox, so its
        //conjuncts assert as facts once and worlds differ only in their
        //assumption sets.
        ConceptCnf cnf = new();
        foreach(AlcConcept conjunct in translation.TBox)
        {
            cnf.AssertFact(conjunct);
        }

        bool isConsistent = CheckAllSeedWorlds(cnf, translation, searchMode, cancellationToken);

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
    /// <param name="searchMode">The solver's search mode.</param>
    /// <param name="cancellationToken">A token that aborts between solver calls.</param>
    /// <returns><see langword="true"/> when every seed world is satisfiable.</returns>
    private static bool CheckAllSeedWorlds(
        ConceptCnf cnf,
        AlcModuleReasoner.Translation translation,
        SatSearchMode searchMode,
        CancellationToken cancellationToken)
    {
        Dictionary<int[], WorldState> memo = new(CanonicalKeyComparer.Instance);

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
        //worlds, not this model.
        List<(AlcExists Concept, int Variable)> trueExistentials = [];
        List<(AlcForAll Concept, int Variable)> trueUniversals = [];
        foreach(ConceptCnf.ModalAtom atom in cnf.ModalAtoms)
        {
            if(!model[atom.Variable])
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
