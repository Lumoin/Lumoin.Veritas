using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// A durable, file-backed implementation of the <see cref="JournalDelegates"/> contract: every append
/// is written to an append-only log and flushed to stable storage before it is acknowledged, so a
/// committed transition survives a crash. It mirrors <see cref="InMemoryJournal"/> — same
/// optimistic-concurrency append, same journal-owned sequence and timestamp, same
/// <see cref="AppendDelegate"/> / <see cref="ReadDelegate"/> wiring — so it drops into a
/// <see cref="NodeStore"/> unchanged, adding durability.
/// </summary>
/// <remarks>
/// <para>
/// <b>Record framing and recovery.</b> Each entry is one length-prefixed, checksummed record
/// (<see cref="JournalRecordFormat"/>). On construction the log is replayed from the start: each record
/// is verified, and the first record that is short or fails its checksum is the recovery boundary — the
/// log is recovered through the operation before it and the torn tail is truncated, surfaced as an
/// <see cref="UnrecoverableItemReportKind.OperationRange"/> <see cref="RecoveryReport"/> rather than
/// thrown. A genuinely incompatible record (an unsupported payload version) is refused, not truncated,
/// so a newer log is never silently shortened. This advances
/// <see cref="PersistenceInvariant.LossIsNamed"/> at operation granularity.
/// </para>
/// <para>
/// <b>Durable write path.</b> Each record is written at the log's current end offset through
/// <see cref="RandomAccess"/> and flushed to stable storage through the injected
/// <see cref="DurableFlushDelegate"/> (defaulting to the F_FULLFSYNC-aware
/// <see cref="AtomicPublish.DefaultFlush"/>) before the in-memory state advances and the sequence
/// number is returned. Writing at an explicit,
/// only-advanced-on-success offset (rather than an OS append cursor over a buffered stream) means a
/// write that fails part-way leaves bytes the next attempt overwrites in place, never a duplicate
/// record that would brick replay. When the log file is first created its parent directory is flushed
/// through the injected <see cref="DurabilityBarrierDelegate"/>, so the new file's directory entry is
/// durable before the first append is acknowledged — without it a power loss on some file systems could
/// lose an acknowledged first commit. That barrier's reach is host-conditional: it flushes the parent
/// directory on Linux (including Android) and the Apple platforms and is a no-op on Windows (no public
/// directory-fsync API), so on Windows the first-append acknowledgement can precede the directory
/// entry's durability, while the flush that makes each record's bytes durable is a real device-cache
/// flush on every host (<c>FlushFileBuffers</c> on Windows, <c>fsync</c>/<c>F_FULLFSYNC</c> on the
/// others). See <see cref="AtomicPublish"/> for the per-host defaults. The journal is the single
/// linearisation point: a lock guards the
/// head check, the journal-owned sequence and timestamp assignment, the durable write, and the state
/// advance, so concurrent appenders serialise through it.
/// </para>
/// <para>
/// <b>Read mirror.</b> The durable file is the system of record; recovered entries are also held in
/// memory and reads are served from that mirror at <see cref="InMemoryJournal"/> parity. A file-streamed
/// read path (a sequence-to-offset index) and log compaction after a durable snapshot are named
/// follow-ons, not part of this tier.
/// </para>
/// <para>
/// Host-only: there is no file system to append into in a browser runtime, which wires a different
/// durable backend behind the same delegate contract.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("browser")]
public sealed class FileBackedJournal : IDisposable
{
    /// <summary>The recovered entries, index equal to sequence number; the in-memory read mirror of the durable log. Lock-guarded by <see cref="Mutex"/>.</summary>
    private List<JournalEntry> Entries { get; } = [];

    /// <summary>The commitment findings collected during replay; drained into <see cref="CommitmentFindings"/> once construction completes.</summary>
    private List<JournalCommitmentFinding> Findings { get; } = [];

    /// <summary>The lock guarding <see cref="Entries"/>, <see cref="head"/>, <see cref="writeOffset"/>, <see cref="disposed"/>, and the write handle.</summary>
    private Lock Mutex { get; } = new();

    /// <summary>The hash function replay recomputes edit commitments under, or <see langword="null"/> when the journal was opened without commitment verification. When present it MUST be the same hash function the originating node store carries.</summary>
    private VeritasHash? Hash { get; }

    /// <summary>The clock read once per append, inside the lock, so the timestamp sequence is monotonic with the sequence-number sequence.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The checksum algorithm each record is framed under.</summary>
    private ChecksumAlgorithm Checksum { get; }

    /// <summary>The pool the per-append serialization buffer is rented from.</summary>
    private MemoryPool<byte> BufferPool { get; }

    /// <summary>The file-content durability flush applied after each durable write; injected so a consumer can wire a platform-specific flush and a test can substitute it.</summary>
    private DurableFlushDelegate DurableFlush { get; }

    /// <summary>The write handle into the durable log; records are written at <see cref="writeOffset"/> through <see cref="RandomAccess"/>.</summary>
    private SafeFileHandle Handle { get; }

