namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// The state-count ceilings the automaton module never exceeds, carried as one value
/// rather than scattered constants. A ceiling is a soundness safeguard, not a
/// correctness bound: every breach yields a value-based budget-exceeded outcome the
/// caller consumes as an abstention, never a silent wrong answer.
/// </summary>
/// <param name="MaxNfaStates">The most states a Thompson construction may mint before it abstains.</param>
/// <param name="MaxProductStates">The most pair-states a lazy intersection product may visit before it abstains.</param>
/// <param name="MaxDfaStates">The most subsets a determinization may mint before it abstains.</param>
public readonly record struct AutomatonBudgets(int MaxNfaStates, int MaxProductStates, int MaxDfaStates)
{
    /// <summary>
    /// The default ceilings, calibrated around the existing 4096-state automaton-state
    /// precedent: 4096 for both the NFA and the DFA state spaces, and double that for
    /// the pairwise product whose reachable state space is the cross of two factors.
    /// </summary>
    public static AutomatonBudgets Default { get; } = new(4096, 8192, 4096);

    /// <summary>The ceiling for a given budget axis.</summary>
    /// <param name="kind">The budget axis.</param>
    /// <returns>The configured ceiling.</returns>
    public int Limit(AutomatonBudgetKind kind)
    {
        return kind switch
        {
            AutomatonBudgetKind.MaxNfaStates => MaxNfaStates,
            AutomatonBudgetKind.MaxProductStates => MaxProductStates,
            AutomatonBudgetKind.MaxDfaStates => MaxDfaStates,
            _ => 0
        };
    }
}

/// <summary>The state-space axis an <see cref="AutomatonBudgets"/> ceiling governs.</summary>
public enum AutomatonBudgetKind
{
    /// <summary>The Thompson NFA state count.</summary>
    MaxNfaStates,

    /// <summary>The lazy intersection product pair-state count.</summary>
    MaxProductStates,

    /// <summary>The determinization subset count.</summary>
    MaxDfaStates,
}
