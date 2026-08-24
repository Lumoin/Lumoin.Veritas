using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// A mutable builder that mints states and range-labelled or epsilon transitions and
/// freezes to an immutable <see cref="NondeterministicAutomaton"/>. It tracks a state
/// ceiling: once the ceiling is crossed the builder keeps a value-based
/// <see cref="BudgetExceeded"/> flag the Thompson construction reads to abandon a
/// pathological expansion rather than run away.
/// </summary>
internal sealed class NondeterministicAutomatonBuilder
{
    /// <summary>The source state of each range-labelled transition.</summary>
    private List<int> SymbolFrom { get; } = [];

    /// <summary>The label of each range-labelled transition.</summary>
    private List<CodePointRange> SymbolLabel { get; } = [];

    /// <summary>The target state of each range-labelled transition.</summary>
    private List<int> SymbolTo { get; } = [];

    /// <summary>The source state of each epsilon transition.</summary>
    private List<int> EpsilonFrom { get; } = [];

    /// <summary>The target state of each epsilon transition.</summary>
    private List<int> EpsilonTo { get; } = [];

    /// <summary>The initial states.</summary>
    private List<int> Initial { get; } = [];

    /// <summary>The accepting states.</summary>
    private List<int> Accepting { get; } = [];

    /// <summary>The number of states minted so far.</summary>
    public int StateCount { get; private set; }

    /// <summary>The most states permitted before the build is abandoned.</summary>
    private int MaxStates { get; }

    /// <summary>Whether the state ceiling has been crossed.</summary>
    public bool BudgetExceeded { get; private set; }

    /// <summary>Creates a builder with the given state ceiling.</summary>
    /// <param name="maxStates">The most states the build may mint.</param>
    public NondeterministicAutomatonBuilder(int maxStates)
    {
        MaxStates = maxStates;
    }

    /// <summary>Mints a fresh state and flags the ceiling breach when it is crossed.</summary>
    /// <returns>The new state id.</returns>
    public int AddState()
    {
        int id = StateCount;
        StateCount++;
        if(StateCount > MaxStates)
        {
            BudgetExceeded = true;
        }

        return id;
    }

    /// <summary>Adds a range-labelled transition.</summary>
    /// <param name="from">The source state.</param>
    /// <param name="label">The code-point range that enables the transition.</param>
    /// <param name="to">The target state.</param>
    public void AddTransition(int from, CodePointRange label, int to)
    {
        SymbolFrom.Add(from);
        SymbolLabel.Add(label);
        SymbolTo.Add(to);
    }

    /// <summary>Adds a range-labelled transition for every range of a set (no transition when the set is empty).</summary>
    /// <param name="from">The source state.</param>
    /// <param name="set">The code-point set that enables the transition.</param>
    /// <param name="to">The target state.</param>
    public void AddTransitions(int from, CodePointSet set, int to)
    {
        foreach(CodePointRange range in set.Ranges)
        {
            AddTransition(from, range, to);
        }
    }

    /// <summary>Adds an epsilon transition.</summary>
    /// <param name="from">The source state.</param>
    /// <param name="to">The target state.</param>
    public void AddEpsilon(int from, int to)
    {
        EpsilonFrom.Add(from);
        EpsilonTo.Add(to);
    }

    /// <summary>Marks a state initial.</summary>
    /// <param name="state">The state.</param>
    public void MarkInitial(int state)
    {
        Initial.Add(state);
    }

    /// <summary>Marks a state accepting.</summary>
    /// <param name="state">The state.</param>
    public void MarkAccepting(int state)
    {
        Accepting.Add(state);
    }

