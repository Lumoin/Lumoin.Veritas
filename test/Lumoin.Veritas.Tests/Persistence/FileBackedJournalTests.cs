using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Journal;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32.SafeHandles;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The durable file-backed journal: appends are flushed before they are acknowledged and survive a
/// reopen; the optimistic-concurrency, journal-owned-sequence, and journal-owned-timestamp contract
/// matches <see cref="InMemoryJournal"/> so the delegates drop into a node store unchanged; and replay
/// recovers the intact prefix of a damaged log, truncating a torn or corrupt tail and naming the loss
/// (<see cref="PersistenceInvariant.LossIsNamed"/>) rather than throwing or mis-reading. Fault
/// injection is deterministic — specific bytes are flipped or appended, no timers, no randomness.
/// </summary>
[TestClass]
internal sealed class FileBackedJournalTests
{
    /// <summary>The MSTest context, used for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Creates a fresh, uniquely-named temp directory for one test's journal file.</summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        return Directory.CreateTempSubdirectory("veritas-journal-").FullName;
    }

    /// <summary>Builds a non-mutating initial entry from <paramref name="parent"/> to <paramref name="child"/> with placeholder journal-owned fields.</summary>
    /// <param name="parent">The parent identifier.</param>
    /// <param name="child">The child identifier.</param>
    /// <param name="sequence">The placeholder sequence number (overwritten on append).</param>
    /// <returns>The entry.</returns>
    private static JournalEntry MakeEntry(NodeIdentifier parent, NodeIdentifier child, long sequence = 0)
    {
        return new JournalEntry(
            ParentId: parent,
            ChildId: child,
            EntryKind: EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: null,
            Additions: ImmutableArray<EncodedTriple>.Empty,
            Removals: ImmutableArray<EncodedTriple>.Empty,
            Timestamp: default,
            SequenceNumber: sequence);
    }

    /// <summary>Builds a committed entry carrying additions, removals, a session id, and an edit commitment, to exercise every payload field on round-trip.</summary>
    /// <param name="parent">The parent identifier.</param>
    /// <param name="child">The child identifier.</param>
    /// <returns>The entry.</returns>
    private static JournalEntry RichEntry(NodeIdentifier parent, NodeIdentifier child)
    {
        return new JournalEntry(
            ParentId: parent,
            ChildId: child,
            EntryKind: EditSessionEntryKind.Committed,
            SessionId: new SessionId(new Guid("0102030405060708090A0B0C0D0E0F10")),
            EditCommitment: new NodeIdentifier(0x1234_5678_9ABC_DEF0UL),
            Additions: [EncodedTriple.FromEncoded(1, 2, 3), EncodedTriple.FromEncoded(4, 5, 6)],
            Removals: [EncodedTriple.FromEncoded(7, 8, 9)],
            Timestamp: default,
            SequenceNumber: 0);
    }

    /// <summary>Reads every entry from the journal into a list.</summary>
    /// <param name="journal">The journal to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries in order.</returns>
    private static async Task<List<JournalEntry>> ReadAll(FileBackedJournal journal, CancellationToken cancellationToken)
    {
        List<JournalEntry> entries = [];
        await foreach(JournalEntry entry in journal.ReadDelegate(0L, cancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Appends entries 1..<paramref name="count"/> in a chain from the empty head, returning the final head.</summary>
    /// <param name="journal">The journal to append to.</param>
    /// <param name="count">The number of entries to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The head after the chain.</returns>
    private static async Task<NodeIdentifier> AppendChain(FileBackedJournal journal, ulong count, CancellationToken cancellationToken)
    {
        NodeIdentifier previous = NodeIdentifier.Empty;
        for(ulong i = 1; i <= count; i++)
        {
            NodeIdentifier next = new(i);
            await journal.AppendDelegate(MakeEntry(previous, next), previous, cancellationToken).ConfigureAwait(false);
            previous = next;
        }

        return previous;
    }

    /// <summary>Asserts two entries are equal field by field, comparing the triple arrays element-wise (immutable-array equality is by reference, not structural).</summary>
    /// <param name="expected">The expected entry.</param>
    /// <param name="actual">The actual entry.</param>
    private static void AssertEntriesEqual(JournalEntry expected, JournalEntry actual)
    {
        Assert.AreEqual(expected.ParentId, actual.ParentId);
        Assert.AreEqual(expected.ChildId, actual.ChildId);
        Assert.AreEqual(expected.EntryKind, actual.EntryKind);
        Assert.AreEqual(expected.SessionId, actual.SessionId);
        Assert.AreEqual(expected.EditCommitment, actual.EditCommitment);
        Assert.AreEqual(expected.Timestamp, actual.Timestamp);
        Assert.AreEqual(expected.SequenceNumber, actual.SequenceNumber);
        Assert.IsTrue(expected.Additions.AsSpan().SequenceEqual(actual.Additions.AsSpan()), "Additions differ.");
        Assert.IsTrue(expected.Removals.AsSpan().SequenceEqual(actual.Removals.AsSpan()), "Removals differ.");
    }

    /// <summary>A fresh journal over a new file is empty and reports no recovery.</summary>
    [TestMethod]
    public void NewJournalIsEmpty()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            Assert.AreEqual(0, journal.Length);
            Assert.AreEqual(NodeIdentifier.Empty, journal.Head);
            Assert.IsNull(journal.RecoveryReport);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Sequential appends assign dense sequence numbers and advance the head.</summary>
    [TestMethod]
    public async Task SequentialAppendsAdvanceHeadAndSequence()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            NodeIdentifier first = new(0xAAAA);
            NodeIdentifier second = new(0xBBBB);
            long s1 = await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            long s2 = await journal.AppendDelegate(MakeEntry(first, second), first, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(0L, s1);
            Assert.AreEqual(1L, s2);
            Assert.AreEqual(2, journal.Length);
            Assert.AreEqual(second, journal.Head);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A kind the record format cannot read back is rejected at the
    /// WRITE boundary, before any bytes reach the file — an
    /// acknowledged-but-unreadable record would poison the whole log
    /// at the next reopen. The journal stays usable afterwards.
    /// </summary>
    [TestMethod]
    public async Task AppendOfAKindTheFormatCannotReadBackIsRejectedBeforeWriting()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            NodeIdentifier first = new(0xAAAA);
            JournalEntry forked = MakeEntry(NodeIdentifier.Empty, first) with { EntryKind = EditSessionEntryKind.Forked };
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                async () => await journal.AppendDelegate(forked, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

            Assert.AreEqual(0, journal.Length);
            Assert.AreEqual(NodeIdentifier.Empty, journal.Head);

            //The rejected append left no bytes behind: an ordinary
            //append still lands at sequence zero.
            long sequence = await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(0L, sequence);
            Assert.AreEqual(first, journal.Head);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An append whose observed head no longer matches is rejected and leaves the state unchanged.</summary>
    [TestMethod]
    public async Task AppendWithMismatchedExpectedHeadThrows()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            NodeIdentifier first = new(0xAAAA);
            NodeIdentifier rogue = new(0xDEAD);
            await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

            await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await journal.AppendDelegate(MakeEntry(rogue, new NodeIdentifier(0xBEEF)), rogue, TestContext.CancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.AreEqual(1, journal.Length);
            Assert.AreEqual(first, journal.Head);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The journal owns sequence and timestamp: a caller's placeholder values are overwritten with the assigned sequence and the clock-stamped time.</summary>
    [TestMethod]
    public async Task AppendOverwritesProvidedSequenceAndTimestamp()
    {
        string directory = CreateTempDirectory();
        try
        {
            DateTimeOffset pinned = new(2026, 6, 17, 9, 30, 0, TimeSpan.Zero);
            FakeTimeProvider clock = new(pinned);
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, clock, pool);

            await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL), sequence: 777), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

            List<JournalEntry> entries = await ReadAll(journal, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(1, entries);
            Assert.AreEqual(0L, entries[0].SequenceNumber);
            Assert.AreEqual(pinned, entries[0].Timestamp);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Appended entries are durable: reopening the file replays every entry intact, with no recovery report, across the checksum selections — and every payload field round-trips.</summary>
    [TestMethod]
    public async Task ReopenReplaysEveryEntryDurablyAcrossChecksums()
    {
        using VeritasMemoryPool<byte> pool = new();
        foreach(ChecksumAlgorithm checksum in (ChecksumAlgorithm[])[ChecksumAlgorithm.XxHash3, ChecksumAlgorithm.Crc32])
        {
            string directory = CreateTempDirectory();
            try
            {
                string path = Path.Combine(directory, "journal.log");
                DateTimeOffset pinned = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
                FakeTimeProvider clock = new(pinned);
                NodeIdentifier first = new(0x1111);
                NodeIdentifier second = new(0x2222);

                List<JournalEntry> before;
                using(FileBackedJournal journal = new(path, checksum, clock, pool))
                {
                    //A rich entry (session, commitment, non-empty arrays) and a bare entry (null session, null commitment, empty arrays) so both presence branches decode-verify.
                    await journal.AppendDelegate(RichEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
                    await journal.AppendDelegate(MakeEntry(first, second), first, TestContext.CancellationToken).ConfigureAwait(false);
                    before = await ReadAll(journal, TestContext.CancellationToken).ConfigureAwait(false);
                }

                using(FileBackedJournal reopened = new(path, checksum, clock, pool))
                {
                    Assert.IsNull(reopened.RecoveryReport, $"A clean log reported recovery (algorithm {checksum.Name}).");
                    Assert.AreEqual(2, reopened.Length);
                    Assert.AreEqual(second, reopened.Head);

                    List<JournalEntry> after = await ReadAll(reopened, TestContext.CancellationToken).ConfigureAwait(false);
                    Assert.HasCount(before.Count, after);
                    for(int i = 0; i < before.Count; i++)
                    {
                        AssertEntriesEqual(before[i], after[i]);
                    }
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>I7: a torn tail appended past the last intact record is truncated on replay; the log recovers through its last operation and names the discarded byte range, and a second reopen is clean.</summary>
    [TestMethod]
    public async Task TornTailRecoversToBoundaryAndTruncates()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await AppendChain(journal, 3, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] intact = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            byte[] torn = [.. intact, 1, 2, 3, 4, 5];
            await File.WriteAllBytesAsync(path, torn, TestContext.CancellationToken).ConfigureAwait(false);

            using(FileBackedJournal recovered = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                Assert.AreEqual(3, recovered.Length);
                Assert.AreEqual(new NodeIdentifier(3UL), recovered.Head);
                UnrecoverableItemReport? report = recovered.RecoveryReport;
                Assert.IsNotNull(report);
                Assert.AreEqual(UnrecoverableItemReportKind.OperationRange, report.Kind);
                Assert.AreEqual(2L, report.RecoveredThroughSequence);
                Assert.AreEqual(5L, report.DiscardedByteCount);
            }

            //The torn tail was physically truncated, so the file is intact again and a second reopen is clean.
            Assert.AreEqual(intact.Length, new FileInfo(path).Length);
            using(FileBackedJournal reopened = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                Assert.IsNull(reopened.RecoveryReport);
                Assert.AreEqual(3, reopened.Length);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A byte flipped in the last record fails its checksum, so replay recovers the operations before it and names the loss; the head falls back to the prior committed snapshot.</summary>
    [TestMethod]
    public async Task LastRecordCorruptionRecoversThePriorOperations()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await AppendChain(journal, 3, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            bytes[^1] ^= 0xFF;
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedJournal recovered = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);
            Assert.AreEqual(2, recovered.Length);
            Assert.AreEqual(new NodeIdentifier(2UL), recovered.Head);
            UnrecoverableItemReport? report = recovered.RecoveryReport;
            Assert.IsNotNull(report);
            Assert.AreEqual(1L, report.RecoveredThroughSequence);
            Assert.IsGreaterThan(0L, report.DiscardedByteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A byte flipped in the very first record fails its checksum, so nothing is recovered; the loss is named with a -1 recovered-through sequence rather than the store coming up as if intact.</summary>
    [TestMethod]
    public async Task FirstRecordCorruptionRecoversNothing()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await AppendChain(journal, 3, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            long originalLength = bytes.Length;
            //Offset 6 is inside the first record's payload (past the 4-byte length prefix).
            bytes[6] ^= 0xFF;
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedJournal recovered = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);
            Assert.AreEqual(0, recovered.Length);
            Assert.AreEqual(NodeIdentifier.Empty, recovered.Head);
            UnrecoverableItemReport? report = recovered.RecoveryReport;
            Assert.IsNotNull(report);
            Assert.AreEqual(-1L, report.RecoveredThroughSequence);
            Assert.AreEqual(originalLength, report.DiscardedByteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A read from a non-zero lower bound skips the lower sequence numbers.</summary>
    [TestMethod]
    public async Task ReadFromNonZeroSkipsLowerSequences()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            await AppendChain(journal, 5, TestContext.CancellationToken).ConfigureAwait(false);

            List<JournalEntry> entries = [];
            await foreach(JournalEntry entry in journal.ReadDelegate(3L, TestContext.CancellationToken).ConfigureAwait(false))
            {
                entries.Add(entry);
            }

            Assert.HasCount(2, entries);
            Assert.AreEqual(3L, entries[0].SequenceNumber);
            Assert.AreEqual(4L, entries[1].SequenceNumber);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The exposed append and read delegates are usable as standalone delegates of the node-store contract types — the drop-in proof.</summary>
    [TestMethod]
    public async Task DelegatePropertiesDropInUsable()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            JournalDelegates.AppendJournalEntryAsync append = journal.AppendDelegate;
            JournalDelegates.ReadJournalEntriesAsync read = journal.ReadDelegate;

            long sequence = await append(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(0L, sequence);

            int count = 0;
            await foreach(JournalEntry _ in read(0L, TestContext.CancellationToken).ConfigureAwait(false))
            {
                count++;
            }

            Assert.AreEqual(1, count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>After disposal an append throws rather than writing to a closed stream.</summary>
    [TestMethod]
    public async Task AppendAfterDisposeThrows()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);
            journal.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Concurrent appenders racing the same head linearise: exactly one wins per round, durably, and the chain stays dense.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using 'IDisposable' instances complete before the instances are disposed",
        Justification = "Each round's append tasks are awaited through Task.WhenAll before the loop continues, so every task that captured the journal has completed before the journal's using scope disposes it.")]
    [TestMethod]
    public async Task ConcurrentAppendsLinearise()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(Path.Combine(directory, "journal.log"), ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);

            NodeIdentifier seed = new(0xFEED);
            await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, seed), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

            const int rounds = 3;
            const int contendersPerRound = 4;
            for(int round = 0; round < rounds; round++)
            {
                NodeIdentifier currentHead = journal.Head;
                Task<long?>[] attempts = new Task<long?>[contendersPerRound];
                for(int i = 0; i < contendersPerRound; i++)
                {
                    ulong childValue = ((ulong)round << 32) | ((ulong)i + 1UL);
                    JournalEntry entry = MakeEntry(currentHead, new NodeIdentifier(childValue));
                    attempts[i] = TryAppendAsync(journal, entry, currentHead);
                }

                long?[] results = await Task.WhenAll(attempts).ConfigureAwait(false);
                int successes = results.Count(static r => r.HasValue);
                Assert.AreEqual(1, successes, $"Round {round}: exactly one append must win.");
            }

            Assert.AreEqual(1 + rounds, journal.Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>I7 asymmetry: a checksum-valid record carrying an unsupported payload version is refused (the constructor throws), never silently truncated as if it were a torn tail — so a newer log opened by an older build is not shortened.</summary>
    [TestMethod]
    public async Task UnsupportedPayloadVersionIsRefusedNotTruncated()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            long originalLength = bytes.Length;
            //The payload version byte sits just past the 4-byte length prefix; bump it and re-seal the record so it still verifies.
            bytes[4] = 99;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.ThrowsExactly<NotSupportedException>(() => { using FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool); });
            Assert.AreEqual(originalLength, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A checksum-valid record whose decoded payload is structurally malformed (an unknown entry kind) is a codec error: the constructor throws rather than truncating it as at-rest corruption.</summary>
    [TestMethod]
    public async Task MalformedPayloadIsRefusedNotTruncated()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            long originalLength = bytes.Length;
            //The kind byte is payload offset 9 (version 1 + sequence 8), i.e. file offset 13; set it past the last valid kind.
            bytes[13] = 99;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.ThrowsExactly<InvalidDataException>(() => { using FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool); });
            Assert.AreEqual(originalLength, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A garbage tail whose length prefix decodes to a huge value is bounded — it is treated as the recovery boundary without over-allocating — and the intact prefix is recovered.</summary>
    [TestMethod]
    public async Task HugeLengthPrefixTailIsBoundedAndRecovered()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await AppendChain(journal, 2, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] intact = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            //A maximal length prefix (0xFFFFFFFF) followed by two bytes: must be rejected by the bound, not allocated.
            byte[] withGarbageLength = [.. intact, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00];
            await File.WriteAllBytesAsync(path, withGarbageLength, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedJournal recovered = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool);
            Assert.AreEqual(2, recovered.Length);
            UnrecoverableItemReport? report = recovered.RecoveryReport;
            Assert.IsNotNull(report);
            Assert.AreEqual(1L, report.RecoveredThroughSequence);
            Assert.AreEqual(6L, report.DiscardedByteCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The directory durability barrier is invoked exactly once, when the log file is first created, and not again on a reopen — closing the acknowledged-but-directory-not-durable gap for a fresh log.</summary>
    [TestMethod]
    public void DurabilityBarrierIsInvokedOnFirstCreationOnly()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            using VeritasMemoryPool<byte> pool = new();
            RecordingBarrier barrier = new();

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, barrier.Flush))
            {
                Assert.AreEqual(1, barrier.CallCount);
                Assert.AreEqual(expectedDirectory, barrier.LastDirectory);
            }

            using(FileBackedJournal reopened = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, barrier.Flush))
            {
                Assert.AreEqual(1, barrier.CallCount);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Re-seals a single-record file: recomputes the record's checksum over its length prefix and payload after the bytes were mutated, so the record verifies and replay reaches the decode path.</summary>
    /// <param name="record">The single-record file bytes, mutated in place.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    private static void RecomputeSingleRecordChecksum(byte[] record, ChecksumAlgorithm checksum)
    {
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(record);
        int checksummedLength = sizeof(uint) + (int)payloadLength;
        checksum.Compute(record.AsSpan(0, checksummedLength), record.AsSpan(checksummedLength, checksum.ByteWidth));
    }

    /// <summary>Wraps an append to swallow concurrency conflicts and report a nullable sequence number — for the concurrent-append race where most contenders lose.</summary>
    /// <param name="journal">The journal.</param>
    /// <param name="entry">The entry to append.</param>
    /// <param name="expectedHead">The head the contender observed.</param>
    /// <returns>The assigned sequence number, or <see langword="null"/> when the contender lost the race.</returns>
    private static async Task<long?> TryAppendAsync(FileBackedJournal journal, JournalEntry entry, NodeIdentifier expectedHead)
    {
        try
        {
            return await journal.AppendDelegate(entry, expectedHead, CancellationToken.None).ConfigureAwait(false);
        }
        catch(EditSessionConcurrencyException)
        {
            return null;
        }
    }

    /// <summary>A test directory durability barrier that records how often it was invoked and the last directory it flushed, so a test can assert the journal flushes the directory once on first creation.</summary>
    private sealed class RecordingBarrier
    {
        /// <summary>The number of times the barrier was invoked.</summary>
        public int CallCount { get; private set; }

        /// <summary>The directory passed to the most recent invocation.</summary>
        public string? LastDirectory { get; private set; }

        /// <summary>Records one barrier invocation; matches the <see cref="DurabilityBarrierDelegate"/> shape.</summary>
        /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
        public void Flush(string directoryPath)
        {
            CallCount++;
            LastDirectory = directoryPath;
        }
    }

    /// <summary>
    /// Every durable append flushes the record through the injected file-content
    /// flush seam, so the sound production default — <c>fcntl(F_FULLFSYNC)</c> on
    /// the Apple mobile platforms where the runtime flush degrades to a plain
    /// <c>fsync</c> — is reached on every record. A regression that wrote durably
    /// without going through the seam would silently weaken durability on those
    /// platforms; this asserts the seam is on the path.
    /// </summary>
    [TestMethod]
    public async Task EveryDurableAppendFlushesThroughTheFlushSeam()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            RecordingFlush flush = new();

            using FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, AtomicPublish.DefaultBarrier, flush.Flush);
            NodeIdentifier head = NodeIdentifier.Empty;
            for(int i = 1; i <= 3; i++)
            {
                NodeIdentifier next = new((ulong)i);
                await journal.AppendDelegate(MakeEntry(head, next), head, TestContext.CancellationToken).ConfigureAwait(false);
                head = next;
            }

            Assert.AreEqual(3, flush.CallCount, "A durable append did not go through the file-content flush seam.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// An append whose durability flush fails surfaces the failure rather than
    /// acknowledging the record: a failed <c>fsync</c> is never a silent false
    /// durable commit. This is the fault-injection point a real power-loss flush
    /// failure manifests at, driven directly through the flush seam.
    /// </summary>
    [TestMethod]
    public async Task AnAppendWhoseDurabilityFlushFailsSurfacesTheFailure()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, AtomicPublish.DefaultBarrier, FailingFlush);

            await Assert.ThrowsExactlyAsync<IOException>(
                async () => await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A test file-content durability flush that records how often it was invoked and still flushes durably through the production default, so a test can assert every durable append reaches the flush seam.</summary>
    private sealed class RecordingFlush
    {
        /// <summary>The number of times the flush was invoked.</summary>
        public int CallCount { get; private set; }

        /// <summary>Records one flush invocation and performs the production durable flush; matches the <see cref="DurableFlushDelegate"/> shape.</summary>
        /// <param name="handle">The open handle to the written file.</param>
        public void Flush(SafeFileHandle handle)
        {
            CallCount++;
            AtomicPublish.DefaultFlush(handle);
        }
    }

    /// <summary>A file-content durability flush that always fails — the injected <c>fsync</c> failure a power-loss harness drives the durable-write path with; matches the <see cref="DurableFlushDelegate"/> shape.</summary>
    /// <param name="handle">The open handle that would be flushed.</param>
    private static void FailingFlush(SafeFileHandle handle)
    {
        throw new IOException("Injected durable-flush failure (a simulated fsync error).");
    }
}
