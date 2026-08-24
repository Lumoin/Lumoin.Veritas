using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

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

    /// <summary>
    /// The corpus's information-theoretic shape, surfaced by the corpus
    /// builders so the footprint section can anchor measured bits/triple
    /// against the raw and the information-floor figures: the largest node
    /// identifier any subject or object carries and the number of distinct
    /// predicates.
    /// </summary>
    /// <param name="MaxNodeId">The largest subject/object encoded identifier in the corpus.</param>
    /// <param name="PredicateCount">The number of distinct predicates in the corpus.</param>
    private readonly record struct CorpusShape(uint MaxNodeId, int PredicateCount);

    /// <summary>Runs both corpora: footprint, probes, the triangle-join rung, and the end-to-end route rung.</summary>
    /// <returns>A task that completes when both corpora have run and reported.</returns>
    public static async Task RunSelfIndexSoakAsync()
    {
        (List<EncodedTriple> triangles, CorpusShape triangleShape) = BuildTriangleCorpus(500_000);
        RunCorpus("triangles", triangles, triangleShape);
        await RunTriangleJoinAsync("triangles", triangles, edgePredicate: 1_000).ConfigureAwait(false);
        await RunRouteAsync("triangles", triangles, edgePredicate: 1_000).ConfigureAwait(false);

        (List<EncodedTriple> community, CorpusShape communityShape) = BuildCommunityCorpus(nodes: 200_000, communitySize: 1_000, outDegree: 7);
        RunCorpus("community", community, communityShape);
        await RunTriangleJoinAsync("community", community, edgePredicate: 1).ConfigureAwait(false);
        await RunRouteAsync("community", community, edgePredicate: 1).ConfigureAwait(false);
    }

    /// <summary>
    /// The triangle-join rung: the same three-pattern cyclic query counted by
    /// the CSR leapfrog (the production evaluator over the all-six-orders
    /// index, materialising solutions) and by a worst-case-optimal driver over
    /// self-index triejoin iterators (counting agreed bindings without
    /// materialising). Three repetitions each; the minimum is reported.
    /// </summary>
    /// <param name="label">The corpus label.</param>
    /// <param name="corpus">The triples.</param>
    /// <param name="edgePredicate">The edge predicate the triangle patterns bind.</param>
    private static async Task RunTriangleJoinAsync(string label, List<EncodedTriple> corpus, uint edgePredicate)
    {
        ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(corpus, ColumnarOrderSetMode.AllSixOrders);
        TripleSelfIndex selfIndex = TripleSelfIndex.Build(corpus);

        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        TermId edge = TermId.FromEncoded(edgePredicate);
        TriplePattern[] patterns =
        [
            new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(edge), PatternPosition.OfVariable(b)),
            new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(edge), PatternPosition.OfVariable(c)),
            new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.Bound(edge), PatternPosition.OfVariable(a)),
        ];
        BasicGraphPattern query = new(patterns, registry);

        long csrCount = 0;
        TimeSpan csrBest = TimeSpan.MaxValue;
        for(int repetition = 0; repetition < 3; repetition++)
        {
            ColumnarBasicGraphPatternEvaluator evaluator = new(columnar, query, Planners.FirstOccurrence(query), VeritasClock.System);
            long start = Stopwatch.GetTimestamp();
            csrCount = 0;
            await foreach(Solution _ in evaluator.EvaluateAsync().ConfigureAwait(false))
            {
                csrCount++;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if(elapsed < csrBest)
            {
                csrBest = elapsed;
            }
        }

        long selfCount = 0;
        TimeSpan selfBest = TimeSpan.MaxValue;
        for(int repetition = 0; repetition < 3; repetition++)
        {
            long start = Stopwatch.GetTimestamp();
            selfCount = CountLeapfrogSolutions(selfIndex, patterns, [a, b, c]);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
            if(elapsed < selfBest)
            {
                selfBest = elapsed;
            }
        }

        string agreement = csrCount == selfCount ? "MATCH" : "MISMATCH";
        double ratio = selfBest.TotalMilliseconds > 0 ? csrBest.TotalMilliseconds / selfBest.TotalMilliseconds : 0;
        Console.WriteLine($"[self-index]   triangle-join corpus={label} solutions={csrCount:N0} {agreement} | csr leapfrog {csrBest.TotalMilliseconds,8:F1} ms | self leapfrog {selfBest.TotalMilliseconds,8:F1} ms | csr/self x{ratio:F2}");
    }

    /// <summary>
    /// The end-to-end route rung: the same cyclic triangle query, routed by a
    /// <see cref="QueryEngineRendezvous"/> from a <see cref="HypertrieGraphStore"/>
    /// system of record under two policies — the all-six-orders default, and a
    /// three-rotation view that opts the rotation-incompatible shape onto the
    /// succinct self-index. For each policy the FIRST query pays the on-demand
    /// index build and the SECOND reuses it; both are drained whole, so the
    /// figure is the production query cost a caller sees, with the per-query
    /// <see cref="QueryTraceEventKind.EngineSelected"/> event separating build
    /// from query. The two policies' solution counts are asserted equal.
    /// </summary>
    /// <param name="label">The corpus label.</param>
    /// <param name="corpus">The triples.</param>
    /// <param name="edgePredicate">The edge predicate the triangle patterns bind.</param>
    private static async Task RunRouteAsync(string label, List<EncodedTriple> corpus, uint edgePredicate)
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(corpus, VeritasHashing.Default, CancellationToken.None).ConfigureAwait(false);

        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        TermId edge = TermId.FromEncoded(edgePredicate);
        TriplePattern[] patterns =
        [
            new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(edge), PatternPosition.OfVariable(b)),
            new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(edge), PatternPosition.OfVariable(c)),
            new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.Bound(edge), PatternPosition.OfVariable(a)),
        ];
        BasicGraphPattern query = new(patterns, registry);

        QueryEnginePolicy defaultPolicy = QueryEnginePolicy.Default;
        QueryEnginePolicy selfIndexPolicy = QueryEnginePolicy.Default with { OrderSetMode = ColumnarOrderSetMode.ThreeRotations, PreferSelfIndex = true };

        (long defaultCount, TimeSpan defaultFirst, TimeSpan defaultSecond, QueryEngineKind defaultEngine, long defaultBuildMilliseconds) =
            await RouteOnceAsync(store, defaultPolicy, query).ConfigureAwait(false);
        (long selfCount, TimeSpan selfFirst, TimeSpan selfSecond, QueryEngineKind selfEngine, long selfBuildMilliseconds) =
            await RouteOnceAsync(store, selfIndexPolicy, query).ConfigureAwait(false);

        string agreement = defaultCount == selfCount ? "MATCH" : "MISMATCH";
        Console.WriteLine($"[self-index]   route corpus={label} solutions(default)={defaultCount:N0} solutions(self-index)={selfCount:N0} {agreement}");
        Console.WriteLine($"[self-index]     default      engine={defaultEngine,-13} | first {defaultFirst.TotalMilliseconds,8:F1} ms | second {defaultSecond.TotalMilliseconds,8:F1} ms | event build {defaultBuildMilliseconds,6:N0} ms");
        Console.WriteLine($"[self-index]     self-index   engine={selfEngine,-13} | first {selfFirst.TotalMilliseconds,8:F1} ms | second {selfSecond.TotalMilliseconds,8:F1} ms | event build {selfBuildMilliseconds,6:N0} ms");
    }

    /// <summary>
    /// Runs the query twice through a fresh rendezvous over the store under the
    /// policy: the first drain materialises the on-demand index and the second
    /// reuses the view. Returns the solution count (identical across both
    /// drains), each drain's wall time, and the engine kind and event-carried
    /// build milliseconds the first query's selection event announced.
    /// </summary>
    /// <param name="store">The system of record.</param>
    /// <param name="policy">The routing policy.</param>
    /// <param name="query">The triangle pattern.</param>
    /// <returns>The solution count, the first and second drain times, the selected engine, and the first selection event's build milliseconds.</returns>
    private static async Task<(long Count, TimeSpan First, TimeSpan Second, QueryEngineKind Engine, long BuildMilliseconds)> RouteOnceAsync(
        HypertrieGraphStore store,
        QueryEnginePolicy policy,
        BasicGraphPattern query)
    {
        QueryEngineRendezvous rendezvous = new(store, policy);
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        (long firstCount, TimeSpan firstElapsed) = await DrainQueryAsync(rendezvous, query, handler).ConfigureAwait(false);
        QueryEngineKind engine = selections.Count > 0 ? selections[^1].Engine : default;
        long buildMilliseconds = selections.Count > 0 ? selections[^1].Value : 0;

        (long secondCount, TimeSpan secondElapsed) = await DrainQueryAsync(rendezvous, query, handler).ConfigureAwait(false);

        //Both drains answer the same query on the same generation; the count is
        //the first's, the agreement of the two confirmed by the route line.
        long count = firstCount == secondCount ? firstCount : -1;

        return (count, firstElapsed, secondElapsed, engine, buildMilliseconds);
    }

    /// <summary>Drains the query once through the rendezvous, timing the whole end-to-end stream including any on-demand build, and counting solutions.</summary>
    /// <param name="rendezvous">The routing rendezvous.</param>
    /// <param name="query">The pattern.</param>
    /// <param name="handler">The selection-event sink.</param>
    /// <returns>The solution count and the wall time.</returns>
    private static async Task<(long Count, TimeSpan Elapsed)> DrainQueryAsync(QueryEngineRendezvous rendezvous, BasicGraphPattern query, TraceHandler<QueryTraceEvent> handler)
    {
        long start = Stopwatch.GetTimestamp();
        long count = 0;
        await foreach(Solution _ in rendezvous.QueryAsync(query, TimeProvider.System, traceHandler: handler).ConfigureAwait(false))
        {
            count++;
        }

        return (count, Stopwatch.GetElapsedTime(start));
    }

    /// <summary>A trace handler that appends every engine-selection event to the sink.</summary>
    /// <param name="sink">The receiving list.</param>
    /// <returns>The collecting handler.</returns>
    private static TraceHandler<QueryTraceEvent> CollectSelections(List<QueryTraceEvent> sink)
    {
        return (in QueryTraceEvent evt) =>
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                sink.Add(evt);
            }
        };
    }

    /// <summary>The worst-case-optimal count over self-index iterators: per level a track-max agreement via <c>Seek</c>, an <c>Open</c> descent on agreement, an <c>Up</c>-and-advance on exhaustion — iteratively.</summary>
    /// <param name="index">The self-index.</param>
    /// <param name="patterns">The patterns.</param>
    /// <param name="globalOrder">The global variable order.</param>
    /// <returns>The solution count.</returns>
    private static long CountLeapfrogSolutions(TripleSelfIndex index, TriplePattern[] patterns, Variable[] globalOrder)
    {
        SelfIndexTriejoinIterator[] iterators = new SelfIndexTriejoinIterator[patterns.Length];
        for(int i = 0; i < patterns.Length; i++)
        {
            List<Variable> restriction = [];
            foreach(Variable variable in globalOrder)
            {
                foreach(Variable patternVariable in patterns[i].Variables())
                {
                    if(patternVariable == variable)
                    {
                        restriction.Add(variable);

                        break;
                    }
                }
            }

            iterators[i] = new SelfIndexTriejoinIterator(index, patterns[i], restriction);
        }

        List<SelfIndexTriejoinIterator>[] participants = new List<SelfIndexTriejoinIterator>[globalOrder.Length];
        for(int level = 0; level < globalOrder.Length; level++)
        {
            participants[level] = [];
            foreach(SelfIndexTriejoinIterator iterator in iterators)
            {
                foreach(Variable variable in iterator.VariableOrder)
                {
                    if(variable == globalOrder[level])
                    {
                        participants[level].Add(iterator);

                        break;
                    }
                }
            }
        }

        long solutions = 0;
        uint[] bindings = new uint[globalOrder.Length];
        int currentLevel = 0;
        bool entering = true;
        while(currentLevel >= 0)
        {
            List<SelfIndexTriejoinIterator> active = participants[currentLevel];
            bool found;
            uint key = 0;
            if(entering)
            {
                foreach(SelfIndexTriejoinIterator iterator in active)
                {
                    iterator.RestartCurrentLevel();
                }

                found = TryAgree(active, 0, out key);
            }
            else
            {
                found = bindings[currentLevel] != uint.MaxValue && TryAgree(active, bindings[currentLevel] + 1, out key);
            }

            if(!found)
            {
                currentLevel--;
                if(currentLevel >= 0)
                {
                    foreach(SelfIndexTriejoinIterator iterator in participants[currentLevel])
                    {
                        iterator.Up();
                    }

                    entering = false;
                }

                continue;
            }

            bindings[currentLevel] = key;
            if(currentLevel == globalOrder.Length - 1)
            {
                solutions++;
                entering = false;

                continue;
            }

            foreach(SelfIndexTriejoinIterator iterator in active)
            {
                iterator.Open(TermId.FromEncoded(key));
            }

            currentLevel++;
            entering = true;
        }

        return solutions;
    }

    /// <summary>The track-max agreement loop: lower-bounds every participant, then raises stragglers to the running maximum until all agree or one ends.</summary>
    /// <param name="active">The participants at the level.</param>
    /// <param name="lowerBound">The starting lower bound.</param>
    /// <param name="key">Receives the agreed key.</param>
    /// <returns><see langword="true"/> when all participants agree on a key.</returns>
    private static bool TryAgree(List<SelfIndexTriejoinIterator> active, uint lowerBound, out uint key)
    {
        key = 0;
        uint maxKey = 0;
        foreach(SelfIndexTriejoinIterator iterator in active)
        {
            iterator.Seek(TermId.FromEncoded(lowerBound));
            if(iterator.AtEnd)
            {
                return false;
            }

            maxKey = Math.Max(maxKey, iterator.Key.Encoded);
        }

        while(true)
        {
            bool stable = true;
            foreach(SelfIndexTriejoinIterator iterator in active)
            {
                if(iterator.Key.Encoded < maxKey)
                {
                    iterator.Seek(TermId.FromEncoded(maxKey));
                    if(iterator.AtEnd)
                    {
                        return false;
                    }

                    if(iterator.Key.Encoded > maxKey)
                    {
                        maxKey = iterator.Key.Encoded;
                        stable = false;
                    }
                }
            }

            if(stable)
            {
                key = maxKey;

                return true;
            }
        }
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

    /// <summary>The ceiling of the base-2 logarithm of <paramref name="value"/>, the bit width to address one of that many distinct identifiers — zero for a single value.</summary>
    /// <param name="value">The cardinality to address; one or greater.</param>
    /// <returns>The number of bits, <c>⌈log₂(value)⌉</c>.</returns>
    private static int CeilLog2(uint value)
    {
        if(value <= 1)
        {
            return 0;
        }

        return 32 - System.Numerics.BitOperations.LeadingZeroCount(value - 1);
    }

    /// <summary>Builds the self-index and both columnar encodings over the corpus and reports the anchor rows, footprint, build time, probe latencies, and seek latency.</summary>
    /// <param name="label">The corpus label for the output lines.</param>
    /// <param name="corpus">The triples.</param>
    /// <param name="shape">The corpus's information-theoretic shape, threaded from the builder.</param>
    private static void RunCorpus(string label, List<EncodedTriple> corpus, CorpusShape shape)
    {
        long start = Stopwatch.GetTimestamp();
        TripleSelfIndex selfIndex = TripleSelfIndex.Build(corpus);
        TimeSpan selfBuild = Stopwatch.GetElapsedTime(start);

        int tripleCount = selfIndex.Count;
        Console.WriteLine($"[self-index] corpus={label} triples={tripleCount:N0} maxNodeId={shape.MaxNodeId:N0} predicates={shape.PredicateCount:N0}");

        //Anchor 1: the raw triple at three 32-bit identifiers — the encoded
        //form with no structure exploited, the high-water mark every index
        //should beat.
        const double rawBits = 96.0;
        double rawMiB = rawBits / 8.0 * tripleCount / (1024.0 * 1024.0);
        Console.WriteLine($"[self-index]   raw triples (3x32-bit)          |                  | {rawMiB,7:F1} MiB | {rawBits,6:F1} bits/triple total");

        //Anchor 2: the information floor — the bits a triple needs to name two
        //nodes and one predicate from their alphabets, ignoring structure and
        //redundancy. Two node positions at ceil(log2(maxNodeId)) bits and one
        //predicate at ceil(log2(predicateCount)) bits.
        int nodeBits = CeilLog2(shape.MaxNodeId);
        int predicateBits = CeilLog2((uint)shape.PredicateCount);
        double floorBits = (2.0 * nodeBits) + predicateBits;
        double floorMiB = floorBits / 8.0 * tripleCount / (1024.0 * 1024.0);
        Console.WriteLine($"[self-index]   information floor               | 2*{nodeBits,2}+{predicateBits,2} bits     | {floorMiB,7:F1} MiB | {floorBits,6:F1} bits/triple total (2*ceil(log2 {shape.MaxNodeId:N0})+ceil(log2 {shape.PredicateCount:N0}))");

        double selfBits = (double)selfIndex.BitCount / tripleCount;
        double selfMiB = selfIndex.BitCount / 8.0 / (1024.0 * 1024.0);
        Console.WriteLine($"[self-index]   self-index (whole structure)    | build {selfBuild.TotalMilliseconds,8:F1} ms | {selfMiB,7:F1} MiB | {selfBits,6:F1} bits/triple total");

        foreach(ColumnarOrderSetMode mode in (ReadOnlySpan<ColumnarOrderSetMode>)[ColumnarOrderSetMode.AllSixOrders, ColumnarOrderSetMode.ThreeRotations])
        {
            foreach(ColumnarValueColumnEncoding encoding in (ReadOnlySpan<ColumnarValueColumnEncoding>)[ColumnarValueColumnEncoding.EliasFanoWhenMonotone, ColumnarValueColumnEncoding.FrameOfReference])
            {
                start = Stopwatch.GetTimestamp();
                ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(corpus, mode, encoding);
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
                Console.WriteLine($"[self-index]   columnar {mode,-14} {encoding,-22} | build {columnarBuild.TotalMilliseconds,8:F1} ms | {miB,7:F1} MiB | {bits,6:F1} bits/triple total");
            }
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
    /// <returns>The corpus, three edges per group, with its information-theoretic shape: node ids run 1..3·groups under a single predicate.</returns>
    private static (List<EncodedTriple> Corpus, CorpusShape Shape) BuildTriangleCorpus(int groups)
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

        CorpusShape shape = new(MaxNodeId: (uint)(groups * 3), PredicateCount: 1);

        return (corpus, shape);
    }

    /// <summary>Builds a community-clustered multi-predicate graph: sequential node ids in communities, most edges within the community, a few across, predicates drawn from a small set.</summary>
    /// <param name="nodes">The node count.</param>
    /// <param name="communitySize">Nodes per community.</param>
    /// <param name="outDegree">Out-edges per node.</param>
    /// <returns>The corpus with its information-theoretic shape: node ids run 1..nodes under up to 32 predicates.</returns>
    private static (List<EncodedTriple> Corpus, CorpusShape Shape) BuildCommunityCorpus(int nodes, int communitySize, int outDegree)
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

        //Subjects and targets both run 1..nodes; the predicate alphabet is the
        //fixed 32-value set the draw above samples.
        CorpusShape shape = new(MaxNodeId: (uint)nodes, PredicateCount: 32);

        return ([.. corpus], shape);
    }
}
