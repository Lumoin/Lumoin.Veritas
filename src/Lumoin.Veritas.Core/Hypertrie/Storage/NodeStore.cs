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
/// is presented with a candidate node, the store records the
/// candidate and returns the same reference; subsequent calls with
/// the same identifier and content-equivalent edge maps return the
/// previously-interned canonical reference. Hash collisions are
/// detected by full content comparison and disambiguated by
/// chaining; correctness is preserved at the cost of a content
/// compare per chain probe.
/// </summary>
/// <remarks>
/// <para>
/// The store is the single owner of the canonicalisation contract:
/// any two structurally-identical nodes that pass through it become
/// reference-equal. Consumers must not mutate a node's
/// <see cref="HypertrieNode.Edge0"/>, <see cref="HypertrieNode.Edge1"/>,
/// or <see cref="HypertrieNode.Edge2"/> fields after presenting it,
/// because every other consumer holding the same canonical
/// reference would silently observe the change.
/// </para>
/// <para>
/// <b>Concurrent intern.</b> The intern table is a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> and
/// <see cref="Intern"/> is a CAS loop: lookup, content-walk on
/// hit, then either <c>TryAdd</c> a fresh head or <c>TryUpdate</c>
/// onto the observed head. Concurrent writers racing on the same
/// identifier converge — content-equal candidates collapse to one
/// canonical reference, content-different candidates chain. Reads
/// (lookup, content-walk, the success branch when the content
/// already matches) take no lock at all.
/// </para>
/// <para>
/// <b>Storage shape.</b> Each interned node is a
/// <see cref="HypertrieNode"/> class instance allocated by its
/// builder and presented to <see cref="Intern"/>. The store keeps
/// the canonical reference reachable via the
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> chain; no
/// per-node arena slot, no handle indirection. Iteration walks
/// node references directly through embedded
/// <see cref="EdgeMap"/> child slots — cache locality follows from
/// the embedded layout, not from a separate arena.
/// </para>
/// <para>
/// <b>Hash function.</b> The store carries the
/// <see cref="VeritasHash"/> used by every layer that computes
/// content-addressed identifiers — node identifiers, edit
/// commitments. Carrying the delegate here means the store and
/// any consumer that builds nodes for it agree on the same hash
/// by construction.
/// </para>
/// <para>
/// <b>Snapshot registry.</b> The store tracks the set of
/// <see cref="HypertrieSnapshot"/> instances currently holding a
/// reference to it. Snapshots register themselves on construction
/// and deregister on final release. The registry drives the mark
/// phase of <see cref="SweepAsync"/>: every node reachable from
/// any registered snapshot is preserved; every other interned
/// node is eligible for chain eviction.
/// </para>
/// <para>
/// <b>Mutation gate.</b> A
/// <see cref="AsyncSharedExclusiveLock"/> coordinates the
/// build/edit-session and sweep paths. Builds and edit sessions
/// take the gate in <em>shared</em> mode — many concurrent
/// mutators may run together; structural sharing in the intern
/// table makes them naturally compatible.
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
/// because it transitively owns every
/// <see cref="EdgeMap"/>'s SortedArray-tier rentals. Disposal
/// walks every interned node and releases its EdgeMaps' rentals.
/// The node objects themselves become eligible for GC once no
/// snapshot pins them. Calling <see cref="Dispose"/> while a build
/// or sweep is in flight is a contract violation; callers ensure
/// no such operation is pending before disposing.
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

    //Number of times a CAS-loop iteration in Intern lost the race
    //and discarded a freshly-presented candidate. Diagnostic only;
    //surfaces pathological contention via Debug.Assert.
    private int racesLost;

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
    /// <paramref name="hash"/> as the hash function and no journal.
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
    /// <param name="hash">The hash function the store and its consumers share.</param>
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
    /// Returns the canonical <see cref="HypertrieNode"/> reference
    /// for the given (<paramref name="identifier"/>,
    /// <paramref name="candidate"/>) pair. If
    /// <paramref name="identifier"/> has not been seen, records
    /// <paramref name="candidate"/> in the intern table and returns
    /// the candidate's own reference. If the identifier has been
    /// seen and an existing canonical node has content-equal edge
    /// maps (and equal depth), returns the existing reference. If
    /// the identifier has been seen but no chain entry's content
    /// matches (a hash collision), chains the candidate onto the
    /// bucket and returns the candidate's reference.
    /// </summary>
    /// <param name="identifier">The candidate's identifier.</param>
    /// <param name="candidate">The candidate node.</param>
    /// <returns>The canonical reference for this identifier and content.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is <c>null</c>.</exception>
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
                Interlocked.Increment(ref racesLost);
                Debug.Assert(Volatile.Read(ref racesLost) <= 1000,
                    "Excessive contention on Intern; race-loss exceeded threshold.");
                continue;
            }

            //Walk the observed chain looking for a content-equal node.
            ChainEntry? cursor = observedHead;

            while(cursor is not null)
            {
                HypertrieNode cursorNode = cursor.Node;
                if(cursorNode.Depth == candidate.Depth && NodeContentEquals(cursorNode, candidate))
                {
                    //Found content-equal entry; the candidate reference becomes unreachable.
                    return cursorNode;
                }

                cursor = cursor.Next;
            }

            //No content match in the observed chain — push the
            //candidate onto the head and try to publish the new
            //head atomically. If we lose the CAS, another writer
            //changed the chain; we go back to the top and re-examine.
            ChainEntry newHead = new() { Node = candidate, Next = observedHead };
            if(Storage.TryUpdate(identifier, newHead, observedHead))
            {
                Interlocked.Increment(ref nodeCount);

                return candidate;
            }

            //Lost the CAS; retry.
            Interlocked.Increment(ref racesLost);
            Debug.Assert(Volatile.Read(ref racesLost) <= 1000,
                "Excessive contention on Intern; race-loss exceeded threshold.");
        }
    }

    /// <summary>
    /// Returns <c>true</c> when an interned node exists with
    /// <paramref name="identifier"/>; for diagnostics and tests
    /// only.
    /// </summary>
    /// <param name="identifier">The identifier to check.</param>
    /// <returns><c>true</c> when the identifier is present.</returns>
    public bool Contains(NodeIdentifier identifier) => Storage.ContainsKey(identifier);

    /// <summary>
    /// Adds <paramref name="snapshot"/> to the registry of
    /// currently-acquired snapshots. Called by
    /// <see cref="HypertrieSnapshot"/>'s constructor; consumers
    /// do not call this directly.
    /// </summary>
    /// <param name="snapshot">The snapshot to register.</param>
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
    /// <param name="snapshot">The snapshot to deregister.</param>
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
    /// <param name="snapshot">The snapshot to look up.</param>
    /// <returns><c>true</c> when present.</returns>
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
    /// holders may run concurrently. Builds and edit-session
    /// commits hold this scope while they mutate the intern
    /// table; sweeps wait for the scope to release before
    /// claiming the gate exclusively.
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

        SharedScope scope = await EnterSharedScopeAsync(cancellationToken).ConfigureAwait(false);
        HypertrieSnapshot? acquired = null;
        bool ownershipTransferred = false;

        try
        {
            acquired = baseSnapshot.Acquire();

            if(JournalAppend is not null)
            {
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
    /// acquired snapshot and evicts every chain entry pointing at
    /// a node not on that reachable set. Acquires the mutation
    /// gate exclusively for the duration of the sweep, blocking
    /// concurrent builds and edit-session commits while it runs;
    /// reads are unaffected.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the sweep at any per-step check.</param>
    /// <returns>Statistics describing the sweep.</returns>
    public async ValueTask<SweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        ExclusiveScope scope = await EnterExclusiveScopeAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HypertrieSnapshot[] live;
            lock(AcquiredSnapshots)
            {
                live = [.. AcquiredSnapshots];
            }

            HashSet<HypertrieNode> reachable = [];

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
    /// Releases the resources owned by this store. Walks every
    /// interned node and disposes each <see cref="EdgeMap"/>'s
    /// SortedArray-tier rentals. Safe to call only when no build
    /// or sweep is in flight; concurrent disposal is a contract
    /// violation. Disposing more than once is a no-op.
    /// </summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        //Walk every chain entry across every bucket, releasing
        //each node's three EdgeMap SortedArray rentals.
        foreach(KeyValuePair<NodeIdentifier, ChainEntry> bucket in Storage)
        {
            for(ChainEntry? cursor = bucket.Value; cursor is not null; cursor = cursor.Next)
            {
                HypertrieNode node = cursor.Node;
                EdgeMap.DisposeRentals(ref node.Edge0);
                if(node.Depth >= 2)
                {
                    EdgeMap.DisposeRentals(ref node.Edge1);
                }
                if(node.Depth >= 3)
                {
                    EdgeMap.DisposeRentals(ref node.Edge2);
                }
            }
        }

        Storage.Clear();
        MutationGate.Dispose();
    }

    //Walks every reachable node from the given root and adds the
    //node reference to the reachable set. Iterative to satisfy the
    //"no recursion in graph algorithms" rule; the work stack
    //carries node references pending visit and dedup is performed
    //by the set itself.
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
                continue;
            }

            for(int position = 0; position < current.Depth; position++)
            {
                ref EdgeMap map = ref current.EdgeMapAt(position);
                foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(map))
                {
                    //At depth-1 leaves the child reference is null;
                    //higher depths always carry a real reference.
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
            ref EdgeMap leftMap = ref left.EdgeMapAt(position);
            ref EdgeMap rightMap = ref right.EdgeMapAt(position);
            if(!EdgeMapContentEquals(in leftMap, in rightMap))
            {
                return false;
            }
        }

        return true;
    }

    //Compares two edge maps for content equality. Two maps are
    //equal when they hold the same set of (key, child) pairs,
    //regardless of representation: an Inline-kind map and a
    //SortedArray-kind map with the same entry set are content
    //equal even though their EdgeMap.Equals would return false
    //because the heap fields (rental owners) differ.
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

        foreach(KeyValuePair<uint, HypertrieNode?> entry in EdgeMap.Enumerate(left))
        {
            if(!EdgeMap.TryGetChild(in right, entry.Key, InlineKeyLookups.Scalar, out HypertrieNode? rightChild))
            {
                return false;
            }

            //Children are canonical references by the time their
            //parent reaches Intern, so reference equality is exactly
            //content equality. For depth-1 leaves both sides carry
            //null and the comparison still holds.
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
        /// <summary>The canonical node this chain entry pins.</summary>
        public required HypertrieNode Node { get; init; }

        /// <summary>The next entry in the collision chain, or <c>null</c> at the tail.</summary>
        public ChainEntry? Next { get; init; }
    }
}
