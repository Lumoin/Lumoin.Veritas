using System;
using System.Collections.Generic;
namespace Lumoin.Veritas.Geo.Dggs.Lattice;

/// <summary>
/// The permutation tables that rearrange quaternary digits when a Hilbert curve level's
/// sub-triangles are shifted, so that a child cell always overlaps its parent cell, plus the shift
/// operation itself.
/// </summary>
internal static class DigitShifter
{
    /// <summary>The rearrangement pattern for non-flipped traversal.</summary>
    public static readonly int[] Pattern = [0, 1, 3, 4, 5, 6, 7, 2];

    /// <summary>The rearrangement pattern used when the traversal's I/J axes are flipped.</summary>
    public static readonly int[] PatternFlipped = [0, 1, 2, 7, 3, 4, 5, 6];

    /// <summary>
    /// The inverse permutation of <see cref="Pattern"/>, used when digits are consumed
    /// least-significant-first rather than most-significant-first.
    /// </summary>
    public static readonly int[] PatternReversed = ReversePattern(Pattern);

    /// <summary>The inverse permutation of <see cref="PatternFlipped"/>.</summary>
    public static readonly int[] PatternFlippedReversed = ReversePattern(PatternFlipped);

    /// <summary>
    /// Inverts a permutation of <c>0..pattern.Length-1</c>: the result at index <c>k</c> is the
    /// index in <paramref name="pattern"/> whose value is <c>k</c>. Computed by the same search the
    /// reversed patterns are derived from, rather than transcribed as literals, so the two stay in
    /// lockstep with <see cref="Pattern"/> and <see cref="PatternFlipped"/> by construction.
    /// </summary>
    public static int[] ReversePattern(int[] pattern)
    {
        int[] reversed = new int[pattern.Length];
        for(int index = 0; index < pattern.Length; index++)
        {
            reversed[index] = Array.IndexOf(pattern, index);
        }

        return reversed;
    }

    /// <summary>
    /// Shifts the quaternary digit at <paramref name="index"/> and its child digit at
    /// <paramref name="index"/> − 1 in place, using <paramref name="pattern"/> to look up the
    /// rearranged pair, so that the child pentagon always overlaps its parent. A no-op when
    /// <paramref name="index"/> is 0 (there is no child level below it) or when the accumulated
    /// flip state does not call for a shift at this level.
    /// </summary>
    public static void ShiftDigits(IList<int> digits, int index, FlipPair flips, bool invertJ, int[] pattern)
    {
        if(index <= 0)
        {
            return;
        }

        // parentDigit is always a valid, already-computed digit at every call site (index is an
        // in-bounds position filled before this call), so no zero-on-missing-value guard is needed.
        int parentDigit = digits[index];
        int childDigit = digits[index - 1];
        int flipSum = (int)flips.FlipX + (int)flips.FlipY;

        bool needsShift;
        bool isFirst;

        // The flipSum value that calls for a shift is flipped depending on the orientation,
        // specifically on the value of invertJ.
        if(invertJ != (flipSum == 0))
        {
            needsShift = parentDigit == 1 || parentDigit == 2; // Second and third pentagons only.
            isFirst = parentDigit == 1; // The second pentagon is first.
        }
        else
        {
            needsShift = parentDigit < 2; // The first two pentagons only.
            isFirst = parentDigit == 0; // The first pentagon is first.
        }

        if(!needsShift)
        {
            return;
        }

        // source and destination are always non-negative (0..7), so integer division below is
        // exactly Math.floor of the corresponding real division — no separate floor call is needed.
        int source = isFirst ? childDigit : childDigit + 4;
        int destination = pattern[source];
        digits[index - 1] = destination % 4;
        digits[index] = (parentDigit + 4 + (destination / 4) - (source / 4)) % 4;
    }

    /// <summary>
    /// Span-based overload of <see cref="ShiftDigits(IList{int}, int, FlipPair, bool, int[])"/> for
    /// stack-allocated digit buffers. Kept as a literal duplicate of that method's body — rather than a
    /// shared helper both call into — because <see cref="Span{T}"/> cannot implement
    /// <see cref="IList{T}"/>, so one of the two must exist as its own method regardless; duplicating
    /// the (short, already-fixture-verified) body avoids introducing a new indirection on the hot path
    /// this overload serves.
    /// </summary>
    public static void ShiftDigits(Span<int> digits, int index, FlipPair flips, bool invertJ, int[] pattern)
    {
        if(index <= 0)
        {
            return;
        }

        int parentDigit = digits[index];
        int childDigit = digits[index - 1];
        int flipSum = (int)flips.FlipX + (int)flips.FlipY;

        bool needsShift;
        bool isFirst;

        if(invertJ != (flipSum == 0))
        {
            needsShift = parentDigit == 1 || parentDigit == 2;
            isFirst = parentDigit == 1;
        }
        else
        {
            needsShift = parentDigit < 2;
            isFirst = parentDigit == 0;
        }

        if(!needsShift)
        {
            return;
        }

        int source = isFirst ? childDigit : childDigit + 4;
        int destination = pattern[source];
        digits[index - 1] = destination % 4;
        digits[index] = (parentDigit + 4 + (destination / 4) - (source / 4)) % 4;
    }
}