    /// <summary>The byte offset the next record is written at — the recovered intact length, advanced only after a record is durably written.</summary>
    private long writeOffset;

    /// <summary>The current head identifier; reassigned on every append, so a field rather than a property.</summary>
    private NodeIdentifier head = NodeIdentifier.Empty;

    /// <summary>Whether the journal has been disposed; reassigned, so a field.</summary>
    private bool disposed;

    /// <summary>Opens a durable journal over <paramref name="filePath"/>, replaying and recovering an existing log, with an explicit directory durability barrier, file-content flush, and optional replay-time commitment verification.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under; the corruption detector the torn-tail recovery relies on.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="JournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <param name="flush">The file-content durability flush applied after each durable write; pass <see cref="AtomicPublish.DefaultFlush"/> in production.</param>
    /// <param name="hash">The hash function replay recomputes edit commitments under, or <see langword="null"/> to skip verification. When provided it MUST be the same hash function the originating node store carries; a mismatched entry surfaces as a <see cref="CommitmentFindings"/> finding, not a refusal.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedJournal(string filePath, ChecksumAlgorithm checksum, TimeProvider timeProvider, MemoryPool<byte> bufferPool, DurabilityBarrierDelegate barrier, DurableFlushDelegate flush, VeritasHash? hash)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(bufferPool);
        ArgumentNullException.ThrowIfNull(barrier);
        ArgumentNullException.ThrowIfNull(flush);

        Checksum = checksum;
        TimeProvider = timeProvider;
        BufferPool = bufferPool;
        DurableFlush = flush;
        Hash = hash;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if(!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool fileExisted = File.Exists(filePath);
        RecoveryReport = Replay(filePath);
        CommitmentFindings = [.. Findings];
        Handle = File.OpenHandle(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

        //Drop any torn or corrupt tail physically so the next append starts at the recovered intact length.
        if(RandomAccess.GetLength(Handle) > writeOffset)
        {
            RandomAccess.SetLength(Handle, writeOffset);
            DurableFlush(Handle);
        }

        //Make a freshly-created log file's directory entry durable before the first append is acknowledged.
        if(!fileExisted && !string.IsNullOrEmpty(directory))
        {
            barrier(directory);
        }
    }

    /// <summary>Opens a durable journal over <paramref name="filePath"/> with the given directory barrier and file-content flush, without replay-time commitment verification.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under; the corruption detector the torn-tail recovery relies on.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="JournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <param name="flush">The file-content durability flush applied after each durable write; pass <see cref="AtomicPublish.DefaultFlush"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedJournal(string filePath, ChecksumAlgorithm checksum, TimeProvider timeProvider, MemoryPool<byte> bufferPool, DurabilityBarrierDelegate barrier, DurableFlushDelegate flush)
        : this(filePath, checksum, timeProvider, bufferPool, barrier, flush, hash: null)
    {
    }

    /// <summary>Opens a durable journal over <paramref name="filePath"/> with the given directory barrier and the production file-content flush.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="JournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedJournal(string filePath, ChecksumAlgorithm checksum, TimeProvider timeProvider, MemoryPool<byte> bufferPool, DurabilityBarrierDelegate barrier)
        : this(filePath, checksum, timeProvider, bufferPool, barrier, AtomicPublish.DefaultFlush, hash: null)
    {
    }

    /// <summary>Opens a durable journal over <paramref name="filePath"/> with the production durability seams.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="JournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedJournal(string filePath, ChecksumAlgorithm checksum, TimeProvider timeProvider, MemoryPool<byte> bufferPool)
        : this(filePath, checksum, timeProvider, bufferPool, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush)
    {
    }

    /// <summary>The named loss recovered on construction — an <see cref="UnrecoverableItemReportKind.OperationRange"/> report when a torn or corrupt tail was truncated, or <see langword="null"/> when the log replayed intact.</summary>
    public UnrecoverableItemReport? RecoveryReport { get; }

    /// <summary>The commitment findings collected during replay when the journal was opened with a hash: one per entry whose stored fingerprint disagreed with a recomputation over its own parent and edits. Always empty when the journal was opened without a hash (verification skipped). A finding is corruption evidence, not a refusal — replay continues past it.</summary>
    public ImmutableArray<JournalCommitmentFinding> CommitmentFindings { get; }

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

    /// <summary>The append delegate this journal exposes for wiring into <see cref="NodeStore"/> construction, enforcing the linear-log optimistic-concurrency contract durably.</summary>
    public JournalDelegates.AppendJournalEntryAsync AppendDelegate => AppendAsync;

    /// <summary>The read delegate this journal exposes for wiring into <see cref="NodeStore"/> construction, streaming entries at or above a sequence lower bound from the in-memory mirror.</summary>
    public JournalDelegates.ReadJournalEntriesAsync ReadDelegate => ReadAsync;

    /// <summary>Replays the log file, recovering the intact prefix and recording where a torn or corrupt tail begins; the physical truncation is applied by the constructor through the write handle.</summary>
    /// <param name="filePath">The log file.</param>
    /// <returns>An operation-range report when a tail is to be truncated, or <see langword="null"/> when the log replayed intact.</returns>
    private UnrecoverableItemReport? Replay(string filePath)
    {
        if(!File.Exists(filePath))
        {
            writeOffset = 0;

            return null;
        }

        byte[] bytes = File.ReadAllBytes(filePath);
        int offset = 0;
        long recoveredThroughSequence = -1;
        while(JournalRecordFormat.TryReadRecord(bytes.AsSpan(offset), Checksum, out JournalEntry entry, out int recordLength))
        {
            if(entry.SequenceNumber != Entries.Count)
            {
                throw new InvalidDataException($"A journal record at sequence position {Entries.Count} carries sequence number {entry.SequenceNumber}.");
            }

            //When a hash was supplied, recompute each committing entry's fingerprint from its own contents and
            //surface a disagreement as a finding: the record's bytes are checksum-valid, so a mismatch is not
            //at-rest corruption of the record but evidence that the stored fingerprint and edits are inconsistent.
            if(Hash is not null && entry.EditCommitment.HasValue)
            {
                NodeIdentifier recomputed = EditCommitmentHashing.Compute(Hash, entry.ParentId, entry.Additions, entry.Removals);
                if(recomputed != entry.EditCommitment.Value)
                {
                    Findings.Add(new JournalCommitmentFinding(entry.SequenceNumber, entry.EditCommitment.Value, recomputed));
                }
            }

            Entries.Add(entry);
            head = entry.ChildId;
            recoveredThroughSequence = entry.SequenceNumber;
            offset += recordLength;
        }

        writeOffset = offset;
        if(offset == bytes.Length)
        {
            return null;
        }

        return UnrecoverableItemReport.OperationRange(recoveredThroughSequence, bytes.Length - offset);
    }

    /// <summary>Appends an entry durably under optimistic concurrency: the record is flushed to disk before the in-memory state advances.</summary>
    /// <param name="entry">The entry to append; its sequence number and timestamp are overwritten by the journal.</param>
    /// <param name="expectedHead">The head the caller observed; the append succeeds only when it still equals the actual head.</param>
    /// <param name="cancellationToken">A token that aborts the append before any work.</param>
    /// <returns>The assigned sequence number.</returns>
    /// <exception cref="EditSessionConcurrencyException">The head no longer equals <paramref name="expectedHead"/>.</exception>
    /// <exception cref="ObjectDisposedException">The journal has been disposed.</exception>
    private ValueTask<long> AppendAsync(JournalEntry entry, NodeIdentifier expectedHead, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long assignedSequence;

        lock(Mutex)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if(head != expectedHead)
            {
                throw new EditSessionConcurrencyException(expectedHead, head);
            }

            assignedSequence = Entries.Count;
            //SequenceNumber and Timestamp are journal-owned; the clock is read inside the lock so the
            //timestamp sequence is monotonic with the sequence-number sequence even under concurrent append.
            JournalEntry stored = entry with
            {
                SequenceNumber = assignedSequence,
                Timestamp = TimeProvider.GetUtcNow(),
            };

            //Durable first, then advance the in-memory state: if the durable write throws, neither the
            //write offset nor the in-memory state advances, so a retry overwrites the failed bytes in place.
            WriteRecordDurably(stored);

            Entries.Add(stored);
            head = stored.ChildId;
        }

        return ValueTask.FromResult(assignedSequence);
    }

