using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Interns <see cref="HypertrieNode"/> instances by
/// <see cref="NodeIdentifier"/>. The first time a given identifier
/// is presented with a node, the store records the node and
/// returns it; subsequent calls with the same identifier and
/// content-equivalent edge maps return the recorded node. Hash
/// collisions are detected by full content comparison and
/// disambiguated by chaining; correctness is preserved at the
/// cost of a content compare per chain probe.
/// </summary>
/// <remarks>
/// <para>
/// The store is the single owner of the canonicalisation
/// contract: any two structurally-identical nodes that pass
/// through it become reference-equal. Consumers must not mutate
/// a node after presenting it to the store, because every other
/// node that shares the canonical instance would silently
/// change.
/// </para>
/// <para>
/// <b>Concurrent intern.</b> The intern table is a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> and
/// <see cref="Intern"/> is a CAS loop: lookup, content-walk on
/// hit, then either <c>TryAdd</c> a fresh head or <c>TryUpdate</c>
/// onto the observed head. Concurrent writers racing on the same
/// identifier converge — content-equal candidates collapse to one
/// canonical instance, content-different candidates chain. Reads
/// (lookup, content-walk, the success branch when the content
/// already matches) take no lock at all.
/// </para>
/// <para>
/// <b>Hash function.</b> The store carries the
/// <see cref="VeritasHash"/> used by every layer that computes
/// content-addressed identifiers — node identifiers, edit
/// commitments. Carrying the delegate here means the store and
/// any consumer that builds nodes for it agree on the same
/// hash by construction. The library never picks a default
/// hash; the application names one (typically
/// <see cref="VeritasHashing.Default"/>) at the composition
/// root.
/// </para>
/// <para>
/// <b>Snapshot registry.</b> The store also tracks the set of
/// <see cref="HypertrieSnapshot"/> instances currently holding a
/// reference to it. Snapshots register themselves on
/// construction and deregister on final release. The registry
/// drives the mark phase of <see cref="SweepAsync"/>: every node
/// reachable from any registered snapshot is preserved; every
/// other interned node is eligible for eviction.
/// </para>
/// <para>
/// <b>Mutation gate.</b> A
/// <see cref="AsyncSharedExclusiveLock"/> coordinates the
/// build/edit-session and sweep paths. Builds and (in a future
/// batch) edit sessions take the gate in <em>shared</em> mode —
/// many concurrent mutators may run together; structural sharing
/// in the intern table makes them naturally compatible.
/// <see cref="SweepAsync"/> takes the gate in <em>exclusive</em>
/// mode — no concurrent mutators while it walks the intern table
/// to prune. Reads (iterators, queries, snapshot acquire and
/// release) take no gate at all.
/// </para>
/// <para>
/// <b>Journal.</b> The store optionally carries
/// <see cref="JournalDelegates.AppendJournalEntryAsync"/> and
/// <see cref="JournalDelegates.ReadJournalEntriesAsync"/>
/// delegates. When supplied, build and edit-session-commit paths
/// append a <see cref="JournalEntry"/> recording the transition,
/// and edit-session open and abandon paths append non-mutating
/// lifecycle entries. When omitted, the store has no journal and
/// build/commit paths run unrecorded; this is the legacy mode
/// existing tests rely on.
/// </para>
/// <para>
/// <b>Disposal.</b> The store is <see cref="IDisposable"/>
/// because it owns an <see cref="AsyncSharedExclusiveLock"/>. In
/// typical usage where the store outlives all of its
/// <see cref="HypertrieGraphStore"/> consumers, explicit disposal
/// is unnecessary — the lock allocates no <c>WaitHandle</c> on
/// the paths used here, so the garbage collector reclaims
/// everything cleanly. Long-lived hosts that recycle stores
/// during operation should still call <see cref="Dispose"/> at
/// end of life. Calling <see cref="Dispose"/> while a build or
/// sweep is in flight is a contract violation; callers ensure no
/// such operation is pending before disposing.
/// </para>
/// </remarks>
[DebuggerDisplay("NodeStore Count={Count} AcquiredSnapshots={AcquiredSnapshotCount} SweepCount={SweepCount}")]
public sealed class NodeStore: IDisposable
{
    //The intern table: identifier -> chain head. Concurrent so
    //multiple in-flight intern calls can write without an outer
    //lock; the chain is rebuilt on hash collision and only the
    //head reference is mutated, so the dictionary's atomic
    //TryAdd / TryUpdate operations cover correctness.
    private ConcurrentDictionary<NodeIdentifier, ChainEntry> Storage { get; } = new();

