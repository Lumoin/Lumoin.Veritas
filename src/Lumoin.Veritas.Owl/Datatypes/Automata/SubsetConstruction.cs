using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>Whether a determinization completed or hit the subset ceiling.</summary>
internal enum DeterminizeOutcome
{
    /// <summary>The DFA was built within budget.</summary>
    Done,

    /// <summary>The subset ceiling was crossed; the caller consumes this as an abstention.</summary>
    BudgetExceeded,
}

/// <summary>The value-based result of a determinization.</summary>
/// <param name="Outcome">Whether determinization completed.</param>
/// <param name="Automaton">The built DFA, on success.</param>
internal readonly record struct DeterminizeResult(DeterminizeOutcome Outcome, DeterministicAutomaton? Automaton)
{
    /// <summary>A completed determinization.</summary>
    /// <param name="automaton">The built DFA.</param>
    /// <returns>The result.</returns>
    public static DeterminizeResult Done(DeterministicAutomaton automaton)
    {
        return new DeterminizeResult(DeterminizeOutcome.Done, automaton);
    }

    /// <summary>A determinization that crossed the subset ceiling.</summary>
    /// <returns>The result.</returns>
    public static DeterminizeResult BudgetExceeded()
    {
        return new DeterminizeResult(DeterminizeOutcome.BudgetExceeded, null);
    }
}

/// <summary>
/// Determinizes an epsilon-NFA over range-labelled transitions into a DFA by subset
/// construction. Each subset's outgoing labels are split at their endpoints into elementary
/// intervals, and every interval routes to the epsilon-closed union of the covering targets,
/// so the result carries disjoint per-state ranges. The subset count is bounded by
/// <see cref="AutomatonBudgets.MaxDfaStates"/>; a breach is a value-based abstention. The
/// resulting DFA is partial: uncovered code points have no transition until
/// <see cref="DeterministicAutomaton.Complement"/> completes it.
/// </summary>
internal static class SubsetConstruction
{
    /// <summary>Determinizes an NFA within the given subset ceiling.</summary>
    /// <param name="nfa">The source automaton.</param>
    /// <param name="maxDfaStates">The most subsets the construction may mint.</param>
    /// <returns>The determinization result.</returns>
    public static DeterminizeResult Determinize(NondeterministicAutomaton nfa, int maxDfaStates)
    {
        Dictionary<int[], int> memo = new(SortedIntArrayComparer.Instance);
        Queue<(int Id, int[] Set)> pending = new();
        List<bool> accepting = [];
        List<(int From, CodePointRange Label, int To)> transitions = [];
        bool[] scratch = new bool[nfa.StateCount];

        List<int> startClosure = [];
        nfa.AppendEpsilonClosure(nfa.InitialStates, scratch, startClosure);
        bool budgetExceeded = false;
        GetOrCreate(memo, pending, accepting, SortAndDedup(startClosure), maxDfaStates, ref budgetExceeded);

        List<(CodePointRange Label, int Target)> edges = [];
        List<int> bounds = [];
        List<int> rawTargets = [];
        List<int> closure = [];

        while(pending.Count > 0 && !budgetExceeded)
        {
            (int id, int[] set) = pending.Dequeue();
            accepting[id] = AnyAccepting(nfa, set);

            edges.Clear();
            foreach(int state in set)
            {
                (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = nfa.SymbolTransitions(state);
                for(int i = 0; i < labels.Length; i++)
                {
                    edges.Add((labels[i], targets[i]));
                }
            }

            if(edges.Count == 0)
            {
                continue;
            }

            bounds.Clear();
            foreach((CodePointRange label, _) in edges)
            {
                bounds.Add(label.Low);
                bounds.Add(label.High + 1);
            }

            int[] boundary = SortAndDedup(bounds);
            for(int k = 0; k < boundary.Length - 1; k++)
            {
                int low = boundary[k];
                int high = boundary[k + 1] - 1;

                rawTargets.Clear();
                foreach((CodePointRange label, int target) in edges)
                {
                    if(label.Low <= low && high <= label.High)
                    {
                        rawTargets.Add(target);
                    }
                }

                if(rawTargets.Count == 0)
                {
                    continue;
                }

                Array.Clear(scratch);
                closure.Clear();
                nfa.AppendEpsilonClosure(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rawTargets), scratch, closure);
                int targetId = GetOrCreate(memo, pending, accepting, SortAndDedup(closure), maxDfaStates, ref budgetExceeded);
                transitions.Add((id, new CodePointRange(low, high), targetId));
                if(budgetExceeded)
                {
                    break;
                }
            }
        }

        if(budgetExceeded)
        {
            return DeterminizeResult.BudgetExceeded();
        }

        return DeterminizeResult.Done(DeterministicAutomaton.FromTransitions(memo.Count, 0, [.. accepting], transitions));
    }

