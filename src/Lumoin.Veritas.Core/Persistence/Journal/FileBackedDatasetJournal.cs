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
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Core.Persistence.Journal;

/// <summary>
/// A durable, file-backed implementation of the <see cref="DatasetJournalDelegates"/> contract: every append
/// is written to an append-only log and flushed to stable storage before it is acknowledged, so a committed
/// dataset transition survives a crash. It mirrors <see cref="InMemoryDatasetJournal"/> — same
/// optimistic-concurrency append, same journal-owned sequence and timestamp, same
/// <see cref="AppendDelegate"/> / <see cref="ReadDelegate"/> wiring — and mirrors the per-store
/// <see cref="FileBackedJournal"/>'s framing, recovery, and durability discipline, adding one thing the
/// dataset level needs: term durability.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the term section exists.</b> A journal entry carries <see cref="EncodedTriple"/> term IDENTIFIERS
/// only; the identifier-to-term mapping for terms minted after the last persisted generation lives only in
/// the in-memory dictionary, so a crash would otherwise leave replayed triples with identifiers the
/// dictionary cannot resolve. Each record therefore carries every dictionary term minted since the previous
/// durable record, making the log self-contained: the log alone — or the log replayed over an
/// already-loaded generation — reconstructs a fully resolvable dataset. Term dictionary identifiers are dense
/// and sequential (1-based), which is what makes a watermark-delimited range sound: the range
/// <c>(watermark, count]</c> names exactly the identifiers minted since the previous record.
/// </para>
/// <para>
/// <b>Record framing and recovery.</b> Each entry is one length-prefixed, checksummed record
/// (<see cref="DatasetJournalRecordFormat"/>). On construction the log is replayed from the start: each
/// record is verified, and the first record that is short or fails its checksum is the recovery boundary —
/// the log is recovered through the operation before it and the torn tail is truncated, surfaced as an
/// <see cref="UnrecoverableItemReportKind.OperationRange"/> <see cref="RecoveryReport"/> rather than thrown.
/// An incompatible record (an unsupported payload version) is refused, not truncated. Replay also restores
/// the dictionary from each record's term section and verifies it against the live dictionary: a term whose
/// identifier already denotes a DIFFERENT term is a hard refusal — the log belongs to a different history —
/// while a fingerprint disagreement between an entry's stored commitment and a recomputation over its own
/// contents is a <see cref="CommitmentFindings"/> finding, since the record's bytes are checksum-valid and
/// only the fingerprint and the edits disagree.
/// </para>
/// <para>
/// <b>Durable write path.</b> Inside one lock the append checks the head, stamps the journal-owned sequence
/// and timestamp, captures the dictionary's newly-minted term range atomically against concurrent mints,
/// writes the record at the log's current end offset through <see cref="RandomAccess"/>, flushes it through
/// the injected <see cref="DurableFlushDelegate"/>, and only then advances the write offset, the durable term
/// watermark, the in-memory mirror, and the head. A write that fails part-way advances none of them, so a
/// retry overwrites the failed bytes in place. When the log file is first created its parent directory is
/// flushed through the injected <see cref="DurabilityBarrierDelegate"/>. See <see cref="AtomicPublish"/> for
/// the per-host durability defaults, which match the per-store journal exactly.
/// </para>
/// <para>
/// Host-only: there is no file system to append into in a browser runtime, which wires a different durable
/// backend behind the same delegate contract.
/// </para>
/// </remarks>
[UnsupportedOSPlatform("browser")]
public sealed class FileBackedDatasetJournal : IDisposable
{
    /// <summary>The recovered entries, index equal to sequence number; the in-memory read mirror of the durable log. Lock-guarded by <see cref="Mutex"/>.</summary>
    private List<DatasetJournalEntry> Entries { get; } = [];

    /// <summary>The commitment findings collected during replay; drained into <see cref="CommitmentFindings"/> once construction completes.</summary>
    private List<JournalCommitmentFinding> Findings { get; } = [];

    /// <summary>The lock guarding <see cref="Entries"/>, <see cref="head"/>, <see cref="writeOffset"/>, <see cref="durableTermWatermark"/>, <see cref="disposed"/>, and the write handle.</summary>
    private Lock Mutex { get; } = new();

