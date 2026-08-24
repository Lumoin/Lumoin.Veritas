using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Threading;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// One open transaction over a <see cref="HypertrieSnapshot"/>.
/// Accumulates additions and removals in an
/// <see cref="EditBuffer"/>; produces a new snapshot on
/// <see cref="CommitAsync"/>; writes an
/// <see cref="EditSessionEntryKind.Abandoned"/> entry on
/// <see cref="DisposeAsync"/> when not committed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> A session is in one of three internal states:
/// <c>Open</c> after construction, <c>Committed</c> after a
/// successful <see cref="CommitAsync"/>, or <c>Disposed</c> after
/// <see cref="DisposeAsync"/>. The transitions are one-way:
/// <c>Open → Committed</c> on successful commit; <c>Open →
/// Disposed</c> or <c>Committed → Disposed</c> on dispose. Calling
/// <see cref="Add"/>, <see cref="Remove"/>,
/// <see cref="AddRange"/>, or <see cref="RemoveRange"/> in any
/// state but <c>Open</c> throws
/// <see cref="InvalidOperationException"/>; calling
/// <see cref="CommitAsync"/> in any state but <c>Open</c> throws
/// the same.
/// </para>
/// <para>
/// <b>What dispose does.</b> Dispose has two responsibilities:
/// release the shared mutation gate the session holds, and
/// release the base-snapshot reference the session acquired at
/// open. When the session was not committed, dispose also
/// attempts to write an
/// <see cref="EditSessionEntryKind.Abandoned"/> entry on the
/// journal. The append is best-effort — a failure inside dispose
/// is swallowed because there is no place to surface it; the
/// scope and snapshot are still released so the caller's resource
/// hygiene is preserved.
/// </para>
/// <para>
/// <b>Optimistic concurrency.</b> The <c>Started</c> entry was
/// written when the session opened; if another session commits
/// against the same base before this session does, this session's
/// <see cref="CommitAsync"/> raises
/// <see cref="EditSessionConcurrencyException"/>. Callers handle
/// the exception by reading the actual head from the exception's
/// <see cref="EditSessionConcurrencyException.ActualHead"/>,
/// rebasing their edit buffer if appropriate, opening a new
/// session against the new base, and retrying.
/// </para>
/// <para>
/// <b>Empty commit.</b> When the effective delta is empty —
/// every literal add was already present, every literal remove was
/// already absent — <see cref="CommitAsync"/> writes no
/// <c>Committed</c> entry, returns the base snapshot acquired
/// for the caller (a fresh reference), and transitions to
/// <c>Committed</c>.
/// </para>
/// <para>
/// <b>Thread safety.</b> A session is owned by exactly one
/// logical caller. Concurrent <see cref="Add"/> / <see cref="Remove"/>
/// against one session is a contract violation; the underlying
/// <see cref="EditBuffer"/> is not thread-safe. State transitions
/// (Open → Committed → Disposed) use
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>
/// so two threads cannot both observe the session as <c>Open</c>
/// and double-commit or commit-then-double-dispose-as-abandoned.
/// </para>
/// </remarks>
[DebuggerDisplay("EditSession Id={Id} State={CurrentState} Buffer={Buffer.Count}")]
public sealed class EditSession: IAsyncDisposable
{
    //State machine values; encoded as int so Interlocked.CompareExchange applies.
    private const int StateOpen = 0;
    private const int StateCommitted = 1;
    private const int StateDisposed = 2;

    private int state = StateOpen;

    //The base snapshot the session branches from. Acquired by
    //NodeStore.OpenEditSessionAsync before this session was
    //constructed; released here at dispose time.
    private HypertrieSnapshot BaseSnapshot { get; }

    //The shared mutation-gate scope held for the session's
    //lifetime. Released at dispose time.
    private SharedScope SharedScope { get; }

    //The store hosting the canonical nodes. Used to intern the
    //patched nodes at commit time and to find the journal-append
    //delegate for session-lifecycle entries.
    private NodeStore Store { get; }

    /// <summary>The accumulated edits the session intends to commit.</summary>
    public EditBuffer Buffer { get; } = new();

    /// <summary>The session's opaque identifier; identifies session-lifecycle entries in the journal.</summary>
    public SessionId Id { get; }

    /// <summary>The base snapshot identifier the session branches from.</summary>
    public NodeIdentifier BaseSnapshotId => BaseSnapshot.Id;

    //Diagnostic projection of the state field for the debugger
    //display. Not the truth source — that is `state` — but a
    //convenient string view.
    private string CurrentState => Volatile.Read(ref state) switch
    {
        StateOpen => "Open",
        StateCommitted => "Committed",
        StateDisposed => "Disposed",
        _ => "Unknown",
    };

