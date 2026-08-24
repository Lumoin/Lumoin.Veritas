using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Interns <see cref="HypertrieNode"/> instances by
/// <see cref="NodeIdentifier"/>. The first time a given identifier
/// is presented with a node, the store records the node in its
/// segmented arena and returns a <see cref="NodeHandle"/>;
/// subsequent calls with the same identifier and content-equivalent
/// edge maps return the existing handle. Hash collisions are
/// detected by full content comparison and disambiguated by
/// chaining; correctness is preserved at the cost of a content
/// compare per chain probe.
/// </summary>
/// <remarks>
/// <para>
/// The store is the single owner of the canonicalisation contract:
/// any two structurally-identical nodes that pass through it become
/// handle-equal. Consumers must not mutate a node's
/// <see cref="HypertrieNode.EdgeMaps"/> array after presenting it,
/// because every other node that resolves to the same handle would
/// silently observe the change.
/// </para>
/// <para>
/// <b>Concurrent intern.</b> The intern table is a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> and
/// <see cref="Intern"/> is a CAS loop: lookup, content-walk on
/// hit, then either <c>TryAdd</c> a fresh head or <c>TryUpdate</c>
/// onto the observed head. Concurrent writers racing on the same
/// identifier converge — content-equal candidates collapse to one
/// canonical handle, content-different candidates chain. Reads
/// (lookup, content-walk, the success branch when the content
/// already matches) take no lock at all.
/// </para>
/// <para>
/// <b>Segmented arena.</b> Canonical nodes live in a segmented
/// arena of <see cref="HypertrieNode"/> values rented from
/// <see cref="VeritasMemoryPool{T}"/>. Each segment holds
/// <see cref="SegmentSize"/> entries; the outer array of segments
/// is grown by allocate-and-publish under a brief lock. Index 0 of
/// segment 0 is reserved for the <see cref="NodeHandle.None"/>
/// sentinel; real handles start at 1.
/// Readers compute
/// <c>(segment, offset) = (handle &gt;&gt; SegmentShift, handle &amp; SegmentMask)</c>,
/// index twice, and read the value — lock-free in steady state
/// because segments never move once allocated.
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
/// node is eligible for chain eviction. Arena slots themselves are
/// not reclaimed by sweep — the chain entry is dropped but the
/// arena slot remains occupied until store disposal. This matches
/// the existing build-once-and-share-forever lifecycle.
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
/// because it owns the segmented arena's pool rentals plus every
/// <see cref="EdgeMap"/>'s SortedArray-tier rentals. Disposal
/// walks every stored node, releases its EdgeMaps' rentals, then
/// releases every arena segment. Long-lived hosts that recycle
/// stores during operation should call <see cref="Dispose"/> at
/// end of life. Calling <see cref="Dispose"/> while a build or
/// sweep is in flight is a contract violation; callers ensure no
/// such operation is pending before disposing.
/// </para>
/// </remarks>
[DebuggerDisplay("NodeStore Count={Count} AcquiredSnapshots={AcquiredSnapshotCount} SweepCount={SweepCount}")]
public sealed class NodeStore: IDisposable
{
    /// <summary>The number of <see cref="HypertrieNode"/> entries per arena segment. 4096 entries × ~16 bytes = ~64 KB per segment; fits comfortably in L2 cache.</summary>
    private const int SegmentShift = 12;

    /// <summary>The size of each arena segment, derived from <see cref="SegmentShift"/>.</summary>
    private const int SegmentSize = 1 << SegmentShift;

    /// <summary>The bit mask for extracting an in-segment offset from a handle.</summary>
    private const uint SegmentMask = SegmentSize - 1;

    /// <summary>Initial capacity of the outer segments array. Grown by doubling on demand.</summary>
    private const int InitialSegmentArrayCapacity = 4;

    //The intern table: identifier -> chain head. Concurrent so
    //multiple in-flight intern calls can write without an outer
    //lock; the chain is rebuilt on hash collision and only the
    //head reference is mutated, so the dictionary's atomic
    //TryAdd / TryUpdate operations cover correctness.
    private ConcurrentDictionary<NodeIdentifier, ChainEntry> Storage { get; } = new();

