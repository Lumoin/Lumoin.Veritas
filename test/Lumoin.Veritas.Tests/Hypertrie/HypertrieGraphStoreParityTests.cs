using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Hypertrie;

/// <summary>
/// Side-by-side parity tests comparing
/// <see cref="HypertrieGraphStore"/> with
/// <see cref="InMemoryGraphStore"/>. Every test builds both stores
/// from the same input and asserts they produce the same set of
/// triples for the same query. This is the primary safety net
/// while the hypertrie matures: if either backend regresses,
/// parity breaks immediately.
/// </summary>
[TestClass]
internal sealed class HypertrieGraphStoreParityTests
{
    public TestContext TestContext { get; set; } = null!;

    //A small graph spanning multiple subjects, predicates, and
    //objects, with deliberate sharing across positions so that the
    //hypertrie's multi-position branching and InMemoryGraphStore's
    //three permutation indexes can disagree if either is wrong.
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
    public async Task CountMatchesAcrossStores()
    {
        InMemoryGraphStore inMemory = InMemoryGraphStore.Build(SampleTriples);
        HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(SampleTriples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(inMemory.Count, hypertrie.Count);
    }

    [TestMethod]
    public async Task MatchAllUnboundParity()
    {
        await AssertParityAsync(SampleTriples, null, null, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchSubjectOnlyParity()
    {
        await AssertParityAsync(SampleTriples, 1U, null, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 2U, null, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 3U, null, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchPredicateOnlyParity()
    {
        await AssertParityAsync(SampleTriples, null, 10U, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, 11U, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, 12U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchObjectOnlyParity()
    {
        await AssertParityAsync(SampleTriples, null, null, 100U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, null, 101U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, null, 200U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, null, 300U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchSubjectAndPredicateParity()
    {
        await AssertParityAsync(SampleTriples, 1U, 10U, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 1U, 11U, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 2U, 10U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchSubjectAndObjectParity()
    {
        await AssertParityAsync(SampleTriples, 1U, null, 100U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 2U, null, 100U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchPredicateAndObjectParity()
    {
        await AssertParityAsync(SampleTriples, null, 10U, 100U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, 11U, 100U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchAllBoundParityWhenPresent()
    {
        await AssertParityAsync(SampleTriples, 1U, 10U, 100U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 1U, 10U, 101U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 3U, 12U, 300U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchAllBoundParityWhenAbsent()
    {
        await AssertParityAsync(SampleTriples, 1U, 10U, 999U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 999U, 10U, 100U).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, 1U, 999U, 100U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MatchPatternsThatYieldNothing()
    {
        await AssertParityAsync(SampleTriples, 999U, null, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, 999U, null).ConfigureAwait(false);
        await AssertParityAsync(SampleTriples, null, null, 999U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task EmptyGraphParity()
    {
        await AssertParityAsync([], null, null, null).ConfigureAwait(false);
        await AssertParityAsync([], 1U, null, null).ConfigureAwait(false);
        await AssertParityAsync([], 1U, 2U, 3U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task DuplicateInputDeduplicatesIdentically()
    {
        EncodedTriple t = EncodedTriple.FromEncoded(7, 8, 9);
        EncodedTriple[] withDuplicates = [t, t, t, t];

        InMemoryGraphStore inMemory = InMemoryGraphStore.Build(withDuplicates);
        HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(withDuplicates, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(inMemory.Count, hypertrie.Count);
        Assert.AreEqual(1, hypertrie.Count);
    }

    [TestMethod]
    public async Task StarPatternAtPopularSubjectMatches()
    {
        //One subject participates in many triples; verify "S only
        //bound" matches the same set on both stores at a non-trivial
        //degree.
        List<EncodedTriple> triples = [];
        for(uint p = 0; p < 25; p++)
        {
            for(uint o = 100; o < 125; o++)
            {
                triples.Add(EncodedTriple.FromEncoded(1, p, o));
            }
        }
        triples.Add(EncodedTriple.FromEncoded(2, 0, 100));
        triples.Add(EncodedTriple.FromEncoded(3, 0, 100));

        await AssertParityAsync(triples, 1U, null, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task HighFanOutPredicateMatches()
    {
        //One predicate connects many subject-object pairs; verify
        //"P only bound" parity at a non-trivial degree.
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < 25; s++)
        {
            for(uint o = 0; o < 25; o++)
            {
                triples.Add(EncodedTriple.FromEncoded(s, 99, o));
            }
        }
        triples.Add(EncodedTriple.FromEncoded(0, 1, 0));
        triples.Add(EncodedTriple.FromEncoded(0, 2, 0));

        await AssertParityAsync(triples, null, 99U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task MediumDensityExercisesSortedArrayRange()
    {
        //Twenty distinct subjects, each with one (predicate, object)
        //pair. Every "subject only" query exercises the SortedArray
        //descent path on the hypertrie side at moderate cardinality.
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < 20; s++)
        {
            triples.Add(EncodedTriple.FromEncoded(s, 1, s + 1000));
        }

        await AssertParityAsync(triples, null, null, null).ConfigureAwait(false);
        for(uint s = 0; s < 20; s++)
        {
            await AssertParityAsync(triples, s, null, null).ConfigureAwait(false);
        }
        await AssertParityAsync(triples, null, 1U, null).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task HighCardinalitySubjectsParity()
    {
        //Several thousand distinct subjects sharing one (predicate,
        //object) pair. With the SortedArray representation now
        //scaling without an upper bound, this exercises the "S
        //only" descent path at a degree that an earlier design
        //would have routed through a hash-table representation.
        const uint total = 4096;
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < total; s++)
        {
            triples.Add(EncodedTriple.FromEncoded(s, 1, 0));
        }

        await AssertParityAsync(triples, null, null, null).ConfigureAwait(false);
        await AssertParityAsync(triples, 0U, null, null).ConfigureAwait(false);
        await AssertParityAsync(triples, total - 1U, null, null).ConfigureAwait(false);
        await AssertParityAsync(triples, total / 2U, null, null).ConfigureAwait(false);
        await AssertParityAsync(triples, null, 1U, null).ConfigureAwait(false);
        await AssertParityAsync(triples, null, null, 0U).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task RangedStarPatternsAtAllRepresentationTiers()
    {
        //Walk a range that crosses through Empty, Inline, and
        //SortedArray at low, medium, and high cardinalities so each
        //region of the SortedArray growth path is covered.
        uint[] degrees = [0, 1, 2, 5, 16, 64, 100, 500, 2000];
        foreach(uint degree in degrees)
        {
            List<EncodedTriple> triples = [];
            for(uint o = 0; o < degree; o++)
            {
                triples.Add(EncodedTriple.FromEncoded(1, 50, o));
            }
            await AssertParityAsync(triples, 1U, 50U, null).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task DedupCollapsesSharedLeavesToOneCanonicalInstance()
    {
        //Property under test: when many subjects share an
        //identical predicate-object structure, the hypertrie's
        //bottom-up build interns one canonical depth-1 leaf for
        //each shared content set, regardless of how many subjects
        //use it.
        //
        //Construction: 50 subjects, each with the single triple
        //(s_i, predicate=1, object=42). Without dedup, every
        //subject would produce its own depth-1 leaf for {42} on
        //the S-first descent path, so depth-1 leaves alone would
        //number at least 50. With dedup, one canonical leaf for
        //{42} is shared by all subjects.
        const int subjectCount = 50;
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjectCount; s++)
        {
            triples.Add(EncodedTriple.FromEncoded(s, 1, 42));
        }

        using NodeStore store = new(VeritasHashing.Default);
        HypertrieGraphStore deduped = await HypertrieGraphStore.BuildAsync(triples, store, TestContext.CancellationToken).ConfigureAwait(false);

        //Sanity: matching still returns every triple.
        HashSet<EncodedTriple> matched = [.. deduped.Match(TermId.None, TermId.None, TermId.None)];
        Assert.HasCount(subjectCount, matched);

        Assert.IsLessThanOrEqualTo(subjectCount, store.Count,
            $"Total canonical node count ({store.Count}) is at or above the per-subject lower bound " +
            $"a non-deduped build would produce ({subjectCount}). Dedup is not firing.");
    }

    [TestMethod]
    public async Task IdenticalInputsProduceTheSameRootInstance()
    {
        //A second consequence of dedup: building the same input
        //twice into a shared store adds zero new canonical nodes
        //on the second build — every node finds an interned
        //instance.
        using NodeStore shared = new(VeritasHashing.Default);
        HypertrieGraphStore first = await HypertrieGraphStore.BuildAsync(SampleTriples, shared, TestContext.CancellationToken).ConfigureAwait(false);
        int afterFirstBuild = shared.Count;

        HypertrieGraphStore second = await HypertrieGraphStore.BuildAsync(SampleTriples, shared, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(afterFirstBuild, shared.Count, "Second build must add zero new canonical nodes.");
        Assert.AreEqual(first.Count, second.Count);
    }

    private async Task AssertParityAsync(IEnumerable<EncodedTriple> triples, uint? s, uint? p, uint? o)
    {
        EncodedTriple[] materialized = [.. triples];
        InMemoryGraphStore inMemory = InMemoryGraphStore.Build(materialized);
        HypertrieGraphStore hypertrie = await HypertrieGraphStore.BuildAsync(materialized, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        TermId subject = s.HasValue ? TermId.FromEncoded(s.Value) : TermId.None;
        TermId predicate = p.HasValue ? TermId.FromEncoded(p.Value) : TermId.None;
        TermId @object = o.HasValue ? TermId.FromEncoded(o.Value) : TermId.None;

        List<EncodedTriple> hypertrieList = [.. hypertrie.Match(subject, predicate, @object)];
        HashSet<EncodedTriple> hypertrieSet = [.. hypertrieList];
        HashSet<EncodedTriple> inMemorySet = [.. inMemory.Match(subject, predicate, @object)];

        Assert.HasCount(hypertrieSet.Count, hypertrieList);

        Assert.IsTrue(
            inMemorySet.SetEquals(hypertrieSet),
            $"Pattern (s={Format(s)}, p={Format(p)}, o={Format(o)}): " +
            $"InMemory yielded {inMemorySet.Count} distinct triple(s), " +
            $"Hypertrie yielded {hypertrieSet.Count} distinct triple(s).");
    }

    private static string Format(uint? value)
    {
        return value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
    }
}
