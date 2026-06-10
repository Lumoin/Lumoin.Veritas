using System;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The block-compressed bit-vector's contract: <c>Access</c>,
/// <c>Rank1</c>/<c>Rank0</c>, and <c>Select1</c>/<c>Select0</c> agree with a plain
/// bit array at every position and rank — across sparse, balanced, and dense
/// fills, all-ones, all-zeros, off-block and off-superblock lengths, and the
/// empty vector — with padding bits never leaking into the counts. A linear scan
/// is the oracle, and the rank/select sibling is a second independent oracle for
/// a differential check.
/// </summary>
[TestClass]
internal sealed class BlockCompressedBitVectorTests
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

    /// <summary>Builds the vector and asserts every operation against a linear scan of the pattern.</summary>
    /// <param name="bits">The pattern.</param>
    private static void AssertMatchesOracle(bool[] bits)
    {
        BlockCompressedBitVector vector = BlockCompressedBitVector.Build(Pack(bits), bits.Length);

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
            Assert.AreEqual(bits[position], vector.Access(position), $"Access disagreed at {position}");
            Assert.AreEqual(ones, vector.Rank1(position), $"Rank1 disagreed at {position}");
            Assert.AreEqual(position - ones, vector.Rank0(position), $"Rank0 disagreed at {position}");
            if(bits[position])
            {
                Assert.AreEqual(position, vector.Select1(ones), $"Select1 disagreed at rank {ones}");
            }
            else
            {
                Assert.AreEqual(position, vector.Select0(position - ones), $"Select0 disagreed at rank {position - ones}");
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
    public void DensitySpectrumMatchesOracle()
    {
        //1%, 5%, 25%, 50%, and 95% fills exercise the full class range, from the
        //single-pattern all-zero and all-set classes through the wide middle.
        foreach(int density in (int[])[10, 50, 250, 500, 950])
        {
            AssertMatchesOracle(Pattern(4_500, density, (ulong)density));
        }
    }

    [TestMethod]
    public void OffBlockAndOffSuperblockLengthsMatchOracle()
    {
        //Lengths straddling the 15-bit block boundary and the 32-block
        //(480-bit) superblock boundary, where the partial final block, the
        //padding masks, and the directory's tail entry earn their keep.
        foreach(int length in (int[])[1, 14, 15, 16, 449, 450, 451, 479, 480, 481, 4_500])
        {
            AssertMatchesOracle(Pattern(length, 500, (ulong)length));
        }
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
        BlockCompressedBitVector vector = BlockCompressedBitVector.Build([], 0);

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
        //counts and every operation see exactly the 70 valid bits, with the
        //last block (positions 60..69) reading only its valid prefix.
        ulong[] payload = new ulong[2];
        payload[0] = 0xAAAAAAAAAAAAAAAAUL;
        payload[1] = ulong.MaxValue;

        BlockCompressedBitVector vector = BlockCompressedBitVector.Build(payload, 70);

        Assert.AreEqual(70, vector.Length);
        Assert.AreEqual(32 + 6, vector.OneCount);
        Assert.AreEqual(32, vector.ZeroCount);
        Assert.AreEqual(69, vector.Select1(vector.OneCount - 1));
        Assert.AreEqual(62, vector.Select0(vector.ZeroCount - 1));
    }

    [TestMethod]
    public void RejectsMismatchedPayloadAndBadArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BlockCompressedBitVector.Build(new ulong[2], 64));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BlockCompressedBitVector.Build([], -1));
        Assert.ThrowsExactly<ArgumentNullException>(() => BlockCompressedBitVector.Build(null!, 0));

        BlockCompressedBitVector vector = BlockCompressedBitVector.Build([ulong.MaxValue], 64);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Access(64));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Access(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Rank1(65));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Rank1(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select1(64));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select1(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vector.Select0(0));
    }

    [TestMethod]
    public void AgreesWithRankSelectSiblingAcrossTheSpectrum()
    {
        //A second independent index built over the identical payload: every
        //Access, Rank, and Select must return the same answer from both, so a
        //bug in either index — directory, walk, or enumerative codec — surfaces.
        foreach(int length in (int[])[15, 480, 481, 4_500])
        {
            foreach(int density in (int[])[10, 250, 500, 950])
            {
                bool[] bits = Pattern(length, density, ((ulong)length << 16) ^ (ulong)density);

                BlockCompressedBitVector compressed = BlockCompressedBitVector.Build(Pack(bits), length);
                RankSelectBitVector plain = RankSelectBitVector.Build(Pack(bits), length);

                Assert.AreEqual(plain.Length, compressed.Length);
                Assert.AreEqual(plain.OneCount, compressed.OneCount);
                Assert.AreEqual(plain.ZeroCount, compressed.ZeroCount);

                for(int position = 0; position < length; position++)
                {
                    Assert.AreEqual(plain.Access(position), compressed.Access(position), $"Access differed at {position} (len {length}, density {density})");
                    Assert.AreEqual(plain.Rank1(position), compressed.Rank1(position), $"Rank1 differed at {position} (len {length}, density {density})");
                    Assert.AreEqual(plain.Rank0(position), compressed.Rank0(position), $"Rank0 differed at {position} (len {length}, density {density})");
                }

                Assert.AreEqual(plain.Rank1(length), compressed.Rank1(length));
                Assert.AreEqual(plain.Rank0(length), compressed.Rank0(length));

                for(int rank = 0; rank < plain.OneCount; rank++)
                {
                    Assert.AreEqual(plain.Select1(rank), compressed.Select1(rank), $"Select1 differed at rank {rank} (len {length}, density {density})");
                }

                for(int rank = 0; rank < plain.ZeroCount; rank++)
                {
                    Assert.AreEqual(plain.Select0(rank), compressed.Select0(rank), $"Select0 differed at rank {rank} (len {length}, density {density})");
                }
            }
        }
    }

    [TestMethod]
    public void SkewedFillStaysBelowTheUncompressedFootprint()
    {
        //A 1% fill is far below the order-zero entropy of a balanced vector, so
        //the class-plus-offset form must store it in well under one bit per
        //input bit — and below the rank/select sibling's whole-structure size.
        const int Length = 100_000;
        bool[] bits = Pattern(Length, 10, 23);

        BlockCompressedBitVector compressed = BlockCompressedBitVector.Build(Pack(bits), Length);
        RankSelectBitVector plain = RankSelectBitVector.Build(Pack(bits), Length);

        Assert.IsLessThan((double)Length, (double)compressed.BitCount);
        Assert.IsLessThan(plain.BitCount, compressed.BitCount);
    }
}