    //The registry of live snapshots — the sweep's root set. Weakly
    //keyed: a snapshot that is explicitly released (reference count
    //reaching zero) deregisters deterministically, and a snapshot
    //whose holder was dropped without releasing becomes sweepable
    //once the collector reclaims it. The weak keying is what makes
    //many logical stores over one shared arena safe to supersede by
    //simply dropping the reference — reachability is liveness.
    //The marker is a shared sentinel; the value must never
    //reference the key, or the entry would pin it.
    private ConditionalWeakTable<HypertrieSnapshot, object> AcquiredSnapshots { get; } = new();

    //The registry of live root-set pins — the sweep's second root
    //source, one entry per dataset-level snapshot however many
    //roots it pins. Weakly keyed under the same liveness model as
    //AcquiredSnapshots.
    private ConditionalWeakTable<HypertrieRootSetPin, object> PinnedRootSets { get; } = new();

    private static object SnapshotMarker { get; } = new();

    //The shared/exclusive gate. Builds and edit sessions take
    //shared; sweeps take exclusive.
    private AsyncSharedExclusiveLock MutationGate { get; } = new();

    //Pool from which arena segments are rented. Retained for the
    //store's lifetime so disposal can return every segment.
    private VeritasMemoryPool<HypertrieNode> NodePool { get; }

    //The segmented arena. Each segment is an IMemoryOwner whose
    //Memory.Span[offset] is the canonical HypertrieNode at index
    //(segmentIndex * SegmentSize + offset). Segments never move
    //once allocated; readers are lock-free.
    //
    //The outer array reference is volatile-published on grow so
    //readers see either the old or new array, never a torn one.
    //New segments are allocated under SegmentAllocationLock.
    private IMemoryOwner<HypertrieNode>?[] segments;

    //Monotonic next-handle counter. Increments under
    //SegmentAllocationLock when allocating a new slot. Handle 0
    //is reserved for NodeHandle.None.
    private uint nextHandle = 1;

    //Lock taken briefly when allocating a new arena slot or
    //growing the outer segments array. Read paths never touch it.
    private object SegmentAllocationLock { get; } = new();

    //The pair arena: single-entry depth-2 subtrees stored as one
    //packed ulong per pair (high 32 bits the first key, low 32 the
    //second), addressed by SEN2 handles. Plain managed segments —
    //8-byte values need no pooling and nothing to dispose. Pairs
    //are append-only and never deduplicated: duplicate-pair waste
    //is bounded by 8 bytes per triple, whereas a dedup table would
    //cost an entry per distinct pair. Like node arena slots, pair
    //slots are not reclaimed by sweep; they live until store
    //disposal. The outer array is volatile-published on grow.
    private ulong[]?[] pairSegments = new ulong[]?[InitialSegmentArrayCapacity];

    //Monotonic next-pair counter. Index 0 is a valid pair slot —
    //the SEN2 tag bit keeps the handle encoding non-zero.
    private uint nextPairIndex;

    //Lock covering pair-slot reservation and pair-segment growth.
    private object PairAllocationLock { get; } = new();

    //Number of times a CAS-loop iteration in Intern lost the race
    //and discarded a freshly-allocated handle. Diagnostic only;
    //surfaces pathological contention via Debug.Assert.
    private int racesLost;

    private int nodeCount;

    private int sweepCount;

    private int disposed;