    /// <summary>The dictionary the term section is captured from on append and verified and restored into on replay; shared with the dataset whose transitions this journal records.</summary>
    private TermDictionary Dictionary { get; }

    /// <summary>The pool the decoded term bytes are interned into on replay; it must outlive the dictionary, which holds views over its memory.</summary>
    private Utf8StringPool TermPool { get; }

    /// <summary>The hash function commitment verification recomputes under; the same hash the dataset's arena carries.</summary>
    private VeritasHash Hash { get; }

    /// <summary>The clock read once per append, inside the lock, so the timestamp sequence is monotonic with the sequence-number sequence.</summary>
    private TimeProvider TimeProvider { get; }

    /// <summary>The checksum algorithm each record is framed under.</summary>
    private ChecksumAlgorithm Checksum { get; }

    /// <summary>Resolves the record-stream checksum algorithm id a v2 header records, so an unreadable stream is refused at open rather than truncated; <see cref="ChecksumAlgorithm.DefaultResolver"/> when none was injected.</summary>
    private ResolveChecksumAlgorithmDelegate ResolveChecksum { get; }

    /// <summary>The pool the per-append serialization buffer is rented from.</summary>
    private MemoryPool<byte> BufferPool { get; }

    /// <summary>The file-content durability flush applied after each durable write; injected so a consumer can wire a platform-specific flush and a test can substitute it.</summary>
    private DurableFlushDelegate DurableFlush { get; }

    /// <summary>The write handle into the durable log; records are written at <see cref="writeOffset"/> through <see cref="RandomAccess"/>.</summary>
    private SafeFileHandle Handle { get; }

    /// <summary>The byte offset the next record is written at — the recovered intact length, advanced only after a record is durably written.</summary>
    private long writeOffset;

    /// <summary>The dictionary count captured by the last durable record; the next append's term section covers <c>(durableTermWatermark, count]</c>, and it advances only after a record's flush succeeds, so a field rather than a property.</summary>
    private int durableTermWatermark;

    /// <summary>The current head identifier; reassigned on every append, so a field rather than a property.</summary>
    private NodeIdentifier head = NodeIdentifier.Empty;

    /// <summary>Whether the journal has been disposed; reassigned, so a field.</summary>
    private bool disposed;

