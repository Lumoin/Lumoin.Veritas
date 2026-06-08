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
    /// <param name="cardinalities">A-priori per-class upper bounds handed to the planner on every consultation, or <c>null</c> when none are known.</param>
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
        AprioriCardinalities? cardinalities = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        IdentifierDelegate? identifiers = null,
        CancellationToken cancellationToken = default)
    {
        return Query(pinnedStore: null, query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, correlationId, identifiers, cancellationToken);
    }

    /// <summary>
    /// Evaluates the query against a specific store generation — a
    /// snapshot a caller pinned before later commits. When the
    /// rendezvous still holds that generation the policy selects
    /// normally; when it has advanced past it, the query runs on
    /// the pinned store directly, preserving snapshot isolation,
    /// and the decision is announced as
    /// <see cref="EngineSelectionReason.SnapshotSuperseded"/>.
    /// </summary>
    /// <param name="pinnedStore">The store generation the caller's snapshot pinned, or <c>null</c> to use the rendezvous's current generation.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events.</param>
    /// <param name="planner">The planner to use, or <c>null</c> to use <see cref="Planners.FirstOccurrence"/>.</param>
    /// <param name="cardinalities">A-priori per-class upper bounds handed to the planner on every consultation, or <c>null</c> when none are known.</param>
    /// <param name="accessControl">Optional access-control policy. <c>null</c> treats every candidate as allowed.</param>
    /// <param name="accessContext">Caller-supplied access context. Required when <paramref name="accessControl"/> is non-<c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler for query-execution events, including the selection event.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events. Pass <see cref="Guid.Empty"/> to generate a fresh one.</param>
    /// <param name="identifiers">The identifier source used to mint a fresh correlation id when <paramref name="correlationId"/> is <see cref="Guid.Empty"/>; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <param name="cancellationToken">Cancellation token threaded into evaluation.</param>
    /// <returns>An async sequence of solutions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public IAsyncEnumerable<Solution> Query(
        HypertrieGraphStore? pinnedStore,
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

        if(pinnedStore is not null && !ReferenceEquals(pinnedStore, currentStore))
        {
            //The caller's snapshot predates (or sidesteps) the
            //rendezvous's current generation; the derived view does
            //not describe it. Run on the pinned store directly.
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, EngineSelectionReason.SnapshotSuperseded, pinnedStore.Count, buildMilliseconds: 0);

            return pinnedStore.Query(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        bool shapeQualifies = query.Patterns.Count >= policy.MinimumPatternsForColumnar && IsColumnarCapable(query);

        //A rotation-incompatible join (a cyclic shape under a
        //three-rotation view — see ColumnarRotationPlanner) is
        //checked against the POLICY's mode before any view exists,
        //so it never triggers a build it cannot use, and its
        //fallback is announced distinctly for the feedback join.
        bool rotationCompatible = shapeQualifies
            && (policy.OrderSetMode == ColumnarOrderSetMode.AllSixOrders
                || ColumnarRotationPlanner.TryPlanGlobalOrder(policy.OrderSetMode, query) is not null);

        if(shapeQualifies && !rotationCompatible)
        {
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, EngineSelectionReason.RotationIncompatible, currentStore.Count, buildMilliseconds: 0);

            return currentStore.Query(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        bool qualifies = shapeQualifies && rotationCompatible;

        if(qualifies && currentView is null && policy.BuildViewOnDemand)
        {
            (currentView, bool isBuilt, long buildMilliseconds) = EnsureView(currentStore);

            if(currentView is not null)
            {
                //A racing query may have built the view first; it
                //then arrives here as a reuse with zero build cost.
                EngineSelectionReason reason = isBuilt
                    ? EngineSelectionReason.ViewBuilt
                    : EngineSelectionReason.ViewReused;

                IAsyncEnumerable<Solution>? batched = TryQueryBatched(
                    currentView, query, reason, buildMilliseconds, timeProvider, accessControl, traceHandler, effectiveCorrelationId, cancellationToken);

                if(batched is not null)
                {
                    return batched;
                }

                EmitEngineSelected(
                    timeProvider, traceHandler, effectiveCorrelationId,
                    QueryEngineKind.Columnar, reason, currentView.TripleCount, buildMilliseconds);

                return QueryColumnar(currentView, query, planner, cardinalities, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, cancellationToken);
            }

            //A commit advanced the rendezvous past the generation
            //this query read; the read pair stays the consistent
            //one to answer on.
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, EngineSelectionReason.SnapshotSuperseded, currentStore.Count, buildMilliseconds: 0);

            return currentStore.Query(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        if(qualifies && currentView is not null)
        {
            IAsyncEnumerable<Solution>? batched = TryQueryBatched(
                currentView, query, EngineSelectionReason.ViewReused, buildMilliseconds: 0, timeProvider, accessControl, traceHandler, effectiveCorrelationId, cancellationToken);

            if(batched is not null)
            {
                return batched;
            }

            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Columnar, EngineSelectionReason.ViewReused, currentView.TripleCount, buildMilliseconds: 0);

            return QueryColumnar(currentView, query, planner, cardinalities, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, cancellationToken);
        }

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            QueryEngineKind.Hypertrie, EngineSelectionReason.SystemOfRecord, currentStore.Count, buildMilliseconds: 0);

        return currentStore.Query(
            query,
            timeProvider,
            planner,
            cardinalities,
            accessControl,
            accessContext,
            traceHandler,
            effectiveCorrelationId,
            identifiers,
            cancellationToken);
    }

    /// <summary>
    /// Whether the columnar driver can evaluate every pattern of the
    /// query: a pattern binding the same variable at two of its own
    /// positions (a per-pattern self-join, <c>?x :q ?x</c>) needs the
    /// hypertrie driver's synthetic-key descent and stays on the
    /// system of record.
    /// </summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns><see langword="true"/> when every pattern is columnar-evaluable.</returns>
    internal static bool IsColumnarCapable(BasicGraphPattern query)
    {
        foreach(TriplePattern pattern in query.Patterns)
        {
            bool subjectPredicate = pattern.Subject.IsVariable && pattern.Predicate.IsVariable && pattern.Subject.Variable == pattern.Predicate.Variable;
            bool subjectObject = pattern.Subject.IsVariable && pattern.Object.IsVariable && pattern.Subject.Variable == pattern.Object.Variable;
            bool predicateObject = pattern.Predicate.IsVariable && pattern.Object.IsVariable && pattern.Predicate.Variable == pattern.Object.Variable;

            if(subjectPredicate || subjectObject || predicateObject)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Materialises the columnar view for the given store
    /// generation, exactly once, preserving the invariant that the
    /// view always pairs with the store published under the same
    /// lock.
    /// </summary>
    /// <param name="expectedStore">The store generation the caller's decision was made against.</param>
    /// <returns>
    /// The view paired with <paramref name="expectedStore"/> and
    /// whether this call built it (a racing caller that lost the
    /// build reuses the winner's view with zero build cost) — or no
    /// view at all when a commit advanced the rendezvous past the
    /// expected generation while the caller decided.
    /// </returns>
    private (ColumnarTripleIndex? View, bool IsBuilt, long BuildMilliseconds) EnsureView(HypertrieGraphStore expectedStore)
    {
        lock(StateLock)
        {
            if(!ReferenceEquals(store, expectedStore))
            {
                return (null, false, 0L);
            }

            if(columnarView is not null)
            {
                return (columnarView, false, 0L);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            ColumnarTripleIndex view = ColumnarTripleIndex.Build(store.Match(TermId.None, TermId.None, TermId.None), policy.OrderSetMode);
            stopwatch.Stop();

            columnarView = view;

            return (view, true, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Routes a qualifying query to the batched scan-and-hash
    /// pipeline when the policy opts in, no per-candidate access
    /// control is wired (the batched path has no consultation
    /// point), and the shape plans acyclic — or returns
    /// <c>null</c> for the leapfrog path.
    /// </summary>
    /// <param name="view">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="reason">The view-acquisition reason the selection event carries.</param>
    /// <param name="buildMilliseconds">The view build cost paid by this query, when any.</param>
    /// <param name="timeProvider">Clock for trace events.</param>
    /// <param name="accessControl">The access-control policy; non-<c>null</c> disqualifies the batched path.</param>
    /// <param name="traceHandler">Optional trace handler.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the flattening.</param>
    /// <returns>The batched solution stream, or <c>null</c>.</returns>
    private IAsyncEnumerable<Solution>? TryQueryBatched(
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        EngineSelectionReason reason,
        long buildMilliseconds,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if(!policy.PreferBatchedForAcyclic || accessControl is not null)
        {
            return null;
        }

        ColumnarBatchPlan? plan = ColumnarBatchPipeline.TryPlan(view, query, policy.PreferSemijoinReduction, policy.PreferFactorizedStar, policy.PreferFactorizedChain);

        if(plan is null)
        {
            return null;
        }

        EmitEngineSelected(
            timeProvider, traceHandler, correlationId,
            QueryEngineKind.ColumnarBatched, reason, view.TripleCount, buildMilliseconds);

        return SolutionBatch.FlattenAsync(ColumnarBatchPipeline.Run(view, plan), cancellationToken);
    }

    /// <summary>
    /// The columnar batched pipeline as a <em>raw batch stream</em> rather than a flattened per-row
    /// <see cref="Solution"/> sequence: when this query qualifies for the batched scan-and-hash path (the same
    /// gating <see cref="Query(HypertrieGraphStore?, BasicGraphPattern, TimeProvider, Planner?, AprioriCardinalities?, AccessControlDelegate?, AccessContext?, TraceHandler{QueryTraceEvent}?, Guid, IdentifierDelegate?, CancellationToken)"/>
    /// applies — qualifying shape, rotation-compatible, view available or buildable on demand, acyclic plan, and
    /// no per-candidate access control), returns the plan's output schema and the column-major
    /// <see cref="SolutionBatch"/> stream the pipeline produces, having announced the selection on the trace bus.
    /// Returns <see langword="null"/> for every case the per-row <c>Query</c> would handle differently (a
    /// superseded snapshot, a non-qualifying shape, a rotation-incompatible or cyclic plan, access control, or the
    /// batched policy disabled); the caller then evaluates through <c>Query</c> for those, so a conservative
    /// <see langword="null"/> is always sound — it only declines the columnar fast path, never returns wrong rows.
    /// A consumer that keeps encoded term ids columnar avoids the per-row <see cref="Solution"/> the flattening
    /// boundary would otherwise allocate for every scanned row.
    /// </summary>
    /// <param name="pinnedStore">The store generation the caller's snapshot pinned, or <c>null</c> to use the rendezvous's current generation.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on the emitted selection event.</param>
    /// <param name="accessControl">The access-control policy; non-<c>null</c> declines the batched path (it has no per-candidate consultation point).</param>
    /// <param name="traceHandler">Optional trace handler the selection event is emitted to.</param>
    /// <param name="correlationId">Correlation id stamped on the selection event. Pass <see cref="Guid.Empty"/> to generate a fresh one.</param>
    /// <param name="identifiers">The identifier source used to mint a fresh correlation id when <paramref name="correlationId"/> is <see cref="Guid.Empty"/>; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <returns>The plan's output schema and its batch stream when the batched path applies, otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public (IReadOnlyList<Variable> Schema, IEnumerable<SolutionBatch> Batches)? TryQueryBatchedColumns(
        HypertrieGraphStore? pinnedStore,
        BasicGraphPattern query,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        IdentifierDelegate? identifiers = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeProvider);

        //The batched path has no per-candidate access-control consultation point, and it only exists when the
        //policy opts in. Either disqualifier declines to the per-row Query.
        if(accessControl is not null || !policy.PreferBatchedForAcyclic)
        {
            return null;
        }

        HypertrieGraphStore currentStore;
        ColumnarTripleIndex? currentView;

        lock(StateLock)
        {
            currentStore = store;
            currentView = columnarView;
        }

        //A pinned snapshot the rendezvous has advanced past is answered on the system of record by Query, not here.
        if(pinnedStore is not null && !ReferenceEquals(pinnedStore, currentStore))
        {
            return null;
        }

        if(query.Patterns.Count < policy.MinimumPatternsForColumnar || !IsColumnarCapable(query))
        {
            return null;
        }

        if(policy.OrderSetMode != ColumnarOrderSetMode.AllSixOrders
            && ColumnarRotationPlanner.TryPlanGlobalOrder(policy.OrderSetMode, query) is null)
        {
            return null;
        }

        ColumnarTripleIndex? view = currentView;
        long buildMilliseconds = 0;
        EngineSelectionReason reason = EngineSelectionReason.ViewReused;
        if(view is null)
        {
            if(!policy.BuildViewOnDemand)
            {
                return null;
            }

            (view, bool isBuilt, buildMilliseconds) = EnsureView(currentStore);
            if(view is null)
            {
                //A commit advanced the rendezvous past the read generation; Query answers on the consistent store.
                return null;
            }

            reason = isBuilt ? EngineSelectionReason.ViewBuilt : EngineSelectionReason.ViewReused;
        }

        ColumnarBatchPlan? plan = ColumnarBatchPipeline.TryPlan(view, query, policy.PreferSemijoinReduction, policy.PreferFactorizedStar, policy.PreferFactorizedChain);
        if(plan is null)
        {
            return null;
        }

        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            QueryEngineKind.ColumnarBatched, reason, view.TripleCount, buildMilliseconds);

        return (plan.Schema, ColumnarBatchPipeline.Run(view, plan));
    }

    private IAsyncEnumerable<Solution> QueryColumnar(
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        Planner? planner,
        AprioriCardinalities? cardinalities,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        //ColumnarHyperCube degrades to the sequential evaluator at a
        //degree of parallelism of one.
        return ColumnarHyperCube.Query(
            view,
            query,
            policy.DegreeOfParallelism,
            timeProvider,
            planner,
            cardinalities,
            accessControl,
            accessContext,
            traceHandler,
            correlationId,
            cancellationToken);
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
