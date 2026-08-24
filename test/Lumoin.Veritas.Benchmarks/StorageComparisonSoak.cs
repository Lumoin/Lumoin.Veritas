using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing the columnar triple index's storage, over a triple-count
/// ladder, against (a) an uncompressed baseline — the same CSR columns stored
/// as raw 32-bit values, what a worst-case-optimal join over plain sorted
/// relations would cost — and (b) the index with Elias-Fano opted into the
/// monotone value columns. Reported in bits per triple and MiB.
/// </summary>
/// <remarks>
/// The corpus is the disjoint-triangle structure (favourable, highly
/// compressible) that <see cref="ColumnFootprintSoak"/> uses; on a real, sparse
/// graph the absolute bits per triple run higher. Line-oriented output.
/// </remarks>
internal static class StorageComparisonSoak
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Runs the storage comparison over a triple-count ladder.</summary>
    public static void RunStorageComparisonSoak()
    {
        foreach(int groups in (ReadOnlySpan<int>)[500_000, 2_000_000, 4_000_000])
        {
            RunConfiguration(groups);
        }
    }

    /// <summary>Builds the index both ways at one triple count and reports storage against the uncompressed baseline.</summary>
    /// <param name="groups">The three-edge group count; the triple count is three times this.</param>
    private static void RunConfiguration(int groups)
    {
        List<EncodedTriple> corpus = BuildCorpus(groups);
        long tripleCount = corpus.Count;
        ulong universe = (ulong)((long)(groups - 1) * 3) + 3;
        SoakStatistics.ReportGraph(corpus, $"triples={tripleCount:N0}");
        const ColumnarOrderSetMode mode = ColumnarOrderSetMode.ThreeRotations;

        long startFrame = Stopwatch.GetTimestamp();
        ColumnarTripleIndex current = ColumnarTripleIndex.Build(corpus, mode, ColumnarValueColumnEncoding.FrameOfReference);
        TimeSpan frameElapsed = Stopwatch.GetElapsedTime(startFrame);
        (long currentPacked, long plain, int orders) = Measure(current);

        long startEliasFano = Stopwatch.GetTimestamp();
        ColumnarTripleIndex eliasFano = ColumnarTripleIndex.Build(corpus, mode, ColumnarValueColumnEncoding.EliasFanoWhenMonotone);
        TimeSpan eliasFanoElapsed = Stopwatch.GetElapsedTime(startEliasFano);
        (long eliasFanoPacked, _, _) = Measure(eliasFano);

        double rawTriples = 96.0;
        double packed = 2.0 * BitLength(universe);
        double plainBits = plain * 8.0 / tripleCount;
        double currentBits = currentPacked * 8.0 / tripleCount;
        double eliasFanoBits = eliasFanoPacked * 8.0 / tripleCount;
        double mib = 1024.0 * 1024.0;

        Console.WriteLine($"[storage] triples={tripleCount:N0} SO={universe:N0} mode={mode} orders={orders}");
        Console.WriteLine($"[storage]   raw-triples (3x32)           {rawTriples,7:F1} bits/triple");
        Console.WriteLine($"[storage]   packed (2*ceil(log2 SO))     {packed,7:F1} bits/triple");
        Console.WriteLine($"[storage]   plain-WCOJ uncompressed cols {plainBits,7:F1} bits/triple  ({plain / mib,7:F1} MiB, {plainBits / orders,5:F1} /order)");
        Console.WriteLine($"[storage]   ours current (FrameOfRef)    {currentBits,7:F1} bits/triple  ({currentPacked / mib,7:F1} MiB, {currentBits / orders,5:F1} /order)  x{plainBits / currentBits:F2} vs plain  build {frameElapsed.TotalMilliseconds,7:F0} ms");
        Console.WriteLine($"[storage]   ours + EF (monotone values)  {eliasFanoBits,7:F1} bits/triple  ({eliasFanoPacked / mib,7:F1} MiB, {eliasFanoBits / orders,5:F1} /order)  x{plainBits / eliasFanoBits:F2} vs plain  −{currentBits - eliasFanoBits:F1} vs current  build {eliasFanoElapsed.TotalMilliseconds,7:F0} ms");
    }

    /// <summary>Sums the packed and uncompressed (plain) byte footprints across an index's materialised orders.</summary>
    /// <param name="index">The index to measure.</param>
    /// <returns>The packed bytes, the plain 32-bit bytes, and the order count.</returns>
    private static (long Packed, long Plain, int Orders) Measure(ColumnarTripleIndex index)
    {
        long packed = 0;
        long plain = 0;
        int orders = 0;
        for(int permutation = 0; permutation < 6; permutation++)
        {
            if(!index.IsPermutationAvailable(permutation))
            {
                continue;
            }

            ColumnarOrder order = index.OrderAt(permutation);
            packed += order.PackedByteCount;
            plain += PlainBytesOf(order);
            orders++;
        }

        return (packed, plain, orders);
    }

    /// <summary>The uncompressed footprint of one order: every column entry as a raw 32-bit value.</summary>
    /// <param name="order">The order to measure.</param>
    /// <returns>The plain byte count.</returns>
    private static long PlainBytesOf(ColumnarOrder order)
    {
        long entries = 0;
        for(int level = 0; level < 3; level++)
        {
            entries += order.ValuesLengthAt(level);
        }

        entries += order.OffsetsColumnAt(0).Length;
        entries += order.OffsetsColumnAt(1).Length;

        return entries * sizeof(uint);
    }

    /// <summary>The number of bits needed to address a universe of the given size — <c>ceil(log2(size))</c>.</summary>
    /// <param name="size">The universe size.</param>
    /// <returns>The bit length.</returns>
    private static int BitLength(ulong size)
    {
        return size <= 1 ? 1 : 64 - BitOperations.LeadingZeroCount(size - 1);
    }

    /// <summary>Builds disjoint directed triangles over sequential node ids — the favourable, highly-compressible footprint corpus.</summary>
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
