using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak measuring the columnar triple index's <em>packed column footprint</em>
/// as bits per triple. The concrete type is <see cref="ColumnarTripleIndex"/>;
/// its per-order block-packed CSR columns expose an exact packed byte count
/// (<see cref="ColumnarOrder.PackedByteCount"/>, summed over the materialised
/// orders) — not a heap estimate — divided by the triple count.
/// </summary>
/// <remarks>
/// <para>
/// Reported both <b>per order</b> (the figure that characterises the column
/// encoding) and as the <b>index total</b> (per order × the order count of the
/// mode). The corpus is a structured graph (disjoint directed triangles over
/// sequential ids and one predicate) — a favourable, highly-compressible case,
/// so the per-order figure is a lower-ish bound for this storage. Each
/// <see cref="ColumnarOrderSetMode"/> is a point on the memory-vs-answerable-
/// shapes curve: fewer orders cost less but cannot answer rotation-incompatible
/// (cyclic) joins. Line-oriented output.
/// </para>
/// </remarks>
internal static class ColumnFootprintSoak
{
    /// <summary>The single predicate every edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Runs the soak ladder over a triple-count range and the order-set modes.</summary>
    public static void RunColumnFootprintSoak()
    {
        RunConfiguration(groups: 500_000);
        RunConfiguration(groups: 2_000_000);
    }

    /// <summary>Builds the columnar index from the corpus under each order-set mode and reports its packed bits per triple.</summary>
    /// <param name="groups">The three-edge group count; the triple count is three times this.</param>
    private static void RunConfiguration(int groups)
    {
        List<EncodedTriple> corpus = BuildCorpus(groups);
        long tripleCount = corpus.Count;
        Console.WriteLine($"[column-bits] groups={groups:N0} triples={tripleCount:N0}");
        SoakStatistics.ReportGraph(corpus, $"groups={groups:N0}");

        foreach(ColumnarOrderSetMode mode in (ReadOnlySpan<ColumnarOrderSetMode>)[ColumnarOrderSetMode.AllSixOrders, ColumnarOrderSetMode.ThreeRotations])
        {
            long start = Stopwatch.GetTimestamp();
            ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus, mode);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

            long packedBytes = 0;
            int orders = 0;
            for(int permutation = 0; permutation < 6; permutation++)
            {
                if(index.IsPermutationAvailable(permutation))
                {
                    packedBytes += index.OrderAt(permutation).PackedByteCount;
                    orders++;
                }
            }

            double totalBits = packedBytes * 8.0 / tripleCount;
            double perOrderBits = totalBits / orders;
            double totalMiB = packedBytes / (1024.0 * 1024.0);

            Console.WriteLine($"[column-bits]   {mode,-14} orders={orders} | build {elapsed.TotalMilliseconds,8:F1} ms | packed {totalMiB,7:F1} MiB | {perOrderBits,5:F1} bits/triple/order | {totalBits,6:F1} bits/triple total");
            SoakStatistics.Report(index, mode.ToString());
        }
    }

    /// <summary>Builds disjoint directed triangles over sequential node ids: group <c>i</c> carries the edges (3i→3i+1), (3i+1→3i+2), (3i+2→3i) — a structured, highly-compressible corpus.</summary>
    /// <param name="groups">The group count.</param>
    /// <returns>The triple corpus, three edges per group.</returns>
    private static List<EncodedTriple> BuildCorpus(int groups)
    {
        List<EncodedTriple> corpus = new(groups * 3);
        for(int i = 0; i < groups; i++)
        {
            uint a = (uint)(i * 3);
            uint b = a + 1;
            uint c = a + 2;
            corpus.Add(EncodedTriple.FromEncoded(a, Predicate, b));
            corpus.Add(EncodedTriple.FromEncoded(b, Predicate, c));
            corpus.Add(EncodedTriple.FromEncoded(c, Predicate, a));
        }

        return corpus;
    }
}
