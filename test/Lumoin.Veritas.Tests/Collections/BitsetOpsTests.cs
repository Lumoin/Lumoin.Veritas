using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The differential oracle for <see cref="BitsetOps"/>: the vectorised set operations,
/// their scalar references, and a naive <c>bool[]</c> set model must agree at every word
/// length — sparse, dense, all-ones, all-zeros, partial-tail and exact-word-multiple and
/// exact-vector-width-multiple sizes, and the empty set — with the tail invariant never
/// violated (no bit beyond the domain ever leaks into a result). The <c>bool[]</c> linear
/// model is the ground truth; the vectorised and scalar paths are both checked against it.
/// </summary>
[TestClass]
internal sealed class BitsetOpsTests
{
    /// <summary>The domain sizes (in bits) the sweep covers: partial tails, exact word and vector-width multiples, and larger spans.</summary>
    private static int[] DomainSizes { get; } = [1, 2, 63, 64, 65, 100, 127, 128, 129, 200, 256, 257, 320, 384, 512, 513, 700, 1024, 1025];

    /// <summary>The vectorised path, its scalar reference, and the naive model agree on every operation across the swept domain sizes; both verdicts and both populated/empty results occur.</summary>
    [TestMethod]
    public void DifferentialSweepMatchesNaiveSetSemantics()
    {
        ulong state = 0xD1B54A32D192ED03UL;
        int subsetTrue = 0;
        int subsetFalse = 0;
        int equalTrue = 0;
        int equalFalse = 0;
        int emptyTrue = 0;
        int nonEmptyTrue = 0;

        foreach(int domainSize in DomainSizes)
        {
            for(int round = 0; round < 40; round++)
            {
                //Half the rounds force b ⊇ a (and occasionally b == a) so the subset and
                //equality verdicts cover both branches, not only the overwhelmingly-likely
                //"not a subset" of two independent dense sets.
                bool[] a = NextPattern(domainSize, ref state);
                bool[] b = (round % 2 == 0) ? NextPattern(domainSize, ref state) : Superset(a, ref state, forceEqual: round % 6 == 1);

                ulong[] wordsA = Pack(a);
                ulong[] wordsB = Pack(b);

                bool naiveSubset = NaiveIsSubsetOf(a, b);
                Assert.AreEqual(naiveSubset, BitsetOps.IsSubsetOf(wordsA, wordsB), $"IsSubsetOf at domain {domainSize}, round {round}.");
                Assert.AreEqual(naiveSubset, BitsetOps.IsSubsetOfScalar(wordsA, wordsB), $"IsSubsetOfScalar at domain {domainSize}, round {round}.");
                if(naiveSubset)
                {
                    subsetTrue++;
                }
                else
                {
                    subsetFalse++;
                }

                bool naiveEqual = NaiveSetEquals(a, b);
                Assert.AreEqual(naiveEqual, BitsetOps.SetEquals(wordsA, wordsB), $"SetEquals at domain {domainSize}, round {round}.");
                Assert.AreEqual(naiveEqual, BitsetOps.SetEqualsScalar(wordsA, wordsB), $"SetEqualsScalar at domain {domainSize}, round {round}.");
                if(naiveEqual)
                {
                    equalTrue++;
                }
                else
                {
                    equalFalse++;
                }

                AssertCombine(a, b, domainSize, static (x, y) => x && y, BitsetOps.And, BitsetOps.AndScalar, "And");
                AssertCombine(a, b, domainSize, static (x, y) => x || y, BitsetOps.Or, BitsetOps.OrScalar, "Or");
                AssertCombine(a, b, domainSize, static (x, y) => x && !y, BitsetOps.AndNot, BitsetOps.AndNotScalar, "AndNot");

                bool naiveEmpty = NaiveIsEmpty(a);
                Assert.AreEqual(naiveEmpty, BitsetOps.IsEmpty(wordsA), $"IsEmpty at domain {domainSize}, round {round}.");
                Assert.AreEqual(naiveEmpty, BitsetOps.IsEmptyScalar(wordsA), $"IsEmptyScalar at domain {domainSize}, round {round}.");
                if(naiveEmpty)
                {
                    emptyTrue++;
                }
                else
                {
                    nonEmptyTrue++;
                }

                Assert.AreEqual(NaivePopCount(a), BitsetOps.PopCount(wordsA), $"PopCount at domain {domainSize}, round {round}.");
            }
        }

        Assert.IsGreaterThan(0, subsetTrue, "The sweep covers the subset-holds case.");
        Assert.IsGreaterThan(0, subsetFalse, "The sweep covers the subset-fails case.");
        Assert.IsGreaterThan(0, equalTrue, "The sweep covers the equal case.");
        Assert.IsGreaterThan(0, equalFalse, "The sweep covers the unequal case.");
        Assert.IsGreaterThan(0, emptyTrue, "The sweep covers the empty case.");
        Assert.IsGreaterThan(0, nonEmptyTrue, "The sweep covers the non-empty case.");
    }

