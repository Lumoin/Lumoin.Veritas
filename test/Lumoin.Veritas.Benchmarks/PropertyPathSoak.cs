using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Rdf;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Long-running soak target for the property-path evaluator at
/// scales where BenchmarkDotNet's iteration model is wasteful. Not
/// a <see cref="BenchmarkAttribute"/>-driven benchmark: runs as a
/// plain method in the host process so a profiler attached via PID
/// gets clean call-stack samples and process-wide metrics
/// (<c>GC.CollectionCount</c>,
/// <c>Process.GetCurrentProcess().WorkingSet64</c>) come out clean.
/// </summary>
/// <remarks>
/// <para>
/// Each <c>Run*</c> method builds the graph, materialises into both
/// stores, runs every applicable path shape three times against
/// each store, and prints min/median/max wall-clock plus per-cell
/// allocation, GC counts, and peak working-set deltas. The
/// reporting format is line-oriented so it can be collated into a
/// markdown table by hand.
/// </para>
/// </remarks>
internal static class PropertyPathSoak
{
    private const int ChainBranchEvery = 8;

    private const int SmallWorldNeighbours = 4;

    private const double SmallWorldRewireFraction = 0.05;

    private const int Repetitions = 3;

    /// <summary>
    /// Runs the chain-graph soak at the given size against both
    /// stores and all chain-applicable path shapes.
    /// </summary>
    public static async Task RunChainSoakAsync(int linkCount)
    {
        Console.WriteLine($"[path-soak] chain linkCount={linkCount:N0} branchEvery={ChainBranchEvery}");

        long beforeBuildWs = Process.GetCurrentProcess().WorkingSet64;
        EncodedTriple[] triples = SyntheticGraph.GenerateChain(linkCount, ChainBranchEvery, seed: 42);
        Console.WriteLine($"[path-soak]   generated {triples.Length:N0} triples");

        await RunOnBothStoresAsync(
            triples,
            beforeBuildWs,
            start: TermId.FromEncoded(50_000_000U),
            pathShapes: ["PlusFromStart", "AlternationPlus", "SequenceWithKleene"]).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the small-world soak at the given (approximate) total
    /// triple count.
    /// </summary>
    public static async Task RunSmallWorldSoakAsync(int approximateSize)
    {
        int nodeCount = approximateSize / (2 * SmallWorldNeighbours + 1);
        Console.WriteLine($"[path-soak] smallWorld nodeCount={nodeCount:N0} neighbours={SmallWorldNeighbours} rewire={SmallWorldRewireFraction}");

        long beforeBuildWs = Process.GetCurrentProcess().WorkingSet64;
        EncodedTriple[] triples = SyntheticGraph.GenerateSmallWorld(
            nodeCount, SmallWorldNeighbours, SmallWorldRewireFraction, seed: 42);
        Console.WriteLine($"[path-soak]   generated {triples.Length:N0} triples");

        await RunOnBothStoresAsync(
            triples,
            beforeBuildWs,
            start: TermId.FromEncoded(50_000_000U + (uint)(nodeCount / 3)),
            pathShapes: ["StarRandomStart", "AlternationPlus", "SequenceWithKleene"]).ConfigureAwait(false);
    }

    private static async Task RunOnBothStoresAsync(
        EncodedTriple[] triples,
        long beforeBuildWs,
        TermId start,
        string[] pathShapes)
    {
        SoakStatistics.ReportGraph(triples, $"path corpus {triples.Length:N0} triples");

        //Hypertrie is the production target. InMemoryGraphStore exists
        //as a correctness reference for hypertrie parity tests, not as
        //a production storage backend; its performance on path-evaluator
        //workloads is irrelevant to the AST walker's design decisions.
        //The 100k InMemory results captured in the prior soak run remain
        //in the report for context but are not refreshed at larger sizes.
        long hypertrieBuildStart = Stopwatch.GetTimestamp();
        HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false);
        long hypertrieBuildElapsed = Stopwatch.GetElapsedTime(hypertrieBuildStart).Ticks;
        long afterHypertrieWs = Process.GetCurrentProcess().WorkingSet64;
        Console.WriteLine($"[path-soak]   Hypertrie build: {TimeSpan.FromTicks(hypertrieBuildElapsed).TotalMilliseconds:F1} ms; WS delta {(afterHypertrieWs - beforeBuildWs) / (1024 * 1024):N0} MB");

        GraphMatchOps hypertrieOps = hypertrie.AsMatchOps();
        foreach(string pathShape in pathShapes)
        {
            await MeasureCellAsync("Hypertrie", pathShape, start, hypertrieOps).ConfigureAwait(false);
        }
    }

