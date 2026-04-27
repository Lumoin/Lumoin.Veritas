using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// A default in-process journal implementation. Stores entries in
/// a lock-guarded list and maintains the current head identifier
/// for optimistic-concurrency append. Suitable for tests, for
/// in-memory single-process workloads, and as a reference for
/// what the
/// <see cref="JournalDelegates.AppendJournalEntryAsync"/> contract
/// requires of a durable backend.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency.</b> Append acquires the internal lock, checks
/// the head against <c>expectedHead</c> while holding the lock,
/// and only inside the lock either appends and updates the head
/// or throws. Readers can take the lock, snapshot the entry
/// list, and stream the snapshot outside the lock; writers do
/// not block on long-running reads.
/// </para>
/// <para>
/// <b>Journal-owned fields.</b> The journal is the linearisation
/// point for entry order. Two fields on <see cref="JournalEntry"/>
/// are owned by the journal at this point and overwritten on
/// append: <see cref="JournalEntry.SequenceNumber"/> is assigned
/// from the entry's index in the log (the first appended entry
/// receives <c>0</c>, the next <c>1</c>, and so on; a journal
/// that has never been appended to has length zero), and
/// <see cref="JournalEntry.Timestamp"/> is read from the
/// journal's <see cref="TimeProvider"/> at the moment the entry
/// is committed. Whatever values the caller put on these two
/// fields are placeholders; they do not survive append. Stamping
/// at the linearisation point gives a total order on
/// <c>Timestamp</c> consistent with <c>SequenceNumber</c> and
/// removes any dependency on caller clock skew.
/// </para>
/// </remarks>
[DebuggerDisplay("InMemoryJournal Length={Length} Head={head.Value:X16}")]
public sealed class InMemoryJournal
{
    //The list of appended entries; index equals SequenceNumber.
    //Lock-guarded against the journal's own internal lock.
    private List<JournalEntry> Entries { get; } = [];

    //Lock for both Entries and head. Property-shaped so the wider
    //"properties over fields" rule applies; the auto-generated
    //backing field is the lock target.
    private Lock Mutex { get; } = new();

    private NodeIdentifier head = NodeIdentifier.Empty;

    //The clock used to stamp Timestamp on every appended entry.
    //Injected at construction so tests can pin time deterministically
    //via FakeTimeProvider; production callers receive
    //TimeProvider.System through the parameterless constructor.
    private TimeProvider TimeProvider { get; }

    /// <summary>
    /// Constructs a journal that stamps <see cref="JournalEntry.Timestamp"/>
    /// from <see cref="TimeProvider.System"/>. Suitable for production
    /// callers; tests that need to pin time should use
    /// <see cref="InMemoryJournal(TimeProvider)"/> with a
    /// <c>FakeTimeProvider</c>.
    /// </summary>
    public InMemoryJournal()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Constructs a journal that stamps <see cref="JournalEntry.Timestamp"/>
    /// from the supplied <paramref name="timeProvider"/>. The clock
    /// is read once per append, inside the journal's mutex so
    /// concurrent appenders see a monotonic Timestamp sequence
    /// matching the SequenceNumber sequence.
    /// </summary>
    /// <param name="timeProvider">The clock to read Timestamp from on append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <c>null</c>.</exception>
    public InMemoryJournal(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
    }

    /// <summary>The current head identifier — the <see cref="JournalEntry.ChildId"/> of the most recent entry, or <see cref="NodeIdentifier.Empty"/> when the journal is empty.</summary>
    public NodeIdentifier Head
    {
        get
        {
            lock(Mutex)
            {
                return head;
            }
        }
    }

    /// <summary>The number of entries currently in the journal.</summary>
    public int Length
    {
        get
        {
            lock(Mutex)
            {
                return Entries.Count;
            }
        }
    }

    /// <summary>
    /// The append delegate this journal exposes for wiring into
    /// <see cref="NodeStore"/> construction. Each call enforces the
    /// linear-log OCC contract documented on
    /// <see cref="JournalDelegates.AppendJournalEntryAsync"/>.
    /// </summary>
    public JournalDelegates.AppendJournalEntryAsync AppendDelegate => AppendAsync;

    /// <summary>
    /// The read delegate this journal exposes for wiring into
    /// <see cref="NodeStore"/> construction. Streams entries with
    /// sequence numbers at or above the supplied lower bound.
    /// </summary>
    public JournalDelegates.ReadJournalEntriesAsync ReadDelegate => ReadAsync;

    private ValueTask<long> AppendAsync(JournalEntry entry, NodeIdentifier expectedHead, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long assignedSequence;

        lock(Mutex)
        {
            if(head != expectedHead)
            {
                throw new EditSessionConcurrencyException(expectedHead, head);
            }

            assignedSequence = Entries.Count;
            //SequenceNumber and Timestamp are both journal-owned;
            //read the clock inside the lock so the timestamp
            //sequence is monotonic with the sequence-number
            //sequence even under concurrent append.
            JournalEntry stored = entry with
            {
                SequenceNumber = assignedSequence,
                Timestamp = TimeProvider.GetUtcNow(),
            };
            Entries.Add(stored);
            head = stored.ChildId;
        }

        return ValueTask.FromResult(assignedSequence);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "CS1998:Async method lacks await operators",
        Justification = "Yield-only async iterator. The async modifier is required for the IAsyncEnumerable return type and the EnumeratorCancellation attribute, but the iterator emits an in-memory snapshot and has no asynchronous work of its own. Adding an artificial await would change the observable timing without serving any purpose.")]
    private async IAsyncEnumerable<JournalEntry> ReadAsync(
        long fromSequenceNumber,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Snapshot the entries under the lock so the iterator
        //emits a consistent view even under concurrent appends.
        //Yield outside the lock so the iterator does not hold the
        //mutex across consumer awaits.
        JournalEntry[] snapshot;
        lock(Mutex)
        {
            snapshot = [.. Entries];
        }

        for(int i = 0; i < snapshot.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JournalEntry entry = snapshot[i];
            if(entry.SequenceNumber >= fromSequenceNumber)
            {
                yield return entry;
            }
        }
    }
}
