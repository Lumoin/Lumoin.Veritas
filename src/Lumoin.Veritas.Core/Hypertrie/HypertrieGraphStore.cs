using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Core.Hypertrie;

/// <summary>
/// An in-memory graph store backed by a depth-3 hypertrie with
/// content-addressed node deduplication. Each node carries one
/// <see cref="EdgeMap"/> per remaining position, so any
/// combination of bound and unbound subject, predicate, and
/// object positions is answered through a single descent without
/// permutation duplication; structurally-identical subtrees
/// collapse to a single canonical instance via the
/// <see cref="NodeStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a peer of <see cref="InMemoryGraphStore"/> and exposes
/// the same surface for single-pattern matching:
/// <see cref="BuildAsync(IEnumerable{EncodedTriple}, CancellationToken)"/>,
/// <see cref="Count"/>, <see cref="Match"/>,
/// <see cref="AsMatchDelegate"/>,
/// <see cref="AsCountDelegate"/>. Multi-pattern queries are
/// answered by <see cref="QueryAsync"/>, which dispatches to a
/// worst-case-optimal join engine in
/// <see cref="BasicGraphPatternEvaluator"/>.
/// </para>
/// <para>
/// <b>Snapshot lifecycle.</b> Building a store creates a single
/// <see cref="HypertrieSnapshot"/> pinning the constructed root
/// and registering itself with the underlying
/// <see cref="NodeStore"/>. The store holds this snapshot for
/// its lifetime; consumers needing their own independently-held
/// reference can call
/// <see cref="HypertrieSnapshot.Acquire"/> on
/// <see cref="Snapshot"/> and dispose when finished.
/// </para>
/// <para>
/// The store is built once from a sequence of triples and is
/// immutable after construction. Mutability arrives in a future
/// batch through edit sessions, where new edits produce new
/// snapshots without touching nodes referenced by existing
/// snapshots.
/// </para>
/// <para>
/// <b>Build vs sweep.</b> A build runs under the
/// <see cref="NodeStore"/>'s mutation gate in shared mode, so
/// multiple concurrent builds on the same store run together
/// (structural sharing in the intern table makes them naturally
/// compatible). Sweeps take the gate exclusively and wait for
/// every active shared holder to leave. The shared scope is held
/// only over the work that mutates the intern table; triple
/// deduplication runs outside the scope so a long-running sweep
/// does not block on enumeration. Reads against existing
/// snapshots are unaffected.
/// </para>
/// <para>
/// <b>Journal.</b> When the underlying <see cref="NodeStore"/>
/// was constructed with a journal, every successful build writes
/// a <see cref="JournalEntry"/> recording the transition from
/// <see cref="NodeIdentifier.Empty"/> to the new root identifier.
/// A build against a store whose journal head is non-empty fails
/// with <see cref="EditSessionConcurrencyException"/> — initial
/// builds are first-entry-only; subsequent state changes go
/// through edit sessions. Stores constructed without a journal
/// write no entries and are unaffected.
/// </para>
/// <para>
/// <b>Async surface.</b> Building requires entering the
/// <see cref="NodeStore"/>'s shared mutation scope, which is
/// async-only. Both build overloads are therefore async. In the
/// uncontended case the gate completes synchronously and the
/// returned <see cref="ValueTask"/> carries its result without
/// allocation.
/// </para>
/// </remarks>
[DebuggerDisplay("HypertrieGraphStore Count={Count} Nodes={Snapshot.Store.Count}")]
public sealed class HypertrieGraphStore
{
    /// <summary>
    /// The snapshot pinning the root and the
    /// <see cref="NodeStore"/> behind it. Held for the lifetime
    /// of this graph store.
    /// </summary>
    public HypertrieSnapshot Snapshot { get; }

    /// <summary>Gets the number of distinct triples in the store.</summary>
    public int Count { get; }

    private HypertrieGraphStore(HypertrieSnapshot snapshot, int count)
    {
        Snapshot = snapshot;
        Count = count;
    }

