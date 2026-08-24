using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// When a compute lane is supplied, <see cref="QueryEngineRendezvous"/>
/// materialises the on-demand columnar view as a lane turn off the serve
/// path: the first qualifying query admits the build and answers from the
/// system of record (reported as <see cref="EngineSelectionReason.ViewBuilding"/>),
/// and once the lane runs the turn a later query reuses the view — with
/// the same results either engine produces.
/// </summary>
[TestClass]
internal sealed class QueryEngineRendezvousLaneTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The predicate every fixture edge carries.</summary>
    private static TermId Knows { get; } = TermId.FromEncoded(100);

    /// <summary>A test lane that queues admitted work and runs it only when drained, so the off-path build is deterministically observable.</summary>
    private sealed class ManualComputeLane: IComputeLane
    {
        /// <summary>The admitted, not-yet-run turns.</summary>
        private readonly List<ComputeWorkDelegate> pending = [];

        /// <inheritdoc/>
        public int WorkerCount => 1;

        /// <inheritdoc/>
        public int QueueDepth => pending.Count;

        /// <inheritdoc/>
        public long TurnsCompleted { get; private set; }

        /// <inheritdoc/>
        public long ShedCount => 0;

        /// <inheritdoc/>
        public int QueueDepthOf(ComputeWorkClass workClass)
        {
            return pending.Count;
        }

        /// <inheritdoc/>
        public ComputeAdmission Admit(ComputeWorkClass workClass, ComputeWorkDelegate work)
        {
            pending.Add(work);

            return ComputeAdmission.Admitted;
        }

        /// <summary>Runs every queued turn to completion.</summary>
        /// <returns>A task that completes when the queue is drained.</returns>
        public async Task DrainAsync()
        {
            ComputeWorkDelegate[] snapshot = [.. pending];
            pending.Clear();

            foreach(ComputeWorkDelegate work in snapshot)
            {
                await work(CancellationToken.None).ConfigureAwait(false);
                TurnsCompleted++;
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    public async Task ViewBuildOffloadsToTheLaneAndServesFromTheSystemOfRecordUntilItLands()
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

        ManualComputeLane lane = new();
        await using var laneScope = lane.ConfigureAwait(false);
        QueryEngineRendezvous rendezvous = new(store, QueryEnginePolicy.Default, lane);
        VariableRegistry registry = new();
        BasicGraphPattern query = TwoPatternJoin(registry);
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        //First qualifying query: the build is admitted to the lane and this query serves the
        //system of record while the view is in flight.
        List<Solution> fromSystemOfRecord = await Drain(rendezvous.QueryAsync(
            query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[^1].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewBuilding, selections[^1].SelectionReason);
        Assert.AreEqual(1, lane.QueueDepth, "The view build was admitted to the lane.");

        //Run the lane: the view materialises off the serve path.
        await lane.DrainAsync().ConfigureAwait(false);

        //A later query now reuses the materialised view, answering the join identically.
        List<Solution> fromView = await Drain(rendezvous.QueryAsync(
            query, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(QueryEngineKind.ColumnarBatched, selections[^1].Engine);
        Assert.AreEqual(EngineSelectionReason.ViewReused, selections[^1].SelectionReason);

        //Both engines answer the join identically.
        Assert.HasCount(fromSystemOfRecord.Count, fromView);
    }

    [TestMethod]
    public async Task SelfIndexBuildOffloadsToTheLaneAndServesFromTheSystemOfRecordUntilItLands()
    {
        //A 3-cycle: under the three-rotation order set the triangle shape is rotation-incompatible,
        //so it routes to the succinct self-index when the policy opts in.
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, Knows.Encoded, 2),
            EncodedTriple.FromEncoded(2, Knows.Encoded, 3),
            EncodedTriple.FromEncoded(3, Knows.Encoded, 1),
        ];

        HypertrieGraphStore store = await HypertrieGraphStore
            .BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        ManualComputeLane lane = new();
        await using var laneScope = lane.ConfigureAwait(false);
        QueryEnginePolicy policy = new(MinimumPatternsForColumnar: 2, BuildViewOnDemand: true, DegreeOfParallelism: 1, OrderSetMode: ColumnarOrderSetMode.ThreeRotations, PreferSelfIndex: true);
        QueryEngineRendezvous rendezvous = new(store, policy, lane);
        VariableRegistry registry = new();
        BasicGraphPattern triangle = Triangle(registry);
        List<QueryTraceEvent> selections = [];
        TraceHandler<QueryTraceEvent> handler = CollectSelections(selections);

        //First query: the self-index build is admitted to the lane and this query serves the
        //system of record while it is in flight.
        List<Solution> fromSystemOfRecord = await Drain(rendezvous.QueryAsync(
            triangle, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(QueryEngineKind.Hypertrie, selections[^1].Engine);
        Assert.AreEqual(1, lane.QueueDepth, "The self-index build was admitted to the lane.");

        //Run the lane: the self-index materialises off the serve path.
        await lane.DrainAsync().ConfigureAwait(false);

        //A later query now answers from the self-index, with the same results.
        List<Solution> fromSelfIndex = await Drain(rendezvous.QueryAsync(
            triangle, VeritasClock.System, traceHandler: handler, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreEqual(QueryEngineKind.SelfIndex, selections[^1].Engine);
        Assert.HasCount(fromSystemOfRecord.Count, fromSelfIndex);
    }

    /// <summary>(?a knows ?b) . (?b knows ?c) . (?c knows ?a): a 3-cycle — rotation-incompatible under a reduced order set, so it routes to the self-index.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The basic graph pattern.</returns>
    private static BasicGraphPattern Triangle(VariableRegistry registry)
    {
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        TriplePattern first = new(PatternPosition.OfVariable(a), PatternPosition.Bound(Knows), PatternPosition.OfVariable(b));
        TriplePattern second = new(PatternPosition.OfVariable(b), PatternPosition.Bound(Knows), PatternPosition.OfVariable(c));
        TriplePattern third = new(PatternPosition.OfVariable(c), PatternPosition.Bound(Knows), PatternPosition.OfVariable(a));

        return new BasicGraphPattern([first, second, third], registry);
    }

    /// <summary>(?a knows ?b) . (?b knows ?c): a two-pattern join that qualifies for the columnar view under the default policy.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The basic graph pattern.</returns>
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

    /// <summary>A trace handler that records the engine-selection events.</summary>
    /// <param name="sink">The list selections are appended to.</param>
    /// <returns>The handler.</returns>
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

    /// <summary>Drains an async solution stream into a list.</summary>
    /// <param name="solutions">The stream.</param>
    /// <returns>The drained solutions.</returns>
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
