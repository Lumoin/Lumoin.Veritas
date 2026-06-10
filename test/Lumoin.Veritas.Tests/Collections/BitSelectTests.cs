using System.Collections.Generic;
using System.Numerics;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The select-in-word primitive: for every rank a word can answer, the result
/// is the position of the rank-th set bit — checked against an independent
/// clear-loop reference over structured and mixed words, so whichever path the
/// hardware takes (bit-deposit or the portable loop) agrees with the
/// definition.
/// </summary>
[TestClass]
internal sealed class BitSelectTests
{
    /// <summary>The reference select: clears the lowest set bit <paramref name="rank"/> times and returns the next set position.</summary>
    /// <param name="word">The word.</param>
    /// <param name="rank">The zero-based rank.</param>
    /// <returns>The in-word bit position.</returns>
    private static int ReferenceSelect(ulong word, int rank)
    {
        for(int cleared = 0; cleared < rank; cleared++)
        {
            word &= word - 1;
        }

        return BitOperations.TrailingZeroCount(word);
    }

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

    [TestMethod]
    public void AgreesWithTheReferenceOverEveryAnswerableRank()
    {
        //Structured words (empty-adjacent, saturated, alternating, single-bit
        //extremes) and a mixed sweep; every rank below the popcount must hit
        //the reference position exactly.
        List<ulong> words = [1UL, 1UL << 63, ulong.MaxValue, 0xAAAAAAAAAAAAAAAAUL, 0x5555555555555555UL, 0x8000000000000001UL];
        ulong state = 11;
        for(int i = 0; i < 200; i++)
        {
            state = Mix(state);
            words.Add(state);
        }

        foreach(ulong word in words)
        {
            int bits = BitOperations.PopCount(word);
            for(int rank = 0; rank < bits; rank++)
            {
                Assert.AreEqual(ReferenceSelect(word, rank), BitSelect.InWord(word, rank), $"Select disagreed on word {word:X16} rank {rank}");
            }
        }
    }
}
