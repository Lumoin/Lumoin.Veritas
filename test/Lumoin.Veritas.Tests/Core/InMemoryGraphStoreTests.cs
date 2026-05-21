using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

[TestClass]
internal sealed class InMemoryGraphStoreTests
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
    public void BuildDeduplicatesTriples()
    {
        EncodedTriple triple = EncodedTriple.FromEncoded(10, 11, 12);
        InMemoryGraphStore store = InMemoryGraphStore.Build([triple, triple, triple]);

        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void MatchWithAllUnboundReturnsAllTriples()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.None, TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchBySubjectUsesSpOIndex()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 13, 14),
            EncodedTriple.FromEncoded(15, 11, 12)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.None, TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchByPredicateUsesPosIndex()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 11, 14),
            EncodedTriple.FromEncoded(15, 16, 17)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.FromEncoded(11), TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchByObjectUsesOspIndex()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 12),
            EncodedTriple.FromEncoded(15, 16, 17)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.None, TermId.None, TermId.FromEncoded(12)));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchBySubjectAndPredicateFiltersCorrectly()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 11, 13),
            EncodedTriple.FromEncoded(10, 14, 15),
            EncodedTriple.FromEncoded(16, 11, 17)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.None));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void MatchByAllThreePositionsReturnsSingleTriple()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(10, 11, 13),
            EncodedTriple.FromEncoded(13, 14, 15)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(11), TermId.FromEncoded(12)));

        Assert.HasCount(1, results);
        Assert.AreEqual(EncodedTriple.FromEncoded(10, 11, 12), results[0]);
    }

    [TestMethod]
    public void MatchReturnsEmptyForNoResults()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12)
        ]);

        List<EncodedTriple> results = Collect(store.Match(TermId.FromEncoded(99), TermId.None, TermId.None));

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public async Task AsyncMatchDelegateYieldsResults()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15)
        ]);

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
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15),
            EncodedTriple.FromEncoded(16, 17, 18)
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
            EncodedTriple.FromEncoded(10, 11, 12),
            EncodedTriple.FromEncoded(13, 14, 15),
            EncodedTriple.FromEncoded(16, 17, 18)
        ]);

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

    [TestMethod]
    public async Task NullFilterHasZeroOverhead()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 11, 12)]);

        StorageDelegates.MatchTriplesAsync original = store.AsMatchDelegate();
        StorageDelegates.MatchTriplesAsync wrapped = StorageDelegates.WithFilter(original, null);

        //When the filter is null, the exact same delegate instance is returned.
        Assert.AreSame(original, wrapped);

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
