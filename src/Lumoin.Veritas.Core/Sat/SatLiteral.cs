using System.Diagnostics;

namespace Lumoin.Veritas.Core.Sat;

/// <summary>
/// One literal of a CNF clause: a variable index and its polarity.
/// </summary>
/// <param name="Variable">The zero-based variable index.</param>
/// <param name="IsPositive">Whether the literal asserts the variable (<see langword="true"/>) or its negation.</param>
[DebuggerDisplay("{IsPositive ? \"x\" : \"~x\"}{Variable}")]
public readonly record struct SatLiteral(int Variable, bool IsPositive)
{
    /// <summary>The literal with the opposite polarity.</summary>
    /// <returns>The negated literal.</returns>
    public SatLiteral Negated()
    {
        return new SatLiteral(Variable, !IsPositive);
    }
}
