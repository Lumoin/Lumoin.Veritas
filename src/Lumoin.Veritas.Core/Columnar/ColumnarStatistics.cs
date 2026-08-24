using System;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Structural statistics of one materialised order of a
/// <see cref="ColumnarTripleIndex"/>, read directly off its compressed-sparse
/// columns: exact per-level cardinalities (column lengths), the per-level
/// fan-out distribution (offset-column deltas), and the realised packed bits
/// per value and per triple. Computed from the column lengths plus one pass
/// over the offset columns. The companion
/// <see cref="ColumnarStatisticsTraceEvent"/> carries the scalar summary on the
/// diagnostics trace channel.
/// </summary>
/// <param name="Permutation">The order's permutation index.</param>
/// <param name="TripleCount">The triple count, equal to the level-2 value count.</param>
/// <param name="Level0Count">Distinct level-0 values (subjects for the SPO order).</param>
/// <param name="Level1Count">Distinct level-0/level-1 pairs.</param>
/// <param name="Level2Count">The leaf value count, equal to <paramref name="TripleCount"/>.</param>
/// <param name="Level0FanOut">Level-0 → level-1 child-group sizes.</param>
/// <param name="Level1FanOut">Level-1 → level-2 child-group sizes.</param>
/// <param name="Level0BitsPerValue">Packed bits per value of the level-0 value column.</param>
/// <param name="Level1BitsPerValue">Packed bits per value of the level-1 value column.</param>
/// <param name="Level2BitsPerValue">Packed bits per value of the level-2 value column.</param>
/// <param name="BitsPerTriple">The order's total packed footprint in bits per triple.</param>
public sealed record ColumnarStatistics(
    int Permutation,
    long TripleCount,
    int Level0Count,
    int Level1Count,
    int Level2Count,
    ColumnarFanOut Level0FanOut,
    ColumnarFanOut Level1FanOut,
    double Level0BitsPerValue,
    double Level1BitsPerValue,
    double Level2BitsPerValue,
    double BitsPerTriple)
{
    /// <summary>Computes the statistics of one order, reading its columns directly.</summary>
    /// <param name="order">The order to summarise.</param>
    /// <param name="permutation">The order's permutation index.</param>
    /// <returns>The statistics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is <see langword="null"/>.</exception>
    public static ColumnarStatistics From(ColumnarOrder order, int permutation)
    {
        ArgumentNullException.ThrowIfNull(order);

        int level0 = order.ValuesLengthAt(0);
        int level1 = order.ValuesLengthAt(1);
        int level2 = order.ValuesLengthAt(2);

        return new ColumnarStatistics(
            permutation,
            level2,
            level0,
            level1,
            level2,
            FanOutOf(order.OffsetsColumnAt(0), level0),
            FanOutOf(order.OffsetsColumnAt(1), level1),
            BitsPerValue(order.ValuesColumnAt(0)),
            BitsPerValue(order.ValuesColumnAt(1)),
            BitsPerValue(order.ValuesColumnAt(2)),
            order.PackedByteCount * 8.0 / Math.Max(level2, 1));
    }

    /// <summary>The fan-out distribution of an exclusive-end offset column over its parent values — one pass reading consecutive offsets as group sizes.</summary>
    /// <param name="offsets">The offset column; one entry per parent plus a final bound.</param>
    /// <param name="parentCount">The parent value count.</param>
    /// <returns>The min, max, and mean child-group size.</returns>
    private static ColumnarFanOut FanOutOf(BlockPackedColumn offsets, int parentCount)
    {
        if(parentCount == 0)
        {
            return new ColumnarFanOut(0, 0, 0);
        }

        BlockPackedColumnReader reader = new(offsets);
        int min = int.MaxValue;
        int max = 0;
        long sum = 0;
        uint previous = reader.ValueAt(0);
        for(int i = 1; i <= parentCount; i++)
        {
            uint current = reader.ValueAt(i);
            int delta = (int)(current - previous);
            min = Math.Min(min, delta);
            max = Math.Max(max, delta);
            sum += delta;
            previous = current;
        }

        return new ColumnarFanOut(min, max, (double)sum / parentCount);
    }

    /// <summary>The packed bits per value of a column.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The bits per value.</returns>
    private static double BitsPerValue(BlockPackedColumn column)
    {
        return column.PackedByteCount * 8.0 / Math.Max(column.Length, 1);
    }
}
