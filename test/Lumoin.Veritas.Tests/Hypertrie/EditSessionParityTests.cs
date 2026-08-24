using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Parity tests asserting that
/// <see cref="EditSession.CommitAsync"/> produces a snapshot
/// content-equivalent to <see cref="HypertrieGraphStore.BuildAsync"/>
/// over the same final triple set. The hypertrie's content-
/// addressed identifier algebra makes the equivalence sharp:
/// the two snapshots must share root identifier and full triple
/// extent, regardless of which descent path produced them.
/// </summary>
[TestClass]
internal sealed class EditSessionParityTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SingleAdditionMatchesBuildAsync()
    {
        EncodedTriple[] baseTriples = [EncodedTriple.FromEncoded(1, 10, 100)];
        EncodedTriple[] addedTriples = [EncodedTriple.FromEncoded(2, 20, 200)];

        await AssertCommitParityAsync(baseTriples, addedTriples, removed: []).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task SingleRemovalMatchesBuildAsync()
    {
        EncodedTriple[] baseTriples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(2, 20, 200),
        ];
        EncodedTriple[] removedTriples = [EncodedTriple.FromEncoded(2, 20, 200)];

        await AssertCommitParityAsync(baseTriples, added: [], removedTriples).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MixedAdditionsAndRemovalsMatchBuildAsync()
    {
        EncodedTriple[] baseTriples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(2, 20, 200),
            EncodedTriple.FromEncoded(3, 30, 300),
        ];

        EncodedTriple[] added =
        [
            EncodedTriple.FromEncoded(4, 40, 400),
            EncodedTriple.FromEncoded(5, 50, 500),
        ];

        EncodedTriple[] removed =
        [
            EncodedTriple.FromEncoded(2, 20, 200),
            EncodedTriple.FromEncoded(3, 30, 300),
        ];

        await AssertCommitParityAsync(baseTriples, added, removed).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RedundantAdditionTreatedAsNoOp()
    {
        //Adding a triple that is already present produces no
        //structural change. The committed snapshot must equal the
        //base snapshot's id; the literal "addition" is filtered
        //before patching.
        EncodedTriple[] baseTriples = [EncodedTriple.FromEncoded(1, 10, 100)];
        EncodedTriple alreadyPresent = baseTriples[0];

        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(baseTriples, store, TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier baseId = graph.Snapshot.Id;

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.Add(alreadyPresent);
            using HypertrieSnapshot committed = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(baseId, committed.Id);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task RedundantRemovalTreatedAsNoOp()
    {
        EncodedTriple[] baseTriples = [EncodedTriple.FromEncoded(1, 10, 100)];
        EncodedTriple absent = EncodedTriple.FromEncoded(99, 99, 99);

        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(baseTriples, store, TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier baseId = graph.Snapshot.Id;

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.Remove(absent);
            using HypertrieSnapshot committed = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(baseId, committed.Id);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task AddThenRemoveSameTripleResultsInBaseId()
    {
        //Last-write-wins inside the buffer means Remove(t) after
        //Add(t) records Remove. Removing a triple absent from the
        //base is a no-op against the base; the result equals the
        //base.
        EncodedTriple[] baseTriples = [EncodedTriple.FromEncoded(1, 10, 100)];
        EncodedTriple t = EncodedTriple.FromEncoded(2, 20, 200);

        InMemoryJournal journal = new();
        using NodeStore store = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(baseTriples, store, TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier baseId = graph.Snapshot.Id;

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.Add(t);
            session.Remove(t);
            using HypertrieSnapshot committed = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(baseId, committed.Id);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task ManyAdditionsMatchBuildAsync()
    {
        //Volume test: enough edits to produce SortedArray-kind
        //edge maps in the patched D2 nodes, exercising the
        //promotion ladder.
        List<EncodedTriple> baseTriples = [];
        List<EncodedTriple> addedTriples = [];

        for(uint i = 0; i < 5; i++)
        {
            baseTriples.Add(EncodedTriple.FromEncoded(1, 10, 100 + i));
        }
        for(uint i = 0; i < 10; i++)
        {
            addedTriples.Add(EncodedTriple.FromEncoded(1, 10, 200 + i));
        }

        await AssertCommitParityAsync([.. baseTriples], [.. addedTriples], removed: []).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DenseSharedSubjectMatchesBuildAsync()
    {
        //Many edits all sharing the same subject — exercises a
        //path where the S-first descent batches edits into a
        //single (outer=S) bucket and re-interns one D2 even though
        //many leaves change.
        EncodedTriple[] baseTriples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(1, 10, 101),
            EncodedTriple.FromEncoded(1, 11, 100),
        ];

        EncodedTriple[] added =
        [
            EncodedTriple.FromEncoded(1, 10, 102),
            EncodedTriple.FromEncoded(1, 11, 101),
            EncodedTriple.FromEncoded(1, 12, 200),
        ];

        await AssertCommitParityAsync(baseTriples, added, removed: []).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EdgeMapPromotionFromInlineToSortedArrayMatchesBuildAsync()
    {
        //Start with a single triple — every edge map on that path
        //is Inline. Add a second triple sharing every position but
        //the object — leaves must promote from Inline to
        //SortedArray. The patched result must match BuildAsync
        //exactly, including the canonical EdgeMapKind discriminant
        //(verified indirectly through identifier equality).
        EncodedTriple[] baseTriples = [EncodedTriple.FromEncoded(1, 10, 100)];
        EncodedTriple[] added = [EncodedTriple.FromEncoded(1, 10, 101)];

        await AssertCommitParityAsync(baseTriples, added, removed: []).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RemovingLastEntryFromLeafMatchesBuildAsync()
    {
        //Remove the only triple at a particular (S, P) leaf — the
        //leaf must become empty and be dropped from the parent's
        //edge map. BuildAsync over the post-remove set never
        //allocates the leaf in the first place; the patcher must
        //arrive at the same canonical empty-leaf-eliminated shape.
        EncodedTriple[] baseTriples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(2, 20, 200),
        ];
        EncodedTriple[] removed = [EncodedTriple.FromEncoded(1, 10, 100)];

        await AssertCommitParityAsync(baseTriples, added: [], removed).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RemoveAllThenAddDifferentSetMatchesBuildAsync()
    {
        //Total replacement: remove every base triple, add a
        //disjoint set. The intermediate state never needs to be
        //materialised — the patcher applies adds and removes
        //together against the base.
        EncodedTriple[] baseTriples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(2, 20, 200),
        ];
        EncodedTriple[] added =
        [
            EncodedTriple.FromEncoded(3, 30, 300),
            EncodedTriple.FromEncoded(4, 40, 400),
        ];

        await AssertCommitParityAsync(baseTriples, added, removed: baseTriples).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task BuildFromEmptyThroughSessionMatchesDirectBuild()
    {
        //An "initial-as-edits" scenario: open a session against
        //an empty graph and add every triple, then commit. The
        //resulting snapshot must match a direct BuildAsync over
        //the same triples. Tests that the patcher produces the
        //same canonical structure when the base is empty as the
        //bottom-up build does.
        EncodedTriple[] addedTriples =
        [
            EncodedTriple.FromEncoded(1, 10, 100),
            EncodedTriple.FromEncoded(1, 10, 101),
            EncodedTriple.FromEncoded(2, 20, 200),
            EncodedTriple.FromEncoded(3, 30, 300),
        ];

        await AssertCommitParityAsync(baseTriples: [], addedTriples, removed: []).ConfigureAwait(false);
    }

    private async Task AssertCommitParityAsync(EncodedTriple[] baseTriples, EncodedTriple[] added, EncodedTriple[] removed)
    {
        EncodedTriple[] finalSet = [.. baseTriples.Except(removed).Concat(added).Distinct()];

        using NodeStore expectedStore = new(VeritasHashing.Default);
        HypertrieGraphStore expected = await HypertrieGraphStore.BuildAsync(finalSet, expectedStore, TestContext.CancellationToken).ConfigureAwait(false);
        NodeIdentifier expectedId = expected.Snapshot.Id;

        InMemoryJournal journal = new();
        using NodeStore actualStore = new(VeritasHashing.Default, journal.AppendDelegate, journal.ReadDelegate);
        HypertrieGraphStore graph = await HypertrieGraphStore.BuildAsync(baseTriples, actualStore, TestContext.CancellationToken).ConfigureAwait(false);

        EditSession session = await graph.OpenEditSessionAsync(TestContext.CancellationToken).ConfigureAwait(false);
        try
        {
            session.AddRange(added);
            session.RemoveRange(removed);
            using HypertrieSnapshot committed = await session.CommitAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(expectedId, committed.Id);

            HashSet<EncodedTriple> expectedSet = [.. HypertrieOps.Match(expected.Snapshot.Store.GetByHandle(expected.Snapshot.Root), expected.Snapshot.Store, TermId.None, TermId.None, TermId.None)];
            HashSet<EncodedTriple> actualSet = [.. HypertrieOps.Match(committed.Store.GetByHandle(committed.Root), committed.Store, TermId.None, TermId.None, TermId.None)];
            Assert.IsTrue(expectedSet.SetEquals(actualSet));
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
