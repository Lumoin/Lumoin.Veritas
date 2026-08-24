using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// One permutation's three-level compressed-sparse column set
/// inside a <see cref="ColumnarTripleIndex"/>: distinct sorted
/// values per level, with offset columns demarcating each parent
/// value's group in the next level. Every column is stored
/// block-packed (<see cref="BlockPackedColumn"/>): zigzag-delta
/// bit-packed lanes with patched exceptions, decoded one block at
/// a time through per-cursor readers.
/// </summary>
/// <remarks>
/// <para>
/// Offsets are exclusive-end: the children of the level-0 value at
/// index <c>i</c> occupy level-1 indices
/// <c>[Level0Offsets[i], Level0Offsets[i + 1])</c>, and likewise
/// from level 1 to level 2. The offset columns carry one more
/// entry than their value columns; the final entry equals the next
/// level's length.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarOrder L0={level0Values.Length} L1={level1Values.Length} L2={level2Values.Length}")]
public sealed partial class ColumnarOrder
{
    private readonly BlockPackedColumn level0Values;

    private readonly BlockPackedColumn level0Offsets;

    private readonly BlockPackedColumn level1Values;

    private readonly BlockPackedColumn level1Offsets;

    private readonly BlockPackedColumn level2Values;

    private ColumnarOrder(
        BlockPackedColumn level0Values,
        BlockPackedColumn level0Offsets,
        BlockPackedColumn level1Values,
        BlockPackedColumn level1Offsets,
        BlockPackedColumn level2Values)
    {
        this.level0Values = level0Values;
        this.level0Offsets = level0Offsets;
        this.level1Values = level1Values;
        this.level1Offsets = level1Offsets;
        this.level2Values = level2Values;
    }

    /// <summary>
    /// Returns the packed value column at the given descent level.
    /// </summary>
    /// <param name="level">The descent level; 0, 1, or 2.</param>
    /// <returns>The level's packed value column.</returns>
    public BlockPackedColumn ValuesColumnAt(int level)
    {
        return level switch
        {
            0 => level0Values,
            1 => level1Values,
            2 => level2Values,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Level must be 0, 1, or 2."),
        };
    }

    /// <summary>
    /// Returns the packed offset column from the given descent
    /// level into the next level. Levels 0 and 1 carry offsets;
    /// level 2 is the deepest.
    /// </summary>
    /// <param name="level">The descent level; 0 or 1.</param>
    /// <returns>The level's packed exclusive-end offset column.</returns>
    public BlockPackedColumn OffsetsColumnAt(int level)
    {
        return level switch
        {
            0 => level0Offsets,
            1 => level1Offsets,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Only levels 0 and 1 carry offsets."),
        };
    }

    /// <summary>The number of values in the value column at the given level.</summary>
    /// <param name="level">The descent level; 0, 1, or 2.</param>
    /// <returns>The value column's length.</returns>
    public int ValuesLengthAt(int level)
    {
        return ValuesColumnAt(level).Length;
    }

    /// <summary>The packed size in bytes across all five columns — the number the soak ladder tracks.</summary>
    public long PackedByteCount =>
        level0Values.PackedByteCount
        + level0Offsets.PackedByteCount
        + level1Values.PackedByteCount
        + level1Offsets.PackedByteCount
        + level2Values.PackedByteCount;

    //Builds one permutation's column set: sorts the working array
    //by its packed 96-bit permutation keys, emits all five columns
    //in a single pass over the sorted run, then block-packs each.
    //valueEncoding defaults to the Elias-Fano policy: a qualifying
    //value column keeps the smaller of the succinct candidate and
    //its frame-of-reference packing, so the default never enlarges
    //a column.
    internal static ColumnarOrder Build(EncodedTriple[] working, byte[] permutation, ColumnarValueColumnEncoding valueEncoding = ColumnarValueColumnEncoding.EliasFanoWhenMonotone, ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        return BuildConcatenated([working], permutation, level0Ranges: null, valueEncoding, backing);
    }

