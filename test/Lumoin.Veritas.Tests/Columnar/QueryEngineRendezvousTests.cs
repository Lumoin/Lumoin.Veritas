using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// Tests for <see cref="QueryEngineRendezvous"/>: policy routing,
/// the <see cref="QueryTraceEventKind.EngineSelected"/> decision
/// side-channel, result equivalence across engines, and the
/// write-path <see cref="QueryEngineRendezvous.Advance"/> keeping
/// the derived view in step.
/// </summary>
[TestClass]
internal sealed class QueryEngineRendezvousTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static TermId Knows { get; } = TermId.FromEncoded(100);

    /// <summary>A single-pattern query routes to the system of record and says so on the bus.</summary>
    [TestMethod]
    public async Task SinglePatternRoutesToSystemOfRecord()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        BasicGraphPattern query = SinglePattern(registry);
        List<QueryTraceEvent> selections = [];

        List<Solution> solutions = await Drain(rendezvous.QueryAsync(
            query, VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.SystemOfRecord, selections[0].SelectionReason);
        Assert.HasCount(4, solutions);
    }

    /// <summary>The first join query builds the view (reporting its cost), the second reuses it; the acyclic two-pattern shape takes the batched pipeline under the default policy, and the build/reuse reasons flow through it unchanged.</summary>
    [TestMethod]
    public async Task JoinQueryBuildsThenReusesTheView()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        BasicGraphPattern query = TwoPatternJoin(registry);
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        await Drain(rendezvous.QueryAsync(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        await Drain(rendezvous.QueryAsync(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(2, selections);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, selections[0].SelectionReason);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[1].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[1].SelectionReason);
        Assert.AreEqual(0L, selections[1].Value, "A reused view reports zero build cost.");
        Assert.AreEqual(4, selections[1].Count, "The selection event carries the engine's triple count.");
    }

    /// <summary>A rendezvous seeded with a pre-built columnar view (a warm-loaded durable sidecar) serves the first join from that view with no build — the selection reports ViewReused, not ViewBuilt.</summary>
    [TestMethod]
    public async Task SeededViewServesTheFirstJoinWithoutBuilding()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        ColumnarTripleIndex warmView = ColumnarTripleIndex.Build(triples);
        QueryEngineRendezvous rendezvous = new(store, QueryEnginePolicy.Default, computeLane: null, initialView: warmView);

        List<QueryTraceEvent> selections = [];
        _ = await Drain(rendezvous.QueryAsync(
            TwoPatternJoin(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[0].SelectionReason, "The seeded view is used directly; the first query does not build it.");
    }

    /// <summary>The hypertrie-residency knob gates whether a present columnar view answers a single-pattern (sub-threshold) shape: Eager (the default) keeps it on the trie, Deferred answers it from the view — with results identical to the trie's, since correctness must not depend on the choice.</summary>
    [TestMethod]
    public async Task HypertrieResidencyGatesWhetherAViewAnswersASinglePattern()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        ColumnarTripleIndex warmView = ColumnarTripleIndex.Build(triples);

        //Eager (default): a seeded view does not divert a single pattern off the trie.
        QueryEngineRendezvous eager = new(store, QueryEnginePolicy.Default, computeLane: null, initialView: warmView);
        List<QueryTraceEvent> eagerSelections = [];
        List<Solution> viaTrie = await Drain(eager.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(eagerSelections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, eagerSelections);
        Assert.AreEqual(QueryEngineKind.Hypertrie, eagerSelections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.SystemOfRecord, eagerSelections[0].SelectionReason);

        //Deferred: the seeded view answers the single pattern without touching the trie.
        QueryEnginePolicy deferredPolicy = QueryEnginePolicy.Default with { HypertrieResidency = HypertrieResidency.Deferred };
        QueryEngineRendezvous deferred = new(store, deferredPolicy, computeLane: null, initialView: warmView);
        List<QueryTraceEvent> deferredSelections = [];
        List<Solution> viaView = await Drain(deferred.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(deferredSelections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, deferredSelections);
        Assert.AreEqual(QueryEngineKind.Columnar, deferredSelections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewReused, deferredSelections[0].SelectionReason);

        //Correctness must not depend on the residency choice: identical answers from both engines.
        Assert.HasCount(viaTrie.Count, viaView);
    }

    /// <summary>Both engines yield identical solution sets for the same join.</summary>
    [TestMethod]
    public async Task EnginesAgreeOnJoinResults()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        (QueryEngineRendezvous recordOnly, VariableRegistry recordRegistry) = await CreateRendezvousAsync(
            new QueryEnginePolicy(MinimumPatternsForColumnar: int.MaxValue, BuildViewOnDemand: false, DegreeOfParallelism: 1, OrderSetMode: ColumnarOrderSetMode.AllSixOrders)).ConfigureAwait(false);

        List<Solution> columnarSolutions = await Drain(rendezvous.QueryAsync(
            TwoPatternJoin(registry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<Solution> hypertrieSolutions = await Drain(recordOnly.QueryAsync(
            TwoPatternJoin(recordRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(hypertrieSolutions.Count, columnarSolutions);
    }

    /// <summary>Advance evolves the view by the commit delta; post-commit queries see the new data on the columnar engine.</summary>
    [TestMethod]
    public async Task AdvanceKeepsTheViewInStepWithTheWritePath()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        BasicGraphPattern query = TwoPatternJoin(registry);
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        List<Solution> before = await Drain(rendezvous.QueryAsync(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        //Commit: 4 knows 1 arrives, 1 knows 2 leaves. The successor
        //store is rebuilt here the way a session commit would
        //produce one; the rendezvous receives the same effective
        //delta the journal entry records.
        EncodedTriple added = EncodedTriple.FromEncoded(4, Knows.Encoded, 1);
        EncodedTriple removed = EncodedTriple.FromEncoded(1, Knows.Encoded, 2);
        EncodedTriple[] newTriples =
        [
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3),
            added,
        ];
        HypertrieGraphStore newStore = await HypertrieGraphStore
            .BuildAsync(newTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        rendezvous.Advance(newStore, [added], [removed]);

        List<Solution> after = await Drain(rendezvous.QueryAsync(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[^1].SelectionReason, "The advanced view answers without a rebuild.");
        Assert.AreNotEqual(before.Count, after.Count, "The commit changes the join's result set.");

        //The reference: the same query on the post-commit store.
        List<Solution> reference = await Drain(newStore.QueryAsync(query, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(reference.Count, after, "The advanced view agrees with the post-commit system of record.");
    }

    //Builds a rendezvous over a small knows-graph:
    //1→2, 2→3, 3→1, 1→3.
    private async Task<(QueryEngineRendezvous Rendezvous, VariableRegistry Registry)> CreateRendezvousAsync(QueryEnginePolicy? policy = null)
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        return (new QueryEngineRendezvous(store, policy ?? QueryEnginePolicy.Default), new VariableRegistry());
    }

    //(?s knows ?o): one pattern — stays on the system of record.
    private static BasicGraphPattern SinglePattern(VariableRegistry registry)
    {
        TriplePattern pattern = new(
            PatternPosition.OfVariable(registry.GetOrAdd("s")),
            PatternPosition.Bound(Knows),
            PatternPosition.OfVariable(registry.GetOrAdd("o")));

        return new BasicGraphPattern([pattern], registry);
    }

    //(?a knows ?b) . (?b knows ?c): two patterns — qualifies for
    //the columnar view under the default policy.
    private static BasicGraphPattern TwoPatternJoin(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        TriplePattern first = new(
            PatternPosition.OfVariable(a),
            PatternPosition.Bound(Knows),
            PatternPosition.OfVariable(b));
        TriplePattern second = new(
            PatternPosition.OfVariable(b),
            PatternPosition.Bound(Knows),
            PatternPosition.OfVariable(c));

        return new BasicGraphPattern([first, second], registry);
    }

    //A trace handler that records only the selection events.
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

    private static async Task<List<Solution>> Drain(IAsyncEnumerable<Solution> solutions)
    {
        List<Solution> drained = [];

        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            drained.Add(solution);
        }

        return drained;
    }

    /// <summary>Under deferred residency a warm-loaded view answers BOTH a single pattern and a multi-pattern join without ever materialising the trie — the warm serve-from-disk start.</summary>
    [TestMethod]
    public async Task DeferredWithWarmViewServesSinglePatternAndJoinWithoutBuildingTheTrie()
    {
        using VeritasMemoryPool<EncodedTriple> pool = new();
        QueryEngineRendezvous deferred = DeferredRendezvous(pool, KnowsTriangle(), withWarmView: true);

        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        List<Solution> single = await Drain(deferred.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<Solution> join = await Drain(deferred.QueryAsync(
            TwoPatternJoin(new VariableRegistry()), VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(4, single, "The single pattern is served from the warm view.");
        Assert.IsNotEmpty(join, "The join is served from the warm view.");
        Assert.IsFalse(deferred.IsTrieMaterialized, "Neither shape built the trie.");
        Assert.AreEqual(0, deferred.TrieBuildCount, "No deferred build ran.");
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine, "The single pattern answered on the columnar view, not the trie.");
    }

    /// <summary>Without a warm view, a deferred single-pattern query has nothing to serve from, so it materialises the trie — exactly once across repeated queries — and answers correctly.</summary>
    [TestMethod]
    public async Task DeferredWithoutWarmViewMaterialisesTheTrieOnceForASinglePattern()
    {
        using VeritasMemoryPool<EncodedTriple> pool = new();
        QueryEngineRendezvous deferred = DeferredRendezvous(pool, KnowsTriangle(), withWarmView: false);

        List<Solution> first = await Drain(deferred.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsTrue(deferred.IsTrieMaterialized, "With no warm view, the single pattern forced the trie to materialise.");
        Assert.AreEqual(1, deferred.TrieBuildCount, "The trie built exactly once.");
        Assert.HasCount(4, first, "Every knows triple matches ?s knows ?o.");

        List<Solution> second = await Drain(deferred.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(1, deferred.TrieBuildCount, "A second query reuses the materialised trie — no rebuild.");
        Assert.HasCount(4, second);
    }

    /// <summary>#14 step 1: an access-controlled SINGLE pattern serves from the warm view's per-candidate-consulting leapfrog driver — leak-free, since the output row IS the triple — so it no longer forces the trie under deferred residency; an access-controlled JOIN likewise serves from the view's consulting driver.</summary>
    [TestMethod]
    public async Task DeferredAccessControlledSinglePatternServesFromTheViewWithoutTheTrie()
    {
        using VeritasMemoryPool<EncodedTriple> pool = new();
        QueryEngineRendezvous deferred = DeferredRendezvous(pool, KnowsTriangle(), withWarmView: true);

        AccessContext context = new DeferredTestAccessContext("user");
        List<QueryTraceEvent> selections = [];

        List<Solution> aclSingle = await Drain(deferred.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, accessControl: AllowAll, accessContext: context, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsFalse(deferred.IsTrieMaterialized, "An access-controlled single pattern is served from the warm view, not the trie.");
        Assert.AreEqual(0, deferred.TrieBuildCount, "No trie build was forced by the access-controlled single pattern.");
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine, "The leapfrog columnar driver answered, consulting access control per candidate.");
        Assert.HasCount(4, aclSingle, "Allow-all yields every triple.");

        List<Solution> aclJoin = await Drain(deferred.QueryAsync(
            TwoPatternJoin(new VariableRegistry()), VeritasClock.System, accessControl: AllowAll, accessContext: context, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsNotEmpty(aclJoin, "The access-controlled join answers correctly from the view's consulting driver.");
        Assert.IsFalse(deferred.IsTrieMaterialized, "Neither the access-controlled single pattern nor the join forced the trie.");
    }

    /// <summary>A denying access-control policy filters an access-controlled single pattern leak-free on the warm view, and the surviving result is identical to the eager engine's (which routes the same query to the trie) — correctness must not depend on which engine consults the policy.</summary>
    [TestMethod]
    public async Task DeferredAccessControlledSinglePatternFiltersLeakFreeAndAgreesWithEager()
    {
        EncodedTriple[] data = KnowsTriangle();

        using VeritasMemoryPool<EncodedTriple> pool = new();
        QueryEngineRendezvous deferred = DeferredRendezvous(pool, data, withWarmView: true);
        AccessContext context = new DeferredTestAccessContext("user");

        List<Solution> viaView = await Drain(deferred.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, accessControl: DenyObjectThree, accessContext: context, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsFalse(deferred.IsTrieMaterialized, "The denied single pattern is filtered on the view, not the trie.");
        Assert.HasCount(2, viaView, "Two of the four knows triples have object != 3 (1->2, 3->1); the two with object 3 are denied.");

        //Oracle: the eager engine routes the same access-controlled single pattern to the trie; identical result.
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(data, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEngineRendezvous eager = new(store, QueryEnginePolicy.Default, computeLane: null, initialView: ColumnarTripleIndex.Build(data));
        List<Solution> viaTrie = await Drain(eager.QueryAsync(
            SinglePattern(new VariableRegistry()), VeritasClock.System, accessControl: DenyObjectThree, accessContext: context, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(viaTrie.Count, viaView, "The view (deferred) and trie (eager) agree on the access-controlled result.");
    }

    /// <summary>An access-controlled fully-bound single pattern — a membership check — is filtered on the warm view's constant-pattern consultation: a denied triple is hidden, an allowed one yields the single empty solution, neither materialising the trie.</summary>
    [TestMethod]
    public async Task DeferredAccessControlledMembershipIsFilteredOnTheViewWithoutTheTrie()
    {
        using VeritasMemoryPool<EncodedTriple> pool = new();
        QueryEngineRendezvous deferred = DeferredRendezvous(pool, KnowsTriangle(), withWarmView: true);
        AccessContext context = new DeferredTestAccessContext("user");

        //1 knows 3 exists, but its object is 3 -> denied -> hidden, with no trie build.
        List<Solution> denied = await Drain(deferred.QueryAsync(
            BoundTriple(TermId.FromEncoded(1), TermId.FromEncoded(3)), VeritasClock.System, accessControl: DenyObjectThree, accessContext: context, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsEmpty(denied, "The denied membership triple is hidden.");
        Assert.IsFalse(deferred.IsTrieMaterialized, "The membership check did not force the trie.");

        //1 knows 2 exists and is allowed -> the single empty solution.
        List<Solution> allowed = await Drain(deferred.QueryAsync(
            BoundTriple(TermId.FromEncoded(1), TermId.FromEncoded(2)), VeritasClock.System, accessControl: DenyObjectThree, accessContext: context, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, allowed, "The allowed membership triple yields the single empty solution.");
        Assert.IsFalse(deferred.IsTrieMaterialized, "Still no trie.");
    }

    /// <summary>Eager and deferred residency yield identical solution multisets across a mixed workload — correctness must not depend on the residency choice.</summary>
    [TestMethod]
    public async Task EagerAndDeferredAgreeAcrossAMixedWorkload()
    {
        EncodedTriple[] data =
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(5, Knows.Encoded, 5),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(data, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEngineRendezvous eager = new(store, QueryEnginePolicy.Default, computeLane: null, initialView: ColumnarTripleIndex.Build(data));

        using VeritasMemoryPool<EncodedTriple> warmPool = new();
        QueryEngineRendezvous deferredWarm = DeferredRendezvous(warmPool, data, withWarmView: true);

        using VeritasMemoryPool<EncodedTriple> coldPool = new();
        QueryEngineRendezvous deferredCold = DeferredRendezvous(coldPool, data, withWarmView: false);

        foreach(Func<VariableRegistry, BasicGraphPattern> shape in new Func<VariableRegistry, BasicGraphPattern>[] { SinglePattern, TwoPatternJoin })
        {
            List<Solution> eagerSolutions = await Drain(eager.QueryAsync(shape(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
            List<Solution> warmSolutions = await Drain(deferredWarm.QueryAsync(shape(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
            List<Solution> coldSolutions = await Drain(deferredCold.QueryAsync(shape(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            Assert.HasCount(eagerSolutions.Count, warmSolutions, "Eager and warm-view deferred agree on the shape's solution count.");
            Assert.HasCount(eagerSolutions.Count, coldSolutions, "Eager and no-view (trie-materialising) deferred agree on the shape's solution count.");
        }
    }

    /// <summary>Concurrent queries that all need the (unbuilt) trie share a single materialisation rather than each building one.</summary>
    [TestMethod]
    public async Task ConcurrentFirstTrieNeedQueriesShareASingleBuild()
    {
        using VeritasMemoryPool<EncodedTriple> pool = new();

        //No warm view: every single-pattern query falls to the trie, so all of them race for the first build.
        QueryEngineRendezvous deferred = DeferredRendezvous(pool, KnowsTriangle(), withWarmView: false);

        Task<List<Solution>>[] queries = Enumerable.Range(0, 32)
            .Select(_ => Drain(deferred.QueryAsync(SinglePattern(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)))
            .ToArray();

        List<Solution>[] results = await Task.WhenAll(queries).ConfigureAwait(false);

        Assert.AreEqual(1, deferred.TrieBuildCount, "Concurrent first-trie-need queries share a single build.");
        Assert.IsTrue(deferred.IsTrieMaterialized);
        foreach(List<Solution> result in results)
        {
            Assert.HasCount(4, result, "Every concurrent query sees the full result.");
        }
    }

    /// <summary>A faulted deferred build clears the in-flight slot, so a later caller re-kicks the build rather than awaiting a permanently poisoned task.</summary>
    [TestMethod]
    public async Task AFaultedDeferredBuildClearsTheSlotForRetry()
    {
        using VeritasMemoryPool<EncodedTriple> pool = new();
        DeferredTrieSource source = CreateDeferredSource(pool, KnowsTriangle());

        //Dispose the source so its build throws — the build-failure path under test.
        source.Dispose();

        QueryEnginePolicy policy = QueryEnginePolicy.Default with { HypertrieResidency = HypertrieResidency.Deferred };
        QueryEngineRendezvous deferred = new(store: null, policy, computeLane: null, initialView: null, deferredStore: source);

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await deferred.MaterializeTrieAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.IsFalse(deferred.IsTrieMaterialized, "A faulted build leaves the trie unmaterialised.");
        Assert.AreEqual(1, deferred.TrieBuildCount, "The first build attempt ran.");

        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            async () => await deferred.MaterializeTrieAsync(TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreEqual(2, deferred.TrieBuildCount, "The faulted build cleared the slot, so the second call re-kicked rather than awaiting the poisoned task.");
    }

    //An allow-everything access-control policy: still requires per-candidate consultation, so it forces the
    //consulting drivers (the trie for a sub-threshold shape).
    private static ValueTask<AccessDecision> AllowAll(AccessRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(AccessDecision.Allow);
    }

    //Denies any candidate triple whose object is term 3, allowing the rest — a content-dependent policy that
    //exercises real per-candidate filtering (not just an allow-everything pass).
    private static ValueTask<AccessDecision> DenyObjectThree(AccessRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(request.Triple.Object.Encoded == 3 ? AccessDecision.Deny : AccessDecision.Allow);
    }

    //(s knows o): a fully-bound single pattern — a membership check whose output row is the empty solution.
    private static BasicGraphPattern BoundTriple(TermId subject, TermId @object)
    {
        TriplePattern pattern = new(PatternPosition.Bound(subject), PatternPosition.Bound(Knows), PatternPosition.Bound(@object));

        return new BasicGraphPattern([pattern], new VariableRegistry());
    }

    //The knows-triangle 1→2, 2→3, 3→1, 1→3 (no self-loop), the small fixture the deferred tests share.
    private static EncodedTriple[] KnowsTriangle()
    {
        return
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
            EncodedTriple.FromEncoded(1, Knows.Encoded, 3),
        ];
    }

    //Builds a deferred-residency rendezvous over the triples, optionally seeded with a warm columnar view (the
    //sidecar). The source's buffer returns to pool when a build consumes it, so pool must outlive the rendezvous.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The deferred source's ownership transfers to the rendezvous, which disposes it after a build; an unbuilt source is reclaimed by GC.")]
    private static QueryEngineRendezvous DeferredRendezvous(VeritasMemoryPool<EncodedTriple> pool, EncodedTriple[] triples, bool withWarmView)
    {
        DeferredTrieSource source = CreateDeferredSource(pool, triples);
        ColumnarTripleIndex? warmView = withWarmView ? ColumnarTripleIndex.Build(triples) : null;
        QueryEnginePolicy policy = QueryEnginePolicy.Default with { HypertrieResidency = HypertrieResidency.Deferred };

        return new QueryEngineRendezvous(store: null, policy, computeLane: null, initialView: warmView, deferredStore: source);
    }

    //Wraps the triples in a pooled segment a deferred source takes ownership of.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The segment's ownership transfers to the returned DeferredTrieSource.")]
    private static DeferredTrieSource CreateDeferredSource(VeritasMemoryPool<EncodedTriple> pool, EncodedTriple[] triples)
    {
        IMemoryOwner<EncodedTriple> owner = pool.Rent(triples.Length);
        triples.CopyTo(owner.Memory.Span);

        return new DeferredTrieSource(new DecodedItemSegment(owner, triples.Length), VeritasHashing.Default);
    }

    /// <summary>A cyclic core takes the Free Join generic join under the shipped default, with answers identical to the leapfrog driver's.</summary>
    [TestMethod]
    public async Task CyclicJoinRoutesToFreeJoinUnderTheDefaultPolicy()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaSelector = await Drain(rendezvous.QueryAsync(
            Triangle(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous pinned, VariableRegistry pinnedRegistry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Manual }).ConfigureAwait(false);
        List<Solution> viaLeapfrog = await Drain(pinned.QueryAsync(
            Triangle(pinnedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.Structural, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.CyclicCore, selections[0].SelectionDecision.Reason);
        Assert.IsNotEmpty(viaLeapfrog, "The oracle answers the triangle.");
        Assert.AreSequenceEqual(Fingerprints(viaLeapfrog), Fingerprints(viaSelector), "The routed answers equal the leapfrog driver's.");
    }

    /// <summary>A disconnected (cartesian) shape takes the Free Join generic join under the shipped default, with answers identical to the leapfrog driver's.</summary>
    [TestMethod]
    public async Task DisconnectedJoinRoutesToFreeJoinUnderTheDefaultPolicy()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaSelector = await Drain(rendezvous.QueryAsync(
            DisjointJoin(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous pinned, VariableRegistry pinnedRegistry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Manual }).ConfigureAwait(false);
        List<Solution> viaLeapfrog = await Drain(pinned.QueryAsync(
            DisjointJoin(pinnedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(JoinSelectionReason.DisconnectedComponents, selections[0].SelectionDecision.Reason);
        Assert.IsNotEmpty(viaLeapfrog, "The oracle answers the cartesian shape.");
        Assert.AreSequenceEqual(Fingerprints(viaLeapfrog), Fingerprints(viaSelector), "The routed answers equal the leapfrog driver's cartesian answer.");
    }

    /// <summary>An acyclic connected shape keeps the measured batched default, and now says which selector kept it there.</summary>
    [TestMethod]
    public async Task AcyclicJoinKeepsTheBatchedRouteUnderTheDefaultPolicy()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            TwoPatternJoin(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].Engine);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].SelectionDecision.Route);
        Assert.AreEqual(JoinStrategySelectorKind.Structural, selections[0].SelectionDecision.SelectorKind);
    }

    /// <summary>An access-controlled query is never put to the seam: it goes to the driver that consults per candidate.</summary>
    [TestMethod]
    public async Task AccessControlledJoinIsNotPutToTheSelector()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];
        AccessContext context = new DeferredTestAccessContext("user");

        await Drain(rendezvous.QueryAsync(
            Triangle(registry), VeritasClock.System, accessControl: AllowAll, accessContext: context, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.None, selections[0].SelectionDecision.SelectorKind);
    }

    /// <summary>An explicit policy force outranks a deployment-supplied selector: the seam does not consult one at all.</summary>
    [TestMethod]
    public async Task ForcedFreeJoinBeatsASuppliedSelector()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { PreferFreeJoin = true, JoinRouteSelector = AlwaysBatched }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.Forced, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.PolicyForced, selections[0].SelectionDecision.Reason);
    }

    /// <summary>A supplied selector may route against the batched default: the engine enters the route it names and never clamps it.</summary>
    [TestMethod]
    public async Task ASuppliedSelectorRoutesAnAcyclicShapeToFreeJoin()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = AlwaysFreeJoin }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaFreeJoin = await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<Solution> viaBatched = await Drain(batched.QueryAsync(
            AcyclicStar(batchedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(SuppliedFreeJoinKind, selections[0].SelectionDecision.SelectorKind);
        Assert.IsNotEmpty(viaBatched, "The oracle answers the star on the batched route.");
        Assert.AreSequenceEqual(Fingerprints(viaBatched), Fingerprints(viaFreeJoin), "The against-batched route answers identically.");
    }

    /// <summary>A decision naming a route this seam does not serve falls through to the sound order, and the trace still records what was asked for.</summary>
    [TestMethod]
    public async Task ADecisionTheSeamCannotServeFallsBackToTheSoundRoute()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = AlwaysHypertrie }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].Engine);
        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[0].SelectionDecision.Route);
        Assert.AreEqual(SuppliedHypertrieKind, selections[0].SelectionDecision.SelectorKind, "The route half of an unpopulated decision is itself the system of record, so the deciding identity is what tells a recorded decision from a defaulted one.");
    }

    /// <summary>When the chosen route declines the shape, the trace names the route that ran and keeps the choice apart from it.</summary>
    [TestMethod]
    public async Task ADeclinedChoiceIsTracedAsTheRouteThatRan()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = AlwaysBatched }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            Triangle(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].SelectionDecision.Route);
    }

    /// <summary>The rotation gate runs before the seam: a cyclic shape under a reduced order set never reaches a selector.</summary>
    [TestMethod]
    public async Task ThreeRotationViewKeepsACyclicShapeOffTheSelector()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { OrderSetMode = ColumnarOrderSetMode.ThreeRotations }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            Triangle(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.RotationIncompatible, selections[0].SelectionReason);
        Assert.AreEqual(JoinStrategySelectorKind.None, selections[0].SelectionDecision.SelectorKind);
    }

    /// <summary>Every view-routed decision carries the features it was taken on, on all three routes, with each member equal to the shape's own value.</summary>
    [TestMethod]
    public async Task TheSelectionPayloadRidesEveryViewRoutedDecision()
    {
        (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> batchedSelections = [];
        await Drain(batched.QueryAsync(
            TwoPatternJoin(batchedRegistry), VeritasClock.System, traceHandler: CollectSelections(batchedSelections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous freeJoin, VariableRegistry freeJoinRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> freeJoinSelections = [];
        await Drain(freeJoin.QueryAsync(
            Triangle(freeJoinRegistry), VeritasClock.System, traceHandler: CollectSelections(freeJoinSelections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous leapfrog, VariableRegistry leapfrogRegistry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false }).ConfigureAwait(false);
        List<QueryTraceEvent> leapfrogSelections = [];
        await Drain(leapfrog.QueryAsync(
            TwoPatternJoin(leapfrogRegistry), VeritasClock.System, traceHandler: CollectSelections(leapfrogSelections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, batchedSelections);
        Assert.HasCount(1, freeJoinSelections);
        Assert.HasCount(1, leapfrogSelections);

        JoinSelectionFeatures batchedFeatures = batchedSelections[0].SelectionFeatures;
        Assert.AreEqual(2, batchedFeatures.PatternCount);
        Assert.AreEqual(4, batchedFeatures.ViewTripleCount);
        Assert.IsTrue(batchedFeatures.Acyclic);
        Assert.AreEqual(1, batchedFeatures.ComponentCount);
        Assert.AreEqual(ColumnarOrderSetMode.AllSixOrders, batchedFeatures.OrderSetMode);
        Assert.IsTrue(batchedFeatures.BatchedRouteEligible);

        JoinSelectionFeatures freeJoinFeatures = freeJoinSelections[0].SelectionFeatures;
        Assert.AreEqual(3, freeJoinFeatures.PatternCount);
        Assert.AreEqual(4, freeJoinFeatures.ViewTripleCount);
        Assert.IsFalse(freeJoinFeatures.Acyclic);
        Assert.AreEqual(1, freeJoinFeatures.ComponentCount);
        Assert.AreEqual(ColumnarOrderSetMode.AllSixOrders, freeJoinFeatures.OrderSetMode);
        Assert.IsTrue(freeJoinFeatures.BatchedRouteEligible);

        JoinSelectionFeatures leapfrogFeatures = leapfrogSelections[0].SelectionFeatures;
        Assert.AreEqual(2, leapfrogFeatures.PatternCount);
        Assert.AreEqual(4, leapfrogFeatures.ViewTripleCount);
        Assert.IsTrue(leapfrogFeatures.Acyclic);
        Assert.AreEqual(1, leapfrogFeatures.ComponentCount);
        Assert.AreEqual(ColumnarOrderSetMode.AllSixOrders, leapfrogFeatures.OrderSetMode);
        Assert.IsFalse(leapfrogFeatures.BatchedRouteEligible);
    }

    /// <summary>A supplied selector explicitly choosing the leapfrog driver is honoured on a shape the batched route could plan: the retry never upgrades it.</summary>
    [TestMethod]
    public async Task ASuppliedSelectorRoutesAnAcyclicShapeToLeapfrog()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = AlwaysColumnar, PreferBatchedForAcyclic = true }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaLeapfrog = await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<Solution> viaBatched = await Drain(batched.QueryAsync(
            AcyclicStar(batchedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].SelectionDecision.Route);
        Assert.AreEqual(SuppliedColumnarKind, selections[0].SelectionDecision.SelectorKind);
        Assert.IsNotEmpty(viaBatched, "The oracle answers the star on the batched route.");
        Assert.AreSequenceEqual(Fingerprints(viaBatched), Fingerprints(viaLeapfrog), "The explicitly chosen leapfrog route answers identically.");
    }

    /// <summary>A reduced order set keeps a disconnected shape with a cyclic component on the route it had before the selector shipped.</summary>
    [TestMethod]
    public async Task AReducedOrderSetKeepsADisconnectedCyclicShapeOnItsRoute()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { OrderSetMode = ColumnarOrderSetMode.ThreeRotations }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            DisjointTriangle(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[0].Engine, "The reduced-order route is the one this shape had before the selector shipped.");
        Assert.AreEqual(EngineSelectionReason.RotationIncompatible, selections[0].SelectionReason, "The reduced-order fallback announces itself as the rotation gate's, not the seam's.");
        Assert.AreNotEqual(QueryEngineKind.FreeJoin, selections[0].Engine, "No shape engagement reaches a reduced order set.");
    }

    /// <summary>The selection payload carries both halves of the skew signal on one event, so a consumer joining the bus sees the data reading and the structural companion the decision was taken beside.</summary>
    [TestMethod]
    public async Task TheSelectionPayloadCarriesTheSkewSignal()
    {
        EncodedTriple[] triples = FanEightStar();
        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEngineRendezvous rendezvous = new(store, QueryEnginePolicy.Default);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            ThreeArmStar(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);

        //Every hub carries eight objects on each arm, so the heaviest fan of the one join key is eight, its
        //degree-weighted mean over three keys of eight is 192 / 24 = 8.0, and a join-cover build leaves each
        //of the three arms a private tail.
        Assert.AreEqual(8, selections[0].SelectionFeatures.MaximumKeyFanOut);
        Assert.AreEqual(3, selections[0].SelectionFeatures.TailBearingRelationCount);
        Assert.AreEqual(8.0, selections[0].SelectionFeatures.DegreeWeightedMeanFanOut, 0.0001);
        Assert.AreNotEqual(JoinSelectionFeatures.UnreadableKeyFanOut, selections[0].SelectionFeatures.MaximumKeyFanOut);
        Assert.AreNotEqual(JoinSelectionFeatures.UnplannedTailBearingRelationCount, selections[0].SelectionFeatures.TailBearingRelationCount);
        Assert.AreEqual(3, selections[0].SelectionFeatures.PatternCount, "The payload is populated, so all three readings crossed the trace boundary.");
    }

    /// <summary>An explicit force outranks a contrary route hint: the operator outranks the caller.</summary>
    [TestMethod]
    public async Task AForcedRouteBeatsAContraryHint()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { PreferFreeJoin = true }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaForce = await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.Batched, default, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<Solution> viaBatched = await Drain(batched.QueryAsync(
            AcyclicStar(batchedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.Forced, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.PolicyForced, selections[0].SelectionDecision.Reason);
        Assert.IsNotEmpty(viaBatched, "The oracle answers the star on the batched route.");
        Assert.AreSequenceEqual(Fingerprints(viaBatched), Fingerprints(viaForce), "The forced route answers identically.");
    }

    /// <summary>A route hint outranks the selector, and every route the hint vocabulary names is one the seam serves.</summary>
    [TestMethod]
    public async Task ARouteHintBeatsTheSelector()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaHint = await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.FreeJoin, default, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<Solution> viaBatched = await Drain(batched.QueryAsync(
            AcyclicStar(batchedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine, "The structural rule would have kept this shape on the batched route.");
        Assert.AreEqual(JoinStrategySelectorKind.Hinted, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.HintedRoute, selections[0].SelectionDecision.Reason);
        Assert.AreSequenceEqual(Fingerprints(viaBatched), Fingerprints(viaHint));

        //The remaining vocabulary member: a hinted leapfrog runs the leapfrog driver on the same shape.
        (QueryEngineRendezvous leapfrog, VariableRegistry leapfrogRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> leapfrogSelections = [];
        List<Solution> viaLeapfrog = await Drain(leapfrog.QueryAsync(
            AcyclicStar(leapfrogRegistry), VeritasClock.System, traceHandler: CollectSelections(leapfrogSelections), hints: new JoinQueryHints(JoinRouteHintKind.Leapfrog, default, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, leapfrogSelections);
        Assert.AreEqual(QueryEngineKind.Columnar, leapfrogSelections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.Hinted, leapfrogSelections[0].SelectionDecision.SelectorKind);
        Assert.AreSequenceEqual(Fingerprints(viaBatched), Fingerprints(viaLeapfrog));
    }

    /// <summary>A hint is a preference, not a force: a route that declines the shape falls through, and the trace names the route that ran.</summary>
    [TestMethod]
    public async Task AnUnservableHintFallsThroughToTheRouteThatRan()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        List<Solution> viaFallThrough = await Drain(rendezvous.QueryAsync(
            Triangle(registry), VeritasClock.System, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.Batched, default, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous oracle, VariableRegistry oracleRegistry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Manual }).ConfigureAwait(false);
        List<Solution> viaLeapfrog = await Drain(oracle.QueryAsync(
            Triangle(oracleRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine, "The batched route declines a cyclic core, so the fall-through served.");
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].SelectionDecision.Route, "The decision keeps what was asked for.");
        Assert.IsNotEmpty(viaLeapfrog, "The oracle answers the triangle.");
        Assert.AreSequenceEqual(Fingerprints(viaLeapfrog), Fingerprints(viaFallThrough));
    }

    /// <summary>An access-controlled query is never put to the hints, exactly as it is never put to the selector.</summary>
    [TestMethod]
    public async Task AnAccessControlledQueryIgnoresHints()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];
        AccessContext context = new DeferredTestAccessContext("user");

        await Drain(rendezvous.QueryAsync(
            Triangle(registry), VeritasClock.System, accessControl: AllowAll, accessContext: context, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.FreeJoin, FreeJoinDepthPolicy.Full, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.None, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, selections[0].SelectionDecision.Depth, "No hint reaches an access-controlled query.");
        Assert.AreEqual(JoinSelectionHintedAxes.None, selections[0].SelectionDecision.HintedAxes);
    }

    /// <summary>A non-route hint overlays a selector-decided route: the kind and reason stay the selector's and the overlaid axis is named.</summary>
    [TestMethod]
    public async Task AHintedDepthRidesASelectorDecidedRoute()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            TwoPatternJoin(registry), VeritasClock.System, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.None, FreeJoinDepthPolicy.Full, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(JoinStrategySelectorKind.Structural, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(JoinSelectionReason.AcyclicBatched, selections[0].SelectionDecision.Reason);
        Assert.AreEqual(FreeJoinDepthPolicy.Full, selections[0].SelectionDecision.Depth);
        Assert.AreEqual(JoinSelectionHintedAxes.Depth, selections[0].SelectionDecision.HintedAxes);
    }

    /// <summary>A deployment-supplied selector that names no new axis keeps every route, answer, and emission the engine had before the axes existed.</summary>
    [TestMethod]
    public async Task ASuppliedSelectorWithUnspecifiedAxesPreservesTodaysBehaviour()
    {
        foreach((JoinStrategySelectorDelegate selector, QueryEngineKind expected) in ((JoinStrategySelectorDelegate, QueryEngineKind)[])[(AlwaysFreeJoin, QueryEngineKind.FreeJoin), (AlwaysBatched, QueryEngineKind.ColumnarBatched), (AlwaysColumnar, QueryEngineKind.Columnar)])
        {
            (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
                QueryEnginePolicy.Default with { JoinRouteSelector = selector }).ConfigureAwait(false);
            List<QueryTraceEvent> selections = [];

            List<Solution> viaSupplied = await Drain(rendezvous.QueryAsync(
                AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
            List<Solution> viaBatched = await Drain(batched.QueryAsync(
                AcyclicStar(batchedRegistry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

            Assert.HasCount(1, selections);
            Assert.AreEqual(expected, selections[0].Engine);
            Assert.AreEqual(FreeJoinDepthPolicy.Unspecified, selections[0].SelectionDecision.Depth);
            Assert.AreEqual(FreeJoinTrieBuildPreference.Unspecified, selections[0].SelectionDecision.Build);
            Assert.AreEqual(FactorizationEngagement.Unspecified, selections[0].SelectionDecision.Factorization);
            Assert.AreEqual(JoinSelectionHintedAxes.None, selections[0].SelectionDecision.HintedAxes);
            Assert.AreSequenceEqual(Fingerprints(viaBatched), Fingerprints(viaSupplied));
        }
    }

    /// <summary>A factorisation force reaches the decision's axis ahead of the statistics and ahead of a contrary hint.</summary>
    [TestMethod]
    public async Task AFactorizationFlagForcesItsEngagementRegardlessOfStatistics()
    {
        //The property-table shape: every arm's fan is one, so the calibrated statistics decline the
        //factorising route outright.
        QueryEnginePolicy forced = QueryEnginePolicy.Default with { PreferFactorizedStar = true, JoinRouteSelector = JoinStrategySelectors.Calibrated };
        QueryEngineRendezvous rendezvous = await RendezvousOverAsync(PropertyTableStar(), forced).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            ThreeArmStar(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(FactorizationEngagement.Star, selections[0].SelectionDecision.Factorization);

        //A contrary hint on the same axis loses to the force, and an axis a hint lost is not stamped.
        QueryEngineRendezvous contrary = await RendezvousOverAsync(PropertyTableStar(), forced).ConfigureAwait(false);
        List<QueryTraceEvent> contrarySelections = [];

        await Drain(contrary.QueryAsync(
            ThreeArmStar(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(contrarySelections), hints: new JoinQueryHints(JoinRouteHintKind.None, default, default, FactorizationEngagement.Chain), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, contrarySelections);
        Assert.AreEqual(FactorizationEngagement.Star, contrarySelections[0].SelectionDecision.Factorization);
        Assert.AreEqual(JoinSelectionHintedAxes.None, contrarySelections[0].SelectionDecision.HintedAxes, "Only an overlaid axis is stamped; a losing hint overlaid nothing.");
    }

    /// <summary>A Free-Join-routed query announces the depths its plan applied, exactly once, with the counts and mask the plan carries.</summary>
    [TestMethod]
    public async Task ThePlanAppliedEventCarriesTheRunsDepthOutcomes()
    {
        EncodedTriple[] triples = FanEightStar();
        QueryEngineRendezvous rendezvous = await RendezvousOverAsync(triples, QueryEnginePolicy.Default with { PreferFreeJoin = true }).ConfigureAwait(false);
        List<QueryTraceEvent> events = [];

        await Drain(rendezvous.QueryAsync(
            ThreeArmStar(new VariableRegistry()), VeritasClock.System, traceHandler: CollectAll(events), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        List<QueryTraceEvent> applied = EventsOfKind(events, QueryTraceEventKind.FreeJoinPlanApplied);

        Assert.HasCount(1, applied);

        FreeJoinPlan? plan = FreeJoinPipeline.TryPlan(ColumnarTripleIndex.Build(triples), ThreeArmStar(new VariableRegistry()), FreeJoinDepthPolicy.Unspecified);

        Assert.IsNotNull(plan);
        Assert.AreEqual(plan.RelationCount, applied[0].Count);
        Assert.AreEqual(plan.FullDepthRelationCount, applied[0].FullDepthRelationCount);
        Assert.AreEqual(plan.PlannedTailBearingRelationCount, applied[0].PlannedTailBearingRelationCount);
        Assert.AreEqual(plan.FullDepthRelationMask, applied[0].FullDepthRelationMask);
        Assert.AreEqual(3, applied[0].Count, "The three-arm star plans three relations.");
    }

    /// <summary>No other route plans Free Join relations, so none announces the event.</summary>
    [TestMethod]
    public async Task ThePlanAppliedEventStaysOffEveryOtherRoute()
    {
        (QueryEngineRendezvous batched, VariableRegistry batchedRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> batchedEvents = [];
        await Drain(batched.QueryAsync(TwoPatternJoin(batchedRegistry), VeritasClock.System, traceHandler: CollectAll(batchedEvents), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous leapfrog, VariableRegistry leapfrogRegistry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false }).ConfigureAwait(false);
        List<QueryTraceEvent> leapfrogEvents = [];
        await Drain(leapfrog.QueryAsync(TwoPatternJoin(leapfrogRegistry), VeritasClock.System, traceHandler: CollectAll(leapfrogEvents), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        (QueryEngineRendezvous hypertrie, VariableRegistry hypertrieRegistry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> hypertrieEvents = [];
        await Drain(hypertrie.QueryAsync(SinglePattern(hypertrieRegistry), VeritasClock.System, traceHandler: CollectAll(hypertrieEvents), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsEmpty(EventsOfKind(batchedEvents, QueryTraceEventKind.FreeJoinPlanApplied));
        Assert.IsEmpty(EventsOfKind(leapfrogEvents, QueryTraceEventKind.FreeJoinPlanApplied));
        Assert.IsEmpty(EventsOfKind(hypertrieEvents, QueryTraceEventKind.FreeJoinPlanApplied));
        Assert.IsNotEmpty(EventsOfKind(batchedEvents, QueryTraceEventKind.EngineSelected), "The batched query did announce its route, so the trace handler was wired.");
    }

    /// <summary>The count path takes the factorised face where the calibrated statistics justify it, and its count is the answer count.</summary>
    [TestMethod]
    public async Task TheCountPathRoutesToTheFactorisedFaceWhereStatisticsJustify()
    {
        EncodedTriple[] triples = FanEightStar();
        QueryEnginePolicy calibrated = QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Calibrated };
        QueryEngineRendezvous rendezvous = await RendezvousOverAsync(triples, calibrated).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        long? counted = rendezvous.TryCountBatched(
            pinnedStore: null, ThreeArmStar(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(selections));

        List<Solution> drained = await Drain(rendezvous.QueryAsync(
            ThreeArmStar(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        long? batchedCount = ColumnarBatchPipeline.TryCount(ColumnarTripleIndex.Build(triples), ThreeArmStar(new VariableRegistry()), VeritasMemoryPool<uint>.Shared);

        Assert.IsNotNull(counted);
        Assert.IsNotNull(batchedCount);
        Assert.AreEqual((long)drained.Count, counted.Value);
        Assert.AreEqual(batchedCount.Value, counted.Value);
        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine, "The factorised face served the count.");
        Assert.AreEqual(FactorizationEngagement.Star, selections[0].SelectionDecision.Factorization, "The count path emits what decided it.");
        Assert.AreEqual(3, selections[0].SelectionFeatures.PatternCount, "The count path emits the features it decided on.");
    }

    /// <summary>Where the statistics decline the face, the count path still answers on the batched count.</summary>
    [TestMethod]
    public async Task TheCountPathFallsBackWhereTheFaceDeclines()
    {
        EncodedTriple[] triples = PropertyTableStar();
        QueryEnginePolicy calibrated = QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Calibrated };
        QueryEngineRendezvous rendezvous = await RendezvousOverAsync(triples, calibrated).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        long? counted = rendezvous.TryCountBatched(
            pinnedStore: null, ThreeArmStar(new VariableRegistry()), VeritasClock.System, traceHandler: CollectSelections(selections));

        List<Solution> drained = await Drain(rendezvous.QueryAsync(
            ThreeArmStar(new VariableRegistry()), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsNotNull(counted);
        Assert.AreEqual((long)drained.Count, counted.Value);
        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[0].Engine, "The batched count path served the declining shape.");
    }

    /// <summary>The count path stays closed under access control, which the new engagement does not open.</summary>
    [TestMethod]
    public async Task TheCountPathStaysClosedUnderAccessControl()
    {
        QueryEnginePolicy calibrated = QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Calibrated };
        QueryEngineRendezvous rendezvous = await RendezvousOverAsync(FanEightStar(), calibrated).ConfigureAwait(false);

        Assert.IsNull(rendezvous.TryCountBatched(
            pinnedStore: null, ThreeArmStar(new VariableRegistry()), VeritasClock.System, accessControl: AllowAll));
    }

    /// <summary>The hint overlay reaches the non-route axes inside the force branch, where no selector is consulted at all.</summary>
    [TestMethod]
    public async Task HintsOverlayTheNonRouteAxesUnderAForce()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync(
            QueryEnginePolicy.Default with { PreferFreeJoin = true }).ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.None, FreeJoinDepthPolicy.Full, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.Forced, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(FreeJoinDepthPolicy.Full, selections[0].SelectionDecision.Depth);
        Assert.AreEqual(JoinSelectionHintedAxes.Depth, selections[0].SelectionDecision.HintedAxes);
    }

    /// <summary>The hint overlay reaches the non-route axes beside a route hint, and the stamp names both overlaid axes.</summary>
    [TestMethod]
    public async Task HintsOverlayTheNonRouteAxesBesideARouteHint()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        List<QueryTraceEvent> selections = [];

        await Drain(rendezvous.QueryAsync(
            AcyclicStar(registry), VeritasClock.System, traceHandler: CollectSelections(selections), hints: new JoinQueryHints(JoinRouteHintKind.FreeJoin, FreeJoinDepthPolicy.Full, default, default), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.FreeJoin, selections[0].Engine);
        Assert.AreEqual(JoinStrategySelectorKind.Hinted, selections[0].SelectionDecision.SelectorKind);
        Assert.AreEqual(FreeJoinDepthPolicy.Full, selections[0].SelectionDecision.Depth);
        Assert.AreEqual(JoinSelectionHintedAxes.Route | JoinSelectionHintedAxes.Depth, selections[0].SelectionDecision.HintedAxes);
    }

    /// <summary>Fifty hub subjects, each carrying exactly one object on each of three arm predicates: the property-table shape whose factorisation compresses nothing.</summary>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] PropertyTableStar()
    {
        List<EncodedTriple> triples = [];
        for(uint hub = 1; hub <= 50; hub++)
        {
            triples.Add(EncodedTriple.FromEncoded(hub, FirstArm.Encoded, 10_000 + hub));
            triples.Add(EncodedTriple.FromEncoded(hub, SecondArm.Encoded, 20_000 + hub));
            triples.Add(EncodedTriple.FromEncoded(hub, ThirdArm.Encoded, 30_000 + hub));
        }

        return [.. triples];
    }

    /// <summary>A rendezvous over the given triples and policy, with no warm view: the first qualifying query builds it.</summary>
    /// <param name="triples">The fixture triples.</param>
    /// <param name="policy">The engine policy.</param>
    /// <returns>The rendezvous.</returns>
    private async Task<QueryEngineRendezvous> RendezvousOverAsync(EncodedTriple[] triples, QueryEnginePolicy policy)
    {
        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        return new QueryEngineRendezvous(store, policy);
    }

    /// <summary>A trace handler that records every event, so a row can assert one kind's absence as well as its presence.</summary>
    /// <param name="sink">The list the handler appends to.</param>
    /// <returns>The handler.</returns>
    private static TraceHandler<QueryTraceEvent> CollectAll(List<QueryTraceEvent> sink)
    {
        return (in QueryTraceEvent evt) => sink.Add(evt);
    }

    /// <summary>The recorded events of one kind, in emission order.</summary>
    /// <param name="events">The recorded events.</param>
    /// <param name="kind">The kind sought.</param>
    /// <returns>The matching events.</returns>
    private static List<QueryTraceEvent> EventsOfKind(List<QueryTraceEvent> events, QueryTraceEventKind kind)
    {
        List<QueryTraceEvent> matching = [];
        foreach(QueryTraceEvent evt in events)
        {
            if(evt.Kind == kind)
            {
                matching.Add(evt);
            }
        }

        return matching;
    }

    //Three hub subjects, each carrying eight objects on each of three arm predicates: out-degree eight per
    //arm on every hub, and one join variable across the three arms.
    private static EncodedTriple[] FanEightStar()
    {
        List<EncodedTriple> triples = [];
        for(uint hub = 1; hub <= 3; hub++)
        {
            for(uint offset = 0; offset < 8; offset++)
            {
                triples.Add(EncodedTriple.FromEncoded(hub, FirstArm.Encoded, 10_000 + (hub * 100) + offset));
                triples.Add(EncodedTriple.FromEncoded(hub, SecondArm.Encoded, 20_000 + (hub * 100) + offset));
                triples.Add(EncodedTriple.FromEncoded(hub, ThirdArm.Encoded, 30_000 + (hub * 100) + offset));
            }
        }

        return [.. triples];
    }

    /// <summary>(?k firstArm ?b) . (?k secondArm ?c) . (?k thirdArm ?d): the three-arm star on one hub key.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ThreeArmStar(VariableRegistry registry)
    {
        Variable k = registry.GetOrAdd("k");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(FirstArm), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(SecondArm), PatternPosition.OfVariable(c)),
                new TriplePattern(PatternPosition.OfVariable(k), PatternPosition.Bound(ThirdArm), PatternPosition.OfVariable(d))
            ],
            registry);
    }

    /// <summary>The star's first arm predicate.</summary>
    private static TermId FirstArm { get; } = TermId.FromEncoded(300);

    /// <summary>The star's second arm predicate.</summary>
    private static TermId SecondArm { get; } = TermId.FromEncoded(301);

    /// <summary>The star's third arm predicate.</summary>
    private static TermId ThirdArm { get; } = TermId.FromEncoded(302);

    /// <summary>The telemetry identity the always-free-join test selector names itself with.</summary>
    private static JoinStrategySelectorKind SuppliedFreeJoinKind { get; } = JoinStrategySelectorKind.Create(2000);

    /// <summary>The telemetry identity the always-batched test selector names itself with.</summary>
    private static JoinStrategySelectorKind SuppliedBatchedKind { get; } = JoinStrategySelectorKind.Create(2001);

    /// <summary>The telemetry identity the always-hypertrie test selector names itself with.</summary>
    private static JoinStrategySelectorKind SuppliedHypertrieKind { get; } = JoinStrategySelectorKind.Create(2002);

    /// <summary>The telemetry identity the always-leapfrog test selector names itself with.</summary>
    private static JoinStrategySelectorKind SuppliedColumnarKind { get; } = JoinStrategySelectorKind.Create(2003);

    /// <summary>A deployment-supplied selector that always names the Free Join route; a static method group, so it captures nothing.</summary>
    private static JoinStrategySelectorDelegate AlwaysFreeJoin { get; } = SelectFreeJoin;

    /// <summary>A deployment-supplied selector that always names the batched route.</summary>
    private static JoinStrategySelectorDelegate AlwaysBatched { get; } = SelectBatched;

    /// <summary>A deployment-supplied selector that always names a route this seam does not serve.</summary>
    private static JoinStrategySelectorDelegate AlwaysHypertrie { get; } = SelectHypertrie;

    /// <summary>A deployment-supplied selector that always names the leapfrog driver.</summary>
    private static JoinStrategySelectorDelegate AlwaysColumnar { get; } = SelectColumnar;

    /// <summary>Names the Free Join route for every shape.</summary>
    /// <param name="context">The consultation context; ignored.</param>
    /// <param name="cancellationToken">The query's token; ignored.</param>
    /// <returns>The Free Join decision.</returns>
    private static JoinSelectionDecision SelectFreeJoin(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        return JoinSelectionDecision.Supplied(QueryEngineKind.FreeJoin, SuppliedFreeJoinKind);
    }

    /// <summary>Names the batched route for every shape.</summary>
    /// <param name="context">The consultation context; ignored.</param>
    /// <param name="cancellationToken">The query's token; ignored.</param>
    /// <returns>The batched decision.</returns>
    private static JoinSelectionDecision SelectBatched(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        return JoinSelectionDecision.Supplied(QueryEngineKind.ColumnarBatched, SuppliedBatchedKind);
    }

    /// <summary>Names the system of record — a route this seam does not serve — for every shape.</summary>
    /// <param name="context">The consultation context; ignored.</param>
    /// <param name="cancellationToken">The query's token; ignored.</param>
    /// <returns>The unserved decision.</returns>
    private static JoinSelectionDecision SelectHypertrie(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        return JoinSelectionDecision.Supplied(QueryEngineKind.Hypertrie, SuppliedHypertrieKind);
    }

    /// <summary>Names the leapfrog driver for every shape.</summary>
    /// <param name="context">The consultation context; ignored.</param>
    /// <param name="cancellationToken">The query's token; ignored.</param>
    /// <returns>The leapfrog decision.</returns>
    private static JoinSelectionDecision SelectColumnar(in JoinSelectionContext context, CancellationToken cancellationToken)
    {
        return JoinSelectionDecision.Supplied(QueryEngineKind.Columnar, SuppliedColumnarKind);
    }

    /// <summary>(?x knows ?y) . (?y knows ?z) . (?z knows ?x): the cyclic core the GYO reduction does not clear.</summary>
    /// <param name="registry">The variable registry the pattern binds into.</param>
    /// <returns>The triangle pattern.</returns>
    private static BasicGraphPattern Triangle(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(Knows), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(Knows), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(Knows), PatternPosition.OfVariable(x))
            ],
            registry);
    }

    /// <summary>(?a knows ?b) . (?c knows ?d): two components, a cartesian answer.</summary>
    /// <param name="registry">The variable registry the pattern binds into.</param>
    /// <returns>The disconnected two-pattern join.</returns>
    private static BasicGraphPattern DisjointJoin(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(c), PatternPosition.Bound(Knows), PatternPosition.OfVariable(d))
            ],
            registry);
    }

    /// <summary>A triangle beside an independent pattern: two components, one of them cyclic.</summary>
    /// <param name="registry">The variable registry the pattern binds into.</param>
    /// <returns>The disconnected shape with a cyclic component.</returns>
    private static BasicGraphPattern DisjointTriangle(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");
        Variable m = registry.GetOrAdd("m");
        Variable n = registry.GetOrAdd("n");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(Knows), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(Knows), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(Knows), PatternPosition.OfVariable(x)),
                new TriplePattern(PatternPosition.OfVariable(m), PatternPosition.Bound(Knows), PatternPosition.OfVariable(n))
            ],
            registry);
    }

    /// <summary>(?a knows ?b) . (?a knows ?c): the acyclic connected star the batched route plans.</summary>
    /// <param name="registry">The variable registry the pattern binds into.</param>
    /// <returns>The acyclic star pattern.</returns>
    private static BasicGraphPattern AcyclicStar(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(b)),
                new TriplePattern(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(c))
            ],
            registry);
    }

    /// <summary>Order-insensitive fingerprints of a solution set, so two routes' answers compare as sets.</summary>
    /// <param name="solutions">The solutions to fingerprint.</param>
    /// <returns>The sorted fingerprint list.</returns>
    private static List<string> Fingerprints(List<Solution> solutions)
    {
        List<string> fingerprints = [];
        foreach(Solution solution in solutions)
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>The opaque test access context.</summary>
    /// <param name="Subject">The requester's identity label.</param>
    private sealed record DeferredTestAccessContext(string Subject): AccessContext;
}