    /// <summary>Freezes the builder into an immutable automaton with row-indexed transition storage.</summary>
    /// <returns>The frozen automaton.</returns>
    public NondeterministicAutomaton Build()
    {
        int states = StateCount;

        int[] symbolRowStart = new int[states + 1];
        CodePointRange[] symbolLabels = new CodePointRange[SymbolFrom.Count];
        int[] symbolTargets = new int[SymbolFrom.Count];
        BuildRows(SymbolFrom, states, symbolRowStart, out int[] symbolOrder);
        for(int i = 0; i < SymbolFrom.Count; i++)
        {
            int source = symbolOrder[i];
            symbolLabels[i] = SymbolLabel[source];
            symbolTargets[i] = SymbolTo[source];
        }

        int[] epsilonRowStart = new int[states + 1];
        int[] epsilonTargets = new int[EpsilonFrom.Count];
        BuildRows(EpsilonFrom, states, epsilonRowStart, out int[] epsilonOrder);
        for(int i = 0; i < EpsilonFrom.Count; i++)
        {
            epsilonTargets[i] = EpsilonTo[epsilonOrder[i]];
        }

        bool[] accepting = new bool[states];
        foreach(int state in Accepting)
        {
            accepting[state] = true;
        }

        int[] initialStates = [.. Initial];

        return new NondeterministicAutomaton(states, symbolRowStart, symbolLabels, symbolTargets, epsilonRowStart, epsilonTargets, accepting, initialStates);
    }

    /// <summary>Counting-sorts transition indices by source state into row-start offsets, without any comparison sort.</summary>
    /// <param name="sources">The per-transition source states.</param>
    /// <param name="states">The state count.</param>
    /// <param name="rowStart">The row-start offsets to fill (length <paramref name="states"/> + 1).</param>
    /// <param name="order">The transition indices grouped by source, in row order.</param>
    private static void BuildRows(List<int> sources, int states, int[] rowStart, out int[] order)
    {
        for(int i = 0; i < sources.Count; i++)
        {
            rowStart[sources[i] + 1]++;
        }

        for(int s = 0; s < states; s++)
        {
            rowStart[s + 1] += rowStart[s];
        }

        order = new int[sources.Count];
        int[] cursor = new int[states];
        for(int i = 0; i < sources.Count; i++)
        {
            int source = sources[i];
            int position = rowStart[source] + cursor[source];
            order[position] = i;
            cursor[source]++;
        }
    }
}

/// <summary>
/// An immutable epsilon-nondeterministic finite automaton over the XML Char alphabet,
/// held in flat row-indexed arrays: range-labelled transitions and epsilon transitions
/// are each grouped by source state through a row-start offset table, so a state's
/// out-edges are a contiguous span. Range labels are non-empty by construction, so
/// language emptiness reduces to plain graph reachability.
/// </summary>
internal sealed class NondeterministicAutomaton
{
    /// <summary>The number of states.</summary>
    public int StateCount { get; }

    /// <summary>Row-start offsets into the range-labelled transition arrays, indexed by source state.</summary>
    private int[] SymbolRowStart { get; }

    /// <summary>The range labels of the range-labelled transitions, grouped by source state.</summary>
    private CodePointRange[] SymbolLabels { get; }

    /// <summary>The target states of the range-labelled transitions, grouped by source state.</summary>
    private int[] SymbolTargets { get; }

    /// <summary>Row-start offsets into the epsilon transition arrays, indexed by source state.</summary>
    private int[] EpsilonRowStart { get; }

    /// <summary>The target states of the epsilon transitions, grouped by source state.</summary>
    private int[] EpsilonTargets { get; }

    /// <summary>Whether each state is accepting.</summary>
    private bool[] AcceptingStates { get; }

    /// <summary>The initial states.</summary>
    public int[] InitialStates { get; }

    /// <summary>Wraps the frozen arrays.</summary>
    /// <param name="stateCount">The number of states.</param>
    /// <param name="symbolRowStart">The range-labelled row offsets.</param>
    /// <param name="symbolLabels">The range labels.</param>
    /// <param name="symbolTargets">The range-labelled targets.</param>
    /// <param name="epsilonRowStart">The epsilon row offsets.</param>
    /// <param name="epsilonTargets">The epsilon targets.</param>
    /// <param name="acceptingStates">The per-state accepting flags.</param>
    /// <param name="initialStates">The initial states.</param>
    public NondeterministicAutomaton(int stateCount, int[] symbolRowStart, CodePointRange[] symbolLabels, int[] symbolTargets, int[] epsilonRowStart, int[] epsilonTargets, bool[] acceptingStates, int[] initialStates)
    {
        StateCount = stateCount;
        SymbolRowStart = symbolRowStart;
        SymbolLabels = symbolLabels;
        SymbolTargets = symbolTargets;
        EpsilonRowStart = epsilonRowStart;
        EpsilonTargets = epsilonTargets;
        AcceptingStates = acceptingStates;
        InitialStates = initialStates;
    }

