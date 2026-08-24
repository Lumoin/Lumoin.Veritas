using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Verification suite for
/// <see cref="HypertrieGraphStore.MatchByObjects"/>. Mirror of
/// <see cref="HypertrieGraphStoreMatchBySubjectsTests"/> across the
/// object position.
/// </summary>
[TestClass]
internal sealed class HypertrieGraphStoreMatchByObjectsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task EmptyObjectSetYieldsEmpty()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        IEnumerable<EncodedTriple> results = store.MatchByObjects(
            TermId.None, TermId.FromEncoded(100), ReadOnlyMemory<TermId>.Empty);

        Assert.IsEmpty(Collect(results));
    }

    [TestMethod]
    public async Task SingleElementSetMatchesSingleObjectForm()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId[] singleton = [TermId.FromEncoded(11)];
        HashSet<EncodedTriple> byBatch = ToSet(store.MatchByObjects(TermId.None, TermId.FromEncoded(100), singleton));
        HashSet<EncodedTriple> bySingle = ToSet(store.Match(TermId.None, TermId.FromEncoded(100), TermId.FromEncoded(11)));

        Assert.IsTrue(byBatch.SetEquals(bySingle));
    }

    [TestMethod]
    public async Task MultiElementSetYieldsUnion()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14),
            EncodedTriple.FromEncoded(20, 100, 21),
            EncodedTriple.FromEncoded(99, 100, 50)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

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
    public async Task SubjectFilterIsApplied()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(20, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 13)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

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
    public async Task DuplicatesInSetAreTolerated()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(12, 100, 11)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId[] objects = [TermId.FromEncoded(11), TermId.FromEncoded(11)];
        HashSet<EncodedTriple> results = ToSet(store.MatchByObjects(TermId.None, TermId.FromEncoded(100), objects));

        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 11), results);
        Assert.Contains(EncodedTriple.FromEncoded(12, 100, 11), results);
    }

    [TestMethod]
    public async Task UnboundPredicateThrows()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [EncodedTriple.FromEncoded(10, 100, 11)],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);
        TermId[] objects = [TermId.FromEncoded(11)];

        Assert.Throws<ArgumentException>(() =>
            Collect(store.MatchByObjects(TermId.None, TermId.None, objects)));
    }

    [TestMethod]
    public async Task NoneInSetThrows()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [EncodedTriple.FromEncoded(10, 100, 11)],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);
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
