using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Measures end-to-end <see cref="HypertrieGraphStore.Query"/>
/// latency and allocation across pattern shapes and graph sizes.
/// </summary>
/// <remarks>
/// Three pattern shapes are exercised: a single-pattern lookup
/// (closer to the existing single-pattern <see cref="HypertrieGraphStore.Match"/>
/// path but routed through the WCOJ engine), a two-pattern join,
/// and a three-pattern triangle. Each shape is parameterised by
/// graph size to surface scaling behaviour.
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "BenchmarkDotNet instantiates this class via reflection.")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public types and members for its reflection-based runner.")]
public class QueryBenchmark
{
    private HypertrieGraphStore store = null!;

    private BasicGraphPattern singlePattern = null!;

    private BasicGraphPattern twoPatternJoin = null!;

    private BasicGraphPattern threePatternTriangle = null!;

    /// <summary>The number of distinct subjects in the synthetic graph.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int SubjectCount { get; set; }

    /// <summary>
    /// Per-benchmark setup — builds the graph and the BGPs once
    /// for all queries at this size. Async because the hypertrie
    /// build path enters the node store's async mutation gate;
    /// BenchmarkDotNet supports async <see cref="GlobalSetup"/>
    /// natively.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        EncodedTriple[] triples = SyntheticGraph.GenerateSocial(SubjectCount, seed: 42);
        store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default).ConfigureAwait(false);

        VariableRegistry r1 = new();
        Variable r1s = r1.GetOrAdd("s");
        Variable r1o = r1.GetOrAdd("o");

        TriplePattern singleP = new(
            PatternPosition.OfVariable(r1s),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(r1o));

        singlePattern = new([singleP], r1);

        VariableRegistry r2 = new();
        Variable r2x = r2.GetOrAdd("x");
        Variable r2y = r2.GetOrAdd("y");

        TriplePattern joinP1 = new(
            PatternPosition.OfVariable(r2x),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(r2y));

        TriplePattern joinP2 = new(
            PatternPosition.OfVariable(r2y),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.LivesInPredicate)),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.PopularCity)));

        twoPatternJoin = new([joinP1, joinP2], r2);

        VariableRegistry r3 = new();
        Variable r3a = r3.GetOrAdd("a");
        Variable r3b = r3.GetOrAdd("b");
        Variable r3c = r3.GetOrAdd("c");

        TriplePattern triP1 = new(
            PatternPosition.OfVariable(r3a),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(r3b));

        TriplePattern triP2 = new(
            PatternPosition.OfVariable(r3b),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(r3c));

        TriplePattern triP3 = new(
            PatternPosition.OfVariable(r3a),
            PatternPosition.Bound(TermId.FromEncoded(SyntheticGraph.KnowsPredicate)),
            PatternPosition.OfVariable(r3c));

        threePatternTriangle = new([triP1, triP2, triP3], r3);
    }

    /// <summary>One-pattern query enumeration. Measures the WCOJ engine's overhead at minimum complexity.</summary>
    [Benchmark]
    public async Task<int> SinglePattern()
    {
        int count = 0;

        await foreach(Solution _ in store.QueryAsync(singlePattern, VeritasClock.System).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    /// <summary>Two-pattern join with one shared variable. Most common WCOJ shape in real workloads.</summary>
    [Benchmark]
    public async Task<int> TwoPatternJoin()
    {
        int count = 0;

        await foreach(Solution _ in store.QueryAsync(twoPatternJoin, VeritasClock.System).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }

    /// <summary>Three-pattern triangle join. The classic WCOJ benchmark; this is where worst-case-optimal join algorithms beat hash-join.</summary>
    [Benchmark]
    public async Task<int> ThreePatternTriangle()
    {
        int count = 0;

        await foreach(Solution _ in store.QueryAsync(threePatternTriangle, VeritasClock.System).ConfigureAwait(false))
        {
            count++;
        }

        return count;
    }
}