    private HashSet<HypertrieSnapshot> AcquiredSnapshots { get; } = [];

    //The shared/exclusive gate. Builds and edit sessions take
    //shared; sweeps take exclusive.
    private AsyncSharedExclusiveLock MutationGate { get; } = new();

    private int nodeCount;

    private int sweepCount;

    private int disposed;

    /// <summary>The hash function the store and every layer that computes content-addressed identifiers share. Set once at construction; the only place a hash function is named in a running system.</summary>
    public VeritasHash Hash { get; }

    /// <summary>
    /// The optional journal-append delegate. <c>null</c> when the
    /// store was constructed without a journal; in that case the
    /// build/commit paths emit no journal entries.
    /// </summary>
    public JournalDelegates.AppendJournalEntryAsync? JournalAppend { get; }

    /// <summary>
    /// The optional journal-read delegate. <c>null</c> when the
    /// store was constructed without a journal.
    /// </summary>
    public JournalDelegates.ReadJournalEntriesAsync? JournalRead { get; }

    /// <summary>The number of distinct canonical nodes currently interned.</summary>
    public int Count => Volatile.Read(ref nodeCount);

    /// <summary>
    /// The number of <see cref="SweepAsync"/> calls that have
    /// completed against this store. Diagnostic only; useful for
    /// tests that want to observe whether a trigger has fired.
    /// </summary>
    public int SweepCount => Volatile.Read(ref sweepCount);

    /// <summary>
    /// The number of <see cref="HypertrieSnapshot"/> instances
    /// currently holding a reference to this store. Diagnostic
    /// only; consumers should not synchronise on this value.
    /// </summary>
    public int AcquiredSnapshotCount
    {
        get
        {
            lock(AcquiredSnapshots)
            {
                return AcquiredSnapshots.Count;
            }
        }
    }