    /// <summary>Opens a durable dataset journal over <paramref name="filePath"/>, replaying and recovering an existing log, with an explicit directory durability barrier and file-content flush.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="dictionary">The dictionary the term section is captured from on append and verified and restored into on replay; the same dictionary the recorded dataset's transitions encode against.</param>
    /// <param name="termPool">The pool the decoded term bytes are interned into on replay; it must outlive <paramref name="dictionary"/>.</param>
    /// <param name="hash">The hash function commitment verification recomputes under. It MUST be the same hash function the dataset's arena carries (<see cref="VeritasHashing.Default"/> on every engine path); a different hash would make every checksum-valid entry appear as a spurious commitment finding.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under; the corruption detector the torn-tail recovery relies on.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="DatasetJournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <param name="flush">The file-content durability flush applied after each durable write; pass <see cref="AtomicPublish.DefaultFlush"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload, or its term section denotes a term the dictionary already binds to a different identifier.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedDatasetJournal(
        string filePath,
        TermDictionary dictionary,
        Utf8StringPool termPool,
        VeritasHash hash,
        ChecksumAlgorithm checksum,
        TimeProvider timeProvider,
        MemoryPool<byte> bufferPool,
        DurabilityBarrierDelegate barrier,
        DurableFlushDelegate flush)
        : this(filePath, dictionary, termPool, hash, checksum, timeProvider, bufferPool, barrier, flush, writeV2HeaderOnFresh: false, NodeIdentifier.Empty, attachTermWatermark: 0, resolveChecksum: null)
    {
    }

    /// <summary>The single header-owning constructor: replays and recovers an existing log (v1 or v2), or, on a fresh file, either leaves it headerless (v1) or writes a v2 header durably before the first append can be acknowledged.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="dictionary">The dictionary the term section is captured from and restored into; its <see cref="TermDictionary.Epoch"/> is stamped into a freshly-written v2 header.</param>
    /// <param name="termPool">The pool the decoded term bytes are interned into on replay.</param>
    /// <param name="hash">The hash function commitment verification recomputes under.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under; its id is recorded in a freshly-written v2 header.</param>
    /// <param name="timeProvider">The clock the journal stamps entries from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created.</param>
    /// <param name="flush">The file-content durability flush applied after each durable write.</param>
    /// <param name="writeV2HeaderOnFresh">When <see langword="true"/> and the file is fresh (length 0), a v2 header is written durably; when <see langword="false"/>, a fresh file is left headerless (v1). The read path handles both versions regardless.</param>
    /// <param name="headerAnchor">The onboarding anchor stamped into a freshly-written v2 header (ignored on an existing file), or <see cref="NodeIdentifier.Empty"/> for a self-contained create-path log.</param>
    /// <param name="attachTermWatermark">The attach term watermark stamped into a freshly-written v2 header (ignored on an existing file); the exclusive lower bound the log's term-watermark chain starts from.</param>
    /// <param name="resolveChecksum">Resolves the record-stream checksum algorithm id a v2 header records; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload or diverges from the dictionary, or an existing v2 header is corrupt.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, an existing v2 header uses an unsupported major version or an unresolvable record-stream algorithm id, or the host is big-endian.</exception>
    private FileBackedDatasetJournal(
        string filePath,
        TermDictionary dictionary,
        Utf8StringPool termPool,
        VeritasHash hash,
        ChecksumAlgorithm checksum,
        TimeProvider timeProvider,
        MemoryPool<byte> bufferPool,
        DurabilityBarrierDelegate barrier,
        DurableFlushDelegate flush,
        bool writeV2HeaderOnFresh,
        NodeIdentifier headerAnchor,
        int attachTermWatermark,
        ResolveChecksumAlgorithmDelegate? resolveChecksum)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(termPool);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(bufferPool);
        ArgumentNullException.ThrowIfNull(barrier);
        ArgumentNullException.ThrowIfNull(flush);
        ArgumentOutOfRangeException.ThrowIfNegative(attachTermWatermark);

        Dictionary = dictionary;
        TermPool = termPool;
        Hash = hash;
        Checksum = checksum;
        TimeProvider = timeProvider;
        BufferPool = bufferPool;
        DurableFlush = flush;
        ResolveChecksum = resolveChecksum ?? ChecksumAlgorithm.DefaultResolver;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if(!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool fileExisted = File.Exists(filePath);
        RecoveryReport = Replay(filePath, writeV2HeaderOnFresh, headerAnchor, attachTermWatermark, out byte[]? pendingHeaderBytes);
        CommitmentFindings = [.. Findings];
        Handle = File.OpenHandle(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

        if(pendingHeaderBytes is not null)
        {
            //A fresh v2 log: write the header durably before any append can be acknowledged, so an acked record
            //always sits behind a durable header. The write offset already points past the header.
            RandomAccess.Write(Handle, pendingHeaderBytes, 0);
            DurableFlush(Handle);
        }
        else if(RandomAccess.GetLength(Handle) > writeOffset)
        {
            //Drop any torn or corrupt tail physically so the next append starts at the recovered intact length. The
            //recovered length is header-offset-based, so a v2 log never truncates into its header.
            RandomAccess.SetLength(Handle, writeOffset);
            DurableFlush(Handle);
        }

        //Make a freshly-created log file's directory entry durable before the first append is acknowledged.
        if(!fileExisted && !string.IsNullOrEmpty(directory))
        {
            barrier(directory);
        }
    }

    /// <summary>Opens a durable dataset journal that writes a dataset-journal format v2 header on a fresh file: an attached log continues from the persisted <paramref name="anchor"/> state, and its term-watermark chain starts at <paramref name="attachTermWatermark"/> so the first attached append re-captures only the terms minted after attachment. On an existing file the header is read from disk and these creation parameters are ignored.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="dictionary">The dictionary the term section is captured from and restored into; its <see cref="TermDictionary.Epoch"/> is stamped into a freshly-written header.</param>
    /// <param name="termPool">The pool the decoded term bytes are interned into on replay.</param>
    /// <param name="hash">The hash function commitment verification recomputes under; the same hash the dataset's arena carries.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under; its id is recorded in the header.</param>
    /// <param name="timeProvider">The clock the journal stamps entries from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created.</param>
    /// <param name="flush">The file-content durability flush applied after each durable write.</param>
    /// <param name="anchor">The onboarding anchor for an attached log (the persisted state it continues from), or <see cref="NodeIdentifier.Empty"/> for a self-contained create-path log.</param>
    /// <param name="attachTermWatermark">The dictionary term count at file creation — the loaded generation's term count for an attached log, or 0 for a create-path log.</param>
    /// <param name="resolveChecksum">Resolves the record-stream checksum algorithm id a v2 header records; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The opened journal.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attachTermWatermark"/> is negative.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload or diverges from the dictionary, or an existing v2 header is corrupt.</exception>
    /// <exception cref="NotSupportedException">An existing v2 header uses an unsupported major version or an unresolvable record-stream algorithm id, or the host is big-endian.</exception>
    public static FileBackedDatasetJournal OpenV2(
        string filePath,
        TermDictionary dictionary,
        Utf8StringPool termPool,
        VeritasHash hash,
        ChecksumAlgorithm checksum,
        TimeProvider timeProvider,
        MemoryPool<byte> bufferPool,
        DurabilityBarrierDelegate barrier,
        DurableFlushDelegate flush,
        NodeIdentifier anchor,
        int attachTermWatermark,
        ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        return new FileBackedDatasetJournal(filePath, dictionary, termPool, hash, checksum, timeProvider, bufferPool, barrier, flush, writeV2HeaderOnFresh: true, anchor, attachTermWatermark, resolveChecksum);
    }

    /// <summary>Reads a dataset-journal file's v2 header replication epoch without opening the log, so a journal-only reopen can construct the dictionary carrying the stamped epoch rather than minting a fresh one. The read is bounded to the header bytes — the preamble first, then exactly the declared remainder — never the record stream, so the cost is fixed however large the log has grown.</summary>
    /// <param name="filePath">The log file.</param>
    /// <param name="resolveChecksum">Resolves the record-stream checksum algorithm id the header records; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <returns>The header's replication epoch when the file is a v2 log; <see langword="null"/> when the file is absent, empty, or a headerless v1 log.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The file carries the v2 discriminator but its header is corrupt or torn.</exception>
    /// <exception cref="NotSupportedException">The v2 header uses an unsupported major version or an unresolvable record-stream algorithm id, or the host is big-endian.</exception>
    public static ulong? TryReadReplicationEpoch(string filePath, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if(!File.Exists(filePath))
        {
            return null;
        }

        using SafeFileHandle handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] preamble = new byte[DatasetJournalHeader.PreambleSize];
        int preambleRead = ReadAvailable(handle, preamble, 0);
        if(!DatasetJournalHeader.LooksLikeV2(preamble.AsSpan(0, preambleRead)))
        {
            return null;
        }

        if(preambleRead < DatasetJournalHeader.PreambleSize)
        {
            //The discriminator is present but the preamble is not whole — a torn header; Read names the refusal.
            return DatasetJournalHeader.Read(preamble.AsSpan(0, preambleRead), resolveChecksum ?? ChecksumAlgorithm.DefaultResolver).ReplicationEpoch;
        }

        //The preamble declares the header's true length (a higher-minor header is longer than the v1.0 size);
        //read exactly the declared remainder. A file shorter than declared surfaces as Read's torn-header refusal.
        int headerLength = DatasetJournalHeader.DeclaredLength(preamble);
        byte[] header = new byte[headerLength];
        preamble.CopyTo(header, 0);
        int totalRead = DatasetJournalHeader.PreambleSize + ReadAvailable(handle, header.AsSpan(DatasetJournalHeader.PreambleSize), DatasetJournalHeader.PreambleSize);

        return DatasetJournalHeader.Read(header.AsSpan(0, totalRead), resolveChecksum ?? ChecksumAlgorithm.DefaultResolver).ReplicationEpoch;
    }

    /// <summary>Reads as many bytes as the file holds into <paramref name="buffer"/> starting at <paramref name="offset"/>, looping partial reads; fewer bytes than the buffer means the file ended.</summary>
    /// <param name="handle">The read handle.</param>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="offset">The file offset to read from.</param>
    /// <returns>The number of bytes read, at most the buffer length.</returns>
    private static int ReadAvailable(SafeFileHandle handle, Span<byte> buffer, long offset)
    {
        int total = 0;
        while(total < buffer.Length)
        {
            int read = RandomAccess.Read(handle, buffer[total..], offset + total);
            if(read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>Opens a durable dataset journal over <paramref name="filePath"/> with the given directory barrier and the production file-content flush.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="dictionary">The dictionary the term section is captured from and restored into.</param>
    /// <param name="termPool">The pool the decoded term bytes are interned into on replay.</param>
    /// <param name="hash">The hash function commitment verification recomputes under; the same hash the dataset's arena carries.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="DatasetJournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="barrier">The directory durability barrier flushed when the log file is first created; pass <see cref="AtomicPublish.DefaultBarrier"/> in production.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload, or its term section diverges from the dictionary.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedDatasetJournal(
        string filePath,
        TermDictionary dictionary,
        Utf8StringPool termPool,
        VeritasHash hash,
        ChecksumAlgorithm checksum,
        TimeProvider timeProvider,
        MemoryPool<byte> bufferPool,
        DurabilityBarrierDelegate barrier)
        : this(filePath, dictionary, termPool, hash, checksum, timeProvider, bufferPool, barrier, AtomicPublish.DefaultFlush)
    {
    }

    /// <summary>Opens a durable dataset journal over <paramref name="filePath"/> with the production durability seams.</summary>
    /// <param name="filePath">The append-only log file; created with its directory if absent.</param>
    /// <param name="dictionary">The dictionary the term section is captured from and restored into.</param>
    /// <param name="termPool">The pool the decoded term bytes are interned into on replay.</param>
    /// <param name="hash">The hash function commitment verification recomputes under; the same hash the dataset's arena carries.</param>
    /// <param name="checksum">The checksum algorithm each record is framed under.</param>
    /// <param name="timeProvider">The clock the journal stamps <see cref="DatasetJournalEntry.Timestamp"/> from on append.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A checksum-valid record on disk carries a malformed payload, or its term section diverges from the dictionary.</exception>
    /// <exception cref="NotSupportedException">A record uses an unsupported payload format version, or the host is big-endian.</exception>
    public FileBackedDatasetJournal(
        string filePath,
        TermDictionary dictionary,
        Utf8StringPool termPool,
        VeritasHash hash,
        ChecksumAlgorithm checksum,
        TimeProvider timeProvider,
        MemoryPool<byte> bufferPool)
        : this(filePath, dictionary, termPool, hash, checksum, timeProvider, bufferPool, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush)
    {
    }

    /// <summary>The named loss recovered on construction — an <see cref="UnrecoverableItemReportKind.OperationRange"/> report when a torn or corrupt tail was truncated, or <see langword="null"/> when the log replayed intact.</summary>
    public UnrecoverableItemReport? RecoveryReport { get; }

    /// <summary>The commitment findings collected during replay: one per entry whose stored fingerprint disagreed with a recomputation over its own parent and transitions. Empty when every entry verified. A finding is corruption evidence, not a refusal — replay continues past it.</summary>
    public ImmutableArray<JournalCommitmentFinding> CommitmentFindings { get; }

    /// <summary>The v2 header facts this log was opened over — the onboarding anchor, replication epoch, and attach term watermark — or the neutral v1 defaults (<see cref="DatasetJournalHeaderInfo.IsV2"/> <see langword="false"/>) for a headerless v1 log. Read once at open; never consulted in the torn-tail scan.</summary>
    internal DatasetJournalHeaderInfo Header { get; private set; }

    /// <summary>The persisted dataset state this log continues from (an attached log's onboarding anchor), or <see cref="NodeIdentifier.Empty"/> for a self-contained create-path log or a v1 file. The recovery pivot consumes it to fold an attached log's records over their base generation. State identifiers are content-addressed, so the anchor alone does not name a unique history — two independently built generations with identical encoded content share it; <see cref="HeaderReplicationEpoch"/> is the identity discriminator that tells such histories apart.</summary>
    public NodeIdentifier HeaderAnchor => Header.Anchor;

    /// <summary>The dictionary replication epoch this log's v2 header records — the epoch of the dictionary the log was created against — or <see langword="null"/> for a headerless v1 log. A generation-bearing reopen cross-checks it against the loaded dictionary's epoch: the content-addressed anchor cannot tell two independently built stores with identical encoded content apart, so the epoch is the discriminator that keeps a journal bound to the history it was attached under.</summary>
    public ulong? HeaderReplicationEpoch => Header.IsV2 ? Header.ReplicationEpoch : null;

    /// <summary>The current head identifier — the <see cref="DatasetJournalEntry.ChildId"/> of the most recent entry, the header anchor for an attached log with no entries yet (the persisted state the log continues from), or <see cref="NodeIdentifier.Empty"/> for an empty, unanchored journal.</summary>
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

    /// <summary>The append delegate this journal exposes for wiring into a dataset, enforcing the linear-log optimistic-concurrency contract durably.</summary>
    public DatasetJournalDelegates.AppendDatasetJournalEntryAsync AppendDelegate => AppendAsync;

    /// <summary>The read delegate this journal exposes for wiring into a dataset, streaming entries at or above a sequence lower bound from the in-memory mirror.</summary>
    public DatasetJournalDelegates.ReadDatasetJournalEntriesAsync ReadDelegate => ReadAsync;

    /// <summary>Replays the log file, recovering the intact prefix, restoring and verifying the dictionary from each record's term section, and collecting commitment findings; a v2 file's records are scanned after its header, a fresh v2 file's header is prepared for the constructor to write, and the physical truncation is applied by the constructor through the write handle.</summary>
    /// <param name="filePath">The log file.</param>
    /// <param name="writeV2HeaderOnFresh">Whether a fresh (length-0) file is stamped with a v2 header.</param>
    /// <param name="headerAnchor">The onboarding anchor stamped into a freshly-written v2 header.</param>
    /// <param name="attachTermWatermark">The attach term watermark stamped into a freshly-written v2 header; the exclusive lower bound the log's term-watermark chain starts from.</param>
    /// <param name="pendingHeaderBytes">Receives the v2 header bytes the constructor must write durably on a fresh v2 file; <see langword="null"/> for a v1 file or any existing file.</param>
    /// <returns>An operation-range report when a tail is to be truncated, or <see langword="null"/> when the log replayed intact.</returns>
    /// <exception cref="InvalidDataException">A record's sequence number is out of order, its term watermark does not continue the durable watermark chain, its term section denotes a term the dictionary already binds to a different identifier, an existing v2 header is corrupt or torn (including a sub-discriminator fragment on the v2 open path), or the header's record-stream algorithm disagrees with the journal's framing algorithm.</exception>
    /// <exception cref="NotSupportedException">A record or an existing v2 header uses an unsupported version, an existing v2 header's record-stream algorithm id is unresolvable, or the host is big-endian.</exception>
    private UnrecoverableItemReport? Replay(string filePath, bool writeV2HeaderOnFresh, NodeIdentifier headerAnchor, int attachTermWatermark, out byte[]? pendingHeaderBytes)
    {
        pendingHeaderBytes = null;
        Header = default;
        durableTermWatermark = 0;

        byte[] bytes = File.Exists(filePath) ? File.ReadAllBytes(filePath) : [];
        if(bytes.Length == 0)
        {
            //A fresh file: either stamp a v2 header (whose watermark seeds the term-watermark chain) or leave it
            //headerless as a v1 log.
            if(writeV2HeaderOnFresh)
            {
                pendingHeaderBytes = new byte[DatasetJournalHeader.Size];
                DatasetJournalHeader.Write(pendingHeaderBytes, headerAnchor, Dictionary.Epoch, attachTermWatermark, Checksum);
                Header = new DatasetJournalHeaderInfo(IsV2: true, headerAnchor, Dictionary.Epoch, attachTermWatermark, DatasetJournalHeader.Size, Checksum.Id);
                durableTermWatermark = attachTermWatermark;
                writeOffset = DatasetJournalHeader.Size;

                //An attached log's head is the anchor state it continues from until the first record advances it, so a
                //commit opens against the same head the resumed dataset serves. A create-path header (Empty anchor)
                //leaves the head empty, so the Initial record still opens the log.
                head = headerAnchor;
            }
            else
            {
                writeOffset = 0;
            }

            return null;
        }

        //A torn v2 creation can leave fewer bytes than the discriminator needs; on the v2 open path that fragment is
        //refused deterministically (a torn creation acked nothing), never mistaken for a v1 log and truncated. The
        //plain v1 open path keeps its truncate-to-boundary semantics over the same bytes.
        if(writeV2HeaderOnFresh && bytes.Length <= DatasetJournalHeader.DiscriminatorIndex)
        {
            throw new InvalidDataException("A dataset journal v2 file is truncated before its discriminator; a torn creation acked nothing, so the file is refused.");
        }

        //A v2 file's records begin after its header's TRUE on-disk length (a higher-minor header declares a longer
        //payload); a headerless v1 file's first record sits at offset 0. A v2 header's attach watermark seeds the
        //term-watermark chain, so the first record continues from it.
        int recordStart;
        if(DatasetJournalHeader.LooksLikeV2(bytes))
        {
            Header = DatasetJournalHeader.Read(bytes, ResolveChecksum);

            //The header's record-stream algorithm id must be the very algorithm this journal frames records under:
            //a resolvable-but-different algorithm (a narrower checksum over wider records) would fail every record's
            //verify and read as a clean torn tail — silent truncation of acked history, which the header exists to
            //prevent.
            if(Header.RecordStreamChecksumId != Checksum.Id)
            {
                throw new InvalidDataException($"The dataset journal header records record-stream checksum algorithm id {Header.RecordStreamChecksumId}, but this journal frames records under id {Checksum.Id} ({Checksum.Name}); a mis-framed scan would mistake every record for a torn tail, so the log is refused.");
            }

            durableTermWatermark = Header.AttachTermWatermark;

            //An attached log with no records continues from its anchor state; a record below advances the head past
            //it. A create-path or v1 log anchors at the empty head, so the head stays empty until its Initial record.
            head = Header.Anchor;
            recordStart = Header.HeaderLength;
        }
        else
        {
            recordStart = 0;
        }

        int offset = recordStart;
        long recoveredThroughSequence = -1;
        while(DatasetJournalRecordFormat.TryReadRecord(bytes.AsSpan(offset), Checksum, TermPool, out DatasetJournalRecord record, out int recordLength))
        {
            DatasetJournalEntry entry = record.Entry;
            if(entry.SequenceNumber != Entries.Count)
            {
                throw new InvalidDataException($"A dataset journal record at sequence position {Entries.Count} carries sequence number {entry.SequenceNumber}.");
            }

            //Every record must continue the durable term-watermark chain: the first record continues from the
            //header's attach watermark (0 for a create-path or v1 log), a later record from the previous record's
            //captured count. On a generation-loaded dictionary the first record's overlap identifiers verify and
            //its new identifiers append; the watermark equality is what proves the log belongs to this chain.
            if(record.TermWatermark != durableTermWatermark)
            {
                throw new InvalidDataException($"A dataset journal record's term watermark {record.TermWatermark} does not continue the durable count {durableTermWatermark}.");
            }

            RestoreTermSection(record);
            durableTermWatermark = record.TermWatermark + record.NewTerms.Length;

            if(entry.EditCommitment.HasValue)
            {
                NodeIdentifier recomputed = DatasetStateHashing.ComputeCommitment(Hash, entry.ParentId, entry.Transitions);
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

    /// <summary>Verifies and restores a record's term section against the live dictionary: an identifier the dictionary already holds must resolve to the same term (overlap), the next identifier appends the term (and the assigned id must match), and any higher identifier is a gap.</summary>
    /// <param name="record">The decoded record whose term section is applied.</param>
    /// <exception cref="InvalidDataException">A term diverges from the dictionary's term at the same identifier, an appended term lands at a lower identifier, or the section skips an identifier.</exception>
    private void RestoreTermSection(in DatasetJournalRecord record)
    {
        int watermark = record.TermWatermark;
        RdfTerm[] terms = record.NewTerms;
        for(int i = 0; i < terms.Length; i++)
        {
            uint expectedId = (uint)(watermark + 1 + i);
            uint currentCount = (uint)Dictionary.Count;
            if(expectedId <= currentCount)
            {
                RdfTerm existing = Dictionary.Resolve(expectedId);
                if(!existing.Equals(terms[i]))
                {
                    throw new InvalidDataException($"A dataset journal term at identifier {expectedId} does not match the dictionary's term; the journal belongs to a different history.");
                }
            }
            else if(expectedId == currentCount + 1)
            {
                TermId assigned = Dictionary.GetOrAdd(terms[i]);
                if(assigned.Encoded != expectedId)
                {
                    throw new InvalidDataException($"A dataset journal term expected identifier {expectedId} but the dictionary assigned {assigned.Encoded}; the term is already present at a lower identifier.");
                }
            }
            else
            {
                throw new InvalidDataException($"A dataset journal term section skips an identifier: {expectedId} exceeds the dictionary's next identifier {currentCount + 1}.");
            }
        }
    }

    /// <summary>Appends an entry durably under optimistic concurrency: the term range and record are flushed to disk before the in-memory state advances.</summary>
    /// <param name="entry">The entry to append; its sequence number and timestamp are overwritten by the journal.</param>
    /// <param name="expectedHead">The head the caller observed; the append succeeds only when it still equals the actual head.</param>
    /// <param name="cancellationToken">A token that aborts the append before any work.</param>
    /// <returns>The assigned sequence number.</returns>
    /// <exception cref="EditSessionConcurrencyException">The head no longer equals <paramref name="expectedHead"/>.</exception>
    /// <exception cref="ObjectDisposedException">The journal has been disposed.</exception>
    /// <exception cref="ArgumentException">The entry carries a kind the durable format does not accept, or the record would exceed the single-record byte bound.</exception>
    private ValueTask<long> AppendAsync(DatasetJournalEntry entry, NodeIdentifier expectedHead, CancellationToken cancellationToken)
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
            DatasetJournalEntry stored = entry with
            {
                SequenceNumber = assignedSequence,
                Timestamp = TimeProvider.GetUtcNow(),
            };

            //Capture the terms minted since the last durable record atomically against concurrent mints, then
            //write durably before advancing any in-memory state: if the durable write throws, neither the write
            //offset nor the term watermark nor the in-memory state advances, so a retry overwrites the failed
            //bytes in place and re-captures the same term range.
            (int count, RdfTerm[] newTerms) = Dictionary.CaptureBeyond(durableTermWatermark);
            WriteRecordDurably(stored, durableTermWatermark, newTerms);

            Entries.Add(stored);
            head = stored.ChildId;
            durableTermWatermark = count;
        }

        return ValueTask.FromResult(assignedSequence);
    }

    /// <summary>Serializes one record into a pooled buffer, writes it at the log's end offset, flushes it to stable storage, then advances the offset — the synchronous durability barrier the append is built on.</summary>
    /// <param name="entry">The journal-stamped entry to write.</param>
    /// <param name="termWatermark">The exclusive lower bound of the record's term identifier range.</param>
    /// <param name="newTerms">The terms minted since the previous durable record.</param>
    private void WriteRecordDurably(in DatasetJournalEntry entry, int termWatermark, ReadOnlySpan<RdfTerm> newTerms)
    {
        int size = DatasetJournalRecordFormat.ComputeRecordSize(entry, termWatermark, newTerms, Checksum);
        using IMemoryOwner<byte> owner = BufferPool.Rent(size);
        Span<byte> buffer = owner.Memory.Span[..size];
        DatasetJournalRecordFormat.WriteRecord(buffer, entry, termWatermark, newTerms, Checksum);

        RandomAccess.Write(Handle, buffer, writeOffset);
        DurableFlush(Handle);
        writeOffset += size;
    }

    [SuppressMessage(
        "Style",
        "CS1998:Async method lacks await operators",
        Justification = "Yield-only async iterator. The async modifier is required for the IAsyncEnumerable return type and the EnumeratorCancellation attribute, but the iterator emits an in-memory snapshot and has no asynchronous work of its own.")]
    private async IAsyncEnumerable<DatasetJournalEntry> ReadAsync(
        long fromSequenceNumber,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