    /// <summary>Serializes one record into a pooled buffer, writes it at the log's end offset, flushes it to stable storage, then advances the offset — the synchronous durability barrier the append is built on.</summary>
    /// <param name="entry">The journal-stamped entry to write.</param>
    private void WriteRecordDurably(in JournalEntry entry)
    {
        int size = JournalRecordFormat.ComputeRecordSize(entry, Checksum);
        using IMemoryOwner<byte> owner = BufferPool.Rent(size);
        Span<byte> buffer = owner.Memory.Span[..size];
        JournalRecordFormat.WriteRecord(buffer, entry, Checksum);

        RandomAccess.Write(Handle, buffer, writeOffset);
        DurableFlush(Handle);
        writeOffset += size;
    }

    [SuppressMessage(
        "Style",
        "CS1998:Async method lacks await operators",
        Justification = "Yield-only async iterator. The async modifier is required for the IAsyncEnumerable return type and the EnumeratorCancellation attribute, but the iterator emits an in-memory snapshot and has no asynchronous work of its own.")]
    private async IAsyncEnumerable<JournalEntry> ReadAsync(
        long fromSequenceNumber,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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

    /// <summary>Closes the write handle. Outstanding reads over a captured snapshot remain valid; there is no buffered data to flush, since each record is written unbuffered through <see cref="RandomAccess"/>.</summary>
    public void Dispose()
    {
        lock(Mutex)
        {
            if(disposed)
            {
                return;
            }

            disposed = true;
            Handle.Dispose();
        }
    }
}