    /// <summary>
    /// Constructs an empty store using
    /// <paramref name="hash"/> as the hash function and no
    /// journal.
    /// </summary>
    /// <param name="hash">The hash function the store and its consumers share. Pass <see cref="VeritasHashing.Default"/> for the canonical xxHash64 implementation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    public NodeStore(VeritasHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        Hash = hash;
        JournalAppend = null;
        JournalRead = null;
    }

    /// <summary>
    /// Constructs an empty store using
    /// <paramref name="hash"/> as the hash function and the
    /// given journal delegates.
    /// </summary>
    /// <param name="hash">The hash function the store and its consumers share. Pass <see cref="VeritasHashing.Default"/> for the canonical xxHash64 implementation.</param>
    /// <param name="journalAppend">The append-with-OCC delegate; must not be <c>null</c>.</param>
    /// <param name="journalRead">The read delegate; must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public NodeStore(
        VeritasHash hash,
        JournalDelegates.AppendJournalEntryAsync journalAppend,
        JournalDelegates.ReadJournalEntriesAsync journalRead)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(journalAppend);
        ArgumentNullException.ThrowIfNull(journalRead);

        Hash = hash;
        JournalAppend = journalAppend;
        JournalRead = journalRead;
    }

    /// <summary>
    /// Returns the canonical instance for the given
    /// (<paramref name="identifier"/>, <paramref name="candidate"/>)
    /// pair. If <paramref name="identifier"/> has not been seen,
    /// records <paramref name="candidate"/> as the canonical
    /// instance and returns it. If the identifier has been seen
    /// and an existing canonical node has content-equal edge
    /// maps (and equal depth), returns the existing canonical
    /// node. If the identifier has been seen but no chain entry's
    /// content matches (a hash collision), chains the candidate
    /// onto the bucket and returns the candidate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Concurrent intern calls on the same identifier converge
    /// through a CAS loop on the dictionary entry. A retry happens
    /// only when another writer races us; the path is allocation-
    /// and lock-free in the uncontended case.
    /// </para>
    /// </remarks>
    /// <param name="identifier">The candidate's identifier.</param>
    /// <param name="candidate">The candidate node; must not be <c>null</c>.</param>
    /// <returns>The canonical node for this identifier and content.</returns>
    public HypertrieNode Intern(NodeIdentifier identifier, HypertrieNode candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        while(true)
        {
            if(!Storage.TryGetValue(identifier, out ChainEntry? observedHead))
            {
                //First node observed for this identifier — try to claim the bucket.
                ChainEntry freshHead = new() { Node = candidate, Next = null };
                if(Storage.TryAdd(identifier, freshHead))
                {
                    Interlocked.Increment(ref nodeCount);

                    return candidate;
                }

                //Lost the race; another writer took the bucket. Retry from the top.
                continue;
            }

            //Walk the observed chain looking for a content-equal node.
            ChainEntry? cursor = observedHead;

            while(cursor is not null)
            {
                if(cursor.Node.Depth == candidate.Depth && NodeContentEquals(cursor.Node, candidate))
                {
                    return cursor.Node;
                }

                cursor = cursor.Next;
            }

            //No content match in the observed chain — push the
            //candidate onto the head and try to publish the new
            //head atomically. If we lose the CAS, another writer
            //changed the chain (added a content-equal entry, or
            //added another collision); we go back to the top and
            //re-examine the chain.
            ChainEntry newHead = new() { Node = candidate, Next = observedHead };
            if(Storage.TryUpdate(identifier, newHead, observedHead))
            {
                Interlocked.Increment(ref nodeCount);

                return candidate;
            }

            //Lost the CAS; retry.
        }
    }

    /// <summary>
    /// Returns <c>true</c> when an interned node exists with
    /// <paramref name="identifier"/>; for diagnostics and tests
    /// only.
    /// </summary>
    public bool Contains(NodeIdentifier identifier) => Storage.ContainsKey(identifier);

    /// <summary>
    /// Adds <paramref name="snapshot"/> to the registry of
    /// currently-acquired snapshots. Called by
    /// <see cref="HypertrieSnapshot"/>'s constructor; consumers
    /// do not call this directly.
    /// </summary>
    internal void RegisterSnapshot(HypertrieSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock(AcquiredSnapshots)
        {
            AcquiredSnapshots.Add(snapshot);
        }
    }

    /// <summary>
    /// Removes <paramref name="snapshot"/> from the registry of
    /// currently-acquired snapshots. Called by
    /// <see cref="HypertrieSnapshot.Release"/> when the reference
    /// count reaches zero; consumers do not call this directly.
    /// </summary>
    internal void UnregisterSnapshot(HypertrieSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock(AcquiredSnapshots)
        {
            AcquiredSnapshots.Remove(snapshot);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="snapshot"/> is
    /// currently in the acquired-snapshots registry.
    /// Diagnostic / tests only.
    /// </summary>
    public bool IsSnapshotAcquired(HypertrieSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock(AcquiredSnapshots)
        {
            return AcquiredSnapshots.Contains(snapshot);
        }
    }

    /// <summary>
    /// Acquires the mutation gate in shared mode. Multiple shared
    /// holders may run concurrently. Builds and (in a future
    /// batch) edit-session commits hold this scope while they
    /// mutate the intern table; sweeps wait for the scope to
    /// release before claiming the gate exclusively.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A scope handle whose disposal releases the shared hold.</returns>
    internal ValueTask<SharedScope> EnterSharedScopeAsync(CancellationToken cancellationToken)
        => MutationGate.EnterSharedAsync(cancellationToken);

    /// <summary>
    /// Acquires the mutation gate in exclusive mode. Used only by
    /// <see cref="SweepAsync"/>; blocks every shared holder until
    /// released.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A scope handle whose disposal releases the exclusive hold.</returns>
    internal ValueTask<ExclusiveScope> EnterExclusiveScopeAsync(CancellationToken cancellationToken)
        => MutationGate.EnterExclusiveAsync(cancellationToken);

    /// <summary>
    /// Opens an <see cref="EditSession"/> against
    /// <paramref name="baseSnapshot"/>. Acquires the shared
    /// mutation gate and a fresh reference to
    /// <paramref name="baseSnapshot"/>, both held for the
    /// session's lifetime; appends an
    /// <see cref="EditSessionEntryKind.Started"/> entry to the
    /// journal under optimistic concurrency. The session's
    /// <see cref="EditSession.Id"/> is freshly allocated.
    /// </summary>
    /// <param name="baseSnapshot">The snapshot the session branches from. The session acquires a fresh reference; the caller's reference is not consumed.</param>
    /// <param name="cancellationToken">A token that aborts the open.</param>
    /// <returns>An opened session ready to receive edits.</returns>
    /// <remarks>
    /// <para>
    /// <b>Optimistic concurrency on Started.</b>
    /// <see cref="EditSessionEntryKind.Started"/> is non-mutating
    /// — its <c>ChildId</c> equals its <c>ParentId</c>, both equal
    /// <paramref name="baseSnapshot"/>'s identifier. The journal
    /// rejects the append when the head no longer equals
    /// <c>baseSnapshot.Id</c>; in that case this method retries
    /// against the new head reported in
    /// <see cref="EditSessionConcurrencyException.ActualHead"/>
    /// only if the new head still corresponds to
    /// <paramref name="baseSnapshot"/>'s identifier (i.e. the
    /// retry is harmless because no actual state change happened
    /// between the two append attempts). When the head has
    /// genuinely moved, the open propagates the
    /// <see cref="EditSessionConcurrencyException"/> to the
    /// caller — opening a session against a stale snapshot is a
    /// caller error the library does not paper over.
    /// </para>
    /// <para>
    /// <b>No journal, no Started entry.</b> When the store was
    /// constructed without a journal, no entry is written; the
    /// session still acquires the shared scope and the snapshot
    /// reference. The session's <c>Started</c> / <c>Committed</c>
    /// / <c>Abandoned</c> lifecycle is purely in-memory in that
    /// configuration.
    /// </para>
    /// <para>
    /// <b>Cleanup on failure.</b> When the
    /// <see cref="EditSessionEntryKind.Started"/> append fails for
    /// any reason — non-OCC exceptions or the unhandleable
    /// genuine-head-moved case — this method releases the shared
    /// scope and the snapshot reference before propagating, so
    /// the caller never sees a half-opened session leaking
    /// resources.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="baseSnapshot"/> is <c>null</c>.</exception>
    /// <exception cref="EditSessionConcurrencyException">The journal head no longer corresponds to <paramref name="baseSnapshot"/>; another session committed first.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered.</exception>
    public async ValueTask<EditSession> OpenEditSessionAsync(
        HypertrieSnapshot baseSnapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseSnapshot);
        cancellationToken.ThrowIfCancellationRequested();

        SessionId sessionId = SessionId.NewId();

        //Acquire resources in the order: scope first, snapshot
        //reference second, journal append third. Any failure on a
        //later step releases everything acquired earlier before
        //propagating. The shape mirrors the ownership-transfer
        //pattern (CA2000) used elsewhere in the library: the
        //local references are non-null until ownership transfers
        //to the constructed EditSession.
        SharedScope scope = await EnterSharedScopeAsync(cancellationToken).ConfigureAwait(false);
        HypertrieSnapshot? acquired = null;
        bool ownershipTransferred = false;

        try
        {
            acquired = baseSnapshot.Acquire();

            if(JournalAppend is not null)
            {
                //The Started entry is non-mutating: ChildId
                //equals ParentId equals baseSnapshot.Id. A
                //genuine head movement causes the OCC append to
                //throw; we surface the exception so the caller
                //rebases. There is no value in retrying inside
                //this method because the caller's intent is tied
                //to the specific baseSnapshot supplied.
                JournalEntry startedEntry = JournalEntry.Started(baseSnapshot.Id, sessionId);
                await JournalAppend(startedEntry, baseSnapshot.Id, cancellationToken).ConfigureAwait(false);
            }

            EditSession session = new(this, acquired, scope, sessionId);
            ownershipTransferred = true;
            return session;
        }
        finally
        {
            if(!ownershipTransferred)
            {
                acquired?.Release();
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Walks the node graph reachable from every currently
    /// acquired snapshot and evicts every interned canonical
    /// node not on that reachable set. Acquires the mutation
    /// gate exclusively for the duration of the sweep, blocking
    /// concurrent builds and edit-session commits while it
    /// runs; reads are unaffected. Triggering is the operator's
    /// responsibility — the library never sweeps unprompted.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the sweep at any per-step check.</param>
    /// <returns>Statistics describing the sweep.</returns>
    public async ValueTask<SweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        //Acquire the exclusive scope and dispose it explicitly in a
        //finally so the DisposeAsync await can carry ConfigureAwait(false).
        //C# does not yet have syntax for ConfigureAwait on a `await using`
        //declaration; the manual shape satisfies CA2007 without the
        //semantic of ConfiguredAsyncDisposable, which would erase the
        //ExclusiveScope static type.
        ExclusiveScope scope = await EnterExclusiveScopeAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HypertrieSnapshot[] live;
            lock(AcquiredSnapshots)
            {
                live = [.. AcquiredSnapshots];
            }

            HashSet<HypertrieNode> reachable = new(ReferenceEqualityComparer.Instance);

            foreach(HypertrieSnapshot snapshot in live)
            {
                MarkReachable(snapshot.Root, reachable, cancellationToken);
            }

            return PruneUnreachable(reachable, cancellationToken);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the resources owned by this store. Safe to call
    /// only when no build or sweep is in flight; concurrent
    /// disposal is a contract violation. Disposing a store more
    /// than once is a no-op.
    /// </summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        MutationGate.Dispose();
    }

    //Walks every reachable node from the given root and adds it
    //to the reachable set. Iterative to satisfy the "no recursion
    //in graph algorithms" rule; the work stack carries nodes
    //pending visit and dedup is performed by the set itself.
    private static void MarkReachable(HypertrieNode root, HashSet<HypertrieNode> reachable, CancellationToken cancellationToken)
    {
        Stack<HypertrieNode> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            HypertrieNode current = work.Pop();
            if(!reachable.Add(current))
            {
                //Already visited — every node reachable from it is already on the reachable set.
                continue;
            }

            for(int position = 0; position < current.Depth; position++)
            {
                foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(current.EdgeMaps[position]))
                {
                    //At depth-1 leaves the child reference is null;
                    //higher depths always carry a non-null child.
                    if(entry.Value is not null)
                    {
                        work.Push(entry.Value);
                    }
                }
            }
        }
    }

    //Walks the intern table once, dropping bucket entries whose
    //node is not in the reachable set. Buckets are mutated in a
    //second pass to avoid modifying the dictionary during
    //enumeration. Returns sweep statistics for the diagnostic
    //surface.
    private SweepResult PruneUnreachable(HashSet<HypertrieNode> reachable, CancellationToken cancellationToken)
    {
        List<NodeIdentifier> keysToRemove = [];
        List<KeyValuePair<NodeIdentifier, ChainEntry>> keysToReplace = [];
        int evicted = 0;
        int chainsTouched = 0;

        foreach(KeyValuePair<NodeIdentifier, ChainEntry> bucket in Storage)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ChainEntry head = bucket.Value;

            //Common case: a single-entry chain. Either the node is
            //reachable and the bucket stays unchanged, or the node
            //is unreachable and the whole bucket goes.
            if(head.Next is null)
            {
                if(!reachable.Contains(head.Node))
                {
                    keysToRemove.Add(bucket.Key);
                    evicted++;
                    chainsTouched++;
                }

                continue;
            }

            //Collision chain: rebuild from reachable entries. The
            //rebuild reverses chain order, which is harmless —
            //chain order has no semantic meaning beyond head-first
            //collision lookup.
            ChainEntry? newHead = null;
            int oldLength = 0;
            int newLength = 0;

            for(ChainEntry? cursor = head; cursor is not null; cursor = cursor.Next)
            {
                oldLength++;
                if(reachable.Contains(cursor.Node))
                {
                    newHead = new ChainEntry { Node = cursor.Node, Next = newHead };
                    newLength++;
                }
            }

            if(newLength == 0)
            {
                keysToRemove.Add(bucket.Key);
                evicted += oldLength;
                chainsTouched++;
            }
            else if(newLength < oldLength)
            {
                keysToReplace.Add(new KeyValuePair<NodeIdentifier, ChainEntry>(bucket.Key, newHead!));
                evicted += oldLength - newLength;
                chainsTouched++;
            }
        }

        foreach(NodeIdentifier key in keysToRemove)
        {
            Storage.TryRemove(key, out _);
        }

        foreach(KeyValuePair<NodeIdentifier, ChainEntry> kvp in keysToReplace)
        {
            //Indexer setter on ConcurrentDictionary is an atomic
            //upsert; under exclusive scope no concurrent writer
            //can race us anyway.
            Storage[kvp.Key] = kvp.Value;
        }

        Interlocked.Add(ref nodeCount, -evicted);
        Interlocked.Increment(ref sweepCount);

        return new SweepResult(NodesEvicted: evicted, NodesRetained: Volatile.Read(ref nodeCount), ChainsTouched: chainsTouched);
    }

    //Compares two nodes for content equality, used to disambiguate
    //hash collisions during interning. The depth check is the
    //caller's responsibility before this is invoked.
    private static bool NodeContentEquals(HypertrieNode left, HypertrieNode right)
    {
        Debug.Assert(left.Depth == right.Depth, "Depth must be equal before content comparison.");

        for(int position = 0; position < left.Depth; position++)
        {
            if(!EdgeMapContentEquals(in left.EdgeMaps[position], in right.EdgeMaps[position]))
            {
                return false;
            }
        }

        return true;
    }

    //Compares two edge maps for content equality. Two maps are
    //equal when they hold the same set of (key, child) pairs,
    //regardless of representation: an Inline-kind map and a
    //SortedArray-kind map with the same single entry are content
    //equal even though their EdgeMap.Equals would return false
    //because the heap fields differ.
    private static bool EdgeMapContentEquals(in EdgeMap left, in EdgeMap right)
    {
        int leftCount = EdgeMap.Count(in left);
        int rightCount = EdgeMap.Count(in right);

        if(leftCount != rightCount)
        {
            return false;
        }

        if(leftCount == 0)
        {
            return true;
        }

        foreach(KeyValuePair<long, HypertrieNode?> entry in EdgeMap.Enumerate(left))
        {
            if(!EdgeMap.TryGetChild(in right, entry.Key, out HypertrieNode? rightChild))
            {
                return false;
            }

            //Children are themselves canonical instances by the time
            //their parent reaches Intern, so reference equality on
            //children is exactly content equality. For depth-1 leaves
            //both sides carry null and the comparison still holds.
            if(!ReferenceEquals(entry.Value, rightChild))
            {
                return false;
            }
        }

        return true;
    }

    //One node in the collision chain for a given identifier. A
    //chain length above one indicates a hash collision and is
    //expected to be vanishingly rare in practice; the chain is
    //kept simple — singly-linked, head insertion — because lookups
    //walk it only on collision and writes only ever push a new
    //head.
    private sealed class ChainEntry
    {
        public required HypertrieNode Node { get; init; }

        public ChainEntry? Next { get; init; }
    }
}
