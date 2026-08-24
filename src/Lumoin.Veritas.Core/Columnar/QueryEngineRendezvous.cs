using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Tracing;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Core.Memory;

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

    //The compute lane that materialises the on-demand view off the serve
    //path when present; null routes the build inline under StateLock.
    private readonly IComputeLane? computeLane;

    //The current engines. Swapped together under StateLock by
    //Advance; read together under the same lock at query start so
    //a query never pairs a new store with a stale view. The store is
    //null only under HypertrieResidency.Deferred before the trie is
    //materialised, where deferredSource holds the recovered triples
    //instead: exactly one of (store, deferredSource) is non-null
    //until the first materialisation, after which store is set and
    //deferredSource cleared. Eager construction supplies a concrete
    //store and null source, so every "store is null" branch is dead
    //and the eager path is byte-identical.
    private HypertrieGraphStore? store;

    private ColumnarTripleIndex? columnarView;

    //The deferred system-of-record build source under
    //HypertrieResidency.Deferred: the recovered triples the trie is
    //materialised from on first demand. Null once the trie is built,
    //and null from the outset in eager residency.
    private DeferredTrieSource? deferredSource;

    //The single shared trie-materialisation task — the in-flight
    //token AND the result channel (a bool cannot serve awaiters that
    //need the built store). All reads/writes under StateLock.
    private Task<HypertrieGraphStore>? trieBuildInFlight;

    //Counts how many deferred trie-build turns have run (build
    //attempts) — a peer of the StateLock-guarded state cluster,
    //read and written under the lock. Test-only observability:
    //exactly one across concurrent first-trie-need queries, and an
    //increment per re-kick after a faulted build cleared the slot.
    private int trieBuildCount;

    //Set under StateLock while an off-path view build is in flight on the
    //compute lane, so concurrent queries admit at most one build turn.
    private bool viewBuildPending;

    //The succinct self-index serving rotation-incompatible shapes when the
    //policy opts in; materialised on first such demand and dropped on every
    //commit — it rebuilds rather than evolving by delta.
    private TripleSelfIndex? selfIndexView;

    //Set under StateLock while an off-path self-index build is in flight on
    //the compute lane, so concurrent queries admit at most one build turn.
    private bool selfIndexBuildPending;

    //The store generation the registered value-index methods were last built against, or null while
    //they are unbuilt or invalidated by a commit. Read and written under StateLock, always compared by
    //reference against the current store, so a probe can never pair a stale index with a newer store.
    private HypertrieGraphStore? valueIndexGeneration;

    private object StateLock { get; } = new();

    //Sequence-number counter for trace events emitted by this
    //rendezvous. A field rather than a property because
    //Interlocked requires a ref parameter.
    private long traceSequence;

    /// <summary>The pool every batched route's query-scoped factorised arena rents slabs from — the ONE resolved instance all four pipeline call sites share, so rentals attribute to it.</summary>
    private readonly MemoryPool<uint> arenaPool;

    /// <summary>
    /// Constructs a rendezvous over <paramref name="store"/> with the given policy and an optional pre-built
    /// columnar view — a warm-loaded sidecar that corresponds to <paramref name="store"/>'s triples, so the first
    /// qualifying query serves from it with no build. Absent one, the view materialises per policy when a
    /// qualifying query arrives.
    /// </summary>
    /// <param name="store">The system of record, or <c>null</c> for a deferred-residency rendezvous that materialises the trie on demand from <paramref name="deferredStore"/>. Exactly one of <paramref name="store"/> and <paramref name="deferredStore"/> must be non-<c>null</c>.</param>
    /// <param name="policy">The selection policy. Pass <see cref="QueryEnginePolicy.Default"/> for the standard join-routing behaviour.</param>
    /// <param name="computeLane">An optional compute lane. When supplied, the on-demand columnar view materialises as a lane turn off the serve path and queries serve from the system of record until it lands; when <c>null</c>, the view builds inline on the first qualifying query.</param>
    /// <param name="initialView">A pre-built columnar view materialised from the same triples as the system of record (a warm-loaded durable sidecar), or <c>null</c> to build the view on demand. It must be consistent with the served triples; a later commit evolves it by the same delta. Under deferred residency this is the warm view a read generation serves from without ever materialising the trie.</param>
    /// <param name="deferredStore">The deferred build source the trie is materialised from on first demand (under <see cref="HypertrieResidency.Deferred"/>), or <c>null</c> for an eager rendezvous whose <paramref name="store"/> is resident up front. Exactly one of <paramref name="store"/> and this must be non-<c>null</c>.</param>
    /// <param name="valueIndexes">The composed value-index registry whose access methods this rendezvous's commit path maintains; <see langword="null"/> uses <see cref="ValueIndexRegistry.Empty"/> (zero overhead).</param>
    /// <param name="factorizedArenaPool">The pool the batched routes' query-scoped factorised arenas rent slabs from; <see langword="null"/> uses <see cref="VeritasMemoryPool{T}.Shared"/>. A caller that supplies its own instance can attribute the routes' rentals to it through the pool's per-instance measurement tag.</param>
    /// <exception cref="ArgumentException">Neither or both of <paramref name="store"/> and <paramref name="deferredStore"/> are supplied.</exception>
    public QueryEngineRendezvous(HypertrieGraphStore? store, QueryEnginePolicy policy, IComputeLane? computeLane = null, ColumnarTripleIndex? initialView = null, DeferredTrieSource? deferredStore = null, ValueIndexRegistry? valueIndexes = null, MemoryPool<uint>? factorizedArenaPool = null)
    {
        if((store is null) == (deferredStore is null))
        {
            throw new ArgumentException("Exactly one of the system-of-record store or the deferred build source must be supplied: the store for an eager (always-resident) rendezvous, the deferred source for a warm deferred-residency one.", nameof(store));
        }

        this.store = store;
        this.policy = policy;
        this.computeLane = computeLane;
        columnarView = initialView;
        deferredSource = deferredStore;
        ValueIndexes = valueIndexes ?? ValueIndexRegistry.Empty;
        arenaPool = factorizedArenaPool ?? VeritasMemoryPool<uint>.Shared;
    }

    /// <summary>The composed value-index registry: the access methods this rendezvous's commit path maintains and its consumers consult. <see cref="ValueIndexRegistry.Empty"/> unless the host registered methods.</summary>
    public ValueIndexRegistry ValueIndexes { get; }

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

            //The self-index has no delta form; the next qualifying query
            //rebuilds it from the post-commit store.
            selfIndexView = null;

            //Value indexes rebuild wholesale per generation: the commit invalidates,
            //and the next probe rebuilds from the post-commit store — never a delta
            //application, never build work under this publish lock's caller.
            valueIndexGeneration = null;
        }
    }

    /// <summary>
    /// Opens a value-index probe against the current generation, rebuilding the registered access
    /// methods from it first when a commit invalidated them (the drop-and-rebuild lifecycle: the
    /// probe pays the rebuild, never the publishing commit). Returns <see langword="false"/> — the
    /// caller scans instead — when no registered axis involves the predicate, when the store is not
    /// materialised (deferred residency before the first build), or when the registry is empty
    /// (the zero-overhead default: one branch, no allocation).
    /// </summary>
    /// <remarks>
    /// The store and the value-index generation are read under one <c>StateLock</c> acquisition and
    /// compared by reference, and the probe's answer derives wholly from the index built against that
    /// exact store — so a probe can never pair a stale index with a newer generation, the torn-probe
    /// hazard the generation pin exists for. The caller additionally pins its OWN evaluation store:
    /// the probe serves only when <paramref name="callerStore"/> IS this rendezvous's current store by
    /// reference, so a query holding an older pinned snapshot, or an update whose WHERE clause runs
    /// over a substituted default graph (<c>WITH &lt;g&gt;</c>), declines to its own scan instead of
    /// being answered from a graph it is not evaluating.
    /// </remarks>
    /// <param name="predicateIri">The predicate the probe's comparison constrains.</param>
    /// <param name="request">The probe.</param>
    /// <param name="dictionary">The shared term dictionary the rebuild resolves predicates and values through.</param>
    /// <param name="callerStore">The default-graph store the CALLER is evaluating over (its pinned snapshot or substituted graph); the probe declines unless it is reference-equal to this rendezvous's current store.</param>
    /// <param name="cursor">Receives the hit cursor when a registered axis serves the probe.</param>
    /// <returns><see langword="true"/> when a registered axis serves the probe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <see langword="null"/>.</exception>
    public bool TryOpenValueProbe(Utf8String predicateIri, in ValueProbeRequest request, TermDictionary dictionary, HypertrieGraphStore? callerStore, out ValueProbeCursor? cursor)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        cursor = null;
        if(ValueIndexes.IsEmpty)
        {
            return false;
        }

        ValueIndexRegistration? registration = ValueIndexes.FindByPredicate(predicateIri);
        if(registration is null)
        {
            return false;
        }

        lock(StateLock)
        {
            if(store is null || !ReferenceEquals(store, callerStore))
            {
                return false;
            }

            if(!ReferenceEquals(valueIndexGeneration, store))
            {
                //Rebuild EVERY registration against the pinned generation, so one consistent
                //generation stamp covers the whole registry (the EnsureView inline-build precedent).
                StoreValueSegmentSource source = new(store, dictionary);
                for(int i = 0; i < ValueIndexes.Registrations.Count; i++)
                {
                    if(ValueIndexes.Registrations[i].Method.Build(source) != ValueIndexBuildOutcome.Built)
                    {
                        return false;
                    }
                }

                valueIndexGeneration = store;
            }

            cursor = registration.Method.OpenProbe(in request);

            return true;
        }
    }

    /// <summary>
    /// Installs a recovered value-index sidecar image into the registered access methods and stamps
    /// the current generation as built, so the first probe serves warm instead of paying the rebuild.
    /// All-or-nothing: EVERY composed registration must find its matching image entry (by datatype and
    /// declared predicate identity) AND accept its payload (each method validates its own
    /// configuration stamps), else nothing is stamped and the first probe rebuilds cold from the
    /// pinned store — a partially installed or configuration-mismatched warm state is never served.
    /// Declines under an empty registry and under deferred residency before the store materialises
    /// (both leave the always-correct cold path).
    /// </summary>
    /// <param name="image">The verified sidecar image.</param>
    /// <returns><see langword="true"/> when every registration installed and the generation is stamped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    public bool TryInstallValueIndexSnapshots(ValueIndexImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if(ValueIndexes.IsEmpty)
        {
            return false;
        }

        lock(StateLock)
        {
            if(store is null)
            {
                return false;
            }

            for(int i = 0; i < ValueIndexes.Registrations.Count; i++)
            {
                ValueIndexRegistration registration = ValueIndexes.Registrations[i];
                ValueIndexImageEntry? entry = FindImageEntry(image, registration);
                if(entry is null || !registration.Method.TryInstallSnapshot(entry.Payload.Span))
                {
                    valueIndexGeneration = null;

                    return false;
                }
            }

            valueIndexGeneration = store;

            return true;
        }
    }

    /// <summary>Finds the image entry whose axis identity — datatype IRI and declared predicate IRI(s) — matches a registration, or <see langword="null"/> when none does.</summary>
    /// <param name="image">The sidecar image.</param>
    /// <param name="registration">The registration to match.</param>
    /// <returns>The matching entry, or <see langword="null"/>.</returns>
    private static ValueIndexImageEntry? FindImageEntry(ValueIndexImage image, ValueIndexRegistration registration)
    {
        for(int i = 0; i < image.Entries.Count; i++)
        {
            ValueIndexImageEntry entry = image.Entries[i];
            if(entry.DatatypeIri.Equals(registration.Method.DatatypeIri)
                && entry.StartPredicateIri.Equals(registration.Axis.StartPredicateIri)
                && Nullable.Equals(entry.EndPredicateIri, registration.Axis.EndPredicateIri))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluates the query on the engine the policy selects,
    /// announcing the decision on the trace bus. The parameter
    /// surface mirrors <see cref="HypertrieGraphStore.QueryAsync"/>.
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
    /// <param name="hints">What this one query asks of its join, per axis. A hint outranks the selector and yields to every policy force; the default hints nothing, and an access-controlled query is never put to hints at all.</param>
    /// <param name="cancellationToken">Cancellation token threaded into evaluation.</param>
    /// <returns>An async sequence of solutions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public IAsyncEnumerable<Solution> QueryAsync(
        BasicGraphPattern query,
        TimeProvider timeProvider,
        Planner? planner = null,
        AprioriCardinalities? cardinalities = null,
        AccessControlDelegate? accessControl = null,
        AccessContext? accessContext = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        IdentifierDelegate? identifiers = null,
        JoinQueryHints hints = default,
        CancellationToken cancellationToken = default)
    {
        return QueryAsync(pinnedStore: null, query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, correlationId, identifiers, hints, cancellationToken);
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
    /// <param name="hints">What this one query asks of its join, per axis. A hint outranks the selector and yields to every policy force; the default hints nothing, and an access-controlled query is never put to hints at all.</param>
    /// <param name="cancellationToken">Cancellation token threaded into evaluation.</param>
    /// <returns>An async sequence of solutions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public IAsyncEnumerable<Solution> QueryAsync(
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
        JoinQueryHints hints = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        HypertrieGraphStore? currentStore;
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

            return pinnedStore.QueryAsync(
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
            //Deferred residency, trie unbuilt: a rotation-incompatible (cyclic) shape under a reduced order set
            //needs the self-index (built from the trie) or the trie itself, neither of which the warm view can
            //supply. Materialise the trie on demand.
            if(currentStore is null)
            {
                return QueryTrieDeferredAsync(
                    query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, hints, cancellationToken);
            }

            IAsyncEnumerable<Solution>? selfIndexed = TryQuerySelfIndexAsync(
                currentStore, query, timeProvider, accessControl, traceHandler, effectiveCorrelationId, cancellationToken);

            if(selfIndexed is not null)
            {
                return selfIndexed;
            }

            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, EngineSelectionReason.RotationIncompatible, currentStore.Count, buildMilliseconds: 0);

            return currentStore.QueryAsync(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        bool qualifies = shapeQualifies && rotationCompatible;

        if(qualifies && currentView is null && policy.BuildViewOnDemand)
        {
            //Deferred residency, trie unbuilt and no warm view to build from (no sidecar): materialise the trie on
            //demand and answer the join on it. A deferred generation WITH a sidecar takes the currentView-present
            //branch below and never reaches here.
            if(currentStore is null)
            {
                return QueryTrieDeferredAsync(
                    query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, hints, cancellationToken);
            }

            (ColumnarTripleIndex? ensured, EngineSelectionReason ensureReason, long buildMilliseconds) = EnsureView(currentStore);

            if(ensured is not null)
            {
                //A racing query may have built the view first; it then arrives
                //here as a reuse with zero build cost. EnsureView reports which.
                currentView = ensured;

                return RouteOverViewAsync(
                    currentView, query, ensureReason, buildMilliseconds,
                    planner, cardinalities, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, hints, cancellationToken);
            }

            //No view to pair with: either an off-path build is in flight on the
            //compute lane (ViewBuilding) or a commit advanced the rendezvous past
            //the generation this query read (SnapshotSuperseded). Either way the
            //read store stays the consistent one to answer on.
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Hypertrie, ensureReason, currentStore.Count, buildMilliseconds: 0);

            return currentStore.QueryAsync(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, cancellationToken);
        }

        if(qualifies && currentView is not null)
        {
            return RouteOverViewAsync(
                currentView, query, EngineSelectionReason.ViewReused, buildMilliseconds: 0,
                planner, cardinalities, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, hints, cancellationToken);
        }

        //Under deferred hypertrie residency, a present columnar view answers a columnar-capable shape the join
        //thresholds did not route to it — a single pattern, say — without holding the hypertrie. The leapfrog
        //columnar driver (QueryColumnar) consults access control per candidate, materialising each pattern's full
        //triple from the solution (per-solution provenance), so a SINGLE pattern — whose output row IS the triple,
        //with no hidden intermediate to leak — is filtered leak-free and byte-identically to the trie. An
        //access-controlled single pattern is therefore admitted here too; a multi-pattern sub-threshold shape under
        //access control stays conservatively on the trie (the leapfrog filters it leak-free as well, but that
        //widening is a later step). The batched and Free Join fast paths carry no per-candidate provenance and have
        //already declined access control above. This is what lets a warm-loaded read generation serve without
        //holding the trie at all; it never BUILDS a view for this, and Eager residency keeps these shapes on the trie.
        if(policy.HypertrieResidency == HypertrieResidency.Deferred
            && currentView is not null
            && IsColumnarCapable(query)
            && (accessControl is null || query.Patterns.Count == 1))
        {
            EmitEngineSelected(
                timeProvider, traceHandler, effectiveCorrelationId,
                QueryEngineKind.Columnar, EngineSelectionReason.ViewReused, currentView.TripleCount, buildMilliseconds: 0);

            return QueryColumnarAsync(currentView, query, planner, cardinalities, timeProvider, accessControl, accessContext, traceHandler, effectiveCorrelationId, cancellationToken);
        }

        //Deferred residency, trie unbuilt, and the shape is the trie's home (an access-controlled query — ACL
        //always stays on the trie — a per-pattern self-join, or any single-pattern lookup with no warm view to
        //divert it): materialise the trie on demand and answer on it.
        if(currentStore is null)
        {
            return QueryTrieDeferredAsync(
                query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, effectiveCorrelationId, identifiers, hints, cancellationToken);
        }

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            QueryEngineKind.Hypertrie, EngineSelectionReason.SystemOfRecord, currentStore.Count, buildMilliseconds: 0);

        return currentStore.QueryAsync(
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
    /// Whether the deferred system-of-record trie has been materialised: <see langword="false"/> for a
    /// deferred-residency rendezvous still serving purely from its warm columnar view, <see langword="true"/> once a
    /// query forced the build (and always for an eager rendezvous). Exposed for tests asserting that a warm read
    /// generation serves without ever building the trie, and the build happens at most once.
    /// </summary>
    internal bool IsTrieMaterialized
    {
        get
        {
            lock(StateLock)
            {
                return store is not null;
            }
        }
    }

    /// <summary>The number of deferred trie-build turns that have run — zero while a deferred-residency rendezvous serves purely from its warm view, one once a query forced the build, and more only after a faulted build cleared the slot and a later caller re-kicked. Exposed for tests asserting concurrent first-trie-need queries share a single build and that a faulted build is retried rather than poisoning the rendezvous.</summary>
    internal int TrieBuildCount
    {
        get
        {
            lock(StateLock)
            {
                return trieBuildCount;
            }
        }
    }

    /// <summary>
    /// Materialises the deferred hypertrie system of record on demand, building it at most once across concurrent
    /// callers, and returns it. An eager rendezvous (or an already-materialised deferred one) returns the resident
    /// store immediately. The shared build runs under an independent token, so one caller's cancellation stops only
    /// that caller's wait, never the build the other callers await.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts THIS caller's wait for the build; the shared build itself runs to completion regardless.</param>
    /// <returns>The materialised — or already-resident — system-of-record store.</returns>
    public async ValueTask<HypertrieGraphStore> MaterializeTrieAsync(CancellationToken cancellationToken = default)
    {
        Task<HypertrieGraphStore> build = EnsureTrieBuild();

        return await build.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the shared trie-materialisation task, installing it exactly once: a resident store completes
    /// synchronously; a deferred-unbuilt one kicks the single build turn (a racer reuses the in-flight task). Never
    /// awaits inside the lock — it only obtains the task the caller awaits off the lock, and the kicked turn yields
    /// before any build work so the materialisation runs off <see cref="StateLock"/>.
    /// </summary>
    /// <returns>The task that completes with the materialised store.</returns>
    private Task<HypertrieGraphStore> EnsureTrieBuild()
    {
        lock(StateLock)
        {
            if(store is not null)
            {
                return Task.FromResult(store);
            }

            trieBuildInFlight ??= new TrieBuildTurn(this).RunAsync();

            return trieBuildInFlight;
        }
    }

    /// <summary>
    /// Builds the deferred trie off the lock and publishes it under <see cref="StateLock"/>: on success the store is
    /// set, the deferred source disposed (its recovered-triple buffer returned), and the in-flight slot cleared; on
    /// failure the slot is cleared so a later caller retries rather than awaiting a permanently faulted task.
    /// </summary>
    /// <returns>The materialised system-of-record store.</returns>
    private async Task<HypertrieGraphStore> BuildTrieAsync()
    {
        //Yield before any build work so the expensive materialisation (the synchronous sort prefix included) runs
        //off StateLock: EnsureTrieBuild kicked this turn while holding the lock, and a build under it would
        //serialise every concurrent query behind the cold start.
        await Task.Yield();

        DeferredTrieSource source;
        lock(StateLock)
        {
            //deferredSource is set at construction and only cleared by this method after a successful build, and the
            //turn is installed exactly once while the store is null, so it is non-null here.
            source = deferredSource!;
            trieBuildCount++;
        }

        try
        {
            //An independent token: the shared build is never cancelled by one awaiting caller. Per-caller
            //cancellation is honoured by MaterializeTrieAsync's WaitAsync.
            HypertrieGraphStore trie = await source.BuildAsync(CancellationToken.None).ConfigureAwait(false);

            lock(StateLock)
            {
                store = trie;
                deferredSource?.Dispose();
                deferredSource = null;
                trieBuildInFlight = null;

                return trie;
            }
        }
        catch
        {
            lock(StateLock)
            {
                trieBuildInFlight = null;
            }

            throw;
        }
    }

    /// <summary>
    /// The trie-materialisation turn: holds the rendezvous rather than capturing it in a closure, matching the
    /// project's no-closure convention (a peer of <see cref="ViewBuildTurn"/> and <see cref="SelfIndexBuildTurn"/>).
    /// </summary>
    private sealed class TrieBuildTurn
    {
        /// <summary>The rendezvous whose deferred trie this turn materialises.</summary>
        private readonly QueryEngineRendezvous owner;

        /// <summary>Constructs the turn over <paramref name="owner"/>.</summary>
        /// <param name="owner">The rendezvous.</param>
        public TrieBuildTurn(QueryEngineRendezvous owner)
        {
            this.owner = owner;
        }

        /// <summary>Runs the build. Method-group convertible, no closure.</summary>
        /// <returns>The task that completes with the materialised store.</returns>
        public Task<HypertrieGraphStore> RunAsync()
        {
            return owner.BuildTrieAsync();
        }
    }

    /// <summary>
    /// The deferred system-of-record path: materialises the hypertrie on first demand — awaited inside this
    /// consumer-driven iterator, so the synchronous <see cref="QueryAsync(HypertrieGraphStore?, BasicGraphPattern, TimeProvider, Planner?, AprioriCardinalities?, AccessControlDelegate?, AccessContext?, TraceHandler{QueryTraceEvent}?, Guid, IdentifierDelegate?, CancellationToken)"/>
    /// never blocks — then evaluates the query on it. Reached only under <see cref="HypertrieResidency.Deferred"/>
    /// before the trie is built, for a shape the warm view cannot serve (an access-controlled query, a per-pattern
    /// self-join, or a cyclic shape without a self-index). Identical in result to the eager trie path.
    /// </summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on the emitted selection event.</param>
    /// <param name="planner">The planner, or <c>null</c> for the default.</param>
    /// <param name="cardinalities">A-priori cardinalities, or <c>null</c>.</param>
    /// <param name="accessControl">The access-control policy, consulted per candidate on the materialised trie.</param>
    /// <param name="accessContext">The access context handed to <paramref name="accessControl"/>.</param>
    /// <param name="traceHandler">Optional trace handler the selection event is emitted to.</param>
    /// <param name="correlationId">The correlation id stamped on the selection event.</param>
    /// <param name="identifiers">The identifier source threaded into the trie evaluation.</param>
    /// <param name="hints">The query's join hints. The system of record serves no view-borne route, so no axis of them names anything this path can take; the parameter keeps the hint lane one shape across every branch the query surface dispatches to.</param>
    /// <param name="cancellationToken">A token that aborts the materialisation wait and the evaluation.</param>
    /// <returns>The trie's solution sequence.</returns>
    private async IAsyncEnumerable<Solution> QueryTrieDeferredAsync(
        BasicGraphPattern query,
        TimeProvider timeProvider,
        Planner? planner,
        AprioriCardinalities? cardinalities,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        IdentifierDelegate? identifiers,
        JoinQueryHints hints,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HypertrieGraphStore materialized = await MaterializeTrieAsync(cancellationToken).ConfigureAwait(false);

        EmitEngineSelected(
            timeProvider, traceHandler, correlationId,
            QueryEngineKind.Hypertrie, EngineSelectionReason.SystemOfRecord, materialized.Count, buildMilliseconds: 0);

        await foreach(Solution solution in materialized.QueryAsync(
            query, timeProvider, planner, cardinalities, accessControl, accessContext, traceHandler, correlationId, identifiers, cancellationToken).ConfigureAwait(false))
        {
            yield return solution;
        }
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
    private (ColumnarTripleIndex? View, EngineSelectionReason Reason, long BuildMilliseconds) EnsureView(HypertrieGraphStore expectedStore)
    {
        lock(StateLock)
        {
            if(!ReferenceEquals(store, expectedStore))
            {
                return (null, EngineSelectionReason.SnapshotSuperseded, 0L);
            }

            if(columnarView is not null)
            {
                return (columnarView, EngineSelectionReason.ViewReused, 0L);
            }

            if(computeLane is not null)
            {
                //Off the serve path: admit one build turn for this generation and serve
                //this query from the system of record while it runs. A shed admission
                //falls through to the inline build below so the view is never starved.
                if(viewBuildPending)
                {
                    return (null, EngineSelectionReason.ViewBuilding, 0L);
                }

                viewBuildPending = true;
                if(computeLane.Admit(ComputeWorkClass.ViewBuild, new ViewBuildTurn(this, expectedStore).RunAsync) == ComputeAdmission.Admitted)
                {
                    return (null, EngineSelectionReason.ViewBuilding, 0L);
                }

                viewBuildPending = false;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            ColumnarTripleIndex view = ColumnarTripleIndex.Build(expectedStore.Match(TermId.None, TermId.None, TermId.None), policy.OrderSetMode, backing: policy.ColumnPayloadBacking);
            stopwatch.Stop();

            columnarView = view;

            return (view, EngineSelectionReason.ViewBuilt, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// A delta-free columnar index over the current generation's triples for read-only graph analytics: the
    /// serving columnar view when it is already built and carries no pending delta — the near-free path that pays
    /// nothing for an index a columnar query already materialised — and otherwise a freshly built delta-free index
    /// from the system of record, leaving the serving view untouched. <see langword="null"/> only when the system
    /// of record is not materialised (deferred residency before the first build), where the caller loads its own.
    /// The method never mutates the rendezvous, so it runs safely alongside concurrent queries and commits; the
    /// fast path returns under the lock and any build runs outside it over the immutable store snapshot.
    /// </summary>
    /// <returns>A delta-free index over the current triples, or <see langword="null"/> when the store is not materialised.</returns>
    public ColumnarTripleIndex? TryGetAnalyticsView()
    {
        HypertrieGraphStore snapshot;
        ColumnarOrderSetMode orderSetMode;
        ColumnPayloadBacking backing;

        lock(StateLock)
        {
            if(columnarView is { HasDelta: false })
            {
                return columnarView;
            }

            if(store is null)
            {
                return null;
            }

            snapshot = store;
            orderSetMode = policy.OrderSetMode;
            backing = policy.ColumnPayloadBacking;
        }

        return ColumnarTripleIndex.Build(snapshot.Match(TermId.None, TermId.None, TermId.None), orderSetMode, backing: backing);
    }

    /// <summary>
    /// Builds a delta-free analytics view filtered to the triples an access-control policy authorizes for
    /// <paramref name="context"/>: every triple of the system of record is put to <paramref name="accessControl"/>,
    /// and only those it answers <see cref="AccessDecision.Allow"/> are admitted — a <see cref="AccessDecision.Deny"/>
    /// or <see cref="AccessDecision.NotFound"/> triple is dropped, exactly as the query path withholds it. This is the
    /// access-scoped analytics index: a policy-bearing engine computes graph analytics over only what the caller may
    /// see, so no hidden triple reaches an algorithm. A context is required, like the access-controlled query path —
    /// the policy decides against it. Under deferred residency with the trie unbuilt the system of record is
    /// materialised on demand (the filtered build consults the policy per triple, which the warm columnar view alone
    /// cannot supply), at most once across concurrent callers; an eager rendezvous always has a resident store. The
    /// per-triple scan runs over an immutable store snapshot, so it is safe alongside concurrent queries and commits.
    /// </summary>
    /// <param name="accessControl">The policy consulted per candidate triple.</param>
    /// <param name="context">The caller's access context the policy decides against; required, since a policy with no context cannot decide.</param>
    /// <param name="cancellationToken">A token that aborts the filtered build.</param>
    /// <returns>A delta-free index over the authorized triples.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="accessControl"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="context"/> is <see langword="null"/>: an access context must be supplied when access control is configured, as on the query path.</exception>
    public async ValueTask<ColumnarTripleIndex?> BuildFilteredAnalyticsViewAsync(AccessControlDelegate accessControl, AccessContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessControl);
        if(context is null)
        {
            throw new ArgumentException("An access context must be supplied when access control is configured.", nameof(context));
        }

        HypertrieGraphStore? snapshot;
        ColumnarOrderSetMode orderSetMode;
        ColumnPayloadBacking backing;

        lock(StateLock)
        {
            snapshot = store;
            orderSetMode = policy.OrderSetMode;
            backing = policy.ColumnPayloadBacking;
        }

        //Deferred residency, trie unbuilt: the warm view has no per-triple authorization point, so the filtered
        //build needs the system of record. Materialise it on demand (the same transition the deferred query paths
        //take), built at most once across concurrent callers; an eager rendezvous always has a resident store.
        snapshot ??= await MaterializeTrieAsync(cancellationToken).ConfigureAwait(false);

        List<EncodedTriple> authorized = [];
        foreach(EncodedTriple triple in snapshot.Match(TermId.None, TermId.None, TermId.None))
        {
            AccessDecision decision = await accessControl(new AccessRequest(triple, context), cancellationToken).ConfigureAwait(false);
            if(decision == AccessDecision.Allow)
            {
                authorized.Add(triple);
            }
        }

        return ColumnarTripleIndex.Build(authorized, orderSetMode, backing: backing);
    }

    /// <summary>
    /// Materialises the columnar view for <paramref name="expectedStore"/>
    /// off the serve path — the body of the compute-lane view-build turn.
    /// The expensive build runs without the lock; the result is installed
    /// under <see cref="StateLock"/> only if the store has not advanced
    /// past the build's generation, so a commit that lands mid-build
    /// discards the stale view and the next qualifying query rebuilds.
    /// </summary>
    /// <param name="expectedStore">The store generation the build was admitted against.</param>
    private void BuildViewOffLane(HypertrieGraphStore expectedStore)
    {
        lock(StateLock)
        {
            //A commit may have landed between admission and this turn running.
            if(!ReferenceEquals(store, expectedStore) || columnarView is not null)
            {
                viewBuildPending = false;

                return;
            }
        }

        ColumnarTripleIndex view = ColumnarTripleIndex.Build(expectedStore.Match(TermId.None, TermId.None, TermId.None), policy.OrderSetMode, backing: policy.ColumnPayloadBacking);

        lock(StateLock)
        {
            if(ReferenceEquals(store, expectedStore) && columnarView is null)
            {
                columnarView = view;
            }

            viewBuildPending = false;
        }
    }

    /// <summary>
    /// The compute-lane turn that builds the on-demand columnar view off
    /// the serve path. Holds the rendezvous and the store generation the
    /// build was admitted against rather than capturing them in a closure,
    /// matching the project's no-closure convention.
    /// </summary>
    private sealed class ViewBuildTurn
    {
        /// <summary>The rendezvous whose view this turn materialises.</summary>
        private readonly QueryEngineRendezvous owner;

        /// <summary>The store generation the build was admitted against.</summary>
        private readonly HypertrieGraphStore expectedStore;

        /// <summary>Constructs the turn over <paramref name="owner"/> for <paramref name="expectedStore"/>.</summary>
        /// <param name="owner">The rendezvous.</param>
        /// <param name="expectedStore">The store generation to build from.</param>
        public ViewBuildTurn(QueryEngineRendezvous owner, HypertrieGraphStore expectedStore)
        {
            this.owner = owner;
            this.expectedStore = expectedStore;
        }

        /// <summary>Runs the build. Method-group convertible to <see cref="ComputeWorkDelegate"/>.</summary>
        /// <param name="cancellationToken">Unused; the build runs to completion.</param>
        /// <returns>A completed task — the build is synchronous CPU work.</returns>
        public ValueTask RunAsync(CancellationToken cancellationToken)
        {
            owner.BuildViewOffLane(expectedStore);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Materialises the self-index for the given store generation, exactly
    /// once, under the same lock discipline as <see cref="EnsureView"/>: the
    /// self-index always pairs with the store published under the same lock,
    /// and a commit that advanced the rendezvous past the expected generation
    /// yields none.
    /// </summary>
    /// <param name="expectedStore">The store generation the caller's decision was made against.</param>
    /// <returns>The self-index paired with <paramref name="expectedStore"/> and whether this call built it, or no self-index when the generation was superseded.</returns>
    private (TripleSelfIndex? Index, bool IsBuilt, long BuildMilliseconds) EnsureSelfIndex(HypertrieGraphStore expectedStore)
    {
        lock(StateLock)
        {
            if(!ReferenceEquals(store, expectedStore))
            {
                return (null, false, 0L);
            }

            if(selfIndexView is not null)
            {
                return (selfIndexView, false, 0L);
            }

            if(computeLane is not null)
            {
                //Off the serve path: admit one self-index build turn and serve this query from the
                //system of record while it runs. A shed admission falls through to the inline build.
                if(selfIndexBuildPending)
                {
                    return (null, false, 0L);
                }

                selfIndexBuildPending = true;
                if(computeLane.Admit(ComputeWorkClass.ViewBuild, new SelfIndexBuildTurn(this, expectedStore).RunAsync) == ComputeAdmission.Admitted)
                {
                    return (null, false, 0L);
                }

                selfIndexBuildPending = false;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            TripleSelfIndex index = TripleSelfIndex.Build(expectedStore.Match(TermId.None, TermId.None, TermId.None));
            stopwatch.Stop();

            selfIndexView = index;

            return (index, true, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Materialises the succinct self-index for <paramref name="expectedStore"/>
    /// off the serve path — the body of the compute-lane self-index build
    /// turn. The build runs without the lock; the result installs under
    /// <see cref="StateLock"/> only if the store has not advanced past the
    /// build's generation.
    /// </summary>
    /// <param name="expectedStore">The store generation the build was admitted against.</param>
    private void BuildSelfIndexOffLane(HypertrieGraphStore expectedStore)
    {
        lock(StateLock)
        {
            if(!ReferenceEquals(store, expectedStore) || selfIndexView is not null)
            {
                selfIndexBuildPending = false;

                return;
            }
        }

        TripleSelfIndex index = TripleSelfIndex.Build(expectedStore.Match(TermId.None, TermId.None, TermId.None));

        lock(StateLock)
        {
            if(ReferenceEquals(store, expectedStore) && selfIndexView is null)
            {
                selfIndexView = index;
            }

            selfIndexBuildPending = false;
        }
    }

    /// <summary>
    /// The compute-lane turn that builds the succinct self-index off the
    /// serve path. Holds the rendezvous and the store generation rather
    /// than capturing them in a closure, matching the no-closure convention.
    /// </summary>
    private sealed class SelfIndexBuildTurn
    {
        /// <summary>The rendezvous whose self-index this turn materialises.</summary>
        private readonly QueryEngineRendezvous owner;

        /// <summary>The store generation the build was admitted against.</summary>
        private readonly HypertrieGraphStore expectedStore;

        /// <summary>Constructs the turn over <paramref name="owner"/> for <paramref name="expectedStore"/>.</summary>
        /// <param name="owner">The rendezvous.</param>
        /// <param name="expectedStore">The store generation to build from.</param>
        public SelfIndexBuildTurn(QueryEngineRendezvous owner, HypertrieGraphStore expectedStore)
        {
            this.owner = owner;
            this.expectedStore = expectedStore;
        }

        /// <summary>Runs the build. Method-group convertible to <see cref="ComputeWorkDelegate"/>.</summary>
        /// <param name="cancellationToken">Unused; the build runs to completion.</param>
        /// <returns>A completed task — the build is synchronous CPU work.</returns>
        public ValueTask RunAsync(CancellationToken cancellationToken)
        {
            owner.BuildSelfIndexOffLane(expectedStore);

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Routes a rotation-incompatible query to the worst-case-optimal join
    /// over the succinct self-index when the policy opts in and no
    /// per-candidate access control is wired (the self-index path has no
    /// consultation point, like the batched and Free Join paths), or returns
    /// <see langword="null"/> for the system-of-record fallback — when the
    /// policy opts out, access control is wired, the on-demand build is
    /// disabled with no self-index materialised, the generation was
    /// superseded, or the pipeline declines the shape.
    /// </summary>
    /// <param name="currentStore">The store generation the routing decision was made against.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock for trace events.</param>
    /// <param name="accessControl">The access-control policy; non-<see langword="null"/> disqualifies the self-index path.</param>
    /// <param name="traceHandler">Optional trace handler.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the flattening.</param>
    /// <returns>The self-index solution stream, or <see langword="null"/>.</returns>
    private IAsyncEnumerable<Solution>? TryQuerySelfIndexAsync(
        HypertrieGraphStore currentStore,
        BasicGraphPattern query,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if(!policy.PreferSelfIndex || accessControl is not null)
        {
            return null;
        }

        TripleSelfIndex? existing;
        lock(StateLock)
        {
            existing = selfIndexView;
        }

        if(existing is null && !policy.BuildViewOnDemand)
        {
            return null;
        }

        (TripleSelfIndex? selfIndex, bool isBuilt, long buildMilliseconds) = EnsureSelfIndex(currentStore);
        if(selfIndex is null)
        {
            return null;
        }

        IEnumerable<SolutionBatch>? batches = SelfIndexPipeline.Run(selfIndex, query);
        if(batches is null)
        {
            return null;
        }

        EmitEngineSelected(
            timeProvider, traceHandler, correlationId,
            QueryEngineKind.SelfIndex, isBuilt ? EngineSelectionReason.ViewBuilt : EngineSelectionReason.ViewReused, selfIndex.Count, buildMilliseconds);

        return SolutionBatch.FlattenAsync(batches, cancellationToken);
    }

    /// <summary>
    /// The columnar batched pipeline as a <em>raw batch stream</em> rather than a flattened per-row
    /// <see cref="Solution"/> sequence: when this query qualifies for the batched scan-and-hash path (the same
    /// gating <see cref="QueryAsync(HypertrieGraphStore?, BasicGraphPattern, TimeProvider, Planner?, AprioriCardinalities?, AccessControlDelegate?, AccessContext?, TraceHandler{QueryTraceEvent}?, Guid, IdentifierDelegate?, CancellationToken)"/>
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

        HypertrieGraphStore? currentStore;
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

            //Deferred residency, trie unbuilt and no warm view to build from: decline the columnar fast path; the
            //caller's per-row evaluation materialises the trie on demand.
            if(currentStore is null)
            {
                return null;
            }

            (view, reason, buildMilliseconds) = EnsureView(currentStore);
            if(view is null)
            {
                //A commit advanced the rendezvous past the read generation; Query answers on the consistent store.
                return null;
            }

            //EnsureView set the reason to ViewBuilt (just materialised) or ViewReused.
        }

        //This entry point is a caller's request for the batched columnar FORM rather than a route choice,
        //so it stays selector-blind and takes the policy's factorisation flags verbatim — the standing
        //behaviour an unstated engagement axis names.
        JoinStrategyChoice choice = FactorizationChoiceOf(FactorizationEngagement.Unspecified);
        ColumnarBatchPlan? plan = ColumnarBatchPipeline.TryPlan(view, query, choice.SemijoinReduction, choice.FactorizedStar, choice.FactorizedChain);
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

        return (plan.Schema, ColumnarBatchPipeline.Run(view, plan, arenaPool));
    }

    /// <summary>
    /// Counts the query's solutions on the columnar view WITHOUT materialising
    /// them, when the batched path applies and the shape factorises — the
    /// consumer that keeps the compressed form: the same gating as
    /// <see cref="TryQueryBatchedColumns"/>, then one join-route consultation
    /// deciding which factorised form counts the shape — the Free Join
    /// factorised face where the decision's statistics justify it, otherwise
    /// <see cref="ColumnarBatchPipeline.TryCount"/> — whose result equals the
    /// drained row count exactly either way, since both count the same answer.
    /// Returns <see langword="null"/> for every
    /// other case (a superseded snapshot, a non-qualifying or
    /// rotation-incompatible shape, a non-factorisable plan, access control,
    /// or the batched policy disabled); the caller then evaluates and counts
    /// normally, so a conservative <see langword="null"/> is always sound.
    /// </summary>
    /// <param name="pinnedStore">The store generation the caller's snapshot pinned, or <c>null</c> to use the rendezvous's current generation.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on the emitted selection event.</param>
    /// <param name="accessControl">The access-control policy; non-<c>null</c> declines the count (it has no per-candidate consultation point).</param>
    /// <param name="traceHandler">Optional trace handler the selection event is emitted to.</param>
    /// <param name="correlationId">Correlation id stamped on the selection event. Pass <see cref="Guid.Empty"/> to generate a fresh one.</param>
    /// <param name="identifiers">The identifier source used to mint a fresh correlation id when <paramref name="correlationId"/> is <see cref="Guid.Empty"/>; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <returns>The solution count, or <see langword="null"/> when the count fast path does not apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public long? TryCountBatched(
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

        if(accessControl is not null || !policy.PreferBatchedForAcyclic)
        {
            return null;
        }

        HypertrieGraphStore? currentStore;
        ColumnarTripleIndex? currentView;

        lock(StateLock)
        {
            currentStore = store;
            currentView = columnarView;
        }

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

            //Deferred residency, trie unbuilt and no warm view to build from: decline the columnar fast path; the
            //caller's per-row evaluation materialises the trie on demand.
            if(currentStore is null)
            {
                return null;
            }

            (view, reason, buildMilliseconds) = EnsureView(currentStore);
            if(view is null)
            {
                return null;
            }

            //EnsureView set the reason to ViewBuilt (just materialised) or ViewReused.
        }

        //The count is a route decision like any other, taken at the same seam on the same statistics: the
        //composed decision's factorisation axis is what says whether the Free Join factorised face — which
        //counts through the representation instead of flattening it — is the shape's cheaper counter. The
        //count API carries no hints, so the composition reduces to force, then selector, then the standing
        //behaviour; the answer is identical either way, so a declined face costs only the fall-back.
        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(view, query, in policy);
        JoinSelectionDecision decision = ComposeDecision(view, query, in features, hints: default, CancellationToken.None);

        QueryEngineKind engine = QueryEngineKind.ColumnarBatched;
        long? count = null;

        if(decision.Factorization is FactorizationEngagement.Star or FactorizationEngagement.Chain)
        {
            count = TryCountThroughFactorizedFace(view, query, TrieBuildOf(decision.Build));
            if(count is not null)
            {
                engine = QueryEngineKind.FreeJoin;
            }
        }

        count ??= ColumnarBatchPipeline.TryCount(view, query, arenaPool);
        if(count is null)
        {
            return null;
        }

        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            engine, reason, view.TripleCount, buildMilliseconds, features, decision);

        return count;
    }

    /// <summary>
    /// Counts the query's solutions through the Free Join factorised face — the answer stays factorised and
    /// the row count is computed through the representation, with no flatten — or returns
    /// <see langword="null"/> when the shape is not one that face serves.
    /// </summary>
    /// <param name="view">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="trieBuild">The build mode the relations' tries materialise under.</param>
    /// <returns>The flat row count, or <see langword="null"/> when the face declines the shape.</returns>
    private long? TryCountThroughFactorizedFace(ColumnarTripleIndex view, BasicGraphPattern query, FreeJoinTrieBuild trieBuild)
    {
        using FactorizedArena arena = new(arenaPool);
        FactorizedBatch? factorized = FreeJoinPipeline.RunFactorized(view, query, arena, trieBuild);

        return factorized?.FlatRowCount;
    }

    /// <summary>
    /// The distinct key projections of the query on the columnar view WITHOUT
    /// materialising its solutions, when the batched path applies and the shape
    /// is a factorisable star whose key covers the projection — the
    /// late-materialisation consumer: the same gating as
    /// <see cref="TryQueryBatchedColumns"/>, then
    /// <see cref="ColumnarBatchPipeline.TryDistinctKeys"/>, whose rows equal
    /// the drained-projected-deduplicated rows exactly. Returns
    /// <see langword="null"/> for every other case; the caller then evaluates
    /// and deduplicates normally, so a conservative <see langword="null"/> is
    /// always sound.
    /// </summary>
    /// <param name="pinnedStore">The store generation the caller's snapshot pinned, or <c>null</c> to use the rendezvous's current generation.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="projection">The projected variables, distinct, all expected to be star-key variables.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on the emitted selection event.</param>
    /// <param name="accessControl">The access-control policy; non-<c>null</c> declines (no per-candidate consultation point).</param>
    /// <param name="traceHandler">Optional trace handler the selection event is emitted to.</param>
    /// <param name="correlationId">Correlation id stamped on the selection event. Pass <see cref="Guid.Empty"/> to generate a fresh one.</param>
    /// <param name="identifiers">The identifier source used to mint a fresh correlation id when <paramref name="correlationId"/> is <see cref="Guid.Empty"/>; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <returns>The distinct projected rows as batches over <paramref name="projection"/>, or <see langword="null"/> when the fast path does not apply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/>, <paramref name="projection"/>, or <paramref name="timeProvider"/> is <c>null</c>.</exception>
    public List<SolutionBatch>? TryDistinctKeysBatched(
        HypertrieGraphStore? pinnedStore,
        BasicGraphPattern query,
        IReadOnlyList<Variable> projection,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl = null,
        TraceHandler<QueryTraceEvent>? traceHandler = null,
        Guid correlationId = default,
        IdentifierDelegate? identifiers = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if(accessControl is not null || !policy.PreferBatchedForAcyclic)
        {
            return null;
        }

        HypertrieGraphStore? currentStore;
        ColumnarTripleIndex? currentView;

        lock(StateLock)
        {
            currentStore = store;
            currentView = columnarView;
        }

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

            //Deferred residency, trie unbuilt and no warm view to build from: decline the columnar fast path; the
            //caller's per-row evaluation materialises the trie on demand.
            if(currentStore is null)
            {
                return null;
            }

            (view, reason, buildMilliseconds) = EnsureView(currentStore);
            if(view is null)
            {
                return null;
            }

            //EnsureView set the reason to ViewBuilt (just materialised) or ViewReused.
        }

        List<SolutionBatch>? distinct = ColumnarBatchPipeline.TryDistinctKeys(view, query, projection, arenaPool);
        if(distinct is null)
        {
            return null;
        }

        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        EmitEngineSelected(
            timeProvider, traceHandler, effectiveCorrelationId,
            QueryEngineKind.ColumnarBatched, reason, view.TripleCount, buildMilliseconds);

        return distinct;
    }

    /// <summary>
    /// The one place a query with a columnar view in hand chooses its route: the access-control
    /// pre-check, the explicit force, the single join-route selector consultation, the ordered route
    /// attempt, and the one selection event both view-acquisition branches announce through.
    /// </summary>
    /// <remarks>
    /// The seam sits here because this is the only point where all three view-borne routes are
    /// simultaneously available and none has been paid for. A decision the engine cannot serve — a route
    /// that declines the shape, or one this seam does not own — costs a fall-through to the sound default
    /// and never an answer, so no selector can produce a wrong result.
    /// </remarks>
    /// <param name="view">The columnar view the chosen route runs on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="reason">The view-acquisition reason the selection event carries.</param>
    /// <param name="buildMilliseconds">The view build cost paid by this query, when any.</param>
    /// <param name="planner">The planner handed to the leapfrog driver.</param>
    /// <param name="cardinalities">A-priori cardinalities handed to the planner.</param>
    /// <param name="timeProvider">Clock for trace events.</param>
    /// <param name="accessControl">The access-control policy; non-<see langword="null"/> keeps the query off the seam entirely.</param>
    /// <param name="accessContext">The caller-supplied access context.</param>
    /// <param name="traceHandler">Optional trace handler.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="hints">What the caller asked of this one query, per axis.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the route.</param>
    /// <returns>The chosen route's solution stream.</returns>
    private IAsyncEnumerable<Solution> RouteOverViewAsync(
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        EngineSelectionReason reason,
        long buildMilliseconds,
        Planner? planner,
        AprioriCardinalities? cardinalities,
        TimeProvider timeProvider,
        AccessControlDelegate? accessControl,
        AccessContext? accessContext,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        JoinQueryHints hints,
        CancellationToken cancellationToken)
    {
        //Access control stays a property of the engine, not of a policy: the batched and Free Join routes
        //have no per-candidate consultation point, so an access-controlled query is not put to the seam at
        //all — neither to a selector nor to the caller's hints — and goes to the driver that does consult
        //per candidate.
        if(accessControl is not null)
        {
            EmitEngineSelected(
                timeProvider, traceHandler, correlationId,
                QueryEngineKind.Columnar, reason, view.TripleCount, buildMilliseconds);

            return QueryColumnarAsync(view, query, planner, cardinalities, timeProvider,
                accessControl, accessContext, traceHandler, correlationId, cancellationToken);
        }

        JoinSelectionFeatures features = JoinShapeAnalysis.Describe(view, query, in policy);
        JoinSelectionDecision decision = ComposeDecision(view, query, in features, in hints, cancellationToken);

        //The decision's route first, then the sound order. A route that declines the shape, or one this
        //seam does not serve, costs a fall-through and never an answer. A decision naming the leapfrog
        //driver is honoured by the terminal arm: the batched retry excludes it, so an explicit leapfrog
        //choice is never silently upgraded to the batched route.
        QueryEngineKind ran = decision.Route;
        IAsyncEnumerable<Solution>? stream = TryRouteAsync(ran, view, query, in decision, timeProvider, traceHandler, correlationId, cancellationToken);

        if(stream is null && ran != QueryEngineKind.ColumnarBatched && decision.Route != QueryEngineKind.Columnar)
        {
            ran = QueryEngineKind.ColumnarBatched;
            stream = TryRouteAsync(ran, view, query, in decision, timeProvider, traceHandler, correlationId, cancellationToken);
        }

        if(stream is null)
        {
            ran = QueryEngineKind.Columnar;
            stream = QueryColumnarAsync(view, query, planner, cardinalities, timeProvider,
                accessControl, accessContext, traceHandler, correlationId, cancellationToken);
        }

        EmitEngineSelected(
            timeProvider, traceHandler, correlationId,
            ran, reason, view.TripleCount, buildMilliseconds, features, decision);

        return stream;
    }

    /// <summary>
    /// The ONE site the per-query decision is composed at: force, then hint, then selector, then the
    /// engine's standing behaviour — resolved per axis, so no branch can lose an axis another branch keeps.
    /// A set <see cref="QueryEnginePolicy.PreferFreeJoin"/> fixes the route and no selector is consulted at
    /// all; a route hint does the same, since a selector could only be overridden; otherwise the selector is
    /// consulted exactly once and sees the hints for the axes it leaves unspecified. The non-route axes are
    /// then overlaid whatever decided the route, and the decision records which axes a hint actually set.
    /// </summary>
    /// <param name="view">The columnar view the chosen route would run on.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="features">The shape features the decision is taken on.</param>
    /// <param name="hints">What the caller asked of this one query, per axis.</param>
    /// <param name="cancellationToken">The query's token, handed to a supplied selector.</param>
    /// <returns>The composed decision.</returns>
    private JoinSelectionDecision ComposeDecision(
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        in JoinSelectionFeatures features,
        in JoinQueryHints hints,
        CancellationToken cancellationToken)
    {
        bool routeHinted = !policy.PreferFreeJoin && hints.Route != JoinRouteHintKind.None;
        JoinSelectionDecision decision;

        if(policy.PreferFreeJoin)
        {
            decision = JoinSelectionDecision.Forced(QueryEngineKind.FreeJoin);
        }
        else if(routeHinted)
        {
            decision = JoinSelectionDecision.Hinted(RouteOfHint(hints.Route));
        }
        else
        {
            JoinSelectionContext context = new(query, view, features, hints);
            decision = (policy.JoinRouteSelector ?? JoinStrategySelectors.Structural)(in context, cancellationToken);
        }

        return OverlayHintedAxes(decision, in hints, routeHinted);
    }

    /// <summary>
    /// Overlays the non-route axes onto a decision whatever decided its route: a policy force outranks the
    /// hint and the selector alike, a hint outranks the selector, and an axis nothing named keeps the value
    /// the decision already carries. Only an axis a hint actually set is stamped on
    /// <see cref="JoinSelectionDecision.HintedAxes"/>, so a hint that lost to a force claims nothing.
    /// </summary>
    /// <param name="decision">The decision whose route is already resolved.</param>
    /// <param name="hints">What the caller asked of this one query, per axis.</param>
    /// <param name="routeHinted">Whether a hint decided the route.</param>
    /// <returns>The decision with its non-route axes resolved.</returns>
    private JoinSelectionDecision OverlayHintedAxes(JoinSelectionDecision decision, in JoinQueryHints hints, bool routeHinted)
    {
        JoinSelectionHintedAxes hintedAxes = routeHinted ? JoinSelectionHintedAxes.Route : JoinSelectionHintedAxes.None;

        FreeJoinDepthPolicy depth = decision.Depth;
        if(hints.Depth != FreeJoinDepthPolicy.Unspecified)
        {
            depth = hints.Depth;
            hintedAxes |= JoinSelectionHintedAxes.Depth;
        }

        FreeJoinTrieBuildPreference build = decision.Build;
        if(hints.Build != FreeJoinTrieBuildPreference.Unspecified)
        {
            build = hints.Build;
            hintedAxes |= JoinSelectionHintedAxes.Build;
        }

        //The factorisation flags are this rung's only non-route forces, so they reach the axis ahead of both
        //the hint and whatever the route branch decided.
        FactorizationEngagement factorization = ForcedFactorization();
        if(factorization == FactorizationEngagement.Unspecified)
        {
            factorization = decision.Factorization;

            if(hints.Factorization != FactorizationEngagement.Unspecified)
            {
                factorization = hints.Factorization;
                hintedAxes |= JoinSelectionHintedAxes.Factorization;
            }
        }

        return decision with { Depth = depth, Build = build, Factorization = factorization, HintedAxes = hintedAxes };
    }

    /// <summary>The engagement the policy's factorisation flags force, or <see cref="FactorizationEngagement.Unspecified"/> when neither is set. A star and a chain are different shapes, so a policy setting both names the star, the engagement the pipeline resolves first.</summary>
    /// <returns>The forced engagement, or <see cref="FactorizationEngagement.Unspecified"/>.</returns>
    private FactorizationEngagement ForcedFactorization()
    {
        if(policy.PreferFactorizedStar)
        {
            return FactorizationEngagement.Star;
        }

        if(policy.PreferFactorizedChain)
        {
            return FactorizationEngagement.Chain;
        }

        return FactorizationEngagement.Unspecified;
    }

    /// <summary>The view-borne route a set hint names. The caller consults this only for a set hint, so the absent case never reaches it and shares the leapfrog driver's arm.</summary>
    /// <param name="hint">The hinted route.</param>
    /// <returns>The engine that serves it.</returns>
    private static QueryEngineKind RouteOfHint(JoinRouteHintKind hint)
    {
        return hint switch
        {
            JoinRouteHintKind.FreeJoin => QueryEngineKind.FreeJoin,
            JoinRouteHintKind.Batched => QueryEngineKind.ColumnarBatched,
            _ => QueryEngineKind.Columnar,
        };
    }

    /// <summary>The trie build mode a decision names, or the policy's own when it names none.</summary>
    /// <param name="preference">The decision's build axis.</param>
    /// <returns>The build mode the Free Join route uses.</returns>
    private FreeJoinTrieBuild TrieBuildOf(FreeJoinTrieBuildPreference preference)
    {
        return preference switch
        {
            FreeJoinTrieBuildPreference.Eager => FreeJoinTrieBuild.Eager,
            FreeJoinTrieBuildPreference.Lazy => FreeJoinTrieBuild.Lazy,
            _ => policy.FreeJoinTrieBuild,
        };
    }

    /// <summary>
    /// The batched pipeline's own engagement vocabulary for a decision's factorisation axis: an axis that
    /// names nothing leaves the policy's flags standing verbatim, and a named engagement is taken as the
    /// only one.
    /// </summary>
    /// <param name="engagement">The decision's factorisation axis.</param>
    /// <returns>The choice the batched planner consumes.</returns>
    private JoinStrategyChoice FactorizationChoiceOf(FactorizationEngagement engagement)
    {
        return engagement switch
        {
            FactorizationEngagement.None => new JoinStrategyChoice(policy.PreferSemijoinReduction, false, false),
            FactorizationEngagement.Star => new JoinStrategyChoice(policy.PreferSemijoinReduction, true, false),
            FactorizationEngagement.Chain => new JoinStrategyChoice(policy.PreferSemijoinReduction, false, true),
            _ => new JoinStrategyChoice(policy.PreferSemijoinReduction, policy.PreferFactorizedStar, policy.PreferFactorizedChain),
        };
    }

    /// <summary>
    /// Enters one view-borne route, or returns <see langword="null"/> when the route is not one this seam
    /// serves, is disabled by policy, or declines the shape. Emits no SELECTION event: the caller owns the
    /// one selection event, so a fall-through never announces a route that did not run.
    /// </summary>
    /// <param name="route">The route to enter.</param>
    /// <param name="view">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="decision">The composed decision, whose depth, build, and factorisation axes the routes consume.</param>
    /// <param name="timeProvider">Clock for the Free Join route's plan-applied event.</param>
    /// <param name="traceHandler">Optional trace handler.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the flattening.</param>
    /// <returns>The route's solution stream, or <see langword="null"/>.</returns>
    private IAsyncEnumerable<Solution>? TryRouteAsync(
        QueryEngineKind route,
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        in JoinSelectionDecision decision,
        TimeProvider timeProvider,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        return route switch
        {
            QueryEngineKind.FreeJoin => TryFreeJoinRouteAsync(view, query, decision.Depth, TrieBuildOf(decision.Build), timeProvider, traceHandler, correlationId, cancellationToken),
            QueryEngineKind.ColumnarBatched when policy.PreferBatchedForAcyclic => TryBatchedRouteAsync(view, query, decision.Factorization, cancellationToken),
            _ => null,
        };
    }

    /// <summary>
    /// Plans and enters the Free Join flat route, announcing the depths the plan applied before the drain
    /// begins, or returns <see langword="null"/> when the shape has no global descent order.
    /// </summary>
    /// <param name="view">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="depth">The decision's depth axis, threaded into planning.</param>
    /// <param name="trieBuild">The build mode the relations' tries materialise under.</param>
    /// <param name="timeProvider">Clock for the plan-applied event.</param>
    /// <param name="traceHandler">Optional trace handler.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the flattening.</param>
    /// <returns>The Free Join solution stream, or <see langword="null"/>.</returns>
    private IAsyncEnumerable<Solution>? TryFreeJoinRouteAsync(
        ColumnarTripleIndex view,
        BasicGraphPattern query,
        FreeJoinDepthPolicy depth,
        FreeJoinTrieBuild trieBuild,
        TimeProvider timeProvider,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        FreeJoinPlan? plan = FreeJoinPipeline.TryPlan(view, query, depth);
        if(plan is null)
        {
            return null;
        }

        EmitFreeJoinPlanApplied(timeProvider, traceHandler, correlationId, plan);

        return SolutionBatch.FlattenAsync(FreeJoinPipeline.Run(view, plan, trieBuild), cancellationToken);
    }

    /// <summary>
    /// Plans and enters the batched scan-and-hash route, or returns <see langword="null"/> when the shape
    /// has no batched plan. The star/chain engagement is the composed decision's own axis.
    /// </summary>
    /// <param name="view">The columnar view.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="factorization">The composed decision's factorisation axis.</param>
    /// <param name="cancellationToken">Cancellation token threaded into the flattening.</param>
    /// <returns>The batched solution stream, or <see langword="null"/>.</returns>
    private IAsyncEnumerable<Solution>? TryBatchedRouteAsync(ColumnarTripleIndex view, BasicGraphPattern query, FactorizationEngagement factorization, CancellationToken cancellationToken)
    {
        JoinStrategyChoice choice = FactorizationChoiceOf(factorization);
        ColumnarBatchPlan? plan = ColumnarBatchPipeline.TryPlan(view, query, choice.SemijoinReduction, choice.FactorizedStar, choice.FactorizedChain);

        if(plan is null)
        {
            return null;
        }

        return SolutionBatch.FlattenAsync(ColumnarBatchPipeline.Run(view, plan, arenaPool), cancellationToken);
    }

    private IAsyncEnumerable<Solution> QueryColumnarAsync(
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
        return ColumnarHyperCube.QueryAsync(
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

    /// <summary>
    /// Announces one routing decision on the query trace bus. A decision taken through the join-route
    /// selector seam passes the features it was taken on and the decision itself; every other call site
    /// passes neither, and the event carries the defaults that say no selector was consulted.
    /// </summary>
    /// <param name="timeProvider">Clock used to stamp the event.</param>
    /// <param name="traceHandler">The trace handler, or <see langword="null"/> to emit nothing.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="engine">The engine that serves the query — the route that actually ran.</param>
    /// <param name="reason">How the view was acquired.</param>
    /// <param name="tripleCount">The serving engine's triple count.</param>
    /// <param name="buildMilliseconds">The view build cost paid by this query, when any.</param>
    /// <param name="selectionFeatures">The shape features the join-route decision was taken on.</param>
    /// <param name="selectionDecision">The join-route decision that was taken.</param>
    private void EmitEngineSelected(
        TimeProvider timeProvider,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        QueryEngineKind engine,
        EngineSelectionReason reason,
        int tripleCount,
        long buildMilliseconds,
        JoinSelectionFeatures selectionFeatures = default,
        JoinSelectionDecision selectionDecision = default)
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
            buildMilliseconds,
            selectionFeatures,
            selectionDecision);

        traceHandler(in evt);
    }

    /// <summary>
    /// Announces the depths one Free Join plan applied, after planning and before the drain. Emitted by the
    /// Free Join arm alone: no other route plans relations, so no other route announces it.
    /// </summary>
    /// <param name="timeProvider">Clock used to stamp the event.</param>
    /// <param name="traceHandler">The trace handler, or <see langword="null"/> to emit nothing.</param>
    /// <param name="correlationId">The correlation id.</param>
    /// <param name="plan">The plan whose summary values the event carries.</param>
    private void EmitFreeJoinPlanApplied(
        TimeProvider timeProvider,
        TraceHandler<QueryTraceEvent>? traceHandler,
        Guid correlationId,
        FreeJoinPlan plan)
    {
        if(traceHandler is null)
        {
            return;
        }

        long sequence = Interlocked.Increment(ref traceSequence);
        QueryTraceEvent evt = QueryTraceEvent.FreeJoinPlanApplied(
            sequence,
            timeProvider.GetUtcNow().UtcTicks,
            correlationId,
            plan.RelationCount,
            plan.FullDepthRelationCount,
            plan.PlannedTailBearingRelationCount,
            plan.FullDepthRelationMask);

        traceHandler(in evt);
    }
}
