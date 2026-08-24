using System;
using System.Diagnostics;
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
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Profile-mode soak for the worst-case-optimal join driver: a
/// long-lived loop over the same query shapes
/// <see cref="QueryBenchmark"/> measures, in the host process, so
/// a sampling profiler (dotnet-trace, PerfView) sees real
/// call-stack samples without BenchmarkDotNet's
/// fork-per-iteration orchestration in the way.
/// </summary>
internal static class WcojQuerySoak
{
    /// <summary>
    /// Builds a social graph and loops the single-pattern and
    /// triangle queries for <paramref name="duration"/> each,
    /// printing solution-row throughput.
    /// </summary>
    /// <param name="subjectCount">The number of distinct subjects in the synthetic graph.</param>
    /// <param name="duration">How long to loop each query shape.</param>
    public static async Task RunQuerySoakAsync(int subjectCount, TimeSpan duration)
    {
        EncodedTriple[] triples = SyntheticGraph.GenerateSocial(subjectCount, seed: 42);
        SoakStatistics.ReportGraph(triples, $"social {subjectCount:N0} subjects");

        long heapBeforeHypertrie = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch hypertrieBuild = Stopwatch.StartNew();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false);
        hypertrieBuild.Stop();
        long heapAfterHypertrie = GC.GetTotalMemory(forceFullCollection: true);

        Stopwatch columnarBuild = Stopwatch.StartNew();
        ColumnarTripleIndex columnar = ColumnarTripleIndex.Build(triples);
        columnarBuild.Stop();
        long heapAfterColumnar = GC.GetTotalMemory(forceFullCollection: true);

        double hypertrieBytes = heapAfterHypertrie - heapBeforeHypertrie;
        double columnarBytes = heapAfterColumnar - heapAfterHypertrie;

        //The three-rotation memory profile, measured alongside: the
        //index is built and dropped — only its residency is read.
        ColumnarTripleIndex rotations = ColumnarTripleIndex.Build(triples, ColumnarOrderSetMode.ThreeRotations);
        long heapAfterRotations = GC.GetTotalMemory(forceFullCollection: true);
        double rotationBytes = heapAfterRotations - heapAfterColumnar;
        GC.KeepAlive(rotations);
        rotations = null!;

        Console.WriteLine($"WCOJ query soak: {subjectCount} subjects, {triples.Length} triples, {duration.TotalSeconds:F0}s per shape.");
        Console.WriteLine($"Build: hypertrie {hypertrieBuild.Elapsed.TotalMilliseconds:F1} ms, columnar {columnarBuild.Elapsed.TotalMilliseconds:F1} ms.");
        Console.WriteLine(
            $"Heap residency: hypertrie {hypertrieBytes / (1024 * 1024):F1} MB ({hypertrieBytes / triples.Length:F0} B/triple), "
            + $"columnar {columnarBytes / (1024 * 1024):F1} MB ({columnarBytes / triples.Length:F0} B/triple), "
            + $"three-rotation {rotationBytes / (1024 * 1024):F1} MB ({rotationBytes / triples.Length:F0} B/triple).");

        VariableRegistry singleRegistry = new();
        Variable singleS = singleRegistry.GetOrAdd("s");
        Variable singleO = singleRegistry.GetOrAdd("o");

        TriplePattern singleP = new(
            PatternPosition.OfVariable(singleS),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(singleO));

        BasicGraphPattern singlePattern = new([singleP], singleRegistry);

        VariableRegistry triangleRegistry = new();
        Variable triangleA = triangleRegistry.GetOrAdd("a");
        Variable triangleB = triangleRegistry.GetOrAdd("b");
        Variable triangleC = triangleRegistry.GetOrAdd("c");

        TriplePattern triangleP1 = new(
            PatternPosition.OfVariable(triangleA),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(triangleB));

        TriplePattern triangleP2 = new(
            PatternPosition.OfVariable(triangleB),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(triangleC));

