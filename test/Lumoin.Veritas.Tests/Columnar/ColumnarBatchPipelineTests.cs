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
        QueryEngineRendezvous leapfrog = new(store, QueryEnginePolicy.Default);

        List<QueryEngineKind> kinds = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                kinds.Add(evt.Engine);
            }
        }

        List<string> viaBatched = await DrainAsync(batched.Query(
            AcyclicJoin(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> viaLeapfrog = await DrainAsync(leapfrog.Query(
            AcyclicJoin(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, viaLeapfrog.Count);
        CollectionAssert.AreEqual(viaLeapfrog, viaBatched);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, kinds[0]);

        //The triangle stays on leapfrog under the same policy.
        kinds.Clear();
        List<string> triangleBatched = await DrainAsync(batched.Query(
            Triangle(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> triangleLeapfrog = await DrainAsync(leapfrog.Query(
            Triangle(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, triangleLeapfrog.Count);
        CollectionAssert.AreEqual(triangleLeapfrog, triangleBatched);
        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.Columnar, kinds[0]);
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
        _ = await DrainAsync(rendezvous.Query(
            AcyclicJoin(new VariableRegistry()), TimeProvider.System,
            accessControl: static (request, cancellationToken) => ValueTask.FromResult(AccessDecision.Allow),
            accessContext: context,
            traceHandler: Capture,
            cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, kinds);
        Assert.AreEqual(QueryEngineKind.Columnar, kinds[0]);
    }

    /// <summary>The opaque test access context.</summary>
    /// <param name="Subject">The requester's identity label.</param>
    private sealed record BatchTestAccessContext(string Subject): AccessContext;
}