    private static async Task MeasureCellAsync(string storeName, string pathShape, TermId start, GraphMatchOps ops)
    {
        PropertyPath path = BuildPath(pathShape);

        //One untimed warm-up run so first-touch JIT and any
        //evaluator-side lazy initialisation does not skew the first
        //timed iteration.
        int warmupCount = await DrainAsync(start, path, ops).ConfigureAwait(false);

        double[] millis = new double[Repetitions];
        long[] allocBytes = new long[Repetitions];
        int[] gen0Counts = new int[Repetitions];
        int[] gen1Counts = new int[Repetitions];
        int[] gen2Counts = new int[Repetitions];
        int lastCount = 0;

        for(int i = 0; i < Repetitions; i++)
        {
            long beforeAlloc = GC.GetTotalAllocatedBytes(precise: false);
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);

            long startTicks = Stopwatch.GetTimestamp();
            lastCount = await DrainAsync(start, path, ops).ConfigureAwait(false);
            long elapsed = Stopwatch.GetElapsedTime(startTicks).Ticks;

            millis[i] = TimeSpan.FromTicks(elapsed).TotalMilliseconds;
            allocBytes[i] = GC.GetTotalAllocatedBytes(precise: false) - beforeAlloc;
            gen0Counts[i] = GC.CollectionCount(0) - gen0Before;
            gen1Counts[i] = GC.CollectionCount(1) - gen1Before;
            gen2Counts[i] = GC.CollectionCount(2) - gen2Before;
        }

        long peakWs = Process.GetCurrentProcess().PeakWorkingSet64;
        double min = Math.Min(millis[0], Math.Min(millis[1], millis[2]));
        double max = Math.Max(millis[0], Math.Max(millis[1], millis[2]));
        double median = millis[0] + millis[1] + millis[2] - min - max;

        Console.WriteLine($"[path-soak]   {storeName,-9} {pathShape,-18} " +
            $"count={lastCount,12:N0}  " +
            $"min/median/max ms = {min,8:F1} / {median,8:F1} / {max,8:F1}  " +
            $"alloc(median) = {allocBytes[1] / (1024 * 1024),6:N0} MB  " +
            $"gc0/1/2 = {gen0Counts[1]}/{gen1Counts[1]}/{gen2Counts[1]}  " +
            $"peak WS = {peakWs / (1024 * 1024):N0} MB  " +
            $"warmup count = {warmupCount:N0}");
    }

    private static async Task<int> DrainAsync(TermId start, PropertyPath path, GraphMatchOps ops)
    {
        int count = 0;
        await foreach(TermId _ in PropertyPathEvaluator.EvaluateAsync(
            start, path, ops, CancellationToken.None).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    private static PropertyPath BuildPath(string pathShape)
    {
        IriId p = IriId.FromUnchecked(TermId.FromEncoded(SyntheticGraph.PathPredicateP));
        IriId q = IriId.FromUnchecked(TermId.FromEncoded(SyntheticGraph.PathPredicateQ));

        PredicatePath pPath = new(p);
        PredicatePath qPath = new(q);

        return pathShape switch
        {
            "PlusFromStart" => new OneOrMorePath(pPath),
            "StarRandomStart" => new ZeroOrMorePath(pPath),
            "AlternationPlus" => new OneOrMorePath(new AlternativePath([pPath, qPath])),
            "SequenceWithKleene" => new SequencePath([pPath, new ZeroOrMorePath(qPath)]),
            _ => throw new NotSupportedException($"Unknown PathShape '{pathShape}'."),
        };
    }
}
