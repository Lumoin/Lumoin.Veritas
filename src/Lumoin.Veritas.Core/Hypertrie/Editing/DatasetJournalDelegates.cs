using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Delegates the library uses to talk to a DATASET journal
/// implementation — the dataset-level instance of the journal
/// consensus seam. Default implementations are provided by
/// <see cref="InMemoryDatasetJournal"/>; consumers wiring durable
/// or replicated journals (PostgreSQL, FastPaxos-backed,
/// file-backed) implement these delegates against their backend's
/// append and read primitives, exactly as for the per-store
/// <see cref="JournalDelegates"/> pair.
/// </summary>
public static class DatasetJournalDelegates
{
    /// <summary>
    /// Appends a dataset journal entry under optimistic
    /// concurrency. The implementation must atomically observe the
    /// current head and reject the append when it does not equal
    /// <paramref name="expectedHead"/> — the linearisability
    /// contract that makes one entry's multi-graph transitions an
    /// atomic, totally-ordered dataset commit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On success, the implementation assigns the entry a monotonic
    /// <see cref="DatasetJournalEntry.SequenceNumber"/>, stamps
    /// <see cref="DatasetJournalEntry.Timestamp"/> from its own
    /// clock, makes the result durable per its durability
    /// semantics, updates the head to the entry's
    /// <see cref="DatasetJournalEntry.ChildId"/>, and returns the
    /// assigned sequence number.
    /// </para>
    /// <para>
    /// On head-mismatch, the implementation throws
    /// <see cref="EditSessionConcurrencyException"/> with the
    /// expected and actual heads; the caller rebases against the
    /// new head and retries.
    /// </para>
    /// <para>
    /// The implementation must not silently drop the append, must
    /// not mutate the entry beyond the two journal-owned fields,
    /// and must not emit the entry before its parent is the
    /// current head — with one exception. An
    /// <see cref="EditSessionEntryKind.Forked"/> entry's
    /// <see cref="DatasetJournalEntry.ParentId"/> names a state
    /// produced by a DIFFERENT journal (the cross-journal edge of
    /// the world DAG), so its validity condition is instead that
    /// the journal is unborn: the expected head — and the actual
    /// head — is <see cref="NodeIdentifier.Empty"/>, which no real
    /// dataset state identifier equals. The head-equals-expected
    /// check alone therefore enforces both rules; an implementation
    /// that additionally validates the parent must accept
    /// <c>ParentId == head</c> or (<c>EntryKind == Forked</c> and
    /// <c>head == NodeIdentifier.Empty</c>).
    /// </para>
    /// </remarks>
    /// <param name="entry">The entry to append. The caller fills every field except the journal-owned <see cref="DatasetJournalEntry.SequenceNumber"/> and <see cref="DatasetJournalEntry.Timestamp"/>.</param>
    /// <param name="expectedHead">The head the caller observed when forming the entry; the append succeeds only when it equals the actual head.</param>
    /// <param name="cancellationToken">A token that aborts the append.</param>
    /// <returns>The sequence number assigned to the appended entry.</returns>
    /// <exception cref="EditSessionConcurrencyException">The journal head no longer equals <paramref name="expectedHead"/>.</exception>
    public delegate ValueTask<long> AppendDatasetJournalEntryAsync(
        DatasetJournalEntry entry,
        NodeIdentifier expectedHead,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads dataset journal entries starting from the given
    /// sequence number, in order. Implementations may stream
    /// entries and honour <paramref name="cancellationToken"/>
    /// per element.
    /// </summary>
    /// <param name="fromSequenceNumber">The first sequence number to return. Pass <c>0</c> to read from the beginning.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>An async sequence of entries with sequence numbers at or above <paramref name="fromSequenceNumber"/>.</returns>
    public delegate IAsyncEnumerable<DatasetJournalEntry> ReadDatasetJournalEntriesAsync(
        long fromSequenceNumber,
        CancellationToken cancellationToken);
}
