using Lumoin.Veritas.Core.Hypertrie.Storage;
using System;
using System.Diagnostics;
using System.Threading;

namespace Lumoin.Veritas.Core.Hypertrie;

/// <summary>
/// A reference-counted handle to a hypertrie root and its
/// associated <see cref="NodeStore"/>. Consumers acquire a
/// snapshot to obtain a stable view of the graph and release it
/// when finished; sweep / reclamation passes use the set of
/// currently-acquired snapshots to determine which canonical
/// nodes are still reachable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why snapshots.</b> The hypertrie design is built on
/// content-addressed deduplication: nodes that are interned in
/// the <see cref="NodeStore"/> are immutable, and modifications
/// produce new canonical nodes that share most of the structure
/// with the originals (a persistent-data-structure
/// architecture). A snapshot pins one root and the canonical
/// nodes reachable from it; multiple snapshots can coexist over
/// the same store, each pinning a different root, with all
/// shared nodes shared in storage.
/// </para>
/// <para>
/// <b>Identity.</b> Every snapshot carries the
/// <see cref="NodeIdentifier"/> of its root as its
/// <see cref="Id"/>. This is content-addressed: two snapshots
/// with structurally-identical contents have the same
/// <see cref="Id"/>, and the <see cref="Id"/> alone is enough to
/// rebuild the snapshot deterministically given the journal that
/// produced it. <see cref="Id"/> is computed during root
/// interning and supplied to the snapshot constructor by the
/// caller; the constructor does not recompute it.
/// </para>
/// <para>
/// <b>Lifecycle.</b> A snapshot starts with reference count 1 —
/// the creator holds the initial reference. Calling
/// <see cref="Acquire"/> increments the count; calling
/// <see cref="Release"/> or <see cref="Dispose"/> decrements it.
/// When the count reaches zero the snapshot deregisters from the
/// store and becomes eligible for reclamation in a future sweep.
/// </para>
/// <para>
/// <b>Thread safety.</b> Reference-count operations are atomic
/// (<see cref="Interlocked.Increment(ref int)"/> /
/// <see cref="Interlocked.Decrement(ref int)"/>). Because the
/// hypertrie root and the canonical nodes reachable from it are
/// immutable, multiple iterators on the same snapshot are safe
/// to run concurrently with no further coordination. Deregistration
/// from the store on final release is guarded so two threads
/// cannot both observe the count crossing zero.
/// </para>
/// <para>
/// <b>Disposal.</b> <see cref="Dispose"/> is equivalent to one
/// <see cref="Release"/> call. Iterators are
/// <see cref="IDisposable"/> precisely so they can release their
/// snapshot reference at the end of their lifetime; consumers
/// should likewise dispose snapshots when finished. Disposing
/// twice is a no-op (a guard prevents double-decrement).
/// </para>
/// </remarks>
[DebuggerDisplay("HypertrieSnapshot Id={Id.Value:X16} RefCount={RefCount}")]
public sealed class HypertrieSnapshot: IDisposable
{
    //Reference count and disposed flag are declared as private
    //fields rather than auto-properties because the Interlocked
    //primitives used for atomic updates require a `ref` parameter,
    //which only fields can supply directly. Reads still go through
    //the public RefCount property below for consistency with the
    //wider codebase convention of property access over field
    //access.
    private int refCount;

    private int disposed;

    /// <summary>The intern table holding the canonical node instances reachable from <see cref="Root"/>.</summary>
    public NodeStore Store { get; }

    /// <summary>The handle of the depth-3 root pinned by this snapshot. Resolve to a <see cref="HypertrieNode"/> via <see cref="NodeStore.GetByHandle(NodeHandle)"/> on <see cref="Store"/>.</summary>
    public NodeHandle Root { get; }

    /// <summary>The content-addressed identifier of <see cref="Root"/>; the snapshot's identity in the journal.</summary>
    public NodeIdentifier Id { get; }

    /// <summary>The current reference count. Diagnostic only — not a stable consistency primitive.</summary>
    public int RefCount => Volatile.Read(ref refCount);

    /// <summary>
    /// Constructs a new snapshot with reference count 1 and
    /// registers it with <paramref name="store"/>. The supplied
    /// <paramref name="id"/> must be the
    /// <see cref="NodeIdentifier"/> computed for
    /// <paramref name="root"/> during interning; the constructor
    /// trusts the caller and does not recompute it.
    /// </summary>
    /// <param name="store">The intern table.</param>
    /// <param name="root">The hypertrie root handle to pin.</param>
    /// <param name="id">The content-addressed identifier of <paramref name="root"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    public HypertrieSnapshot(NodeStore store, NodeHandle root, NodeIdentifier id)
    {
        ArgumentNullException.ThrowIfNull(store);

        Store = store;
        Root = root;
        Id = id;
        refCount = 1;

        store.RegisterSnapshot(this);
    }

    /// <summary>
    /// Increments the reference count and returns this snapshot.
    /// Safe to call from any thread; throws if the snapshot has
    /// already been fully released.
    /// </summary>
    /// <returns>This snapshot, for fluent chaining.</returns>
    /// <exception cref="ObjectDisposedException">The snapshot has already been fully released.</exception>
    public HypertrieSnapshot Acquire()
    {
        int updated = Interlocked.Increment(ref refCount);

        if(updated <= 1)
        {
            //We were already at zero (or below) — undo the increment and reject.
            Interlocked.Decrement(ref refCount);

            throw new ObjectDisposedException(nameof(HypertrieSnapshot), "Cannot acquire a snapshot whose reference count has already reached zero.");
        }

        return this;
    }

    /// <summary>
    /// Decrements the reference count. When the count reaches
    /// zero the snapshot is deregistered from
    /// <see cref="Store"/>; further <see cref="Acquire"/> calls
    /// throw. Calling <see cref="Release"/> after the count is
    /// already zero is a no-op — the count never goes negative.
    /// </summary>
    public void Release()
    {
        //Bounded decrement: if the count is already zero we leave
        //it at zero rather than letting it slide negative. This
        //makes Release idempotent at zero, which keeps Dispose
        //safe to call even after a test has already taken the
        //count to zero via direct Release.
        int observed;

        do
        {
            observed = Volatile.Read(ref refCount);

            if(observed == 0)
            {
                return;
            }
        }
        while(Interlocked.CompareExchange(ref refCount, observed - 1, observed) != observed);

        if(observed - 1 == 0)
        {
            Store.UnregisterSnapshot(this);
        }
    }

    /// <summary>
    /// Releases this snapshot exactly once, even when called
    /// multiple times. Equivalent to one <see cref="Release"/>
    /// call on the first invocation; subsequent invocations are
    /// no-ops.
    /// </summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Release();
    }
}
