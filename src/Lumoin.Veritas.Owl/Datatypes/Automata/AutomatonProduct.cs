using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>The value-based verdict of an intersection-emptiness query.</summary>
internal enum ProductEmptiness
{
    /// <summary>The intersection language is empty.</summary>
    Empty,

    /// <summary>The intersection language is non-empty (an accepting pair-state is reachable).</summary>
    NonEmpty,

    /// <summary>The pair-state ceiling was crossed; the caller consumes this as an abstention.</summary>
    BudgetExceeded,
}

/// <summary>
/// Decides emptiness of the intersection of two epsilon-NFA languages by a lazy worklist
/// over pair-states, memoized in a <c>(int, int)</c> keyed dictionary. It never materializes
/// the full product: only reachable pairs are visited, and the first reachable accepting pair
/// proves non-emptiness. The pair-state count is bounded by
/// <see cref="AutomatonBudgets.MaxProductStates"/>; a breach is a value-based abstention.
/// </summary>
internal static class AutomatonProduct
{
    /// <summary>Decides whether the intersection of two automaton languages is empty.</summary>
    /// <param name="first">The first automaton.</param>
    /// <param name="second">The second automaton.</param>
    /// <param name="maxProductStates">The most pair-states the worklist may visit.</param>
    /// <returns>The emptiness verdict.</returns>
    public static ProductEmptiness IsIntersectionEmpty(NondeterministicAutomaton first, NondeterministicAutomaton second, int maxProductStates)
    {
        Dictionary<(int, int), int> memo = [];
        Queue<(int, int)> worklist = new();

        foreach(int a in first.InitialStates)
        {
            foreach(int b in second.InitialStates)
            {
                if(TryEnqueue(memo, worklist, (a, b), maxProductStates, out bool seededBreach) && seededBreach)
                {
                    return ProductEmptiness.BudgetExceeded;
                }
            }
        }

        while(worklist.Count > 0)
        {
            (int a, int b) = worklist.Dequeue();
            if(first.IsAccepting(a) && second.IsAccepting(b))
            {
                return ProductEmptiness.NonEmpty;
            }

            foreach(int nextA in first.EpsilonTargetsOf(a))
            {
                if(Visit(memo, worklist, (nextA, b), maxProductStates))
                {
                    return ProductEmptiness.BudgetExceeded;
                }
            }

            foreach(int nextB in second.EpsilonTargetsOf(b))
            {
                if(Visit(memo, worklist, (a, nextB), maxProductStates))
                {
                    return ProductEmptiness.BudgetExceeded;
                }
            }

            (ReadOnlySpan<CodePointRange> aLabels, ReadOnlySpan<int> aTargets) = first.SymbolTransitions(a);
            (ReadOnlySpan<CodePointRange> bLabels, ReadOnlySpan<int> bTargets) = second.SymbolTransitions(b);
            for(int i = 0; i < aLabels.Length; i++)
            {
                for(int j = 0; j < bLabels.Length; j++)
                {
                    if(RangesOverlap(aLabels[i], bLabels[j]) && Visit(memo, worklist, (aTargets[i], bTargets[j]), maxProductStates))
                    {
                        return ProductEmptiness.BudgetExceeded;
                    }
                }
            }
        }

        return ProductEmptiness.Empty;
    }

    /// <summary>Enqueues a pair-state during expansion, reporting a ceiling breach.</summary>
    /// <param name="memo">The visited pair map.</param>
    /// <param name="worklist">The worklist.</param>
    /// <param name="pair">The pair-state.</param>
    /// <param name="maxProductStates">The pair-state ceiling.</param>
    /// <returns><see langword="true"/> when the ceiling was crossed adding this pair.</returns>
    private static bool Visit(Dictionary<(int, int), int> memo, Queue<(int, int)> worklist, (int, int) pair, int maxProductStates)
    {
        TryEnqueue(memo, worklist, pair, maxProductStates, out bool breached);

        return breached;
    }

    /// <summary>Adds a pair-state to the memo and worklist when new, reporting a ceiling breach.</summary>
    /// <param name="memo">The visited pair map.</param>
    /// <param name="worklist">The worklist.</param>
    /// <param name="pair">The pair-state.</param>
    /// <param name="maxProductStates">The pair-state ceiling.</param>
    /// <param name="budgetExceeded">Whether the ceiling was crossed by this add.</param>
    /// <returns><see langword="true"/> when the pair was newly added.</returns>
    private static bool TryEnqueue(Dictionary<(int, int), int> memo, Queue<(int, int)> worklist, (int, int) pair, int maxProductStates, out bool budgetExceeded)
    {
        budgetExceeded = false;
        if(memo.ContainsKey(pair))
        {
            return false;
        }

        memo[pair] = memo.Count;
        worklist.Enqueue(pair);
        if(memo.Count > maxProductStates)
        {
            budgetExceeded = true;
        }

        return true;
    }

    /// <summary>Whether two inclusive ranges share a code point.</summary>
    /// <param name="first">The first range.</param>
    /// <param name="second">The second range.</param>
    /// <returns><see langword="true"/> when they overlap.</returns>
    private static bool RangesOverlap(CodePointRange first, CodePointRange second)
    {
        return Math.Max(first.Low, second.Low) <= Math.Min(first.High, second.High);
    }
}
