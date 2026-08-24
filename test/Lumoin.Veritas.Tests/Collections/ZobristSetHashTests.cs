using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The differential oracle for <see cref="ZobristSetHash"/>: the incremental
/// <see cref="ZobristSetHash.Toggle"/> walk must reproduce the from-scratch
/// <see cref="ZobristSetHash.Hash"/>, equal sets must hash equal regardless of the order they
/// are built in, toggling an element twice must be the identity, a seeded source must give a
/// reproducible table, and the all-zero source must collapse every hash to zero — the case that
/// makes the exact-confirmation contract load-bearing. The seeds are pinned so the sweep is
/// deterministic across runs and platforms.
/// </summary>
[TestClass]
internal sealed class ZobristSetHashTests
{
    /// <summary>The domain sizes (in bits) the sweep covers: partial tails, exact word multiples, and larger spans.</summary>
    private static int[] DomainSizes { get; } = [1, 2, 63, 64, 65, 100, 128, 129, 200, 256, 257, 500, 1024];

    /// <summary>The incremental toggle walk reproduces the from-scratch hash, and toggling an element twice is the identity, across the swept domain sizes.</summary>
    [TestMethod]
    public void IncrementalToggleMatchesFromScratchHash()
    {
        ulong state = 0x243F6A8885A308D3UL;
        int nonEmptySets = 0;

        foreach(int domainSize in DomainSizes)
        {
            ZobristSetHash hash = new(domainSize, VeritasRandomness.Seeded(0x9E3779B9UL));
            for(int round = 0; round < 50; round++)
            {
                ulong[] words = BuildRandomSet(domainSize, ref state, out List<int> ids);

                //The incremental hash, accumulated element by element from the empty set, equals
                //the hash computed from the packed words in one pass.
                ulong incremental = 0UL;
                foreach(int id in ids)
                {
                    incremental = hash.Toggle(incremental, id);
                }

                Assert.AreEqual(hash.Hash(words), incremental, $"Incremental vs from-scratch at domain {domainSize}, round {round}.");

                //Toggling any present element off and on again returns to the from-scratch hash:
                //exclusive-or is its own inverse.
                if(ids.Count > 0)
                {
                    nonEmptySets++;
                    int someId = ids[ids.Count / 2];
                    ulong scratch = hash.Hash(words);
                    Assert.AreEqual(scratch, hash.Toggle(hash.Toggle(scratch, someId), someId), $"Toggle involution at domain {domainSize}, round {round}.");
                }
            }
        }

        Assert.IsGreaterThan(0, nonEmptySets, "The sweep covers non-empty sets.");
    }

    /// <summary>The same set hashes equal whichever order its elements are toggled in — the hash depends on membership, not insertion order.</summary>
    [TestMethod]
    public void OrderOfTogglesDoesNotChangeHash()
    {
        ulong state = 0x13198A2E03707344UL;
        ZobristSetHash hash = new(300, VeritasRandomness.Seeded(0xA5A5A5A5UL));

        for(int round = 0; round < 40; round++)
        {
            _ = BuildRandomSet(300, ref state, out List<int> ids);

            ulong forward = 0UL;
            foreach(int id in ids)
            {
                forward = hash.Toggle(forward, id);
            }

            ulong reverse = 0UL;
            for(int i = ids.Count - 1; i >= 0; i--)
            {
                reverse = hash.Toggle(reverse, ids[i]);
            }

            Assert.AreEqual(forward, reverse, $"Order independence at round {round}.");
        }
    }

    /// <summary>The same seed produces the same table: two independently constructed instances agree on every element value and on random set hashes.</summary>
    [TestMethod]
    public void SameSeedIsReproducible()
    {
        const int domainSize = 256;
        ZobristSetHash first = new(domainSize, VeritasRandomness.Seeded(0xC0FFEEUL));
        ZobristSetHash second = new(domainSize, VeritasRandomness.Seeded(0xC0FFEEUL));

        for(int id = 0; id < domainSize; id++)
        {
            Assert.AreEqual(first.ValueOf(id), second.ValueOf(id), $"Pinned value {id}.");
        }

        ulong state = 0x082EFA98EC4E6C89UL;
        for(int round = 0; round < 30; round++)
        {
            ulong[] words = BuildRandomSet(domainSize, ref state, out _);
            Assert.AreEqual(first.Hash(words), second.Hash(words), $"Reproducible hash at round {round}.");
        }
    }

