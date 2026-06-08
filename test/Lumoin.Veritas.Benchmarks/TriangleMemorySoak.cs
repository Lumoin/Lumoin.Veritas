using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak measuring the resident memory of the columnar triple index on
/// triangle-dense data — the storage footprint behind the "extreme triangle"
/// representation, reported as bits per triple. The concrete type measured is
/// <see cref="ColumnarTripleIndex"/>: its per-order block-packed CSR columns
/// (delta-of-sorted values plus bit-packed offsets) are where a structured,
/// near-sequential triangle corpus compresses hardest.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is disjoint directed triangles over sequential node ids and one
/// predicate — the favourable, highly-structured case that the compact figure
/// describes; a random triangle graph would pack looser. Footprint is the
/// managed-heap delta across the build (<see cref="GC.GetTotalMemory(bool)"/>
/// with a full collect on each side, the corpus already resident before the
/// "before" sample so the delta isolates the index). This is approximate — the
/// codebase has no exact per-structure byte accessor yet — so the figure is a
/// heap-retention estimate, not a serialized size. Line-oriented output.
/// </para>
/// </remarks>
internal static class TriangleMemorySoak
{
    /// <summary>The single predicate every triangle edge carries.</summary>
    private const uint Predicate = 1_000;

    /// <summary>Runs the soak ladder over a triangle-count range.</summary>
    public static void RunTriangleMemorySoak()
    {
        RunConfiguration(triangles: 100_000);
        RunConfiguration(triangles: 500_000);
        RunConfiguration(triangles: 2_000_000);
    }

    /// <summary>Builds the columnar index from a triangle corpus and reports its resident bits per triple.</summary>
    /// <param name="triangles">The triangle count; the triple count is three times this.</param>
    private static void RunConfiguration(int triangles)
    {
        List<EncodedTriple> corpus = BuildTriangles(triangles);
        long tripleCount = corpus.Count;

        //The corpus is resident before the baseline sample, so the delta is the
        //index alone. Full collect both sides to drop build scratch.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);

        long start = Stopwatch.GetTimestamp();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(corpus);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        long indexBytes = Math.Max(after - before, 0);
        GC.KeepAlive(index);

        double bitsPerTriple = indexBytes * 8.0 / tripleCount;
        double indexMiB = indexBytes / (1024.0 * 1024.0);

        Console.WriteLine($"[triangle] triangles={triangles:N0} triples={tripleCount:N0} | build {elapsed.TotalMilliseconds,8:F1} ms | index {indexMiB,7:F1} MiB | {bitsPerTriple,6:F1} bits/triple ({indexBytes * 1.0 / tripleCount:F2} B/triple)");
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
