using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class HypertrieGraphStoreTests
{
    public TestContext TestContext { get; set; } = null!;

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

        List<EncodedTriple> results = store.Match(TermId.None, TermId.None, TermId.None).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(10), TermId.None, TermId.None).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.None, TermId.FromEncoded(11), TermId.None).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.None, TermId.None, TermId.FromEncoded(12)).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.None).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(10), TermId.None, TermId.FromEncoded(12)).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.None, TermId.FromEncoded(11), TermId.FromEncoded(12)).ToList();

        Assert.HasCount(2, results);
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

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12)).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(99), TermId.None, TermId.None).ToList();

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

        List<EncodedTriple> results = store.Match(TermId.None, TermId.None, TermId.None).ToList();

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
