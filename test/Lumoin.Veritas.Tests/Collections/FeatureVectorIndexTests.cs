using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The differential oracle for <see cref="FeatureVectorIndex"/>: the feature-trie retrieval must
/// return exactly the same set of element ids as the naive all-pairs scan, in both subsumption
/// directions, across random corpora and queries over a range of domain sizes and bucket counts —
/// the equality is what proves the monotone-feature pruning loses no true match (no false
/// negatives) while the exact confirmation admits no false positive. A hand-built example pins the
/// two directions apart so an inverted relation cannot pass, and the degenerate corpora are covered
/// explicitly.
/// </summary>
[TestClass]
internal sealed class FeatureVectorIndexTests
{
    /// <summary>The domain sizes (in bits) the sweep covers: a single bit, exact and off-by-one word multiples, and larger multi-word spans.</summary>
    private static int[] DomainSizes { get; } = [1, 64, 65, 128, 200, 256];

    /// <summary>The bucket-count settings the sweep covers: the default, a single total-population feature, a few buckets, and a count that clamps to the word count.</summary>
    private static int?[] BucketCounts { get; } = [null, 1, 4, 32];

    /// <summary>The directions the sweep retrieves.</summary>
    private static SubsumptionDirection[] Directions { get; } = [SubsumptionDirection.Supersets, SubsumptionDirection.Subsets];

    /// <summary>The trie retrieval and the naive all-pairs scan agree on the exact id set for every direction across the swept domains, bucket counts, corpora, and queries.</summary>
    [TestMethod]
    public void DifferentialSweepMatchesNaiveRetrieval()
    {
        ulong state = 0x452821E638D01377UL;
        int supersetHits = 0;
        int subsetHits = 0;

        foreach(int domainSize in DomainSizes)
        {
            foreach(int? bucketCount in BucketCounts)
            {
                for(int round = 0; round < 25; round++)
                {
                    //A sparse query so genuine supersets exist in the corpus, and a corpus that
                    //mixes a copy of the query, a constructed strict superset and subset, and random
                    //sets of mixed density — so both directions see real hits and real misses.
                    ulong[] query = RandomSet(domainSize, oneInN: 4, ref state);
                    List<ReadOnlyMemory<ulong>> corpus =
                    [
                        ((ulong[])query.Clone()).AsMemory(),
                        Superset(query, domainSize, ref state).AsMemory(),
                        Subset(query, ref state).AsMemory(),
                        RandomSet(domainSize, oneInN: 2, ref state).AsMemory(),
                        RandomSet(domainSize, oneInN: 3, ref state).AsMemory(),
                        RandomSet(domainSize, oneInN: 8, ref state).AsMemory(),
                    ];

                    FeatureVectorIndex index = new(corpus, domainSize, bucketCount);

                    foreach(SubsumptionDirection direction in Directions)
                    {
                        List<int> trie = [];
                        index.Retrieve(query, direction, trie);

                        List<int> naive = [];
                        index.RetrieveNaive(query, direction, naive);

                        AssertSameIds(naive, trie, $"domain {domainSize}, buckets {bucketCount?.ToString(CultureInfo.InvariantCulture) ?? "default"}, round {round}, {direction}.");

                        if(direction == SubsumptionDirection.Supersets)
                        {
                            supersetHits += naive.Count;
                        }
                        else
                        {
                            subsetHits += naive.Count;
                        }
                    }
                }
            }
        }

        Assert.IsGreaterThan(0, supersetHits, "The sweep covers non-empty superset retrievals.");
        Assert.IsGreaterThan(0, subsetHits, "The sweep covers non-empty subset retrievals.");
    }

