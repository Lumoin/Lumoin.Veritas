using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>Whether a language has finitely or infinitely many distinct strings.</summary>
internal enum AutomatonCountKind
{
    /// <summary>A finite language; <see cref="AutomatonCount.Value"/> holds the distinct-string count.</summary>
    Finite,

    /// <summary>An infinite language.</summary>
    Infinite,
}

/// <summary>The value-based result of a distinct-string count.</summary>
/// <param name="Kind">Whether the language is finite or infinite.</param>
/// <param name="Value">The distinct-string count, when finite (saturated at <see cref="long.MaxValue"/>).</param>
internal readonly record struct AutomatonCount(AutomatonCountKind Kind, long Value)
{
    /// <summary>A finite count.</summary>
    /// <param name="value">The distinct-string count.</param>
    /// <returns>The count.</returns>
    public static AutomatonCount Finite(long value)
    {
        return new AutomatonCount(AutomatonCountKind.Finite, value);
    }

    /// <summary>An infinite language.</summary>
    public static AutomatonCount Infinite { get; } = new(AutomatonCountKind.Infinite, 0);
}

/// <summary>
/// Counts the distinct strings a DFA accepts: the DFA is first trimmed to
/// productive-and-useful states (reachable from the initial state and co-reachable to an
/// accepting state); finiteness is decided by whether a topological order covers every useful
/// state (any remaining state sits on a cycle, so the language is infinite); and a finite
/// language is counted by topological dynamic programming where each transition contributes its
/// range width. Counting a DFA — not a raw NFA or a lazy product — is what makes path count equal
/// string count.
/// </summary>
internal static class AutomatonCounting
{
    /// <summary>Counts the distinct strings a deterministic automaton accepts.</summary>
    /// <param name="dfa">The deterministic automaton.</param>
    /// <returns>The finite count, or the infinite verdict.</returns>
    public static AutomatonCount CountDistinct(DeterministicAutomaton dfa)
    {
        int n = dfa.StateCount;

        List<(int To, long Width)>[] forward = new List<(int To, long Width)>[n];
        List<int>[] reverse = new List<int>[n];
        for(int s = 0; s < n; s++)
        {
            forward[s] = [];
            reverse[s] = [];
        }

        for(int s = 0; s < n; s++)
        {
            (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = dfa.TransitionsOf(s);
            for(int i = 0; i < labels.Length; i++)
            {
                forward[s].Add((targets[i], labels[i].Width));
                reverse[targets[i]].Add(s);
            }
        }

        bool[] reachable = ForwardReachable(dfa.InitialState, forward, n);
        bool[] coReachable = CoReachable(dfa, reverse, n);
        bool[] useful = new bool[n];
        int usefulCount = 0;
        for(int s = 0; s < n; s++)
        {
            useful[s] = reachable[s] && coReachable[s];
            if(useful[s])
            {
                usefulCount++;
            }
        }

        if(!useful[dfa.InitialState])
        {
            return AutomatonCount.Finite(0);
        }

        if(!TryTopologicalOrder(forward, useful, usefulCount, out int[] order))
        {
            return AutomatonCount.Infinite;
        }

        long[] distinctFrom = new long[n];
        for(int index = order.Length - 1; index >= 0; index--)
        {
            int state = order[index];
            long total = dfa.IsAccepting(state) ? 1 : 0;
            foreach((int to, long width) in forward[state])
            {
                if(useful[to])
                {
                    total = SaturatingAdd(total, SaturatingMultiply(width, distinctFrom[to]));
                }
            }

            distinctFrom[state] = total;
        }

        return AutomatonCount.Finite(distinctFrom[dfa.InitialState]);
    }

    /// <summary>The states reachable from the initial state over the forward edges.</summary>
    /// <param name="initial">The initial state.</param>
    /// <param name="forward">The forward adjacency.</param>
    /// <param name="stateCount">The state count.</param>
    /// <returns>The per-state reachability flags.</returns>
    private static bool[] ForwardReachable(int initial, List<(int To, long Width)>[] forward, int stateCount)
    {
        bool[] reachable = new bool[stateCount];
        Stack<int> worklist = new();
        reachable[initial] = true;
        worklist.Push(initial);
        while(worklist.Count > 0)
        {
            int state = worklist.Pop();
            foreach((int to, _) in forward[state])
            {
                if(!reachable[to])
                {
                    reachable[to] = true;
                    worklist.Push(to);
                }
            }
        }

        return reachable;
    }

    /// <summary>The states that can reach an accepting state over the forward edges (via the reverse adjacency).</summary>
    /// <param name="dfa">The deterministic automaton.</param>
    /// <param name="reverse">The reverse adjacency.</param>
    /// <param name="stateCount">The state count.</param>
    /// <returns>The per-state co-reachability flags.</returns>
    private static bool[] CoReachable(DeterministicAutomaton dfa, List<int>[] reverse, int stateCount)
    {
        bool[] coReachable = new bool[stateCount];
        Stack<int> worklist = new();
        for(int s = 0; s < stateCount; s++)
        {
            if(dfa.IsAccepting(s))
            {
                coReachable[s] = true;
                worklist.Push(s);
            }
        }

        while(worklist.Count > 0)
        {
            int state = worklist.Pop();
            foreach(int source in reverse[state])
            {
                if(!coReachable[source])
                {
                    coReachable[source] = true;
                    worklist.Push(source);
                }
            }
        }

        return coReachable;
    }

    /// <summary>Attempts a topological order of the useful subgraph by Kahn's algorithm; failure means a cycle.</summary>
    /// <param name="forward">The forward adjacency.</param>
    /// <param name="useful">The per-state useful flags.</param>
    /// <param name="usefulCount">The number of useful states.</param>
    /// <param name="order">The topological order (source to sink), on success.</param>
    /// <returns><see langword="true"/> when the useful subgraph is acyclic.</returns>
    private static bool TryTopologicalOrder(List<(int To, long Width)>[] forward, bool[] useful, int usefulCount, out int[] order)
    {
        int n = forward.Length;
        int[] indegree = new int[n];
        for(int s = 0; s < n; s++)
        {
            if(!useful[s])
            {
                continue;
            }

            foreach((int to, _) in forward[s])
            {
                if(useful[to])
                {
                    indegree[to]++;
                }
            }
        }

        Queue<int> ready = new();
        for(int s = 0; s < n; s++)
        {
            if(useful[s] && indegree[s] == 0)
            {
                ready.Enqueue(s);
            }
        }

        List<int> emitted = [];
        while(ready.Count > 0)
        {
            int state = ready.Dequeue();
            emitted.Add(state);
            foreach((int to, _) in forward[state])
            {
                if(!useful[to])
                {
                    continue;
                }

                indegree[to]--;
                if(indegree[to] == 0)
                {
                    ready.Enqueue(to);
                }
            }
        }

        order = [.. emitted];

        return emitted.Count == usefulCount;
    }

    /// <summary>Adds two non-negative counts, saturating at <see cref="long.MaxValue"/>.</summary>
    /// <param name="first">The first count.</param>
    /// <param name="second">The second count.</param>
    /// <returns>The saturated sum.</returns>
    private static long SaturatingAdd(long first, long second)
    {
        long sum = unchecked(first + second);
        if(sum < first || sum < second)
        {
            return long.MaxValue;
        }

        return sum;
    }

    /// <summary>Multiplies two non-negative counts, saturating at <see cref="long.MaxValue"/>.</summary>
    /// <param name="first">The first count.</param>
    /// <param name="second">The second count.</param>
    /// <returns>The saturated product.</returns>
    private static long SaturatingMultiply(long first, long second)
    {
        if(first == 0 || second == 0)
        {
            return 0;
        }

        if(first > long.MaxValue / second)
        {
            return long.MaxValue;
        }

        return first * second;
    }
}