    /// <summary>The empty (zero-word) bitset answers the degenerate operations without indexing.</summary>
    [TestMethod]
    public void EmptyDomainIsDegenerate()
    {
        Assert.AreEqual(0, BitsetOps.WordCount(0));

        ReadOnlySpan<ulong> empty = [];
        Assert.IsTrue(BitsetOps.IsEmpty(empty));
        Assert.IsTrue(BitsetOps.IsSubsetOf(empty, empty));
        Assert.IsTrue(BitsetOps.SetEquals(empty, empty));
        Assert.AreEqual(0, BitsetOps.PopCount(empty));
    }

    /// <summary>Bit access round-trips: set, read, clear over a hand-checked pattern, with the tail left clear.</summary>
    [TestMethod]
    public void BitAccessRoundTrips()
    {
        const int domainSize = 130;
        ulong[] words = new ulong[BitsetOps.WordCount(domainSize)];
        Assert.HasCount(3, words);

        int[] indices = [0, 1, 63, 64, 65, 128, 129];
        foreach(int index in indices)
        {
            BitsetOps.Set(words, index);
        }

        for(int i = 0; i < domainSize; i++)
        {
            Assert.AreEqual(Array.IndexOf(indices, i) >= 0, BitsetOps.Get(words, i), $"Get bit {i}.");
        }

        Assert.AreEqual(indices.Length, BitsetOps.PopCount(words));
        BitsetOps.Clear(words, 64);
        Assert.IsFalse(BitsetOps.Get(words, 64));
        Assert.AreEqual(indices.Length - 1, BitsetOps.PopCount(words));
    }

    /// <summary>MaskTail clears bits beyond the domain in the last word and leaves domain bits untouched.</summary>
    [TestMethod]
    public void MaskTailClearsBeyondDomain()
    {
        const int domainSize = 130;
        ulong[] words = [ulong.MaxValue, ulong.MaxValue, ulong.MaxValue];

        BitsetOps.MaskTail(words, domainSize);

        Assert.AreEqual(ulong.MaxValue, words[0]);
        Assert.AreEqual(ulong.MaxValue, words[1]);
        Assert.AreEqual((1UL << 2) - 1UL, words[2], "Only the two in-domain bits of the last word survive.");
        Assert.AreEqual(domainSize, BitsetOps.PopCount(words));

        //An exact word multiple has no tail to mask.
        ulong[] exact = [ulong.MaxValue, ulong.MaxValue];
        BitsetOps.MaskTail(exact, 128);
        Assert.AreEqual(128, BitsetOps.PopCount(exact));
    }

    /// <summary>Operations over mismatched word lengths are rejected rather than silently truncated.</summary>
    [TestMethod]
    public void MismatchedLengthsThrow()
    {
        ulong[] two = new ulong[2];
        ulong[] three = new ulong[3];

        Assert.ThrowsExactly<ArgumentException>(() => BitsetOps.And(two, three));
        Assert.ThrowsExactly<ArgumentException>(() => BitsetOps.Or(two, three));
        Assert.ThrowsExactly<ArgumentException>(() => BitsetOps.AndNot(two, three));
        Assert.ThrowsExactly<ArgumentException>(() => _ = BitsetOps.IsSubsetOf(two, three));
        Assert.ThrowsExactly<ArgumentException>(() => _ = BitsetOps.SetEquals(two, three));
    }

    /// <summary>Applies a combining op (vectorised and scalar) to a copy of <paramref name="a"/> and asserts both match the bitwise model and keep the tail clear.</summary>
    /// <param name="a">The first pattern.</param>
    /// <param name="b">The second pattern.</param>
    /// <param name="domainSize">The domain size in bits.</param>
    /// <param name="combine">The per-bit set operation.</param>
    /// <param name="vectorized">The vectorised in-place op.</param>
    /// <param name="scalar">The scalar reference in-place op.</param>
    /// <param name="name">The op name, for assertion messages.</param>
    private static void AssertCombine(bool[] a, bool[] b, int domainSize, Func<bool, bool, bool> combine, CombineOp vectorized, CombineOp scalar, string name)
    {
        bool[] expected = new bool[domainSize];
        for(int i = 0; i < domainSize; i++)
        {
            expected[i] = combine(a[i], b[i]);
        }

        ulong[] vectorWords = Pack(a);
        vectorized(vectorWords, Pack(b));
        AssertBits(expected, vectorWords, $"{name} (vectorised) at domain {domainSize}.");

        ulong[] scalarWords = Pack(a);
        scalar(scalarWords, Pack(b));
        AssertBits(expected, scalarWords, $"{name} (scalar) at domain {domainSize}.");
    }

