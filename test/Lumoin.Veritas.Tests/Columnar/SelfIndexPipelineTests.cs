using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The self-index pipeline — the worst-case-optimal join driven over
/// <see cref="SelfIndexTriejoinIterator"/>s — is answer-identical to the
/// system-of-record leapfrog engine on cyclic (triangle), chain, and star
/// shapes, with bound positions and ground patterns included, and declines
/// exactly the shapes outside its contract (per-pattern self-joins, queries
/// binding no variables).
/// </summary>
[TestClass]
internal sealed class SelfIndexPipelineTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Drains a solution stream into order-insensitive per-solution fingerprints.</summary>
    /// <param name="solutions">The stream.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static async Task<List<string>> LeapfrogFingerprintsAsync(IAsyncEnumerable<Solution> solutions)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Flattens a pipeline batch stream into the same fingerprint form, each row's cells sorted by variable id.</summary>
    /// <param name="batches">The pipeline's batch stream (or <see langword="null"/>).</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> PipelineFingerprints(IEnumerable<SolutionBatch>? batches)
    {
        Assert.IsNotNull(batches, "The pipeline declined a shape it should answer.");

        List<string> fingerprints = [];
        foreach(SolutionBatch batch in batches)
        {
            for(int row = 0; row < batch.Count; row++)
            {
                List<string> cells = [];
                for(int column = 0; column < batch.Schema.Count; column++)
                {
                    cells.Add($"{batch.Schema[column].Id}={batch.ColumnOf(column)[row]}");
                }

                cells.Sort(StringComparer.Ordinal);
                fingerprints.Add(string.Join(";", cells));
            }
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Builds the self-index and the leapfrog store from the fixture, runs the query on both, and asserts the pipeline's answers equal leapfrog's.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="expectAnswers">Whether the oracle must return at least one solution.</param>
    private async Task AssertPipelineAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, bool expectAnswers = true)
    {
        TripleSelfIndex index = TripleSelfIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await LeapfrogFingerprintsAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> pipeline = PipelineFingerprints(SelfIndexPipeline.Run(index, query));

        if(expectAnswers)
        {
            Assert.IsGreaterThan(0, oracle.Count);
        }

        Assert.AreSequenceEqual(oracle, pipeline);
    }

    /// <summary>A directed graph over one predicate carrying some 3-cycles.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> TriangleFixture()
    {
        HashSet<EncodedTriple> seen = [];
        List<EncodedTriple> triples = [];
        for(uint i = 1; i < 15; i++)
        {
            for(uint j = 1; j < 15; j++)
            {
                if(i != j && (((i * 7) + (j * 3)) % 5) == 0 && seen.Add(EncodedTriple.FromEncoded(i, 200, j)))
                {
                    triples.Add(EncodedTriple.FromEncoded(i, 200, j));
                }
            }
        }

        return triples;
    }

    /// <summary>The triangle query <c>?x p ?y . ?y p ?z . ?z p ?x</c> — the cyclic shape a reduced rotation set cannot plan.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern TriangleQuery(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(x)),
            ],
            registry);
    }

    /// <summary>A connected chain fixture: each subject reaches an object on p1, and that object reaches a terminal on p2.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ChainFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 20; subject++)
        {
            uint @object = 100 + subject;
            triples.Add(EncodedTriple.FromEncoded(subject, 200, @object));
            triples.Add(EncodedTriple.FromEncoded(@object, 201, 500 + subject));
        }

        return triples;
    }

    /// <summary>The chain query <c>?s p1 ?o . ?o p2 ?t</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable t = registry.GetOrAdd("t");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(o), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(t)),
            ],
            registry);
    }

    /// <summary>The chain query with the first subject ground: <c>5 p1 ?o . ?o p2 ?t</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern BoundSubjectChainQuery(VariableRegistry registry)
    {
        Variable o = registry.GetOrAdd("o");
        Variable t = registry.GetOrAdd("t");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.Bound(TermId.FromEncoded(5)), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(o), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(t)),
            ],
            registry);
    }

    /// <summary>A star query over the triangle fixture's predicate with a bound object on one arm: <c>?k p ?b . ?k p 6</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern BoundObjectStarQuery(VariableRegistry registry)
    {
        Variable k = registry.GetOrAdd("k");
        Variable b = registry.GetOrAdd("b");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.Bound(TermId.FromEncoded(6))),
            ],
            registry);
    }

    [TestMethod]
    public async Task TriangleAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(TriangleFixture(), TriangleQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(ChainFixture(), ChainQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task BoundSubjectChainAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(ChainFixture(), BoundSubjectChainQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task BoundObjectStarAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(TriangleFixture(), BoundObjectStarQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task GroundPatternPresentKeepsTheJoin()
    {
        //The fully ground pattern exists, so the chain's answers survive it.
        List<EncodedTriple> fixture = ChainFixture();
        EncodedTriple present = fixture[0];

        await AssertPipelineAgreesWithLeapfrog(fixture, registry =>
        {
            Variable s = registry.GetOrAdd("s");
            Variable o = registry.GetOrAdd("o");

            return new BasicGraphPattern(
                [
                    new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                    new TriplePattern(PatternPosition.Bound(present.Subject), PatternPosition.Bound(present.Predicate), PatternPosition.Bound(present.Object)),
                ],
                registry);
        }).ConfigureAwait(false);
    }

    [TestMethod]
    public void GroundPatternAbsentEmptiesTheResult()
    {
        List<EncodedTriple> fixture = ChainFixture();
        TripleSelfIndex index = TripleSelfIndex.Build(fixture);
        VariableRegistry registry = new();
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        BasicGraphPattern query = new(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.Bound(TermId.FromEncoded(999_999)), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.Bound(TermId.FromEncoded(999_998))),
            ],
            registry);

        List<string> pipeline = PipelineFingerprints(SelfIndexPipeline.Run(index, query));

        Assert.IsEmpty(pipeline);
    }

    [TestMethod]
    public void DeclinesAPerPatternSelfJoin()
    {
        TripleSelfIndex index = TripleSelfIndex.Build(ChainFixture());
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        BasicGraphPattern query = new(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(x)),
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(y)),
            ],
            registry);

        Assert.IsNull(SelfIndexPipeline.Run(index, query));
    }

    /// <summary>Collects engine-selection trace events into the sink.</summary>
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

    [TestMethod]
    public async Task SelfIndexRouteAnswersTheTriangleUnderThreeRotations()
    {
        List<EncodedTriple> fixture = TriangleFixture();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = TriangleQuery(new VariableRegistry());

        //The all-orders default is the answer oracle.
        QueryEngineRendezvous allOrders = new(store, QueryEnginePolicy.Default);
        List<string> oracle = await LeapfrogFingerprintsAsync(allOrders.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        Assert.IsGreaterThan(0, oracle.Count);

        //Three rotations without the flag: the cyclic shape falls back to the
        //system of record, announced as rotation-incompatible.
        QueryEnginePolicy threeRotations = QueryEnginePolicy.Default with { OrderSetMode = ColumnarOrderSetMode.ThreeRotations };
        List<QueryTraceEvent> fallbackSelections = [];
        QueryEngineRendezvous fallback = new(store, threeRotations);
        List<string> viaFallback = await LeapfrogFingerprintsAsync(fallback.QueryAsync(query, TimeProvider.System, traceHandler: CollectSelections(fallbackSelections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreSequenceEqual(oracle, viaFallback);
        Assert.AreEqual(QueryEngineKind.Hypertrie, fallbackSelections[^1].Engine);
        Assert.AreEqual(EngineSelectionReason.RotationIncompatible, fallbackSelections[^1].SelectionReason);

        //Three rotations with the flag: the self-index answers identically,
        //building on first demand and reusing after.
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);
        QueryEngineRendezvous selfIndexed = new(store, threeRotations with { PreferSelfIndex = true });
        List<string> first = await LeapfrogFingerprintsAsync(selfIndexed.QueryAsync(query, TimeProvider.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> second = await LeapfrogFingerprintsAsync(selfIndexed.QueryAsync(query, TimeProvider.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreSequenceEqual(oracle, first);
        Assert.AreSequenceEqual(oracle, second);
        Assert.HasCount(2, selections);
        Assert.AreEqual(QueryEngineKind.SelfIndex, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, selections[0].SelectionReason);
        Assert.AreEqual(QueryEngineKind.SelfIndex, selections[1].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[1].SelectionReason);
    }

    [TestMethod]
    public async Task SelfIndexViewDropsOnAdvanceAndRebuildsFresh()
    {
        List<EncodedTriple> fixture = TriangleFixture();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = TriangleQuery(new VariableRegistry());

        QueryEnginePolicy policy = QueryEnginePolicy.Default with { OrderSetMode = ColumnarOrderSetMode.ThreeRotations, PreferSelfIndex = true };
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);
        QueryEngineRendezvous rendezvous = new(store, policy);

        List<string> before = await LeapfrogFingerprintsAsync(rendezvous.QueryAsync(query, TimeProvider.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        //Commit: a fresh disjoint 3-cycle arrives; the successor store is
        //rebuilt the way a session commit would produce one, and the
        //rendezvous receives the journal's effective delta.
        EncodedTriple[] added =
        [
            EncodedTriple.FromEncoded(20, 200, 21),
            EncodedTriple.FromEncoded(21, 200, 22),
            EncodedTriple.FromEncoded(22, 200, 20),
        ];
        List<EncodedTriple> newTriples = [.. fixture, .. added];
        HypertrieGraphStore newStore = await HypertrieGraphStore.BuildAsync(newTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        rendezvous.Advance(newStore, added, []);

        List<string> after = await LeapfrogFingerprintsAsync(rendezvous.QueryAsync(query, TimeProvider.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> reference = await LeapfrogFingerprintsAsync(newStore.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreSequenceEqual(reference, after);
        Assert.HasCount(before.Count + 3, after);
        Assert.AreEqual(QueryEngineKind.SelfIndex, selections[^1].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, selections[^1].SelectionReason, "The self-index rebuilds after a commit rather than answering stale.");
    }

    [TestMethod]
    public void DeclinesAGroundOnlyQuery()
    {
        List<EncodedTriple> fixture = ChainFixture();
        TripleSelfIndex index = TripleSelfIndex.Build(fixture);
        EncodedTriple present = fixture[0];
        BasicGraphPattern query = new(
            [
                new TriplePattern(PatternPosition.Bound(present.Subject), PatternPosition.Bound(present.Predicate), PatternPosition.Bound(present.Object)),
                new TriplePattern(PatternPosition.Bound(present.Subject), PatternPosition.Bound(present.Predicate), PatternPosition.Bound(present.Object)),
            ],
            new VariableRegistry());

        Assert.IsNull(SelfIndexPipeline.Run(index, query));
    }
}
