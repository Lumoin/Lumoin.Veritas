using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// Delegates the library uses to talk to a journal implementation.
/// Default implementations are provided by
/// <see cref="InMemoryJournal"/>; consumers wiring durable
/// journals (PostgreSQL, FastPaxos-backed, file-backed) implement
/// these delegates against their backend's append and read
/// primitives.
/// </summary>
public static class JournalDelegates
{
    /// <summary>
    /// Appends a journal entry under optimistic concurrency. The
    /// implementation must atomically observe the current head and
    /// reject the append when it does not equal
    /// <paramref name="expectedHead"/>; this is the linearisability
    /// contract the library relies on for cross-session
    /// serialisability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On success, the implementation assigns the entry a
    /// monotonic <see cref="JournalEntry.SequenceNumber"/>, stamps
    /// <see cref="JournalEntry.Timestamp"/> from the journal's
    /// own clock, makes the result durable per the
    /// implementation's durability semantics, updates the head to
    /// <see cref="JournalEntry.ChildId"/> of <paramref name="entry"/>,
    /// and returns the assigned sequence number to the caller.
    /// </para>
    /// <para>
    /// On parent-mismatch, the implementation throws
    /// <see cref="EditSessionConcurrencyException"/> with the
    /// expected and actual heads. The caller is expected to
    /// rebase against the new head and retry.
    /// </para>
    /// <para>
    /// <b>Journal-owned fields.</b>
    /// <see cref="JournalEntry.SequenceNumber"/> and
    /// <see cref="JournalEntry.Timestamp"/> are owned by the
    /// implementation and are overridden on every successful
    /// append. The journal is the single linearisation point for
    /// both order and time, so the timestamp sequence is
    /// monotonic with the sequence-number sequence even under
    /// concurrent appenders. Whatever value the caller put on
    /// these two fields is treated as a placeholder; it does not
    /// survive append.
    /// </para>
    /// <para>
    /// The implementation must not silently drop the append, must
    /// not mutate <paramref name="entry"/> beyond the
    /// sequence-number and timestamp assignments documented
    /// above, and must not emit the entry before its parent is
    /// the current head.
    /// </para>
    /// </remarks>
    /// <param name="entry">The entry to append. The caller fills every field except <see cref="JournalEntry.SequenceNumber"/> and <see cref="JournalEntry.Timestamp"/>, which the implementation overrides.</param>
    /// <param name="expectedHead">The current head the caller observed when forming this entry. The append succeeds when this equals the actual head; otherwise the implementation throws.</param>
    /// <param name="cancellationToken">A token that aborts the append.</param>
    /// <returns>The sequence number assigned to the appended entry.</returns>
    /// <exception cref="EditSessionConcurrencyException">The journal head no longer equals <paramref name="expectedHead"/>.</exception>
    public delegate ValueTask<long> AppendJournalEntryAsync(
        JournalEntry entry,
        NodeIdentifier expectedHead,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads journal entries starting from the given sequence
    /// number, in order. Implementations may stream entries and
    /// honour <paramref name="cancellationToken"/> per-element.
    /// </summary>
    /// <param name="fromSequenceNumber">The first sequence number to return. Pass <c>0</c> to read from the beginning.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>An async sequence of journal entries with sequence numbers at or above <paramref name="fromSequenceNumber"/>.</returns>
    public delegate IAsyncEnumerable<JournalEntry> ReadJournalEntriesAsync(
        long fromSequenceNumber,
        CancellationToken cancellationToken);
}
