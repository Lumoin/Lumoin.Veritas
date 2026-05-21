using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verification suite for <see cref="InMemoryGraphStore.MatchBySubjects"/>.
/// The primitive is the subject-set peer of <see cref="InMemoryGraphStore.Match"/>;
/// these tests pin the contract documented on
/// <see cref="StorageDelegates.MatchTriplesBySubjectsAsync"/>: predicate
/// must be bound, the subject set may not carry <see cref="TermId.None"/>,
/// empty sets yield empty results, and the multi-subject result is the
/// union of the per-subject results.
/// </summary>
/// <remarks>
/// <para>
/// Encoded node ids start at <c>10</c> (well clear of the
/// <see cref="TermId.None"/> sentinel at <c>0</c>) so bound-to-X queries
/// do not alias with unbound queries.
/// </para>
/// </remarks>
[TestClass]
internal sealed class InMemoryGraphStoreMatchBySubjectsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void EmptySubjectSetYieldsEmpty()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14)
        ]);

        IEnumerable<EncodedTriple> results = store.MatchBySubjects(
            ReadOnlyMemory<TermId>.Empty, TermId.FromEncoded(100), TermId.None);

        Assert.IsEmpty(Collect(results));
    }

    [TestMethod]
    public void SingleElementSetMatchesSingleSubjectForm()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(13, 100, 14)
        ]);

        TermId[] singleton = [TermId.FromEncoded(10)];
        HashSet<EncodedTriple> byBatch = ToSet(store.MatchBySubjects(singleton, TermId.FromEncoded(100), TermId.None));
        HashSet<EncodedTriple> bySingle = ToSet(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(100), TermId.None));

        Assert.IsTrue(byBatch.SetEquals(bySingle));
    }

    [TestMethod]
    public void MultiElementSetYieldsUnion()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(13, 100, 14),
            EncodedTriple.FromEncoded(20, 100, 21),
            EncodedTriple.FromEncoded(99, 100, 50)
        ]);

        TermId[] subjects = [TermId.FromEncoded(10), TermId.FromEncoded(20)];
        HashSet<EncodedTriple> results = ToSet(store.MatchBySubjects(subjects, TermId.FromEncoded(100), TermId.None));

        HashSet<EncodedTriple> expected = new()
        {
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(20, 100, 21),
        };
        Assert.IsTrue(results.SetEquals(expected));
    }

    [TestMethod]
    public void ObjectFilterIsApplied()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(20, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 13)
        ]);

        TermId[] subjects = [TermId.FromEncoded(10), TermId.FromEncoded(20)];
        HashSet<EncodedTriple> results = ToSet(store.MatchBySubjects(subjects, TermId.FromEncoded(100), TermId.FromEncoded(11)));

        HashSet<EncodedTriple> expected = new()
        {
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 11),
        };
        Assert.IsTrue(results.SetEquals(expected));
    }

    [TestMethod]
    public void DuplicatesInSetAreTolerated()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12)
        ]);

        TermId[] subjects = [TermId.FromEncoded(10), TermId.FromEncoded(10)];
        HashSet<EncodedTriple> results = ToSet(store.MatchBySubjects(subjects, TermId.FromEncoded(100), TermId.None));

        //Output may or may not be deduplicated; the contract requires
        //at-least membership of every triple that matches subject 10.
        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 11), results);
        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 12), results);
    }

    [TestMethod]
    public void UnboundPredicateThrows()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);
        TermId[] subjects = [TermId.FromEncoded(10)];

        Assert.Throws<ArgumentException>(() =>
            Collect(store.MatchBySubjects(subjects, TermId.None, TermId.None)));
    }

    [TestMethod]
    public void NoneInSetThrows()
    {
        InMemoryGraphStore store = InMemoryGraphStore.Build([EncodedTriple.FromEncoded(10, 100, 11)]);
        TermId[] subjects = [TermId.FromEncoded(10), TermId.None];

        Assert.Throws<ArgumentException>(() =>
            Collect(store.MatchBySubjects(subjects, TermId.FromEncoded(100), TermId.None)));
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
