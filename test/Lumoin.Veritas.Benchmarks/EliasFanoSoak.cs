using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak quantifying the Elias-Fano succinct column encoding against
/// <see cref="BlockPackedColumnMode.FrameOfReference"/> on monotone sequences —
/// the ~10-vs-~22 bits/triple comparison, measured here rather than recalled.
/// For each universe/count ratio it reports the bits per value of each encoding
/// (and the Elias-Fano information bound), then times the seek both serve:
/// Elias-Fano <c>NextGEQ</c> vs frame-of-reference <c>LowerBound</c>.
/// </summary>
/// <remarks>
/// The sequence is a cumulative walk whose average gap sets the universe/count
/// ratio, so the Elias-Fano bound ≈ <c>2 + log2(gap)</c>. Line-oriented output
/// for hand-collation, the same shape as the other <c>--profile-*</c> soaks.
/// </remarks>
internal static class EliasFanoSoak
{
    /// <summary>Runs the soak ladder over a spread of universe/count ratios.</summary>
    public static void RunEliasFanoSoak()
    {
        foreach(int gap in (ReadOnlySpan<int>)[4, 16, 64, 256, 1024])
        {
            RunConfiguration(count: 1_000_000, gap);
        }
    }

    /// <summary>
    /// Sweeps the Elias-Fano select-sample rate on a fixed monotone sequence and
    /// reports footprint and seek against frame of reference. Since the seek
    /// measures faster than frame of reference, the question is how far the
    /// samples can be SPARSIFIED — saving footprint, lengthening select — before
    /// the seek advantage is spent. The simplest form of the multi-level-select
    /// lever: vary one-level sample density rather than add a hierarchy.
    /// </summary>
    public static void RunSelectDensitySoak()
    {
        const int count = 1_000_000;
        const int gap = 64;
        uint[] values = BuildMonotone(count, gap, out ulong universe);
        uint[] targets = BuildTargets(count, universe);

        BlockPackedColumn frame = BlockPackedColumn.Build(values, BlockPackedColumnMode.FrameOfReference);
        double frameBits = frame.PackedByteCount * 8.0 / count;
        double frameSeek = TimeFrameSeek(frame, count, targets);

        Console.WriteLine(
            $"[ef-select] select-density sweep — count={count:N0} u/n≈{gap}; FoR baseline {frameBits:F1} bits/val, {frameSeek:F0} ms seek\n"
            + $"[ef-select]   {"sampleRate",10} {"EF bits/val",12} {"EF seek ms",11} {"size vs FoR",12} {"seek vs FoR",12}");
        foreach(int rate in (ReadOnlySpan<int>)[8, 16, 32, 64, 128, 256, 512, 1024])
        {
            EliasFanoSequence eliasFano = EliasFanoSequence.Build(values, rate);
            double eliasFanoBits = (double)eliasFano.BitCount / count;
            double eliasFanoSeek = TimeEliasFanoSeek(eliasFano, targets);
            Console.WriteLine($"[ef-select]   {rate,10} {eliasFanoBits,12:F2} {eliasFanoSeek,11:F0} {frameBits / eliasFanoBits,11:F2}x {frameSeek / Math.Max(eliasFanoSeek, 0.01),11:F2}x");
        }
    }

    /// <summary>Builds both encodings over a monotone sequence of the given average gap and reports size and seek cost.</summary>
    /// <param name="count">The value count.</param>
    /// <param name="gap">The average successive gap; sets the universe/count ratio.</param>
    private static void RunConfiguration(int count, int gap)
    {
        uint[] values = BuildMonotone(count, gap, out ulong universe);

        EliasFanoSequence eliasFano = EliasFanoSequence.Build(values);
        BlockPackedColumn frame = BlockPackedColumn.Build(values, BlockPackedColumnMode.FrameOfReference);

        double eliasFanoBits = (double)eliasFano.BitCount / count;
        double frameBits = frame.PackedByteCount * 8.0 / count;
        double bound = 2.0 + Math.Log2(gap);

        Console.WriteLine($"[elias-fano] count={count:N0} u/n≈{gap,5} | EF {eliasFanoBits,5:F1} bits/val | FoR {frameBits,5:F1} bits/val | bound≈{bound,4:F1} | size x{frameBits / eliasFanoBits:F2}");

        uint[] targets = BuildTargets(count, universe);
        double eliasFanoSeekMs = TimeEliasFanoSeek(eliasFano, targets);
        double frameSeekMs = TimeFrameSeek(frame, count, targets);

        Console.WriteLine($"[elias-fano]   seek: EF {eliasFanoSeekMs,7:F1} ms  FoR {frameSeekMs,7:F1} ms  (EF x{frameSeekMs / Math.Max(eliasFanoSeekMs, 0.01):F2})");
    }