    /// <summary>
    /// Constructs a new edit session. Called by
    /// <see cref="NodeStore.OpenEditSessionAsync"/>; consumers do
    /// not call this directly.
    /// </summary>
    /// <param name="store">The store hosting the canonical nodes and the journal delegates.</param>
    /// <param name="baseSnapshot">The snapshot the session branches from. Must already be acquired on the caller's behalf — the session takes ownership of that reference and releases it at dispose time.</param>
    /// <param name="sharedScope">The shared mutation-gate scope the session holds for its lifetime. The session releases it at dispose time.</param>
    /// <param name="id">The session's opaque identifier.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="baseSnapshot"/> is <c>null</c>.</exception>
    internal EditSession(NodeStore store, HypertrieSnapshot baseSnapshot, SharedScope sharedScope, SessionId id)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(baseSnapshot);

        Store = store;
        BaseSnapshot = baseSnapshot;
        SharedScope = sharedScope;
        Id = id;
    }

    /// <summary>
    /// Records that <paramref name="triple"/> should be added at
    /// commit. Last-write-wins against any prior edit for the same
    /// triple in this session.
    /// </summary>
    /// <param name="triple">The triple to add.</param>
    /// <exception cref="InvalidOperationException">The session is not <c>Open</c>.</exception>
    public void Add(EncodedTriple triple)
    {
        ThrowIfNotOpen();
        Buffer.Add(triple);
    }

    /// <summary>
    /// Records that <paramref name="triple"/> should be removed at
    /// commit. Last-write-wins against any prior edit for the same
    /// triple in this session.
    /// </summary>
    /// <param name="triple">The triple to remove.</param>
    /// <exception cref="InvalidOperationException">The session is not <c>Open</c>.</exception>
    public void Remove(EncodedTriple triple)
    {
        ThrowIfNotOpen();
        Buffer.Remove(triple);
    }

    /// <summary>
    /// Records every triple in <paramref name="triples"/> as
    /// scheduled for addition. Equivalent to calling
    /// <see cref="Add"/> in turn.
    /// </summary>
    /// <param name="triples">The triples to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The session is not <c>Open</c>.</exception>
    public void AddRange(IEnumerable<EncodedTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ThrowIfNotOpen();

        foreach(EncodedTriple triple in triples)
        {
            Buffer.Add(triple);
        }
    }

    /// <summary>
    /// Records every triple in <paramref name="triples"/> as
    /// scheduled for removal. Equivalent to calling
    /// <see cref="Remove"/> in turn.
    /// </summary>
    /// <param name="triples">The triples to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The session is not <c>Open</c>.</exception>
    public void RemoveRange(IEnumerable<EncodedTriple> triples)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ThrowIfNotOpen();

        foreach(EncodedTriple triple in triples)
        {
            Buffer.Remove(triple);
        }
    }

    /// <summary>
    /// Commits the session's accumulated edits, producing a new
    /// <see cref="HypertrieSnapshot"/>. Transitions the session
    /// from <c>Open</c> to <c>Committed</c>; subsequent calls
    /// throw.
    /// </summary>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    /// <returns>
    /// A freshly-acquired snapshot of the post-commit state.
    /// When the effective delta is empty, the returned snapshot
    /// is an acquired reference to the base snapshot and no
    /// journal entry is written.
    /// </returns>
    /// <exception cref="InvalidOperationException">The session is not <c>Open</c>.</exception>
    /// <exception cref="EditSessionConcurrencyException">Another session committed against the same base; the session's <c>Started</c> entry's parent is no longer the journal head.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was triggered.</exception>
    public async ValueTask<HypertrieSnapshot> CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfNotOpen();
        cancellationToken.ThrowIfCancellationRequested();

        //Snapshot the pending edits into stable lists; the buffer
        //may contain both Add and Remove kinds with last-write-wins
        //semantics already collapsed.
        List<EncodedTriple> literalAdds = [.. Buffer.PendingAdditions];
        List<EncodedTriple> literalRemoves = [.. Buffer.PendingRemovals];

        ApplyDeltaResult delta = HypertrieOpsPatching.ApplyDelta(
            BaseSnapshot,
            literalAdds,
            literalRemoves,
            Store,
            BuildPools.CreateDefault());

        //Empty effective delta — return base acquired, no journal
        //entry, transition to Committed.
        if(delta.EffectiveAdditions.Count == 0 && delta.EffectiveRemovals.Count == 0)
        {
            HypertrieSnapshot acquired = BaseSnapshot.Acquire();
            TransitionToCommitted();
            return acquired;
        }

        //Append the committed entry. The journal's OCC contract
        //rejects the append when the head moved between session
        //open and now, surfacing as EditSessionConcurrencyException.
        if(Store.JournalAppend is not null)
        {
            ImmutableArray<EncodedTriple> additionsImmutable = [.. delta.EffectiveAdditions];
            ImmutableArray<EncodedTriple> removalsImmutable = [.. delta.EffectiveRemovals];

            JournalEntry committedEntry = JournalEntry.Committed(
                hash: Store.Hash,
                parentId: BaseSnapshot.Id,
                childId: delta.Id,
                sessionId: Id,
                additions: additionsImmutable,
                removals: removalsImmutable);

            await Store.JournalAppend(committedEntry, BaseSnapshot.Id, cancellationToken).ConfigureAwait(false);
        }

        //Construct a new snapshot wrapping the new root and its
        //identifier. Constructing the snapshot registers it with
        //the store; that has to happen under the mutation gate (we
        //hold a shared scope) so a concurrent sweep does not run
        //between intern and registration.
        HypertrieSnapshot newSnapshot = new(Store, delta.Root, delta.Id);
        TransitionToCommitted();
        return newSnapshot;
    }

    /// <summary>
    /// Releases the session's resources. When called on an
    /// <c>Open</c> session, attempts to write an
    /// <see cref="EditSessionEntryKind.Abandoned"/> entry to the
    /// journal — best-effort; failures are swallowed because there
    /// is no place to surface them. Always releases the shared
    /// scope and the base-snapshot reference.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        int previous = Interlocked.Exchange(ref state, StateDisposed);
        if(previous == StateDisposed)
        {
            //Double dispose — nothing to do; the previous call
            //already released the scope and the snapshot.
            return;
        }

        try
        {
            //If the session was open, attempt to write the abandon
            //entry. The journal head moved means another session
            //committed first — the abandon is still meaningful as
            //"this session intended to commit and did not," so
            //attempt the append against whatever the current head
            //is. We deliberately swallow any exception inside this
            //branch: there is no caller to surface it to, and we
            //must not leak the scope or the snapshot reference.
            if(previous == StateOpen && Store.JournalAppend is not null)
            {
                JournalEntry abandonedEntry = JournalEntry.Abandoned(BaseSnapshot.Id, Id);

                try
                {
                    await Store.JournalAppend(abandonedEntry, BaseSnapshot.Id, CancellationToken.None).ConfigureAwait(false);
                }
                catch(EditSessionConcurrencyException)
                {
                    //Head moved before we could record the abandon.
                    //Acceptable — the session's lifecycle is
                    //recorded by the Started entry written at open;
                    //the absence of either Committed or Abandoned
                    //against this SessionId is a recoverable signal
                    //to journal replay that the session crashed.
                }
                catch(OperationCanceledException)
                {
                    //Should not happen — we passed
                    //CancellationToken.None — but defensive against
                    //future implementations that introduce other
                    //cancellation surfaces.
                }
                catch(InvalidOperationException)
                {
                    //Defensive against journal implementations that
                    //surface state-violation errors during shutdown.
                }
            }
        }
        finally
        {
            //Release the snapshot reference first, then the shared
            //scope. Order is intentional: the snapshot's
            //deregistration touches NodeStore-internal state that
            //must run while the gate is still held; otherwise a
            //concurrent sweep could fire between the two.
            BaseSnapshot.Release();
            await SharedScope.DisposeAsync().ConfigureAwait(false);
        }
    }

    //Throws when the session is not in the Open state. Inlined as
    //a one-liner so call sites read fluently and the JIT can
    //inline the predicate.
    private void ThrowIfNotOpen()
    {
        int observed = Volatile.Read(ref state);
        if(observed != StateOpen)
        {
            throw new InvalidOperationException(
                observed switch
                {
                    StateCommitted => "The edit session has already been committed.",
                    StateDisposed => "The edit session has been disposed.",
                    _ => "The edit session is not in an open state.",
                });
        }
    }

    //CAS the state from Open to Committed. The transition must
    //succeed exactly once per session. Because the only callers
    //are CommitAsync (which is gated by ThrowIfNotOpen) and the
    //session is owned by one logical caller, the CAS is in
    //practice non-racing — but the strict CAS is here as a
    //defence against future use patterns that share the session
    //across threads.
    private void TransitionToCommitted()
    {
        int previous = Interlocked.CompareExchange(ref state, StateCommitted, StateOpen);
        if(previous != StateOpen)
        {
            throw new InvalidOperationException("The edit session was disposed concurrently with the commit.");
        }
    }
}
