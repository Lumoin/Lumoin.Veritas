using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class HypertrieGraphStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    private static List<T> Collect<T>(IEnumerable<T> source)
    {
        List<T> list = [];
        foreach(T item in source)
        {
            list.Add(item);
        }

        return list;
    }

    //Encoded values start at 10 (well clear of the TermId.None
    //sentinel at 0) so bound-to-X queries do not alias with
    //unbound queries.

    [TestMethod]
    public async Task BuildDeduplicatesTriples()
    {
        EncodedTriple triple = EncodedTriple.FromEncoded(10, 11, 12);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([triple, triple, triple], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public async Task MatchWithAllUnboundReturnsAllTriples()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.None, TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchBySubjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 13, 14),
            EncodedTriple.FromEncoded(15, 11, 12)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.None, TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByPredicateFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 11, 14),
            EncodedTriple.FromEncoded(15, 16, 17)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.FromEncoded(11), TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByObjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 12),
            EncodedTriple.FromEncoded(15, 16, 17)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.None, TermId.FromEncoded(12)));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchBySubjectAndPredicateFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 11, 13),
            EncodedTriple.FromEncoded(10, 14, 15),
            EncodedTriple.FromEncoded(16, 11, 17)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchBySubjectAndObjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 13, 12),
            EncodedTriple.FromEncoded(10, 14, 15),
            EncodedTriple.FromEncoded(16, 11, 12)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.None, TermId.FromEncoded(12)));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByPredicateAndObjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 11, 12),
            EncodedTriple.FromEncoded(14, 11, 15),
            EncodedTriple.FromEncoded(16, 17, 12)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.FromEncoded(11), TermId.FromEncoded(12)));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task BuildSharedReturnsIsolatedPositionallyMatchingStores()
    {
        //Three graphs through one arena: distinct content, overlapping
        //content (shares the 10-11-12 subtree with graph 0), and empty.
        List<EncodedTriple> first = [EncodedTriple.FromEncoded(10, 11, 12), EncodedTriple.FromEncoded(13, 14, 15)];
        List<EncodedTriple> second = [EncodedTriple.FromEncoded(10, 11, 12), EncodedTriple.FromEncoded(16, 17, 18)];
        List<EncodedTriple> third = [];

        IReadOnlyList<HypertrieGraphStore> stores = await HypertrieGraphStore.BuildSharedAsync(
            [first, second, third], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.HasCount(3, stores);
        Assert.AreEqual(2, stores[0].Count);
        Assert.AreEqual(2, stores[1].Count);
        Assert.AreEqual(0, stores[2].Count);

        //Graph isolation: content interned in the shared arena never
        //leaks across roots — each store matches exactly its own set.
        Assert.AreSequenceEqual(first, Collect(stores[0].Match(TermId.None, TermId.None, TermId.None)), SequenceOrder.InAnyOrder);
        Assert.AreSequenceEqual(second, Collect(stores[1].Match(TermId.None, TermId.None, TermId.None)), SequenceOrder.InAnyOrder);
        Assert.HasCount(0, Collect(stores[2].Match(TermId.None, TermId.None, TermId.None)));
        Assert.HasCount(0, Collect(stores[0].Match(TermId.FromEncoded(16), TermId.None, TermId.None)));
        Assert.HasCount(1, Collect(stores[1].Match(TermId.FromEncoded(10), TermId.None, TermId.None)));
    }

    [TestMethod]
    public async Task BuildSharedIdenticalGraphsShareOneCanonicalRoot()
    {
        List<EncodedTriple> triples = [EncodedTriple.FromEncoded(10, 11, 12), EncodedTriple.FromEncoded(10, 13, 14)];

        IReadOnlyList<HypertrieGraphStore> stores = await HypertrieGraphStore.BuildSharedAsync(
            [triples, triples], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        //Identical content interns to the same canonical root; both
        //stores answer independently over it.
        Assert.AreEqual(stores[0].Snapshot.Id, stores[1].Snapshot.Id);
        Assert.AreSequenceEqual(
            Collect(stores[0].Match(TermId.None, TermId.None, TermId.None)),
            Collect(stores[1].Match(TermId.None, TermId.None, TermId.None)), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task BuildSharedMatchesIsolatedBuildsTripleForTriple()
    {
        //Differential gate: the family build answers exactly what
        //per-store isolated builds answer, graph by graph.
        List<List<EncodedTriple>> graphs = [];
        for(int g = 0; g < 8; g++)
        {
            List<EncodedTriple> triples = [];
            for(int i = 0; i < 24; i++)
            {
                uint entity = (uint)((g * 24) + i + 10);
                triples.Add(EncodedTriple.FromEncoded(entity, (uint)(11 + (i % 3)), entity + 1_000));
            }

            graphs.Add(triples);
        }

        IReadOnlyList<HypertrieGraphStore> shared = await HypertrieGraphStore.BuildSharedAsync(
            graphs, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        for(int g = 0; g < graphs.Count; g++)
        {
            HypertrieGraphStore isolated = await HypertrieGraphStore.BuildAsync(graphs[g], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(isolated.Count, shared[g].Count);
            Assert.AreSequenceEqual(
                Collect(isolated.Match(TermId.None, TermId.None, TermId.None)),
                Collect(shared[g].Match(TermId.None, TermId.None, TermId.None)), SequenceOrder.InAnyOrder);
        }
    }

    [TestMethod]
    public async Task MatchByAllThreePositionsReturnsSingleTriple()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 11, 13),
            EncodedTriple.FromEncoded(13, 14, 15)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12)));

        Assert.HasCount(1, results);
        Assert.AreEqual(EncodedTriple.FromEncoded(10, 11, 12), results[0]);
    }

    [TestMethod]
    public async Task MatchReturnsEmptyForNoResults()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(99), TermId.None, TermId.None));

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task MatchOnEmptyStoreReturnsNothing()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, store.Count);
        Assert.IsEmpty(store.Match(TermId.None, TermId.None, TermId.None));
        Assert.IsEmpty(store.Match(TermId.FromEncoded(10), TermId.None, TermId.None));
        Assert.IsEmpty(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12)));
    }

    [TestMethod]
    public async Task MatchYieldsEachTripleExactlyOnceForFullScan()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(11, 12, 13),
            EncodedTriple.FromEncoded(11, 12, 14),
            EncodedTriple.FromEncoded(11, 15, 13),
            EncodedTriple.FromEncoded(16, 12, 13),
        ];
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.None, TermId.None));

        Assert.HasCount(triples.Length, results);
        HashSet<EncodedTriple> distinct = [.. results];
        Assert.HasCount(triples.Length, distinct);
    }

    [TestMethod]
    public async Task AsyncMatchDelegateYieldsResults()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        StorageDelegates.MatchTriplesAsync match = store.AsMatchDelegate();
        List<EncodedTriple> results = [];

        await foreach(EncodedTriple triple in match(TermId.None, TermId.None, TermId.None, TestContext.CancellationToken).ConfigureAwait(false))
        {
            results.Add(triple);
        }

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task CountDelegateReturnsCorrectCount()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15),
            EncodedTriple.FromEncoded(16, 17, 18)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        StorageDelegates.CountTriplesAsync count = store.AsCountDelegate();
        long result = await count(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3L, result);
    }

    [TestMethod]
    public async Task VisibilityFilterExcludesTriples()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15),
            EncodedTriple.FromEncoded(16, 17, 18)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        //Only allow triples where the subject's encoded id is less than 15.
        StorageDelegates.TripleVisibilityFilter filter = (s, _, _) => s.Encoded < 15;
        StorageDelegates.MatchTriplesAsync filtered = StorageDelegates.WithFilter(
            store.AsMatchDelegate(), filter);

        List<EncodedTriple> results = [];
        await foreach(EncodedTriple triple in filtered(TermId.None, TermId.None, TermId.None, TestContext.CancellationToken).ConfigureAwait(false))
        {
            results.Add(triple);
        }

        Assert.HasCount(2, results);
    }
}
