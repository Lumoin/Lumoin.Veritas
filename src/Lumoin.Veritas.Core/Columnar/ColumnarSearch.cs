using System;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The search primitive the columnar index and its cursors share:
/// lower-bound binary search over an ascending slice of a value
/// column.
/// </summary>
internal static class ColumnarSearch
{
    /// <summary>
    /// Returns the smallest index in <c>[lo, hi)</c> whose value is
    /// greater than or equal to <paramref name="target"/>, or
    /// <paramref name="hi"/> when no such index exists.
    /// </summary>
    /// <param name="values">The ascending value column.</param>
    /// <param name="lo">The slice's inclusive start.</param>
    /// <param name="hi">The slice's exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    internal static int LowerBound(ReadOnlySpan<uint> values, int lo, int hi, uint target)
    {
        int low = lo;
        int high = hi;

        while(low < high)
        {
            int mid = low + ((high - low) >> 1);

            if(values[mid] < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