    /// <summary>
    /// Builds a new <see cref="HypertrieGraphStore"/> from the
    /// given triples using a fresh <see cref="NodeStore"/> built
    /// with <paramref name="hash"/> and no journal. Convenience
    /// for tests and one-shot loads where the caller does not
    /// need to share or configure the store.
    /// </summary>
    /// <param name="triples">The triples to index.</param>
    /// <param name="hash">The hash function. Pass <see cref="VeritasHashing.Default"/> for the canonical xxHash64 implementation.</param>
    /// <param name="cancellationToken">A token that aborts the build at any per-step check.</param>
    /// <returns>A new immutable graph store.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public static async ValueTask<HypertrieGraphStore> BuildAsync(
        IEnumerable<EncodedTriple> triples,
        VeritasHash hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(hash);

        //Ownership-transfer pattern (CA2000). The freshly allocated
        //store is owned by this method until the inner build
        //completes successfully, at which point ownership passes to
        //the returned graph store via its snapshot. If the inner
        //build throws, the store is orphaned and the finally block
        //disposes it. Setting the local to null on the success path
        //is the explicit signal that ownership has been transferred.
        NodeStore? store = null;
        BuildPools pools = BuildPools.CreateDefault();

        try
        {
            store = new NodeStore(hash, pools.NodePool);

            //Bind the pools to the store's lifetime so disposing
            //the store releases their slab inventory.
            store.AttachToLifetime(pools.NodePool);
            store.AttachToLifetime(pools.KeyPool);
            store.AttachToLifetime(pools.ChildPool);
            store.AttachToLifetime(pools.PermutationPool);

            HypertrieGraphStore result = await BuildAsync(triples, store, pools, cancellationToken).ConfigureAwait(false);
            store = null;

            return result;
        }
        finally
        {
            store?.Dispose();
        }
    }

    /// <summary>
    /// Builds one graph store per element of
    /// <paramref name="graphs"/>, all interning through ONE shared
    /// <see cref="NodeStore"/> and renting from ONE shared pools
    /// bundle — many logical graph tries, one physical arena. The
    /// stores are returned in input order, each holding its own
    /// snapshot pinning its own root.
    /// </summary>
    /// <remarks>
    /// This is the dataset-composition build path. Isolated builds
    /// pay a fixed per-store cost (pool slab inventory, intern
    /// table, pair-arena segment granularity) that dominates
    /// resident memory once graphs number in the thousands; the
    /// shared arena pays it once for the whole family, and
    /// identical subtrees across graphs intern to one canonical
    /// instance. The arena stays reachable while any returned
    /// store's snapshot is.
    /// </remarks>
    /// <param name="graphs">The graphs to index, one triple sequence per graph; an empty sequence produces an empty store.</param>
    /// <param name="hash">The hashing bundle used to derive node identities.</param>
    /// <param name="cancellationToken">A token that aborts the builds at any per-step check.</param>
    /// <returns>The constructed stores, positionally matching <paramref name="graphs"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graphs"/> or <paramref name="hash"/> is <c>null</c>.</exception>
    public static async ValueTask<IReadOnlyList<HypertrieGraphStore>> BuildSharedAsync(
        IReadOnlyList<IEnumerable<EncodedTriple>> graphs,
        VeritasHash hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        ArgumentNullException.ThrowIfNull(hash);

        //Ownership-transfer pattern (CA2000), as in the single-graph
        //overload: the arena is owned here until every build has
        //completed, at which point the returned stores' snapshots
        //collectively keep it alive. A failure midway orphans the
        //arena and the finally block disposes it, releasing the
        //slab inventory the partial builds accumulated.
        NodeStore? store = null;
        BuildPools pools = BuildPools.CreateDefault();

        try
        {
            store = new NodeStore(hash, pools.NodePool);
            store.AttachToLifetime(pools.NodePool);
            store.AttachToLifetime(pools.KeyPool);
            store.AttachToLifetime(pools.ChildPool);
            store.AttachToLifetime(pools.PermutationPool);

            HypertrieGraphStore[] stores = new HypertrieGraphStore[graphs.Count];
            for(int i = 0; i < graphs.Count; i++)
            {
                stores[i] = await BuildAsync(graphs[i], store, pools, cancellationToken).ConfigureAwait(false);
            }

            store = null;

            return stores;
        }
        finally
        {
            store?.Dispose();
        }
    }

    /// <summary>
    /// Builds a new <see cref="HypertrieGraphStore"/> from the
    /// given triples, interning every node it produces through
    /// <paramref name="store"/>. The same store may be reused
    /// across multiple builds to share canonical instances
    /// between graphs.
    /// </summary>
    /// <param name="triples">The triples to index.</param>
    /// <param name="store">The intern table to use.</param>
    /// <param name="cancellationToken">A token that aborts the build at any per-step check.</param>
    /// <returns>A new immutable graph store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="EditSessionConcurrencyException"><paramref name="store"/> has a journal whose head is not <see cref="NodeIdentifier.Empty"/>; subsequent state changes go through edit sessions, not <see cref="BuildAsync(IEnumerable{EncodedTriple}, NodeStore, CancellationToken)"/>.</exception>
    public static ValueTask<HypertrieGraphStore> BuildAsync(
        IEnumerable<EncodedTriple> triples,
        NodeStore store,
        CancellationToken cancellationToken = default)
    {
        return BuildAsync(triples, store, BuildPools.CreateDefault(), cancellationToken);
    }

    /// <summary>
    /// Builds a new <see cref="HypertrieGraphStore"/> from the
    /// given triples, interning every node it produces through
    /// <paramref name="store"/> and using <paramref name="pools"/>
    /// for EdgeMap-tier buffer rentals.
    /// </summary>
    /// <param name="triples">The triples to index.</param>
    /// <param name="store">The intern table to use.</param>
    /// <param name="pools">The pools bundle.</param>
    /// <param name="cancellationToken">A token that aborts the build at any per-step check.</param>
    /// <returns>A new immutable graph store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="EditSessionConcurrencyException"><paramref name="store"/> has a journal whose head is not <see cref="NodeIdentifier.Empty"/>; subsequent state changes go through edit sessions.</exception>
    public static async ValueTask<HypertrieGraphStore> BuildAsync(
        IEnumerable<EncodedTriple> triples,
        NodeStore store,
        BuildPools pools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(store);

        //Triple deduplication does not touch the store, so it runs
        //outside the mutation scope. Holding the scope only over
        //the work that mutates the intern table keeps a long-running
        //sweep from blocking on triple enumeration. The wrapper
        //also sorts in canonical SPO order so the build path can
        //skip its own re-dedup and use linear walks instead of
        //per-position dictionary grouping.
        DistinctSortedTriples distinctSorted = DistinctSortedTriples.Create(triples);

        //The mutation scope serialises this build against any
        //concurrent sweep on the same store. Shared mode means
        //multiple concurrent builds run together. The scope
        //extends through snapshot construction so that the new
        //snapshot is registered before the gate is released —
        //otherwise a sweep that wins the race for the gate would
        //evict freshly-built nodes that no snapshot yet pins.
        //Acquire and dispose explicitly so the disposal await can
        //carry ConfigureAwait(false); C# does not yet have syntax
        //for that on `await using` declarations.
        SharedScope scope = await store.EnterSharedScopeAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (NodeHandle rootHandle, NodeIdentifier rootId) = HypertrieOps.BuildBottomUpWithIdentifier(distinctSorted, store, pools);

            //Journal append (when the store has a journal). The OCC
            //contract requires a parent of NodeIdentifier.Empty; a
            //store whose journal already has entries is past the
            //initial-build window and the append throws.
            //The journal stamps Timestamp and SequenceNumber on
            //append; the factory leaves both as placeholders.
            if(store.JournalAppend is not null)
            {
                JournalEntry entry = JournalEntry.Initial(store.Hash, rootId, [.. distinctSorted.AsSpan()]);

                await store.JournalAppend(entry, NodeIdentifier.Empty, cancellationToken).ConfigureAwait(false);
            }

            HypertrieSnapshot snapshot = new(store, rootHandle, rootId);

            return new HypertrieGraphStore(snapshot, distinctSorted.Count);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wraps an existing <see cref="HypertrieSnapshot"/> — typically the new snapshot an
    /// <see cref="EditSession.CommitAsync"/> returns — in a queryable store, so committed state can be read and
    /// further edited without rebuilding. The read-side complement to <see cref="OpenEditSessionAsync"/>: an edit
    /// session commits to a snapshot, and this re-wraps that snapshot as a store.
    /// </summary>
    /// <param name="snapshot">The snapshot to wrap; used as-is, no rebuild.</param>
    /// <returns>A store reading <paramref name="snapshot"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Reference ownership.</b> The returned store <see cref="HypertrieSnapshot.Acquire">acquires</see> its own
    /// reference to <paramref name="snapshot"/> and holds it for the store's lifetime (like a built store). The caller
    /// remains the owner of the reference it passed in and must still release it (e.g. the <c>using</c> on a
    /// <see cref="EditSession.CommitAsync"/> result).
    /// </para>
    /// <para>
    /// <b>Count.</b> <see cref="Count"/> is computed by a full match enumeration over the snapshot (O(n)); the snapshot
    /// carries no materialised count. This is the one rebuild-shaped cost of re-wrapping — kept thin so a future seam
    /// can query a snapshot in place instead.
    /// </para>
    /// </remarks>
    public static HypertrieGraphStore FromSnapshot(HypertrieSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        HypertrieNode root = snapshot.Store.GetByHandle(snapshot.Root);
        int count = 0;
        foreach(EncodedTriple _ in HypertrieOps.Match(root, snapshot.Store, TermId.None, TermId.None, TermId.None))
        {
            count++;
        }

        return new HypertrieGraphStore(snapshot.Acquire(), count);
    }

    /// <summary>
    /// Wraps an existing <see cref="HypertrieSnapshot"/> with a
    /// KNOWN triple count, skipping the counting enumeration of
    /// <see cref="FromSnapshot(HypertrieSnapshot)"/>. For callers
    /// that track counts alongside roots (a dataset's graph
    /// directory); the count is trusted, not verified.
    /// </summary>
    /// <param name="snapshot">The snapshot to wrap; used as-is, no rebuild. The returned store acquires its own reference; the caller still owns and releases its own.</param>
    /// <param name="count">The snapshot's distinct triple count.</param>
    /// <returns>A store reading <paramref name="snapshot"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public static HypertrieGraphStore FromSnapshot(HypertrieSnapshot snapshot, int count)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return new HypertrieGraphStore(snapshot.Acquire(), count);
    }

    /// <summary>
    /// Opens an <see cref="EditSession"/> against this store's
    /// current <see cref="Snapshot"/>. The session holds a fresh
    /// reference to the snapshot for its lifetime; the caller
    /// continues to read this store unchanged. Convenience shim
    /// over <see cref="NodeStore.OpenEditSessionAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>An opened session ready to receive edits.</returns>
    /// <exception cref="EditSessionConcurrencyException">The journal head no longer corresponds to <see cref="Snapshot"/>; another session committed first.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered.</exception>
    public ValueTask<EditSession> OpenEditSessionAsync(CancellationToken cancellationToken = default)
    {
        return Snapshot.Store.OpenEditSessionAsync(Snapshot, cancellationToken);
    }

    /// <summary>
    /// Returns all triples matching the given pattern. Each
    /// position is either a bound <see cref="TermId"/> or
    /// <see cref="TermId.None"/> for "any."
    /// </summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any.</param>
    /// <returns>All matching triples.</returns>
    /// <remarks>
    /// <para>
    /// <b>Unbound positions.</b> A position parameter of
    /// <see cref="TermId.None"/> means "match any value at this
    /// position." Since <see cref="TermId.None"/> equals
    /// <c>default(TermId)</c>, position parameters that default to
    /// <c>default</c> are unbound by construction.
    /// </para>
    /// </remarks>
    public IEnumerable<EncodedTriple> Match(TermId subject, TermId predicate, TermId @object)
    {
        HypertrieNode root = Snapshot.Store.GetByHandle(Snapshot.Root);
        return HypertrieOps.Match(root, Snapshot.Store, subject, predicate, @object);
    }

    /// <summary>
    /// Returns the cross-product of <paramref name="subjects"/> with a
    /// bound <paramref name="predicate"/>, optionally constrained by a
    /// bound <paramref name="object"/>. Performs a single
    /// predicate-rooted descent through the depth-3 trie and probes
    /// once per subject.
    /// </summary>
    /// <param name="subjects">The encoded subject identifiers. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <param name="predicate">The bound predicate to match.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any.</param>
    /// <returns>Matching triples; output ordering is unspecified.</returns>
    /// <exception cref="ArgumentException"><paramref name="predicate"/> is <see cref="TermId.None"/>, or <paramref name="subjects"/> contains <see cref="TermId.None"/>.</exception>
    public IEnumerable<EncodedTriple> MatchBySubjects(
        ReadOnlyMemory<TermId> subjects,
        TermId predicate,
        TermId @object)
    {
        HypertrieNode root = Snapshot.Store.GetByHandle(Snapshot.Root);
        return HypertrieOps.MatchBySubjects(root, Snapshot.Store, subjects, predicate, @object);
    }

    /// <summary>
    /// Mirror of <see cref="MatchBySubjects"/> across the object
    /// position.
    /// </summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any.</param>
    /// <param name="predicate">The bound predicate to match.</param>
    /// <param name="objects">The encoded object identifiers. May be empty; must not contain <see cref="TermId.None"/>.</param>
    /// <returns>Matching triples; output ordering is unspecified.</returns>
    /// <exception cref="ArgumentException"><paramref name="predicate"/> is <see cref="TermId.None"/>, or <paramref name="objects"/> contains <see cref="TermId.None"/>.</exception>
    public IEnumerable<EncodedTriple> MatchByObjects(
        TermId subject,
        TermId predicate,
        ReadOnlyMemory<TermId> objects)
    {
        HypertrieNode root = Snapshot.Store.GetByHandle(Snapshot.Root);
        return HypertrieOps.MatchByObjects(root, Snapshot.Store, subject, predicate, objects);
    }

    /// <summary>
    /// Evaluates a basic graph pattern against this store using
    /// worst-case-optimal joins.
    /// </summary>
    /// <param name="query">The basic graph pattern.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Pass <see cref="TimeProvider.System"/> in production; tests pinning trace timing pass a <c>FakeTimeProvider</c>.</param>
    /// <param name="planner">The planner to use, or <c>null</c> to use <see cref="Planners.FirstOccurrence"/>.</param>
    /// <param name="cardinalities">A-priori per-class upper bounds handed to the planner on every consultation, or <c>null</c> when none are known.</param>
    /// <param name="accessControl">Optional access-control policy. <c>null</c> treats every candidate as allowed.</param>
    /// <param name="accessContext">Caller-supplied access context. Required when <paramref name="accessControl"/> is non-<c>null</c>.</param>
    /// <param name="traceHandler">Optional trace handler for query-execution events.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events. Pass <see cref="Guid.Empty"/> to generate a fresh one.</param>
    /// <param name="identifiers">The identifier source used to mint a fresh correlation id when <paramref name="correlationId"/> is <see cref="Guid.Empty"/>; defaults to <see cref="VeritasIdentifiers.System"/>.</param>
    /// <param name="cancellationToken">Cancellation token threaded into iterator operations and the access-control consultation.</param>
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Planner effectivePlanner = planner ?? Planners.FirstOccurrence(query);
        Guid effectiveCorrelationId = correlationId == Guid.Empty
            ? (identifiers ?? VeritasIdentifiers.System)(new IdentifierRequest(IdentifierPurpose.Correlation, default))
            : correlationId;

        BasicGraphPatternEvaluator evaluator = new(
            Snapshot,
            query,
            effectivePlanner,
            timeProvider,
            cardinalities,
            accessControl,
            accessContext,
            traceHandler,
            effectiveCorrelationId);

        return evaluator.EvaluateAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.MatchTriplesAsync"/>
    /// delegate backed by this store.
    /// </summary>
    public StorageDelegates.MatchTriplesAsync AsMatchDelegate()
    {
        return MatchDelegateImpl;
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.MatchTriplesBySubjectsAsync"/>
    /// delegate backed by this store.
    /// </summary>
    public StorageDelegates.MatchTriplesBySubjectsAsync AsMatchBySubjectsDelegate()
    {
        return MatchBySubjectsDelegateImpl;
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.MatchTriplesByObjectsAsync"/>
    /// delegate backed by this store.
    /// </summary>
    public StorageDelegates.MatchTriplesByObjectsAsync AsMatchByObjectsDelegate()
    {
        return MatchByObjectsDelegateImpl;
    }

    /// <summary>
    /// Bundles the three match delegates into a <see cref="GraphMatchOps"/>
    /// for callers — such as
    /// <see cref="Lumoin.Veritas.Rdf.PropertyPathEvaluator"/> — that need
    /// all three forms.
    /// </summary>
    public GraphMatchOps AsMatchOps()
    {
        return new GraphMatchOps(
            AsMatchDelegate(),
            AsMatchBySubjectsDelegate(),
            AsMatchByObjectsDelegate());
    }

    /// <summary>
    /// Creates a <see cref="StorageDelegates.CountTriplesAsync"/>
    /// delegate backed by this store.
    /// </summary>
    public StorageDelegates.CountTriplesAsync AsCountDelegate()
    {
        return CountDelegateImpl;
    }

    /// <summary>The instance implementation behind <see cref="AsMatchDelegate"/>, bound as a method group so the delegate closes over no enclosing local.</summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The predicate to match, or <see cref="TermId.None"/> for any predicate.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    private IAsyncEnumerable<EncodedTriple> MatchDelegateImpl(TermId subject, TermId predicate, TermId @object, CancellationToken cancellationToken)
    {
        return ToAsyncEnumerable(Match(subject, predicate, @object), cancellationToken);
    }

    /// <summary>The instance implementation behind <see cref="AsMatchBySubjectsDelegate"/>, bound as a method group so the delegate closes over no enclosing local.</summary>
    /// <param name="subjects">The encoded subject identifiers to look up under <paramref name="predicate"/>.</param>
    /// <param name="predicate">The bound predicate to match.</param>
    /// <param name="object">The object to match, or <see cref="TermId.None"/> for any object.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    private IAsyncEnumerable<EncodedTriple> MatchBySubjectsDelegateImpl(ReadOnlyMemory<TermId> subjects, TermId predicate, TermId @object, CancellationToken cancellationToken)
    {
        return ToAsyncEnumerable(MatchBySubjects(subjects, predicate, @object), cancellationToken);
    }

    /// <summary>The instance implementation behind <see cref="AsMatchByObjectsDelegate"/>, bound as a method group so the delegate closes over no enclosing local.</summary>
    /// <param name="subject">The subject to match, or <see cref="TermId.None"/> for any subject.</param>
    /// <param name="predicate">The bound predicate to match.</param>
    /// <param name="objects">The encoded object identifiers to look up under <paramref name="predicate"/>.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of matching triples.</returns>
    private IAsyncEnumerable<EncodedTriple> MatchByObjectsDelegateImpl(TermId subject, TermId predicate, ReadOnlyMemory<TermId> objects, CancellationToken cancellationToken)
    {
        return ToAsyncEnumerable(MatchByObjects(subject, predicate, objects), cancellationToken);
    }

    /// <summary>The instance implementation behind <see cref="AsCountDelegate"/>, bound as a method group so the delegate closes over no enclosing local.</summary>
    /// <param name="cancellationToken">A token to cancel the operation; the count is immediate, so it is not observed.</param>
    /// <returns>The total triple count.</returns>
    private ValueTask<long> CountDelegateImpl(CancellationToken cancellationToken)
    {
        return ValueTask.FromResult((long)Count);
    }

    private static async IAsyncEnumerable<EncodedTriple> ToAsyncEnumerable(
        IEnumerable<EncodedTriple> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(EncodedTriple triple in source)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return triple;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
