using System;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The rank/select bit-vector's contract: <c>Access</c>, <c>Rank1</c>/<c>Rank0</c>,
/// and <c>Select1</c>/<c>Select0</c> agree with a plain bit array at every
/// position and rank — across sparse, balanced, and dense fills, all-ones,
/// all-zeros, off-word and off-superblock lengths, and the empty vector — with
/// padding bits never leaking into the counts. A linear scan is the oracle.
/// </summary>
[TestClass]
internal sealed class RankSelectBitVectorTests
{
    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
    /// <param name="state">The counter to mix.</param>
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

    /// <summary>Packs a bool pattern into payload words, leaving padding bits clear.</summary>
    /// <param name="bits">The pattern.</param>
    /// <returns>The packed words.</returns>
    private static ulong[] Pack(bool[] bits)
    {
        ulong[] words = new ulong[(bits.Length + 63) >> 6];
        for(int i = 0; i < bits.Length; i++)
        {
            if(bits[i])
            {
                words[i >> 6] |= 1UL << (i & 63);
            }
        }

        return words;
    }

    /// <summary>Builds the vector under every rank-scan mode and asserts every operation against a linear scan of the pattern — the vector-popcount mode exercises its hardware lane where supported and its fallback elsewhere.</summary>
    /// <param name="bits">The pattern.</param>
    /// <param name="selectSampleRate">The select-sample rate to build with.</param>
    private static void AssertMatchesOracle(bool[] bits, int selectSampleRate = 512)
    {
        foreach(RankScanMode mode in (ReadOnlySpan<RankScanMode>)[RankScanMode.Sequential, RankScanMode.Unrolled, RankScanMode.VectorPopCount])
        {
            AssertMatchesOracleUnderMode(bits, selectSampleRate, mode);
        }
    }

    /// <summary>Builds the vector under one rank-scan mode and asserts every operation against a linear scan of the pattern.</summary>
    /// <param name="bits">The pattern.</param>
    /// <param name="selectSampleRate">The select-sample rate to build with.</param>
    /// <param name="mode">The rank-scan mode to build with.</param>
    private static void AssertMatchesOracleUnderMode(bool[] bits, int selectSampleRate, RankScanMode mode)
    {
        RankSelectBitVector vector = RankSelectBitVector.Build(Pack(bits), bits.Length, selectSampleRate, mode);

        int oneCount = 0;
        foreach(bool bit in bits)
        {
            if(bit)
            {
                oneCount++;
            }
        }

        Assert.AreEqual(bits.Length, vector.Length);
        Assert.AreEqual(oneCount, vector.OneCount);
        Assert.AreEqual(bits.Length - oneCount, vector.ZeroCount);

        int ones = 0;
        for(int position = 0; position < bits.Length; position++)
        {
            Assert.AreEqual(bits[position], vector.Access(position), $"Access disagreed at {position} ({mode})");
            Assert.AreEqual(ones, vector.Rank1(position), $"Rank1 disagreed at {position} ({mode})");
            Assert.AreEqual(position - ones, vector.Rank0(position), $"Rank0 disagreed at {position} ({mode})");
            if(bits[position])
            {
                Assert.AreEqual(position, vector.Select1(ones), $"Select1 disagreed at rank {ones} ({mode})");
            }
            else
            {
                Assert.AreEqual(position, vector.Select0(position - ones), $"Select0 disagreed at rank {position - ones} ({mode})");
            }

            if(bits[position])
            {
                ones++;
            }
        }

        Assert.AreEqual(oneCount, vector.Rank1(bits.Length));
        Assert.AreEqual(bits.Length - oneCount, vector.Rank0(bits.Length));
    }

    /// <summary>A pattern of the given length where each bit is set with roughly the given per-mille density.</summary>
    /// <param name="length">The bit length.</param>
    /// <param name="densityPerMille">Set-bit density in 0–1000.</param>
    /// <param name="seed">The mixer seed.</param>
    /// <returns>The pattern.</returns>
    private static bool[] Pattern(int length, int densityPerMille, ulong seed)
    {
        bool[] bits = new bool[length];
        ulong state = seed;
        for(int i = 0; i < length; i++)
        {
            state = Mix(state);
            bits[i] = (state % 1000) < (ulong)densityPerMille;
        }

        return bits;
    }

    [TestMethod]
    public void SparseBalancedAndDenseFillsMatchOracle()
    {
        foreach(int density in (int[])[10, 500, 990])
        {
            AssertMatchesOracle(Pattern(5_000, density, (ulong)density));
        }
    }

    [TestMethod]
    public void OffWordAndOffSuperblockLengthsMatchOracle()
    {
        //Lengths straddling word and superblock boundaries, where the padding
        //masks and the directory's tail entry earn their keep.
        foreach(int length in (int[])[1, 63, 64, 65, 511, 512, 513, 1023, 1025])
        {
            AssertMatchesOracle(Pattern(length, 500, (ulong)length));
        }
    }

    [TestMethod]
    public void SparseSelectSamplesStillAnswerExactly()
    {
        //A sample rate far below the marker count forces multi-word resume
        //scans in both selects.
        AssertMatchesOracle(Pattern(4_096, 500, 11), selectSampleRate: 8);
        AssertMatchesOracle(Pattern(4_096, 500, 13), selectSampleRate: 4_096);
    }

    [TestMethod]
    public void AllOnesAndAllZeros()
    {
        bool[] allOnes = new bool[700];
        Array.Fill(allOnes, true);
        AssertMatchesOracle(allOnes);
        AssertMatchesOracle(new bool[700]);
    }

    [TestMethod]
    public void EmptyVectorRanksZeroAndRejectsSelects()
    {
        RankSelectBitVector vector = RankSelectBitVector.Build([], 0);

        Assert.AreEqual(0, vector.Length);
        Assert.AreEqual(0, vector.Rank1(0));
        Assert.AreEqual(0, vector.Rank0(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select1(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select0(0));
    }

    [TestMethod]
    public void PaddingBitsBeyondTheLengthDoNotLeak()
    {
        //Garbage set bits beyond the valid length must vanish at build: the
        //counts and every operation see exactly the 70 valid bits.
        ulong[] payload = new ulong[2];
        payload[0] = 0xAAAAAAAAAAAAAAAAUL;
        payload[1] = ulong.MaxValue;

        RankSelectBitVector vector = RankSelectBitVector.Build(payload, 70);

        Assert.AreEqual(70, vector.Length);
        Assert.AreEqual(32 + 6, vector.OneCount);
        Assert.AreEqual(32, vector.ZeroCount);
        Assert.AreEqual(69, vector.Select1(vector.OneCount - 1));
        Assert.AreEqual(62, vector.Select0(vector.ZeroCount - 1));
    }

    [TestMethod]
    public void RejectsMismatchedPayloadAndBadArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RankSelectBitVector.Build(new ulong[2], 64));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RankSelectBitVector.Build([], -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RankSelectBitVector.Build(new ulong[1], 64, 0));

        RankSelectBitVector vector = RankSelectBitVector.Build([ulong.MaxValue], 64);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Access(64));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Rank1(65));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select1(64));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select0(0));
    }

    [TestMethod]
    public void FootprintOverheadStaysSmall()
    {
        //1M bits at half density: payload n bits, directory n/512 ints, the
        //two sample arrays together ≈ n/512 ints — assert under 1.2 n overall.
        const int Length = 1_000_000;
        bool[] bits = Pattern(Length, 500, 17);
        RankSelectBitVector vector = RankSelectBitVector.Build(Pack(bits), Length);

        Assert.IsLessThan(1.2 * Length, (double)vector.BitCount);
    }
}
