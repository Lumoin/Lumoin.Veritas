using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Lumoin.Veritas.Core.Sat;

/// <summary>
/// The verdict of a satisfiability run.
/// </summary>
/// <param name="IsSatisfiable">Whether a satisfying assignment exists.</param>
/// <param name="Assignment">A satisfying assignment indexed by variable, or <c>null</c> when unsatisfiable. Variables no clause constrains come out <see langword="false"/>.</param>
/// <param name="AssumptionCore">For an unsatisfiable verdict, the subset of the supplied assumptions that participates in the refutation; re-solving while assuming only this subset is still unsatisfiable. It is <c>null</c> for a satisfiable verdict and non-<c>null</c> for every unsatisfiable verdict — empty when the formula alone refutes (no assumption participates), the full supplied set when the search mode cannot tighten it, and a proper subset when the conflict analysis attributes the refutation to fewer assumptions.</param>
/// <param name="Statistics">The work the run spent reaching the verdict; <see cref="SatSolveStatistics.Empty"/> for a verdict decided before any search step (such as a direct assumption contradiction).</param>
[DebuggerDisplay("SatVerdict {IsSatisfiable}")]
public readonly record struct SatVerdict(bool IsSatisfiable, IReadOnlyList<bool>? Assignment, IReadOnlyList<SatLiteral>? AssumptionCore = null, SatSolveStatistics Statistics = default);

/// <summary>
/// The satisfiability seam consumers program against: any engine deciding
/// CNF satisfiability slots in. <see cref="SatSolver.Solve"/> is the
/// in-library default.
/// </summary>
/// <remarks>
/// The seam sits at whole-solver granularity, never inside a propagation
/// loop — the join layer measured an in-driver delegate at −11–22% and
/// reverted it; engines swap as units. A bit-parallel sibling (clause
/// masks over packed assignment words, popcount and and-not kernels,
/// vectorized across words) is the SIMD path when a workload outgrows the
/// propagation-only default, behind this same signature.
/// </remarks>
/// <param name="clauses">The clauses, each a disjunction of literals; the formula is their conjunction.</param>
/// <param name="variableCount">The number of variables.</param>
/// <param name="cancellationToken">A token that aborts the search.</param>
/// <returns>The verdict, with a satisfying assignment when one exists.</returns>
public delegate SatVerdict SatSolve(
    IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
    int variableCount,
    CancellationToken cancellationToken);

/// <summary>
/// The solve-under-assumptions seam: deciding CNF satisfiability with a set
/// of literals fixed before any branching. <see cref="SatSolver.SolveUnderAssumptions"/>
/// is the in-library default. The plain <see cref="SatSolve"/> is the
/// special case of an empty assumption set.
/// </summary>
/// <remarks>
/// The assumptions are a reserved trail prefix: each is decided up front and
/// never unwound, so a conflict that traces to that prefix is the
/// unsatisfiable-under-assumptions proof. An unsatisfiable verdict also
/// carries the failed-assumption core — the subset of assumptions reachable
/// from the final conflict through reason antecedents — in
/// <see cref="SatVerdict.AssumptionCore"/>. The seam stays at whole-solver
/// granularity, matching <see cref="SatSolve"/>.
/// </remarks>
/// <param name="clauses">The clauses, each a disjunction of literals; the formula is their conjunction.</param>
/// <param name="variableCount">The number of variables.</param>
/// <param name="assumptions">The literals fixed before branching.</param>
/// <param name="cancellationToken">A token that aborts the search.</param>
/// <returns>The verdict, with a satisfying assignment of the formula and every assumption when one exists, or the failed-assumption core when none does.</returns>
public delegate SatVerdict SatSolveUnderAssumptions(
    IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
    int variableCount,
    IReadOnlyList<SatLiteral> assumptions,
    CancellationToken cancellationToken);

