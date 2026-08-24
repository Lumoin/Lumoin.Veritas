using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing MANAGED vs NATIVE-ALIGNED column payload backing on the axes that actually
/// differ — GC-resident bytes, build cost, and read cost — at FIXED bytes. The packed
/// footprint (<see cref="ColumnarOrder.PackedByteCount"/>) is byte-identical between the two
/// (asserted as a control), so this is NOT a footprint measurement: native moves the
/// block-packed payload words off the GC heap (the LOH), and the soak measures the managed-heap
/// reduction, the build/read cost delta, and confirms the working set is not inflated.
/// </summary>
/// <remarks>
/// <para>
/// Two encodings frame the range. <see cref="ColumnarValueColumnEncoding.FrameOfReference"/>
/// keeps every value column block-packed, so native moves the most off the heap — the full
/// potential. <see cref="ColumnarValueColumnEncoding.EliasFanoWhenMonotone"/> (the default) is
/// realistic: its Elias-Fano value columns hold their own managed arrays that this increment
/// does NOT move off-GC (that is a later increment), so only the prefixed-delta offsets and the
/// frame-of-reference fallback columns go native — a smaller, honest reduction.
/// </para>
/// <para>
/// Hardware/OS axes (bus saturation, cache misses, DMA overlap) do not differ here — same
/// bytes, same access pattern — so they are out of scope; they belong to the mmap (I/O) soaks.
/// Line-oriented output with anchor rows and a per-triple scale projection.
/// </para>
/// </remarks>
internal static class ColumnBackingSoak
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>One backing's measured runtime sample at one configuration.</summary>
    /// <param name="PackedBytes">The packed footprint (identical across backings — the control).</param>
    /// <param name="BuildMs">Index build wall-clock.</param>
    /// <param name="BuildAllocBytes">Bytes allocated on the managed heap during build.</param>
    /// <param name="ManagedHeapBytes">Managed heap attributable to the live index (after a full GC).</param>
    /// <param name="LohBytes">Large-object-heap size with the index live.</param>
    /// <param name="WorkingSetBytes">Process working set with the index live.</param>
    /// <param name="ScanMs">Wall-clock to decode every block of every column once.</param>
    /// <param name="ProbeNsPerOp">Per-probe cost of pointwise reads on a block-packed column.</param>
    private readonly record struct Sample(
        long PackedBytes,
        double BuildMs,
        long BuildAllocBytes,
        long ManagedHeapBytes,
        long LohBytes,
        long WorkingSetBytes,
        double ScanMs,
        double ProbeNsPerOp);

    /// <summary>Runs the managed-vs-native backing comparison over a triple-count ladder.</summary>
    public static void RunColumnBackingSoak()
    {
        foreach(int groups in (ReadOnlySpan<int>)[500_000, 2_000_000, 4_000_000])
        {
            RunConfiguration(groups);
        }
    }

    /// <summary>Runs both encodings, each managed vs native, at one triple count.</summary>
    /// <param name="groups">The three-edge group count; the triple count is three times this.</param>
    private static void RunConfiguration(int groups)
    {
        List<EncodedTriple> corpus = BuildCorpus(groups);
        long tripleCount = corpus.Count;
        const ColumnarOrderSetMode mode = ColumnarOrderSetMode.ThreeRotations;
        SoakStatistics.ReportGraph(corpus, $"triples={tripleCount:N0}");

        foreach(ColumnarValueColumnEncoding encoding in (ReadOnlySpan<ColumnarValueColumnEncoding>)[ColumnarValueColumnEncoding.FrameOfReference, ColumnarValueColumnEncoding.EliasFanoWhenMonotone])
        {
            Sample managed = MeasureBacking(corpus, mode, encoding, ColumnPayloadBacking.Managed);
            Sample native = MeasureBacking(corpus, mode, encoding, ColumnPayloadBacking.NativeAligned);

            ReportPair(tripleCount, mode, encoding, managed, native);
        }
    }

    /// <summary>Prints the anchor rows, the delta, and the scale projection for one encoding.</summary>
    private static void ReportPair(long tripleCount, ColumnarOrderSetMode mode, ColumnarValueColumnEncoding encoding, Sample managed, Sample native)
    {
        double mib = 1024.0 * 1024.0;
        double gib = mib * 1024.0;
        string footprint = managed.PackedBytes == native.PackedBytes ? "MATCH" : $"DIFFER {managed.PackedBytes} vs {native.PackedBytes}";
        long offGc = managed.ManagedHeapBytes - native.ManagedHeapBytes;
        double offGcPerTriple = (double)offGc / tripleCount;

        Console.WriteLine($"[backing] triples={tripleCount:N0} mode={mode} encoding={encoding} packed-footprint {footprint} ({managed.PackedBytes / mib,6:F1} MiB)  | columns: build-ms build-alloc-MiB managed-heap-MiB LOH-MiB workingset-MiB scan-ms probe-ns/op");
        WriteRow("managed (LOH)", managed, mib);
        WriteRow("native (off-GC)", native, mib);
        Console.WriteLine($"[backing]   off-GC managed-heap reduction: {offGc / mib,6:F1} MiB  ({offGcPerTriple,5:F1} B/triple)   working-set delta {(native.WorkingSetBytes - managed.WorkingSetBytes) / mib,+6:F1} MiB (≈0 expected)");
        Console.WriteLine($"[backing]   scale (off-GC bytes removed): 12M {offGcPerTriple * 12_000_000 / gib,5:F2} GiB   100M {offGcPerTriple * 100_000_000 / gib,6:F2} GiB   1B {offGcPerTriple * 1_000_000_000 / gib,6:F2} GiB");
    }

    /// <summary>Prints one backing's row.</summary>
    private static void WriteRow(string label, Sample sample, double mib)
    {
        Console.WriteLine($"[backing]   {label,-16} {sample.BuildMs,9:F0} {sample.BuildAllocBytes / mib,16:F1} {sample.ManagedHeapBytes / mib,17:F1} {sample.LohBytes / mib,9:F1} {sample.WorkingSetBytes / mib,15:F1} {sample.ScanMs,9:F1} {sample.ProbeNsPerOp,12:F1}");
    }

    /// <summary>Builds an index with the given backing and measures build, GC residency, working set, and read cost.</summary>
    /// <param name="corpus">The triple corpus.</param>
    /// <param name="mode">The order-set mode.</param>
    /// <param name="encoding">The value-column encoding.</param>
    /// <param name="backing">The payload backing under test.</param>
    /// <returns>The measured sample.</returns>
    private static Sample MeasureBacking(List<EncodedTriple> corpus, ColumnarOrderSetMode mode, ColumnarValueColumnEncoding encoding, ColumnPayloadBacking backing)
    {
        Settle();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        long allocBefore = GC.GetTotalAllocatedBytes(precise: true);

        long buildStart = Stopwatch.GetTimestamp();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus, mode, encoding, backing);
        double buildMs = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;
        long buildAlloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

        Settle();
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        long managedHeap = Math.Max(0, heapAfter - heapBefore);

        GCMemoryInfo gcInfo = GC.GetGCMemoryInfo();
        long loh = gcInfo.GenerationInfo.Length > 3 ? gcInfo.GenerationInfo[3].SizeAfterBytes : 0;

        long workingSet;
        using(Process process = Process.GetCurrentProcess())
        {
            process.Refresh();
            workingSet = process.WorkingSet64;
        }

        long packed = MeasurePacked(index);
        double scanMs = MeasureScan(index);
        double probeNs = MeasureProbe(index);

        GC.KeepAlive(index);

        return new Sample(packed, buildMs, buildAlloc, managedHeap, loh, workingSet, scanMs, probeNs);
    }

    /// <summary>Sums the packed footprint across the index's materialised orders.</summary>
    private static long MeasurePacked(ColumnarTripleIndex index)
    {
        long packed = 0;
        for(int permutation = 0; permutation < 6; permutation++)
        {
            if(index.IsPermutationAvailable(permutation))
            {
                packed += index.OrderAt(permutation).PackedByteCount;
            }
        }

        return packed;
    }

    /// <summary>Decodes every block of every column once — the scan read path that goes through the payload span.</summary>
    private static double MeasureScan(ColumnarTripleIndex index)
    {
        Span<uint> scratch = new uint[BlockPackedColumn.BlockLength];
        long start = Stopwatch.GetTimestamp();
        for(int permutation = 0; permutation < 6; permutation++)
        {
            if(!index.IsPermutationAvailable(permutation))
            {
                continue;
            }

            ColumnarOrder order = index.OrderAt(permutation);
            for(int level = 0; level < 3; level++)
            {
                ScanColumn(order.ValuesColumnAt(level), scratch);

                if(level < 2)
                {
                    ScanColumn(order.OffsetsColumnAt(level), scratch);
                }
            }
        }

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Decodes every block of one column into the reused scratch — only for block-packed columns, the ones the backing affects (Elias-Fano columns hold managed arrays in both backings, and block-decoding a partitioned-Elias-Fano column is not its representative access path).</summary>
    private static void ScanColumn(BlockPackedColumn column, Span<uint> scratch)
    {
        if(column.Mode is not (BlockPackedColumnMode.FrameOfReference or BlockPackedColumnMode.PrefixedDeltas))
        {
            return;
        }

        for(int block = 0; block < column.BlockCount; block++)
        {
            column.DecodeBlock(block, scratch);
        }
    }

    /// <summary>Pointwise reads on a block-packed offset column (always present, always block-packed), the per-op span path.</summary>
    private static double MeasureProbe(ColumnarTripleIndex index)
    {
        const int Probes = 200_000;
        BlockPackedColumn column = index.OrderAt(FirstAvailable(index)).OffsetsColumnAt(0);
        int length = column.Length;
        if(length == 0)
        {
            return 0.0;
        }

        BlockPackedColumnReader reader = new(column);
        ulong state = 0x1234_5678_9ABC_DEF0UL;
        uint sink = 0;
        long start = Stopwatch.GetTimestamp();
        for(int i = 0; i < Probes; i++)
        {
            state = Mix(state);
            sink ^= reader.ValueAt((int)(state % (ulong)length));
        }

        double elapsedNs = Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1_000_000.0;
        GC.KeepAlive(sink);

        return elapsedNs / Probes;
    }

    /// <summary>The first materialised permutation index.</summary>
    private static int FirstAvailable(ColumnarTripleIndex index)
    {
        for(int permutation = 0; permutation < 6; permutation++)
        {
            if(index.IsPermutationAvailable(permutation))
            {
                return permutation;
            }
        }

        return 0;
    }

    /// <summary>Forces the heap to a settled state: full collect, drain finalizers (frees discarded native buffers), collect again.</summary>
    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness — reproducible probe indices, no entropy seam.</summary>
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

    /// <summary>Builds disjoint directed triangles over sequential node ids — the favourable, highly-compressible corpus the storage soaks use.</summary>
    /// <param name="groups">The group count.</param>
    /// <returns>The triple corpus, three edges per group.</returns>
    private static List<EncodedTriple> BuildCorpus(int groups)
    {
        List<EncodedTriple> corpus = new(groups * 3);
        for(int i = 0; i < groups; i++)
        {
            uint a = (uint)((long)i * 3);
            uint b = a + 1;
            uint c = a + 2;
            corpus.Add(EncodedTriple.FromEncoded(a, Predicate, b));
            corpus.Add(EncodedTriple.FromEncoded(b, Predicate, c));
            corpus.Add(EncodedTriple.FromEncoded(c, Predicate, a));
        }

        return corpus;
    }
}