    /// <summary>Times a full sweep of Elias-Fano successor queries.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="targets">The query targets.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double TimeEliasFanoSeek(EliasFanoSequence sequence, uint[] targets)
    {
        long start = Stopwatch.GetTimestamp();
        ulong checksum = 0;
        foreach(uint target in targets)
        {
            checksum += (ulong)sequence.NextGEQ(target);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        GC.KeepAlive(checksum);

        return elapsed.TotalMilliseconds;
    }

    /// <summary>Times a full sweep of frame-of-reference lower-bound seeks over the whole column.</summary>
    /// <param name="column">The column.</param>
    /// <param name="count">The column length.</param>
    /// <param name="targets">The query targets.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double TimeFrameSeek(BlockPackedColumn column, int count, uint[] targets)
    {
        uint[] scratch = new uint[BlockPackedColumn.BlockLength];
        long start = Stopwatch.GetTimestamp();
        ulong checksum = 0;
        foreach(uint target in targets)
        {
            int cachedBlock = -1;
            checksum += (ulong)column.LowerBound(0, count, target, scratch, ref cachedBlock);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        GC.KeepAlive(checksum);

        return elapsed.TotalMilliseconds;
    }

    /// <summary>A strictly-increasing sequence whose average successive gap is about <paramref name="gap"/>.</summary>
    /// <param name="count">The value count.</param>
    /// <param name="gap">The average gap.</param>
    /// <param name="universe">Receives the universe (max value plus one).</param>
    /// <returns>The monotone values.</returns>
    private static uint[] BuildMonotone(int count, int gap, out ulong universe)
    {
        uint[] values = new uint[count];
        ulong state = 99;
        ulong running = 0;
        uint span = (uint)((2 * gap) - 1);
        for(int i = 0; i < count; i++)
        {
            state = Mix(state);
            running += 1 + (state % span);
            values[i] = (uint)Math.Min(running, uint.MaxValue);
        }

        universe = (ulong)values[count - 1] + 1;

        return values;
    }

    /// <summary>Random query targets across the universe.</summary>
    /// <param name="count">The target count.</param>
    /// <param name="universe">The value universe.</param>
    /// <returns>The targets.</returns>
    private static uint[] BuildTargets(int count, ulong universe)
    {
        uint[] targets = new uint[count];
        ulong state = 1234;
        for(int i = 0; i < count; i++)
        {
            state = Mix(state);
            targets[i] = (uint)(state % universe);
        }

        return targets;
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    /// <summary>
    /// Measures Elias-Fano against the columnar index's CURRENT per-column
    /// encoding on the REAL columns of a built <see cref="ColumnarTripleIndex"/>:
    /// each column of the SPO order is decoded back to its values and, where the
    /// column is globally non-decreasing (the level-0 value column on a
    /// single-run build, and every exclusive-end offset column), rebuilt as an
    /// <see cref="EliasFanoSequence"/>, so the bits/triple delta is read off the
    /// actual index rather than a synthetic sequence. Value columns at levels 1
    /// and 2 reset within each parent group and are NOT globally monotone — they
    /// are reported as the partitioned-Elias-Fano targets a later step covers,
    /// not encoded here.
    /// </summary>
    public static void RunColumnComparisonSoak()
    {
        foreach(int stride in (ReadOnlySpan<int>)[1, 16, 256])
        {
            RunColumnComparison(groups: 500_000, stride);
        }
    }

    /// <summary>Builds the index from a strided corpus and reports each SPO column's current vs Elias-Fano footprint.</summary>
    /// <param name="groups">The three-edge group count; the triple count is three times this.</param>
    /// <param name="stride">The node-id stride between groups; sets the level-0 value column's average gap (the universe/count ratio Elias-Fano trades on).</param>
    private static void RunColumnComparison(int groups, int stride)
    {
        List<EncodedTriple> corpus = BuildStridedCorpus(groups, stride);
        long tripleCount = corpus.Count;
        SoakStatistics.ReportGraph(corpus, $"stride={stride}");
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations);
        ColumnarOrder order = index.OrderAt(0);

        Console.WriteLine(
            $"[ef-columns] groups={groups:N0} stride={stride} triples={tripleCount:N0} (SPO order)\n"
            + $"[ef-columns]   {"column",-10} {"length",12} {"cur b/val",10} {"EF b/val",9} {"cur b/trip",11} {"EF b/trip",10}  note");

        ReadOnlySpan<(string Name, BlockPackedColumn Column)> columns =
        [
            ("L0 values", order.ValuesColumnAt(0)),
            ("L0 offsets", order.OffsetsColumnAt(0)),
            ("L1 values", order.ValuesColumnAt(1)),
            ("L1 offsets", order.OffsetsColumnAt(1)),
            ("L2 values", order.ValuesColumnAt(2)),
        ];

        foreach((string name, BlockPackedColumn column) in columns)
        {
            uint[] decoded = DecodeColumn(column);
            double currentPerValue = column.PackedByteCount * 8.0 / Math.Max(column.Length, 1);
            double currentPerTriple = column.PackedByteCount * 8.0 / tripleCount;

            if(IsNonDecreasing(decoded))
            {
                EliasFanoSequence eliasFano = EliasFanoSequence.Build(decoded);
                double eliasFanoPerValue = (double)eliasFano.BitCount / Math.Max(column.Length, 1);
                double eliasFanoPerTriple = (double)eliasFano.BitCount / tripleCount;
                string verdict = eliasFanoPerValue < currentPerValue
                    ? $"EF −{currentPerValue - eliasFanoPerValue:F1} b/val (x{currentPerValue / Math.Max(eliasFanoPerValue, 0.01):F2})"
                    : "current wins";
                Console.WriteLine($"[ef-columns]   {name,-10} {column.Length,12:N0} {currentPerValue,10:F2} {eliasFanoPerValue,9:F2} {currentPerTriple,11:F2} {eliasFanoPerTriple,10:F2}  {verdict}");
            }
            else
            {
                Console.WriteLine($"[ef-columns]   {name,-10} {column.Length,12:N0} {currentPerValue,10:F2} {"-",9} {currentPerTriple,11:F2} {"-",10}  within-group: partitioned-EF target");
            }
        }
    }

    /// <summary>Decodes a whole block-packed column back to its values.</summary>
    /// <param name="column">The column to decode.</param>
    /// <returns>The column's values in column order.</returns>
    private static uint[] DecodeColumn(BlockPackedColumn column)
    {
        uint[] values = new uint[column.Length];
        for(int block = 0; block < column.BlockCount; block++)
        {
            int start = block << BlockPackedColumn.BlockShift;
            column.DecodeBlock(block, values.AsSpan(start, column.BlockLengthOf(block)));
        }

        return values;
    }

    /// <summary>Whether a sequence is non-decreasing — the Elias-Fano build precondition.</summary>
    /// <param name="values">The values to test.</param>
    /// <returns><see langword="true"/> when no element is smaller than its predecessor.</returns>
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

    /// <summary>
    /// Disjoint directed triangles whose node ids are spread by
    /// <paramref name="stride"/>: group <c>i</c> carries the edges over nodes
    /// <c>3·i·stride</c>, <c>+1</c>, <c>+2</c>, so the distinct level-0 subjects
    /// ascend with an average gap of about <paramref name="stride"/> — the lever
    /// that moves the value column off the dense, both-encodings-optimal regime.
    /// </summary>
    /// <param name="groups">The group count.</param>
    /// <param name="stride">The node-id stride between groups.</param>
    /// <returns>The triple corpus, three edges per group.</returns>
    private static List<EncodedTriple> BuildStridedCorpus(int groups, int stride)
    {
        const uint predicate = 1_000;
        List<EncodedTriple> corpus = new(groups * 3);
        for(int i = 0; i < groups; i++)
        {
            uint a = (uint)((long)i * 3 * stride);
            uint b = a + 1;
            uint c = a + 2;
            corpus.Add(EncodedTriple.FromEncoded(a, predicate, b));
            corpus.Add(EncodedTriple.FromEncoded(b, predicate, c));
            corpus.Add(EncodedTriple.FromEncoded(c, predicate, a));
        }

        return corpus;
    }

    /// <summary>
    /// Sweeps the per-group fan-out and reports, for the within-group level-2
    /// value column (objects per subject), frame-of-reference bits/value against
    /// partitioned Elias-Fano (each group stored Elias-Fano relative to its own
    /// minimum, sharing one payload). Pins the break-even: tiny groups pay the
    /// per-segment base/width overhead and frame of reference wins; large
    /// clustered groups reach their local entropy and partitioned Elias-Fano
    /// wins. The triple count is held roughly fixed across the sweep.
    /// </summary>
    public static void RunPartitionedComparisonSoak()
    {
        const int targetTriples = 3_000_000;
        Console.WriteLine(
            $"[pef] fan-out sweep — L2 objects/subject, FrameOfReference vs partitioned Elias-Fano, ~{targetTriples:N0} triples/run\n"
            + $"[pef]   {"fanout",7} {"groups",12} {"triples",12} {"FoR b/val",10} {"PEF b/val",10}  verdict");
        foreach(int fanOut in (ReadOnlySpan<int>)[1, 2, 8, 32, 128, 512, 2_048, 8_192])
        {
            RunPartitioned(fanOut, targetTriples);
        }
    }

    /// <summary>Builds a fan-out corpus, then compares the level-2 value column's frame-of-reference and partitioned-Elias-Fano footprints.</summary>
    /// <param name="fanOut">The objects per subject — the level-2 group size.</param>
    /// <param name="targetTriples">The approximate triple count to hold across the sweep.</param>
    private static void RunPartitioned(int fanOut, int targetTriples)
    {
        int subjects = Math.Max(1, targetTriples / fanOut);
        List<EncodedTriple> corpus = BuildFanOutCorpus(subjects, fanOut, out long triples);
        SoakStatistics.ReportGraph(corpus, $"fanout={fanOut}");
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations);
        ColumnarOrder order = index.OrderAt(0);

        BlockPackedColumn level2 = order.ValuesColumnAt(2);
        uint[] values = DecodeColumn(level2);
        uint[] offsets = DecodeColumn(order.OffsetsColumnAt(1));
        int[] boundaries = new int[offsets.Length];
        for(int i = 0; i < offsets.Length; i++)
        {
            boundaries[i] = (int)offsets[i];
        }

        double frameBits = level2.PackedByteCount * 8.0 / Math.Max(values.Length, 1);
        PartitionedEliasFanoSequence partitioned = PartitionedEliasFanoSequence.Build(values, boundaries);
        double partitionedBits = (double)partitioned.BitCount / Math.Max(values.Length, 1);
        string verdict = partitionedBits < frameBits
            ? $"PEF −{frameBits - partitionedBits:F1} b/val (x{frameBits / Math.Max(partitionedBits, 0.01):F2})"
            : "FrameOfReference wins";

        Console.WriteLine($"[pef]   {fanOut,7} {partitioned.SegmentCount,12:N0} {triples,12:N0} {frameBits,10:F2} {partitionedBits,10:F2}  {verdict}");

        //End-to-end: build the index under the opt-in policy and report which
        //encoding its footprint-driven selector actually kept for the L2 column.
        ColumnarTripleIndex selected = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations, ColumnarValueColumnEncoding.EliasFanoWhenMonotone);
        BlockPackedColumn selectedLevel2 = selected.OrderAt(0).ValuesColumnAt(2);
        double selectedBits = selectedLevel2.PackedByteCount * 8.0 / Math.Max(selectedLevel2.Length, 1);
        Console.WriteLine($"[pef]     index picked L2: {selectedLevel2.Mode} at {selectedBits:F2} b/val");
    }

    /// <summary>
    /// Builds a fan-out corpus: <paramref name="subjects"/> subjects, each with
    /// <paramref name="fanOut"/> objects under one predicate, every subject's
    /// objects an ascending run in its own contiguous id range — so the level-2
    /// value column is a concatenation of equal-size ascending groups.
    /// </summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fanOut">The objects per subject.</param>
    /// <param name="triples">Receives the triple count.</param>
    /// <returns>The triple corpus.</returns>
    private static List<EncodedTriple> BuildFanOutCorpus(int subjects, int fanOut, out long triples)
    {
        const uint predicate = 1_000;
        const int objectStride = 4;
        long groupSpan = (long)fanOut * objectStride;
        List<EncodedTriple> corpus = new(subjects * fanOut);
        for(int s = 0; s < subjects; s++)
        {
            long objectBase = (long)s * groupSpan;
            for(int k = 0; k < fanOut; k++)
            {
                uint obj = (uint)Math.Min(objectBase + ((long)k * objectStride), uint.MaxValue);
                corpus.Add(EncodedTriple.FromEncoded((uint)s, predicate, obj));
            }
        }

        triples = (long)subjects * fanOut;

        return corpus;
    }
}
