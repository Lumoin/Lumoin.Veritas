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
/// <see cref="HypertrieGraphStore.MatchBySubjects"/>. The hypertrie
/// implementation must do a single predicate-rooted descent followed by
/// per-subject probes — never <c>|subjects|</c> root descents. The
/// contract on inputs matches the in-memory peer; this suite repeats
/// the seven cases against the hypertrie store.
/// </summary>
[TestClass]
internal sealed class HypertrieGraphStoreMatchBySubjectsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task EmptySubjectSetYieldsEmpty()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(13, 100, 14)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        IEnumerable<EncodedTriple> results = store.MatchBySubjects(
            ReadOnlyMemory<TermId>.Empty, TermId.FromEncoded(100), TermId.None);

        Assert.IsEmpty(Collect(results));
    }

    [TestMethod]
    public async Task SingleElementSetMatchesSingleSubjectForm()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(13, 100, 14)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId[] singleton = [TermId.FromEncoded(10)];
        HashSet<EncodedTriple> byBatch = ToSet(store.MatchBySubjects(singleton, TermId.FromEncoded(100), TermId.None));
        HashSet<EncodedTriple> bySingle = ToSet(store.Match(TermId.FromEncoded(10), TermId.FromEncoded(100), TermId.None));

        Assert.IsTrue(byBatch.SetEquals(bySingle));
    }

    [TestMethod]
    public async Task MultiElementSetYieldsUnion()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(13, 100, 14),
            EncodedTriple.FromEncoded(20, 100, 21),
            EncodedTriple.FromEncoded(99, 100, 50)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

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
    public async Task ObjectFilterIsApplied()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12),
            EncodedTriple.FromEncoded(20, 100, 11),
            EncodedTriple.FromEncoded(20, 100, 13)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

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
    public async Task DuplicatesInSetAreTolerated()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
        [
            EncodedTriple.FromEncoded(10, 100, 11),
            EncodedTriple.FromEncoded(10, 100, 12)
        ], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId[] subjects = [TermId.FromEncoded(10), TermId.FromEncoded(10)];
        HashSet<EncodedTriple> results = ToSet(store.MatchBySubjects(subjects, TermId.FromEncoded(100), TermId.None));

        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 11), results);
        Assert.Contains(EncodedTriple.FromEncoded(10, 100, 12), results);
    }

    [TestMethod]
    public async Task UnboundPredicateThrows()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [EncodedTriple.FromEncoded(10, 100, 11)],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);
        TermId[] subjects = [TermId.FromEncoded(10)];

        Assert.Throws<ArgumentException>(() =>
            Collect(store.MatchBySubjects(subjects, TermId.None, TermId.None)));
    }

    [TestMethod]
    public async Task NoneInSetThrows()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(
            [EncodedTriple.FromEncoded(10, 100, 11)],
            VeritasHashing.Default,
            TestContext.CancellationToken).ConfigureAwait(false);
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
