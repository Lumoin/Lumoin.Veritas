using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The batched router's contract: acyclic shapes plan and run the
/// scan-and-hash pipeline with answers identical to leapfrog's;
/// cyclic, disjoint, and self-join shapes refuse the plan; the
/// rendezvous honours the policy flag, announces the batched kind,
/// and keeps access-controlled queries on the consulting drivers.
/// </summary>
[TestClass]
internal sealed class ColumnarBatchPipelineTests
{
    public TestContext TestContext { get; set; } = null!;

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

    /// <summary>The fixture triples: chain-of-two join data plus a triangle over one predicate.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> Fixture()
    {
        List<EncodedTriple> triples = [];
        ulong state = 55;
        for(int i = 0; i < 3_000; i++)
        {
            state = Mix(state);
            uint subject = 100 + (uint)(state % 25);
            triples.Add(EncodedTriple.FromEncoded(subject, 200, 300 + (uint)((state >> 8) % 40)));
            triples.Add(EncodedTriple.FromEncoded(subject, 201, 400 + (uint)((state >> 16) % 15)));
        }

        triples.Add(EncodedTriple.FromEncoded(10, 200, 11));
        triples.Add(EncodedTriple.FromEncoded(11, 200, 12));
        triples.Add(EncodedTriple.FromEncoded(12, 200, 10));

        return triples;
    }