    //Resources whose lifetime is bound to this store's lifetime.
    //Populated by AttachToLifetime; disposed after the arena/edge-
    //map cleanup completes in Dispose. The list is lazily
    //allocated so stores that own no external resources pay no
    //per-store cost for an empty list.
    private List<IDisposable>? attachedLifetimeResources;

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
    /// only; consumers should not synchronise on this value — the
    /// weak registry means the count also shrinks when collected
    /// snapshots vanish.
    /// </summary>
    public int AcquiredSnapshotCount
    {
        get
        {
            int count = 0;
            foreach(KeyValuePair<HypertrieSnapshot, object> _ in AcquiredSnapshots)
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Constructs an empty store using
    /// <paramref name="hash"/> as the hash function and no
    /// journal. The arena's node pool defaults to a fresh
    /// <see cref="VeritasMemoryPool{HypertrieNode}"/> instance.
    /// </summary>
    /// <param name="hash">The hash function the store and its consumers share. Pass <see cref="VeritasHashing.Default"/> for the canonical xxHash64 implementation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    public NodeStore(VeritasHash hash)
        : this(hash, new VeritasMemoryPool<HypertrieNode>())
    {
    }

    /// <summary>
    /// Constructs an empty store using
    /// <paramref name="hash"/> as the hash function and
    /// <paramref name="nodePool"/> as the source of arena
    /// segments. No journal.
    /// </summary>
    /// <param name="hash">The hash function the store and its consumers share.</param>
    /// <param name="nodePool">Pool from which arena segments are rented. Retained for the store's lifetime; the caller must keep the pool alive at least as long as the store.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public NodeStore(VeritasHash hash, VeritasMemoryPool<HypertrieNode> nodePool)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(nodePool);

        Hash = hash;
        NodePool = nodePool;
        JournalAppend = null;
        JournalRead = null;

        segments = new IMemoryOwner<HypertrieNode>?[InitialSegmentArrayCapacity];
        AllocateInitialSegment();
    }

    /// <summary>
    /// Constructs an empty store using
    /// <paramref name="hash"/> as the hash function and the
    /// given journal delegates. The arena's node pool defaults to
    /// a fresh <see cref="VeritasMemoryPool{HypertrieNode}"/>
    /// instance.
    /// </summary>
    /// <param name="hash">The hash function the store and its consumers share.</param>
    /// <param name="journalAppend">The append-with-OCC delegate; must not be <c>null</c>.</param>
    /// <param name="journalRead">The read delegate; must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public NodeStore(
        VeritasHash hash,
        JournalDelegates.AppendJournalEntryAsync journalAppend,
        JournalDelegates.ReadJournalEntriesAsync journalRead)
        : this(hash, new VeritasMemoryPool<HypertrieNode>(), journalAppend, journalRead)
    {
    }

