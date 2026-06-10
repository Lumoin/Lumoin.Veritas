using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak comparing the succinct triple self-index against the columnar triple
/// index on the same corpora: whole-structure bits per triple and build time
/// (the storage axes), then equal-semantics membership probes — the
/// self-index's forward and backward binding chains against
/// <see cref="ColumnarTripleIndex.Contains"/> — and the self-index's
/// range-successor seek, as nanoseconds per operation.
/// </summary>
/// <remarks>
/// <para>
/// Footprint caveat printed with the numbers: the self-index figure counts the
/// WHOLE structure (payload, rank directories, select samples, boundary
/// sequences); the columnar figure is the packed column payload
/// (<see cref="ColumnarOrder.PackedByteCount"/>). Two corpora: the disjoint
/// directed triangles of the column-bits soak (single predicate — degenerate
/// for the predicate-led rotation, favourable for the others), and a
/// community-clustered multi-predicate graph. Line-oriented output, units in
/// every figure.
/// </para>
/// </remarks>
internal static class SelfIndexSoak
{
    /// <summary>The probe count per membership/seek loop.</summary>
    private const int ProbeCount = 50_000;

    /// <summary>Runs both corpora.</summary>
    public static void RunSelfIndexSoak()
    {
        RunCorpus("triangles", BuildTriangleCorpus(500_000));
        RunCorpus("community", BuildCommunityCorpus(nodes: 200_000, communitySize: 1_000, outDegree: 7));
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

    /// <summary>Builds all three structures over the corpus and reports footprint, build time, probe latencies, and seek latency.</summary>
    /// <param name="label">The corpus label for the output lines.</param>
    /// <param name="corpus">The triples.</param>
    private static void RunCorpus(string label, List<EncodedTriple> corpus)
    {
        long start = Stopwatch.GetTimestamp();
        TripleSelfIndex selfIndex = TripleSelfIndex.Build(corpus);
        TimeSpan selfBuild = Stopwatch.GetElapsedTime(start);

        int tripleCount = selfIndex.Count;
        Console.WriteLine($"[self-index] corpus={label} triples={tripleCount:N0}");

        double selfBits = (double)selfIndex.BitCount / tripleCount;
        double selfMiB = selfIndex.BitCount / 8.0 / (1024.0 * 1024.0);
        Console.WriteLine($"[self-index]   self-index (whole structure)    | build {selfBuild.TotalMilliseconds,8:F1} ms | {selfMiB,7:F1} MiB | {selfBits,6:F1} bits/triple total");

        foreach(ColumnarOrderSetMode mode in (ReadOnlySpan<ColumnarOrderSetMode>)[ColumnarOrderSetMode.AllSixOrders, ColumnarOrderSetMode.ThreeRotations])
        {
            start = Stopwatch.GetTimestamp();
            ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(corpus, mode);
            TimeSpan columnarBuild = Stopwatch.GetElapsedTime(start);

            long packedBytes = 0;
            for(int permutation = 0; permutation < 6; permutation++)
            {
                if(columnar.IsPermutationAvailable(permutation))
                {
                    packedBytes += columnar.OrderAt(permutation).PackedByteCount;
                }
            }

            double bits = packedBytes * 8.0 / tripleCount;
            double miB = packedBytes / (1024.0 * 1024.0);
            Console.WriteLine($"[self-index]   columnar {mode,-14} (packed columns) | build {columnarBuild.TotalMilliseconds,8:F1} ms | {miB,7:F1} MiB | {bits,6:F1} bits/triple total");
        }

        RunProbes(label, corpus, selfIndex);
    }

    /// <summary>Times equal-semantics membership probes on both structures and the self-index's bound-range seek.</summary>
    /// <param name="label">The corpus label.</param>
    /// <param name="corpus">The triples.</param>
    /// <param name="selfIndex">The built self-index.</param>
    private static void RunProbes(string label, List<EncodedTriple> corpus, TripleSelfIndex selfIndex)
    {
        ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.ThreeRotations);

        //Probes: alternating present triples (strided over the corpus) and
        //absent ones — a present (subject, predicate) prefix with another
        //triple's object, verified absent so the probe fails late, not at a
        //boundary guard.
        HashSet<EncodedTriple> present = [.. corpus];
        EncodedTriple[] probes = new EncodedTriple[ProbeCount];
        int stride = Math.Max(1, corpus.Count / ProbeCount);
        ulong state = 99;
        for(int i = 0; i < ProbeCount; i++)
        {
            EncodedTriple candidate = corpus[(i * stride) % corpus.Count];
            if((i & 1) == 0)
            {
                probes[i] = candidate;

                continue;
            }

            state = Mix(state);
            EncodedTriple donor = corpus[(int)(state % (ulong)corpus.Count)];
            EncodedTriple absent = new(candidate.Subject, candidate.Predicate, donor.Object);
            if(present.Contains(absent))
            {
                absent = new EncodedTriple(candidate.Subject, candidate.Predicate, new TermId(uint.MaxValue - 2));
            }

            probes[i] = absent;
        }

        //Forward chain: leader block, second-position narrow, third-position
        //backward bind — the subject-led descent shape.
        int forwardHits = 0;
        long start = Stopwatch.GetTimestamp();
        foreach(EncodedTriple probe in probes)
        {
            SelfIndexRange block = selfIndex.BindFirst(SelfIndexRotation.SubjectPredicateObject, probe.Subject);
            if(block.IsEmpty)
            {
                continue;
            }

            SelfIndexRange narrowed = selfIndex.BindFollowing(block, probe.Subject, probe.Predicate);
            if(narrowed.IsEmpty)
            {
                continue;
            }

            if(!selfIndex.BindPreceding(narrowed, probe.Object).IsEmpty)
            {
                forwardHits++;
            }
        }

        TimeSpan forwardTime = Stopwatch.GetElapsedTime(start);

        //Backward chain: three successive backward steps from the full table.
        int backwardHits = 0;
        start = Stopwatch.GetTimestamp();
        foreach(EncodedTriple probe in probes)
        {
            SelfIndexRange step = selfIndex.BindPreceding(selfIndex.FullRange(SelfIndexRotation.SubjectPredicateObject), probe.Object);
            step = selfIndex.BindPreceding(step, probe.Predicate);
            if(!selfIndex.BindPreceding(step, probe.Subject).IsEmpty)
            {
                backwardHits++;
            }
        }

        TimeSpan backwardTime = Stopwatch.GetElapsedTime(start);

        int containsHits = 0;
        start = Stopwatch.GetTimestamp();
        foreach(EncodedTriple probe in probes)
        {
            if(columnar.Contains(probe.Subject, probe.Predicate, probe.Object))
            {
                containsHits++;
            }
        }

        TimeSpan containsTime = Stopwatch.GetElapsedTime(start);

        string agreement = forwardHits == containsHits && backwardHits == containsHits ? "MATCH" : "MISMATCH";
        double forwardNs = forwardTime.TotalNanoseconds / ProbeCount;
        double backwardNs = backwardTime.TotalNanoseconds / ProbeCount;
        double containsNs = containsTime.TotalNanoseconds / ProbeCount;
        Console.WriteLine($"[self-index]   probes corpus={label} n={ProbeCount:N0} hits={containsHits:N0} {agreement} | self forward {forwardNs,8:F0} ns/op | self backward {backwardNs,8:F0} ns/op | columnar contains {containsNs,8:F0} ns/op");

        //The bound-range seek: from each present probe's (subject, predicate)
        //range, the smallest object at or above the probe's — the inner
        //operation of an ordered intersection over the unbound position.
        int seekHits = 0;
        start = Stopwatch.GetTimestamp();
        foreach(EncodedTriple probe in probes)
        {
            SelfIndexRange block = selfIndex.BindFirst(SelfIndexRotation.SubjectPredicateObject, probe.Subject);
            if(block.IsEmpty)
            {
                continue;
            }

            SelfIndexRange narrowed = selfIndex.BindFollowing(block, probe.Subject, probe.Predicate);
            if(narrowed.IsEmpty)
            {
                continue;
            }

            if(selfIndex.TrySeekPreceding(narrowed, probe.Object, out _))
            {
                seekHits++;
            }
        }

        TimeSpan seekTime = Stopwatch.GetElapsedTime(start);
        double seekNs = seekTime.TotalNanoseconds / ProbeCount;
        Console.WriteLine($"[self-index]   seek corpus={label} n={ProbeCount:N0} found={seekHits:N0} | bind(s,p)+seek(o) {seekNs,8:F0} ns/op");
    }

