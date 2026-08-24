using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;
using Lumoin.Veritas.Core.Indexing;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Produces, per dataset generation, every graph's triples for a
/// lazy <see cref="ColumnarGraphSetIndex"/> build. Consulted at
/// most once per generation, on the first qualifying named-graph
/// join.
/// </summary>
/// <returns>The graphs' triples keyed by graph-name term id.</returns>
public delegate IReadOnlyDictionary<TermId, IEnumerable<EncodedTriple>> GraphSetSource();

/// <summary>
/// The named-graph sibling of <see cref="QueryEngineRendezvous"/>:
/// routes a named graph's basic graph patterns between the graph's
/// system-of-record store and a derived
/// <see cref="ColumnarGraphSetIndex"/> view over ALL named graphs —
/// one shared column set, materialised lazily per dataset
/// generation, amortised across every graph instead of one view
/// per graph.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generations.</b> The owner advances the rendezvous with an
/// opaque generation token whenever the named-graph population
/// changes; queries carry the token their dataset snapshot was
/// taken under. A mismatch — the rendezvous moved on, or the
/// snapshot sidesteps it — answers on the caller's pinned store,
/// preserving snapshot isolation, announced as
/// <see cref="EngineSelectionReason.SnapshotSuperseded"/>.
/// </para>
/// <para>
/// <b>Routing.</b> The same policy gates as the default-graph
/// rendezvous: pattern count, columnar capability, and rotation
/// compatibility against the policy's order-set mode (checked
/// before any build, so an incompatible query never materialises a
/// set it cannot use). Every decision is announced as an
/// <see cref="QueryTraceEventKind.EngineSelected"/> event.
/// </para>
/// </remarks>
[DebuggerDisplay("GraphSetRendezvous HasSet={set is not null}")]
public sealed class GraphSetRendezvous
{
    private readonly QueryEnginePolicy policy;

    /// <summary>Guards the generation/source/set triple.</summary>
    private Lock StateLock { get; } = new();

    /// <summary>The opaque token naming the named-graph population the set describes.</summary>
    private object Generation { get; set; }

    /// <summary>The lazy build's triples source for the current generation.</summary>
    private GraphSetSource Source { get; set; }

    //The derived set for the current generation, or null until the
    //first qualifying query builds it. Naked field: written under
    //the lock, read into locals.
    private ColumnarGraphSetIndex? set;

    //Sequence counter for emitted trace events; a field because
    //Interlocked requires a ref parameter.
    private long traceSequence;

    /// <summary>Constructs the rendezvous over its initial generation.</summary>
    /// <param name="generation">The opaque token naming the initial named-graph population.</param>
    /// <param name="source">The lazy build's triples source for that population.</param>
    /// <param name="policy">The routing policy; also fixes the set's order-set mode.</param>
    /// <param name="valueIndexes">The composed value-index registry; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/> (zero overhead).</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public GraphSetRendezvous(object generation, GraphSetSource source, QueryEnginePolicy policy, ValueIndexRegistry? valueIndexes = null)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(source);

