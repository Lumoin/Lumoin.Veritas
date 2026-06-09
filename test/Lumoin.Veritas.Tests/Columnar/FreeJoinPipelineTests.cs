using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The Free Join pipeline — the reusable builder that scans the columnar index
/// into full-depth GHTs and drives the generic join — is answer-identical to the
/// system-of-record leapfrog engine, on a cyclic (triangle) and an acyclic
/// (chain) shape. This is the executor's oracle exercised through the pipeline a
/// rendezvous route will consume; a mis-build is caught, never a wrong answer.
/// </summary>
[TestClass]
internal sealed class FreeJoinPipelineTests
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

    /// <summary>Flattens a Free Join batch stream into the same fingerprint form, each row's cells sorted by variable id.</summary>
    /// <param name="batches">The pipeline's batch stream (or <see langword="null"/>).</param>
    /// <returns>The sorted fingerprints.</returns>
    private static List<string> PipelineFingerprints(IEnumerable<SolutionBatch>? batches)
    {
        Assert.IsNotNull(batches, "The pipeline returned no plan for a shape the index can answer.");

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

    /// <summary>Builds the index and the leapfrog store from the fixture, runs the query on both, and asserts the pipeline's answers equal leapfrog's.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    private async Task AssertPipelineAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await LeapfrogFingerprintsAsync(store.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> pipeline = PipelineFingerprints(FreeJoinPipeline.Run(index, query));

        Assert.IsGreaterThan(0, oracle.Count);
        CollectionAssert.AreEqual(oracle, pipeline);
    }

    /// <summary>A directed graph over one predicate carrying some 3-cycles.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> TriangleFixture()
    {
        HashSet<EncodedTriple> seen = [];
        List<EncodedTriple> triples = [];
        for(uint i = 0; i < 14; i++)
        {
            for(uint j = 0; j < 14; j++)
            {
                if(i != j && (((i * 7) + (j * 3)) % 5) == 0 && seen.Add(EncodedTriple.FromEncoded(i, 200, j)))
                {
                    triples.Add(EncodedTriple.FromEncoded(i, 200, j));
                }
            }
        }

        return triples;
    }

    /// <summary>The triangle query <c>?x p ?y . ?y p ?z . ?z p ?x</c>.</summary>
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

    /// <summary>A star over a centre subject: three predicates fan out to varying object counts per centre, so each centre's branches multiply where the factorised form keeps them apart.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint k = 1; k <= 6; k++)
        {
            for(uint j = 0; j < k; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(k, 200, 100 + j));
            }

            for(uint j = 0; j < 2; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(k, 201, 200 + j));
            }

            for(uint j = 0; j < 3; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(k, 202, 300 + j));
            }
        }

        return triples;
    }

    /// <summary>The star query <c>?k p0 ?b0 . ?k p1 ?b1 . ?k p2 ?b2</c> — three patterns sharing the centre.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        Variable k = registry.GetOrAdd("k");
        Variable b0 = registry.GetOrAdd("b0");
        Variable b1 = registry.GetOrAdd("b1");
        Variable b2 = registry.GetOrAdd("b2");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(b0)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(b1)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(202)), PatternPosition.OfVariable(b2)),
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

    /// <summary>Builds a rendezvous over the store with the given policy, runs the query, and returns the sorted answer fingerprints.</summary>
    /// <param name="store">The system of record.</param>
    /// <param name="policy">The engine policy.</param>
    /// <param name="query">The query.</param>
    /// <returns>The sorted answer fingerprints.</returns>
    private async Task<List<string>> RendezvousFingerprintsAsync(HypertrieGraphStore store, QueryEnginePolicy policy, BasicGraphPattern query)
    {
        QueryEngineRendezvous rendezvous = new(store, policy);

        return await LeapfrogFingerprintsAsync(rendezvous.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
    }

    /// <summary>With <c>PreferFreeJoin</c> set, the rendezvous routes the query through the Free Join engine and answers identically to the default routing (batched/leapfrog) — the answer-identity that makes the route safe to flip.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    private async Task AssertFreeJoinRouteAgreesWithDefault(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder)
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> viaDefault = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default, query).ConfigureAwait(false);
        List<string> viaFreeJoin = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default with { PreferFreeJoin = true }, query).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaDefault.Count);
        CollectionAssert.AreEqual(viaDefault, viaFreeJoin);
    }

    [TestMethod]
    public async Task FreeJoinRouteAgreesWithDefaultOnTriangle()
    {
        await AssertFreeJoinRouteAgreesWithDefault(TriangleFixture(), TriangleQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FreeJoinRouteAgreesWithDefaultOnChain()
    {
        await AssertFreeJoinRouteAgreesWithDefault(ChainFixture(), ChainQuery).ConfigureAwait(false);
    }

    /// <summary>Builds the index and leapfrog store, runs the factorising star pipeline, and asserts its flattened answers equal leapfrog's and the flat pipeline's; when compression is expected, asserts the stored tuple count is below the flat-row count.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="expectCompression">Whether the factorisation should store strictly fewer tuples than the flat product.</param>
    private async Task AssertFactorizedAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, bool expectCompression)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        FactorizedBatch? factorized = FreeJoinPipeline.RunFactorized(index, query);
        Assert.IsNotNull(factorized, "The pipeline returned no factorised plan for a single-key star.");

        List<string> oracle = await LeapfrogFingerprintsAsync(store.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> flattened = PipelineFingerprints(factorized.Flatten());
        List<string> flat = PipelineFingerprints(FreeJoinPipeline.Run(index, query));

        Assert.IsGreaterThan(0, oracle.Count);
        CollectionAssert.AreEqual(oracle, flattened);
        CollectionAssert.AreEqual(flat, flattened);
        Assert.AreEqual((long)oracle.Count, factorized.FlatRowCount);

        if(expectCompression)
        {
            Assert.IsLessThan(factorized.FlatRowCount, factorized.FactorizedTupleCount);
        }
    }

    [TestMethod]
    public async Task FactorizedStarAgreesWithLeapfrogAndCompresses()
    {
        await AssertFactorizedAgreesWithLeapfrog(StarFixture(), StarQuery, expectCompression: true).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FactorizedChainCentresOnTheSharedVariable()
    {
        await AssertFactorizedAgreesWithLeapfrog(ChainFixture(), ChainQuery, expectCompression: false).ConfigureAwait(false);
    }

    [TestMethod]
    public void FactorizedDeclinesAShapeWithNoSharedVariable()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(TriangleFixture());
        BasicGraphPattern query = TriangleQuery(new VariableRegistry());

        Assert.IsNull(FreeJoinPipeline.RunFactorized(index, query));
    }
}
