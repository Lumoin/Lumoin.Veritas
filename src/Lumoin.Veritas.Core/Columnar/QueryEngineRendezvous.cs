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

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The per-query engine choice at the evaluation boundary: routes a
/// basic graph pattern to the hypertrie system of record or to a
/// derived columnar view, per a <see cref="QueryEnginePolicy"/>,
/// and announces every decision as a
/// <see cref="QueryTraceEventKind.EngineSelected"/> event on the
/// query trace bus.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> The rendezvous tracks the current store and an
/// optional columnar view derived from it. The view is materialised
/// lazily — the first qualifying query pays the build, which a
/// single join-heavy query amortises — and evolves with the write
/// path: <see cref="Advance"/> swaps in the post-commit store and
/// applies the same journal-shaped delta to the view, so the two
/// engines stay in step through the single write path.
/// </para>
/// <para>
/// <b>The decision side-channel.</b> Each query emits one
/// <see cref="QueryTraceEventKind.EngineSelected"/> event under the
/// query's correlation id before delegating, carrying the engine,
/// the reason, the engine's triple count, and the build cost when a
/// view was materialised. Consumers joining these with
/// <see cref="QueryTraceEventKind.QueryCompleted"/> obtain
/// (decision inputs → observed cost) pairs — the feedback an
/// adaptive selection policy learns from. The bus observes; it
/// never dictates.
/// </para>
/// <para>
/// <b>Thread safety.</b> Queries may run concurrently; the view is
/// built at most once per store generation under a lock, and
/// <see cref="Advance"/> publishes the new store/view pair with a
/// lock so concurrent queries see a consistent pair. In-flight
/// queries keep the pair they started with — snapshots and views
/// are immutable.
/// </para>
/// </remarks>
[DebuggerDisplay("QueryEngineRendezvous HasView={columnarView is not null}")]
public sealed class QueryEngineRendezvous
{
    private readonly QueryEnginePolicy policy;

    //The current engines. Swapped together under StateLock by
    //Advance; read together under the same lock at query start so
    //a query never pairs a new store with a stale view.
    private HypertrieGraphStore store;

    private ColumnarTripleIndex? columnarView;

    private object StateLock { get; } = new();

    //Sequence-number counter for trace events emitted by this
    //rendezvous. A field rather than a property because
    //Interlocked requires a ref parameter.
    private long traceSequence;

    /// <summary>
    /// Constructs a rendezvous over <paramref name="store"/> with
    /// the given policy and no columnar view; the view materialises
    /// per policy when a qualifying query arrives.
    /// </summary>
    /// <param name="store">The system of record.</param>
    /// <param name="policy">The selection policy. Pass <see cref="QueryEnginePolicy.Default"/> for the standard join-routing behaviour.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    public QueryEngineRendezvous(HypertrieGraphStore store, QueryEnginePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
        this.policy = policy;
    }

    /// <summary>
    /// Replaces the system of record with its post-commit successor
    /// and evolves the columnar view by the same delta, keeping
    /// both engines in step through the single write path. A view
    /// that was never materialised stays unmaterialised — the next
    /// qualifying query builds it from the new store.
    /// </summary>
    /// <param name="newStore">The post-commit store.</param>
    /// <param name="additions">The commit's effective additions, as the journal records them.</param>
    /// <param name="removals">The commit's effective removals, as the journal records them.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public void Advance(
        HypertrieGraphStore newStore,
        IReadOnlyCollection<EncodedTriple> additions,
        IReadOnlyCollection<EncodedTriple> removals)
    {
        ArgumentNullException.ThrowIfNull(newStore);
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);

        lock(StateLock)
        {
            store = newStore;
            columnarView = columnarView?.Apply(additions, removals);
        }
    }

    /// <summary>
    /// Evaluates the query on the engine the policy selects,
    /// announcing the decision on the trace bus. The parameter
    /// surface mirrors <see cref="HypertrieGraphStore.Query"/>.
    /// </summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events.</param>
    /// <param name="planner">The planner to use, or <c>null</c> to use <see cref="Planners.FirstOccurrence"/>.</param>
    /// <param name="accessControl">Optional access-control policy. <c>null</c> treats every candidate as allowed.</param>
    /// <param name="accessContext">Caller-supplied access context. Required when <paramref name="accessControl"/> is non-<c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler for query-execution events, including the selection event.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events. Pass <see cref="Guid.Empty"/> to generate a fresh one.</param>
    /// <param name="identifiers">The identifier source used to mint a fresh correlation id when <paramref name="correlationId"/> is <see cref="Guid.Empty"/>; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <param name="cancellationToken">Cancellation token threaded into evaluation.</param>
    /// <returns>An async sequence of solutions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public IAsyncEnumerable<Solution> Query(
        BasicGraphPattern query,
        TimeProvider timeProvider,
        Planner? planner = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        IdentifierDelegate? identifiers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        HypertrieGraphStore currentStore;
        ColumnarTripleIndex? currentView;

        lock(StateLock)
        {
            currentStore = store;
            currentView = columnarView;
        }

        bool qualifies = query.Patterns.Count >= policy.MinimumPatternsForColumnar;

        if(qualifies && currentView is null && policy.BuildViewOnDemand)
        {
            (currentView, long buildMilliseconds) = EnsureView();

            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Columnar, EngineSelectionReason.ViewBuilt, currentView.TripleCount, buildMilliseconds);

            return QueryColumnar(currentView, query, planner, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, cancellationToken);
        }

        if(qualifies && currentView is not null)
        {
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Columnar, EngineSelectionReason.ViewReused, currentView.TripleCount, buildMilliseconds: 0);

            return QueryColumnar(currentView, query, planner, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, cancellationToken);
        }

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            QueryEngineKind.Hypertrie, EngineSelectionReason.SystemOfRecord, currentStore.Count, buildMilliseconds: 0);

        return currentStore.Query(
            query,
            timeProvider,
            planner,
            accessControl,
            accessContext,
            traceHandler,
            effectiveCorrelationId,
            identifiers,
            cancellationToken);
    }

    //Materialises the columnar view for the current store, exactly
    //once per store generation, preserving the invariant that the
    //view always pairs with the store published under the same
    //lock. A racing query that loses the lock reuses the winner's
    //view and reports zero build cost.
    private (ColumnarTripleIndex View, long BuildMilliseconds) EnsureView()
    {
        lock(StateLock)
        {
            //The store may have advanced or another query may have
            //built the view while this one waited; reuse whatever
            //pair is current.
            if(columnarView is not null)
            {
                return (columnarView, 0L);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            ColumnarTripleIndex view = ColumnarTripleIndex.Build(store.Match(TermId.None, TermId.None, TermId.None));
            stopwatch.Stop();

            columnarView = view;

            return (view, stopwatch.ElapsedMilliseconds);
        }
    }

    private static IAsyncEnumerable<Solution> QueryColumnar(
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        Planner? planner,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ColumnarBasicGraphPatternEvaluator evaluator = new(
            view,
            query,
            planner ?? Planners.FirstOccurrence(query),
            timeProvider,
            accessControl,
            accessContext,
            traceHandler,
            correlationId);

        return evaluator.EvaluateAsync(cancellationToken);
    }

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