        Generation = generation;
        Source = source;
        this.policy = policy;
        ValueIndexes = valueIndexes ?? ValueIndexRegistry.Empty;
    }

    /// <summary>The composed value-index registry: the access methods the owning dataset's commit path maintains. <see cref="ValueIndexRegistry.Empty"/> unless the host registered methods.</summary>
    public ValueIndexRegistry ValueIndexes { get; }

    /// <summary>
    /// Advances the rendezvous to a new named-graph population: the
    /// derived set is dropped and rebuilds lazily from
    /// <paramref name="source"/> on the next qualifying query.
    /// In-flight queries keep answering on their pinned stores.
    /// </summary>
    /// <param name="generation">The new population's token.</param>
    /// <param name="source">The new population's triples source.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public void Advance(object generation, GraphSetSource source)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(source);

        lock(StateLock)
        {
            Generation = generation;
            Source = source;
            set = null;
        }
    }

    /// <summary>
    /// Evaluates a named graph's basic graph pattern, routing
    /// between the pinned system-of-record store and the shared
    /// columnar graph set per the policy.
    /// </summary>
    /// <param name="expectedGeneration">The generation token the caller's dataset snapshot was taken under.</param>
    /// <param name="graph">The named graph's term id.</param>
    /// <param name="pinnedStore">The graph's system-of-record store from the caller's snapshot — the fallback every mismatch and disqualification answers on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events.</param>
    /// <param name="planner">The planner to use, or <c>null</c> for <see cref="Planners.FirstOccurrence"/>.</param>
    /// <param name="cardinalities">A-priori per-class upper bounds, or <c>null</c>.</param>
    /// <param name="accessControl">Optional access-control policy.</param>
    /// <param name="accessContext">Caller-supplied access context; required when <paramref name="accessControl"/> is non-<c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler for query-execution events, including the selection event.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events; <see cref="Guid.Empty"/> mints a fresh one.</param>
    /// <param name="identifiers">The identifier source for minting a correlation id; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <param name="cancellationToken">Cancellation token threaded into evaluation.</param>
    /// <returns>An async sequence of solutions.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public IAsyncEnumerable<Solution> QueryAsync(
        object expectedGeneration,
        TermId graph,
        HypertrieGraphStore pinnedStore,
        BasicGraphPattern query,
        TimeProvider timeProvider,
        Planner? planner = null,
        AprioriCardinalities? cardinalities = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        IdentifierDelegate? identifiers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedGeneration);
        ArgumentNullException.ThrowIfNull(pinnedStore);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        object currentGeneration;
        ColumnarGraphSetIndex? currentSet;

        lock(StateLock)
        {
            currentGeneration = Generation;
            currentSet = set;
        }

        if(!ReferenceEquals(expectedGeneration, currentGeneration))
        {
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, EngineSelectionReason.SnapshotSuperseded, pinnedStore.Count, buildMilliseconds: 0);

            return pinnedStore.QueryAsync(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        bool shapeQualifies = query.Patterns.Count >= policy.MinimumPatternsForColumnar && QueryEngineRendezvous.IsColumnarCapable(query);
        bool rotationCompatible = shapeQualifies
            && (policy.OrderSetMode == ColumnarOrderSetMode.AllSixOrders
                || ColumnarRotationPlanner.TryPlanGlobalOrder(policy.OrderSetMode, query) is not null);

        if(shapeQualifies && !rotationCompatible)
        {
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, EngineSelectionReason.RotationIncompatible, pinnedStore.Count, buildMilliseconds: 0);

            return pinnedStore.QueryAsync(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        if(shapeQualifies && rotationCompatible)
        {
            long buildMilliseconds = 0;
            bool isBuilt = false;

            if(currentSet is null && policy.BuildViewOnDemand)
            {
                (currentSet, isBuilt, buildMilliseconds) = EnsureSet(expectedGeneration);
            }

            ColumnarTripleIndex? view = currentSet?.GetView(graph);

            if(view is not null)
            {
                EmitEngineSelected(
                    timeProvider, traceHandler, effectiveCorrelationId,
                    QueryEngineKind.Columnar, isBuilt ? EngineSelectionReason.ViewBuilt : EngineSelectionReason.ViewReused, view.TripleCount, buildMilliseconds);

                return ColumnarHyperCube.QueryAsync(
                    view, query, policy.DegreeOfParallelism, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, cancellationToken);
            }
        }

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            QueryEngineKind.Hypertrie, EngineSelectionReason.SystemOfRecord, pinnedStore.Count, buildMilliseconds: 0);

        return pinnedStore.QueryAsync(
            query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
    }

    /// <summary>
    /// Materialises the graph set for the given generation exactly
    /// once. A commit that advanced the rendezvous past the
    /// caller's generation while it decided yields no set; the
    /// caller answers on its pinned store.
    /// </summary>
    /// <param name="expectedGeneration">The generation the caller's decision was made against.</param>
    /// <returns>The set, whether this call built it, and the build cost.</returns>
    private (ColumnarGraphSetIndex? Set, bool IsBuilt, long BuildMilliseconds) EnsureSet(object expectedGeneration)
    {
        lock(StateLock)
        {
            if(!ReferenceEquals(Generation, expectedGeneration))
            {
                return (null, false, 0L);
            }

            if(set is not null)
            {
                return (set, false, 0L);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            ColumnarGraphSetIndex built = ColumnarGraphSetIndex.Build(Source(), policy.OrderSetMode, backing: policy.ColumnPayloadBacking);
            stopwatch.Stop();

            set = built;

            return (built, true, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>Emits one engine-selection trace event when a handler is wired.</summary>
    /// <param name="timeProvider">The clock stamping the event.</param>
    /// <param name="traceHandler">The handler, or <c>null</c> for none.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="engine">The selected engine.</param>
    /// <param name="reason">The selection reason.</param>
    /// <param name="tripleCount">The selected source's triple count.</param>
    /// <param name="buildMilliseconds">The build cost paid by this selection, when any.</param>
    private void EmitEngineSelected(
        TimeProvider timeProvider,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        QueryEngineKind engine,
        EngineSelectionReason reason,
        int tripleCount,
        long buildMilliseconds)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.EngineSelected(
            sequence,
            timeProvider.GetUtcNow().UtcTicks,
            correlationId,
            engine,
            reason,
            tripleCount,
            buildMilliseconds);

        traceHandler(in evt);
    }
}
