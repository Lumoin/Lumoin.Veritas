using System;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The expansion compression pass the three-dimensional exact tier rides: rewrites a
/// valid expansion into an equivalent one whose components are packed — successive
/// banked heads sit at least fifty-two bits apart — so the component count is bounded
/// by the represented value's bit span divided by fifty-two, plus two, instead of by
/// the capacity algebra of the operations that produced it. That bound is what makes
/// the degree-six planarity comparison affordable: the raw orientation expansion has a
/// capacity near two hundred components, but under the documented magnitude walls its
/// value spans about a thousand bits, so its compressed form has at most about twenty,
/// and squaring the compressed form costs about a thousand components instead of
/// seventy-four thousand. The pass is exact — the output's components sum to exactly
/// the input's value — and preserves the storage contract: zero-eliminated,
/// nonoverlapping, increasing in magnitude, a lone zero only as the whole value.
/// </summary>
internal static class ExpansionCompression
{
    /// <summary>
    /// Compresses the valid expansion <paramref name="e"/> into
    /// <paramref name="result"/> and returns the component count. The gathering pass
    /// walks top-down, banking the running head each time an absorption leaves a
    /// nonzero residual and continuing from the residual; the emitting pass walks the
    /// banked heads bottom-up, re-absorbing the running value and emitting each
    /// nonzero residual as a finished low component. Every step is an error-free
    /// two-term transform, so the value never changes. <paramref name="result"/>
    /// needs at least as many components as <paramref name="e"/> — the banked heads
    /// occupy its tail before the output occupies its head — and the spans may be the
    /// same memory: the gathering pass writes only above the input component it has
    /// already read, and the emitting pass writes only below the banked head it has
    /// already read.
    /// </summary>
    /// <remarks>
    /// The packing argument: when a head is banked, its residual is a nonzero
    /// multiple of the absorbed component's quantum, so the residual is at least that
    /// quantum while also at most half an ulp of the banked head — which places the
    /// quantum itself at or below half that ulp. Every input component still pending
    /// lies strictly below the absorbed component's quantum because the input is
    /// nonoverlapping, so their total, together with the residual, stays under one
    /// ulp of the banked head: the next banked head sits at least fifty-two bits
    /// down. The emitting pass writes at most one component per banked head, which
    /// gives the count bound the exact tier's scratch capacities are derived from.
    /// The same quantum argument shows each residual dominates every component still
    /// pending, so the fast two-term transform's magnitude precondition holds at
    /// every step of both passes.
    /// </remarks>
    public static int Compress(ReadOnlySpan<double> e, Span<double> result)
    {
        int last = e.Length - 1;
        double accumulator = e[last];
        int bottom = last;
        for(int index = last - 1; index >= 0; index--)
        {
            (double high, double low) = ExpansionArithmetic.FastTwoSum(accumulator, e[index]);

            if(low != 0.0)
            {
                result[bottom] = high;
                bottom--;
                accumulator = low;
            }
            else
            {
                accumulator = high;
            }
        }

        int written = 0;
        for(int index = bottom + 1; index <= last; index++)
        {
            (double high, double low) = ExpansionArithmetic.FastTwoSum(result[index], accumulator);

            if(low != 0.0)
            {
                result[written] = low;
                written++;
            }

            accumulator = high;
        }

        result[written] = accumulator;
        written++;

        return written;
    }
}
