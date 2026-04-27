using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

[TestClass]
internal sealed class HypertrieGraphStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task BuildDeduplicatesTriples()
    {
        EncodedTriple triple = EncodedTriple.FromEncoded(0, 1, 2);
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([triple, triple, triple], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public async Task MatchWithAllUnboundReturnsAllTriples()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(null, null, null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchBySubjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 3, 4),
            EncodedTriple.FromEncoded(5, 1, 2)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), null, null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByPredicateFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 1, 4),
            EncodedTriple.FromEncoded(5, 6, 7)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(null, TermId.FromEncoded(1), null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByObjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 2),
            EncodedTriple.FromEncoded(5, 6, 7)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(null, null, TermId.FromEncoded(2)).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchBySubjectAndPredicateFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 1, 3),
            EncodedTriple.FromEncoded(0, 4, 5),
            EncodedTriple.FromEncoded(6, 1, 7)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), TermId.FromEncoded(1), null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchBySubjectAndObjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 3, 2),
            EncodedTriple.FromEncoded(0, 4, 5),
            EncodedTriple.FromEncoded(6, 1, 2)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), null, TermId.FromEncoded(2)).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByPredicateAndObjectFiltersCorrectly()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 1, 2),
            EncodedTriple.FromEncoded(4, 1, 5),
            EncodedTriple.FromEncoded(6, 7, 2)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(null, TermId.FromEncoded(1), TermId.FromEncoded(2)).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public async Task MatchByAllThreePositionsReturnsSingleTriple()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 1, 3),
            EncodedTriple.FromEncoded(3, 4, 5)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), TermId.FromEncoded(1), TermId.FromEncoded(2)).ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual(EncodedTriple.FromEncoded(0, 1, 2), results[0]);
    }

    [TestMethod]
    public async Task MatchReturnsEmptyForNoResults()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(99), null, null).ToList();

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task MatchOnEmptyStoreReturnsNothing()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, store.Count);
        Assert.IsEmpty(store.Match(null, null, null));
        Assert.IsEmpty(store.Match(TermId.FromEncoded(0), null, null));
        Assert.IsEmpty(store.Match(TermId.FromEncoded(0), TermId.FromEncoded(1), TermId.FromEncoded(2)));
    }

    [TestMethod]
    public async Task MatchYieldsEachTripleExactlyOnceForFullScan()
    {
        EncodedTriple[] triples =
        [
            EncodedTriple.FromEncoded(1, 2, 3),
            EncodedTriple.FromEncoded(1, 2, 4),
            EncodedTriple.FromEncoded(1, 5, 3),
            EncodedTriple.FromEncoded(6, 2, 3),
        ];
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        List<EncodedTriple> results = store.Match(null, null, null).ToList();

        Assert.HasCount(triples.Length, results);
        HashSet<EncodedTriple> distinct = [.. results];
        Assert.HasCount(triples.Length, distinct);
    }

    [TestMethod]
    public async Task AsyncMatchDelegateYieldsResults()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        StorageDelegates.MatchTriplesAsync match = store.AsMatchDelegate();
        List<EncodedTriple> results = [];

        await foreach(EncodedTriple triple in match(null, null, null, TestContext.CancellationToken).ConfigureAwait(false))
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
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5),
            EncodedTriple.FromEncoded(6, 7, 8)
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
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5),
            EncodedTriple.FromEncoded(6, 7, 8)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        //Only allow triples where the subject's encoded id is less than 5.
        StorageDelegates.TripleVisibilityFilter filter = (s, _, _) => s.Encoded < 5;
        StorageDelegates.MatchTriplesAsync filtered = StorageDelegates.WithFilter(
            store.AsMatchDelegate(), filter);

        List<EncodedTriple> results = [];
        await foreach(EncodedTriple triple in filtered(null, null, null, TestContext.CancellationToken).ConfigureAwait(false))
        {
            results.Add(triple);
        }

        Assert.HasCount(2, results);
    }
}
