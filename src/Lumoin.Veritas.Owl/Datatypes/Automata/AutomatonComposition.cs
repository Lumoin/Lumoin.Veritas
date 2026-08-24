using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>Whether a product-automaton construction completed within budget.</summary>
internal enum ProductStatus
{
    /// <summary>The product automaton was built within the pair-state ceiling.</summary>
    Built,

    /// <summary>The pair-state ceiling was crossed; the caller consumes this as an abstention.</summary>
    BudgetExceeded,
}

/// <summary>The value-based result of a product-automaton construction.</summary>
/// <param name="Status">Whether the product was built.</param>
/// <param name="Automaton">The product automaton, on success.</param>
internal readonly record struct ProductResult(ProductStatus Status, NondeterministicAutomaton? Automaton)
{
    /// <summary>A built product.</summary>
    /// <param name="automaton">The product automaton.</param>
    /// <returns>The result.</returns>
    public static ProductResult Built(NondeterministicAutomaton automaton)
    {
        return new ProductResult(ProductStatus.Built, automaton);
    }

    /// <summary>A product that crossed the pair-state ceiling.</summary>
    /// <returns>The result.</returns>
    public static ProductResult BudgetExceeded()
    {
        return new ProductResult(ProductStatus.BudgetExceeded, null);
    }
}

/// <summary>
/// Materializing compositions of the range-labelled automata the declarative datatype tier needs
/// beyond the stage-A emptiness and determinization operations: an exact-string acceptor, the union
/// of several automata, a nondeterministic view of a determinized automaton (so a complement flows
/// back into a product), and the intersection product built as an automaton (so a multi-factor
/// conjunction can be folded, its language counted, or its emptiness read off). Every state is minted
/// through <see cref="NondeterministicAutomatonBuilder"/>; the product is bounded by the shared
/// pair-state ceiling and reports a value-based breach.
/// </summary>
internal static class AutomatonComposition
{
    /// <summary>An automaton accepting exactly the one string of the given code points.</summary>
    /// <param name="codePoints">The code points of the string.</param>
    /// <returns>The exact-string automaton.</returns>
    public static NondeterministicAutomaton ExactString(ReadOnlySpan<int> codePoints)
    {
        NondeterministicAutomatonBuilder builder = new(codePoints.Length + 2);
        int state = builder.AddState();
        builder.MarkInitial(state);
        foreach(int codePoint in codePoints)
        {
            int next = builder.AddState();
            builder.AddTransition(state, new CodePointRange(codePoint, codePoint), next);
            state = next;
        }

        builder.MarkAccepting(state);

        return builder.Build();
    }

    /// <summary>The union of several automata — accepts a string when any factor does — via a fresh initial state epsilon-linked to each factor's initial states.</summary>
    /// <param name="automata">The factor automata.</param>
    /// <returns>The union automaton.</returns>
    public static NondeterministicAutomaton Union(IReadOnlyList<NondeterministicAutomaton> automata)
    {
        int total = 1;
        foreach(NondeterministicAutomaton automaton in automata)
        {
            total += automaton.StateCount;
        }

        NondeterministicAutomatonBuilder builder = new(total + 1);
        int start = builder.AddState();
        builder.MarkInitial(start);
        int offset = 1;
        foreach(NondeterministicAutomaton automaton in automata)
        {
            for(int s = 0; s < automaton.StateCount; s++)
            {
                builder.AddState();
            }

            CopyInto(builder, automaton, offset);
            foreach(int initial in automaton.InitialStates)
            {
                builder.AddEpsilon(start, offset + initial);
            }

            offset += automaton.StateCount;
        }

        return builder.Build();
    }

