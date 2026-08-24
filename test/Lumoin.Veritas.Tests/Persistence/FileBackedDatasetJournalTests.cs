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
using Lumoin.Veritas.Core.Encoding;
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
/// The durable file-backed DATASET journal: appends are flushed before they are acknowledged and survive a
/// reopen; the optimistic-concurrency, journal-owned-sequence, and journal-owned-timestamp contract matches
/// <see cref="InMemoryDatasetJournal"/>; replay recovers the intact prefix of a damaged log, truncating a
/// torn or corrupt tail and naming the loss; the dataset-only term section makes the log self-contained,
/// restoring the term dictionary on replay and refusing a divergent history; and a fingerprint disagreement
/// between a stored commitment and a recomputation is surfaced as a finding, not a refusal. Fault injection
/// is deterministic — specific bytes are flipped or appended, no timers, no randomness.
/// </summary>
[TestClass]
internal sealed class FileBackedDatasetJournalTests
{
    /// <summary>The MSTest context, used for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The content-addressing hash the journal and the commitment factories agree on.</summary>
    private static VeritasHash Hash { get; } = VeritasHashing.Default;

    /// <summary>Creates a fresh, uniquely-named temp directory for one test's journal file.</summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        return Directory.CreateTempSubdirectory("veritas-dsjournal-").FullName;
    }

    /// <summary>Builds a non-mutating-shaped initial entry from <paramref name="parent"/> to <paramref name="child"/> with no transitions and placeholder journal-owned fields.</summary>
    /// <param name="parent">The parent identifier.</param>
    /// <param name="child">The child identifier.</param>
    /// <param name="sequence">The placeholder sequence number (overwritten on append).</param>
    /// <returns>The entry.</returns>
    private static DatasetJournalEntry MakeEntry(NodeIdentifier parent, NodeIdentifier child, long sequence = 0)
    {
        return new DatasetJournalEntry(
            ParentId: parent,
            ChildId: child,
            EntryKind: EditSessionEntryKind.Initial,
            SessionId: null,
            EditCommitment: null,
            Transitions: [],
            Timestamp: default,
            SequenceNumber: sequence);
    }

    /// <summary>Builds a transition over a graph with explicit parent and child roots and triple deltas.</summary>
    /// <param name="graph">The graph the transition applies to.</param>
    /// <param name="parentRoot">The root before the transition; <see langword="null"/> to create the graph.</param>
    /// <param name="childRoot">The root after the transition; <see langword="null"/> to drop the graph.</param>
    /// <param name="additions">The added triples.</param>
    /// <param name="removals">The removed triples.</param>
    /// <returns>The transition.</returns>
    private static DatasetGraphTransition Transition(
        TermId graph,
        NodeIdentifier? parentRoot,
        NodeIdentifier? childRoot,
        ImmutableArray<EncodedTriple> additions,
        ImmutableArray<EncodedTriple> removals)
    {
        return new DatasetGraphTransition(graph, parentRoot, childRoot, additions, removals);
    }

    /// <summary>Opens a journal over <paramref name="path"/> with the production durability seams (a headerless v1 log on a fresh file).</summary>
    /// <param name="path">The log file.</param>
    /// <param name="dictionary">The dictionary the journal captures terms from and restores into.</param>
    /// <param name="termPool">The pool the journal interns decoded term bytes into on replay.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="timeProvider">The clock the journal stamps timestamps from.</param>
    /// <returns>The opened journal.</returns>
    private static FileBackedDatasetJournal Open(string path, TermDictionary dictionary, Utf8StringPool termPool, VeritasMemoryPool<byte> bufferPool, TimeProvider timeProvider)
    {
        return new FileBackedDatasetJournal(path, dictionary, termPool, Hash, ChecksumAlgorithm.XxHash3, timeProvider, bufferPool);
    }

    /// <summary>Opens a dataset-journal format v2 journal over <paramref name="path"/>, writing a v2 header on a fresh file.</summary>
    /// <param name="path">The log file.</param>
    /// <param name="dictionary">The dictionary the journal captures terms from and restores into; its epoch is stamped into a freshly-written header.</param>
    /// <param name="termPool">The pool the journal interns decoded term bytes into on replay.</param>
    /// <param name="bufferPool">The pool the per-append serialization buffer is rented from.</param>
    /// <param name="anchor">The onboarding anchor stamped into a freshly-written header, or <see cref="NodeIdentifier.Empty"/> for a create-path log.</param>
    /// <param name="attachTermWatermark">The attach term watermark stamped into a freshly-written header.</param>
    /// <returns>The opened journal.</returns>
    private static FileBackedDatasetJournal OpenV2(string path, TermDictionary dictionary, Utf8StringPool termPool, VeritasMemoryPool<byte> bufferPool, NodeIdentifier anchor, int attachTermWatermark)
    {
        return FileBackedDatasetJournal.OpenV2(path, dictionary, termPool, Hash, ChecksumAlgorithm.XxHash3, TimeProvider.System, bufferPool, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush, anchor, attachTermWatermark);
    }

    /// <summary>Reads every entry from the journal into a list.</summary>
    /// <param name="journal">The journal to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The entries in order.</returns>
    private static async Task<List<DatasetJournalEntry>> ReadAll(FileBackedDatasetJournal journal, CancellationToken cancellationToken)
    {
        List<DatasetJournalEntry> entries = [];
        await foreach(DatasetJournalEntry entry in journal.ReadDelegate(0L, cancellationToken).ConfigureAwait(false))
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
    private static async Task<NodeIdentifier> AppendChain(FileBackedDatasetJournal journal, ulong count, CancellationToken cancellationToken)
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

    /// <summary>Asserts two entries are equal field by field, comparing the transitions element-wise (immutable-array equality is by reference, not structural).</summary>
    /// <param name="expected">The expected entry.</param>
    /// <param name="actual">The actual entry.</param>
    private static void AssertEntriesEqual(DatasetJournalEntry expected, DatasetJournalEntry actual)
    {
        Assert.AreEqual(expected.ParentId, actual.ParentId);
        Assert.AreEqual(expected.ChildId, actual.ChildId);
        Assert.AreEqual(expected.EntryKind, actual.EntryKind);
        Assert.AreEqual(expected.SessionId, actual.SessionId);
        Assert.AreEqual(expected.EditCommitment, actual.EditCommitment);
        Assert.AreEqual(expected.Timestamp, actual.Timestamp);
        Assert.AreEqual(expected.SequenceNumber, actual.SequenceNumber);
        Assert.HasCount(expected.Transitions.Length, actual.Transitions, "Transition count differs.");
        for(int i = 0; i < expected.Transitions.Length; i++)
        {
            DatasetGraphTransition e = expected.Transitions[i];
            DatasetGraphTransition a = actual.Transitions[i];
            Assert.AreEqual(e.Graph, a.Graph, $"Transition {i} graph differs.");
            Assert.AreEqual(e.ParentRoot, a.ParentRoot, $"Transition {i} parent root differs.");
            Assert.AreEqual(e.ChildRoot, a.ChildRoot, $"Transition {i} child root differs.");
            Assert.IsTrue(e.Additions.AsSpan().SequenceEqual(a.Additions.AsSpan()), $"Transition {i} additions differ.");
            Assert.IsTrue(e.Removals.AsSpan().SequenceEqual(a.Removals.AsSpan()), $"Transition {i} removals differ.");
        }
    }

    /// <summary>Mints one term of every kind — named node, blank node, plain and language-tagged literal, a nested triple term, and an engine-minted node — into the dictionary in a fixed order.</summary>
    /// <param name="dictionary">The dictionary to mint into.</param>
    private static void MintSampleTerms(TermDictionary dictionary)
    {
        dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s")));
        dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/p")));
        dictionary.GetOrAdd(new BlankNode(Utf8Strings.From("b0")));
        dictionary.GetOrAdd(new Literal(Utf8Strings.From("plain"), new NamedNode(Utf8Strings.From("http://www.w3.org/2001/XMLSchema#string"))));
        dictionary.GetOrAdd(new Literal(Utf8Strings.From("hi"), new NamedNode(Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString")), Utf8Strings.From("en")));
        dictionary.GetOrAdd(new TripleTerm(
            new NamedNode(Utf8Strings.From("http://example.org/a")),
            new NamedNode(Utf8Strings.From("http://example.org/b")),
            new NamedNode(Utf8Strings.From("http://example.org/c"))));
        dictionary.GetOrAdd((RdfTerm)new EngineNode(EngineNodeFamily.Create(7), 11, 22, 33, 44));
    }

    /// <summary>A fresh journal over a new file is empty; sequential appends advance the head and assign dense sequence numbers; an append whose observed head no longer matches is rejected.</summary>
    [TestMethod]
    public async Task NewJournalIsEmptyAndSequentialAppendsAdvanceWhileAConcurrencyMismatchThrows()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            TermDictionary dictionary = new();
            using FileBackedDatasetJournal journal = Open(Path.Combine(directory, "journal.log"), dictionary, termPool, pool, TimeProvider.System);

            Assert.AreEqual(0, journal.Length);
            Assert.AreEqual(NodeIdentifier.Empty, journal.Head);
            Assert.IsNull(journal.RecoveryReport);
            Assert.IsTrue(journal.CommitmentFindings.IsEmpty);

            NodeIdentifier first = new(0xAAAA);
            NodeIdentifier second = new(0xBBBB);
            long s1 = await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            long s2 = await journal.AppendDelegate(MakeEntry(first, second), first, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(0L, s1);
            Assert.AreEqual(1L, s2);
            Assert.AreEqual(2, journal.Length);
            Assert.AreEqual(second, journal.Head);

            NodeIdentifier rogue = new(0xDEAD);
            await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
                await journal.AppendDelegate(MakeEntry(rogue, new NodeIdentifier(0xBEEF)), rogue, TestContext.CancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);

            Assert.AreEqual(2, journal.Length);
            Assert.AreEqual(second, journal.Head);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A mixed batch of Initial, Started, Committed, and Abandoned entries — exercising all three parent-root states (null=create, Empty=existed-empty, value) and a child-root drop across multiple graphs — round-trips byte-faithfully across a reopen, every field intact.</summary>
    [TestMethod]
    public async Task ReopenReplaysEveryEntryDurablyWithAllTransitionShapes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            DateTimeOffset pinned = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
            FakeTimeProvider clock = new(pinned);

            NodeIdentifier c1 = new(0x1111);
            NodeIdentifier c2 = new(0x2222);
            SessionId session = new(new Guid("0102030405060708090A0B0C0D0E0F10"));

            DatasetJournalEntry initial = new(
                ParentId: NodeIdentifier.Empty,
                ChildId: c1,
                EntryKind: EditSessionEntryKind.Initial,
                SessionId: null,
                EditCommitment: new NodeIdentifier(0x1234_5678_9ABC_DEF0UL),
                Transitions: [Transition(TermId.None, parentRoot: null, childRoot: new NodeIdentifier(0xAAAA), [EncodedTriple.FromEncoded(1, 2, 3)], [])],
                Timestamp: default,
                SequenceNumber: 0);
            DatasetJournalEntry started = DatasetJournalEntry.Started(c1, session);
            DatasetJournalEntry committed = new(
                ParentId: c1,
                ChildId: c2,
                EntryKind: EditSessionEntryKind.Committed,
                SessionId: session,
                EditCommitment: new NodeIdentifier(0x0FED_CBA9_8765_4321UL),
                Transitions:
                [
                    Transition(TermId.None, parentRoot: NodeIdentifier.Empty, childRoot: new NodeIdentifier(0xBBBB), [EncodedTriple.FromEncoded(4, 5, 6)], [EncodedTriple.FromEncoded(7, 8, 9)]),
                    Transition(TermId.FromEncoded(42), parentRoot: new NodeIdentifier(0xCCCC), childRoot: null, [], []),
                ],
                Timestamp: default,
                SequenceNumber: 0);
            DatasetJournalEntry abandoned = DatasetJournalEntry.Abandoned(c2, session);

            List<DatasetJournalEntry> before;
            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, clock))
            {
                await journal.AppendDelegate(initial, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
                await journal.AppendDelegate(started, c1, TestContext.CancellationToken).ConfigureAwait(false);
                await journal.AppendDelegate(committed, c1, TestContext.CancellationToken).ConfigureAwait(false);
                await journal.AppendDelegate(abandoned, c2, TestContext.CancellationToken).ConfigureAwait(false);
                before = await ReadAll(journal, TestContext.CancellationToken).ConfigureAwait(false);
            }

            using(FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, clock))
            {
                Assert.IsNull(reopened.RecoveryReport);
                Assert.AreEqual(4, reopened.Length);
                Assert.AreEqual(c2, reopened.Head);

                List<DatasetJournalEntry> after = await ReadAll(reopened, TestContext.CancellationToken).ConfigureAwait(false);
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

    /// <summary>The fork edge is refused at both gates: an append of a Forked-kind entry throws at the write boundary before any bytes reach the file and the journal stays usable, and a hand-crafted checksum-valid record whose kind byte is Forked is refused at construction rather than mis-decoded.</summary>
    [TestMethod]
    public async Task ForkedIsRejectedAtBothTheWriteAndReadGates()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            NodeIdentifier first = new(0xAAAA);

            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                DatasetJournalEntry forked = MakeEntry(NodeIdentifier.Empty, first) with { EntryKind = EditSessionEntryKind.Forked };
                await Assert.ThrowsExactlyAsync<ArgumentException>(
                    async () => await journal.AppendDelegate(forked, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

                Assert.AreEqual(0, journal.Length);
                Assert.AreEqual(NodeIdentifier.Empty, journal.Head);
                Assert.AreEqual(0L, new FileInfo(path).Length);

                //The rejected append left no bytes behind: an ordinary append still lands at sequence zero.
                long sequence = await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(0L, sequence);
                Assert.AreEqual(first, journal.Head);
            }

            //Hand-craft a checksum-valid record whose kind byte is Forked and confirm the read gate refuses it.
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            bytes[KindByteFileOffset] = (byte)EditSessionEntryKind.Forked;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A torn tail appended past the last intact record is truncated on replay; the log recovers through its last operation and names the discarded byte range, the tail is physically truncated, and a second reopen is clean.</summary>
    [TestMethod]
    public async Task TornTailRecoversToBoundaryAndTruncates()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await AppendChain(journal, 3, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] intact = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            byte[] torn = [.. intact, 1, 2, 3, 4, 5];
            await File.WriteAllBytesAsync(path, torn, TestContext.CancellationToken).ConfigureAwait(false);

            using(FileBackedDatasetJournal recovered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                Assert.AreEqual(3, recovered.Length);
                Assert.AreEqual(new NodeIdentifier(3UL), recovered.Head);
                UnrecoverableItemReport? report = recovered.RecoveryReport;
                Assert.IsNotNull(report);
                Assert.AreEqual(UnrecoverableItemReportKind.OperationRange, report.Kind);
                Assert.AreEqual(2L, report.RecoveredThroughSequence);
                Assert.AreEqual(5L, report.DiscardedByteCount);
            }

            Assert.AreEqual(intact.Length, new FileInfo(path).Length);
            using(FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
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

    /// <summary>A byte flipped in the last record recovers the operations before it and names the loss; a byte flipped in the very first record recovers nothing and names the loss with a -1 recovered-through sequence.</summary>
    [TestMethod]
    public async Task LastRecordCorruptionRecoversPriorAndFirstRecordCorruptionRecoversNothing()
    {
        using VeritasMemoryPool<byte> pool = new();
        using Utf8StringPool termPool = new();

        string lastDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(lastDirectory, "journal.log");
            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await AppendChain(journal, 3, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            bytes[^1] ^= 0xFF;
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedDatasetJournal recovered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            Assert.AreEqual(2, recovered.Length);
            Assert.AreEqual(new NodeIdentifier(2UL), recovered.Head);
            UnrecoverableItemReport? report = recovered.RecoveryReport;
            Assert.IsNotNull(report);
            Assert.AreEqual(1L, report.RecoveredThroughSequence);
            Assert.IsGreaterThan(0L, report.DiscardedByteCount);
        }
        finally
        {
            Directory.Delete(lastDirectory, recursive: true);
        }

        string firstDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(firstDirectory, "journal.log");
            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await AppendChain(journal, 3, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            long originalLength = bytes.Length;
            //Offset 6 is inside the first record's payload (past the 4-byte length prefix).
            bytes[6] ^= 0xFF;
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedDatasetJournal recovered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            Assert.AreEqual(0, recovered.Length);
            Assert.AreEqual(NodeIdentifier.Empty, recovered.Head);
            UnrecoverableItemReport? report = recovered.RecoveryReport;
            Assert.IsNotNull(report);
            Assert.AreEqual(-1L, report.RecoveredThroughSequence);
            Assert.AreEqual(originalLength, report.DiscardedByteCount);
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
        }
    }

    /// <summary>A checksum-valid record carrying an unsupported payload version is refused, not truncated; a checksum-valid record whose kind byte is out of range is a codec error refused as malformed — neither is silently shortened.</summary>
    [TestMethod]
    public async Task UnsupportedVersionAndMalformedKindAreRefusedNotTruncated()
    {
        using VeritasMemoryPool<byte> pool = new();
        using Utf8StringPool termPool = new();

        string versionDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(versionDirectory, "journal.log");
            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            long originalLength = bytes.Length;
            //The payload version byte sits just past the 4-byte length prefix; bump it and re-seal so it still verifies.
            bytes[4] = 99;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.ThrowsExactly<NotSupportedException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            });
            Assert.AreEqual(originalLength, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(versionDirectory, recursive: true);
        }

        string kindDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(kindDirectory, "journal.log");
            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            long originalLength = bytes.Length;
            bytes[KindByteFileOffset] = 99;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            });
            Assert.AreEqual(originalLength, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(kindDirectory, recursive: true);
        }
    }

    /// <summary>A garbage tail whose length prefix decodes to a huge value is bounded — treated as the recovery boundary without over-allocating — and the intact prefix is recovered.</summary>
    [TestMethod]
    public async Task HugeLengthPrefixTailIsBoundedAndRecovered()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await AppendChain(journal, 2, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] intact = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            //A maximal length prefix (0xFFFFFFFF) followed by two bytes: must be rejected by the bound, not allocated.
            byte[] withGarbageLength = [.. intact, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00];
            await File.WriteAllBytesAsync(path, withGarbageLength, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedDatasetJournal recovered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
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

    /// <summary>Every durable append flushes through the injected flush seam exactly once; a failing flush surfaces the failure and advances nothing, so a retry overwrites the failed bytes in place and lands cleanly with no duplicate record.</summary>
    [TestMethod]
    public async Task FlushSeamCountsPerAppendAndAFailingFlushLeavesTheStateRetryable()
    {
        using VeritasMemoryPool<byte> pool = new();
        using Utf8StringPool termPool = new();

        string countingDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(countingDirectory, "journal.log");
            RecordingFlush flush = new();
            using FileBackedDatasetJournal journal = new(path, new TermDictionary(), termPool, Hash, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, AtomicPublish.DefaultBarrier, flush.Flush);

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
            Directory.Delete(countingDirectory, recursive: true);
        }

        string failingDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(failingDirectory, "journal.log");
            FlakyFlush flush = new();
            using(FileBackedDatasetJournal journal = new(path, new TermDictionary(), termPool, Hash, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, AtomicPublish.DefaultBarrier, flush.Flush))
            {
                NodeIdentifier child = new(1UL);
                await Assert.ThrowsExactlyAsync<IOException>(
                    async () => await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, child), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

                Assert.AreEqual(0, journal.Length);
                Assert.AreEqual(NodeIdentifier.Empty, journal.Head);

                //The write offset did not advance, so the retry (now with a working flush) overwrites in place.
                long sequence = await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, child), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(0L, sequence);
                Assert.AreEqual(1, journal.Length);
                Assert.AreEqual(child, journal.Head);
            }

            using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            Assert.IsNull(reopened.RecoveryReport);
            Assert.AreEqual(1, reopened.Length);
        }
        finally
        {
            Directory.Delete(failingDirectory, recursive: true);
        }
    }

    /// <summary>The directory durability barrier is invoked exactly once, when the log file is first created, and not again on a reopen.</summary>
    [TestMethod]
    public void DurabilityBarrierIsInvokedOnFirstCreationOnly()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            string expectedDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();
            RecordingBarrier barrier = new();

            using(FileBackedDatasetJournal journal = new(path, new TermDictionary(), termPool, Hash, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, barrier.Flush))
            {
                Assert.AreEqual(1, barrier.CallCount);
                Assert.AreEqual(expectedDirectory, barrier.LastDirectory);
            }

            using(FileBackedDatasetJournal reopened = new(path, new TermDictionary(), termPool, Hash, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, barrier.Flush))
            {
                Assert.AreEqual(1, barrier.CallCount);
            }
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
            using Utf8StringPool termPool = new();
            using FileBackedDatasetJournal journal = Open(Path.Combine(directory, "journal.log"), new TermDictionary(), termPool, pool, TimeProvider.System);

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
                    DatasetJournalEntry entry = MakeEntry(currentHead, new NodeIdentifier(childValue));
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

    /// <summary>Terms minted before an append are carried in the record's term section, so reopening over a FRESH empty dictionary restores the dictionary exactly — the same count, the same identifiers, and value-equal terms of every kind.</summary>
    [TestMethod]
    public async Task TermSectionMakesTheLogSelfContainedAcrossAFreshDictionary()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            TermDictionary writeDictionary = new();
            MintSampleTerms(writeDictionary);

            using(Utf8StringPool writePool = new())
            using(FileBackedDatasetJournal journal = Open(path, writeDictionary, writePool, pool, TimeProvider.System))
            {
                DatasetJournalEntry entry = new(
                    ParentId: NodeIdentifier.Empty,
                    ChildId: new NodeIdentifier(0x5151),
                    EntryKind: EditSessionEntryKind.Initial,
                    SessionId: null,
                    EditCommitment: null,
                    Transitions: [Transition(TermId.None, parentRoot: null, childRoot: new NodeIdentifier(0x9999), [EncodedTriple.FromEncoded(1, 2, 4)], [])],
                    Timestamp: default,
                    SequenceNumber: 0);
                await journal.AppendDelegate(entry, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            TermDictionary readDictionary = new();
            using Utf8StringPool readPool = new();
            using FileBackedDatasetJournal reopened = Open(path, readDictionary, readPool, pool, TimeProvider.System);

            Assert.IsNull(reopened.RecoveryReport);
            Assert.AreEqual(1, reopened.Length);
            Assert.AreEqual(writeDictionary.Count, readDictionary.Count);
            for(uint id = 1; id <= (uint)writeDictionary.Count; id++)
            {
                Assert.AreEqual(writeDictionary.Resolve(id), readDictionary.Resolve(id), $"Term {id} did not round-trip.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A dictionary pre-loaded with a prefix of the log's terms verifies the overlap and restores the suffix; a dictionary that binds a shared identifier to a DIFFERENT term is a divergent history and is refused.</summary>
    [TestMethod]
    public async Task TermOverlapVerifiesAndDivergenceIsRefused()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            TermDictionary writeDictionary = new();
            MintSampleTerms(writeDictionary);
            using(Utf8StringPool writePool = new())
            using(FileBackedDatasetJournal journal = Open(path, writeDictionary, writePool, pool, TimeProvider.System))
            {
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(0x5151)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            //Overlap: the first three terms are already present and must verify; the suffix restores.
            TermDictionary overlap = new();
            overlap.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/s")));
            overlap.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/p")));
            overlap.GetOrAdd(new BlankNode(Utf8Strings.From("b0")));
            using(Utf8StringPool overlapPool = new())
            using(FileBackedDatasetJournal reopened = Open(path, overlap, overlapPool, pool, TimeProvider.System))
            {
                Assert.AreEqual(writeDictionary.Count, overlap.Count);
                for(uint id = 1; id <= (uint)writeDictionary.Count; id++)
                {
                    Assert.AreEqual(writeDictionary.Resolve(id), overlap.Resolve(id), $"Overlap term {id} did not round-trip.");
                }
            }

            //Divergence: identifier 1 is bound to a different term than the log's, so the histories disagree.
            TermDictionary divergent = new();
            divergent.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/DIFFERENT")));
            using Utf8StringPool divergentPool = new();
            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, divergent, divergentPool, pool, TimeProvider.System);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A tampered transition inside a checksum-valid dataset record opens with a commitment finding naming that sequence and the disagreeing fingerprints; an untampered log opens with no findings.</summary>
    [TestMethod]
    public async Task TamperedTransitionSurfacesADatasetCommitmentFinding()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            ImmutableArray<DatasetGraphTransition> transitions =
            [
                Transition(TermId.None, parentRoot: null, childRoot: new NodeIdentifier(0x7777), [EncodedTriple.FromEncoded(0xDEADBEEF, 0x11223344, 0x55667788)], []),
            ];
            DatasetJournalEntry committed = DatasetJournalEntry.Committed(Hash, NodeIdentifier.Empty, new NodeIdentifier(0xC1C1), SessionId.NewId(), transitions);

            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                await journal.AppendDelegate(committed, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            using(FileBackedDatasetJournal untampered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                Assert.IsTrue(untampered.CommitmentFindings.IsEmpty, "An untampered dataset log reported a commitment finding.");
                Assert.AreEqual(1, untampered.Length);
            }

            //Flip a byte of the addition's distinctive subject id (0xDEADBEEF, little-endian) and re-seal the record.
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            int subjectOffset = FindPattern(bytes, [0xEF, 0xBE, 0xAD, 0xDE]);
            Assert.IsGreaterThanOrEqualTo(0, subjectOffset, "The distinctive addition subject was not found in the record.");
            bytes[subjectOffset] ^= 0xFF;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            using FileBackedDatasetJournal tampered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            Assert.IsNull(tampered.RecoveryReport, "A re-sealed tampered record must open, not truncate.");
            Assert.AreEqual(1, tampered.Length);
            Assert.HasCount(1, tampered.CommitmentFindings);
            JournalCommitmentFinding finding = tampered.CommitmentFindings[0];
            Assert.AreEqual(0L, finding.SequenceNumber);
            Assert.AreEqual(committed.EditCommitment!.Value, finding.Stored);
            Assert.AreNotEqual(finding.Stored, finding.Recomputed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The per-store journal opened with a hash verifies each committing entry's commitment on replay: a tampered, re-sealed addition surfaces a finding, while the null-hash constructor performs no verification and reports no findings.</summary>
    [TestMethod]
    public async Task PerStoreCommitmentVerificationIsOptAndFindsATamperedAddition()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();

            ImmutableArray<EncodedTriple> additions = [EncodedTriple.FromEncoded(0xDEADBEEF, 0x11223344, 0x55667788)];
            JournalEntry committed = JournalEntry.Committed(Hash, NodeIdentifier.Empty, new NodeIdentifier(0xC1C1), SessionId.NewId(), additions, []);

            using(FileBackedJournal journal = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                await journal.AppendDelegate(committed, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            int subjectOffset = FindPattern(bytes, [0xEF, 0xBE, 0xAD, 0xDE]);
            Assert.IsGreaterThanOrEqualTo(0, subjectOffset, "The distinctive addition subject was not found in the record.");
            bytes[subjectOffset] ^= 0xFF;
            RecomputeSingleRecordChecksum(bytes, ChecksumAlgorithm.XxHash3);
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            //The null-hash reopen performs no verification: the SAME tampered file that yields a finding under
            //the verifying constructor below reports none here — the opt-out genuinely skips the recomputation.
            using(FileBackedJournal noHash = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool))
            {
                Assert.IsTrue(noHash.CommitmentFindings.IsEmpty, "The null-hash constructor must not verify commitments.");
                Assert.AreEqual(1, noHash.Length);
            }

            using FileBackedJournal verifying = new(path, ChecksumAlgorithm.XxHash3, TimeProvider.System, pool, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush, Hash);
            Assert.IsNull(verifying.RecoveryReport, "A re-sealed tampered record must open, not truncate.");
            Assert.AreEqual(1, verifying.Length);
            Assert.HasCount(1, verifying.CommitmentFindings);
            JournalCommitmentFinding finding = verifying.CommitmentFindings[0];
            Assert.AreEqual(0L, finding.SequenceNumber);
            Assert.AreEqual(committed.EditCommitment!.Value, finding.Stored);
            Assert.AreNotEqual(finding.Stored, finding.Recomputed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Parallel minting of overlapping term sets converges to the distinct total under the dictionary's internal lock, and every term resolves bidirectionally to its assigned identifier.</summary>
    [TestMethod]
    public void ConcurrentMintingConvergesToTheDistinctTotal()
    {
        const int distinctTerms = 256;
        RdfTerm[] terms = new RdfTerm[distinctTerms];
        for(int i = 0; i < distinctTerms; i++)
        {
            terms[i] = new NamedNode(Utf8Strings.From($"http://example.org/t{i}"));
        }

        TermDictionary dictionary = new();
        Parallel.For(0, 16, worker =>
        {
            //Each worker mints the whole set, offset so the workers race the same identifiers from different
            //starting points; the outcome is order-independent because interning is idempotent.
            for(int step = 0; step < distinctTerms; step++)
            {
                int index = (step + (worker * 17)) % distinctTerms;
                _ = dictionary.GetOrAdd(terms[index]);
            }
        });

        Assert.AreEqual(distinctTerms, dictionary.Count);
        for(int i = 0; i < distinctTerms; i++)
        {
            TermId id = dictionary.GetIdOrDefault(terms[i]);
            Assert.AreNotEqual(TermId.None, id, $"Term {i} did not resolve to an identifier.");
            Assert.AreEqual(terms[i], dictionary.Resolve(id), $"Term {i} did not resolve bidirectionally.");
            Assert.AreEqual(id, dictionary.GetOrAdd(terms[i]), $"Term {i} minted a second identifier.");
        }
    }

    /// <summary>A v2 header round-trips every field it carries, with and without an anchor; the reader recovers the anchor, replication epoch, and attach term watermark exactly.</summary>
    [TestMethod]
    public void HeaderRoundTripsEveryField()
    {
        byte[] withAnchor = new byte[DatasetJournalHeader.Size];
        NodeIdentifier anchor = new(0x0123_4567_89AB_CDEFUL);
        DatasetJournalHeader.Write(withAnchor, anchor, replicationEpoch: 0xFEED_BEEF_1234_5678UL, attachTermWatermark: 4242, ChecksumAlgorithm.XxHash3);

        DatasetJournalHeaderInfo info = DatasetJournalHeader.Read(withAnchor, resolveChecksum: null);
        Assert.IsTrue(info.IsV2);
        Assert.AreEqual(anchor, info.Anchor);
        Assert.AreEqual(0xFEED_BEEF_1234_5678UL, info.ReplicationEpoch);
        Assert.AreEqual(4242, info.AttachTermWatermark);
        Assert.AreEqual(DatasetJournalHeader.Size, info.HeaderLength, "A v1.0 header's true on-disk length is the fixed v1.0 size.");
        Assert.AreEqual(ChecksumAlgorithm.XxHash3.Id, info.RecordStreamChecksumId);

        //A create-path header carries no anchor: an absent anchor round-trips to Empty, distinct from a present zero.
        byte[] noAnchor = new byte[DatasetJournalHeader.Size];
        DatasetJournalHeader.Write(noAnchor, NodeIdentifier.Empty, replicationEpoch: 7UL, attachTermWatermark: 0, ChecksumAlgorithm.XxHash3);
        DatasetJournalHeaderInfo createInfo = DatasetJournalHeader.Read(noAnchor, resolveChecksum: null);
        Assert.IsTrue(createInfo.IsV2);
        Assert.AreEqual(NodeIdentifier.Empty, createInfo.Anchor);
        Assert.AreEqual(7UL, createInfo.ReplicationEpoch);
        Assert.AreEqual(0, createInfo.AttachTermWatermark);
    }

    /// <summary>A corrupt magic and a corrupt payload byte are both refused: the magic mismatch is loud, and a flipped payload byte fails the self-checksum rather than being read as truth.</summary>
    [TestMethod]
    public void HeaderMagicAndSelfChecksumCorruptionAreRefused()
    {
        byte[] magicCorrupt = new byte[DatasetJournalHeader.Size];
        DatasetJournalHeader.Write(magicCorrupt, new NodeIdentifier(0xAAAA), 1UL, 0, ChecksumAlgorithm.XxHash3);
        //Flip a magic byte other than the discriminator, so the file is still recognised as a v2 file but its magic
        //no longer matches.
        magicCorrupt[0] ^= 0xFF;
        Assert.ThrowsExactly<InvalidDataException>(() => DatasetJournalHeader.Read(magicCorrupt, resolveChecksum: null));

        byte[] payloadCorrupt = new byte[DatasetJournalHeader.Size];
        DatasetJournalHeader.Write(payloadCorrupt, new NodeIdentifier(0xAAAA), 0x99UL, 0, ChecksumAlgorithm.XxHash3);
        //Flip a byte inside the epoch field: the self-checksum, verified before any field is trusted, catches it.
        payloadCorrupt[ReplicationEpochByteOffset] ^= 0xFF;
        Assert.ThrowsExactly<InvalidDataException>(() => DatasetJournalHeader.Read(payloadCorrupt, resolveChecksum: null));
    }

    /// <summary>A header written under a foreign major version is refused as unsupported, before the self-checksum is even consulted.</summary>
    [TestMethod]
    public void ForeignHeaderMajorVersionIsRefused()
    {
        byte[] header = new byte[DatasetJournalHeader.Size];
        DatasetJournalHeader.Write(header, NodeIdentifier.Empty, 1UL, 0, ChecksumAlgorithm.XxHash3);
        //The major version byte follows the 8-byte magic.
        header[8] = 2;
        Assert.ThrowsExactly<NotSupportedException>(() => DatasetJournalHeader.Read(header, resolveChecksum: null));
    }

    /// <summary>A v2 log whose header records a record-stream checksum algorithm the default resolver does not know (a keyed id) is REFUSED, not truncated: the file survives intact so a resolver that can read it later still can.</summary>
    [TestMethod]
    public void UnresolvableRecordStreamAlgorithmIsRefusedNotTruncated()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            //Stamp a header naming the reserved keyed id 3 (the default resolver declines it); its compute is never
            //invoked here, only its id is recorded, so a placeholder keyed-marked algorithm suffices — the
            //reserved id is constructible only through the keyed factory.
            ChecksumAlgorithm keyed = ChecksumAlgorithm.CreateKeyed(ChecksumAlgorithm.KeyedHmacSha256Id, "keyed-test", ChecksumAlgorithm.ReservedKeyedByteWidth, static (_, destination) => destination.Clear());
            byte[] header = new byte[DatasetJournalHeader.Size];
            DatasetJournalHeader.Write(header, NodeIdentifier.Empty, 5UL, 0, keyed);
            File.WriteAllBytes(path, header);
            long originalLength = header.Length;

            Assert.ThrowsExactly<NotSupportedException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            });
            Assert.AreEqual(originalLength, new FileInfo(path).Length, "An unresolvable record-stream algorithm must refuse, not truncate.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A header carrying a higher minor version and a longer payload than this build knows still opens: the reader consumes the fields it knows and skips the trailing minor-added bytes to reach the self-checksum, reporting the header's TRUE on-disk length.</summary>
    [TestMethod]
    public void TrailingMinorPayloadStillOpens()
    {
        const int extraPayload = 4;
        byte[] header = BuildMinorExtendedHeader(extraPayload, new NodeIdentifier(0xC0FFEEUL), replicationEpoch: 0x42UL, attachTermWatermark: 9);

        DatasetJournalHeaderInfo info = DatasetJournalHeader.Read(header, resolveChecksum: null);
        Assert.IsTrue(info.IsV2);
        Assert.AreEqual(new NodeIdentifier(0xC0FFEEUL), info.Anchor);
        Assert.AreEqual(0x42UL, info.ReplicationEpoch);
        Assert.AreEqual(9, info.AttachTermWatermark);
        Assert.AreEqual(DatasetJournalHeader.Size + extraPayload, info.HeaderLength, "The record stream begins after the DECLARED payload, not the fixed v1.0 size.");
    }

    /// <summary>A same-major, higher-minor header with a LONGER declared payload keeps the whole log intact: the record scan starts at the header's true on-disk length, records after it replay, an append lands at that offset, and no truncation eats into the extended header.</summary>
    [TestMethod]
    public async Task HigherMinorHeaderWithALongerPayloadKeepsRecordsAndAppendsAtTheTrueOffset()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            const int extraPayload = 4;
            int headerLength = DatasetJournalHeader.Size + extraPayload;
            NodeIdentifier anchor = new(0xA11CUL);
            NodeIdentifier child = new(0xB0B0UL);
            await File.WriteAllBytesAsync(path, BuildMinorExtendedHeader(extraPayload, anchor, replicationEpoch: 0x42UL, attachTermWatermark: 0), TestContext.CancellationToken).ConfigureAwait(false);

            using(FileBackedDatasetJournal journal = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                Assert.IsTrue(journal.Header.IsV2);
                Assert.AreEqual(headerLength, journal.Header.HeaderLength, "The record stream begins at the true on-disk header length, not the fixed v1.0 size.");
                Assert.IsNull(journal.RecoveryReport, "An extended header with no records is not a torn tail.");
                Assert.AreEqual(headerLength, (int)new FileInfo(path).Length, "Nothing of the extended header was truncated.");

                //The attached head is the anchor; the first append opens against it and must land at the true offset.
                await journal.AppendDelegate(MakeEntry(anchor, child), anchor, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsTrue(
                DatasetJournalRecordFormat.TryReadRecord(bytes.AsSpan(headerLength), ChecksumAlgorithm.XxHash3, termPool, out DatasetJournalRecord record, out _),
                "The appended record sits immediately after the extended header.");
            Assert.AreEqual(child, record.Entry.ChildId);

            //A reopen replays the record from the true offset and truncates nothing — the regression this pins was a
            //scan at the v1.0 size landing inside the self-checksum and truncating every acked record away.
            using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            Assert.IsNull(reopened.RecoveryReport);
            Assert.AreEqual(1, reopened.Length);
            Assert.AreEqual(child, reopened.Head);
            Assert.AreEqual(bytes.Length, (int)new FileInfo(path).Length, "The reopen truncated nothing.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A crafted headerless log (records written at offset 0 by the v1 constructor) opens as v1: no header, records at offset 0, and a term-watermark chain that starts at 0.</summary>
    [TestMethod]
    public async Task HeaderlessLogOpensAsV1()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            TermDictionary writeDictionary = new();
            MintSampleTerms(writeDictionary);
            using(Utf8StringPool writePool = new())
            using(FileBackedDatasetJournal journal = Open(path, writeDictionary, writePool, pool, TimeProvider.System))
            {
                Assert.IsFalse(journal.Header.IsV2, "The v1 constructor writes no header on a fresh file.");
                await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(0x5151)), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
            }

            //The file carries no v2 discriminator: its byte 3 is part of the first record's length prefix, always < 0x80.
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            Assert.IsLessThan((byte)0x80, bytes[DatasetJournalHeader.DiscriminatorIndex], "A v1 log's byte 3 is a length-prefix byte, never the v2 discriminator.");

            TermDictionary readDictionary = new();
            using Utf8StringPool readPool = new();
            using FileBackedDatasetJournal reopened = Open(path, readDictionary, readPool, pool, TimeProvider.System);
            Assert.IsFalse(reopened.Header.IsV2);
            Assert.AreEqual(NodeIdentifier.Empty, reopened.HeaderAnchor);
            Assert.AreEqual(1, reopened.Length);
            Assert.AreEqual(writeDictionary.Count, readDictionary.Count, "A v1 log restores its terms from a watermark-0 chain.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An attached log seeds its term-watermark chain from the header's attach watermark W, so the first post-attach record captures only the terms minted beyond W; a header whose watermark does not match the first record is refused.</summary>
    [TestMethod]
    public async Task AttachedLogCapturesOnlyTermsBeyondTheAttachWatermark()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            //Pre-load the dictionary with the "generation" terms, then attach at that watermark.
            TermDictionary dictionary = new();
            MintSampleTerms(dictionary);
            int attachWatermark = dictionary.Count;
            NodeIdentifier anchor = new(0xA11C);

            using(FileBackedDatasetJournal journal = OpenV2(path, dictionary, termPool, pool, anchor, attachWatermark))
            {
                Assert.IsTrue(journal.Header.IsV2);
                Assert.AreEqual(anchor, journal.HeaderAnchor);

                //Mint two terms beyond the attach watermark, then append the first post-attach record. An attached
                //log's head is the anchor it continues from, so the first commit opens against the anchor.
                dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/post1")));
                dictionary.GetOrAdd(new NamedNode(Utf8Strings.From("http://example.org/post2")));
                await journal.AppendDelegate(MakeEntry(anchor, new NodeIdentifier(0xB0B0)), anchor, TestContext.CancellationToken).ConfigureAwait(false);
            }

            //The first record sits after the header and captures only the two terms minted beyond the watermark.
            byte[] bytes = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            using(Utf8StringPool decodePool = new())
            {
                bool read = DatasetJournalRecordFormat.TryReadRecord(bytes.AsSpan(DatasetJournalHeader.Size), ChecksumAlgorithm.XxHash3, decodePool, out DatasetJournalRecord record, out _);
                Assert.IsTrue(read, "The first post-attach record must sit immediately after the header.");
                Assert.AreEqual(attachWatermark, record.TermWatermark, "The first record continues the header's attach watermark.");
                Assert.HasCount(2, record.NewTerms, "The first attached append captures only the terms minted beyond the attach watermark, not the whole generation.");
            }

            //A header whose attach watermark disagrees with the first record breaks the continuity chain and is refused.
            int watermarkOffset = DatasetJournalHeader.Size - sizeof(ulong) - sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(watermarkOffset), attachWatermark + 5);
            int checksumOffset = DatasetJournalHeader.Size - sizeof(ulong);
            ChecksumAlgorithm.XxHash3.Compute(bytes.AsSpan(0, checksumOffset), bytes.AsSpan(checksumOffset, sizeof(ulong)));
            await File.WriteAllBytesAsync(path, bytes, TestContext.CancellationToken).ConfigureAwait(false);

            using Utf8StringPool reopenPool = new();
            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), reopenPool, pool, TimeProvider.System);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A torn tail in a v2 log truncates only to a record boundary at or beyond the header, never into the header; a file shorter than a whole header is refused, since a torn creation acked nothing.</summary>
    [TestMethod]
    public async Task V2TornTailTruncatesAboveTheHeaderAndATornHeaderIsRefused()
    {
        string tailDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(tailDirectory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            //A create-path v2 header (Empty anchor) leaves the head empty, so the append chain opens from the empty head.
            using(FileBackedDatasetJournal journal = OpenV2(path, new TermDictionary(), termPool, pool, NodeIdentifier.Empty, attachTermWatermark: 0))
            {
                await AppendChain(journal, 2, TestContext.CancellationToken).ConfigureAwait(false);
            }

            byte[] intact = await File.ReadAllBytesAsync(path, TestContext.CancellationToken).ConfigureAwait(false);
            byte[] torn = [.. intact, 9, 9, 9, 9];
            await File.WriteAllBytesAsync(path, torn, TestContext.CancellationToken).ConfigureAwait(false);

            using(FileBackedDatasetJournal recovered = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                Assert.AreEqual(2, recovered.Length);
                Assert.IsNotNull(recovered.RecoveryReport);
                Assert.AreEqual(4L, recovered.RecoveryReport!.DiscardedByteCount);
            }

            long recoveredLength = new FileInfo(path).Length;
            Assert.AreEqual(intact.Length, recoveredLength);
            Assert.IsGreaterThanOrEqualTo((long)DatasetJournalHeader.Size, recoveredLength, "The truncation never eats into the header.");
        }
        finally
        {
            Directory.Delete(tailDirectory, recursive: true);
        }

        string headerDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(headerDirectory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            using(FileBackedDatasetJournal journal = OpenV2(path, new TermDictionary(), termPool, pool, NodeIdentifier.Empty, attachTermWatermark: 0))
            {
                //Just the header, no records.
            }

            //Truncate the header itself: a torn creation acked nothing, so the file is refused rather than served.
            using(FileStream fs = new(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                fs.SetLength(DatasetJournalHeader.Size - 10);
            }

            using Utf8StringPool reopenPool = new();
            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), reopenPool, pool, TimeProvider.System);
            });
        }
        finally
        {
            Directory.Delete(headerDirectory, recursive: true);
        }

        string fragmentDirectory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(fragmentDirectory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            //A crash before the discriminator byte lands leaves fewer than 4 bytes. On the v2 open path the fragment
            //is refused deterministically — never mistaken for a v1 log and truncated to nothing.
            await File.WriteAllBytesAsync(path, [(byte)'V', (byte)'T'], TestContext.CancellationToken).ConfigureAwait(false);
            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = OpenV2(path, new TermDictionary(), termPool, pool, NodeIdentifier.Empty, attachTermWatermark: 0);
            });
            Assert.AreEqual(2L, new FileInfo(path).Length, "The v2 refusal leaves the fragment untouched.");

            //The plain v1 open path keeps its truncate-to-boundary semantics over the same bytes, as today.
            using(FileBackedDatasetJournal v1 = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System))
            {
                Assert.AreEqual(0, v1.Length);
                Assert.IsNotNull(v1.RecoveryReport, "The v1 open names the fragment as a torn tail.");
            }

            Assert.AreEqual(0L, new FileInfo(path).Length, "The v1 open truncates the fragment away.");
        }
        finally
        {
            Directory.Delete(fragmentDirectory, recursive: true);
        }
    }

    /// <summary>A v2 header that records a DIFFERENT (but resolvable) record-stream algorithm than the journal frames under is refused loudly, not mis-framed: a narrower checksum over wider records would fail every record's verify, read as a clean torn tail, and silently truncate acked history.</summary>
    [TestMethod]
    public void MismatchedRecordStreamAlgorithmIdIsRefusedNotMisFramed()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "journal.log");
            using VeritasMemoryPool<byte> pool = new();
            using Utf8StringPool termPool = new();

            //The header names CRC-32 (id 2, resolvable under the default resolver); the journal frames under XxHash3.
            byte[] header = new byte[DatasetJournalHeader.Size];
            DatasetJournalHeader.Write(header, NodeIdentifier.Empty, 5UL, 0, ChecksumAlgorithm.Crc32);
            File.WriteAllBytes(path, header);
            long originalLength = header.Length;

            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                using FileBackedDatasetJournal reopened = Open(path, new TermDictionary(), termPool, pool, TimeProvider.System);
            });
            Assert.AreEqual(originalLength, new FileInfo(path).Length, "The algorithm disagreement refuses, never truncates.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The known (v1.0) payload size a v2 header declares: record-stream id (1) + anchor presence (1) + anchor (8) + replication epoch (8) + attach term watermark (4).</summary>
    private const int HeaderKnownPayloadSize = sizeof(byte) + sizeof(byte) + sizeof(ulong) + sizeof(ulong) + sizeof(int);

    /// <summary>Builds a v2 header carrying a HIGHER minor version and <paramref name="extraPayload"/> unknown trailing payload bytes — the sealed-journal minor-evolution shape this build must read by skipping to the self-checksum via the declared payload length. The v1.0 header layout is an 8-byte magic, major+minor (2), a u16 payload length, the known payload, and an 8-byte self-checksum; the extension appends payload bytes before the checksum.</summary>
    /// <param name="extraPayload">The number of minor-added payload bytes this build does not know.</param>
    /// <param name="anchor">The onboarding anchor, or <see cref="NodeIdentifier.Empty"/> for no anchor.</param>
    /// <param name="replicationEpoch">The dictionary replication epoch to stamp.</param>
    /// <param name="attachTermWatermark">The attach term watermark to stamp.</param>
    /// <returns>The sealed extended-header bytes.</returns>
    private static byte[] BuildMinorExtendedHeader(int extraPayload, NodeIdentifier anchor, ulong replicationEpoch, int attachTermWatermark)
    {
        byte[] header = new byte[DatasetJournalHeader.Size + extraPayload];
        ReadOnlySpan<byte> magic = [(byte)'V', (byte)'T', (byte)'D', DatasetJournalHeader.DiscriminatorByte, (byte)'J', (byte)'R', (byte)'N', (byte)'L'];
        magic.CopyTo(header);
        header[8] = 1;   //Major.
        header[9] = 1;   //A higher minor.
        int payloadLength = HeaderKnownPayloadSize + extraPayload;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)payloadLength);
        header[12] = ChecksumAlgorithm.XxHash3.Id;
        header[13] = anchor != NodeIdentifier.Empty ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(14), anchor.Value);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(22), replicationEpoch);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(30), attachTermWatermark);
        //Bytes [34, 34+extraPayload) are minor-added payload this build does not know; make them non-zero.
        header.AsSpan(34, extraPayload).Fill(0xAB);
        int checksumOffset = DatasetJournalHeader.PreambleSize + payloadLength;
        ChecksumAlgorithm.XxHash3.Compute(header.AsSpan(0, checksumOffset), header.AsSpan(checksumOffset, sizeof(ulong)));

        return header;
    }

    /// <summary>The byte offset of the replication epoch within a v2 header: the 12-byte preamble, then the record-stream id (1) and anchor presence (1) and anchor (8).</summary>
    private const int ReplicationEpochByteOffset = 12 + sizeof(byte) + sizeof(byte) + sizeof(ulong);

    /// <summary>The file offset of a single record's kind byte: the 4-byte length prefix, then the payload's version (1) and sequence (8) fields.</summary>
    private const int KindByteFileOffset = sizeof(uint) + sizeof(byte) + sizeof(long);

    /// <summary>Re-seals a single-record file: recomputes the record's checksum over its length prefix and payload after the bytes were mutated, so the record verifies and replay reaches the decode path.</summary>
    /// <param name="record">The single-record file bytes, mutated in place.</param>
    /// <param name="checksum">The record checksum algorithm.</param>
    private static void RecomputeSingleRecordChecksum(byte[] record, ChecksumAlgorithm checksum)
    {
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(record);
        int checksummedLength = sizeof(uint) + (int)payloadLength;
        checksum.Compute(record.AsSpan(0, checksummedLength), record.AsSpan(checksummedLength, checksum.ByteWidth));
    }

    /// <summary>Finds the first index of <paramref name="pattern"/> in <paramref name="data"/>.</summary>
    /// <param name="data">The bytes to search.</param>
    /// <param name="pattern">The byte pattern to find.</param>
    /// <returns>The first matching index, or -1 when the pattern is absent.</returns>
    private static int FindPattern(byte[] data, byte[] pattern)
    {
        for(int i = 0; i + pattern.Length <= data.Length; i++)
        {
            if(data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Wraps an append to swallow concurrency conflicts and report a nullable sequence number — for the concurrent-append race where most contenders lose.</summary>
    /// <param name="journal">The journal.</param>
    /// <param name="entry">The entry to append.</param>
    /// <param name="expectedHead">The head the contender observed.</param>
    /// <returns>The assigned sequence number, or <see langword="null"/> when the contender lost the race.</returns>
    private static async Task<long?> TryAppendAsync(FileBackedDatasetJournal journal, DatasetJournalEntry entry, NodeIdentifier expectedHead)
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

    /// <summary>A test directory durability barrier that records how often it was invoked and the last directory it flushed.</summary>
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

    /// <summary>A test file-content durability flush that records how often it was invoked and still flushes durably through the production default.</summary>
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

    /// <summary>A test file-content durability flush that fails its first invocation and then flushes durably — the injected transient <c>fsync</c> failure a retry recovers from.</summary>
    private sealed class FlakyFlush
    {
        /// <summary>The number of times the flush was invoked; a naked field because it is reassigned and needs no external contract.</summary>
        private int calls;

        /// <summary>Fails the first invocation, then performs the production durable flush; matches the <see cref="DurableFlushDelegate"/> shape.</summary>
        /// <param name="handle">The open handle to the written file.</param>
        /// <exception cref="IOException">The first invocation, a simulated transient flush failure.</exception>
        public void Flush(SafeFileHandle handle)
        {
            calls++;
            if(calls == 1)
            {
                throw new IOException("Injected first-flush failure (a simulated transient fsync error).");
            }

            AtomicPublish.DefaultFlush(handle);
        }
    }
}
