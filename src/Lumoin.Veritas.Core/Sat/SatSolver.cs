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
[DebuggerDisplay("SatVerdict {IsSatisfiable}")]
public readonly record struct SatVerdict(bool IsSatisfiable, IReadOnlyList<bool>? Assignment);

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
/// A propositional satisfiability solver over conjunctive normal form:
/// DPLL with unit propagation on an explicit decision trail.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape.</b> The search keeps a trail of assignments and a decision
/// stack of branch points; unit propagation runs to fixpoint between
/// decisions, a conflict unwinds the trail to the deepest decision whose
/// second branch is untried, and an empty decision stack under conflict
/// is the unsatisfiability proof. The trail discipline is the
/// conflict-driven upgrade path: clause learning attaches to the same
/// structure when a workload asks for it.
/// </para>
/// <para>
/// <b>Deliberately the naive first engine.</b> The intended consumers
/// hand over problems a structural device has already bounded —
/// description-logic locality modules, value-space constraint slices,
/// disclosure lattices — where tens to hundreds of variables make
/// propagation-only DPLL sufficient, and correctness is locked by a
/// differential oracle against exhaustive enumeration. No watched
/// literals, no activity heuristics, no clause learning: this engine is
/// expected to be superseded behind the <see cref="SatSolve"/> seam
/// (conflict-driven search on this same trail, or the bit-parallel
/// sibling) once a measured workload outgrows it — the seam, not this
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
    /// <param name="cancellationToken">A token that aborts the search between propagation rounds.</param>
    /// <returns>The verdict, with a satisfying assignment when one exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clauses"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative or a literal indexes beyond it.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static SatVerdict Solve(
        IReadOnlyList<IReadOnlyList<SatLiteral>> clauses,
        int variableCount,
        MemoryPool<int>? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clauses);
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

        MemoryPool<int> effectivePool = pool ?? MemoryPool<int>.Shared;

        //Working state, all variable-bounded: the assignment (-1
        //unassigned, 0 false, 1 true), the trail of assigned variables,
        //and the decision stack as three parallel columns (trail depth,
        //variable, second-branch-tried flag).
        using IMemoryOwner<int> valuesOwner = effectivePool.Rent(variableCount);
        using IMemoryOwner<int> trailOwner = effectivePool.Rent(variableCount);
        using IMemoryOwner<int> decisionDepthsOwner = effectivePool.Rent(variableCount);
        using IMemoryOwner<int> decisionVariablesOwner = effectivePool.Rent(variableCount);
        using IMemoryOwner<int> decisionTriedOwner = effectivePool.Rent(variableCount);

        Span<int> values = valuesOwner.Memory.Span[..variableCount];
        Span<int> trail = trailOwner.Memory.Span[..variableCount];
        Span<int> decisionDepths = decisionDepthsOwner.Memory.Span[..variableCount];
        Span<int> decisionVariables = decisionVariablesOwner.Memory.Span[..variableCount];
        Span<int> decisionTried = decisionTriedOwner.Memory.Span[..variableCount];
        values.Fill(-1);
        int trailCount = 0;
        int decisionCount = 0;

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!Propagate(clauses, values, trail, ref trailCount))
            {
                //Conflict: unwind to the deepest decision whose second
                //branch is untried; none left proves unsatisfiability.
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
                    branched = true;

                    break;
                }

                if(!branched)
                {
                    return new SatVerdict(false, null);
                }

                continue;
            }

            //No conflict and propagation is at fixpoint: branch on the
            //lowest unassigned variable, true first.
            int next = values.IndexOf(-1);
            if(next < 0)
            {
                bool[] assignment = new bool[variableCount];
                for(int i = 0; i < variableCount; i++)
                {
                    assignment[i] = values[i] == 1;
                }

                return new SatVerdict(true, assignment);
            }

            decisionDepths[decisionCount] = trailCount;
            decisionVariables[decisionCount] = next;
            decisionTried[decisionCount] = 0;
            decisionCount++;
            values[next] = 1;
            trail[trailCount++] = next;
        }
    }

    /// <summary>The default engine behind the <see cref="SatSolve"/> seam: <see cref="Solve"/> with the shared pool.</summary>
    public static SatSolve Default { get; } = static (clauses, variableCount, cancellationToken) => Solve(clauses, variableCount, pool: null, cancellationToken);

    /// <summary>
    /// Runs unit propagation to fixpoint: a clause with every literal
    /// false is a conflict; a clause with one unassigned literal and none
    /// true forces that literal.
    /// </summary>
    /// <param name="clauses">The formula.</param>
    /// <param name="values">The assignment column.</param>
    /// <param name="trail">The trail receiving forced assignments.</param>
    /// <param name="trailCount">The trail's live length.</param>
    /// <returns><see langword="false"/> on conflict.</returns>
    private static bool Propagate(IReadOnlyList<IReadOnlyList<SatLiteral>> clauses, Span<int> values, Span<int> trail, ref int trailCount)
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
                    changed = true;
                }
            }
        }

        return true;
    }
}