    /// <summary>Whether a state is accepting.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> when the state is accepting.</returns>
    public bool IsAccepting(int state)
    {
        return AcceptingStates[state];
    }

    /// <summary>The range-labelled out-transitions of a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The labels and targets, as parallel spans.</returns>
    public RangeTransitionView SymbolTransitions(int state)
    {
        int start = SymbolRowStart[state];
        int end = SymbolRowStart[state + 1];

        return new RangeTransitionView(SymbolLabels.AsSpan(start, end - start), SymbolTargets.AsSpan(start, end - start));
    }

    /// <summary>The epsilon out-targets of a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The epsilon targets.</returns>
    public ReadOnlySpan<int> EpsilonTargetsOf(int state)
    {
        int start = EpsilonRowStart[state];
        int end = EpsilonRowStart[state + 1];

        return EpsilonTargets.AsSpan(start, end - start);
    }

    /// <summary>Appends the epsilon closure of a seed set to a result list, without revisiting or recursion.</summary>
    /// <param name="seed">The seed states.</param>
    /// <param name="inClosure">A per-state membership scratch of length <see cref="StateCount"/>, reset by the caller.</param>
    /// <param name="resultToAppendTo">The list the closure states are appended to, in discovery order.</param>
    public void AppendEpsilonClosure(ReadOnlySpan<int> seed, bool[] inClosure, List<int> resultToAppendTo)
    {
        Stack<int> worklist = new();
        foreach(int state in seed)
        {
            if(!inClosure[state])
            {
                inClosure[state] = true;
                resultToAppendTo.Add(state);
                worklist.Push(state);
            }
        }

        while(worklist.Count > 0)
        {
            int state = worklist.Pop();
            foreach(int target in EpsilonTargetsOf(state))
            {
                if(!inClosure[target])
                {
                    inClosure[target] = true;
                    resultToAppendTo.Add(target);
                    worklist.Push(target);
                }
            }
        }
    }

    /// <summary>Whether the automaton accepts a code-point sequence, by direct epsilon-closure simulation.</summary>
    /// <param name="codePoints">The input string as code points.</param>
    /// <returns><see langword="true"/> when some run reaches an accepting state.</returns>
    public bool Accepts(ReadOnlySpan<int> codePoints)
    {
        bool[] scratch = new bool[StateCount];
        List<int> current = [];
        AppendEpsilonClosure(InitialStates, scratch, current);

        List<int> raw = [];
        foreach(int codePoint in codePoints)
        {
            raw.Clear();
            foreach(int state in current)
            {
                (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = SymbolTransitions(state);
                for(int i = 0; i < labels.Length; i++)
                {
                    if(labels[i].Contains(codePoint))
                    {
                        raw.Add(targets[i]);
                    }
                }
            }

            Array.Clear(scratch);
            current = [];
            AppendEpsilonClosure(CollectionsMarshalSpan(raw), scratch, current);
            if(current.Count == 0)
            {
                return false;
            }
        }

        foreach(int state in current)
        {
            if(AcceptingStates[state])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the language is empty — no accepting state is reachable from any initial state.</summary>
    /// <returns><see langword="true"/> when the language is empty.</returns>
    public bool IsEmptyLanguage()
    {
        bool[] visited = new bool[StateCount];
        Stack<int> worklist = new();
        foreach(int state in InitialStates)
        {
            if(!visited[state])
            {
                visited[state] = true;
                worklist.Push(state);
            }
        }

        while(worklist.Count > 0)
        {
            int state = worklist.Pop();
            if(AcceptingStates[state])
            {
                return false;
            }

            foreach(int target in EpsilonTargetsOf(state))
            {
                if(!visited[target])
                {
                    visited[target] = true;
                    worklist.Push(target);
                }
            }

            (_, ReadOnlySpan<int> targets) = SymbolTransitions(state);
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

    /// <summary>Views a list's backing store as a span for closure seeding without an intermediate copy.</summary>
    /// <param name="values">The list.</param>
    /// <returns>The span over the list contents.</returns>
    private static ReadOnlySpan<int> CollectionsMarshalSpan(List<int> values)
    {
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
    }
}
