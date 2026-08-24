namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// Builds counter automata over the XML Char alphabet that accept strings by length, the
/// automaton form of the XSD <c>length</c>, <c>minLength</c>, and <c>maxLength</c> facets.
/// Each is a chain of at most <c>k + 1</c> states advancing one state per any-character step;
/// intersecting a pattern automaton with a length automaton decides a pattern-and-length
/// conjunction by emptiness.
/// </summary>
internal static class LengthAutomaton
{
    /// <summary>An automaton accepting exactly the strings of length at most <paramref name="maximum"/>.</summary>
    /// <param name="maximum">The inclusive maximum length.</param>
    /// <returns>The length automaton.</returns>
    public static NondeterministicAutomaton AtMost(int maximum)
    {
        NondeterministicAutomatonBuilder builder = new(maximum + 2);
        int[] states = MintChain(builder, maximum);
        for(int i = 0; i <= maximum; i++)
        {
            builder.MarkAccepting(states[i]);
        }

        builder.MarkInitial(states[0]);

        return builder.Build();
    }

    /// <summary>An automaton accepting exactly the strings of length at least <paramref name="minimum"/>.</summary>
    /// <param name="minimum">The inclusive minimum length.</param>
    /// <returns>The length automaton.</returns>
    public static NondeterministicAutomaton AtLeast(int minimum)
    {
        NondeterministicAutomatonBuilder builder = new(minimum + 2);
        int[] states = MintChain(builder, minimum);
        foreach(CodePointRange range in XmlCharAlphabet.Universe.Ranges)
        {
            builder.AddTransition(states[minimum], range, states[minimum]);
        }

        builder.MarkAccepting(states[minimum]);
        builder.MarkInitial(states[0]);

        return builder.Build();
    }

    /// <summary>An automaton accepting exactly the strings of length <paramref name="length"/>.</summary>
    /// <param name="length">The exact length.</param>
    /// <returns>The length automaton.</returns>
    public static NondeterministicAutomaton Exactly(int length)
    {
        NondeterministicAutomatonBuilder builder = new(length + 2);
        int[] states = MintChain(builder, length);
        builder.MarkAccepting(states[length]);
        builder.MarkInitial(states[0]);

        return builder.Build();
    }

    /// <summary>Mints a chain of <paramref name="steps"/> + 1 states, each linked to the next by an any-character transition.</summary>
    /// <param name="builder">The automaton builder.</param>
    /// <param name="steps">The number of any-character steps.</param>
    /// <returns>The chain states, indexed by consumed length.</returns>
    private static int[] MintChain(NondeterministicAutomatonBuilder builder, int steps)
    {
        int[] states = new int[steps + 1];
        for(int i = 0; i <= steps; i++)
        {
            states[i] = builder.AddState();
        }

        for(int i = 0; i < steps; i++)
        {
            foreach(CodePointRange range in XmlCharAlphabet.Universe.Ranges)
            {
                builder.AddTransition(states[i], range, states[i + 1]);
            }
        }

        return states;
    }
}
