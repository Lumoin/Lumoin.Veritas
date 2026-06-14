using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Deterministic by construction: branching takes the lowest-indexed
/// unassigned variable, <see langword="true"/> first. The same formula
/// always walks the same tree. Working state rents from the supplied
/// pool; only the returned assignment allocates.
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

        foreach(IReadOnlyList<SatLiteral> clause in clauses)
        {
            foreach(SatLiteral literal in clause)
            {
                if(literal.Variable < 0 || literal.Variable >= variableCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(clauses), literal.Variable, $"A literal indexes variable {literal.Variable} outside [0, {variableCount}).");
                }
            }
        }

        foreach(SatLiteral assumption in assumptions)
        {
            if(assumption.Variable < 0 || assumption.Variable >= variableCount)
            {
                throw new ArgumentOutOfRangeException(nameof(assumptions), assumption.Variable, $"An assumption indexes variable {assumption.Variable} outside [0, {variableCount}).");
            }
        }

        MemoryPool<int> effectivePool = pool ?? MemoryPool<int>.Shared;

        return mode switch
        {
            SatSearchMode.ConflictLearning => SolveConflictLearning(clauses, variableCount, assumptions, effectivePool, cancellationToken),
            _ => SolvePropagationOnly(clauses, variableCount, assumptions, effectivePool, cancellationToken),
        };
    }

    /// <summary>
    /// Chronological-backtracking DPLL: a conflict unwinds to the deepest
    /// decision whose second branch is untried, never below the assumption
    /// prefix.
    /// </summary>
    /// <param name="clauses">The formula.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the working buffers rent from.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists.</returns>
    private static SatVerdict SolvePropagationOnly(
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
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

            if(!Propagate(clauses, values, trail, ref trailCount, ref propagations))
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
    /// <param name="clauses">The formula.</param>
    /// <param name="variableCount">The number of variables.</param>
    /// <param name="assumptions">The literals fixed before branching.</param>
    /// <param name="pool">The pool the working buffers rent from.</param>
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists.</returns>
    private static SatVerdict SolveConflictLearning(
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
        int variableCount,
        IReadOnlyList<SatLiteral> assumptions,
        MemoryPool<int> pool,
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

        //The clause database starts as the formula and grows with the
        //first-UIP clauses; reason indices point into it.
        List<IReadOnlyList<SatLiteral>> database = [.. clauses];

        //The reusable scratch for one conflict analysis: the lower-level
        //literals of the learned clause.
        List<SatLiteral> learned = [];

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

            int conflict = PropagateWithReasons(database, values, levels, reasons, trail, ref trailCount, currentLevel, ref propagations);
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
                    List<SatLiteral> core = ExtractCore(database, conflict, assumptions, assumedBy, reasons, values, seen);

                    return new SatVerdict(false, null, core, new SatSolveStatistics(decisions, propagations, conflicts, learnedClauses, maxDecisionLevel));
                }

                int backjumpLevel = Analyze(database, conflict, values, levels, reasons, trail, trailCount, currentLevel, seen, learned, out SatLiteral asserting);

                //Backjump: drop every assignment above the assertion level,
                //then add the learned clause and let it force the asserting
                //literal at that level.
                while(trailCount > 0 && levels[trail[trailCount - 1]] > backjumpLevel)
                {
                    values[trail[trailCount - 1]] = -1;
                    trailCount--;
                }

                currentLevel = backjumpLevel;
                database.Add(learned.ToArray());
                learnedClauses++;
                int learnedIndex = database.Count - 1;
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

    /// <summary>The reason marker for a variable assigned by decision or assumption rather than forced by a clause.</summary>
    private const int NoReason = -1;

    /// <summary>The marker for a variable no assumption fixed; a non-negative entry indexes the assumption that fixed it.</summary>
    private const int NoAssumption = -1;

    /// <summary>The default engine behind the <see cref="SatSolve"/> seam: <see cref="Solve"/> with the shared pool.</summary>
    public static SatSolve Default { get; } = static (clauses, variableCount, cancellationToken) => Solve(clauses, variableCount, pool: null, cancellationToken: cancellationToken);

    /// <summary>The default engine behind the <see cref="SatSolveUnderAssumptions"/> seam: <see cref="SolveUnderAssumptions"/> with the shared pool.</summary>
    public static SatSolveUnderAssumptions DefaultUnderAssumptions { get; } = static (clauses, variableCount, assumptions, cancellationToken) => SolveUnderAssumptions(clauses, variableCount, assumptions, pool: null, cancellationToken: cancellationToken);

    /// <summary>
    /// Runs unit propagation to fixpoint: a clause with every literal
    /// false is a conflict; a clause with one unassigned literal and none
    /// true forces that literal.
    /// </summary>
    /// <param name="clauses">The formula.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="trail">The trail receiving forced assignments.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="propagations">The running count of forced assignments, incremented once per literal this call propagates.</param>
    /// <returns><see langword="false"/> on conflict.</returns>
    private static bool Propagate(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses, Span<int> values, Span<int> trail, ref int trailCount, ref long propagations)
    {
        bool changed = true;
        while(changed)
        {
            changed = false;
            foreach(IReadOnlyList<SatLiteral> clause in clauses)
            {
                bool satisfied = false;
                int unassignedCount = 0;
                SatLiteral unit = default;
                foreach(SatLiteral literal in clause)
                {
                    int value = values[literal.Variable];
                    if(value == -1)
                    {
                        unassignedCount++;
                        unit = literal;
                    }
                    else if((value == 1) == literal.IsPositive)
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
                    values[unit.Variable] = unit.IsPositive ? 1 : 0;
                    trail[trailCount++] = unit.Variable;
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
    /// <param name="database">The clause database, formula then learned clauses.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="levels">The per-variable decision-level column receiving forced levels.</param>
    /// <param name="reasons">The per-variable reason column receiving forced clause indices.</param>
    /// <param name="trail">The trail receiving forced assignments.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="currentLevel">The level forced literals are recorded at.</param>
    /// <param name="propagations">The running count of forced assignments, incremented once per literal this call propagates.</param>
    /// <returns>The index of the conflicting clause, or <c>-1</c> when propagation reaches a conflict-free fixpoint.</returns>
    private static int PropagateWithReasons(List<IReadOnlyList<SatLiteral>> database, Span<int> values, Span<int> levels, Span<int> reasons, Span<int> trail, ref int trailCount, int currentLevel, ref long propagations)
    {
        bool changed = true;
        while(changed)
        {
            changed = false;
            for(int clauseIndex = 0; clauseIndex < database.Count; clauseIndex++)
            {
                IReadOnlyList<SatLiteral> clause = database[clauseIndex];
                bool satisfied = false;
                int unassignedCount = 0;
                SatLiteral unit = default;
                foreach(SatLiteral literal in clause)
                {
                    int value = values[literal.Variable];
                    if(value == -1)
                    {
                        unassignedCount++;
                        unit = literal;
                    }
                    else if((value == 1) == literal.IsPositive)
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
                    values[unit.Variable] = unit.IsPositive ? 1 : 0;
                    levels[unit.Variable] = currentLevel;
                    reasons[unit.Variable] = clauseIndex;
                    trail[trailCount++] = unit.Variable;
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
    /// <param name="database">The clause database holding the conflict and reason clauses.</param>
    /// <param name="conflict">The index of the conflicting clause.</param>
    /// <param name="values">The assignment column; every learned literal is false under it.</param>
    /// <param name="levels">The per-variable decision-level column.</param>
    /// <param name="reasons">The per-variable reason column.</param>
    /// <param name="trail">The trail, walked from its top to find the implication point.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <param name="currentLevel">The conflict's decision level.</param>
    /// <param name="seen">The analysis marker column; entered cleared and left cleared.</param>
    /// <param name="learned">The reusable scratch the lower-level literals collect into; the asserting literal is prepended on return.</param>
    /// <param name="asserting">The asserting literal: the unique-implication-point variable's false literal, which the learned clause forces true after backjump.</param>
    /// <returns>The assertion level: the highest level among the lower-level literals, or <c>0</c> when the learned clause is unit.</returns>
    private static int Analyze(
        List<IReadOnlyList<SatLiteral>> database,
        int conflict,
        Span<int> values,
        Span<int> levels,
        Span<int> reasons,
        Span<int> trail,
        int trailCount,
        int currentLevel,
        Span<int> seen,
        List<SatLiteral> learned,
        out SatLiteral asserting)
    {
        learned.Clear();
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
            foreach(SatLiteral literal in database[reasonClause])
            {
                int variable = literal.Variable;
                if(variable == implicationVariable || seen[variable] != 0 || levels[variable] == 0)
                {
                    continue;
                }

                seen[variable] = 1;
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

        //Clear the markers the lower-level literals still carry, then prepend
        //the implication point's false literal as the asserting literal.
        foreach(SatLiteral literal in learned)
        {
            seen[literal.Variable] = 0;
        }

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
    /// <param name="database">The clause database holding the conflict and reason clauses.</param>
    /// <param name="conflict">The index of the conflicting clause.</param>
    /// <param name="assumptions">The supplied assumptions; <paramref name="assumedBy"/> indexes into this list.</param>
    /// <param name="assumedBy">The per-variable column naming the assumption that fixed each variable, or <see cref="NoAssumption"/>.</param>
    /// <param name="reasons">The per-variable reason column naming the clause that forced each variable, or <see cref="NoReason"/>.</param>
    /// <param name="values">The assignment column; every variable a walked clause mentions is assigned under it.</param>
    /// <param name="seen">The marker column used as the walk's visited set; entered and left cleared.</param>
    /// <returns>The assumptions reachable from the conflict, each once, in the order the walk first reaches them.</returns>
    private static List<SatLiteral> ExtractCore(
        List<IReadOnlyList<SatLiteral>> database,
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
            foreach(SatLiteral literal in database[clauseIndex])
            {
                int variable = literal.Variable;

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
        foreach(SatLiteral assumption in assumptions)
        {
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
}
