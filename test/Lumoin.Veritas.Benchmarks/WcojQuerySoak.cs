using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

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
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false);

        Console.WriteLine($"WCOJ query soak: {subjectCount} subjects, {triples.Length} triples, {duration.TotalSeconds:F0}s per shape.");

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

        await RunShapeAsync(store, "SinglePattern", singlePattern, duration).ConfigureAwait(false);
        await RunShapeAsync(store, "ThreePatternTriangle", trianglePattern, duration).ConfigureAwait(false);
    }

    private static async Task RunShapeAsync(HypertrieGraphStore store, string label, BasicGraphPattern pattern, TimeSpan duration)
    {
        long queries = 0;
        long rows = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        while(stopwatch.Elapsed < duration)
        {
            await foreach(Solution _ in store.Query(pattern, TimeProvider.System).ConfigureAwait(false))
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
