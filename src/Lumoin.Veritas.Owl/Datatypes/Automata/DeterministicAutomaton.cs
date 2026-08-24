using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// An immutable deterministic finite automaton over the XML Char alphabet, produced by
/// <see cref="SubsetConstruction"/>. Each state's out-transitions carry disjoint code-point
/// ranges, held in flat row-indexed arrays; a partial DFA omits transitions for uncovered
/// code points, so it is not necessarily complete over the universe until
/// <see cref="Complement"/> completes it against a dead state.
/// </summary>
internal sealed class DeterministicAutomaton
{
    /// <summary>The number of states.</summary>
    public int StateCount { get; }

    /// <summary>The single initial state.</summary>
    public int InitialState { get; }

    /// <summary>Whether each state is accepting.</summary>
    private bool[] AcceptingStates { get; }

    /// <summary>Row-start offsets into the transition arrays, indexed by source state.</summary>
    private int[] RowStart { get; }

    /// <summary>The disjoint range labels, grouped by source state.</summary>
    private CodePointRange[] Labels { get; }

    /// <summary>The target states, grouped by source state.</summary>
    private int[] Targets { get; }

    /// <summary>Wraps the frozen arrays.</summary>
    /// <param name="stateCount">The number of states.</param>
    /// <param name="initialState">The initial state.</param>
    /// <param name="acceptingStates">The per-state accepting flags.</param>
    /// <param name="rowStart">The row offsets.</param>
    /// <param name="labels">The disjoint range labels.</param>
    /// <param name="targets">The targets.</param>
    private DeterministicAutomaton(int stateCount, int initialState, bool[] acceptingStates, int[] rowStart, CodePointRange[] labels, int[] targets)
    {
        StateCount = stateCount;
        InitialState = initialState;
        AcceptingStates = acceptingStates;
        RowStart = rowStart;
        Labels = labels;
        Targets = targets;
    }

    /// <summary>Whether a state is accepting.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> when the state is accepting.</returns>
    public bool IsAccepting(int state)
    {
        return AcceptingStates[state];
    }

    /// <summary>The out-transitions of a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The disjoint labels and targets, as parallel spans.</returns>
    public RangeTransitionView TransitionsOf(int state)
    {
        int start = RowStart[state];
        int end = RowStart[state + 1];

        return new RangeTransitionView(Labels.AsSpan(start, end - start), Targets.AsSpan(start, end - start));
    }

    /// <summary>Builds a DFA from an unordered transition list, grouping transitions by source without a comparison sort.</summary>
    /// <param name="stateCount">The number of states.</param>
    /// <param name="initialState">The initial state.</param>
    /// <param name="acceptingStates">The per-state accepting flags.</param>
    /// <param name="transitions">The transitions, in any order.</param>
    /// <returns>The frozen DFA.</returns>
    public static DeterministicAutomaton FromTransitions(int stateCount, int initialState, bool[] acceptingStates, List<(int From, CodePointRange Label, int To)> transitions)
    {
        int[] rowStart = new int[stateCount + 1];
        foreach((int from, _, _) in transitions)
        {
            rowStart[from + 1]++;
        }

        for(int s = 0; s < stateCount; s++)
        {
            rowStart[s + 1] += rowStart[s];
        }

        CodePointRange[] labels = new CodePointRange[transitions.Count];
        int[] targets = new int[transitions.Count];
        int[] cursor = new int[stateCount];
        foreach((int from, CodePointRange label, int to) in transitions)
        {
            int position = rowStart[from] + cursor[from];
            labels[position] = label;
            targets[position] = to;
            cursor[from]++;
        }

        return new DeterministicAutomaton(stateCount, initialState, acceptingStates, rowStart, labels, targets);
    }

    /// <summary>Whether the DFA accepts a code-point sequence.</summary>
    /// <param name="codePoints">The input string as code points.</param>
    /// <returns><see langword="true"/> when the run ends in an accepting state.</returns>
    public bool Accepts(ReadOnlySpan<int> codePoints)
    {
        int state = InitialState;
        foreach(int codePoint in codePoints)
        {
            (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = TransitionsOf(state);
            int next = -1;
            for(int i = 0; i < labels.Length; i++)
            {
                if(labels[i].Contains(codePoint))
                {
                    next = targets[i];
                    break;
                }
            }

            if(next < 0)
            {
                return false;
            }

            state = next;
        }

        return AcceptingStates[state];
    }

    /// <summary>Whether the language is empty — no accepting state is reachable from the initial state.</summary>
    /// <returns><see langword="true"/> when the language is empty.</returns>
    public bool IsEmptyLanguage()
    {
        bool[] visited = new bool[StateCount];
        Stack<int> worklist = new();
        visited[InitialState] = true;
        worklist.Push(InitialState);
        while(worklist.Count > 0)
        {
            int state = worklist.Pop();
            if(AcceptingStates[state])
            {
                return false;
            }

            (_, ReadOnlySpan<int> targets) = TransitionsOf(state);
            foreach(int target in targets)
            {
                if(!visited[target])
                {
                    visited[target] = true;
                    worklist.Push(target);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The universe-bounded complement DFA: the partial DFA is completed against a fresh
    /// dead state (every universe code point a state does not cover routes to it), then every
    /// state's acceptance is flipped, so the complement accepts exactly the universe strings
    /// this automaton rejects.
    /// </summary>
    /// <returns>The complement DFA.</returns>
    public DeterministicAutomaton Complement()
    {
        int dead = StateCount;
        int newStateCount = StateCount + 1;
        List<(int From, CodePointRange Label, int To)> transitions = [];

        for(int state = 0; state < StateCount; state++)
        {
            (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = TransitionsOf(state);
            CodePointSet covered = CodePointSet.Empty;
            for(int i = 0; i < labels.Length; i++)
            {
                transitions.Add((state, labels[i], targets[i]));
                covered = CodePointSet.Union(covered, CodePointSet.Of([labels[i]]));
            }

            foreach(CodePointRange gap in XmlCharAlphabet.Complement(covered).Ranges)
            {
                transitions.Add((state, gap, dead));
            }
        }

        foreach(CodePointRange range in XmlCharAlphabet.Universe.Ranges)
        {
            transitions.Add((dead, range, dead));
        }

        bool[] accepting = new bool[newStateCount];
        for(int state = 0; state < StateCount; state++)
        {
            accepting[state] = !AcceptingStates[state];
        }

        accepting[dead] = true;

        return FromTransitions(newStateCount, InitialState, accepting, transitions);
    }
}
