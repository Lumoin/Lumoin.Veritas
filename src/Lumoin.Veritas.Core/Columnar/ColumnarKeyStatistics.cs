using System;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The per-key group statistics one pattern exposes on a columnar view, read off a materialised order's
/// offset columns: the mean matches per key value, the largest, and the degree-weighted mean. All three
/// are exact group statistics of the base columns — two offset differences for the mean, one pass over the
/// key group's offsets for the other two — and all three decline the same shapes, so a caller that cannot
/// read one cannot read another. The order-wide sibling is <see cref="ColumnarStatistics"/>, which
/// summarises every level of one permutation rather than one key group of one pattern.
/// </summary>
/// <remarks>
/// Statistics read the base columns; an uncompacted delta skews them slightly, which only moves decisions
/// near a threshold.
/// </remarks>
internal static class ColumnarKeyStatistics
{
    /// <summary>
    /// Estimates the pattern's mean matches per distinct value of <paramref name="key"/>: the key group's
    /// triples over its distinct key values, two offset-column differences rather than a scan. Fails when
    /// the pattern's shape or the view's order set does not expose the statistic.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="key">The key variable.</param>
    /// <param name="fanOut">The estimated mean matches per key value; zero when the bound prefix matches nothing.</param>
    /// <returns><see langword="true"/> when the statistic was read.</returns>
    internal static bool TryEstimateKeyFanOut(ColumnarTripleIndex index, TriplePattern pattern, Variable key, out double fanOut)
    {
        fanOut = 0;

        switch(LocateKeyGroup(index, pattern, key, out KeyGroupSpan span))
        {
            case KeyGroupKind.AtMostOnePerKey:
            {
                fanOut = 1;

                return true;
            }

            case KeyGroupKind.Located:
            {
                if(span.KeysEnd == span.KeysStart)
                {
                    return true;
                }

                BlockPackedColumnReader level1Offsets = new(span.Order.OffsetsColumnAt(1));
                long triples = level1Offsets.ValueAt(span.KeysEnd) - (long)level1Offsets.ValueAt(span.KeysStart);
                fanOut = triples / (double)(span.KeysEnd - span.KeysStart);

                return true;
            }

            default:
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Estimates the largest matches any one value of <paramref name="key"/> carries — the key group's
    /// heaviest child group. Reads the same shapes <see cref="TryEstimateKeyFanOut"/> reads and declines the
    /// same ones.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="key">The key variable.</param>
    /// <param name="maximumFanOut">The largest matches one key value carries; zero when the bound prefix matches nothing.</param>
    /// <returns><see langword="true"/> when the statistic was read.</returns>
    internal static bool TryEstimateMaximumKeyFanOut(ColumnarTripleIndex index, TriplePattern pattern, Variable key, out int maximumFanOut)
    {
        return TryReadKeyGroupFanOut(index, pattern, key, out maximumFanOut, out _);
    }

    /// <summary>
    /// Estimates the degree-weighted mean matches per value of <paramref name="key"/> — the sum of squared
    /// per-key match counts over their sum, so a key group's heavy values weigh in proportion to the work
    /// they carry rather than by their share of the distinct keys. Distinct from
    /// <see cref="TryEstimateKeyFanOut"/>'s arithmetic mean, which weighs every key value alike. Reads the
    /// same shapes both other statistics read and declines the same ones.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="key">The key variable.</param>
    /// <param name="weightedMeanFanOut">The degree-weighted mean matches per key value; zero when the bound prefix matches nothing.</param>
    /// <returns><see langword="true"/> when the statistic was read.</returns>
    internal static bool TryEstimateWeightedMeanKeyFanOut(ColumnarTripleIndex index, TriplePattern pattern, Variable key, out double weightedMeanFanOut)
    {
        return TryReadKeyGroupFanOut(index, pattern, key, out _, out weightedMeanFanOut);
    }

    /// <summary>
    /// Reads both per-key group statistics of one located key group in one pass over its level-1 offsets:
    /// the heaviest child group and the degree-weighted mean. One locator and one loop serve both, so the
    /// two can never be computed from different group sets or through different permutation searches; the
    /// cost is one packed read per distinct key value in the group, never more than the pattern's own match
    /// count, and the widening adds two accumulators and no traversal.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="key">The key variable.</param>
    /// <param name="maximumFanOut">The largest matches one key value carries; zero when the bound prefix matches nothing.</param>
    /// <param name="weightedMeanFanOut">The degree-weighted mean matches per key value; zero when the bound prefix matches nothing.</param>
    /// <returns><see langword="true"/> when the statistics were read.</returns>
    internal static bool TryReadKeyGroupFanOut(ColumnarTripleIndex index, TriplePattern pattern, Variable key, out int maximumFanOut, out double weightedMeanFanOut)
    {
        maximumFanOut = 0;
        weightedMeanFanOut = 0;

        switch(LocateKeyGroup(index, pattern, key, out KeyGroupSpan span))
        {
            case KeyGroupKind.AtMostOnePerKey:
            {
                maximumFanOut = 1;
                weightedMeanFanOut = 1;

                return true;
            }

            case KeyGroupKind.Located:
            {
                if(span.KeysEnd == span.KeysStart)
                {
                    return true;
                }

                BlockPackedColumnReader level1Offsets = new(span.Order.OffsetsColumnAt(1));
                long degrees = 0;
                long squaredDegrees = 0;
                uint previous = level1Offsets.ValueAt(span.KeysStart);
                for(int keyValue = span.KeysStart; keyValue < span.KeysEnd; keyValue++)
                {
                    uint next = level1Offsets.ValueAt(keyValue + 1);
                    int degree = (int)(next - previous);
                    maximumFanOut = Math.Max(maximumFanOut, degree);
                    degrees += degree;
                    squaredDegrees += (long)degree * degree;
                    previous = next;
                }

                if(degrees > 0)
                {
                    weightedMeanFanOut = squaredDegrees / (double)degrees;
                }

                return true;
            }

            default:
            {
                return false;
            }
        }
    }

    /// <summary>How a pattern's key group reads on this view.</summary>
    private enum KeyGroupKind
    {
        /// <summary>The pattern's shape or the view's order set does not expose the statistic.</summary>
        Unreadable = 0,

        /// <summary>The key is the pattern's only variable, so each key value carries at most one match.</summary>
        AtMostOnePerKey = 1,

        /// <summary>The group is located; an empty span is a bound prefix that matches nothing.</summary>
        Located = 2
    }

    /// <summary>One located key group: the order it lives in and the half-open span of its distinct key values at level one.</summary>
    /// <param name="Order">The materialised order the group was located in.</param>
    /// <param name="KeysStart">The first level-1 index of the group's distinct key values.</param>
    /// <param name="KeysEnd">The exclusive end of that level-1 range.</param>
    private readonly record struct KeyGroupSpan(ColumnarOrder Order, int KeysStart, int KeysEnd);

    /// <summary>
    /// Locates the pattern's key group: the pattern must bind the key, carry exactly one bound position and
    /// two distinct variables, and the view must materialise a permutation whose first two levels are the
    /// bound position then the key. The search is confined to this view's own level-0 slice, so a graph view
    /// reads its own graph's groups.
    /// </summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="key">The key variable.</param>
    /// <param name="span">The located group on <see cref="KeyGroupKind.Located"/>; otherwise the default.</param>
    /// <returns>How the group reads.</returns>
    private static KeyGroupKind LocateKeyGroup(ColumnarTripleIndex index, TriplePattern pattern, Variable key, out KeyGroupSpan span)
    {
        span = default;

        int boundPosition = -1;
        int keyPosition = -1;
        int boundCount = 0;
        int variableCount = 0;
        for(int position = 0; position < 3; position++)
        {
            if(pattern.At(position).IsBound)
            {
                boundPosition = position;
                boundCount++;
            }
            else
            {
                variableCount++;
                if(pattern.At(position).Variable == key)
                {
                    keyPosition = position;
                }
            }
        }

        if(keyPosition < 0)
        {
            return KeyGroupKind.Unreadable;
        }

        //A pattern binding the key as its only variable contributes each key value at most once — a pure
        //semijoin arm, fan-out one.
        if(variableCount == 1)
        {
            return KeyGroupKind.AtMostOnePerKey;
        }

        if(boundCount != 1 || variableCount != 2)
        {
            return KeyGroupKind.Unreadable;
        }

        //The order [bound, key, other] reads the statistic directly: the bound value's level-0 group spans
        //its distinct key values at level 1, and the level-1 offsets accumulate the group's triples.
        for(int permutationIndex = 0; permutationIndex < 6; permutationIndex++)
        {
            if(!index.IsPermutationAvailable(permutationIndex))
            {
                continue;
            }

            ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(permutationIndex);
            if(permutation[0] != boundPosition || permutation[1] != keyPosition)
            {
                continue;
            }

            ColumnarOrder order = index.OrderAt(permutationIndex);
            (int level0Start, int level0End) = index.Level0BoundsAt(permutationIndex);
            BlockPackedColumnReader level0Values = new(order.ValuesColumnAt(0));
            uint boundValue = pattern.At(boundPosition).BoundTerm.Encoded;
            int group = level0Values.LowerBound(level0Start, level0End, boundValue);
            if(group == level0End || level0Values.ValueAt(group) != boundValue)
            {
                span = new KeyGroupSpan(order, 0, 0);

                return KeyGroupKind.Located;
            }

            BlockPackedColumnReader level0Offsets = new(order.OffsetsColumnAt(0));
            span = new KeyGroupSpan(order, (int)level0Offsets.ValueAt(group), (int)level0Offsets.ValueAt(group + 1));

            return KeyGroupKind.Located;
        }

        return KeyGroupKind.Unreadable;
    }
}