    /// <summary>Different seeds produce different tables: at least one pinned value differs.</summary>
    [TestMethod]
    public void DifferentSeedsProduceDifferentTables()
    {
        const int domainSize = 128;
        ZobristSetHash first = new(domainSize, VeritasRandomness.Seeded(1UL));
        ZobristSetHash second = new(domainSize, VeritasRandomness.Seeded(2UL));

        bool anyDifferent = false;
        for(int id = 0; id < domainSize; id++)
        {
            if(first.ValueOf(id) != second.ValueOf(id))
            {
                anyDifferent = true;

                break;
            }
        }

        Assert.IsTrue(anyDifferent, "Two distinct seeds yield distinct tables.");
    }

    /// <summary>
    /// The all-zero source collapses every hash to zero: the table carries no information, so
    /// distinct sets are indistinguishable by hash. This is the worst-case collision and the reason
    /// a consumer must confirm a hash match with an exact set comparison rather than trust the hash.
    /// </summary>
    [TestMethod]
    public void ZeroSourceCollapsesEveryHashToZero()
    {
        const int domainSize = 200;
        ZobristSetHash hash = new(domainSize, VeritasRandomness.Zero);

        for(int id = 0; id < domainSize; id++)
        {
            Assert.AreEqual(0UL, hash.ValueOf(id), $"Zero pinned value {id}.");
        }

        ulong state = 0xBE5466CF34E90C6CUL;
        for(int round = 0; round < 20; round++)
        {
            ulong[] words = BuildRandomSet(domainSize, ref state, out List<int> ids);
            Assert.AreEqual(0UL, hash.Hash(words), $"Collapsed hash at round {round}.");
            if(ids.Count > 0)
            {
                Assert.AreEqual(0UL, hash.Toggle(0UL, ids[0]), $"Collapsed toggle at round {round}.");
            }
        }
    }

    /// <summary>The empty domain hashes the empty set to zero and reports a zero word count.</summary>
    [TestMethod]
    public void EmptyDomainIsDegenerate()
    {
        ZobristSetHash hash = new(0, VeritasRandomness.Seeded(7UL));

        Assert.AreEqual(0, hash.DomainSize);
        Assert.AreEqual(0, hash.WordCount());
        Assert.AreEqual(0UL, hash.Hash([]));
    }

    /// <summary>A randomness source that under-delivers bytes is rejected rather than silently leaving a partial table.</summary>
    [TestMethod]
    public void ShortRandomnessSourceThrows()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = new ZobristSetHash(8, TruncatedBytes));
    }

    /// <summary>A randomness source that returns one byte fewer than asked for, exercising the under-delivery guard.</summary>
    /// <param name="request">The randomness request whose byte count is short-changed.</param>
    /// <returns>A bytes value one shorter than requested.</returns>
    private static RandomnessValue TruncatedBytes(in RandomnessRequest request)
    {
        return new RandomnessValue(RandomnessKind.Bytes, Double: default, default, new byte[Math.Max(0, request.ByteCount - 1)]);
    }

    /// <summary>Builds a random bitset over the domain and reports its member ids.</summary>
    /// <param name="domainSize">The domain size in bits.</param>
    /// <param name="state">The mixer state.</param>
    /// <param name="ids">Receives the member element ids in ascending order.</param>
    /// <returns>The packed words, tail-clean.</returns>
    private static ulong[] BuildRandomSet(int domainSize, ref ulong state, out List<int> ids)
    {
        ulong[] words = new ulong[BitsetOps.WordCount(domainSize)];
        ids = [];
        for(int i = 0; i < domainSize; i++)
        {
            state = Mix(state);
            if((state & 1UL) != 0UL)
            {
                BitsetOps.Set(words, i);
                ids.Add(i);
            }
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