    /// <summary>A nondeterministic view of a deterministic automaton, so a completed complement DFA can re-enter a product.</summary>
    /// <param name="dfa">The deterministic automaton.</param>
    /// <returns>The equivalent nondeterministic automaton.</returns>
    public static NondeterministicAutomaton FromDeterministic(DeterministicAutomaton dfa)
    {
        NondeterministicAutomatonBuilder builder = new(dfa.StateCount + 1);
        for(int s = 0; s < dfa.StateCount; s++)
        {
            builder.AddState();
        }

        for(int s = 0; s < dfa.StateCount; s++)
        {
            (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = dfa.TransitionsOf(s);
            for(int i = 0; i < labels.Length; i++)
            {
                builder.AddTransition(s, labels[i], targets[i]);
            }

            if(dfa.IsAccepting(s))
            {
                builder.MarkAccepting(s);
            }
        }

        builder.MarkInitial(dfa.InitialState);

        return builder.Build();
    }

    /// <summary>
    /// The intersection product of two automata, materialized as an automaton whose states are the
    /// reachable pairs of factor states. Epsilon moves advance one factor; a range-labelled move
    /// advances both when their labels overlap, on the overlap range; a pair is accepting when both
    /// factors are. The reachable pair count is bounded by <paramref name="maxProductStates"/>.
    /// </summary>
    /// <param name="first">The first factor.</param>
    /// <param name="second">The second factor.</param>
    /// <param name="maxProductStates">The most pair-states the product may mint.</param>
    /// <returns>The product result.</returns>
    public static ProductResult Product(NondeterministicAutomaton first, NondeterministicAutomaton second, int maxProductStates)
    {
        NondeterministicAutomatonBuilder builder = new(maxProductStates + 8);
        Dictionary<(int, int), int> ids = [];
        Queue<(int, int)> worklist = new();

        foreach(int a in first.InitialStates)
        {
            foreach(int b in second.InitialStates)
            {
                int id = Intern(builder, ids, worklist, (a, b));
                builder.MarkInitial(id);
            }
        }

        bool budgetExceeded = ids.Count > maxProductStates;
        while(worklist.Count > 0 && !budgetExceeded)
        {
            (int a, int b) = worklist.Dequeue();
            int fromId = ids[(a, b)];
            if(first.IsAccepting(a) && second.IsAccepting(b))
            {
                builder.MarkAccepting(fromId);
            }

            foreach(int nextA in first.EpsilonTargetsOf(a))
            {
                builder.AddEpsilon(fromId, Intern(builder, ids, worklist, (nextA, b)));
            }

            foreach(int nextB in second.EpsilonTargetsOf(b))
            {
                builder.AddEpsilon(fromId, Intern(builder, ids, worklist, (a, nextB)));
            }

            (ReadOnlySpan<CodePointRange> aLabels, ReadOnlySpan<int> aTargets) = first.SymbolTransitions(a);
            (ReadOnlySpan<CodePointRange> bLabels, ReadOnlySpan<int> bTargets) = second.SymbolTransitions(b);
            for(int i = 0; i < aLabels.Length; i++)
            {
                for(int j = 0; j < bLabels.Length; j++)
                {
                    int low = Math.Max(aLabels[i].Low, bLabels[j].Low);
                    int high = Math.Min(aLabels[i].High, bLabels[j].High);
                    if(low <= high)
                    {
                        builder.AddTransition(fromId, new CodePointRange(low, high), Intern(builder, ids, worklist, (aTargets[i], bTargets[j])));
                    }
                }
            }

            budgetExceeded = ids.Count > maxProductStates;
        }

        return budgetExceeded ? ProductResult.BudgetExceeded() : ProductResult.Built(builder.Build());
    }

    /// <summary>Copies a factor automaton's transitions and accepting flags into a builder at a state-id offset.</summary>
    /// <param name="builder">The target builder, whose states for this factor are already minted.</param>
    /// <param name="automaton">The factor automaton.</param>
    /// <param name="offset">The state-id offset the factor's states are mapped to.</param>
    private static void CopyInto(NondeterministicAutomatonBuilder builder, NondeterministicAutomaton automaton, int offset)
    {
        for(int s = 0; s < automaton.StateCount; s++)
        {
            foreach(int target in automaton.EpsilonTargetsOf(s))
            {
                builder.AddEpsilon(offset + s, offset + target);
            }

            (ReadOnlySpan<CodePointRange> labels, ReadOnlySpan<int> targets) = automaton.SymbolTransitions(s);
            for(int i = 0; i < labels.Length; i++)
            {
                builder.AddTransition(offset + s, labels[i], offset + targets[i]);
            }

            if(automaton.IsAccepting(s))
            {
                builder.MarkAccepting(offset + s);
            }
        }
    }

    /// <summary>Returns the product state id for a factor-state pair, minting one when the pair is new.</summary>
    /// <param name="builder">The product builder.</param>
    /// <param name="ids">The pair-to-id memo.</param>
    /// <param name="worklist">The unprocessed-pair worklist.</param>
    /// <param name="pair">The factor-state pair.</param>
    /// <returns>The product state id.</returns>
    private static int Intern(NondeterministicAutomatonBuilder builder, Dictionary<(int, int), int> ids, Queue<(int, int)> worklist, (int, int) pair)
    {
        if(ids.TryGetValue(pair, out int existing))
        {
            return existing;
        }

        int id = builder.AddState();
        ids[pair] = id;
        worklist.Enqueue(pair);

        return id;
    }
}
