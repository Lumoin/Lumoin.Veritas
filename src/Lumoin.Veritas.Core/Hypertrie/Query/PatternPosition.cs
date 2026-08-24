using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Hypertrie.Query;

/// <summary>
/// One position in a <see cref="TriplePattern"/>. A position is
/// either a bound <see cref="TermId"/> (a constant the pattern
/// matches against) or a <see cref="Variable"/> (a value the query
/// engine will bind during evaluation).
/// </summary>
/// <remarks>
/// <para>
/// A value-typed discriminated union: <see cref="Kind"/> selects
/// which of <see cref="BoundTerm"/> and <see cref="Variable"/> is
/// meaningful. Construction is through the static factory methods
/// <see cref="Bound"/> and <see cref="OfVariable"/>; direct
/// instantiation via <c>new</c> bypasses the factory and is
/// discouraged.
/// </para>
/// <para>
/// Equality is value-based across all fields, including the
/// inactive one: positions equal on <see cref="Kind"/> and active
/// payload, but with mismatched inactive payload, would compare
/// unequal. The factories zero the inactive payload so this is not
/// a concern in practice.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly record struct PatternPosition
{
    /// <summary>The active discriminator.</summary>
    public PatternPositionKind Kind { get; init; }

    /// <summary>For <see cref="PatternPositionKind.Bound"/>: the bound term identity.</summary>
    public TermId BoundTerm { get; init; }

    /// <summary>For <see cref="PatternPositionKind.Variable"/>: the variable identity.</summary>
    public Variable Variable { get; init; }

    private string DebuggerDisplay => Kind switch
    {
        PatternPositionKind.Bound => $"Bound({BoundTerm.Encoded})",
        PatternPositionKind.Variable => $"Var({Variable.Id})",
        _ => "?",
    };

    /// <summary>
    /// Returns <c>true</c> when the position is a bound term;
    /// otherwise <c>false</c>.
    /// </summary>
    public bool IsBound => Kind == PatternPositionKind.Bound;

    /// <summary>
    /// Returns <c>true</c> when the position is a variable;
    /// otherwise <c>false</c>.
    /// </summary>
    public bool IsVariable => Kind == PatternPositionKind.Variable;

    /// <summary>
    /// Constructs a bound position carrying <paramref name="term"/>.
    /// </summary>
    public static PatternPosition Bound(TermId term) => new()
    {
        Kind = PatternPositionKind.Bound,
        BoundTerm = term,
        Variable = default,
    };

    /// <summary>
    /// Constructs a variable position carrying
    /// <paramref name="variable"/>.
    /// </summary>
    public static PatternPosition OfVariable(Variable variable) => new()
    {
        Kind = PatternPositionKind.Variable,
        BoundTerm = default,
        Variable = variable,
    };

    /// <summary>
    /// Returns the bound term, or throws if the position is a
    /// variable. Callers that may receive either kind should
    /// dispatch on <see cref="Kind"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The position is a variable, not a bound term.</exception>
    public TermId AsBound()
    {
        if(Kind != PatternPositionKind.Bound)
        {
            throw new InvalidOperationException("PatternPosition is a variable, not a bound term.");
        }

        return BoundTerm;
    }

    /// <summary>
    /// Returns the variable, or throws if the position is a bound
    /// term. Callers that may receive either kind should dispatch
    /// on <see cref="Kind"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">The position is a bound term, not a variable.</exception>
    public Variable AsVariable()
    {
        if(Kind != PatternPositionKind.Variable)
        {
            throw new InvalidOperationException("PatternPosition is a bound term, not a variable.");
        }

        return Variable;
    }
}