    /// <summary>Builds disjoint directed triangles over sequential node ids — the column-bits soak's corpus: group <c>i</c> carries (3i→3i+1), (3i+1→3i+2), (3i+2→3i) under one predicate.</summary>
    /// <param name="groups">The group count.</param>
    /// <returns>The corpus, three edges per group.</returns>
    private static List<EncodedTriple> BuildTriangleCorpus(int groups)
    {
        List<EncodedTriple> corpus = new(groups * 3);
        for(int i = 0; i < groups; i++)
        {
            uint a = (uint)(i * 3) + 1;
            uint b = a + 1;
            uint c = a + 2;
            corpus.Add(EncodedTriple.FromEncoded(a, 1_000, b));
            corpus.Add(EncodedTriple.FromEncoded(b, 1_000, c));
            corpus.Add(EncodedTriple.FromEncoded(c, 1_000, a));
        }

        return corpus;
    }

    /// <summary>Builds a community-clustered multi-predicate graph: sequential node ids in communities, most edges within the community, a few across, predicates drawn from a small set.</summary>
    /// <param name="nodes">The node count.</param>
    /// <param name="communitySize">Nodes per community.</param>
    /// <param name="outDegree">Out-edges per node.</param>
    /// <returns>The corpus.</returns>
    private static List<EncodedTriple> BuildCommunityCorpus(int nodes, int communitySize, int outDegree)
    {
        HashSet<EncodedTriple> corpus = new(nodes * outDegree);
        ulong state = 5;
        for(int node = 0; node < nodes; node++)
        {
            uint subject = (uint)node + 1;
            int communityStart = (node / communitySize) * communitySize;
            for(int edge = 0; edge < outDegree; edge++)
            {
                state = Mix(state);
                uint predicate = 1 + (uint)(state % 32);
                state = Mix(state);
                int target = (state % 100) < 95
                    ? communityStart + (int)(Mix(state) % (ulong)communitySize)
                    : (int)(Mix(state ^ 0x5DEECE66DUL) % (ulong)nodes);
                corpus.Add(EncodedTriple.FromEncoded(subject, predicate, (uint)target + 1));
            }
        }

        return [.. corpus];
    }
}
