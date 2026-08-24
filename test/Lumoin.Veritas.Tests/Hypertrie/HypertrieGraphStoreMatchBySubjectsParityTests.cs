using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Parity tests: <see cref="HypertrieGraphStore.MatchBySubjects"/> and
/// <see cref="HypertrieGraphStore.MatchByObjects"/> must produce the
/// same result set as their <see cref="InMemoryGraphStore"/> peers for
/// every shared input. The two backends use completely different data
/// structures, so cross-validation guarantees both implement the same
/// semantics.
/// </summary>
[TestClass]
internal sealed class HypertrieGraphStoreMatchBySubjectsParityTests
{
    public TestContext TestContext { get; set; } = null!;

    private static EncodedTriple[] SampleTriples { get; } =
    [
        EncodedTriple.FromEncoded(1, 10, 100),
        EncodedTriple.FromEncoded(1, 10, 101),
        EncodedTriple.FromEncoded(1, 11, 100),
        EncodedTriple.FromEncoded(2, 10, 100),
        EncodedTriple.FromEncoded(2, 11, 200),
        EncodedTriple.FromEncoded(3, 12, 300),
    ];

    [TestMethod]
    public async Task MatchBySubjectsEmptySetParity()
    {
        await AssertSubjectsParityAsync(SampleTriples, Array.Empty<uint>(), 10U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchBySubjectsSingletonParity()
    {
        await AssertSubjectsParityAsync(SampleTriples, [1U], 10U, null).ConfigureAwait(false);
        await AssertSubjectsParityAsync(SampleTriples, [2U], 10U, null).ConfigureAwait(false);
        await AssertSubjectsParityAsync(SampleTriples, [3U], 12U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchBySubjectsMultiSetWithBoundObjectParity()
    {
        await AssertSubjectsParityAsync(SampleTriples, [1U, 2U], 10U, 100U).ConfigureAwait(false);
        await AssertSubjectsParityAsync(SampleTriples, [1U, 2U, 3U], 10U, 100U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchBySubjectsMultiSetWithUnboundObjectParity()
    {
        await AssertSubjectsParityAsync(SampleTriples, [1U, 2U], 10U, null).ConfigureAwait(false);
        await AssertSubjectsParityAsync(SampleTriples, [1U, 2U, 3U], 11U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchBySubjectsDuplicateSubjectsParity()
    {
        await AssertSubjectsParityAsync(SampleTriples, [1U, 1U, 2U, 2U], 10U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchByObjectsEmptySetParity()
    {
        await AssertObjectsParityAsync(SampleTriples, null, 10U, Array.Empty<uint>()).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchByObjectsSingletonParity()
    {
        await AssertObjectsParityAsync(SampleTriples, null, 10U, [100U]).ConfigureAwait(false);
        await AssertObjectsParityAsync(SampleTriples, null, 11U, [200U]).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchByObjectsMultiSetWithBoundSubjectParity()
    {
        await AssertObjectsParityAsync(SampleTriples, 1U, 10U, [100U, 101U]).ConfigureAwait(false);
        await AssertObjectsParityAsync(SampleTriples, 2U, 11U, [100U, 200U, 300U]).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchByObjectsMultiSetWithUnboundSubjectParity()
    {
        await AssertObjectsParityAsync(SampleTriples, null, 10U, [100U, 101U]).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchByObjectsDuplicateObjectsParity()
    {
        await AssertObjectsParityAsync(SampleTriples, null, 10U, [100U, 100U, 101U]).ConfigureAwait(false);
    }

    private async Task AssertSubjectsParityAsync(
        IEnumerable<EncodedTriple> triples,
        uint[] subjectIds,
        uint predicateId,
        uint? objectId)
    {
        EncodedTriple[] materialized = [.. triples];
        InMemoryGraphStore inMemory = InMemoryGraphStore.Build(materialized);
        HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(
            materialized, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId[] subjects = new TermId[subjectIds.Length];
        for(int i = 0; i < subjectIds.Length; i++)
        {
            subjects[i] = TermId.FromEncoded(subjectIds[i]);
        }

        TermId predicate = TermId.FromEncoded(predicateId);
        TermId @object = objectId.HasValue ? TermId.FromEncoded(objectId.Value) : TermId.None;

        HashSet<EncodedTriple> inMemorySet = ToSet(inMemory.MatchBySubjects(subjects, predicate, @object));
        HashSet<EncodedTriple> hypertrieSet = ToSet(hypertrie.MatchBySubjects(subjects, predicate, @object));

        Assert.IsTrue(
            inMemorySet.SetEquals(hypertrieSet),
            $"MatchBySubjects parity broke at subjects=[{string.Join(",", subjectIds)}], p={predicateId}, o={(objectId.HasValue ? objectId.Value.ToString(CultureInfo.InvariantCulture) : "?")}.");
    }

    private async Task AssertObjectsParityAsync(
        IEnumerable<EncodedTriple> triples,
        uint? subjectId,
        uint predicateId,
        uint[] objectIds)
    {
        EncodedTriple[] materialized = [.. triples];
        InMemoryGraphStore inMemory = InMemoryGraphStore.Build(materialized);
        HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(
            materialized, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId subject = subjectId.HasValue ? TermId.FromEncoded(subjectId.Value) : TermId.None;
        TermId predicate = TermId.FromEncoded(predicateId);

        TermId[] objects = new TermId[objectIds.Length];
        for(int i = 0; i < objectIds.Length; i++)
        {
            objects[i] = TermId.FromEncoded(objectIds[i]);
        }

        HashSet<EncodedTriple> inMemorySet = ToSet(inMemory.MatchByObjects(subject, predicate, objects));
        HashSet<EncodedTriple> hypertrieSet = ToSet(hypertrie.MatchByObjects(subject, predicate, objects));

        Assert.IsTrue(
            inMemorySet.SetEquals(hypertrieSet),
            $"MatchByObjects parity broke at s={(subjectId.HasValue ? subjectId.Value.ToString(CultureInfo.InvariantCulture) : "?")}, p={predicateId}, objects=[{string.Join(",", objectIds)}].");
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
