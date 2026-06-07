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

        List<Solution> solutions = await Drain(rendezvous.Query(
            query, VeritasClock.System, traceHandler: CollectSelections(selections), cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(1, selections);
        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.SystemOfRecord, selections[0].SelectionReason);
        Assert.HasCount(4, solutions);
    }

    /// <summary>The first join query builds the view (reporting its cost), the second reuses it.</summary>
    [TestMethod]
    public async Task JoinQueryBuildsThenReusesTheView()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        BasicGraphPattern query = TwoPatternJoin(registry);
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        await Drain(rendezvous.Query(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        await Drain(rendezvous.Query(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(2, selections);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[0].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, selections[0].SelectionReason);
        Assert.AreEqual(QueryEngineKind.Columnar, selections[1].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[1].SelectionReason);
        Assert.AreEqual(0L, selections[1].Value, "A reused view reports zero build cost.");
        Assert.AreEqual(4, selections[1].Count, "The selection event carries the engine's triple count.");
    }

    /// <summary>Both engines yield identical solution sets for the same join.</summary>
    [TestMethod]
    public async Task EnginesAgreeOnJoinResults()
    {
        (QueryEngineRendezvous rendezvous, VariableRegistry registry) = await CreateRendezvousAsync().ConfigureAwait(false);
        (QueryEngineRendezvous recordOnly, VariableRegistry recordRegistry) = await CreateRendezvousAsync(
            new QueryEnginePolicy(MinimumPatternsForColumnar: int.MaxValue, BuildViewOnDemand: false, DegreeOfParallelism: 1, OrderSetMode: ColumnarOrderSetMode.AllSixOrders)).ConfigureAwait(false);

        List<Solution> columnarSolutions = await Drain(rendezvous.Query(
            TwoPatternJoin(registry), VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<Solution> hypertrieSolutions = await Drain(recordOnly.Query(
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

        List<Solution> before = await Drain(rendezvous.Query(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

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

        List<Solution> after = await Drain(rendezvous.Query(query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[^1].SelectionReason, "The advanced view answers without a rebuild.");
        Assert.AreNotEqual(before.Count, after.Count, "The commit changes the join's result set.");

        //The reference: the same query on the post-commit store.
        List<Solution> reference = await Drain(newStore.Query(query, VeritasClock.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

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
}