    /// <summary>An in-place bitset combining operation.</summary>
    /// <param name="target">The accumulator.</param>
    /// <param name="other">The other set.</param>
    private delegate void CombineOp(Span<ulong> target, ReadOnlySpan<ulong> other);

    /// <summary>Asserts the words hold exactly the expected bits over the domain and nothing in the tail.</summary>
    /// <param name="expected">The expected pattern.</param>
    /// <param name="words">The actual words.</param>
    /// <param name="message">The assertion context.</param>
    private static void AssertBits(bool[] expected, ulong[] words, string message)
    {
        for(int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], BitsetOps.Get(words, i), $"{message} bit {i}.");
        }

        Assert.AreEqual(NaiveCount(expected), BitsetOps.PopCount(words), $"{message} tail leaked (population mismatch).");
    }

    /// <summary>A random bit pattern of the given size.</summary>
    /// <param name="domainSize">The size in bits.</param>
    /// <param name="state">The mixer state.</param>
    /// <returns>The pattern.</returns>
    private static bool[] NextPattern(int domainSize, ref ulong state)
    {
        bool[] bits = new bool[domainSize];
        for(int i = 0; i < domainSize; i++)
        {
            state = Mix(state);
            bits[i] = (state & 1UL) != 0UL;
        }

        return bits;
    }

    /// <summary>A superset of <paramref name="a"/> — each clear bit may be raised — optionally exactly equal.</summary>
    /// <param name="a">The base pattern.</param>
    /// <param name="state">The mixer state.</param>
    /// <param name="forceEqual">When set, returns a copy of <paramref name="a"/>.</param>
    /// <returns>The superset.</returns>
    private static bool[] Superset(bool[] a, ref ulong state, bool forceEqual)
    {
        bool[] bits = new bool[a.Length];
        for(int i = 0; i < a.Length; i++)
        {
            state = Mix(state);
            bits[i] = a[i] || (!forceEqual && (state & 1UL) != 0UL);
        }

        return bits;
    }

    /// <summary>Packs a pattern into words, leaving the tail clear.</summary>
    /// <param name="bits">The pattern.</param>
    /// <returns>The words.</returns>
    private static ulong[] Pack(bool[] bits)
    {
        ulong[] words = new ulong[BitsetOps.WordCount(bits.Length)];
        for(int i = 0; i < bits.Length; i++)
        {
            if(bits[i])
            {
                BitsetOps.Set(words, i);
            }
        }

        return words;
    }

    /// <summary>The naive subset test over the patterns.</summary>
    /// <param name="subset">The candidate subset.</param>
    /// <param name="superset">The candidate superset.</param>
    /// <returns><see langword="true"/> when every set bit of the subset is in the superset.</returns>
    private static bool NaiveIsSubsetOf(bool[] subset, bool[] superset)
    {
        for(int i = 0; i < subset.Length; i++)
        {
            if(subset[i] && !superset[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The naive equality test over the patterns.</summary>
    /// <param name="first">The first pattern.</param>
    /// <param name="second">The second pattern.</param>
    /// <returns><see langword="true"/> when the patterns are identical.</returns>
    private static bool NaiveSetEquals(bool[] first, bool[] second)
    {
        for(int i = 0; i < first.Length; i++)
        {
            if(first[i] != second[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The naive emptiness test over the pattern.</summary>
    /// <param name="bits">The pattern.</param>
    /// <returns><see langword="true"/> when no bit is set.</returns>
    private static bool NaiveIsEmpty(bool[] bits)
    {
        foreach(bool bit in bits)
        {
            if(bit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The naive population count over the pattern.</summary>
    /// <param name="bits">The pattern.</param>
    /// <returns>The number of set bits.</returns>
    private static int NaivePopCount(bool[] bits)
    {
        return NaiveCount(bits);
    }

    /// <summary>Counts the set bits in a pattern.</summary>
    /// <param name="bits">The pattern.</param>
    /// <returns>The count.</returns>
    private static int NaiveCount(bool[] bits)
    {
        int count = 0;
        foreach(bool bit in bits)
        {
            if(bit)
            {
                count++;
            }
        }

        return count;
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