        TriplePattern triangleP3 = new(
            PatternPosition.OfVariable(triangleA),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(triangleC));

        BasicGraphPattern trianglePattern = new([triangleP1, triangleP2, triangleP3], triangleRegistry);

        await RunShapeAsync("hypertrie SinglePattern", () => store.QueryAsync(singlePattern, TimeProvider.System), duration).ConfigureAwait(false);
        await RunShapeAsync("columnar  SinglePattern", () => QueryColumnar(columnar, singlePattern), duration).ConfigureAwait(false);
        await RunShapeAsync("hypertrie ThreePatternTriangle", () => store.QueryAsync(trianglePattern, TimeProvider.System), duration).ConfigureAwait(false);
        await RunShapeAsync("columnar  ThreePatternTriangle", () => QueryColumnar(columnar, trianglePattern), duration).ConfigureAwait(false);

        int processorCount = Environment.ProcessorCount;

        await RunShapeAsync(
            "columnar  ThreePatternTriangle x8",
            () => ColumnarHyperCube.QueryAsync(columnar, trianglePattern, degreeOfParallelism: 8, TimeProvider.System),
            duration).ConfigureAwait(false);
        await RunShapeAsync(
            $"columnar  ThreePatternTriangle x{processorCount}",
            () => ColumnarHyperCube.QueryAsync(columnar, trianglePattern, processorCount, TimeProvider.System),
            duration).ConfigureAwait(false);

        //The acyclic shape the join leg targets: ?x knows ?y . ?y
        //livesIn ?city — a chain, GYO-acyclic, the batched
        //scan-and-hash pipeline's home. Leapfrog vs batched, same
        //answers, on the same columnar view.
        VariableRegistry chainRegistry = new();
        Variable chainX = chainRegistry.GetOrAdd("x");
        Variable chainY = chainRegistry.GetOrAdd("y");
        Variable chainCity = chainRegistry.GetOrAdd("city");

        TriplePattern chainKnows = new(
            PatternPosition.OfVariable(chainX),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(chainY));

        TriplePattern chainLives = new(
            PatternPosition.OfVariable(chainY),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.LivesInPredicate)),
            PatternPosition.OfVariable(chainCity));

        BasicGraphPattern chainPattern = new([chainKnows, chainLives], chainRegistry);
        ColumnarBatchPlan? chainPlan = ColumnarBatchPipeline.TryPlan(columnar, chainPattern);

        await RunShapeAsync("columnar  AcyclicChain leapfrog", () => QueryColumnar(columnar, chainPattern), duration).ConfigureAwait(false);
        await RunShapeAsync(
            chainPlan is null ? "columnar  AcyclicChain batched(UNPLANNED)" : "columnar  AcyclicChain batched",
            () => chainPlan is null
                ? QueryColumnar(columnar, chainPattern)
                : SolutionBatch.FlattenAsync(ColumnarBatchPipeline.Run(columnar, chainPlan, VeritasMemoryPool<uint>.Shared)),
            duration).ConfigureAwait(false);
    }

    private static System.Collections.Generic.IAsyncEnumerable<Solution> QueryColumnar(ColumnarTripleIndex index, BasicGraphPattern pattern)
    {
        ColumnarBasicGraphPatternEvaluator evaluator = new(index, pattern, Planners.FirstOccurrence(pattern), VeritasClock.System);

        return evaluator.EvaluateAsync();
    }

    private static async Task RunShapeAsync(string label, Func<System.Collections.Generic.IAsyncEnumerable<Solution>> query, TimeSpan duration)
    {
        long queries = 0;
        long rows = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        while(stopwatch.Elapsed < duration)
        {
            await foreach(Solution _ in query().ConfigureAwait(false))
            {
                rows++;
            }

            queries++;
        }

        stopwatch.Stop();

        double seconds = stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine($"{label}: {queries} queries, {rows} rows in {seconds:F1}s — {rows / seconds:N0} rows/s, {queries / seconds:F1} queries/s.");
    }
}
