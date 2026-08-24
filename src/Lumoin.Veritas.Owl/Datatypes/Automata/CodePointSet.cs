using System;
using System.Collections.Generic;

namespace Lumoin.Veritas.Owl.Datatypes.Automata;

/// <summary>
/// An immutable set of Unicode code points held as a sorted list of disjoint,
/// non-adjacent <see cref="CodePointRange"/> intervals. Every factory and every
/// set-algebra operation returns a normalized set (ranges sorted by lower endpoint,
/// with overlapping or touching ranges merged) and runs without recursion over the
/// sorted arrays.
/// </summary>
internal sealed class CodePointSet
{
    /// <summary>The empty set.</summary>
    public static CodePointSet Empty { get; } = new(Array.Empty<CodePointRange>());

    /// <summary>The normalized backing ranges.</summary>
    private CodePointRange[] RangesArray { get; }

    /// <summary>The normalized, sorted, disjoint, non-adjacent ranges.</summary>
    public ReadOnlySpan<CodePointRange> Ranges => RangesArray;

    /// <summary>Whether the set contains no code point.</summary>
    public bool IsEmpty => RangesArray.Length == 0;

    /// <summary>Wraps an already-normalized range array without copying.</summary>
    /// <param name="normalized">Ranges that are already sorted, disjoint, and non-adjacent.</param>
    private CodePointSet(CodePointRange[] normalized)
    {
        RangesArray = normalized;
    }

    /// <summary>A set of a single code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The singleton set.</returns>
    public static CodePointSet Single(int codePoint)
    {
        return new CodePointSet([new CodePointRange(codePoint, codePoint)]);
    }

    /// <summary>A set of one inclusive range.</summary>
    /// <param name="low">The lowest included code point.</param>
    /// <param name="high">The highest included code point.</param>
    /// <returns>The range set, or <see cref="Empty"/> when the range is degenerate.</returns>
    public static CodePointSet Range(int low, int high)
    {
        return low > high ? Empty : new CodePointSet([new CodePointRange(low, high)]);
    }

    /// <summary>A normalized set built from arbitrary ranges.</summary>
    /// <param name="ranges">The ranges, in any order and possibly overlapping.</param>
    /// <returns>The normalized set.</returns>
    public static CodePointSet Of(ReadOnlySpan<CodePointRange> ranges)
    {
        return new CodePointSet(Normalize(ranges));
    }

    /// <summary>A normalized set built from a flat list of inclusive <c>[lo, hi]</c> code-point pairs.</summary>
    /// <param name="pairs">The pair list; length must be even.</param>
    /// <returns>The normalized set.</returns>
    public static CodePointSet FromPairs(ReadOnlySpan<int> pairs)
    {
        int count = pairs.Length / 2;
        CodePointRange[] ranges = new CodePointRange[count];
        for(int i = 0; i < count; i++)
        {
            ranges[i] = new CodePointRange(pairs[2 * i], pairs[(2 * i) + 1]);
        }

        return new CodePointSet(Normalize(ranges));
    }

