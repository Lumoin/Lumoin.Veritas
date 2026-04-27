using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class InMemoryGraphStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BuildDeduplicatesTriples()
    {
        EncodedTriple triple = EncodedTriple.FromEncoded(0, 1, 2);
        InMemoryGraphStore store = InMemoryGraphStore.Build([triple, triple, triple]);

        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void MatchWithAllUnboundReturnsAllTriples()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5)
        ]);

        List<EncodedTriple> results = store.Match(null, null, null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchBySubjectUsesSpOIndex()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 3, 4),
            EncodedTriple.FromEncoded(5, 1, 2)
        ]);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), null, null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchByPredicateUsesPosIndex()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 1, 4),
            EncodedTriple.FromEncoded(5, 6, 7)
        ]);

        List<EncodedTriple> results = store.Match(null, TermId.FromEncoded(1), null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchByObjectUsesOspIndex()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 2),
            EncodedTriple.FromEncoded(5, 6, 7)
        ]);

        List<EncodedTriple> results = store.Match(null, null, TermId.FromEncoded(2)).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchBySubjectAndPredicateFiltersCorrectly()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 1, 3),
            EncodedTriple.FromEncoded(0, 4, 5),
            EncodedTriple.FromEncoded(6, 1, 7)
        ]);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), TermId.FromEncoded(1), null).ToList();

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchByAllThreePositionsReturnsSingleTriple()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(0, 1, 3),
            EncodedTriple.FromEncoded(3, 4, 5)
        ]);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(0), TermId.FromEncoded(1), TermId.FromEncoded(2)).ToList();

        Assert.HasCount(1, results);
        Assert.AreEqual(EncodedTriple.FromEncoded(0, 1, 2), results[0]);
    }

    [TestMethod]
    public void MatchReturnsEmptyForNoResults()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2)
        ]);

        List<EncodedTriple> results = store.Match(TermId.FromEncoded(99), null, null).ToList();

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task AsyncMatchDelegateYieldsResults()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5)
        ]);

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
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5),
            EncodedTriple.FromEncoded(6, 7, 8)
        ]);

        StorageDelegates.CountTriplesAsync count = store.AsCountDelegate();
        long result = await count(TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(3L, result);
    }

    [TestMethod]
    public async Task VisibilityFilterExcludesTriples()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(0, 1, 2),
            EncodedTriple.FromEncoded(3, 4, 5),
            EncodedTriple.FromEncoded(6, 7, 8)
        ]);

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

    [TestMethod]
    public async Task NullFilterHasZeroOverhead()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(0, 1, 2)]);

        StorageDelegates.MatchTriplesAsync original = store.AsMatchDelegate();
        StorageDelegates.MatchTriplesAsync wrapped = StorageDelegates.WithFilter(original, null);

        //When the filter is null, the exact same delegate instance is returned.
        Assert.AreSame(original, wrapped);

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