    /// <summary>
    /// Constructs an empty store with all dependencies supplied.
    /// </summary>
    /// <param name="hash">The hash function the store and its consumers share.</param>
    /// <param name="nodePool">Pool from which arena segments are rented.</param>
    /// <param name="journalAppend">The append-with-OCC delegate; must not be <c>null</c>.</param>
    /// <param name="journalRead">The read delegate; must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public NodeStore(
        VeritasHash hash,
        VeritasMemoryPool<HypertrieNode> nodePool,
        JournalDelegates.AppendJournalEntryAsync journalAppend,
        JournalDelegates.ReadJournalEntriesAsync journalRead)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(nodePool);
        ArgumentNullException.ThrowIfNull(journalAppend);
        ArgumentNullException.ThrowIfNull(journalRead);

        Hash = hash;
        NodePool = nodePool;
        JournalAppend = journalAppend;
        JournalRead = journalRead;

        segments = new IMemoryOwner<HypertrieNode>?[InitialSegmentArrayCapacity];
        AllocateInitialSegment();
    }

    //Rents segment 0 and reserves slot 0 as the NodeHandle.None
    //sentinel. Called once from each constructor; runs before any
    //Intern call so no synchronisation is needed.
    private void AllocateInitialSegment()
    {
        IMemoryOwner<HypertrieNode> seg = NodePool.Rent(SegmentSize);
        seg.Memory.Span[0] = default;
        segments[0] = seg;
        //nextHandle is initialised to 1, leaving slot 0 reserved.
    }

    /// <summary>
    /// Returns the canonical handle for the given
    /// (<paramref name="identifier"/>, <paramref name="candidate"/>)
    /// pair. If <paramref name="identifier"/> has not been seen,
    /// records <paramref name="candidate"/> in the arena and
    /// returns a fresh handle. If the identifier has been seen
    /// and an existing canonical node has content-equal edge maps
    /// (and equal depth), returns the existing handle. If the
    /// identifier has been seen but no chain entry's content
    /// matches (a hash collision), chains the candidate onto the
    /// bucket and returns the candidate's fresh handle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Concurrent intern calls on the same identifier converge
    /// through a CAS loop on the dictionary entry. A retry happens
    /// only when another writer races us; when a retry discovers
    /// the bucket already holds a content-equal entry, the
    /// previously-allocated arena slot is left occupied (no
    /// reclamation in the steady state). This matches the existing
    /// build-once lifecycle and the existing waste-on-loss pattern
    /// the chain-entry class allocation had.
    /// </para>
    /// </remarks>
    /// <param name="identifier">The candidate's identifier.</param>
    /// <param name="candidate">The candidate node; <see cref="HypertrieNode.EdgeMaps"/> must not be <c>null</c>.</param>
    /// <returns>The canonical handle for this identifier and content.</returns>
    public NodeHandle Intern(NodeIdentifier identifier, HypertrieNode candidate)
    {
        if(candidate.EdgeMaps is null)
        {
            throw new ArgumentException("Candidate node must have a non-null EdgeMaps array.", nameof(candidate));
        }

        NodeHandle freshHandle = AllocateHandle(candidate);

        while(true)
        {
            if(!Storage.TryGetValue(identifier, out ChainEntry? observedHead))
            {
                //First node observed for this identifier — try to claim the bucket.
                ChainEntry freshHead = new() { NodeHandle = freshHandle, Next = null };
                if(Storage.TryAdd(identifier, freshHead))
                {
                    Interlocked.Increment(ref nodeCount);

                    Debug.Assert(!freshHandle.IsNone, "Intern must return a real handle, never the sentinel.");
                    return freshHandle;
                }

                //Lost the race; another writer took the bucket. Retry from the top.
                Interlocked.Increment(ref racesLost);
                Debug.Assert(Volatile.Read(ref racesLost) <= 1000,
                    "Excessive contention on Intern; slot waste exceeded threshold.");
                continue;
            }

            //Walk the observed chain looking for a content-equal node.
            ChainEntry? cursor = observedHead;

            while(cursor is not null)
            {
                HypertrieNode cursorNode = GetByHandle(cursor.NodeHandle);
                if(cursorNode.Depth == candidate.Depth && NodeContentEquals(cursorNode, candidate))
                {
                    //Found content-equal entry; the freshly-allocated handle is wasted.
                    return cursor.NodeHandle;
                }

                cursor = cursor.Next;
            }

            //No content match in the observed chain — push the
            //candidate's handle onto the head and try to publish
            //the new head atomically. If we lose the CAS, another
            //writer changed the chain (added a content-equal
            //entry, or added another collision); we go back to the
            //top and re-examine.
            ChainEntry newHead = new() { NodeHandle = freshHandle, Next = observedHead };
            if(Storage.TryUpdate(identifier, newHead, observedHead))
            {
                Interlocked.Increment(ref nodeCount);

                Debug.Assert(!freshHandle.IsNone, "Intern must return a real handle, never the sentinel.");
                return freshHandle;
            }

            //Lost the CAS; retry.
            Interlocked.Increment(ref racesLost);
            Debug.Assert(Volatile.Read(ref racesLost) <= 1000,
                "Excessive contention on Intern; slot waste exceeded threshold.");
        }
    }

    /// <summary>
    /// Resolves a handle to the corresponding
    /// <see cref="HypertrieNode"/>.
    /// </summary>
    /// <param name="handle">The handle to resolve.</param>
    /// <returns>The node identified by <paramref name="handle"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="handle"/> is <see cref="NodeHandle.None"/> or out of range.</exception>
    public HypertrieNode GetByHandle(NodeHandle handle)
    {
        //NodeHandle.None encodes as 0 and is rejected by the range check below
        //with ArgumentOutOfRangeException — the documented, observable contract.
        uint encoded = handle.Encoded;
        if(encoded == 0 || encoded >= Volatile.Read(ref nextHandle))
        {
            throw new ArgumentOutOfRangeException(nameof(handle), encoded,
                $"Handle must be between 1 and {Volatile.Read(ref nextHandle) - 1}; '0' is reserved for NodeHandle.None.");
        }

        int segmentIndex = (int)(encoded >> SegmentShift);
        int offset = (int)(encoded & SegmentMask);
        IMemoryOwner<HypertrieNode>?[] segs = Volatile.Read(ref segments);
        return segs[segmentIndex]!.Memory.Span[offset];
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
    /// Appends a single-entry depth-2 pair to the pair arena and
    /// returns its SEN2 handle. The pair carries the subtree's two
    /// remaining-position keys in ascending original-position
    /// order. Pairs are not deduplicated; identical pairs occupy
    /// distinct slots, and content identity is preserved by the
    /// identifier formula, not by slot sharing.
    /// </summary>
    /// <param name="first">The lower remaining position's key.</param>
    /// <param name="second">The higher remaining position's key.</param>
    /// <returns>An SEN2 handle addressing the appended pair.</returns>
    public NodeHandle AllocateSingleEntryPair(uint first, uint second)
    {
        lock(PairAllocationLock)
        {
            uint index = nextPairIndex;
            if((index & ~NodeHandle.Sen2ContentMask) != 0U)
            {
                throw new InvalidOperationException(
                    $"Pair arena exhausted: next index 0x{index:X} exceeds the 30-bit pair-index space.");
            }

            int segIndex = (int)(index >> SegmentShift);
            int offset = (int)(index & SegmentMask);

            if(segIndex >= pairSegments.Length)
            {
                int newCapacity = pairSegments.Length * 2;
                while(segIndex >= newCapacity)
                {
                    newCapacity *= 2;
                }

                ulong[]?[] newArray = new ulong[]?[newCapacity];
                Array.Copy(pairSegments, newArray, pairSegments.Length);
                Volatile.Write(ref pairSegments, newArray);
            }

            pairSegments[segIndex] ??= new ulong[SegmentSize];
            pairSegments[segIndex]![offset] = ((ulong)first << 32) | second;
            Volatile.Write(ref nextPairIndex, index + 1);

            return NodeHandle.ForSingleEntryPair(index);
        }
    }

    /// <summary>
    /// Resolves an SEN2 handle to its pair — the single-entry
    /// depth-2 subtree's two remaining-position keys in ascending
    /// original-position order.
    /// </summary>
    /// <param name="handle">The SEN2 handle to resolve.</param>
    /// <returns>The pair's keys, lower remaining position first.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="handle"/> is not an SEN2 handle, or its index is out of range.</exception>
    public (uint First, uint Second) GetPair(NodeHandle handle)
    {
        if(!handle.IsSingleEntryPair)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), handle.Encoded,
                "Handle must be a single-entry-pair (SEN2) handle.");
        }

        uint index = handle.PairIndex;
        if(index >= Volatile.Read(ref nextPairIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(handle), index,
                $"Pair index must be below {Volatile.Read(ref nextPairIndex)}.");
        }

        int segIndex = (int)(index >> SegmentShift);
        int offset = (int)(index & SegmentMask);
        ulong[]?[] segs = Volatile.Read(ref pairSegments);
        ulong packed = segs[segIndex]![offset];

        return ((uint)(packed >> 32), (uint)packed);
    }

    /// <summary>
    /// Binds <paramref name="resource"/>'s lifetime to this store.
    /// The resource is disposed by <see cref="Dispose"/> after the
    /// arena and edge-map cleanup completes. Use this to bind
    /// pools or other external resources whose validity must
    /// outlast every rental the store made through them; the
    /// resource is disposed deterministically when the store is.
    /// </summary>
    /// <param name="resource">The resource to dispose alongside this store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <c>null</c>.</exception>
    public void AttachToLifetime(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        attachedLifetimeResources ??= [];
        attachedLifetimeResources.Add(resource);
    }

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

        AcquiredSnapshots.Add(snapshot, SnapshotMarker);
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

        AcquiredSnapshots.Remove(snapshot);
    }

    /// <summary>
    /// Pins a set of roots in one registration — the dataset-level
    /// counterpart of a <see cref="HypertrieSnapshot"/>. Every
    /// pinned root stays sweep-reachable until the pin is disposed
    /// or its holder becomes unreachable.
    /// </summary>
    /// <param name="roots">The root handles to pin; copied.</param>
    /// <returns>The registered pin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="roots"/> is <c>null</c>.</exception>
    public HypertrieRootSetPin PinRoots(IReadOnlyCollection<NodeHandle> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        return new HypertrieRootSetPin(this, [.. roots]);
    }

    /// <summary>
    /// Adds <paramref name="pin"/> to the registry of live root-set
    /// pins. Called by the <see cref="HypertrieRootSetPin"/>
    /// constructor; consumers do not call this directly.
    /// </summary>
    /// <param name="pin">The pin to register.</param>
    internal void RegisterRootSetPin(HypertrieRootSetPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        PinnedRootSets.Add(pin, SnapshotMarker);
    }

    /// <summary>
    /// Removes <paramref name="pin"/> from the registry of live
    /// root-set pins. Called by
    /// <see cref="HypertrieRootSetPin.Dispose"/>; consumers do not
    /// call this directly.
    /// </summary>
    /// <param name="pin">The pin to deregister.</param>
    internal void UnregisterRootSetPin(HypertrieRootSetPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        PinnedRootSets.Remove(pin);
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

        return AcquiredSnapshots.TryGetValue(snapshot, out _);
    }

    /// <summary>
    /// Acquires the mutation gate in shared mode. Multiple shared
    /// holders may run concurrently. Builds and edit-session
    /// commits hold this scope while they mutate the intern
    /// table; sweeps wait for the scope to release before
    /// claiming the gate exclusively. Consumers composing many
    /// logical stores over one arena (dataset edit sessions) hold
    /// this scope across their build/patch sequences so snapshot
    /// construction and a concurrent sweep cannot interleave.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A scope handle whose disposal releases the shared hold.</returns>
    public ValueTask<SharedScope> EnterSharedScopeAsync(CancellationToken cancellationToken)
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
    /// a handle not on that reachable set. Acquires the mutation
    /// gate exclusively for the duration of the sweep, blocking
    /// concurrent builds and edit-session commits while it runs;
    /// reads are unaffected. Arena slots themselves are not
    /// reclaimed — the slot remains occupied until store
    /// disposal — but the chain entry is dropped, releasing its
    /// share of <see cref="Count"/>.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the sweep at any per-step check.</param>
    /// <returns>Statistics describing the sweep.</returns>
    public async ValueTask<SweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        ExclusiveScope scope = await EnterExclusiveScopeAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            //The weak registries yield only holders still alive: an
            //explicitly released snapshot or disposed pin
            //deregistered itself, and a dropped-without-release one
            //disappears once the collector reclaims it — superseded
            //logical stores and dataset states over a shared arena
            //become sweepable by unreachability alone.
            List<HypertrieSnapshot> live = [];
            foreach((HypertrieSnapshot snapshot, object _) in AcquiredSnapshots)
            {
                live.Add(snapshot);
            }

            List<HypertrieRootSetPin> pins = [];
            foreach((HypertrieRootSetPin pin, object _) in PinnedRootSets)
            {
                pins.Add(pin);
            }

            HashSet<NodeHandle> reachable = [];

            foreach(HypertrieSnapshot snapshot in live)
            {
                MarkReachable(snapshot.Root, reachable, cancellationToken);
            }

            foreach(HypertrieRootSetPin pin in pins)
            {
                foreach(NodeHandle root in pin.Roots)
                {
                    MarkReachable(root, reachable, cancellationToken);
                }
            }

            return PruneUnreachable(reachable, cancellationToken);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Releases the resources owned by this store. Disposes every
    /// stored <see cref="EdgeMap"/>'s SortedArray-tier rentals
    /// and every arena segment. Safe to call only when no build
    /// or sweep is in flight; concurrent disposal is a contract
    /// violation. Disposing more than once is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Slab clearing.</b> Each arena segment's span is cleared
    /// before the segment returns to <see cref="NodePool"/>, so
    /// the pool's reusable buffer carries no references to nodes'
    /// <see cref="EdgeMap"/>[] arrays. The arrays are GC-collectible
    /// while the pool's slab inventory remains intact for the next
    /// store.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        //Walk every allocated arena slot, releasing each
        //EdgeMap's SortedArray rentals. Slot 0 holds the sentinel
        //(default(HypertrieNode), EdgeMaps == null) — skip it.
        uint allocated = Volatile.Read(ref nextHandle);
        IMemoryOwner<HypertrieNode>?[] segs = segments;
        for(uint h = 1; h < allocated; h++)
        {
            int segIndex = (int)(h >> SegmentShift);
            int offset = (int)(h & SegmentMask);
            IMemoryOwner<HypertrieNode>? seg = segs[segIndex];
            if(seg is null)
            {
                continue;
            }

            HypertrieNode node = seg.Memory.Span[offset];
            if(node.EdgeMaps is null)
            {
                continue;
            }

            for(int i = 0; i < node.EdgeMaps.Length; i++)
            {
                EdgeMap.DisposeRentals(ref node.EdgeMaps[i]);
            }
        }

        //Clear each slab before returning it to the pool so the
        //pool-retained buffer holds no references to nodes'
        //EdgeMap[] arrays.
        for(int i = 0; i < segs.Length; i++)
        {
            IMemoryOwner<HypertrieNode>? slab = segs[i];
            if(slab is null)
            {
                continue;
            }

            slab.Memory.Span.Clear();
            slab.Dispose();
            segs[i] = null;
        }

        //Dispose lifetime-bound resources. Order matters: arena
        //and edge-map rentals returned above must complete before
        //a pool is disposed, since a disposed pool rejects further
        //Rent/Return calls.
        if(attachedLifetimeResources is not null)
        {
            foreach(IDisposable resource in attachedLifetimeResources)
            {
                resource.Dispose();
            }
        }

        MutationGate.Dispose();
    }

    //Allocates a fresh arena slot, writes the candidate node into
    //it, and returns the handle. Grows the segments array on
    //demand. The lock covers slot reservation, segment allocation
    //if needed, and the slot write — so any reader that
    //subsequently calls GetByHandle on the returned handle sees
    //the populated slot via volatile reads of the segments array.
    private NodeHandle AllocateHandle(HypertrieNode node)
    {
        lock(SegmentAllocationLock)
        {
            uint handle = nextHandle;
            if((handle & (NodeHandle.SenTag | NodeHandle.Sen2Tag)) != 0U)
            {
                throw new InvalidOperationException(
                    $"Arena handle space exhausted: next handle 0x{handle:X} collides with a tag bit. " +
                    $"The arena supports up to {NodeHandle.Sen2ContentMask} slots.");
            }

            int segIndex = (int)(handle >> SegmentShift);
            int offset = (int)(handle & SegmentMask);

            //Grow the outer segments array if needed.
            if(segIndex >= segments.Length)
            {
                int newCapacity = segments.Length * 2;
                while(segIndex >= newCapacity)
                {
                    newCapacity *= 2;
                }

                IMemoryOwner<HypertrieNode>?[] newArray = new IMemoryOwner<HypertrieNode>?[newCapacity];
                Array.Copy(segments, newArray, segments.Length);
                Volatile.Write(ref segments, newArray);
            }

            //Allocate the segment if it doesn't exist.
            if(segments[segIndex] is null)
            {
                segments[segIndex] = NodePool.Rent(SegmentSize);
            }

            segments[segIndex]!.Memory.Span[offset] = node;
            Volatile.Write(ref nextHandle, handle + 1);

            return NodeHandle.FromEncoded(handle);
        }
    }

    //Walks every reachable node from the given root and adds its
    //handle to the reachable set. Iterative to satisfy the "no
    //recursion in graph algorithms" rule; the work stack carries
    //handles pending visit and dedup is performed by the set
    //itself.
    private void MarkReachable(NodeHandle root, HashSet<NodeHandle> reachable, CancellationToken cancellationToken)
    {
        Stack<NodeHandle> work = new();
        work.Push(root);

        while(work.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NodeHandle currentHandle = work.Pop();
            //SEN handles carry their data inline in the parent's
            //edge map slot and SEN2 handles live in the pair arena,
            //which sweep never prunes — neither holds a node arena
            //entry to mark. None handles are absence sentinels.
            if(!currentHandle.IsArenaHandle)
            {
                continue;
            }

            if(!reachable.Add(currentHandle))
            {
                continue;
            }

            HypertrieNode current = GetByHandle(currentHandle);
            for(int position = 0; position < current.Depth; position++)
            {
                foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(current.EdgeMaps[position]))
                {
                    if(entry.Value.IsArenaHandle)
                    {
                        work.Push(entry.Value);
                    }
                }
            }
        }
    }

    //Walks the intern table once, dropping bucket entries whose
    //handle is not in the reachable set. Buckets are mutated in a
    //second pass to avoid modifying the dictionary during
    //enumeration. Returns sweep statistics for the diagnostic
    //surface.
    private SweepResult PruneUnreachable(HashSet<NodeHandle> reachable, CancellationToken cancellationToken)
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
                if(!reachable.Contains(head.NodeHandle))
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
                if(reachable.Contains(cursor.NodeHandle))
                {
                    newHead = new ChainEntry { NodeHandle = cursor.NodeHandle, Next = newHead };
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
    private bool NodeContentEquals(HypertrieNode left, HypertrieNode right)
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
    //SortedArray-kind map with the same entry set are content
    //equal even though their EdgeMap.Equals would return false
    //because the heap fields (rental owners) differ.
    private bool EdgeMapContentEquals(in EdgeMap left, in EdgeMap right)
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

        foreach(KeyValuePair<uint, NodeHandle> entry in EdgeMap.Enumerate(left))
        {
            if(!EdgeMap.TryGetChild(in right, entry.Key, InlineKeyLookups.Scalar, out NodeHandle rightChild))
            {
                return false;
            }

            if(!ChildContentEquals(entry.Value, rightChild))
            {
                return false;
            }
        }

        return true;
    }

    //Compares two child slots for content equality. FN children
    //are canonical handles by the time their parent reaches
    //Intern, so handle equality is exactly content equality —
    //likewise None sentinels and SEN slots, whose content is the
    //encoding. SEN2 slots are the exception: pairs are not
    //deduplicated, so two distinct pair indexes may carry the same
    //content; resolve both and compare the pairs themselves.
    private bool ChildContentEquals(NodeHandle left, NodeHandle right)
    {
        if(left == right)
        {
            return true;
        }

        if(left.IsSingleEntryPair && right.IsSingleEntryPair)
        {
            return GetPair(left) == GetPair(right);
        }

        return false;
    }

    //One node in the collision chain for a given identifier. A
    //chain length above one indicates a hash collision and is
    //expected to be vanishingly rare in practice; the chain is
    //kept simple — singly-linked, head insertion — because lookups
    //walk it only on collision and writes only ever push a new
    //head.
    private sealed class ChainEntry
    {
        /// <summary>The arena handle for the canonical node this chain entry pins.</summary>
        public required NodeHandle NodeHandle { get; init; }

        /// <summary>The next entry in the collision chain, or <c>null</c> at the tail.</summary>
        public ChainEntry? Next { get; init; }
    }
}