    /// <summary>A two-pattern acyclic join.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern AcyclicJoin(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2)),
            ],
            registry);
    }

    /// <summary>The triangle — cyclic, the router must refuse it.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern Triangle(VariableRegistry registry)
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

    /// <summary>A fan-out fixture for a three-pattern chain: a 200-edge and a 201-edge share the subject, and a 202-edge extends the 200-edge's object.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ChainFixture()
    {
        List<EncodedTriple> triples = [];
        ulong state = 91;
        for(int i = 0; i < 4_000; i++)
        {
            state = Mix(state);
            uint subject = 100 + (uint)(state % 30);
            uint @object = 300 + (uint)((state >> 8) % 50);
            triples.Add(EncodedTriple.FromEncoded(subject, 200, @object));
            triples.Add(EncodedTriple.FromEncoded(subject, 201, 400 + (uint)((state >> 16) % 20)));
            triples.Add(EncodedTriple.FromEncoded(@object, 202, 500 + (uint)((state >> 24) % 10)));
        }

        return triples;
    }

    /// <summary>A three-pattern acyclic chain: the subject joins the 200- and 201-edges, the object joins the 202-edge.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern ThreePatternChain(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");
        Variable t = registry.GetOrAdd("t");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2)),
                new TriplePattern(PatternPosition.OfVariable(o), PatternPosition.Bound(TermId.FromEncoded(202)), PatternPosition.OfVariable(t)),
            ],
            registry);
    }

    /// <summary>A star fixture: each subject carries several objects on each of three predicates, so the three-pattern join over the shared subject fans out.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarFixture()
    {
        List<EncodedTriple> triples = [];
        for(uint subject = 100; subject < 120; subject++)
        {
            for(uint i = 0; i < 6; i++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, 200, 300 + i));
                triples.Add(EncodedTriple.FromEncoded(subject, 201, 400 + i));
                triples.Add(EncodedTriple.FromEncoded(subject, 202, 500 + i));
            }
        }

        return triples;
    }

    /// <summary>A three-pattern star: every arm joins on the shared subject <c>?s</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");
        Variable o3 = registry.GetOrAdd("o3");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(o2)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(202)), PatternPosition.OfVariable(o3)),
            ],
            registry);
    }

    /// <summary>Drains a solution stream into order-insensitive fingerprints.</summary>
    /// <param name="solutions">The stream.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<Solution> solutions)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    [TestMethod]
    public void PlansAcceptAcyclicAndRefuseCyclicDisjointAndSelfJoins()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(Fixture());
        VariableRegistry registry = new();

        Assert.IsNotNull(ColumnarBatchPipeline.TryPlan(index, AcyclicJoin(registry)));
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, Triangle(new VariableRegistry())));

        //Disjoint components are cartesian products — refused.
        VariableRegistry disjointRegistry = new();
        Variable a = disjointRegistry.GetOrAdd("a");
        Variable b = disjointRegistry.GetOrAdd("b");
        Variable c = disjointRegistry.GetOrAdd("c");
        Variable d = disjointRegistry.GetOrAdd("d");
        BasicGraphPattern disjoint = new(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(d)),
            ],
            disjointRegistry);
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, disjoint));

        //A per-pattern self-join stays on the drivers that evaluate it.
        VariableRegistry selfRegistry = new();
        Variable v = selfRegistry.GetOrAdd("v");
        Variable w = selfRegistry.GetOrAdd("w");
        BasicGraphPattern selfJoin = new(
            [
                new TriplePattern(PatternPosition.OfVariable(v), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(v)),
                new TriplePattern(PatternPosition.OfVariable(v), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(w)),
            ],
            selfRegistry);
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, selfJoin));
    }

    [TestMethod]
    public async Task RoutedAcyclicJoinAgreesWithLeapfrogAndAnnouncesTheBatchedKind()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(Fixture(), VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEnginePolicy batchedPolicy = QueryEnginePolicy.Default with { PreferBatchedForAcyclic = true };
        QueryEngineRendezvous batched = new(store, batchedPolicy);

        //The oracle is pinned to the leapfrog driver on both shapes: the flags-verbatim selector takes no
        //shape engagement, and with the batched route disabled its only route is leapfrog.
        QueryEngineRendezvous leapfrog = new(store, QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false, JoinRouteSelector = JoinStrategySelectors.Manual });

        List<QueryEngineKind> kinds = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                kinds.Add(evt.Engine);
            }
        }

        List<string> viaBatched = await DrainAsync(batched.QueryAsync(
            AcyclicJoin(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaLeapfrog = await DrainAsync(leapfrog.QueryAsync(
            AcyclicJoin(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaLeapfrog.Count);
        Assert.AreSequenceEqual(viaLeapfrog, viaBatched);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, kinds[0]);

        //The triangle takes the Free Join generic join under the same policy — the cyclic core the batched
        //pipeline declines — and answers exactly what the leapfrog driver answers.
        kinds.Clear();
        List<string> triangleBatched = await DrainAsync(batched.QueryAsync(
            Triangle(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> triangleLeapfrog = await DrainAsync(leapfrog.QueryAsync(
            Triangle(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, triangleLeapfrog.Count);
        Assert.AreSequenceEqual(triangleLeapfrog, triangleBatched);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.FreeJoin, kinds[0]);
    }

    [TestMethod]
    public async Task SemijoinReductionAgreesWithStreamingAndLeapfrog()
    {
        List<EncodedTriple> fixture = ChainFixture();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        QueryEngineRendezvous reduced = new(store, QueryEnginePolicy.Default with { PreferSemijoinReduction = true });
        QueryEngineRendezvous streamed = new(store, QueryEnginePolicy.Default with { PreferSemijoinReduction = false });
        QueryEngineRendezvous leapfrog = new(store, QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false });

        List<QueryEngineKind> kinds = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                kinds.Add(evt.Engine);
            }
        }

        List<string> viaReduced = await DrainAsync(reduced.QueryAsync(
            ThreePatternChain(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaStreamed = await DrainAsync(streamed.QueryAsync(
            ThreePatternChain(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaLeapfrog = await DrainAsync(leapfrog.QueryAsync(
            ThreePatternChain(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaLeapfrog.Count);
        Assert.AreSequenceEqual(viaLeapfrog, viaReduced);
        Assert.AreSequenceEqual(viaLeapfrog, viaStreamed);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, kinds[0]);

        //The plan attaches a join tree exactly when reduction is enabled and the shape has three or more patterns.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(fixture);
        Assert.IsNotNull(ColumnarBatchPipeline.TryPlan(index, ThreePatternChain(new VariableRegistry()), useSemijoinReduction: true)!.JoinTree);
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, ThreePatternChain(new VariableRegistry()), useSemijoinReduction: false)!.JoinTree);

        //A two-pattern join stays on the unreduced stream even with reduction enabled.
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, AcyclicJoin(new VariableRegistry()), useSemijoinReduction: true)!.JoinTree);
    }

    [TestMethod]
    public async Task FactorizedStarAgreesWithStreamingAndLeapfrog()
    {
        List<EncodedTriple> fixture = StarFixture();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        QueryEngineRendezvous factorized = new(store, QueryEnginePolicy.Default with { PreferFactorizedStar = true });
        QueryEngineRendezvous streamed = new(store, QueryEnginePolicy.Default with { PreferFactorizedStar = false, PreferSemijoinReduction = false });
        QueryEngineRendezvous leapfrog = new(store, QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false });

        List<QueryEngineKind> kinds = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                kinds.Add(evt.Engine);
            }
        }

        List<string> viaFactorized = await DrainAsync(factorized.QueryAsync(
            StarQuery(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaStreamed = await DrainAsync(streamed.QueryAsync(
            StarQuery(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaLeapfrog = await DrainAsync(leapfrog.QueryAsync(
            StarQuery(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaLeapfrog.Count);
        Assert.AreSequenceEqual(viaLeapfrog, viaFactorized);
        Assert.AreSequenceEqual(viaLeapfrog, viaStreamed);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, kinds[0]);

        //The plan carries a star key exactly when the factorised-star policy is enabled and the shape is a star.
        ColumnarTripleIndex starIndex = ColumnarTripleIndex.Build(fixture);
        Assert.IsNotNull(ColumnarBatchPipeline.TryPlan(starIndex, StarQuery(new VariableRegistry()), useSemijoinReduction: false, useFactorizedStar: true)!.StarKey);
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(starIndex, StarQuery(new VariableRegistry()), useSemijoinReduction: false, useFactorizedStar: false)!.StarKey);

        //A chain is not a star: its third pattern joins on a branch variable, so the star key stays null even with the flag on.
        ColumnarTripleIndex chainIndex = ColumnarTripleIndex.Build(ChainFixture());
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(chainIndex, ThreePatternChain(new VariableRegistry()), useSemijoinReduction: false, useFactorizedStar: true)!.StarKey);
    }

    [TestMethod]
    public async Task FactorizedChainAgreesWithStreamingAndLeapfrog()
    {
        List<EncodedTriple> fixture = ChainFixture();
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        QueryEngineRendezvous factorized = new(store, QueryEnginePolicy.Default with { PreferFactorizedChain = true });
        QueryEngineRendezvous streamed = new(store, QueryEnginePolicy.Default with { PreferFactorizedChain = false, PreferSemijoinReduction = false });
        QueryEngineRendezvous leapfrog = new(store, QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false });

        List<QueryEngineKind> kinds = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                kinds.Add(evt.Engine);
            }
        }

        List<string> viaFactorized = await DrainAsync(factorized.QueryAsync(
            ThreePatternChain(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaStreamed = await DrainAsync(streamed.QueryAsync(
            ThreePatternChain(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaLeapfrog = await DrainAsync(leapfrog.QueryAsync(
            ThreePatternChain(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaLeapfrog.Count);
        Assert.AreSequenceEqual(viaLeapfrog, viaFactorized);
        Assert.AreSequenceEqual(viaLeapfrog, viaStreamed);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, kinds[0]);

        //The plan carries a chain nesting variable exactly when the policy is enabled and the shape is a chain.
        ColumnarTripleIndex chainIndex = ColumnarTripleIndex.Build(fixture);
        Assert.IsNotNull(ColumnarBatchPipeline.TryPlan(chainIndex, ThreePatternChain(new VariableRegistry()), useSemijoinReduction: false, useFactorizedChain: true)!.ChainNestVariable);
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(chainIndex, ThreePatternChain(new VariableRegistry()), useSemijoinReduction: false, useFactorizedChain: false)!.ChainNestVariable);

        //A star is not a chain: its third pattern shares the key, not a branch, so the nesting variable stays null.
        ColumnarTripleIndex starIndex = ColumnarTripleIndex.Build(StarFixture());
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(starIndex, StarQuery(new VariableRegistry()), useSemijoinReduction: false, useFactorizedChain: true)!.ChainNestVariable);
    }

    [TestMethod]
    public async Task AccessControlledQueriesStayOnTheConsultingDrivers()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(Fixture(), VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEngineRendezvous rendezvous = new(store, QueryEnginePolicy.Default with { PreferBatchedForAcyclic = true });

        List<QueryEngineKind> kinds = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                kinds.Add(evt.Engine);
            }
        }

        //An allow-everything policy still requires per-candidate
        //consultation, which the batched path has no point for.
        AccessContext context = new BatchTestAccessContext("user");
        _ = await DrainAsync(rendezvous.QueryAsync(
            AcyclicJoin(new VariableRegistry()), TimeProvider.System,
            accessControl: static (request, cancellationToken) => ValueTask.FromResult(AccessDecision.Allow),
            accessContext: context,
            traceHandler: Capture,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.Columnar, kinds[0]);
    }

    /// <summary>The pipeline's own gate and the selector's features read one acyclicity definition, so the two cannot drift apart on what "acyclic" means.</summary>
    [TestMethod]
    public void ThePipelineAndTheSelectorReadOneAcyclicityDefinition()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(Fixture());

        Assert.IsFalse(JoinShapeAnalysis.Describe(index, Triangle(new VariableRegistry()), QueryEnginePolicy.Default).Acyclic);
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, Triangle(new VariableRegistry())));

        Assert.IsTrue(JoinShapeAnalysis.Describe(index, AcyclicJoin(new VariableRegistry()), QueryEnginePolicy.Default).Acyclic);
        Assert.IsNotNull(ColumnarBatchPipeline.TryPlan(index, AcyclicJoin(new VariableRegistry())));
    }

    /// <summary>Both guards over a per-pattern self-join agree: the columnar path refuses the shape before it begins, and a direct plan request declines it too.</summary>
    [TestMethod]
    public void ASelfJoiningPatternNeverReachesTheColumnarPath()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(Fixture());

        Assert.IsFalse(QueryEngineRendezvous.IsColumnarCapable(SelfJoinBesideAnOrdinaryJoin(new VariableRegistry())));
        Assert.IsNull(ColumnarBatchPipeline.TryPlan(index, SelfJoinBesideAnOrdinaryJoin(new VariableRegistry())));
    }

    /// <summary>A per-pattern self-join beside an ordinary two-variable join.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern SelfJoinBesideAnOrdinaryJoin(VariableRegistry registry)
    {
        Variable v = registry.GetOrAdd("v");
        Variable w = registry.GetOrAdd("w");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(v), PatternPosition.Bound(TermId.FromEncoded(200)), PatternPosition.OfVariable(v)),
                new TriplePattern(PatternPosition.OfVariable(v), PatternPosition.Bound(TermId.FromEncoded(201)), PatternPosition.OfVariable(w))
            ],
            registry);
    }

    /// <summary>The opaque test access context.</summary>
    /// <param name="Subject">The requester's identity label.</param>
    private sealed record BatchTestAccessContext(string Subject): AccessContext;
}
