using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verification suite for <see cref="InMemoryGraphStore.MatchByObjects"/>.
/// Mirror of <see cref="InMemoryGraphStoreMatchBySubjectsTests"/> across
/// the object position; the contract on the primitive is the same with
/// subjects and objects swapped.
/// </summary>
/// <remarks>
/// <para>
/// Encoded node ids start at <c>10</c> (well clear of the
/// <see cref="TermId.None"/> sentinel at <c>0</c>) so bound-to-X queries
/// do not alias with unbound queries.
/// </para>
/// </remarks>
[TestClass]
internal sealed class InMemoryGraphStoreMatchByObjectsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptyObjectSetYieldsEmpty()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14)
        ]);

        IEnumerable<EncodedTriple> results = store.MatchByObjects(
            TermId.None, TermId.FromEncoded(100), ReadOnlyMemory<TermId>.Empty);

        Assert.IsEmpty(Collect(results));
    }

    [TestMethod]
    public void SingleElementSetMatchesSingleObjectForm()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14)
        ]);

        TermId[] singleton = [TermId.FromEncoded(11)];
        HashSet<EncodedTriple> byBatch = ToSet(store.MatchByObjects(TermId.None, TermId.FromEncoded(100), singleton));
        HashSet<EncodedTriple> bySingle = ToSet(store.Match(TermId.None, TermId.FromEncoded(100), TermId.FromEncoded(11)));

        Assert.IsTrue(byBatch.SetEquals(bySingle));
    }

    [TestMethod]
    public void MultiElementSetYieldsUnion()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14),
            EncodedTriple.FromEncoded(20, 100, 21),
            EncodedTriple.FromEncoded(99, 100, 50)
        ]);

        TermId[] objects = [TermId.FromEncoded(11), TermId.FromEncoded(21)];
        HashSet<EncodedTriple> results = ToSet(store.MatchByObjects(TermId.None, TermId.FromEncoded(100), objects));

        HashSet<EncodedTriple> expected = new()
        {
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 21),
        };
        Assert.IsTrue(results.SetEquals(expected));
    }

    [TestMethod]
    public void SubjectFilterIsApplied()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(20, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 13)
        ]);

        TermId[] objects = [TermId.FromEncoded(11), TermId.FromEncoded(12), TermId.FromEncoded(13)];
        HashSet<EncodedTriple> results = ToSet(store.MatchByObjects(TermId.FromEncoded(10), TermId.FromEncoded(100), objects));

        HashSet<EncodedTriple> expected = new()
        {
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
        };
        Assert.IsTrue(results.SetEquals(expected));
    }

    [TestMethod]
    public void DuplicatesInSetAreTolerated()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11)
        ]);

        TermId[] objects = [TermId.FromEncoded(11), TermId.FromEncoded(11)];
        HashSet<EncodedTriple> results = ToSet(store.MatchByObjects(TermId.None, TermId.FromEncoded(100), objects));

        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 11), results);
        Assert.Contains(EncodedTriple.FromEncoded(12, 100, 11), results);
    }

    [TestMethod]
    public void UnboundPredicateThrows()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);
        TermId[] objects = [TermId.FromEncoded(11)];

        Assert.Throws<ArgumentException>(() =>
            Collect(store.MatchByObjects(TermId.None, TermId.None, objects)));
    }

    [TestMethod]
    public void NoneInSetThrows()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);
        TermId[] objects = [TermId.FromEncoded(11), TermId.None];

        Assert.Throws<ArgumentException>(() =>
            Collect(store.MatchByObjects(TermId.None, TermId.FromEncoded(100), objects)));
    }

    private static List<T> Collect<T>(IEnumerable<T> source)
    {
        List<T> list = [];
        foreach(T item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private static HashSet<EncodedTriple> ToSet(IEnumerable<EncodedTriple> source)
    {
        HashSet<EncodedTriple> set = [];
        foreach(EncodedTriple triple in source)
        {
            set.Add(triple);
        }

        return set;
    }
}