/// <summary>
/// A propositional satisfiability solver over conjunctive normal form:
/// DPLL with unit propagation on an explicit decision trail, with a
/// selectable conflict response — chronological backtracking or
/// first-UIP clause learning with backjumping.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape.</b> The search keeps a trail of assignments and unit
/// propagation runs to fixpoint between decisions. How a conflict prunes
/// is the <see cref="SatSearchMode"/> knob: the default unwinds the trail
/// to the deepest decision whose second branch is untried, while
/// <see cref="SatSearchMode.ConflictLearning"/> records the clause that
/// forced each propagated literal, analyses a conflict to its first-UIP
/// clause, adds that clause, and backjumps to its assertion level. Both
/// modes decide the same satisfiability.
/// </para>
/// <para>
/// <b>Assumptions.</b> Solving under assumptions decides a set of literals
/// before any branching — a reserved trail prefix neither conflict
/// response ever unwinds. A conflict that survives down to that prefix is
/// the unsatisfiable-under-assumptions proof; otherwise the search
/// proceeds exactly as the unassumed case over the remaining variables.
/// </para>
/// <para>
/// <b>Failed-assumption core.</b> An unsatisfiable-under-assumptions
/// verdict carries the subset of assumptions that participates in the
/// refutation, in <see cref="SatVerdict.AssumptionCore"/>. The
/// <see cref="SatSearchMode.ConflictLearning"/> mode records a reason
/// clause for every forced literal, so it walks the final conflict back
/// through those reasons to the level-0 assumptions it depends on — a
/// tight core. The <see cref="SatSearchMode.PropagationOnly"/> default
/// records no reasons mid-search, so it returns the whole supplied set as
/// the honest sound core, tightening only the case it can prove cheaply:
/// an assumption that directly contradicts an already-fixed literal. The
/// full set is always sound, since assuming everything reproduces the
/// refutation.
/// </para>
/// <para>
/// <b>Deliberately the naive first engine.</b> The intended consumers
/// hand over problems a structural device has already bounded —
/// description-logic locality modules, value-space constraint slices,
/// disclosure lattices — where tens to hundreds of variables make
/// this engine sufficient, and correctness is locked by a
/// differential oracle against exhaustive enumeration. No watched
/// literals, no activity heuristics: a richer engine slots in behind the
/// <see cref="SatSolve"/> seam (or the bit-parallel sibling) once a
/// measured workload outgrows this one — the seam, not this
/// implementation, is the commitment.
/// </para>
/// <para>
/// Deterministic by construction: no randomness or timer enters the search,
/// so the same formula always walks the same tree. The default and
/// <see cref="SatSearchMode.ConflictLearning"/> modes branch on the
/// lowest-indexed unassigned variable, <see langword="true"/> first;
/// <see cref="SatSearchMode.WatchedLearning"/> branches by
/// variable-move-to-front with phase saving. Working state rents from the
/// supplied pool; only the returned assignment allocates.
/// </para>
/// </remarks>
public static class SatSolver
{
    /// <summary>
    /// Decides satisfiability of a CNF formula. Method-group convertible
    /// to <see cref="SatSolve"/> through <see cref="Default"/>.
    /// </summary>
    /// <param name="clauses">The clauses, each a disjunction of literals; the formula is their conjunction. An empty clause makes the formula unsatisfiable; an empty clause list is satisfiable.</param>
    /// <param name="variableCount">The number of variables; every literal's <see cref="SatLiteral.Variable"/> must lie below it.</param>
    /// <param name="pool">The pool the solver's working buffers rent from; <c>null</c> uses <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <param name="mode">How a conflict prunes the search; both modes decide the same satisfiability.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative or a literal indexes beyond it.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static SatVerdict Solve(
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
        int variableCount,
        MemoryPool<int>? pool = null,
        SatSearchMode mode = SatSearchMode.PropagationOnly,
        CancellationToken cancellationToken = default)
    {
        return SolveUnderAssumptions(clauses, variableCount, assumptions: [], pool, mode, cancellationToken);
    }

    /// <summary>
    /// Decides satisfiability of a CNF formula with a set of literals fixed
    /// before any branching. Method-group convertible to
    /// <see cref="SatSolveUnderAssumptions"/> through
    /// <see cref="DefaultUnderAssumptions"/>.
    /// </summary>
    /// <remarks>
    /// The assumptions form a reserved trail prefix decided ahead of the
    /// search. A satisfying verdict satisfies both the formula and every
    /// assumption; an unsatisfiable verdict means no assignment satisfies
    /// the formula while honouring the assumptions — including the case
    /// where the assumptions contradict each other or a clause directly —
    /// and carries the failed-assumption core in
    /// <see cref="SatVerdict.AssumptionCore"/>.
    /// </remarks>
    /// <param name="clauses">The clauses, each a disjunction of literals; the formula is their conjunction. An empty clause makes the formula unsatisfiable; an empty clause list is satisfiable when the assumptions are mutually consistent.</param>
    /// <param name="variableCount">The number of variables; every literal's <see cref="SatLiteral.Variable"/> must lie below it.</param>
    /// <param name="assumptions">The literals fixed before branching; each variable must lie below <paramref name="variableCount"/>.</param>
    /// <param name="pool">The pool the solver's working buffers rent from; <c>null</c> uses <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <param name="mode">How a conflict prunes the search; both modes decide the same satisfiability and return a model honouring the formula and assumptions when one exists.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment of the formula and every assumption when one exists, or the failed-assumption core in <see cref="SatVerdict.AssumptionCore"/> when none does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> or <paramref name="assumptions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative or a literal indexes beyond it.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static SatVerdict SolveUnderAssumptions(
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int>? pool = null,
        SatSearchMode mode = SatSearchMode.PropagationOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentNullException.ThrowIfNull(assumptions);
        ArgumentOutOfRangeException.ThrowIfNegative(variableCount);

        for(int clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
        {
            IReadOnlyList<SatLiteral> clause = clauses[clauseIndex];
            for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
            {
                SatLiteral literal = clause[literalIndex];
                if(literal.Variable < 0 || literal.Variable >= variableCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(clauses), literal.Variable, $"A literal indexes variable {literal.Variable} outside [0, {variableCount}).");
                }
            }
        }

        ValidateAssumptions(assumptions, variableCount);

        return SolveOnArenaCore(new ClauseArena(clauses), variableCount, assumptions, pool ?? MemoryPool<int>.Shared, mode, cancellationToken);
    }

    /// <summary>
    /// Decides satisfiability over a caller-held clause arena — the lane for a
    /// consumer that interrogates one growing formula under assumption set after
    /// assumption set and keeps the arena across calls instead of rebuilding it
    /// from clause lists per solve.
    /// </summary>
    /// <remarks>
    /// The arena is BORROWED. The engines append the clauses they learn during the
    /// search, and a stateless engine decides its assumptions at level 0, so every
    /// learned clause is a consequence of the formula TOGETHER WITH THIS CALL'S
    /// assumptions — unsound under any other assumption set. The entry therefore
    /// restores the arena to its entry clause count on EVERY exit, cancellation
    /// included. The caller owns clause validity: every literal in the arena must
    /// index below <paramref name="variableCount"/>, established where the clauses
    /// were ingested.
    /// </remarks>
    /// <param name="arena">The clause database holding exactly the formula; learned clauses appended during the call are removed before it returns.</param>
    /// <param name="variableCount">The number of variables; every arena literal and every assumption indexes below it.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the solver's working buffers rent from; <c>null</c> uses <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <param name="mode">How a conflict prunes the search; both modes decide the same satisfiability.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists, or the failed-assumption core when none does.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arena"/> or <paramref name="assumptions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative or an assumption indexes beyond it.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>; the arena is restored before the throw propagates.</exception>
    internal static SatVerdict SolveUnderAssumptionsOnArena(
        ClauseArena arena,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int>? pool,
        SatSearchMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arena);
        ArgumentNullException.ThrowIfNull(assumptions);
        ArgumentOutOfRangeException.ThrowIfNegative(variableCount);
        ValidateAssumptions(assumptions, variableCount);

        int formulaClauseCount = arena.Count;
        try
        {
            return SolveOnArenaCore(arena, variableCount, assumptions, pool ?? MemoryPool<int>.Shared, mode, cancellationToken);
        }
        finally
        {
            arena.TruncateTo(formulaClauseCount);
        }
    }

    /// <summary>Dispatches one solve over the arena to the engine the mode selects.</summary>
    /// <param name="arena">The clause database; the learning engines append their learned clauses onto it.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the working buffers rent from.</param>
    /// <param name="mode">How a conflict prunes the search.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict.</returns>
    private static SatVerdict SolveOnArenaCore(
        ClauseArena arena,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int> pool,
        SatSearchMode mode,
        CancellationToken cancellationToken)
    {
        return mode switch
        {
            SatSearchMode.WatchedLearning => SolveWatchedLearning(arena, variableCount, assumptions, pool, DefaultRestartUnit, DefaultMinimize, DefaultDynamicRestart, DefaultTrailBlocking, cancellationToken),
            SatSearchMode.ConflictLearning => SolveConflictLearning(arena, variableCount, assumptions, pool, DefaultMinimize, cancellationToken),
            _ => SolvePropagationOnly(arena, variableCount, assumptions, pool, cancellationToken),
        };
    }

    /// <summary>Throws when an assumption indexes a variable outside the solve's range.</summary>
    /// <param name="assumptions">The assumptions to validate.</param>
    /// <param name="variableCount">The number of variables; every assumption must index below it.</param>
    /// <exception cref="ArgumentOutOfRangeException">An assumption indexes a variable outside <c>[0, variableCount)</c>.</exception>
    private static void ValidateAssumptions(IReadOnlyList<SatLiteral> assumptions, int variableCount)
    {
        for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
        {
            SatLiteral assumption = assumptions[assumptionIndex];
            if(assumption.Variable < 0 || assumption.Variable >= variableCount)
            {
                throw new ArgumentOutOfRangeException(nameof(assumptions), assumption.Variable, $"An assumption indexes variable {assumption.Variable} outside [0, {variableCount}).");
            }
        }
    }

    /// <summary>
    /// Chronological-backtracking DPLL: a conflict unwinds to the deepest
    /// decision whose second branch is untried, never below the assumption
    /// prefix.
    /// </summary>
    /// <param name="arena">The clause database holding the formula; this mode never appends to it.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the working buffers rent from.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists.</returns>
    private static SatVerdict SolvePropagationOnly(
        ClauseArena arena,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int> pool,
        CancellationToken cancellationToken)
    {
        //Working state, all variable-bounded: the assignment (-1
        //unassigned, 0 false, 1 true), the trail of assigned variables,
        //and the decision stack as three parallel columns (trail depth,
        //variable, second-branch-tried flag).
        using IMemoryOwner<int> valuesOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> trailOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> decisionDepthsOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> decisionVariablesOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> decisionTriedOwner = pool.Rent(variableCount);

        Span<int> values = valuesOwner.Memory.Span[..variableCount];
        Span<int> trail = trailOwner.Memory.Span[..variableCount];
        Span<int> decisionDepths = decisionDepthsOwner.Memory.Span[..variableCount];
        Span<int> decisionVariables = decisionVariablesOwner.Memory.Span[..variableCount];
        Span<int> decisionTried = decisionTriedOwner.Memory.Span[..variableCount];
        values.Fill(-1);
        int trailCount = 0;
        int decisionCount = 0;

        //The work counters, accumulated across the run and reported on the
        //verdict: branch decisions (including a chronological second-branch
        //flip), forced propagations, conflicts hit, and the deepest decision
        //level reached. This mode never learns a clause.
        int decisions = 0;
        long propagations = 0;
        int conflicts = 0;
        int maxDecisionLevel = 0;

        //The variable an assumption fixes records which assumption fixed it,
        //by index into the supplied list, so a direct contradiction names the
        //exact assumption that set the clashing value. NoAssumption marks a
        //variable no assumption fixed.
        using IMemoryOwner<int> assumedByOwner = pool.Rent(Math.Max(variableCount, 1));
        Span<int> assumedBy = assumedByOwner.Memory.Span[..variableCount];
        assumedBy.Fill(NoAssumption);

        //The assumptions are decided first into a reserved trail prefix the
        //conflict unwind never crosses. An assumption that contradicts an
        //earlier one is itself the unsatisfiable-under-assumptions proof; its
        //core is exactly the clashing pair.
        for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
        {
            SatLiteral assumption = assumptions[assumptionIndex];
            int wanted = assumption.IsPositive ? 1 : 0;
            int current = values[assumption.Variable];
            if(current == -1)
            {
                values[assumption.Variable] = wanted;
                assumedBy[assumption.Variable] = assumptionIndex;
                trail[trailCount++] = assumption.Variable;
            }
            else if(current != wanted)
            {
                return new SatVerdict(false, null, [assumptions[assumedBy[assumption.Variable]], assumption]);
            }
        }

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!Propagate(arena, values, trail, ref trailCount, ref propagations))
            {
                //Conflict: unwind to the deepest decision whose second
                //branch is untried, never below the assumption prefix. None
                //left proves unsatisfiability (under assumptions, when the
                //prefix is non-empty). This mode records no reason for a
                //propagated literal, so the honest sound core is the whole
                //supplied set: assuming all of it reproduces the refutation.
                conflicts++;
                bool branched = false;
                while(decisionCount > 0)
                {
                    int depth = decisionDepths[decisionCount - 1];
                    int variable = decisionVariables[decisionCount - 1];
                    for(int i = trailCount - 1; i >= depth; i--)
                    {
                        values[trail[i]] = -1;
                    }

                    trailCount = depth;

                    if(decisionTried[decisionCount - 1] != 0)
                    {
                        decisionCount--;

                        continue;
                    }

                    decisionTried[decisionCount - 1] = 1;
                    values[variable] = 0;
                    trail[trailCount++] = variable;
                    decisions++;
                    branched = true;

                    break;
                }

                if(!branched)
                {
                    return new SatVerdict(false, null, DeduplicatedAssumptions(assumptions), new SatSolveStatistics(decisions, propagations, conflicts, LearnedClauses: 0, maxDecisionLevel));
                }

                continue;
            }

            //No conflict and propagation is at fixpoint: branch on the
            //lowest unassigned variable, true first. Assumption-assigned
            //variables are already set, so this never reopens the prefix.
            int next = values.IndexOf(-1);
            if(next < 0)
            {
                bool[] assignment = new bool[variableCount];
                for(int i = 0; i < variableCount; i++)
                {
                    assignment[i] = values[i] == 1;
                }

                return new SatVerdict(true, assignment, AssumptionCore: null, new SatSolveStatistics(decisions, propagations, conflicts, LearnedClauses: 0, maxDecisionLevel));
            }

            decisionDepths[decisionCount] = trailCount;
            decisionVariables[decisionCount] = next;
            decisionTried[decisionCount] = 0;
            decisionCount++;
            values[next] = 1;
            trail[trailCount++] = next;
            decisions++;
            if(decisionCount > maxDecisionLevel)
            {
                maxDecisionLevel = decisionCount;
            }
        }
    }

    /// <summary>
    /// Conflict-driven clause learning on the trail: each propagated literal
    /// records the clause that forced it, a conflict is analysed to its
    /// first-UIP clause, that clause is added, and the search backjumps to
    /// the clause's assertion level — never above the assumption prefix.
    /// </summary>
    /// <remarks>
    /// The assumptions and their root propagations occupy decision level 0;
    /// branching opens level 1 and up. A conflict whose analysis stays at
    /// level 0 traces wholly to the assumption prefix and is the
    /// unsatisfiable-under-assumptions proof. The learned clauses are facts
    /// of the formula together with these assumptions, so the analysis
    /// buffers grow with the run while the variable-indexed columns rent from
    /// the pool.
    /// </remarks>
    /// <param name="arena">The clause database holding the formula; the first-UIP clauses append onto it and reason indices point into it.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the working buffers rent from.</param>
    /// <param name="minimize">Whether learned clauses are minimized by self-subsumption before being added.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists.</returns>
    private static SatVerdict SolveConflictLearning(
        ClauseArena arena,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int> pool,
        bool minimize,
        CancellationToken cancellationToken)
    {
        //Working state. The assignment (-1 unassigned, 0 false, 1 true), the
        //trail, the per-variable decision level, the per-variable reason
        //(clause index that forced it, or NoReason for a decision or
        //assumption), and the analysis marker column cleared per conflict.
        using IMemoryOwner<int> valuesOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> trailOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> levelsOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> reasonsOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> seenOwner = pool.Rent(variableCount);
        using IMemoryOwner<int> assumedByOwner = pool.Rent(Math.Max(variableCount, 1));

        Span<int> values = valuesOwner.Memory.Span[..variableCount];
        Span<int> trail = trailOwner.Memory.Span[..variableCount];
        Span<int> levels = levelsOwner.Memory.Span[..variableCount];
        Span<int> reasons = reasonsOwner.Memory.Span[..variableCount];
        Span<int> seen = seenOwner.Memory.Span[..variableCount];
        Span<int> assumedBy = assumedByOwner.Memory.Span[..variableCount];
        values.Fill(-1);
        seen.Clear();
        assumedBy.Fill(NoAssumption);
        int trailCount = 0;
        int currentLevel = 0;

        //The work counters, accumulated across the run and reported on the
        //verdict: branch decisions, forced propagations, conflicts hit,
        //first-UIP clauses learned, and the deepest decision level reached.
        int decisions = 0;
        long propagations = 0;
        int conflicts = 0;
        int learnedClauses = 0;
        int maxDecisionLevel = 0;

        //The reusable scratch for one conflict analysis: the lower-level
        //literals of the learned clause, and — when minimizing — the self-subsumption
        //walk's work stack and the marked-variable list it clears.
        List<SatLiteral> learned = [];
        List<int> minimizeStack = [];
        List<int> minimizeToClear = [];

        //The assumptions are decided first at level 0. An assumption that
        //contradicts an earlier one is the unsatisfiable-under-assumptions
        //proof; its core is exactly the clashing pair.
        for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
        {
            SatLiteral assumption = assumptions[assumptionIndex];
            int wanted = assumption.IsPositive ? 1 : 0;
            int current = values[assumption.Variable];
            if(current == -1)
            {
                values[assumption.Variable] = wanted;
                levels[assumption.Variable] = 0;
                reasons[assumption.Variable] = NoReason;
                assumedBy[assumption.Variable] = assumptionIndex;
                trail[trailCount++] = assumption.Variable;
            }
            else if(current != wanted)
            {
                return new SatVerdict(false, null, [assumptions[assumedBy[assumption.Variable]], assumption]);
            }
        }

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int conflict = PropagateWithReasons(arena, values, levels, reasons, trail, ref trailCount, currentLevel, ref propagations);
            if(conflict >= 0)
            {
                //A conflict at level 0 traces wholly to the formula and the
                //assumption prefix: unsatisfiable (under assumptions when the
                //prefix is non-empty). The reason column then yields the
                //failed-assumption core — the assumptions reachable from the
                //conflict through reason antecedents.
                conflicts++;
                if(currentLevel == 0)
                {
                    List<SatLiteral> core = ExtractCore(arena, conflict, assumptions, assumedBy, reasons, values, seen);

                    return new SatVerdict(false, null, core, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel));
                }

                int backjumpLevel = Analyze(arena, conflict, values, levels, reasons, trail, trailCount, currentLevel, seen, learned, bumped: null, minimize ? minimizeStack : null, minimize ? minimizeToClear : null, out SatLiteral asserting);

                //Backjump: drop every assignment above the assertion level,
                //then add the learned clause and let it force the asserting
                //literal at that level.
                while(trailCount > 0 && levels[trail[trailCount - 1]] > backjumpLevel)
                {
                    values[trail[trailCount - 1]] = -1;
                    trailCount--;
                }

                currentLevel = backjumpLevel;
                int learnedIndex = arena.Add(learned);
                learnedClauses++;
                int assertVariable = asserting.Variable;
                values[assertVariable] = asserting.IsPositive ? 1 : 0;
                levels[assertVariable] = backjumpLevel;
                reasons[assertVariable] = learnedIndex;
                trail[trailCount++] = assertVariable;

                continue;
            }

            //No conflict and propagation is at fixpoint: branch on the
            //lowest unassigned variable, true first. Assumption-assigned
            //variables are already set, so this never reopens the prefix.
            int next = values.IndexOf(-1);
            if(next < 0)
            {
                bool[] assignment = new bool[variableCount];
                for(int i = 0; i < variableCount; i++)
                {
                    assignment[i] = values[i] == 1;
                }

                return new SatVerdict(true, assignment, AssumptionCore: null, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel));
            }

            currentLevel++;
            values[next] = 1;
            levels[next] = currentLevel;
            reasons[next] = NoReason;
            trail[trailCount++] = next;
            decisions++;
            if(currentLevel > maxDecisionLevel)
            {
                maxDecisionLevel = currentLevel;
            }
        }
    }

    /// <summary>
    /// Conflict-driven clause learning with two-watched-literal unit propagation,
    /// variable-move-to-front branching, phase saving, and Luby restarts:
    /// the <see cref="SatSearchMode.WatchedLearning"/> engine. It decides the same
    /// satisfiability as <see cref="SolveConflictLearning"/> and reuses its first-UIP
    /// <see cref="Analyze"/> and <see cref="ExtractCore"/>, but replaces the
    /// per-round full-clause scan with watched-literal propagation — a clause is
    /// inspected only when one of its two watched literals is falsified, and a
    /// backtrack needs no watch-list adjustment — and replaces lowest-index,
    /// true-first branching with a <see cref="VmtfQueue"/>: each decision takes the
    /// highest-stamped unassigned variable, a conflict bumps the variables its
    /// analysis resolved on to the front of that order, and a decision reuses the
    /// polarity the variable last held. When configured with a positive restart unit —
    /// off by default, see <see cref="DefaultRestartUnit"/> — it also restarts once the
    /// search has spent its Luby conflict budget, abandoning the current trail back to
    /// the assumption prefix and resuming from the accumulated learned clauses, variable
    /// order, and saved phases. Propagation, branching, and restarts all reorder the
    /// search without changing the verdict, so the search path differs from the scan engines.
    /// </summary>
    /// <param name="arena">The clause database holding the formula; the first-UIP clauses append onto it and the watch state indexes into it.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the working buffers rent from.</param>
    /// <param name="lubyUnit">The Luby restart base unit — the conflict budget the first restart interval scales; the sequence grows the later intervals without bound. A non-positive unit disables Luby restarts, the retained comparand. Ignored when <paramref name="dynamicRestart"/> is set.</param>
    /// <param name="minimize">Whether learned clauses are minimized by self-subsumption before being added.</param>
    /// <param name="dynamicRestart">Whether to restart on the learned-clause literal-block-distance trend (recent worse than run-long) instead of the Luby schedule; it leaves a steadily-progressing search alone.</param>
    /// <param name="trailBlocking">Whether the dynamic policy blocks a restart while the assignment is growing toward a model; effective only when <paramref name="dynamicRestart"/> is set.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists, or the failed-assumption core when none does.</returns>
    private static SatVerdict SolveWatchedLearning(
        ClauseArena arena,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int> pool,
        int lubyUnit,
        bool minimize,
        bool dynamicRestart,
        bool trailBlocking,
        CancellationToken cancellationToken)
    {
        int width = Math.Max(variableCount, 1);
        using IMemoryOwner<int> valuesOwner = pool.Rent(width);
        using IMemoryOwner<int> trailOwner = pool.Rent(width);
        using IMemoryOwner<int> levelsOwner = pool.Rent(width);
        using IMemoryOwner<int> reasonsOwner = pool.Rent(width);
        using IMemoryOwner<int> seenOwner = pool.Rent(width);
        using IMemoryOwner<int> assumedByOwner = pool.Rent(width);
        using IMemoryOwner<int> nextOwner = pool.Rent(width);
        using IMemoryOwner<int> previousOwner = pool.Rent(width);
        using IMemoryOwner<int> stampOwner = pool.Rent(width);
        using IMemoryOwner<int> savedPhaseOwner = pool.Rent(width);

        Span<int> values = valuesOwner.Memory.Span[..variableCount];
        Span<int> trail = trailOwner.Memory.Span[..variableCount];
        Span<int> levels = levelsOwner.Memory.Span[..variableCount];
        Span<int> reasons = reasonsOwner.Memory.Span[..variableCount];
        Span<int> seen = seenOwner.Memory.Span[..variableCount];
        Span<int> assumedBy = assumedByOwner.Memory.Span[..variableCount];
        Span<int> savedPhase = savedPhaseOwner.Memory.Span[..variableCount];
        values.Fill(-1);
        seen.Clear();
        assumedBy.Fill(NoAssumption);

        //Phase saving: a decision assigns the polarity the variable last held,
        //so the search reuses the progress of an abandoned branch. The first
        //time a variable is decided it has no saved phase yet, so it defaults to
        //true — the polarity the scan engines branch on first.
        savedPhase.Fill(1);

        //The variable-move-to-front decision order, over the next/previous/stamp
        //columns: the highest-stamped unassigned variable is decided next, and a
        //conflict bumps the variables its analysis resolved on to the front.
        VmtfQueue queue = new(nextOwner.Memory.Span[..variableCount], previousOwner.Memory.Span[..variableCount], stampOwner.Memory.Span[..variableCount], variableCount);
        List<int> bumpScratch = [];
        int trailCount = 0;
        int propagatedCount = 0;
        int currentLevel = 0;

        int decisions = 0;
        long propagations = 0;
        int conflicts = 0;
        int learnedClauses = 0;
        int maxDecisionLevel = 0;

        //The restart schedule: conflicts since the last restart, the reluctant-doubling
        //Luby state, and the next restart's conflict budget (the base unit times the
        //current Luby term). A non-positive unit leaves the budget unused and never restarts.
        int restarts = 0;
        int conflictsSinceRestart = 0;
        long lubyU = 1;
        long lubyV = 1;
        long restartLimit = (long)lubyUnit * lubyV;

        //The dynamic-restart state: a fast and a slow exponential moving average of the
        //learned-clause literal-block distance. A restart fires when the fast average
        //rises above the slow by the margin — recent clauses are worse than the run's
        //quality — which a steadily-progressing search never does. Used only when
        //dynamicRestart is set; the stamp scratch dedups levels for the per-conflict LBD.
        double fastLbdEma = 0;
        double slowLbdEma = 0;
        bool lbdEmaReady = false;
        int lbdGeneration = 0;

        //The trail-blocking state: a slow moving average of the conflict-time trail size. When
        //a conflict's trail runs well above this average the assignment is growing toward a
        //model, so the fast LBD average is reset to block the restart the trend would fire.
        double trailEma = 0;
        bool trailEmaReady = false;

        //The per-level stamp scratch is only needed for the dynamic policy's per-conflict
        //LBD; the Luby and off paths rent a minimal placeholder and never touch it.
        using IMemoryOwner<int> lbdStampOwner = pool.Rent(dynamicRestart ? width + 1 : 1);
        if(dynamicRestart)
        {
            lbdStampOwner.Memory.Span[..(width + 1)].Clear();
        }

        //The watch state over the caller's clause database: per clause the two
        //literal codes it watches, and per literal code the watchers — clause plus
        //a blocking literal — keyed on it. A literal code is
        //variable*2 + (isPositive ? 1 : 0); the literal a clause watches triggers
        //the clause's inspection when that literal is falsified. Unit clauses are
        //level-0 facts, never watched.
        List<int> watch0 = [];
        List<int> watch1 = [];
        List<Watcher>?[] watches = new List<Watcher>?[2 * width];
        List<SatLiteral> learned = [];
        List<int> minimizeStack = [];
        List<int> minimizeToClear = [];

        //Assumptions are decided first at level 0; the reserved prefix is never unwound.
        for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
        {
            SatLiteral assumption = assumptions[assumptionIndex];
            int wanted = assumption.IsPositive ? 1 : 0;
            int current = values[assumption.Variable];
            if(current == -1)
            {
                values[assumption.Variable] = wanted;
                levels[assumption.Variable] = 0;
                reasons[assumption.Variable] = NoReason;
                assumedBy[assumption.Variable] = assumptionIndex;
                trail[trailCount++] = assumption.Variable;
            }
            else if(current != wanted)
            {
                return new SatVerdict(false, null, [assumptions[assumedBy[assumption.Variable]], assumption]);
            }
        }

        //Install watches for every clause of width >= 2 (on its first two literals)
        //and force every unit clause's literal at level 0. An empty clause refutes
        //the formula outright; a unit that contradicts a fixed literal is a level-0
        //conflict whose core the reason column yields.
        for(int clauseIndex = 0; clauseIndex < arena.Count; clauseIndex++)
        {
            ReadOnlySpan<int> clause = arena.Literals(clauseIndex);
            watch0.Add(-1);
            watch1.Add(-1);
            if(clause.Length == 0)
            {
                return new SatVerdict(false, null, [], new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
            }

            if(clause.Length == 1)
            {
                int unitCode = clause[0];
                int unitVariable = unitCode >> 1;
                int wanted = unitCode & 1;
                int current = values[unitVariable];
                if(current == -1)
                {
                    values[unitVariable] = wanted;
                    levels[unitVariable] = 0;
                    reasons[unitVariable] = clauseIndex;
                    trail[trailCount++] = unitVariable;
                    propagations++;
                }
                else if(current != wanted)
                {
                    conflicts++;
                    List<SatLiteral> core = ExtractCore(arena, clauseIndex, assumptions, assumedBy, reasons, values, seen);

                    return new SatVerdict(false, null, core, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
                }

                continue;
            }

            //The first two literal codes are the clause's initial watches; each
            //watcher carries the other watch as its blocking literal.
            int code0 = clause[0];
            int code1 = clause[1];
            watch0[clauseIndex] = code0;
            watch1[clauseIndex] = code1;
            (watches[code0] ??= []).Add(new Watcher(clauseIndex, code1));
            (watches[code1] ??= []).Add(new Watcher(clauseIndex, code0));
        }

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int conflict = PropagateWatched(arena, watch0, watch1, watches, values, levels, reasons, trail, ref trailCount, ref propagatedCount, currentLevel, ref propagations);
            if(conflict >= 0)
            {
                conflicts++;
                conflictsSinceRestart++;
                if(currentLevel == 0)
                {
                    List<SatLiteral> core = ExtractCore(arena, conflict, assumptions, assumedBy, reasons, values, seen);

                    return new SatVerdict(false, null, core, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
                }

                int backjumpLevel = Analyze(arena, conflict, values, levels, reasons, trail, trailCount, currentLevel, seen, learned, bumpScratch, minimize ? minimizeStack : null, minimize ? minimizeToClear : null, out SatLiteral asserting);

                //Fold the learned clause's literal-block distance into the two moving
                //averages, read now while levels[] still holds the learned literals'
                //levels (the backjump leaves them, but the asserting variable's is
                //reassigned when it is placed). The first conflict seeds both averages.
                if(dynamicRestart)
                {
                    int lbd = ComputeLbd(learned, levels, lbdStampOwner.Memory.Span[..(width + 1)], ref lbdGeneration);
                    if(lbdEmaReady)
                    {
                        fastLbdEma += (lbd - fastLbdEma) * DynamicRestartFastWeight;
                        slowLbdEma += (lbd - slowLbdEma) * DynamicRestartSlowWeight;
                    }
                    else
                    {
                        fastLbdEma = lbd;
                        slowLbdEma = lbd;
                        lbdEmaReady = true;
                    }

                    //Trail blocking: the trail size here is the assignment at the conflict (the
                    //backjump below has not run yet). When it runs well above its recent average
                    //the search is closing on a model, so reset the fast LBD average to the slow —
                    //the restart trigger (fast above slow by the margin) then cannot hold, blocking
                    //the restart. The sample is compared against the average before folding it in,
                    //so a run of growing trails still updates the baseline.
                    if(trailBlocking)
                    {
                        if(trailEmaReady)
                        {
                            if(conflictsSinceRestart >= DynamicRestartMinInterval && trailCount > trailEma * TrailBlockingMargin)
                            {
                                fastLbdEma = slowLbdEma;
                            }

                            trailEma += (trailCount - trailEma) * TrailBlockingWeight;
                        }
                        else
                        {
                            trailEma = trailCount;
                            trailEmaReady = true;
                        }
                    }
                }

                //Bump every variable the analysis resolved on to the front of the
                //decision order while they are still assigned (they sit on the
                //conflict's trail); the search pointer follows them as the backjump
                //below unassigns them, so the next decision reaches the most
                //recently bumped of them first.
                queue.BumpAll(bumpScratch);

                //Backjump: drop every assignment above the assertion level, saving
                //each unassigned variable's polarity for phase saving and telling
                //the queue it is unassigned. Watched literals need no adjustment —
                //they stay valid for the next descent.
                while(trailCount > 0 && levels[trail[trailCount - 1]] > backjumpLevel)
                {
                    int unassigned = trail[trailCount - 1];
                    savedPhase[unassigned] = values[unassigned];
                    values[unassigned] = -1;
                    queue.NotifyUnassigned(unassigned);
                    trailCount--;
                }

                propagatedCount = trailCount;
                currentLevel = backjumpLevel;

                //Add the learned clause. A unit learned clause is a level-0 fact
                //(never watched); a longer one watches the asserting literal and the
                //highest-level remaining literal, so after the backjump the asserting
                //literal is the clause's only unassigned watch and propagates.
                int learnedIndex = arena.Add(learned);
                watch0.Add(-1);
                watch1.Add(-1);
                learnedClauses++;
                if(learned.Count >= 2)
                {
                    int secondWatch = 1;
                    int secondLevel = levels[learned[1].Variable];
                    for(int index = 2; index < learned.Count; index++)
                    {
                        int level = levels[learned[index].Variable];
                        if(level > secondLevel)
                        {
                            secondLevel = level;
                            secondWatch = index;
                        }
                    }

                    int code0 = LiteralCode(learned[0]);
                    int code1 = LiteralCode(learned[secondWatch]);
                    watch0[learnedIndex] = code0;
                    watch1[learnedIndex] = code1;
                    (watches[code0] ??= []).Add(new Watcher(learnedIndex, code1));
                    (watches[code1] ??= []).Add(new Watcher(learnedIndex, code0));
                }

                int assertVariable = asserting.Variable;
                values[assertVariable] = asserting.IsPositive ? 1 : 0;
                levels[assertVariable] = backjumpLevel;
                reasons[assertVariable] = learnedIndex;
                trail[trailCount++] = assertVariable;

                continue;
            }

            //Propagation is at a conflict-free fixpoint. Before the next decision,
            //decide whether to restart: abandon the current trail back to the
            //assumption prefix at level 0, saving each dropped variable's polarity, and
            //resume from the learned clauses, the variable order, and the saved phases
            //the run has accumulated. The dynamic policy restarts when recent learned
            //clauses are worse than the run's quality (a fast LBD average risen above
            //the slow), throttled by a minimum interval — so a steadily-progressing
            //structured search is left alone; the Luby policy restarts on its fixed
            //conflict budget. Either way it stays complete because every restart keeps
            //the learned clauses, so the search cannot cycle, and it fires only above
            //the prefix (a positive level has something to abandon).
            bool shouldRestart = dynamicRestart
                ? lbdEmaReady && conflictsSinceRestart >= DynamicRestartMinInterval && fastLbdEma > slowLbdEma * DynamicRestartMargin
                : lubyUnit > 0 && conflictsSinceRestart >= restartLimit;
            if(currentLevel > 0 && shouldRestart)
            {
                while(trailCount > 0 && levels[trail[trailCount - 1]] > 0)
                {
                    int unassigned = trail[trailCount - 1];
                    savedPhase[unassigned] = values[unassigned];
                    values[unassigned] = -1;
                    queue.NotifyUnassigned(unassigned);
                    trailCount--;
                }

                propagatedCount = trailCount;
                currentLevel = 0;
                conflictsSinceRestart = 0;
                restarts++;

                //The dynamic policy keeps its moving averages across the restart (they
                //track the run's search health); the Luby policy advances its schedule.
                if(!dynamicRestart)
                {
                    restartLimit = (long)lubyUnit * LubyNext(ref lubyU, ref lubyV);
                }

                continue;
            }

            //Branch on the highest-stamped unassigned variable, assigning the
            //polarity it last held. No variable left to decide means every variable
            //is assigned and the formula is satisfied.
            int decisionVariable = queue.Decide(values);
            if(decisionVariable < 0)
            {
                bool[] assignment = new bool[variableCount];
                for(int i = 0; i < variableCount; i++)
                {
                    assignment[i] = values[i] == 1;
                }

                return new SatVerdict(true, assignment, AssumptionCore: null, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel, restarts));
            }

            currentLevel++;
            values[decisionVariable] = savedPhase[decisionVariable];
            levels[decisionVariable] = currentLevel;
            reasons[decisionVariable] = NoReason;
            trail[trailCount++] = decisionVariable;
            decisions++;
            if(currentLevel > maxDecisionLevel)
            {
                maxDecisionLevel = currentLevel;
            }
        }
    }

    /// <summary>The literal's code: variable doubled, plus one when positive — the dense index into the watch table.</summary>
    /// <param name="literal">The literal.</param>
    /// <returns>The code.</returns>
    internal static int LiteralCode(SatLiteral literal)
    {
        return (literal.Variable * 2) + (literal.IsPositive ? 1 : 0);
    }

    /// <summary>
    /// Advances the reluctant-doubling generator of the Luby restart sequence
    /// 1, 1, 2, 1, 1, 2, 4, 1, 1, 2, … and returns the term the advance lands on.
    /// The sequence's small terms recur forever while its peaks climb without bound,
    /// so scaling it by a base unit gives restart intervals that stay short often yet
    /// grow arbitrarily long — the schedule that both escapes an unlucky early branch
    /// and still lets a genuinely deep search reach its answer.
    /// </summary>
    /// <param name="u">The reluctant-doubling index state, advanced in place.</param>
    /// <param name="v">The reluctant-doubling term state, advanced in place; its post-advance value is the return.</param>
    /// <returns>The next Luby term.</returns>
    internal static long LubyNext(ref long u, ref long v)
    {
        if((u & -u) == v)
        {
            u++;
            v = 1;
        }
        else
        {
            v <<= 1;
        }

        return v;
    }

    /// <summary>
    /// The literal-block distance of a clause: the number of distinct decision levels
    /// among its literals — the standard learned-clause quality measure, low for the
    /// "glue" clauses that tie the search together. A generation stamp dedups the levels
    /// in one pass without clearing the whole column, so consecutive calls stay cheap.
    /// </summary>
    /// <param name="clause">The clause literals, whose variables are assigned under <paramref name="levels"/>.</param>
    /// <param name="levels">The per-variable decision-level column.</param>
    /// <param name="levelStamp">The per-level generation-stamp scratch, one wider than the variable count (a decision level can reach it); a bump avoids clearing it per call.</param>
    /// <param name="generation">The current stamp generation, advanced here and reset with the scratch on overflow.</param>
    /// <returns>The count of distinct decision levels.</returns>
    internal static int ComputeLbd(List<SatLiteral> clause, Span<int> levels, Span<int> levelStamp, ref int generation)
    {
        if(generation == int.MaxValue)
        {
            levelStamp.Clear();
            generation = 0;
        }

        int current = ++generation;
        int lbd = 0;
        foreach(SatLiteral literal in clause)
        {
            int level = levels[literal.Variable];
            if(levelStamp[level] != current)
            {
                levelStamp[level] = current;
                lbd++;
            }
        }

        return lbd;
    }

    /// <summary>
    /// The watched-learning engine reached with an explicit Luby restart base unit,
    /// for the tests that need restarts to fire on the small instances an exhaustive
    /// oracle can check: a unit of one restarts after all but the first conflict, a
    /// non-positive unit disables restarts. Production goes through
    /// <see cref="SolveUnderAssumptions"/>, which uses <see cref="DefaultRestartUnit"/>.
    /// </summary>
    /// <param name="clauses">The formula.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="lubyUnit">The Luby restart base unit; a non-positive value disables restarts.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <param name="minimize">Whether to minimize learned clauses by self-subsumption; defaults to the production setting, an explicit value drives the on/off comparand.</param>
    /// <param name="dynamicRestart">Whether to restart on the literal-block-distance trend rather than the Luby schedule; defaults to the production setting.</param>
    /// <param name="trailBlocking">Whether the dynamic policy blocks a restart while the assignment is growing toward a model; effective only with <paramref name="dynamicRestart"/>, defaults to the production setting.</param>
    /// <returns>The verdict, matching what the production settings would decide — restarts and minimization change the path, not the answer.</returns>
    internal static SatVerdict SolveWatchedLearningForTest(
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        int lubyUnit,
        CancellationToken cancellationToken,
        bool minimize = DefaultMinimize,
        bool dynamicRestart = DefaultDynamicRestart,
        bool trailBlocking = DefaultTrailBlocking)
    {
        return SolveWatchedLearning(new ClauseArena(clauses), variableCount, assumptions, MemoryPool<int>.Shared, lubyUnit, minimize, dynamicRestart, trailBlocking, cancellationToken);
    }

    /// <summary>
    /// Runs two-watched-literal unit propagation to fixpoint from the unprocessed
    /// tail of the trail, recording for each forced literal the clause that forced
    /// it. When a watched literal is falsified the clause is inspected — unless the
    /// watcher's blocking literal is already true, in which case the clause is known
    /// satisfied and skipped without reading it. Otherwise, if its other watch is
    /// true it stays put; a non-false literal is found to move the watch to; or —
    /// none existing — the other watch is forced (unit) or, already false, the
    /// clause is the conflict.
    /// </summary>
    /// <param name="arena">The clause database, formula then learned clauses.</param>
    /// <param name="watch0">Per-clause first watched literal code.</param>
    /// <param name="watch1">Per-clause second watched literal code.</param>
    /// <param name="watches">Per-literal-code watchers — clause plus a blocking literal — keyed on that code.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="levels">The per-variable decision-level column receiving forced levels.</param>
    /// <param name="reasons">The per-variable reason column receiving forced clause indices.</param>
    /// <param name="trail">The trail; entries from <paramref name="propagatedCount"/> up are processed, and forced literals are appended.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="propagatedCount">The index of the next trail entry to propagate; advanced to <paramref name="trailCount"/> on a conflict-free return.</param>
    /// <param name="currentLevel">The level forced literals are recorded at.</param>
    /// <param name="propagations">The running count of forced assignments.</param>
    /// <returns>The index of the conflicting clause, or <c>-1</c> at a conflict-free fixpoint.</returns>
    internal static int PropagateWatched(
        ClauseArena arena,
        List<int> watch0,
        List<int> watch1,
        List<Watcher>?[] watches,
        Span<int> values,
        Span<int> levels,
        Span<int> reasons,
        Span<int> trail,
        ref int trailCount,
        ref int propagatedCount,
        int currentLevel,
        ref long propagations)
    {
        while(propagatedCount < trailCount)
        {
            int variable = trail[propagatedCount];
            propagatedCount++;
            int value = values[variable];

            //The literal falsified by this assignment is the one whose polarity the
            //value contradicts; its watch list holds the clauses to inspect.
            int falsifiedCode = (variable * 2) + (value == 0 ? 1 : 0);
            List<Watcher>? list = watches[falsifiedCode];
            if(list is null)
            {
                continue;
            }

            int index = 0;
            while(index < list.Count)
            {
                Watcher watcher = list[index];

                //The cached blocking literal already satisfies the clause: skip it
                //without reading the clause or its watched codes.
                if(CodeSatisfied(watcher.BlockingCode, values))
                {
                    index++;

                    continue;
                }

                int clauseIndex = watcher.ClauseIndex;
                int code0 = watch0[clauseIndex];
                int code1 = watch1[clauseIndex];
                int otherCode = code0 == falsifiedCode ? code1 : code0;

                //The clause is already satisfied by its other watch: leave it, and
                //refresh the watcher's blocking literal to that watch.
                if(CodeSatisfied(otherCode, values))
                {
                    list[index] = new Watcher(clauseIndex, otherCode);
                    index++;

                    continue;
                }

                //Look for a non-false literal (other than the two watched) to move
                //the falsified watch onto.
                ReadOnlySpan<int> clause = arena.Literals(clauseIndex);
                int replacementCode = -1;
                foreach(int code in clause)
                {
                    if(code == code0 || code == code1)
                    {
                        continue;
                    }

                    if(!CodeFalsified(code, values))
                    {
                        replacementCode = code;

                        break;
                    }
                }

                if(replacementCode >= 0)
                {
                    if(code0 == falsifiedCode)
                    {
                        watch0[clauseIndex] = replacementCode;
                    }
                    else
                    {
                        watch1[clauseIndex] = replacementCode;
                    }

                    //Detach from the falsified watch (swap with the tail to keep the
                    //scan O(1)) and attach to the replacement's watch, blocked by the
                    //watch that stays.
                    list[index] = list[^1];
                    list.RemoveAt(list.Count - 1);
                    (watches[replacementCode] ??= []).Add(new Watcher(clauseIndex, otherCode));

                    continue;
                }

                //No replacement: the clause is unit on its other watch, or — that
                //watch being false — the conflict.
                int otherVariable = otherCode >> 1;
                if(values[otherVariable] == -1)
                {
                    values[otherVariable] = otherCode & 1;
                    levels[otherVariable] = currentLevel;
                    reasons[otherVariable] = clauseIndex;
                    trail[trailCount++] = otherVariable;
                    propagations++;
                    index++;
                }
                else
                {
                    return clauseIndex;
                }
            }
        }

        return -1;
    }

    /// <summary>The reason marker for a variable assigned by decision or assumption rather than forced by a clause.</summary>
    internal const int NoReason = -1;

    /// <summary>The marker for a variable no assumption fixed; a non-negative entry indexes the assumption that fixed it.</summary>
    private const int NoAssumption = -1;

    /// <summary>
    /// The production Luby restart base unit for the watched-learning engine and the
    /// incremental session: zero, restarts off. A positive unit is the conflict budget
    /// of a search's first restart interval, which the Luby sequence then grows without
    /// bound over the later intervals; the engines restart only when a positive unit is
    /// configured. It is off by default because restarts are <em>not</em> a measured
    /// speed win on the bounded, structured modules this solver decides: across the
    /// random-3-SAT-at-transition and pigeonhole families, restarts <em>increase</em>
    /// the conflict count at every unit tried (they abandon a nearly-complete resolution
    /// refutation and re-derive much of it), with no heavy-tailed instance present for a
    /// restart to rescue. The capability is retained tunable — insurance against a
    /// heavy-tailed outlier a future workload may exhibit, and the comparand that teaches
    /// the limit — and to be re-measured against the real reasoner workload when the
    /// incremental session is wired in. A dynamic, literal-block-distance-driven restart
    /// with trail-based blocking is the researched candidate that could turn a net win on
    /// structured unsatisfiable cores; that is a separate, later measured step.
    /// </summary>
    internal const int DefaultRestartUnit = 0;

    /// <summary>
    /// Whether the learning engines minimize a first-UIP clause by self-subsumption
    /// before adding it: drop each lower-level literal whose reason is already covered
    /// by the rest of the clause, yielding a shorter, stronger, logically-equivalent
    /// clause. On by default — it is verdict-preserving, propagates over fewer literals,
    /// and bounds the incremental session's learned-clause growth — with an off path
    /// retained as the measured comparand.
    /// </summary>
    internal const bool DefaultMinimize = true;

    /// <summary>
    /// Whether the production watched-learning engine restarts dynamically on the
    /// literal-block-distance trend rather than on a fixed Luby schedule. Off by default;
    /// the setting is threaded so the on-path can be measured, and the two moving averages
    /// let a restart fire only when recent learned clauses are worse (higher LBD) than the
    /// run's running quality, which — unlike a fixed schedule — leaves a steadily-progressing
    /// structured search alone.
    /// </summary>
    internal const bool DefaultDynamicRestart = false;

    /// <summary>The dynamic-restart fast exponential-moving-average weight over recent learned-clause LBD; a larger weight tracks recent conflicts more tightly.</summary>
    private const double DynamicRestartFastWeight = 1.0 / 32.0;

    /// <summary>The dynamic-restart slow exponential-moving-average weight, the run-long LBD quality the fast average is compared against.</summary>
    private const double DynamicRestartSlowWeight = 1.0 / 4096.0;

    /// <summary>The dynamic-restart trigger margin: a restart fires only when the fast LBD average exceeds the slow average by this factor, so a stable-quality search never restarts.</summary>
    private const double DynamicRestartMargin = 1.25;

    /// <summary>
    /// The minimum conflicts between dynamic restarts, so the fast average has settled and a
    /// restart cannot re-fire immediately. Because the conflict counter increments only on a
    /// conflict — each of which adds a learned clause the watched engine never deletes — and
    /// resets only on a restart, at least this many new learned clauses separate consecutive
    /// dynamic restarts: the progress bound that, with the never-shrinking clause database,
    /// keeps the dynamic policy terminating and complete even when the fast average stays high.
    /// </summary>
    private const int DynamicRestartMinInterval = 50;

    /// <summary>
    /// Whether the dynamic-restart policy blocks a restart while the assignment is growing toward a
    /// model. Off by default; the setting is threaded so the on-path can be measured against the
    /// dynamic policy alone. It modifies only the dynamic policy — a growing trail resets the fast LBD
    /// average, so the restart the trend would otherwise fire is postponed — and has no effect when the
    /// dynamic policy is off.
    /// </summary>
    internal const bool DefaultTrailBlocking = false;

    /// <summary>The trail-size ratio above its recent average at which a growing assignment blocks a dynamic restart, so a search closing on a model is not abandoned.</summary>
    private const double TrailBlockingMargin = 1.4;

    /// <summary>The exponential-moving-average weight over the conflict-time trail size the blocking heuristic tracks; slow, so a single deep conflict does not swamp the recent average.</summary>
    private const double TrailBlockingWeight = 1.0 / 4096.0;

    /// <summary>The default engine behind the <see cref="SatSolve"/> seam: <see cref="Solve"/> with the shared pool.</summary>
    public static SatSolve Default { get; } = static (clauses, variableCount, cancellationToken) => Solve(clauses, variableCount, pool: null, cancellationToken: cancellationToken);

    /// <summary>The default engine behind the <see cref="SatSolveUnderAssumptions"/> seam: <see cref="SolveUnderAssumptions"/> with the shared pool.</summary>
    public static SatSolveUnderAssumptions DefaultUnderAssumptions { get; } = static (clauses, variableCount, assumptions, cancellationToken) => SolveUnderAssumptions(clauses, variableCount, assumptions, pool: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Runs unit propagation to fixpoint over the arena's clause spans: a clause
    /// with every literal false is a conflict; a clause with one unassigned
    /// literal and none true forces that literal.
    /// </summary>
    /// <param name="arena">The clause database.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="trail">The trail receiving forced assignments.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="propagations">The running count of forced assignments, incremented once per literal this call propagates.</param>
    /// <returns><see langword="false"/> on conflict.</returns>
    private static bool Propagate(ClauseArena arena, Span<int> values, Span<int> trail, ref int trailCount, ref long propagations)
    {
        bool changed = true;
        while(changed)
        {
            changed = false;
            for(int clauseIndex = 0; clauseIndex < arena.Count; clauseIndex++)
            {
                ReadOnlySpan<int> clause = arena.Literals(clauseIndex);
                bool satisfied = false;
                int unassignedCount = 0;
                int unitCode = 0;
                foreach(int code in clause)
                {
                    int value = values[code >> 1];
                    if(value == -1)
                    {
                        unassignedCount++;
                        unitCode = code;
                    }
                    else if(value == (code & 1))
                    {
                        satisfied = true;

                        break;
                    }
                }

                if(satisfied)
                {
                    continue;
                }

                if(unassignedCount == 0)
                {
                    return false;
                }

                if(unassignedCount == 1)
                {
                    values[unitCode >> 1] = unitCode & 1;
                    trail[trailCount++] = unitCode >> 1;
                    propagations++;
                    changed = true;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Runs unit propagation to fixpoint, recording for each forced literal
    /// the clause that forced it and the level it was forced at: the inputs
    /// the first-UIP analysis needs.
    /// </summary>
    /// <param name="arena">The clause database, formula then learned clauses.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="levels">The per-variable decision-level column receiving forced levels.</param>
    /// <param name="reasons">The per-variable reason column receiving forced clause indices.</param>
    /// <param name="trail">The trail receiving forced assignments.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="currentLevel">The level forced literals are recorded at.</param>
    /// <param name="propagations">The running count of forced assignments, incremented once per literal this call propagates.</param>
    /// <returns>The index of the conflicting clause, or <c>-1</c> when propagation reaches a conflict-free fixpoint.</returns>
    private static int PropagateWithReasons(ClauseArena arena, Span<int> values, Span<int> levels, Span<int> reasons, Span<int> trail, ref int trailCount, int currentLevel, ref long propagations)
    {
        bool changed = true;
        while(changed)
        {
            changed = false;
            for(int clauseIndex = 0; clauseIndex < arena.Count; clauseIndex++)
            {
                ReadOnlySpan<int> clause = arena.Literals(clauseIndex);
                bool satisfied = false;
                int unassignedCount = 0;
                int unitCode = 0;
                foreach(int code in clause)
                {
                    int value = values[code >> 1];
                    if(value == -1)
                    {
                        unassignedCount++;
                        unitCode = code;
                    }
                    else if(value == (code & 1))
                    {
                        satisfied = true;

                        break;
                    }
                }

                if(satisfied)
                {
                    continue;
                }

                if(unassignedCount == 0)
                {
                    return clauseIndex;
                }

                if(unassignedCount == 1)
                {
                    int variable = unitCode >> 1;
                    values[variable] = unitCode & 1;
                    levels[variable] = currentLevel;
                    reasons[variable] = clauseIndex;
                    trail[trailCount++] = variable;
                    propagations++;
                    changed = true;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Derives the first-UIP learned clause from a conflict: resolves the
    /// conflicting clause against the reasons of current-level literals along
    /// the trail until one current-level literal remains — the unique
    /// implication point — and collects the lower-level literals as the rest
    /// of the clause.
    /// </summary>
    /// <param name="arena">The clause database holding the conflict and reason clauses.</param>
    /// <param name="conflict">The index of the conflicting clause.</param>
    /// <param name="values">The assignment column; every learned literal is false under it.</param>
    /// <param name="levels">The per-variable decision-level column.</param>
    /// <param name="reasons">The per-variable reason column.</param>
    /// <param name="trail">The trail, walked from its top to find the implication point.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="currentLevel">The conflict's decision level.</param>
    /// <param name="seen">The analysis marker column; entered cleared and left cleared.</param>
    /// <param name="learned">The reusable scratch the lower-level literals collect into; the asserting literal is prepended on return.</param>
    /// <param name="bumped">A reusable scratch receiving, when non-<c>null</c>, every variable the analysis resolved on — the conflict-side variables the variable-move-to-front order bumps; cleared on entry. <c>null</c> for callers that do not bump.</param>
    /// <param name="minimizeStack">A reusable scratch for the iterative self-subsumption walk; when it and <paramref name="minimizeToClear"/> are both non-<c>null</c> the learned clause is minimized. <c>null</c> disables minimization (the retained comparand).</param>
    /// <param name="minimizeToClear">A reusable scratch the minimization records every marked variable in, so the analysis leaves the marker column cleared; paired with <paramref name="minimizeStack"/>.</param>
    /// <param name="asserting">The asserting literal: the unique-implication-point variable's false literal, which the learned clause forces true after backjump.</param>
    /// <returns>The assertion level: the highest level among the lower-level literals, or <c>0</c> when the learned clause is unit.</returns>
    internal static int Analyze(
        ClauseArena arena,
        int conflict,
        Span<int> values,
        Span<int> levels,
        Span<int> reasons,
        Span<int> trail,
        int trailCount,
        int currentLevel,
        Span<int> seen,
        List<SatLiteral> learned,
        List<int>? bumped,
        List<int>? minimizeStack,
        List<int>? minimizeToClear,
        out SatLiteral asserting)
    {
        learned.Clear();
        bumped?.Clear();
        int pathCount = 0;
        int trailIndex = trailCount - 1;
        int reasonClause = conflict;
        int implicationVariable = -1;

        do
        {
            //Fold in every fresh literal of the current reason clause except
            //the variable being resolved on, whose own reason this clause is:
            //a current-level literal extends the path toward the implication
            //point; a lower-level literal joins the learned clause; a level-0
            //literal is a root fact and omitted.
            foreach(int code in arena.Literals(reasonClause))
            {
                int variable = code >> 1;
                if(variable == implicationVariable || seen[variable] != 0 || levels[variable] == 0)
                {
                    continue;
                }

                seen[variable] = 1;
                bumped?.Add(variable);
                if(levels[variable] == currentLevel)
                {
                    pathCount++;
                }
                else
                {
                    learned.Add(FalseLiteral(variable, values[variable]));
                }
            }

            //Step back to the most recent current-level literal still on the
            //path and resolve on its reason. Lower-level literals are marked
            //too, so the walk skips any that is not at the current level.
            while(seen[trail[trailIndex]] == 0 || levels[trail[trailIndex]] != currentLevel)
            {
                trailIndex--;
            }

            implicationVariable = trail[trailIndex];
            seen[implicationVariable] = 0;
            reasonClause = reasons[implicationVariable];
            pathCount--;
            trailIndex--;
        }
        while(pathCount > 0);

        //Minimize the lower-level literals by self-subsumption when the scratch is
        //supplied, dropping each whose reason is already covered by the rest of the
        //clause; then clear every marker (the surviving literals', the dropped
        //literals', and the antecedents the minimization walked) so the column is left
        //as it was entered. Without the scratch, the lower-level literals are the only
        //markers to clear.
        if(minimizeStack is not null && minimizeToClear is not null)
        {
            MinimizeLearned(arena, learned, levels, reasons, seen, minimizeStack, minimizeToClear);
            foreach(int variable in minimizeToClear)
            {
                seen[variable] = 0;
            }
        }
        else
        {
            foreach(SatLiteral literal in learned)
            {
                seen[literal.Variable] = 0;
            }
        }

        //Prepend the implication point's false literal as the asserting literal, and
        //take the assertion level from the (possibly minimized) lower-level literals.
        asserting = FalseLiteral(implicationVariable, values[implicationVariable]);
        int backjumpLevel = 0;
        foreach(SatLiteral literal in learned)
        {
            int level = levels[literal.Variable];
            if(level > backjumpLevel)
            {
                backjumpLevel = level;
            }
        }

        learned.Insert(0, asserting);

        return backjumpLevel;
    }

    /// <summary>
    /// Minimizes a first-UIP learned clause by self-subsumption: drops each lower-level
    /// literal whose reason clause resolves entirely into literals already in the clause
    /// or into root (level-0) facts, so the shorter clause is still implied by the
    /// formula and still asserts after backjump. The literals still carry their analysis
    /// markers on entry; the survivors and every antecedent the walk marks are recorded
    /// in <paramref name="toClear"/> for the caller to clear.
    /// </summary>
    /// <remarks>
    /// A dropped literal keeps its marker through the pass so a later literal's walk still
    /// treats it as covered — sound because a redundant literal is entailed by the clause
    /// that remains. The implication point is not among these literals (it is prepended
    /// after), so minimization never touches the asserting literal.
    /// </remarks>
    /// <param name="arena">The clause database holding the reason clauses.</param>
    /// <param name="learned">The lower-level literals; redundant ones are removed in place.</param>
    /// <param name="levels">The per-variable decision-level column.</param>
    /// <param name="reasons">The per-variable reason column.</param>
    /// <param name="seen">The marker column; every clause variable is marked on entry, and this adds the antecedent marks it needs.</param>
    /// <param name="stack">The reusable work stack for the iterative walk.</param>
    /// <param name="toClear">The reusable list every marked variable is recorded in — the clause's own variables, seeded here, plus the antecedents the walk marks — so the caller can leave the marker column cleared.</param>
    private static void MinimizeLearned(ClauseArena arena, List<SatLiteral> learned, Span<int> levels, Span<int> reasons, Span<int> seen, List<int> stack, List<int> toClear)
    {
        toClear.Clear();
        foreach(SatLiteral literal in learned)
        {
            toClear.Add(literal.Variable);
        }

        int write = 0;
        for(int read = 0; read < learned.Count; read++)
        {
            SatLiteral literal = learned[read];

            //A decision literal has no reason to resolve away, so it always stays; any
            //other literal stays only if its reason is not covered by the clause.
            if(reasons[literal.Variable] == NoReason || !IsRedundant(arena, literal.Variable, levels, reasons, seen, stack, toClear))
            {
                learned[write] = literal;
                write++;
            }
        }

        if(write < learned.Count)
        {
            learned.RemoveRange(write, learned.Count - write);
        }
    }

    /// <summary>
    /// Whether a forced literal is redundant in the learned clause: an iterative
    /// depth-first walk back through reason clauses that succeeds when every antecedent
    /// it reaches is already marked (in the clause or shown redundant) or a level-0 root
    /// fact, and fails the moment it reaches an unmarked decision antecedent. The walk is
    /// iterative over an explicit stack — never recursive — so a long reason chain cannot
    /// overflow. On success the antecedents it marked stay marked (memoized for later
    /// literals, recorded in <paramref name="toClear"/>); on failure the marks this call
    /// added are rolled back so a later literal's walk is not misled.
    /// </summary>
    /// <param name="arena">The clause database holding the reason clauses.</param>
    /// <param name="startVariable">The forced variable whose redundancy is tested.</param>
    /// <param name="levels">The per-variable decision-level column.</param>
    /// <param name="reasons">The per-variable reason column.</param>
    /// <param name="seen">The marker column, extended and rolled back by the walk.</param>
    /// <param name="stack">The reusable work stack, cleared on entry.</param>
    /// <param name="toClear">The list marked antecedents are appended to; on failure this call's additions are removed.</param>
    /// <returns><see langword="true"/> when the literal is redundant and can be dropped.</returns>
    private static bool IsRedundant(ClauseArena arena, int startVariable, Span<int> levels, Span<int> reasons, Span<int> seen, List<int> stack, List<int> toClear)
    {
        int clearMark = toClear.Count;
        stack.Clear();
        stack.Add(startVariable);
        while(stack.Count > 0)
        {
            int variable = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            foreach(int code in arena.Literals(reasons[variable]))
            {
                int antecedent = code >> 1;

                //The forced literal itself, an already-marked literal, and a level-0 root
                //fact are all covered; move on.
                if(antecedent == variable || seen[antecedent] != 0 || levels[antecedent] == 0)
                {
                    continue;
                }

                //An unmarked decision antecedent is outside the clause and cannot be
                //resolved away: the literal is not redundant. Roll back the marks this
                //call added so a later literal's walk starts clean.
                if(reasons[antecedent] == NoReason)
                {
                    for(int index = clearMark; index < toClear.Count; index++)
                    {
                        seen[toClear[index]] = 0;
                    }

                    toClear.RemoveRange(clearMark, toClear.Count - clearMark);

                    return false;
                }

                //A forced, not-yet-marked antecedent: mark it covered and walk into its reason.
                seen[antecedent] = 1;
                toClear.Add(antecedent);
                stack.Add(antecedent);
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the failed-assumption core from a level-0 conflict — the
    /// analyze-final walk: starting from the conflicting clause, follows reason
    /// antecedents backward through the level-0 implication graph and collects
    /// every assumption it reaches.
    /// </summary>
    /// <remarks>
    /// Every variable a clause in the walk mentions is assigned at level 0, so
    /// it was fixed either by an assumption (named through <paramref name="assumedBy"/>)
    /// or forced by a reason clause (named through <paramref name="reasons"/>);
    /// the walk adds the former to the core and recurses into the latter.
    /// Assumptions outside the reachable set never forced any literal on a path
    /// to the conflict, so they are absent from the implication graph and
    /// dropping them leaves the same propagation reaching the same conflict —
    /// the returned core is sound. The first occurrence of each variable's
    /// assumption fixes its place; later duplicates of the same literal do not
    /// re-enter.
    /// </remarks>
    /// <param name="arena">The clause database holding the conflict and reason clauses.</param>
    /// <param name="conflict">The index of the conflicting clause.</param>
    /// <param name="assumptions">The supplied assumptions; <paramref name="assumedBy"/> indexes into this list.</param>
    /// <param name="assumedBy">The per-variable column naming the assumption that fixed each variable, or <see cref="NoAssumption"/>.</param>
    /// <param name="reasons">The per-variable reason column naming the clause that forced each variable, or <see cref="NoReason"/>.</param>
    /// <param name="values">The assignment column; every variable a walked clause mentions is assigned under it.</param>
    /// <param name="seen">The marker column used as the walk's visited set; entered and left cleared.</param>
    /// <returns>The assumptions reachable from the conflict, each once, in the order the walk first reaches them.</returns>
    private static List<SatLiteral> ExtractCore(
        ClauseArena arena,
        int conflict,
        IReadOnlyList<SatLiteral> assumptions,
        Span<int> assumedBy,
        Span<int> reasons,
        Span<int> values,
        Span<int> seen)
    {
        List<SatLiteral> core = [];
        List<int> pending = [conflict];
        List<int> touched = [];

        while(pending.Count > 0)
        {
            int clauseIndex = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            foreach(int code in arena.Literals(clauseIndex))
            {
                int variable = code >> 1;

                //An unassigned variable cannot sit on the level-0 implication
                //graph: every literal of the conflict and of a reason clause is
                //assigned. The guard keeps the walk total regardless.
                if(values[variable] == -1 || seen[variable] != 0)
                {
                    continue;
                }

                seen[variable] = 1;
                touched.Add(variable);
                if(assumedBy[variable] != NoAssumption)
                {
                    core.Add(assumptions[assumedBy[variable]]);
                }
                else if(reasons[variable] != NoReason)
                {
                    pending.Add(reasons[variable]);
                }
            }
        }

        //Leave the marker column as it was entered: cleared.
        foreach(int variable in touched)
        {
            seen[variable] = 0;
        }

        return core;
    }

    /// <summary>
    /// The supplied assumptions with each literal kept once, in first-occurrence
    /// order: the whole-set sound core for a refutation the search mode cannot
    /// trace to fewer assumptions.
    /// </summary>
    /// <param name="assumptions">The supplied assumptions.</param>
    /// <returns>The distinct assumption literals, in the order they first appear.</returns>
    private static List<SatLiteral> DeduplicatedAssumptions(IReadOnlyList<SatLiteral> assumptions)
    {
        List<SatLiteral> distinct = [];
        HashSet<SatLiteral> already = [];
        for(int assumptionIndex = 0; assumptionIndex < assumptions.Count; assumptionIndex++)
        {
            SatLiteral assumption = assumptions[assumptionIndex];
            if(already.Add(assumption))
            {
                distinct.Add(assumption);
            }
        }

        return distinct;
    }

    /// <summary>
    /// The literal of a variable that is false under its current assignment:
    /// the form in which an assigned variable enters a learned clause.
    /// </summary>
    /// <param name="variable">The variable.</param>
    /// <param name="value">The variable's assignment (0 false, 1 true).</param>
    /// <returns>The literal that the assignment falsifies.</returns>
    private static SatLiteral FalseLiteral(int variable, int value)
    {
        return new SatLiteral(variable, IsPositive: value == 0);
    }

    /// <summary>Whether the literal a code denotes is satisfied under the assignment: its variable is assigned the polarity the code carries.</summary>
    /// <param name="code">The literal code (variable doubled, plus one when positive).</param>
    /// <param name="values">The assignment column (-1 unassigned, 0 false, 1 true).</param>
    /// <returns><see langword="true"/> when the literal is assigned true.</returns>
    internal static bool CodeSatisfied(int code, ReadOnlySpan<int> values)
    {
        return values[code >> 1] == (code & 1);
    }

    /// <summary>Whether the literal a code denotes is falsified under the assignment: its variable is assigned the opposite polarity.</summary>
    /// <param name="code">The literal code (variable doubled, plus one when positive).</param>
    /// <param name="values">The assignment column (-1 unassigned, 0 false, 1 true).</param>
    /// <returns><see langword="true"/> when the literal is assigned false; <see langword="false"/> when it is true or unassigned.</returns>
    internal static bool CodeFalsified(int code, ReadOnlySpan<int> values)
    {
        int value = values[code >> 1];

        return value != -1 && value != (code & 1);
    }

    /// <summary>
    /// The clause database as a flat arena of literal codes: every clause's
    /// literals live back to back in one growable <see cref="int"/> buffer, each
    /// clause a <c>(start, length)</c> slice named by its index, and learned
    /// clauses are appended. A literal code is <c>variable * 2 + polarity</c> — the
    /// same code the watch table keys on. Indexing a clause hands back a span over
    /// that buffer, so propagation and conflict analysis walk contiguous memory
    /// rather than chasing a list of independently-allocated per-clause arrays.
    /// </summary>
    /// <remarks>
    /// This is the same flat, index-addressed layout the data plane uses for the
    /// encoded-triple store — terms and triples are packed into compact indexed
    /// structures rather than pointer-linked nodes — applied here to clause
    /// literals. The two share only that discipline: the codes in this arena are a
    /// dense per-module satisfiability-variable numbering, local to one solve and
    /// unrelated to the triple store's term identifiers, so the two layouts evolve
    /// independently. The buffer lives on the managed heap because learned clauses
    /// are appended during the search; the variable-indexed working columns rent
    /// from the pool instead. A single contiguous literal stream is also the layout
    /// a future vectorized or bit-parallel clause evaluation would require.
    /// </remarks>
    internal sealed class ClauseArena
    {
        /// <summary>The flat literal-code store; clause slices sit back to back in one growable buffer.</summary>
        private List<int> Codes { get; } = [];

        /// <summary>Per-clause start offset into <see cref="Codes"/>; a clause runs to where the next begins.</summary>
        private List<int> Starts { get; } = [];

        /// <summary>Builds an empty arena; a caller that holds one across solves ingests its formula through <see cref="Add"/>.</summary>
        public ClauseArena()
        {
        }

        /// <summary>Builds the arena from the formula's clauses; learned clauses append onto it during the search.</summary>
        /// <param name="clauses">The formula.</param>
        public ClauseArena(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses)
        {
            for(int clauseIndex = 0; clauseIndex < clauses.Count; clauseIndex++)
            {
                Add(clauses[clauseIndex]);
            }
        }

        /// <summary>The number of clauses.</summary>
        public int Count => Starts.Count;

        /// <summary>The literal codes of one clause, as a contiguous span into the arena.</summary>
        /// <param name="clauseIndex">The clause index.</param>
        /// <returns>The clause's literal codes; empty for an empty clause.</returns>
        public ReadOnlySpan<int> Literals(int clauseIndex)
        {
            int start = Starts[clauseIndex];
            int end = clauseIndex + 1 < Starts.Count ? Starts[clauseIndex + 1] : Codes.Count;

            return CollectionsMarshal.AsSpan(Codes)[start..end];
        }

        /// <summary>Appends a clause's literal codes and returns its index.</summary>
        /// <param name="clause">The clause literals to append.</param>
        /// <returns>The appended clause's index.</returns>
        public int Add(IReadOnlyList<SatLiteral> clause)
        {
            int index = Starts.Count;
            Starts.Add(Codes.Count);
            for(int literalIndex = 0; literalIndex < clause.Count; literalIndex++)
            {
                Codes.Add(LiteralCode(clause[literalIndex]));
            }

            return index;
        }

        /// <summary>
        /// Removes every clause appended past the boundary, restoring the arena to
        /// exactly its first <paramref name="clauseCount"/> clauses; the buffers'
        /// capacity is retained, so a later append regrows nothing. A count at or
        /// past the current one is a no-op. The boundary is a previously observed
        /// <see cref="Count"/> — the borrowed-arena entry records it before the
        /// engines append their learned clauses.
        /// </summary>
        /// <param name="clauseCount">The clause count to restore.</param>
        public void TruncateTo(int clauseCount)
        {
            if(clauseCount >= Starts.Count)
            {
                return;
            }

            int codesStart = Starts[clauseCount];
            Codes.RemoveRange(codesStart, Codes.Count - codesStart);
            Starts.RemoveRange(clauseCount, Starts.Count - clauseCount);
        }
    }

    /// <summary>
    /// One entry in a literal's watch list: the clause watching the literal,
    /// paired with a blocking literal — another literal of the clause whose truth
    /// alone satisfies it. A propagation visiting the entry tests the blocking
    /// literal first and skips the clause when it is already true, sparing the
    /// clause read. The hint may go stale as watches move; a false or unassigned
    /// blocking literal just falls through to the full inspection, so it never
    /// affects correctness.
    /// </summary>
    /// <param name="ClauseIndex">The watching clause's index in the arena.</param>
    /// <param name="BlockingCode">A literal code of the clause whose truth satisfies it.</param>
    internal readonly record struct Watcher(int ClauseIndex, int BlockingCode);

    /// <summary>
    /// The variable-move-to-front decision order: a doubly-linked list of
    /// variables held in strictly decreasing stamp from head to tail, the head
    /// the most recently bumped. A decision takes the highest-stamped unassigned
    /// variable; a conflict bumps the variables its analysis resolved on to the
    /// head, so the search concentrates on variables recent conflicts touched.
    /// </summary>
    /// <remarks>
    /// A cached search pointer is the variable the next decision walk starts
    /// from. The list stays sorted by stamp because every bump moves a variable to
    /// the head and gives it the new maximum stamp, so a walk toward lower stamps
    /// visits variables in decreasing stamp. The pointer holds the invariant that
    /// no unassigned variable outranks it: a decision sets it to the variable it
    /// returns (everything above is then assigned), and unassigning a variable that
    /// now outranks it advances it to that variable. Bumping raises the stamps of
    /// variables that are assigned at the time (they sit on the conflict's trail),
    /// so the pointer follows them only once a backjump unassigns them. The columns
    /// are variable-indexed and rent from the caller's pool; the struct stays on
    /// the stack and never escapes the solve.
    /// </remarks>
    internal ref struct VmtfQueue
    {
        /// <summary>The next variable toward a lower stamp, or <c>-1</c> at the tail.</summary>
        private readonly Span<int> next;

        /// <summary>The previous variable toward a higher stamp, or <c>-1</c> at the head.</summary>
        private readonly Span<int> previous;

        /// <summary>The per-variable bump stamp; a larger stamp decides earlier.</summary>
        private readonly Span<int> stamp;

        /// <summary>The highest-stamped variable, decided first; <c>-1</c> when there are no variables.</summary>
        private int head;

        /// <summary>
        /// The last stamp handed out; the next bump raises a variable one above it.
        /// A single bounded solve cannot approach the <see cref="int"/> range, but a
        /// queue reused across many solves can, so <see cref="Bump"/> renumbers the
        /// order before the counter would wrap.
        /// </summary>
        private int lastStamp;

        /// <summary>The variable a decision walk starts from; no unassigned variable outranks it.</summary>
        private int searchPointer;

        /// <summary>The highest-stamped variable, decided first; <c>-1</c> when there are no variables. Carried out so a reused queue can resume from it.</summary>
        public readonly int Head => head;

        /// <summary>The last stamp handed out; carried out so a reused queue can resume the bump counter.</summary>
        public readonly int LastStamp => lastStamp;

        /// <summary>
        /// Builds the order over variables <c>0..count-1</c> in index order, the
        /// lowest index stamped highest so the first decisions follow ascending
        /// index — the order the scan engines branch in — until conflicts reorder it.
        /// </summary>
        /// <param name="nextLinks">The next-link column, length at least <paramref name="count"/>.</param>
        /// <param name="previousLinks">The previous-link column, length at least <paramref name="count"/>.</param>
        /// <param name="stamps">The stamp column, length at least <paramref name="count"/>.</param>
        /// <param name="count">The number of variables.</param>
        public VmtfQueue(Span<int> nextLinks, Span<int> previousLinks, Span<int> stamps, int count)
        {
            next = nextLinks;
            previous = previousLinks;
            stamp = stamps;
            for(int variable = 0; variable < count; variable++)
            {
                next[variable] = variable + 1 < count ? variable + 1 : -1;
                previous[variable] = variable - 1;
                stamp[variable] = count - 1 - variable;
            }

            head = count > 0 ? 0 : -1;
            lastStamp = count - 1;
            searchPointer = head;
        }

        /// <summary>
        /// Adopts an order an earlier solve already built: the columns are left as
        /// they are (the accumulated bumps), and the head and bump counter are
        /// carried in. The search pointer restarts at the head, which is correct
        /// because a reused descent begins with only level-0 facts assigned, so no
        /// unassigned variable outranks the head.
        /// </summary>
        /// <param name="nextLinks">The next-link column from the earlier solve.</param>
        /// <param name="previousLinks">The previous-link column from the earlier solve.</param>
        /// <param name="stamps">The stamp column from the earlier solve.</param>
        /// <param name="resumedHead">The head the earlier solve left.</param>
        /// <param name="resumedLastStamp">The bump counter the earlier solve left.</param>
        public VmtfQueue(Span<int> nextLinks, Span<int> previousLinks, Span<int> stamps, int resumedHead, int resumedLastStamp)
        {
            next = nextLinks;
            previous = previousLinks;
            stamp = stamps;
            head = resumedHead;
            lastStamp = resumedLastStamp;
            searchPointer = resumedHead;
        }

        /// <summary>
        /// The highest-stamped unassigned variable, or <c>-1</c> when every
        /// variable is assigned. The walk starts at the search pointer and follows
        /// the list toward lower stamps; the search-pointer invariant keeps the
        /// skipped head correct.
        /// </summary>
        /// <param name="values">The assignment column; <c>-1</c> marks unassigned.</param>
        /// <returns>The decision variable, or <c>-1</c>.</returns>
        public int Decide(ReadOnlySpan<int> values)
        {
            int variable = searchPointer;
            while(variable != -1 && values[variable] != -1)
            {
                variable = next[variable];
            }

            if(variable != -1)
            {
                searchPointer = variable;
            }

            return variable;
        }

        /// <summary>
        /// Records that a variable has been unassigned, advancing the search
        /// pointer to it when it now outranks the pointer so the next decision
        /// still finds the highest-stamped unassigned variable.
        /// </summary>
        /// <param name="variable">The unassigned variable.</param>
        public void NotifyUnassigned(int variable)
        {
            if(searchPointer == -1 || stamp[variable] > stamp[searchPointer])
            {
                searchPointer = variable;
            }
        }

        /// <summary>
        /// Bumps a set of variables to the head, ordered so the one already
        /// stamped highest ends up highest again and all of them move above the
        /// variables not in the set. The set is small (a conflict's resolution
        /// side), so an insertion sort by current stamp orders it before the
        /// moves; this keeps the set's internal recency rather than scrambling it.
        /// </summary>
        /// <param name="variables">The variables to bump; reordered in place by ascending stamp.</param>
        public void BumpAll(List<int> variables)
        {
            for(int index = 1; index < variables.Count; index++)
            {
                int variable = variables[index];
                int key = stamp[variable];
                int sorted = index - 1;
                while(sorted >= 0 && stamp[variables[sorted]] > key)
                {
                    variables[sorted + 1] = variables[sorted];
                    sorted--;
                }

                variables[sorted + 1] = variable;
            }

            foreach(int variable in variables)
            {
                Bump(variable);
            }
        }

        /// <summary>
        /// Moves one variable to the head and stamps it above every other. The
        /// list stays sorted by stamp because the moved variable takes the new
        /// maximum stamp.
        /// </summary>
        /// <param name="variable">The variable to bump.</param>
        private void Bump(int variable)
        {
            if(variable != head)
            {
                int beforeVariable = previous[variable];
                int afterVariable = next[variable];
                next[beforeVariable] = afterVariable;
                if(afterVariable != -1)
                {
                    previous[afterVariable] = beforeVariable;
                }

                previous[head] = variable;
                previous[variable] = -1;
                next[variable] = head;
                head = variable;
            }

            if(lastStamp == int.MaxValue)
            {
                Renumber();
            }

            stamp[variable] = ++lastStamp;
        }

        /// <summary>
        /// Renumbers the stamps into the current order — highest at the head,
        /// decreasing along the list to the tail — and resets the bump counter. The
        /// order is preserved; only the magnitudes shrink. This is the overflow
        /// backstop for a queue reused across many solves, where the bump counter
        /// would otherwise grow without bound; a single bounded solve never reaches it.
        /// </summary>
        private void Renumber()
        {
            int value = stamp.Length - 1;
            int variable = head;
            while(variable != -1)
            {
                stamp[variable] = value;
                value--;
                variable = next[variable];
            }

            lastStamp = stamp.Length - 1;
        }
    }
}
