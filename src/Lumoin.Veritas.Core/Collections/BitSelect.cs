using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// The select-in-word primitive the succinct sequences share: the bit position
/// of the rank-th set bit within one 64-bit word — the inner step of every
/// sampled <c>select</c>. Hardware bit-deposit answers it in a few
/// instructions where available; the portable path clears the lowest set bit
/// <c>rank</c> times.
/// </summary>
internal static class BitSelect
{
    /// <summary>The bit position (0–63) of the <paramref name="rank"/>-th set bit (0-based) within a word. The caller guarantees the word carries more than <paramref name="rank"/> set bits; with fewer, both paths return 64.</summary>
    /// <param name="word">The word.</param>
    /// <param name="rank">The zero-based rank.</param>
    /// <returns>The in-word bit position.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int InWord(ulong word, int rank)
    {
        if(Bmi2.X64.IsSupported)
        {
            //Depositing a single bit at `rank` through the word as the mask
            //lands it exactly on the word's rank-th set bit; its trailing-zero
            //count is that bit's position.
            return BitOperations.TrailingZeroCount(Bmi2.X64.ParallelBitDeposit(1UL << rank, word));
        }

        for(int cleared = 0; cleared < rank; cleared++)
        {
            word &= word - 1;
        }

        return BitOperations.TrailingZeroCount(word);
    }
}