    /// <summary>Whether a code point is a member of this set.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns><see langword="true"/> when the code point is included.</returns>
    public bool Contains(int codePoint)
    {
        int low = 0;
        int high = RangesArray.Length - 1;
        while(low <= high)
        {
            int mid = low + ((high - low) / 2);
            CodePointRange range = RangesArray[mid];
            if(codePoint < range.Low)
            {
                high = mid - 1;
            }
            else if(codePoint > range.High)
            {
                low = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The union of two sets.</summary>
    /// <param name="first">The first set.</param>
    /// <param name="second">The second set.</param>
    /// <returns>The union.</returns>
    public static CodePointSet Union(CodePointSet first, CodePointSet second)
    {
        if(first.IsEmpty)
        {
            return second;
        }

        if(second.IsEmpty)
        {
            return first;
        }

        CodePointRange[] combined = new CodePointRange[first.RangesArray.Length + second.RangesArray.Length];
        Array.Copy(first.RangesArray, combined, first.RangesArray.Length);
        Array.Copy(second.RangesArray, 0, combined, first.RangesArray.Length, second.RangesArray.Length);

        return new CodePointSet(Normalize(combined));
    }

    /// <summary>The intersection of two sets.</summary>
    /// <param name="first">The first set.</param>
    /// <param name="second">The second set.</param>
    /// <returns>The intersection.</returns>
    public static CodePointSet Intersect(CodePointSet first, CodePointSet second)
    {
        if(first.IsEmpty || second.IsEmpty)
        {
            return Empty;
        }

        List<CodePointRange> result = [];
        int i = 0;
        int j = 0;
        CodePointRange[] a = first.RangesArray;
        CodePointRange[] b = second.RangesArray;
        while(i < a.Length && j < b.Length)
        {
            int low = Math.Max(a[i].Low, b[j].Low);
            int high = Math.Min(a[i].High, b[j].High);
            if(low <= high)
            {
                result.Add(new CodePointRange(low, high));
            }

            if(a[i].High < b[j].High)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return result.Count == 0 ? Empty : new CodePointSet([.. result]);
    }

    /// <summary>The difference of two sets — the code points of <paramref name="minuend"/> not in <paramref name="subtrahend"/>.</summary>
    /// <param name="minuend">The set to subtract from.</param>
    /// <param name="subtrahend">The set to remove.</param>
    /// <returns>The difference.</returns>
    public static CodePointSet Subtract(CodePointSet minuend, CodePointSet subtrahend)
    {
        if(minuend.IsEmpty || subtrahend.IsEmpty)
        {
            return minuend;
        }

        List<CodePointRange> result = [];
        CodePointRange[] b = subtrahend.RangesArray;
        int j = 0;
        foreach(CodePointRange range in minuend.RangesArray)
        {
            int cursor = range.Low;

            //Advance the subtrahend pointer past ranges that end before this minuend range begins.
            while(j < b.Length && b[j].High < range.Low)
            {
                j++;
            }

            int k = j;
            while(k < b.Length && b[k].Low <= range.High)
            {
                if(b[k].Low > cursor)
                {
                    result.Add(new CodePointRange(cursor, Math.Min(b[k].Low - 1, range.High)));
                }

                if(b[k].High >= cursor)
                {
                    cursor = b[k].High + 1;
                }

                if(cursor > range.High)
                {
                    break;
                }

                k++;
            }

            if(cursor <= range.High)
            {
                result.Add(new CodePointRange(cursor, range.High));
            }
        }

        return result.Count == 0 ? Empty : new CodePointSet([.. result]);
    }

    /// <summary>Sorts, merges, and de-duplicates raw ranges into the normalized form the invariant requires.</summary>
    /// <param name="ranges">The raw ranges.</param>
    /// <returns>The normalized range array.</returns>
    private static CodePointRange[] Normalize(ReadOnlySpan<CodePointRange> ranges)
    {
        if(ranges.Length == 0)
        {
            return Array.Empty<CodePointRange>();
        }

        List<CodePointRange> valid = [];
        foreach(CodePointRange range in ranges)
        {
            if(range.Low <= range.High)
            {
                valid.Add(range);
            }
        }

        if(valid.Count == 0)
        {
            return Array.Empty<CodePointRange>();
        }

        CodePointRange[] sorted = [.. valid];
        Array.Sort(sorted);

        List<CodePointRange> merged = [];
        int curLow = sorted[0].Low;
        int curHigh = sorted[0].High;
        for(int i = 1; i < sorted.Length; i++)
        {
            CodePointRange next = sorted[i];
            if(next.Low <= (long)curHigh + 1)
            {
                curHigh = Math.Max(curHigh, next.High);
            }
            else
            {
                merged.Add(new CodePointRange(curLow, curHigh));
                curLow = next.Low;
                curHigh = next.High;
            }
        }

        merged.Add(new CodePointRange(curLow, curHigh));

        return [.. merged];
    }
}
