using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Core.Hypertrie.Editing;

/// <summary>
/// A default in-process DATASET journal implementation. Stores
/// entries in a lock-guarded list and maintains the current head
/// identifier for optimistic-concurrency append. Suitable for
/// tests, for in-memory single-process workloads, and as a
/// reference for what the
/// <see cref="DatasetJournalDelegates.AppendDatasetJournalEntryAsync"/>
/// contract requires of a durable or replicated backend.
/// </summary>
/// <remarks>
/// <para>
/// <b>Concurrency.</b> Append acquires the internal lock, checks
/// the head against <c>expectedHead</c> while holding the lock,
/// and only inside the lock either appends and updates the head or
/// throws. Readers take the lock, snapshot the entry list, and
/// stream the snapshot outside the lock; writers do not block on
/// long-running reads.
/// </para>
/// <para>
/// <b>Journal-owned fields.</b> The journal is the linearisation
/// point for entry order:
/// <see cref="DatasetJournalEntry.SequenceNumber"/> is assigned
/// from the entry's index in the log and
/// <see cref="DatasetJournalEntry.Timestamp"/> is read from the
/// journal's <see cref="TimeProvider"/> inside the lock, so the
/// timestamp sequence is monotonic with the sequence-number
/// sequence even under concurrent appenders.
/// </para>
/// </remarks>
[DebuggerDisplay("InMemoryDatasetJournal Length={Length} Head={head.Value:X16}")]
public sealed class InMemoryDatasetJournal
{
    //The list of appended entries; index equals SequenceNumber.
    //Lock-guarded against the journal's own internal lock.
    private List<DatasetJournalEntry> Entries { get; } = [];

    //Lock for both Entries and head. Property-shaped so the wider
    //"properties over fields" rule applies; the auto-generated
    //backing field is the lock target.
    private Lock Mutex { get; } = new();

    private NodeIdentifier head = NodeIdentifier.Empty;

    //The clock used to stamp Timestamp on every appended entry.
    //Injected at construction so tests can pin time
    //deterministically; production callers receive
    //TimeProvider.System through the parameterless constructor.
    private TimeProvider TimeProvider { get; }

    /// <summary>
    /// Constructs a journal that stamps
    /// <see cref="DatasetJournalEntry.Timestamp"/> from
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    public InMemoryDatasetJournal()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Constructs a journal that stamps
    /// <see cref="DatasetJournalEntry.Timestamp"/> from the
    /// supplied <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">The clock to read Timestamp from on append.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <c>null</c>.</exception>
    public InMemoryDatasetJournal(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
    }

    /// <summary>The current head identifier — the <see cref="DatasetJournalEntry.ChildId"/> of the most recent entry, or <see cref="NodeIdentifier.Empty"/> when the journal is empty.</summary>
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
    /// The append delegate this journal exposes. Each call enforces
    /// the linear-log OCC contract documented on
    /// <see cref="DatasetJournalDelegates.AppendDatasetJournalEntryAsync"/>.
    /// </summary>
    public DatasetJournalDelegates.AppendDatasetJournalEntryAsync AppendDelegate => AppendAsync;

    /// <summary>
    /// The read delegate this journal exposes. Streams entries with
    /// sequence numbers at or above the supplied lower bound.
    /// </summary>
    public DatasetJournalDelegates.ReadDatasetJournalEntriesAsync ReadDelegate => ReadAsync;

    /// <summary>Appends an entry under the OCC head check, stamping the journal-owned fields.</summary>
    /// <param name="entry">The entry to append.</param>
    /// <param name="expectedHead">The head the caller observed.</param>
    /// <param name="cancellationToken">A token that aborts the append.</param>
    /// <returns>The assigned sequence number.</returns>
    private ValueTask<long> AppendAsync(DatasetJournalEntry entry, NodeIdentifier expectedHead, CancellationToken cancellationToken)
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
            DatasetJournalEntry stored = entry with
            {
                SequenceNumber = assignedSequence,
                Timestamp = TimeProvider.GetUtcNow(),
            };
            Entries.Add(stored);
            head = stored.ChildId;
        }

        return ValueTask.FromResult(assignedSequence);
    }

    /// <summary>Streams a consistent snapshot of the entries at or above the given sequence number.</summary>
    /// <param name="fromSequenceNumber">The first sequence number to return.</param>
    /// <param name="cancellationToken">A token that aborts the read.</param>
    /// <returns>The entry stream.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Style",
        "CS1998:Async method lacks await operators",
        Justification = "Yield-only async iterator. The async modifier is required for the IAsyncEnumerable return type and the EnumeratorCancellation attribute, but the iterator emits an in-memory snapshot and has no asynchronous work of its own.")]
    private async IAsyncEnumerable<DatasetJournalEntry> ReadAsync(
        long fromSequenceNumber,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Snapshot the entries under the lock so the iterator emits
        //a consistent view even under concurrent appends; yield
        //outside the lock so the iterator does not hold the mutex
        //across consumer awaits.
        DatasetJournalEntry[] snapshot;
        lock(Mutex)
        {
            snapshot = [.. Entries];
        }

        for(int i = 0; i < snapshot.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DatasetJournalEntry entry = snapshot[i];
            if(entry.SequenceNumber >= fromSequenceNumber)
            {
                yield return entry;
            }
        }
    }
}
