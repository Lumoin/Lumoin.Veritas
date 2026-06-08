using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak measuring the columnar triple index's <em>packed</em> bits per triple
/// on triangle-dense data — the storage footprint behind the "extreme triangle"
/// representation. The concrete type is <see cref="ColumnarTripleIndex"/>; its
/// per-order block-packed CSR columns (<see cref="ColumnarOrder.PackedByteCount"/>,
/// the exact packed byte count — not a heap estimate) are summed across the
/// materialised orders, then divided by the triple count.
/// </summary>
/// <remarks>
/// <para>
/// Reported both <b>per order</b> (the figure to compare against the
/// representation's default) and as the <b>index total</b> (per order × the
/// order count of the mode). The corpus is disjoint directed triangles over
/// sequential ids and one predicate — the favourable, highly-structured case.
/// Each <see cref="ColumnarOrderSetMode"/> is a point on the
/// memory-vs-answerable-shapes curve: fewer orders cost less but cannot answer
/// rotation-incompatible (cyclic) joins. Line-oriented output.
/// </para>
/// </remarks>
internal static class TriangleMemorySoak
{
    /// <summary>The single predicate every triangle edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Runs the soak ladder over a triangle-count range and the order-set modes.</summary>
    public static void RunTriangleMemorySoak()
    {
        RunConfiguration(triangles: 500_000);
        RunConfiguration(triangles: 2_000_000);
    }

    /// <summary>Builds the columnar index from a triangle corpus under each order-set mode and reports its packed bits per triple.</summary>
    /// <param name="triangles">The triangle count; the triple count is three times this.</param>
    private static void RunConfiguration(int triangles)
    {
        List<EncodedTriple> corpus = BuildTriangles(triangles);
        long tripleCount = corpus.Count;
        Console.WriteLine($"[triangle] triangles={triangles:N0} triples={tripleCount:N0}");

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

            Console.WriteLine($"[triangle]   {mode,-14} orders={orders} | build {elapsed.TotalMilliseconds,8:F1} ms | packed {totalMiB,7:F1} MiB | {perOrderBits,5:F1} bits/triple/order | {totalBits,6:F1} bits/triple total");
        }
    }

    /// <summary>Builds disjoint directed triangles over sequential node ids: triangle <c>i</c> carries the edges (3i→3i+1), (3i+1→3i+2), (3i+2→3i).</summary>
    /// <param name="triangles">The triangle count.</param>
    /// <returns>The triple corpus, three edges per triangle.</returns>
    private static List<EncodedTriple> BuildTriangles(int triangles)
    {
        List<EncodedTriple> corpus = new(triangles * 3);
        for(int i = 0; i < triangles; i++)
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