    /// <summary>A query with no super- or subset in a non-empty corpus retrieves nothing in either direction, and the trie agrees with the naive scan on that emptiness — the descend-and-confirm-rejects path.</summary>
    [TestMethod]
    public void NoMatchInNonEmptyCorpusRetrievesNothing()
    {
        const int domainSize = 128;
        List<ReadOnlyMemory<ulong>> corpus =
        [
            Bits(domainSize, 0).AsMemory(),
            Bits(domainSize, 1).AsMemory(),
            Bits(domainSize, 2, 3).AsMemory(),
        ];
        FeatureVectorIndex index = new(corpus, domainSize);

        //{64, 65} shares no bit with any stored set, and none of the stored sets is empty: no
        //stored set contains the query (supersets empty) and none is contained in it (subsets empty).
        ulong[] query = Bits(domainSize, 64, 65);

        foreach(SubsumptionDirection direction in Directions)
        {
            List<int> trie = [];
            index.Retrieve(query, direction, trie);

            List<int> naive = [];
            index.RetrieveNaive(query, direction, naive);

            Assert.IsEmpty(trie, $"trie retrieves nothing for {direction}.");
            AssertSameIds(naive, trie, $"no-match {direction}");
        }
    }

    /// <summary>A hand-built corpus retrieves the expected ids in each direction, with the two directions yielding different sets so an inverted relation cannot pass.</summary>
    [TestMethod]
    public void HandBuiltExampleRetrievesExpectedIds()
    {
        const int domainSize = 8;
        List<ReadOnlyMemory<ulong>> corpus =
        [
            Bits(domainSize).AsMemory(),              //0: {}
            Bits(domainSize, 0, 1).AsMemory(),        //1: {0,1}
            Bits(domainSize, 0, 1, 2, 3).AsMemory(),  //2: {0,1,2,3}
            Bits(domainSize, 2, 3).AsMemory(),        //3: {2,3}
        ];

        FeatureVectorIndex index = new(corpus, domainSize);
        ulong[] query = Bits(domainSize, 0, 1);

        List<int> supersets = [];
        index.Retrieve(query, SubsumptionDirection.Supersets, supersets);
        AssertSameIds([1, 2], supersets, "supersets of {0,1}");

        List<int> subsets = [];
        index.Retrieve(query, SubsumptionDirection.Subsets, subsets);
        AssertSameIds([0, 1], subsets, "subsets of {0,1}");
    }

    /// <summary>A query equal to a stored set retrieves that set in both directions: a set is both a subset and a superset of itself.</summary>
    [TestMethod]
    public void QueryEqualToStoredMatchesBothDirections()
    {
        const int domainSize = 100;
        ulong[] target = Bits(domainSize, 3, 17, 64, 99);
        List<ReadOnlyMemory<ulong>> corpus =
        [
            ((ulong[])target.Clone()).AsMemory(),
            Bits(domainSize, 3, 17).AsMemory(),
            Bits(domainSize, 3, 17, 64, 99, 50).AsMemory(),
        ];

        FeatureVectorIndex index = new(corpus, domainSize);

        List<int> supersets = [];
        index.Retrieve(target, SubsumptionDirection.Supersets, supersets);
        Assert.Contains(0, supersets, "The equal set is a superset of the query.");

        List<int> subsets = [];
        index.Retrieve(target, SubsumptionDirection.Subsets, subsets);
        Assert.Contains(0, subsets, "The equal set is a subset of the query.");
    }

    /// <summary>An empty corpus retrieves nothing in either direction.</summary>
    [TestMethod]
    public void EmptyCorpusRetrievesNothing()
    {
        FeatureVectorIndex index = new([], domainSize: 64);
        ulong[] query = Bits(64, 1, 2, 3);

        List<int> result = [];
        index.Retrieve(query, SubsumptionDirection.Supersets, result);
        index.Retrieve(query, SubsumptionDirection.Subsets, result);

        Assert.IsEmpty(result, "An empty corpus retrieves nothing.");
    }

    /// <summary>The empty domain holds only the empty set, which every query (also empty) both contains and is contained by.</summary>
    [TestMethod]
    public void EmptyDomainMatchesAllEmptySets()
    {
        List<ReadOnlyMemory<ulong>> corpus = [ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty];
        FeatureVectorIndex index = new(corpus, domainSize: 0);

        List<int> supersets = [];
        index.Retrieve([], SubsumptionDirection.Supersets, supersets);
        AssertSameIds([0, 1], supersets, "every empty set is a superset of the empty query");

        List<int> subsets = [];
        index.Retrieve([], SubsumptionDirection.Subsets, subsets);
        AssertSameIds([0, 1], subsets, "every empty set is a subset of the empty query");
    }

