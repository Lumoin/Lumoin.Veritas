using System;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The growable bit vector's contract: the default value owns no buffer and reads
/// clear everywhere, an append advances the count by exactly one bit, a set beyond
/// the end extends the vector with clear bits and keeps every bit already held, a
/// clear past the end is a no-op that never allocates, and both writes report the
/// flip rather than the final state. The checked indexer rejects an index outside
/// the count while the tolerant read answers clear there, the word span covers
/// exactly the words the count fills, and every bit at or beyond the count stays
/// zero so a word-parallel reduction reads the span with no tail mask. A scripted
/// mixed-operation row runs the four mutating operations against a
/// <c>bool[]</c> oracle, so a divergence that only appears after growth is caught
/// where the single-operation rows cannot see it.
/// </summary>
[TestClass]
internal sealed class GrowableBitVectorTests
{
    /// <summary>The default value owns no buffer, holds no bits, covers no words, reads clear at every index through the tolerant read, and rejects every index through the checked one.</summary>
    [TestMethod]
    public void DefaultVectorIsEmptyAndReadsFalse()
    {
        GrowableBitVector vector = default;
        int wordCount = vector.Words.Length;

        Assert.IsTrue(vector.IsEmpty, "The default vector owns no buffer.");
        Assert.AreEqual(0, vector.Count, "The default vector holds no bits.");
        Assert.AreEqual(0, wordCount, "The default vector covers no words.");
        Assert.IsFalse(vector.GetOrDefault(0), "The tolerant read answers clear at the first index.");
        Assert.IsFalse(vector.GetOrDefault(63), "The tolerant read answers clear at the first word's last index.");
        Assert.IsFalse(vector.GetOrDefault(64), "The tolerant read answers clear at the second word's first index.");
        Assert.IsFalse(vector.GetOrDefault(4_096), "The tolerant read answers clear far past the end.");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
        {
            _ = default(GrowableBitVector)[0];
        });
    }

    /// <summary>Every append advances the count by exactly one, and the appended pattern reads back bit for bit.</summary>
    [TestMethod]
    public void AppendTracksCountAndBits()
    {
        const int BitCount = 4_097;

        GrowableBitVector vector = default;
        for(int i = 0; i < BitCount; i++)
        {
            vector.Append(i % 2 == 0);
            Assert.AreEqual(i + 1, vector.Count, $"The count after appending the bit at {i}.");
        }

        for(int i = 0; i < BitCount; i++)
        {
            Assert.AreEqual(i % 2 == 0, vector[i], $"The appended bit at {i}.");
        }
    }

    /// <summary>Appending a clear bit leaves it clear: a run of clear appends followed by one set append reads clear everywhere but the last index.</summary>
    [TestMethod]
    public void AppendFalseLeavesBitClear()
    {
        const int ClearCount = 130;

        GrowableBitVector vector = default;
        for(int i = 0; i < ClearCount; i++)
        {
            vector.Append(false);
        }

        vector.Append(true);

        Assert.AreEqual(ClearCount + 1, vector.Count, "The count covers every appended bit.");
        for(int i = 0; i < ClearCount; i++)
        {
            Assert.IsFalse(vector[i], $"The clear appended bit at {i}.");
        }

        Assert.IsTrue(vector[ClearCount], "The single set appended bit.");
    }

    /// <summary>Bits set at the word and half-word boundaries round-trip, and every index the set leaves untouched reads clear.</summary>
    [TestMethod]
    public void WordBoundaryBitsRoundTrip()
    {
        int[] indexes = [0, 1, 63, 64, 65, 127, 128, 4_095, 4_096];

        GrowableBitVector vector = default;
        foreach(int index in indexes)
        {
            vector.Set(index);
        }

        Assert.AreEqual(4_097, vector.Count, "The highest set index fixes the count.");
        for(int i = 0; i < 4_097; i++)
        {
            Assert.AreEqual(Array.IndexOf(indexes, i) >= 0, vector[i], $"The bit at {i}.");
        }
    }

    /// <summary>A set beyond the end extends the vector, keeps every bit already held, and leaves the opened gap clear.</summary>
    [TestMethod]
    public void SetBeyondEndGrowsAndPreservesEarlierBits()
    {
        GrowableBitVector vector = default;
        vector.Set(0);
        vector.Set(4_096);
        vector.Set(63);

        Assert.AreEqual(4_097, vector.Count, "The highest set index fixes the count.");
        Assert.IsTrue(vector[0], "The bit set before the growth survives it.");
        Assert.IsTrue(vector[63], "The bit set after the growth is held.");
        Assert.IsTrue(vector[4_096], "The bit whose index drove the growth is held.");

        foreach(int index in (int[])[64, 100, 1_000, 4_095])
        {
            Assert.IsFalse(vector[index], $"The gap the growth opened reads clear at {index}.");
        }
    }

    /// <summary>A set reports the clear-to-set flip alone: the first set reports it, a repeat does not, and a set after a clear reports it again.</summary>
    [TestMethod]
    public void SetReportsOnlyTheClearToSetFlip()
    {
        GrowableBitVector vector = default;

        Assert.IsTrue(vector.Set(7), "The first set flips the bit.");
        Assert.IsFalse(vector.Set(7), "A repeated set flips nothing.");
        Assert.IsTrue(vector.Clear(7), "The clear flips the bit back.");
        Assert.IsTrue(vector.Set(7), "The set after a clear flips the bit again.");
    }

    /// <summary>A clear reports the set-to-clear flip alone: a clear of an in-range clear bit reports nothing, the clear after a set reports the flip, and a repeat does not.</summary>
    [TestMethod]
    public void ClearReportsOnlyTheSetToClearFlip()
    {
        GrowableBitVector vector = default;
        for(int i = 0; i < 8; i++)
        {
            vector.Append(false);
        }

        Assert.IsFalse(vector.Clear(7), "A clear of an in-range clear bit flips nothing.");
        Assert.IsTrue(vector.Set(7), "The set flips the bit.");
        Assert.IsTrue(vector.Clear(7), "The clear flips it back.");
        Assert.IsFalse(vector.Clear(7), "A repeated clear flips nothing.");
    }

    /// <summary>A clear at an index at or beyond the count is a no-op that neither advances the count nor allocates, on a populated vector and on the default one alike.</summary>
    [TestMethod]
    public void ClearPastEndIsNoOpAndDoesNotGrow()
    {
        GrowableBitVector vector = default;
        for(int i = 0; i < 8; i++)
        {
            vector.Append(true);
        }

        bool flipped = vector.Clear(10_000);
        int wordCount = vector.Words.Length;

        Assert.IsFalse(flipped, "A clear far past the end flips nothing.");
        Assert.AreEqual(8, vector.Count, "A clear past the end leaves the count alone.");
        Assert.AreEqual(1, wordCount, "A clear past the end grows no buffer.");

        GrowableBitVector empty = default;

        Assert.IsFalse(empty.Clear(0), "A clear on a vector that holds no bits flips nothing.");
        Assert.IsTrue(empty.IsEmpty, "A clear allocates no buffer.");
    }

    /// <summary>A clear touches one bit: the neighbours on both sides of a word boundary keep their value.</summary>
    [TestMethod]
    public void ClearLeavesNeighbourBitsIntact()
    {
        GrowableBitVector vector = default;
        vector.Set(63);
        vector.Set(64);
        vector.Set(65);

        Assert.IsTrue(vector.Clear(64), "The middle bit flips.");
        Assert.IsTrue(vector[63], "The bit below the word boundary survives.");
        Assert.IsFalse(vector[64], "The cleared bit is clear.");
        Assert.IsTrue(vector[65], "The bit above the cleared one survives.");
    }

    /// <summary>The tolerant read answers the held bits inside the count and clear at every index at or beyond it, and rejects a negative index.</summary>
    [TestMethod]
    public void GetOrDefaultReadsFalsePastTheEnd()
    {
        GrowableBitVector vector = HundredBitPattern();

        for(int i = 0; i < 100; i++)
        {
            Assert.AreEqual(i % 3 == 0, vector.GetOrDefault(i), $"The tolerant read inside the count at {i}.");
        }

        Assert.IsFalse(vector.GetOrDefault(100), "The tolerant read at the count answers clear.");
        Assert.IsFalse(vector.GetOrDefault(101), "The tolerant read past the count answers clear.");
        Assert.IsFalse(vector.GetOrDefault(int.MaxValue), "The tolerant read at the largest index answers clear without touching the buffer.");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
        {
            _ = HundredBitPattern().GetOrDefault(-1);
        });
    }

    /// <summary>The checked indexer answers inside the count and rejects a negative index, the count itself, and an index beyond it.</summary>
    [TestMethod]
    public void IndexerRejectsIndexOutsideTheCount()
    {
        GrowableBitVector vector = HundredBitPattern();

        Assert.AreEqual(99 % 3 == 0, vector[99], "The checked indexer answers at the last held index.");

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
        {
            _ = HundredBitPattern()[-1];
        });

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
        {
            _ = HundredBitPattern()[100];
        });

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
        {
            _ = HundredBitPattern()[101];
        });
    }

    /// <summary>Every bit at or beyond the count is zero, before and after a growth, so the word span's population count is exactly the bits written.</summary>
    [TestMethod]
    public void TailBitsBeyondCountStayClear()
    {
        GrowableBitVector vector = default;
        for(int i = 0; i < 65; i++)
        {
            vector.Append(true);
        }

        int wordCountBeforeGrowth = vector.Words.Length;

        Assert.AreEqual(2, wordCountBeforeGrowth, "Sixty-five bits fill two words.");
        Assert.AreEqual(65, BitsetOps.PopCount(vector.Words), "The tail of the second word is clear.");

        vector.Set(200);
        int wordCountAfterGrowth = vector.Words.Length;

        Assert.AreEqual(201, vector.Count, "The set beyond the end fixes the count.");
        Assert.AreEqual(4, wordCountAfterGrowth, "Two hundred and one bits fill four words.");
        Assert.AreEqual(66, BitsetOps.PopCount(vector.Words), "The growth introduced clear bits only.");
    }

    /// <summary>The word span covers exactly the words the count fills, at the count values straddling the word boundary.</summary>
    [TestMethod]
    public void WordsSpanCoversExactlyTheCountWords()
    {
        (int Count, int Words)[] expectations = [(1, 1), (64, 1), (65, 2), (128, 2), (129, 3)];

        GrowableBitVector vector = default;
        int emptyWordCount = vector.Words.Length;

        Assert.AreEqual(0, emptyWordCount, "A vector of no bits covers no words.");
        for(int i = 0; i < expectations.Length; i++)
        {
            while(vector.Count < expectations[i].Count)
            {
                vector.Append(true);
            }

            int wordCount = vector.Words.Length;

            Assert.AreEqual(expectations[i].Words, wordCount, $"The covering word count at {expectations[i].Count} bits.");
        }
    }

    /// <summary>A scripted run of the four mutating operations, with the past-end clear and the extend-by-one set pinned at fixed steps, agrees with a <c>bool[]</c> oracle on every flip flag, on the count, on the bit just touched, on the two indexes past the end, and on the whole population at every sixty-fourth step.</summary>
    [TestMethod]
    public void MixedOperationsMatchBoolArrayOracle()
    {
        const int StepCount = 4_096;
        const int OracleSize = 16_384;

        bool[] oracle = new bool[OracleSize];
        int oracleCount = 0;
        GrowableBitVector vector = default;
        ulong state = 0x243F6A8885A308D3UL;

        for(int step = 0; step < StepCount; step++)
        {
            state = Mix(state);

            int touched;
            if(step % 16 == 0)
            {
                int boundary = oracleCount;

                Assert.IsFalse(vector.Clear(boundary), $"The clear at the count flips nothing at step {step}.");
                touched = -1;
            }
            else if(step % 16 == 1)
            {
                int boundary = oracleCount;

                Assert.IsTrue(vector.Set(boundary), $"The set at the count flips the bit at step {step}.");
                oracle[boundary] = true;
                oracleCount = boundary + 1;
                touched = boundary;
            }
            else
            {
                int op = (int)(state & 3UL);
                int index = (int)((state >>> 8) % 8_192UL);
                if(op < 2)
                {
                    bool value = (state >>> 32 & 1UL) != 0UL;
                    vector.Append(value);
                    oracle[oracleCount] = value;
                    touched = oracleCount;
                    oracleCount++;
                }
                else if(op == 2)
                {
                    bool expected = !oracle[index];

                    Assert.AreEqual(expected, vector.Set(index), $"The set flip at index {index}, step {step}.");
                    oracle[index] = true;
                    if(index >= oracleCount)
                    {
                        oracleCount = index + 1;
                    }

                    touched = index;
                }
                else
                {
                    bool expected = index < oracleCount && oracle[index];

                    Assert.AreEqual(expected, vector.Clear(index), $"The clear flip at index {index}, step {step}.");
                    if(index < oracleCount)
                    {
                        oracle[index] = false;
                    }

                    touched = index < oracleCount ? index : -1;
                }
            }

            Assert.AreEqual(oracleCount, vector.Count, $"The count after step {step}.");
            if(touched >= 0)
            {
                Assert.AreEqual(oracle[touched], vector[touched], $"The bit at the touched index {touched}, step {step}.");
            }

            Assert.IsFalse(vector.GetOrDefault(oracleCount), $"The tolerant read at the count after step {step}.");
            Assert.IsFalse(vector.GetOrDefault(oracleCount + 1), $"The tolerant read past the count after step {step}.");

            if(step % 64 == 63 || step == StepCount - 1)
            {
                int mismatch = -1;
                for(int i = 0; i < oracleCount; i++)
                {
                    if(oracle[i] != vector[i])
                    {
                        mismatch = i;

                        break;
                    }
                }

                Assert.AreEqual(-1, mismatch, $"The whole population agreed with the oracle after step {step}, up to index {mismatch}.");
            }
        }
    }

    /// <summary>The first mutating call at a high index allocates exactly the words that index needs: the write lands, the count covers it, and every other probed index reads clear.</summary>
    [TestMethod]
    public void FirstSetAtHighIndexAllocatesTheCoveringWords()
    {
        GrowableBitVector vector = default;

        bool flipped = vector.Set(4_096);
        int wordCount = vector.Words.Length;

        Assert.IsTrue(flipped, "The first set flips the bit.");
        Assert.AreEqual(4_097, vector.Count, "The count covers the written index.");
        Assert.AreEqual(65, wordCount, "The first allocation covers exactly the words the index needs.");
        Assert.AreEqual(1, BitsetOps.PopCount(vector.Words), "Exactly one bit is set.");
        Assert.IsTrue(vector[4_096], "The written bit is set.");
        Assert.IsFalse(vector[0], "The first index reads clear.");
        Assert.IsFalse(vector[63], "The first word's last index reads clear.");
        Assert.IsFalse(vector[64], "The second word's first index reads clear.");
        Assert.IsFalse(vector[4_095], "The index below the written one reads clear.");
        Assert.IsFalse(vector.GetOrDefault(4_097), "The index past the count reads clear.");
    }

    /// <summary>A vector of one hundred bits whose set bits are the indexes divisible by three — the population the two read rows probe inside, at, and past.</summary>
    /// <returns>The populated vector.</returns>
    private static GrowableBitVector HundredBitPattern()
    {
        GrowableBitVector vector = default;
        for(int i = 0; i < 100; i++)
        {
            vector.Append(i % 3 == 0);
        }

        return vector;
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
