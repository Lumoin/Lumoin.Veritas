using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class InMemoryJournalTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void NewJournalIsEmpty()
    {
        InMemoryJournal journal = new();

        Assert.AreEqual(0, journal.Length);
        Assert.AreEqual(NodeIdentifier.Empty, journal.Head);
    }

    [TestMethod]
    public async Task FirstAppendAssignsSequenceZero()
    {
        InMemoryJournal journal = new();
        NodeIdentifier child = new(0xAAAA);
        JournalEntry entry = MakeEntry(NodeIdentifier.Empty, child, sequence: 999);

        long sequence = await journal.AppendDelegate(entry, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0L, sequence);
        Assert.AreEqual(1, journal.Length);
        Assert.AreEqual(child, journal.Head);
    }

    [TestMethod]
    public async Task SequentialAppendsAdvanceHeadAndSequence()
    {
        InMemoryJournal journal = new();
        NodeIdentifier first = new(0xAAAA);
        NodeIdentifier second = new(0xBBBB);
        NodeIdentifier third = new(0xCCCC);

        long s1 = await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);
        long s2 = await journal.AppendDelegate(MakeEntry(first, second), first, TestContext.CancellationToken).ConfigureAwait(false);
        long s3 = await journal.AppendDelegate(MakeEntry(second, third), second, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0L, s1);
        Assert.AreEqual(1L, s2);
        Assert.AreEqual(2L, s3);

        Assert.AreEqual(3, journal.Length);
        Assert.AreEqual(third, journal.Head);
    }

    [TestMethod]
    public async Task AppendWithMismatchedExpectedHeadThrows()
    {
        InMemoryJournal journal = new();
        NodeIdentifier first = new(0xAAAA);
        NodeIdentifier rogue = new(0xDEAD);

        await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, first), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        EditSessionConcurrencyException thrown = await Assert.ThrowsAsync<EditSessionConcurrencyException>(async () =>
            await journal.AppendDelegate(MakeEntry(rogue, new NodeIdentifier(0xBEEF)), rogue, TestContext.CancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);

        //Read the heads into locals so the analyzer does not
        //heuristically see "ExpectedHead" next to a non-expected
        //argument position and warn about argument order.
        NodeIdentifier reportedExpected = thrown.ExpectedHead;
        NodeIdentifier reportedActual = thrown.ActualHead;
        Assert.AreEqual(rogue, reportedExpected);
        Assert.AreEqual(first, reportedActual);

        //State unchanged: head still 'first', length still 1.
        Assert.AreEqual(1, journal.Length);
        Assert.AreEqual(first, journal.Head);
    }

    [TestMethod]
    public async Task AppendOverwritesProvidedSequenceNumber()
    {
        //Caller-supplied SequenceNumber is ignored; the journal
        //assigns a monotonic one. This is the contract that
        //prevents callers from forging sequence numbers.
        InMemoryJournal journal = new();
        JournalEntry entry = MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL), sequence: 777);

        long assigned = await journal.AppendDelegate(entry, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0L, assigned);

        JournalEntry stored = await ReadFirst(journal).ConfigureAwait(false);
        Assert.AreEqual(0L, stored.SequenceNumber);
    }

    [TestMethod]
    public async Task AppendOverwritesProvidedTimestampFromTimeProvider()
    {
        //The journal owns Timestamp the same way it owns
        //SequenceNumber: the caller's value is a placeholder, the
        //journal stamps the real value from its TimeProvider.
        DateTimeOffset pinned = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        FakeTimeProvider clock = new(pinned);
        InMemoryJournal journal = new(clock);

        JournalEntry entry = MakeEntry(NodeIdentifier.Empty, new NodeIdentifier(1UL));

        await journal.AppendDelegate(entry, NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        JournalEntry stored = await ReadFirst(journal).ConfigureAwait(false);
        Assert.AreEqual(pinned, stored.Timestamp);
    }

    [TestMethod]
    public async Task ReadFromZeroEmitsAllEntriesInOrder()
    {
        InMemoryJournal journal = new();
        NodeIdentifier prev = NodeIdentifier.Empty;
        for(ulong i = 1; i <= 5; i++)
        {
            NodeIdentifier next = new(i);
            await journal.AppendDelegate(MakeEntry(prev, next), prev, TestContext.CancellationToken).ConfigureAwait(false);
            prev = next;
        }

        List<JournalEntry> entries = [];
        await foreach(JournalEntry entry in journal.ReadDelegate(0L, TestContext.CancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        Assert.HasCount(5, entries);
        for(int i = 0; i < entries.Count; i++)
        {
            Assert.AreEqual((long)i, entries[i].SequenceNumber);
        }
    }

    [TestMethod]
    public async Task ReadFromNonZeroSkipsLowerSequences()
    {
        InMemoryJournal journal = new();
        NodeIdentifier prev = NodeIdentifier.Empty;
        for(ulong i = 1; i <= 5; i++)
        {
            NodeIdentifier next = new(i);
            await journal.AppendDelegate(MakeEntry(prev, next), prev, TestContext.CancellationToken).ConfigureAwait(false);
            prev = next;
        }

        List<JournalEntry> entries = [];
        await foreach(JournalEntry entry in journal.ReadDelegate(2L, TestContext.CancellationToken).ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        Assert.HasCount(3, entries);
        Assert.AreEqual(2L, entries[0].SequenceNumber);
        Assert.AreEqual(3L, entries[1].SequenceNumber);
        Assert.AreEqual(4L, entries[2].SequenceNumber);
    }

    [TestMethod]
    public async Task ReadOnEmptyJournalYieldsNothing()
    {
        InMemoryJournal journal = new();

        int seen = 0;
        await foreach(JournalEntry _ in journal.ReadDelegate(0L, TestContext.CancellationToken).ConfigureAwait(false))
        {
            seen++;
        }

        Assert.AreEqual(0, seen);
    }

    [TestMethod]
    public async Task ConcurrentAppendsLinearise()
    {
        //Many tasks racing to append against the same head — only
        //one wins per round, the rest see their parent become
        //stale and throw EditSessionConcurrencyException. After the
        //dust settles, exactly the number of "won" appends shows
        //up in the journal, sequence numbers are dense, and the
        //chain of parent->child is unbroken.
        InMemoryJournal journal = new();

        //Pre-seed the journal with one entry so racers all know
        //the same starting parent.
        NodeIdentifier seedChild = new(0xFEED);
        await journal.AppendDelegate(MakeEntry(NodeIdentifier.Empty, seedChild), NodeIdentifier.Empty, TestContext.CancellationToken).ConfigureAwait(false);

        const int rounds = 5;
        const int contendersPerRound = 8;

        for(int round = 0; round < rounds; round++)
        {
            NodeIdentifier currentHead = journal.Head;
            Task<long?>[] attempts = new Task<long?>[contendersPerRound];

            for(int i = 0; i < contendersPerRound; i++)
            {
                ulong childValue = ((ulong)round << 32) | (ulong)i + 1UL;
                JournalEntry entry = MakeEntry(currentHead, new NodeIdentifier(childValue));
                attempts[i] = TryAppendAsync(journal, entry, currentHead);
            }

            long?[] results = await Task.WhenAll(attempts).ConfigureAwait(false);
            int successes = results.Count(r => r.HasValue);

            Assert.AreEqual(1, successes, $"Round {round}: exactly one append must win.");
        }

        Assert.AreEqual(1 + rounds, journal.Length);
    }

    [TestMethod]
    public async Task DefaultDelegatePropertiesAreUsable()
    {
        //Ensure that the journal's exposed AppendDelegate and
        //ReadDelegate properties work as standalone delegates
        //(this matters because they will be wired into NodeStore
        //by reference, not by repeatedly re-fetching).
        InMemoryJournal journal = new();
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
            Timestamp: DateTimeOffset.UnixEpoch,
            SequenceNumber: sequence);
    }

    private async Task<JournalEntry> ReadFirst(InMemoryJournal journal)
    {
        await foreach(JournalEntry entry in journal.ReadDelegate(0L, TestContext.CancellationToken).ConfigureAwait(false))
        {
            return entry;
        }
        throw new InvalidOperationException("Journal is empty.");
    }

    //Wraps the append delegate to swallow concurrency conflicts
    //and report a nullable sequence number — useful for the
    //concurrent-append test where most racers expect to lose.
    private static async Task<long?> TryAppendAsync(InMemoryJournal journal, JournalEntry entry, NodeIdentifier expectedHead)
    {
        try
        {
            long sequence = await journal.AppendDelegate(entry, expectedHead, default).ConfigureAwait(false);
            return sequence;
        }
        catch(EditSessionConcurrencyException)
        {
            return null;
        }
    }
}