    /// <summary>Returns the DFA id of a subset, minting one when new and flagging a ceiling breach.</summary>
    /// <param name="memo">The subset-to-id map.</param>
    /// <param name="pending">The worklist of unprocessed subsets.</param>
    /// <param name="accepting">The per-DFA-state accepting flags, extended as states mint.</param>
    /// <param name="set">The sorted, de-duplicated NFA-state subset.</param>
    /// <param name="maxDfaStates">The subset ceiling.</param>
    /// <param name="budgetExceeded">Set when the ceiling is crossed.</param>
    /// <returns>The DFA id.</returns>
    private static int GetOrCreate(Dictionary<int[], int> memo, Queue<(int Id, int[] Set)> pending, List<bool> accepting, int[] set, int maxDfaStates, ref bool budgetExceeded)
    {
        if(memo.TryGetValue(set, out int existing))
        {
            return existing;
        }

        int id = memo.Count;
        memo[set] = id;
        accepting.Add(false);
        pending.Enqueue((id, set));
        if(memo.Count > maxDfaStates)
        {
            budgetExceeded = true;
        }

        return id;
    }

    /// <summary>Whether any NFA state in a subset is accepting.</summary>
    /// <param name="nfa">The source automaton.</param>
    /// <param name="set">The subset.</param>
    /// <returns><see langword="true"/> when the subset is accepting.</returns>
    private static bool AnyAccepting(NondeterministicAutomaton nfa, int[] set)
    {
        foreach(int state in set)
        {
            if(nfa.IsAccepting(state))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sorts and de-duplicates a list of state ids into a canonical subset key.</summary>
    /// <param name="values">The state ids.</param>
    /// <returns>The sorted, de-duplicated array.</returns>
    private static int[] SortAndDedup(List<int> values)
    {
        if(values.Count == 0)
        {
            return Array.Empty<int>();
        }

        int[] sorted = [.. values];
        Array.Sort(sorted);
        int write = 1;
        for(int read = 1; read < sorted.Length; read++)
        {
            if(sorted[read] != sorted[write - 1])
            {
                sorted[write] = sorted[read];
                write++;
            }
        }

        if(write == sorted.Length)
        {
            return sorted;
        }

        int[] trimmed = new int[write];
        Array.Copy(sorted, trimmed, write);

        return trimmed;
    }

    /// <summary>Elementwise equality and order-independent hashing of sorted subset keys.</summary>
    private sealed class SortedIntArrayComparer : IEqualityComparer<int[]>
    {
        /// <summary>The shared comparer instance.</summary>
        public static SortedIntArrayComparer Instance { get; } = new();

        /// <summary>Whether two sorted subset keys are elementwise equal.</summary>
        /// <param name="x">The first key.</param>
        /// <param name="y">The second key.</param>
        /// <returns><see langword="true"/> when equal.</returns>
        public bool Equals(int[]? x, int[]? y)
        {
            if(ReferenceEquals(x, y))
            {
                return true;
            }

            if(x is null || y is null || x.Length != y.Length)
            {
                return false;
            }

            for(int i = 0; i < x.Length; i++)
            {
                if(x[i] != y[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A hash of a sorted subset key.</summary>
        /// <param name="obj">The key.</param>
        /// <returns>The hash code.</returns>
        public int GetHashCode(int[] obj)
        {
            HashCode hash = default;
            foreach(int value in obj)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
