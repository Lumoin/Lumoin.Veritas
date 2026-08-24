using System;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// An inclusive range of Unicode code points, the label a range-labelled automaton
/// transition carries. Both endpoints are scalar code-point values in the closed
/// interval <c>[0, 0x10FFFF]</c>; a range whose <see cref="Low"/> equals its
/// <see cref="High"/> denotes a single code point.
/// </summary>
/// <param name="Low">The lowest included code point.</param>
/// <param name="High">The highest included code point.</param>
internal readonly record struct CodePointRange(int Low, int High) : IComparable<CodePointRange>
{
    /// <summary>The number of code points the range spans, widened so a full-plane range never overflows.</summary>
    public long Width => (long)High - Low + 1;

    /// <summary>Whether a code point lies within this range.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the code point is included.</returns>
    public bool Contains(int codePoint)
    {
        return codePoint >= Low && codePoint <= High;
    }

    /// <summary>Orders ranges by lower endpoint, then by upper endpoint.</summary>
    /// <param name="other">The range to compare against.</param>
    /// <returns>A negative, zero, or positive value for less-than, equal, or greater-than order.</returns>
    public int CompareTo(CodePointRange other)
    {
        int byLow = Low.CompareTo(other.Low);

        return byLow != 0 ? byLow : High.CompareTo(other.High);
    }
}
