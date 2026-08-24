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
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The Free Join pipeline — the reusable builder that scans the columnar index
/// into GHTs at their join-cover depths and drives the generic join — is
/// answer-identical to the system-of-record leapfrog engine on a cyclic
/// (triangle) shape, which stays full-depth, and on acyclic (chain, star)
/// shapes, whose private tails drop to leaf vectors. This is the executor's
/// oracle exercised through the pipeline the rendezvous route consumes; a
/// mis-build or a wrong depth is caught, never a wrong answer.
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
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager by default.</param>
    private async Task AssertPipelineAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> oracle = await LeapfrogFingerprintsAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> pipeline = PipelineFingerprints(FreeJoinPipeline.Run(index, query, trieBuild));

        Assert.IsGreaterThan(0, oracle.Count);
        Assert.AreSequenceEqual(oracle, pipeline);
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

    [TestMethod]
    public async Task StarAgreesWithLeapfrog()
    {
        //Every satellite runs at join-cover depth one — a trie on the shared
        //centre, the private branch as a leaf vector, the binary-join shape.
        await AssertPipelineAgreesWithLeapfrog(StarFixture(), StarQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TriangleWithLazyTriesAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(TriangleFixture(), TriangleQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ChainWithLazyTriesAgreesWithLeapfrog()
    {
        await AssertPipelineAgreesWithLeapfrog(ChainFixture(), ChainQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task StarWithLazyTriesAgreesWithLeapfrog()
    {
        //The join-cover depths are unchanged by the build mode; only when a
        //satellite's map materialises moves.
        await AssertPipelineAgreesWithLeapfrog(StarFixture(), StarQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public void JoinCoverDepthCoversTheLastJoinVariableAndKeepsOneLevel()
    {
        VariableRegistry registry = new();
        Variable first = registry.GetOrAdd("first");
        Variable second = registry.GetOrAdd("second");

        //A star satellite: the shared variable leads, the private branch trails
        //into the leaf. A chain head: the join variable trails, so the cover
        //extends to full depth. A chain terminal mirrors the satellite. An
        //island with no join variable keeps its one mandatory level, and an
        //empty schema builds no level at all.
        Assert.AreEqual(1, FreeJoinPipeline.JoinCoverDepth([first, second], [0, 1], [first]));
        Assert.AreEqual(2, FreeJoinPipeline.JoinCoverDepth([first, second], [0, 1], [second]));
        Assert.AreEqual(1, FreeJoinPipeline.JoinCoverDepth([second, first], [1, 0], [first]));
        Assert.AreEqual(1, FreeJoinPipeline.JoinCoverDepth([first, second], [0, 1], []));
        Assert.AreEqual(0, FreeJoinPipeline.JoinCoverDepth([], [], [first]));
    }

    /// <summary>Builds a rendezvous over the store with the given policy, runs the query, and returns the sorted answer fingerprints.</summary>
    /// <param name="store">The system of record.</param>
    /// <param name="policy">The engine policy.</param>
    /// <param name="query">The query.</param>
    /// <returns>The sorted answer fingerprints.</returns>
    private async Task<List<string>> RendezvousFingerprintsAsync(HypertrieGraphStore store, QueryEnginePolicy policy, BasicGraphPattern query)
    {
        QueryEngineRendezvous rendezvous = new(store, policy);

        return await LeapfrogFingerprintsAsync(rendezvous.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
    }

    /// <summary>With <c>PreferFreeJoin</c> set, the rendezvous routes the query through the Free Join engine and answers identically to the default routing (batched/leapfrog) — the answer-identity that makes the route safe to flip.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="trieBuild">The trie build mode the Free Join policy carries; eager by default.</param>
    private async Task AssertFreeJoinRouteAgreesWithDefault(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        List<string> viaDefault = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default, query).ConfigureAwait(false);
        List<string> viaFreeJoin = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default with { PreferFreeJoin = true, FreeJoinTrieBuild = trieBuild }, query).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaDefault.Count);
        Assert.AreSequenceEqual(viaDefault, viaFreeJoin);
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

    [TestMethod]
    public async Task FreeJoinRouteAgreesWithDefaultOnStar()
    {
        await AssertFreeJoinRouteAgreesWithDefault(StarFixture(), StarQuery).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FreeJoinLazyRouteAgreesWithDefaultOnTriangle()
    {
        //The policy carries the lazy build through the rendezvous to the
        //pipeline; the route stays answer-identical to the default engines.
        await AssertFreeJoinRouteAgreesWithDefault(TriangleFixture(), TriangleQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FreeJoinLazyRouteAgreesWithDefaultOnChain()
    {
        await AssertFreeJoinRouteAgreesWithDefault(ChainFixture(), ChainQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FreeJoinLazyRouteAgreesWithDefaultOnStar()
    {
        await AssertFreeJoinRouteAgreesWithDefault(StarFixture(), StarQuery, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    /// <summary>Builds the index and leapfrog store, runs the factorising star pipeline over relations at their join-cover depths, and asserts its flattened answers equal leapfrog's and the flat pipeline's; when compression is expected, asserts the stored tuple count is below the flat-row count. Compression is depth-invariant — the groups are the keys every centre's root holds and the stored tuples are the distinct extent sizes — so the expectation means at cover depth exactly what it meant at full depth.</summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over a fresh registry.</param>
    /// <param name="expectCompression">Whether the factorisation should store strictly fewer tuples than the flat product.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps; eager by default.</param>
    private async Task AssertFactorizedAgreesWithLeapfrog(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, bool expectCompression, FreeJoinTrieBuild trieBuild = FreeJoinTrieBuild.Eager)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = queryBuilder(new VariableRegistry());

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch? factorized = FreeJoinPipeline.RunFactorized(index, query, arena, trieBuild);
        Assert.IsNotNull(factorized, "The pipeline returned no factorised plan for a factorisable shape.");

        List<string> oracle = await LeapfrogFingerprintsAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> flattened = PipelineFingerprints(factorized.Flatten());
        List<string> flat = PipelineFingerprints(FreeJoinPipeline.Run(index, query, trieBuild));

        Assert.IsGreaterThan(0, oracle.Count);
        Assert.AreSequenceEqual(oracle, flattened);
        Assert.AreSequenceEqual(flat, flattened);
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
    public async Task FactorizedStarWithLazyTriesAgreesWithLeapfrogAndCompresses()
    {
        //At cover depth a satellite's single root force materialises every leaf
        //row subset, so laziness prunes nothing there; the grouping and its
        //compression stay mode-independent all the same.
        await AssertFactorizedAgreesWithLeapfrog(StarFixture(), StarQuery, expectCompression: true, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
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

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        Assert.IsNull(FreeJoinPipeline.RunFactorized(index, query, arena));
    }

    /// <summary>A three-hop chain fixture <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c> with fan-out on every hop, so the factorised form compresses against the flat product.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ThreeHopChainFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint hub = 0; hub < 4; hub++)
        {
            uint x = 1_000 + hub;
            for(uint a = 0; a < 3; a++)
            {
                triples.Add(EncodedTriple.FromEncoded(2_000 + (hub * 10) + a, 210, x));
            }

            for(uint b = 0; b < 2; b++)
            {
                uint branchValue = 3_000 + (hub * 10) + b;
                triples.Add(EncodedTriple.FromEncoded(x, 211, branchValue));
                for(uint c = 0; c < 2; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branchValue, 212, 4_000 + (((hub * 10) + b) * 10) + c));
                }
            }
        }

        return triples;
    }

    /// <summary>The three-hop chain query <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c>; its centre is <c>?x</c> and the third pattern extends the <c>?b</c> branch.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ThreeHopChainQuery(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable x = registry.GetOrAdd("x");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(TermId.FromEncoded(210)), PatternPosition.OfVariable(x)),
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(211)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(212)), PatternPosition.OfVariable(c)),
            ],
            registry);
    }

    [TestMethod]
    public async Task FactorizedThreeHopChainNestsTheExtendedBranchAndCompresses()
    {
        await AssertFactorizedAgreesWithLeapfrog(ThreeHopChainFixture(), ThreeHopChainQuery, expectCompression: true).ConfigureAwait(false);
    }

    /// <summary>A star-with-chain fixture: each subject fans out on two star arms and a third arm whose objects each fan out one hop further — the hybrid neither a pure star nor a pure chain covers.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarChainHybridFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 4; subject++)
        {
            for(uint j = 0; j < 2; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 220, 1_000 + (subject * 10) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, 221, 2_000 + (subject * 10) + j));
            }

            for(uint j = 0; j < 2; j++)
            {
                uint branchValue = 3_000 + (subject * 10) + j;
                triples.Add(EncodedTriple.FromEncoded(subject, 222, branchValue));
                for(uint c = 0; c < 2; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branchValue, 223, 4_000 + (((subject * 10) + j) * 10) + c));
                }
            }
        }

        return triples;
    }

    /// <summary>The hybrid query <c>?s p0 ?o0 . ?s p1 ?o1 . ?s p2 ?b . ?b p3 ?c</c> — a three-arm star whose third arm is extended one hop.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarChainHybridQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o0 = registry.GetOrAdd("o0");
        Variable o1 = registry.GetOrAdd("o1");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(220)), PatternPosition.OfVariable(o0)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(221)), PatternPosition.OfVariable(o1)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(222)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(223)), PatternPosition.OfVariable(c)),
            ],
            registry);
    }

    [TestMethod]
    public async Task FactorizedStarWithChainExtensionAgreesAndCompresses()
    {
        await AssertFactorizedAgreesWithLeapfrog(StarChainHybridFixture(), StarChainHybridQuery, expectCompression: true).ConfigureAwait(false);
    }

    /// <summary>A four-hop chain fixture <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c . ?c p4 ?d</c> with fan-out on every hop; it factorises around its middle variable with BOTH branches nested.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> FourHopChainFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint middle = 0; middle < 3; middle++)
        {
            uint b = 1_000 + middle;
            for(uint x = 0; x < 2; x++)
            {
                uint xValue = 2_000 + (middle * 10) + x;
                triples.Add(EncodedTriple.FromEncoded(xValue, 231, b));
                for(uint a = 0; a < 2; a++)
                {
                    triples.Add(EncodedTriple.FromEncoded(3_000 + (((middle * 10) + x) * 10) + a, 230, xValue));
                }
            }

            for(uint c = 0; c < 2; c++)
            {
                uint cValue = 4_000 + (middle * 10) + c;
                triples.Add(EncodedTriple.FromEncoded(b, 232, cValue));
                for(uint d = 0; d < 2; d++)
                {
                    triples.Add(EncodedTriple.FromEncoded(cValue, 233, 5_000 + (((middle * 10) + c) * 10) + d));
                }
            }
        }

        return triples;
    }

    /// <summary>The four-hop chain query <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c . ?c p4 ?d</c>; the planner centres it on <c>?b</c> and nests both the <c>?x</c> and <c>?c</c> branches.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern FourHopChainQuery(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable x = registry.GetOrAdd("x");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(TermId.FromEncoded(230)), PatternPosition.OfVariable(x)),
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(231)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(b), PatternPosition.Bound(TermId.FromEncoded(232)), PatternPosition.OfVariable(c)),
                new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.Bound(TermId.FromEncoded(233)), PatternPosition.OfVariable(d)),
            ],
            registry);
    }

    [TestMethod]
    public async Task FactorizedFourHopChainNestsBothBranchesAroundItsMiddle()
    {
        await AssertFactorizedAgreesWithLeapfrog(FourHopChainFixture(), FourHopChainQuery, expectCompression: true).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task FactorizedRouteWithLazyTriesAgreesOnTheStarChainHybrid()
    {
        //The nested shape under the lazy build: the extended centre stays a
        //two-level trie whose second level forces on first touch, while the
        //cover-depth relations force once at the root.
        await AssertFactorizedAgreesWithLeapfrog(StarChainHybridFixture(), StarChainHybridQuery, expectCompression: true, FreeJoinTrieBuild.Lazy).ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts the factorised planner's order and the join-cover depth vector
    /// the route then builds by, both read through the pipeline's own rules
    /// rather than a test-side copy, so a drift in either would move the
    /// assertion instead of passing beside it.
    /// </summary>
    /// <param name="fixture">The fixture triples.</param>
    /// <param name="queryBuilder">Builds the query over the shared registry.</param>
    /// <param name="expectedOrder">The planned order's variable names, key first.</param>
    /// <param name="expectedDepths">The per-relation cover depth in pattern order.</param>
    private static void AssertFactorizedOrderAndCoverDepths(List<EncodedTriple> fixture, Func<VariableRegistry, BasicGraphPattern> queryBuilder, string[] expectedOrder, int[] expectedDepths)
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        VariableRegistry registry = new();
        BasicGraphPattern query = queryBuilder(registry);

        Assert.IsTrue(FreeJoinPipeline.TryPlanFactorizedOrder(index, query, out IReadOnlyList<Variable>? order), "The planner declined a factorisable shape.");

        List<Variable> expected = [];
        foreach(string name in expectedOrder)
        {
            expected.Add(registry.GetOrAdd(name));
        }

        Assert.AreSequenceEqual(expected, order);

        Dictionary<Variable, int> orderIndex = new(order.Count);
        for(int k = 0; k < order.Count; k++)
        {
            orderIndex[order[k]] = k;
        }

        HashSet<Variable> joinVariables = FreeJoinPipeline.JoinVariablesOf(index, query);
        int[] depths = new int[query.Patterns.Count];
        for(int pattern = 0; pattern < query.Patterns.Count; pattern++)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, query.Patterns[pattern]);
            depths[pattern] = FreeJoinPipeline.JoinCoverDepth(scanSchema, FreeJoinPipeline.OrderedColumns(scanSchema, orderIndex), joinVariables);
        }

        Assert.AreSequenceEqual(expectedDepths, depths);
    }

    [TestMethod]
    public void FactorizedOrderKeepsEveryNestedVariableInsideTheJoinCover()
    {
        //Every variable the factorised emission groups by is bound twice — the
        //key by its centres, an extended branch by its centre and its
        //extension — so the cover never stops short of a level the grouping
        //nests by, and a centre lands at depth two exactly when it is extended.
        //Hand-derived per fixture: the star nests nothing, so all three arms
        //take one level; the chain centres on its shared object with both arms
        //at one; the three-hop chain extends its second relation, which alone
        //takes two; the hybrid does the same for its third arm; the four-hop
        //chain is the one fixture with TWO simultaneously extended branches,
        //and both land at two with both extensions at one.
        AssertFactorizedOrderAndCoverDepths(StarFixture(), StarQuery, ["k", "b0", "b1", "b2"], [1, 1, 1]);
        AssertFactorizedOrderAndCoverDepths(ChainFixture(), ChainQuery, ["o", "s", "t"], [1, 1]);
        AssertFactorizedOrderAndCoverDepths(ThreeHopChainFixture(), ThreeHopChainQuery, ["x", "a", "b", "c"], [1, 2, 1]);
        AssertFactorizedOrderAndCoverDepths(StarChainHybridFixture(), StarChainHybridQuery, ["s", "o0", "o1", "b", "c"], [1, 1, 2, 1]);
        AssertFactorizedOrderAndCoverDepths(FourHopChainFixture(), FourHopChainQuery, ["b", "x", "c", "a", "d"], [1, 2, 2, 1]);
    }

    /// <summary>A three-arm star whose first arm concentrates enough matches on one centre to be built through its private tail while the other two stay at their cover depth — the mixed depth vector.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> MixedFanStarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 3; subject++)
        {
            for(uint j = 0; j < 10; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 240, 10_000 + (subject * 100) + j));
            }

            for(uint j = 0; j < 3; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 241, 20_000 + (subject * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, 242, 30_000 + (subject * 100) + j));
            }
        }

        return triples;
    }

    /// <summary>The mixed-fan star query <c>?k p0 ?b0 . ?k p1 ?b1 . ?k p2 ?b2</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern MixedFanStarQuery(VariableRegistry registry)
    {
        Variable k = registry.GetOrAdd("k");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(240)), PatternPosition.OfVariable(registry.GetOrAdd("b0"))),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(241)), PatternPosition.OfVariable(registry.GetOrAdd("b1"))),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(242)), PatternPosition.OfVariable(registry.GetOrAdd("b2")))
            ],
            registry);
    }

    /// <summary>A three-arm star whose every arm carries eight objects per centre, so every arm's heaviest key clears the depth rule's engagement fan.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> HighFanStarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 1; subject <= 3; subject++)
        {
            for(uint j = 0; j < 8; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 250, 10_000 + (subject * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, 251, 20_000 + (subject * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, 252, 30_000 + (subject * 100) + j));
            }
        }

        return triples;
    }

    /// <summary>The high-fan star query <c>?k p0 ?b0 . ?k p1 ?b1 . ?k p2 ?b2</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern HighFanStarQuery(VariableRegistry registry)
    {
        Variable k = registry.GetOrAdd("k");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(250)), PatternPosition.OfVariable(registry.GetOrAdd("b0"))),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(251)), PatternPosition.OfVariable(registry.GetOrAdd("b1"))),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(TermId.FromEncoded(252)), PatternPosition.OfVariable(registry.GetOrAdd("b2")))
            ],
            registry);
    }

    /// <summary>The trie depths a plan set carries, in pattern order.</summary>
    /// <param name="plans">The relation plans.</param>
    /// <returns>The depths in pattern order.</returns>
    private static int[] DepthsOf(FreeJoinRelationPlan[] plans)
    {
        int[] depths = new int[plans.Length];
        for(int plan = 0; plan < plans.Length; plan++)
        {
            depths[plan] = plans[plan].Depth;
        }

        return depths;
    }

    [TestMethod]
    public async Task AMixedDepthRunAnswersIdenticallyToTheJoinCoverRun()
    {
        List<EncodedTriple> fixture = MixedFanStarFixture();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = MixedFanStarQuery(new VariableRegistry());

        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index, query);

        Assert.IsNotNull(variableOrder, "The star has a global variable order on a six-order view.");

        //The cover-depth oracle is composed from the three surfaces the route itself composes — the
        //planner, the scan, and the trie build — so no depth and no column split is reimplemented here.
        FreeJoinRelationPlan[] coverPlans = FreeJoinPipeline.PlanRelations(index, query, variableOrder, FreeJoinPipeline.JoinVariablesOf(index, query), FreeJoinDepthRule.JoinCover);
        List<GeneralizedHashTrie> coverRelations = new(coverPlans.Length);
        for(int pattern = 0; pattern < coverPlans.Length; pattern++)
        {
            FreeJoinRelationPlan plan = coverPlans[pattern];
            coverRelations.Add(GeneralizedHashTrie.Build(plan.ScanSchema, ColumnarBatchScan.Scan(index, query.Patterns[pattern]), plan.Columns[..plan.Depth], plan.Columns[plan.Depth..]));
        }

        List<string> viaRoute = PipelineFingerprints(FreeJoinPipeline.Run(index, query));
        List<string> viaCover = PipelineFingerprints(FreeJoinExecutor.Execute(coverRelations, variableOrder));
        List<string> viaLeapfrog = await LeapfrogFingerprintsAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaRoute.Count);
        Assert.AreSequenceEqual(viaCover, viaRoute);
        Assert.AreSequenceEqual(viaLeapfrog, viaRoute);
    }

    [TestMethod]
    public void TheFactorizedOrdersPlanKeepsJoinCoverDepths()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(HighFanStarFixture());
        BasicGraphPattern query = HighFanStarQuery(new VariableRegistry());

        Assert.IsTrue(FreeJoinPipeline.TryPlanFactorizedOrder(index, query, out IReadOnlyList<Variable>? factorizedOrder), "The planner declined a factorisable shape.");

        //The factorised face keeps join-cover depths whatever the fan, so every arm stays one level deep.
        int[] cover = [1, 1, 1];

        Assert.AreSequenceEqual(cover, DepthsOf(FreeJoinPipeline.PlanRelations(index, query, factorizedOrder, FreeJoinPipeline.JoinVariablesOf(index, query), FreeJoinDepthRule.JoinCover)));

        using FactorizedArena arena = new(VeritasMemoryPool<uint>.Shared);
        FactorizedBatch? factorized = FreeJoinPipeline.RunFactorized(index, query, arena);

        Assert.IsNotNull(factorized, "The pipeline returned no factorised plan for a factorisable shape.");

        List<string> flattened = PipelineFingerprints(factorized.Flatten());
        List<string> flat = PipelineFingerprints(FreeJoinPipeline.Run(index, query));

        Assert.IsGreaterThan(0, flat.Count);
        Assert.AreSequenceEqual(flat, flattened);
    }

    /// <summary>The per-relation column vectors a plan set carries, in pattern order, as comparable strings.</summary>
    /// <param name="plans">The relation plans.</param>
    /// <returns>The column vectors in pattern order.</returns>
    private static List<string> ColumnsOf(FreeJoinRelationPlan[] plans)
    {
        List<string> columns = [];
        foreach(FreeJoinRelationPlan plan in plans)
        {
            columns.Add(string.Join(",", plan.Columns));
        }

        return columns;
    }

    [TestMethod]
    public async Task TryPlanYieldsTheRelationsAndOrderTheRunConsumes()
    {
        List<EncodedTriple> fixture = MixedFanStarFixture();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        BasicGraphPattern query = MixedFanStarQuery(new VariableRegistry());

        IReadOnlyList<Variable>? variableOrder = ColumnarRotationPlanner.TryPlanGlobalOrder(index, query);

        Assert.IsNotNull(variableOrder, "The star has a global variable order on a six-order view.");

        FreeJoinRelationPlan[] expected = FreeJoinPipeline.PlanRelations(index, query, variableOrder, FreeJoinPipeline.JoinVariablesOf(index, query), FreeJoinDepthRule.FanOutEngaged);
        FreeJoinPlan? plan = FreeJoinPipeline.TryPlan(index, query, FreeJoinDepthPolicy.Unspecified);

        Assert.IsNotNull(plan, "The planner declined a shape the flat route answers.");
        Assert.AreSequenceEqual(variableOrder, plan.Order);
        Assert.AreSequenceEqual(DepthsOf(expected), DepthsOf(plan.Relations));
        Assert.AreSequenceEqual(ColumnsOf(expected), ColumnsOf(plan.Relations));

        //The plan-taking drain and the query-taking one are the same run, and both are the oracle's answer.
        List<string> viaPlan = PipelineFingerprints(FreeJoinPipeline.Run(index, plan, FreeJoinTrieBuild.Eager));
        List<string> viaQuery = PipelineFingerprints(FreeJoinPipeline.Run(index, query, FreeJoinTrieBuild.Eager));
        List<string> viaLeapfrog = await LeapfrogFingerprintsAsync(store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaPlan.Count);
        Assert.AreSequenceEqual(viaQuery, viaPlan);
        Assert.AreSequenceEqual(viaLeapfrog, viaPlan);
    }

    [TestMethod]
    public void TryPlanDeclinesARotationIncompatibleShape()
    {
        //A cyclic shape under three rotations has no global descent order, so planning declines by value.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(TriangleFixture(), ColumnarOrderSetMode.ThreeRotations);
        BasicGraphPattern query = TriangleQuery(new VariableRegistry());

        Assert.IsNull(ColumnarRotationPlanner.TryPlanGlobalOrder(index, query), "The fixture must have no global order for this row to exercise the decline.");
        Assert.IsNull(FreeJoinPipeline.TryPlan(index, query, FreeJoinDepthPolicy.Unspecified));
    }

    [TestMethod]
    public void ThePlanSummaryCountsAndMasksTheEngagedRelations()
    {
        //The mixed star: the first arm's product clears the boundary and extends, the other two keep cover.
        //The tail-bearing count is the join-cover plan's own reading, so the engagement does not move it.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(MixedFanStarFixture());
        BasicGraphPattern query = MixedFanStarQuery(new VariableRegistry());

        FreeJoinPlan? plan = FreeJoinPipeline.TryPlan(index, query, FreeJoinDepthPolicy.Unspecified);

        Assert.IsNotNull(plan, "The planner declined a shape the flat route answers.");
        Assert.AreEqual(3, plan.RelationCount);
        Assert.AreEqual(1, plan.FullDepthRelationCount);
        Assert.AreEqual(3, plan.PlannedTailBearingRelationCount);
        Assert.AreEqual(1L, plan.FullDepthRelationMask);
    }

    [TestMethod]
    public void ADepthOverrideThreadsThroughTryPlanIntoEveryRelation()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(MixedFanStarFixture());
        BasicGraphPattern query = MixedFanStarQuery(new VariableRegistry());

        FreeJoinPlan? engine = FreeJoinPipeline.TryPlan(index, query, FreeJoinDepthPolicy.Unspecified);
        FreeJoinPlan? cover = FreeJoinPipeline.TryPlan(index, query, FreeJoinDepthPolicy.Cover);
        FreeJoinPlan? full = FreeJoinPipeline.TryPlan(index, query, FreeJoinDepthPolicy.Full);

        Assert.IsNotNull(engine);
        Assert.IsNotNull(cover);
        Assert.IsNotNull(full);

        //Unspecified is the engine's per-relation rule, not a concrete depth: only the engaged arm extends.
        int[] engineDepths = [2, 1, 1];
        int[] coverDepths = [1, 1, 1];
        int[] fullDepths = [2, 2, 2];

        Assert.AreSequenceEqual(engineDepths, DepthsOf(engine.Relations));
        Assert.AreSequenceEqual(coverDepths, DepthsOf(cover.Relations));
        Assert.AreSequenceEqual(fullDepths, DepthsOf(full.Relations));
    }
}