    //Builds one permutation's column set over a SEQUENCE of triple
    //runs — the graph-major concatenation: each run sorts and emits
    //independently (group boundaries never merge across runs, even
    //when adjacent runs share level values), offsets land at
    //absolute positions in the shared columns, and each run's
    //level-0 range is reported so a per-run view needs nothing
    //else. With one run this is the plain single-graph build.
    internal static ColumnarOrder BuildConcatenated(
        IReadOnlyList<EncodedTriple[]> runs,
        byte[] permutation,
        (int Start, int End)[]? level0Ranges,
        ColumnarValueColumnEncoding valueEncoding = ColumnarValueColumnEncoding.EliasFanoWhenMonotone,
        ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        byte position0 = permutation[0];
        byte position1 = permutation[1];
        byte position2 = permutation[2];

        List<uint> values0 = [];
        List<uint> offsets0 = [];
        List<uint> values1 = [];
        List<uint> offsets1 = [];
        List<uint> values2 = [];

        for(int run = 0; run < runs.Count; run++)
        {
            EncodedTriple[] working = runs[run];
            ColumnarSearch.SortByPermutation(working, position0, position1, position2);

            int level0Start = values0.Count;
            bool firstOfRun = true;

            for(int i = 0; i < working.Length; i++)
            {
                uint key0 = KeyAt(working[i], position0);
                uint key1 = KeyAt(working[i], position1);
                uint key2 = KeyAt(working[i], position2);

                bool newGroup0 = firstOfRun || values0[^1] != key0;
                firstOfRun = false;

                if(newGroup0)
                {
                    values0.Add(key0);
                    offsets0.Add((uint)values1.Count);
                }

                if(newGroup0 || values1[^1] != key1)
                {
                    values1.Add(key1);
                    offsets1.Add((uint)values2.Count);
                }

                values2.Add(key2);
            }

            if(level0Ranges is not null)
            {
                level0Ranges[run] = (level0Start, values0.Count);
            }
        }

        offsets0.Add((uint)values1.Count);
        offsets1.Add((uint)values2.Count);

        //Value columns are what worst-case-optimal descents SEEK over: frame
        //of reference keeps every lane independently probeable; under the
        //opt-in policy a candidate succinct packing competes on footprint —
        //whole-column Elias-Fano for the globally-monotone top level, and
        //partitioned Elias-Fano (grouped by the parent offset column) for the
        //within-group levels. Offset columns stay prefixed-delta packed (the
        //reader's block cache absorbs their pointwise decodes).
        bool partitioned = valueEncoding == ColumnarValueColumnEncoding.EliasFanoWhenMonotone;
        int[]? level1Boundaries = partitioned ? ToBoundaries(offsets0) : null;
        int[]? level2Boundaries = partitioned ? ToBoundaries(offsets1) : null;

        return new ColumnarOrder(
            BuildValueColumn(CollectionsMarshal.AsSpan(values0), boundaries: null, valueEncoding, backing),
            BlockPackedColumn.Build(CollectionsMarshal.AsSpan(offsets0), BlockPackedColumnMode.PrefixedDeltas, backing: backing),
            BuildValueColumn(CollectionsMarshal.AsSpan(values1), level1Boundaries, valueEncoding, backing),
            BlockPackedColumn.Build(CollectionsMarshal.AsSpan(offsets1), BlockPackedColumnMode.PrefixedDeltas, backing: backing),
            BuildValueColumn(CollectionsMarshal.AsSpan(values2), level2Boundaries, valueEncoding, backing));
    }

    /// <summary>
    /// Builds a value column under the build policy: always the frame-of-
    /// reference packing, and — under the Elias-Fano policy — also a candidate
    /// succinct packing, keeping the smaller via <see cref="SmallerEncoding"/>.
    /// A top-level column (<paramref name="boundaries"/> <see langword="null"/>)
    /// offers whole-column Elias-Fano when globally non-decreasing; a
    /// within-group column offers partitioned Elias-Fano over the parent
    /// boundaries. Because the smaller is kept, the candidate never enlarges a
    /// column.
    /// </summary>
    /// <param name="values">The value column.</param>
    /// <param name="boundaries">The parent group boundaries for a within-group column, or <see langword="null"/> for the top level.</param>
    /// <param name="valueEncoding">The build policy.</param>
    /// <param name="backing">Where the chosen column's block payload lives; default managed. The Elias-Fano candidates hold no block payload, so it shapes only the frame-of-reference candidate.</param>
    /// <returns>The packed value column.</returns>
    private static BlockPackedColumn BuildValueColumn(ReadOnlySpan<uint> values, int[]? boundaries, ColumnarValueColumnEncoding valueEncoding, ColumnPayloadBacking backing)
    {
        BlockPackedColumn frame = BlockPackedColumn.Build(values, BlockPackedColumnMode.FrameOfReference, backing: backing);
        if(valueEncoding != ColumnarValueColumnEncoding.EliasFanoWhenMonotone)
        {
            return frame;
        }

        if(boundaries is null)
        {
            return IsNonDecreasing(values)
                ? SmallerEncoding(frame, BlockPackedColumn.Build(values, BlockPackedColumnMode.EliasFano))
                : frame;
        }

        return SmallerEncoding(frame, BlockPackedColumn.BuildPartitioned(values, boundaries));
    }

    /// <summary>
    /// Returns the smaller-footprint of two packings of the SAME column — the
    /// one with the smaller <see cref="BlockPackedColumn.PackedByteCount"/>, a
    /// tie keeping the first. Because the smaller is kept, offering a column an
    /// additional candidate encoding can only shrink or match the result, never
    /// enlarge it; the only cost of a candidate is packing it.
    /// </summary>
    /// <param name="first">The first candidate; kept on a tie.</param>
    /// <param name="second">The second candidate.</param>
    /// <returns>The smaller-footprint column.</returns>
    private static BlockPackedColumn SmallerEncoding(BlockPackedColumn first, BlockPackedColumn second)
    {
        return second.PackedByteCount < first.PackedByteCount ? second : first;
    }

    /// <summary>Whether a column is globally non-decreasing — the Elias-Fano precondition. Level-0 values on a single-run build qualify; within-group level-1/2 values reset per parent and do not.</summary>
    /// <param name="values">The column.</param>
    /// <returns><see langword="true"/> when no value is smaller than its predecessor.</returns>
    private static bool IsNonDecreasing(ReadOnlySpan<uint> values)
    {
        for(int i = 1; i < values.Length; i++)
        {
            if(values[i] < values[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Copies an exclusive-end offset column into the int boundaries a partitioned-Elias-Fano value column borrows.</summary>
    /// <param name="offsets">The offset column values.</param>
    /// <returns>The boundaries, one per parent plus the final bound.</returns>
    private static int[] ToBoundaries(List<uint> offsets)
    {
        int[] boundaries = new int[offsets.Count];
        for(int i = 0; i < offsets.Count; i++)
        {
            boundaries[i] = (int)offsets[i];
        }

        return boundaries;
    }

    private static uint KeyAt(in EncodedTriple triple, byte position)
    {
        return position switch
        {
            0 => triple.Subject.Encoded,
            1 => triple.Predicate.Encoded,
            2 => triple.Object.Encoded,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be 0 (subject), 1 (predicate), or 2 (object)."),
        };
    }
}