    /// <summary>A query whose word width does not match the domain is rejected.</summary>
    [TestMethod]
    public void MismatchedQueryWidthThrows()
    {
        FeatureVectorIndex index = new([Bits(64, 0).AsMemory()], domainSize: 64);
        ulong[] tooWide = new ulong[2];

        Assert.ThrowsExactly<ArgumentException>(() => index.Retrieve(tooWide, SubsumptionDirection.Supersets, []));
    }

    /// <summary>An element whose word width does not match the domain is rejected at build.</summary>
    [TestMethod]
    public void MismatchedElementWidthThrows()
    {
        List<ReadOnlyMemory<ulong>> corpus = [new ulong[2].AsMemory()];

        Assert.ThrowsExactly<ArgumentException>(() => _ = new FeatureVectorIndex(corpus, domainSize: 64));
    }

    /// <summary>A non-positive explicit bucket count is rejected.</summary>
    [TestMethod]
    public void NonPositiveBucketCountThrows()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new FeatureVectorIndex([], domainSize: 64, bucketCount: 0));
    }

    /// <summary>Sorts both id lists and asserts they hold exactly the same ids.</summary>
    /// <param name="expected">The expected ids.</param>
    /// <param name="actual">The actual ids.</param>
    /// <param name="context">The assertion context.</param>
    private static void AssertSameIds(List<int> expected, List<int> actual, string context)
    {
        List<int> expectedSorted = [.. expected];
        List<int> actualSorted = [.. actual];
        expectedSorted.Sort();
        actualSorted.Sort();

        Assert.HasCount(expectedSorted.Count, actualSorted, $"Id count at {context}.");
        for(int i = 0; i < expectedSorted.Count; i++)
        {
            Assert.AreEqual(expectedSorted[i], actualSorted[i], $"Id {i} at {context}.");
        }
    }

    /// <summary>Builds a bitset over the domain with the given member bits set.</summary>
    /// <param name="domainSize">The domain size in bits.</param>
    /// <param name="members">The bits to set.</param>
    /// <returns>The packed words.</returns>
    private static ulong[] Bits(int domainSize, params int[] members)
    {
        ulong[] words = new ulong[BitsetOps.WordCount(domainSize)];
        foreach(int member in members)
        {
            BitsetOps.Set(words, member);
        }

        return words;
    }

    /// <summary>Builds a random bitset over the domain, each bit set with probability 1/<paramref name="oneInN"/>.</summary>
    /// <param name="domainSize">The domain size in bits.</param>
    /// <param name="oneInN">The reciprocal density; larger is sparser.</param>
    /// <param name="state">The mixer state.</param>
    /// <returns>The packed words, tail-clean.</returns>
    private static ulong[] RandomSet(int domainSize, int oneInN, ref ulong state)
    {
        ulong[] words = new ulong[BitsetOps.WordCount(domainSize)];
        for(int i = 0; i < domainSize; i++)
        {
            state = Mix(state);
            if((int)(state % (ulong)oneInN) == 0)
            {
                BitsetOps.Set(words, i);
            }
        }

        return words;
    }

    /// <summary>Builds a strict-or-equal superset of <paramref name="source"/> by raising some clear bits.</summary>
    /// <param name="source">The base set.</param>
    /// <param name="domainSize">The domain size in bits.</param>
    /// <param name="state">The mixer state.</param>
    /// <returns>The superset words.</returns>
    private static ulong[] Superset(ulong[] source, int domainSize, ref ulong state)
    {
        ulong[] words = (ulong[])source.Clone();
        for(int i = 0; i < domainSize; i++)
        {
            state = Mix(state);
            if((state & 3UL) == 0UL)
            {
                BitsetOps.Set(words, i);
            }
        }

        return words;
    }

    /// <summary>Builds a strict-or-equal subset of <paramref name="source"/> by clearing some set bits.</summary>
    /// <param name="source">The base set.</param>
    /// <param name="state">The mixer state.</param>
    /// <returns>The subset words.</returns>
    private static ulong[] Subset(ulong[] source, ref ulong state)
    {
        ulong[] words = (ulong[])source.Clone();
        for(int wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            state = Mix(state);
            words[wordIndex] &= state;
        }

        return words;
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The state to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }
}
