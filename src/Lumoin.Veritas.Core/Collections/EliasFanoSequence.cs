using System;
using System.Diagnostics;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A monotone (non-decreasing) <c>uint</c> sequence stored in the Elias-Fano
/// quasi-succinct layout — near the information-theoretic floor for sorted
/// integers, ≈ <c>2 + log2(universe / count)</c> bits per value. Each value
/// splits into <see cref="LowBits"/> explicit low bits (a packed array) and the
/// remaining high bits, the high parts encoded as a bit-vector of "gaps" read
/// through <c>select₁</c>. Crucially it answers <see cref="NextGEQ"/>
/// (successor / lower bound) — the seek a worst-case-optimal descent needs —
/// so it is the inverted-index intersection primitive, not a write-once blob.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> For a value <c>v</c> at index <c>i</c>: the low part
/// <c>v &amp; (2^ℓ − 1)</c> sits at bit <c>i·ℓ</c> of <see cref="lower"/>; the
/// high part <c>v &gt;&gt; ℓ</c> contributes a set bit at position
/// <c>(v &gt;&gt; ℓ) + i</c> of <see cref="upper"/>. The upper vector thus has
/// exactly <see cref="Count"/> set bits among <c>count + (max &gt;&gt; ℓ) + 1</c>
/// bits, and <c>access(i)</c> recovers the high part as
/// <c>select₁(i) − i</c>.
/// </para>
/// <para>
/// <b>Access.</b> <see cref="Access"/> is one sampled <c>select₁</c> plus a low
/// read; <see cref="NextGEQ"/> binary-searches over <see cref="Access"/> (a
/// correct, log-time successor — the direct <c>select₀</c> jump is a later
/// sharpening). Single-writer at build, then read-only.
/// </para>
/// </remarks>
[DebuggerDisplay("EliasFanoSequence Count={Count} LowBits={LowBits} Bits={BitCount}")]
public sealed class EliasFanoSequence
{
    /// <summary>One in this many set bits is sampled into <see cref="oneSamples"/> so <c>select₁</c> starts its scan near the answer.</summary>
    private const int SelectSampleRate = 64;

    private readonly ulong[] lower;

    private readonly ulong[] upper;

    //For sample k: the word in `upper` holding the (k·SelectSampleRate)-th set
    //bit, and the exact number of set bits before that word — select₁ resumes
    //its popcount scan from there with the correct running count.
    private readonly int[] selectSampleWord;

    private readonly int[] selectSampleOnesBefore;

    private readonly uint lowMask;

    /// <summary>The number of low bits stored explicitly per value.</summary>
    public int LowBits { get; }

    /// <summary>The number of values in the sequence.</summary>
    public int Count { get; }

    /// <summary>The total footprint in bits — the lower payload, the upper bit-vector, and the select samples.</summary>
    public long BitCount => ((long)lower.Length * 64) + ((long)upper.Length * 64) + ((long)(selectSampleWord.Length + selectSampleOnesBefore.Length) * 32);

    private EliasFanoSequence(ulong[] lower, ulong[] upper, int[] selectSampleWord, int[] selectSampleOnesBefore, int lowBits, int count)
    {
        this.lower = lower;
        this.upper = upper;
        this.selectSampleWord = selectSampleWord;
        this.selectSampleOnesBefore = selectSampleOnesBefore;
        LowBits = lowBits;
        Count = count;
        lowMask = lowBits == 0 ? 0u : (uint)((1UL << lowBits) - 1);
    }

    /// <summary>
    /// Builds the sequence from a non-decreasing run of values.
    /// </summary>
    /// <param name="values">The values, non-decreasing; duplicates allowed.</param>
    /// <returns>The packed sequence.</returns>
    /// <exception cref="ArgumentException">The values are not non-decreasing.</exception>
    public static EliasFanoSequence Build(ReadOnlySpan<uint> values)
    {
        int count = values.Length;
        if(count == 0)
        {
            return new EliasFanoSequence([], [], [0], [0], 0, 0);
        }

        for(int i = 1; i < count; i++)
        {
            if(values[i] < values[i - 1])
            {
                throw new ArgumentException("Elias-Fano requires a non-decreasing sequence.", nameof(values));
            }
        }

        //Universe is max + 1; widen to ulong so max == uint.MaxValue does not wrap.
        ulong universe = (ulong)values[count - 1] + 1;

        //ℓ = max(0, floor(log2(universe / count))).
        int lowBits = 0;
        ulong ratio = universe / (ulong)count;
        if(ratio >= 2)
        {
            lowBits = 63 - BitOperations.LeadingZeroCount(ratio);
        }

        uint lowMask = lowBits == 0 ? 0u : (uint)((1UL << lowBits) - 1);

        //Lower payload: count · lowBits bits.
        int lowerWords = lowBits == 0 ? 0 : (int)(((long)count * lowBits + 63) >> 6);
        ulong[] lower = new ulong[lowerWords];

        //Upper bit-vector: count set bits among count + (max >> ℓ) + 1 positions.
        long highMax = (long)(values[count - 1] >> lowBits);
        long upperBits = count + highMax + 1;
        int upperWords = (int)((upperBits + 63) >> 6);
        ulong[] upper = new ulong[upperWords];

        for(int i = 0; i < count; i++)
        {
            uint value = values[i];

            if(lowBits != 0)
            {
                WriteLow(lower, (long)i * lowBits, value & lowMask, lowBits);
            }

            long position = (long)(value >> lowBits) + i;
            upper[(int)(position >> 6)] |= 1UL << (int)(position & 63);
        }

        //Sample every SelectSampleRate-th set bit: the word it lives in and the
        //number of set bits before that word, so select₁ resumes mid-vector
        //with an exact running count.
        int sampleCount = (count / SelectSampleRate) + 1;
        int[] selectSampleWord = new int[sampleCount];
        int[] selectSampleOnesBefore = new int[sampleCount];
        int ones = 0;
        int nextSample = 0;
        for(int word = 0; word < upperWords; word++)
        {
            int wordOnes = BitOperations.PopCount(upper[word]);
            while(nextSample < sampleCount && (long)nextSample * SelectSampleRate < ones + wordOnes)
            {
                selectSampleWord[nextSample] = word;
                selectSampleOnesBefore[nextSample] = ones;
                nextSample++;
            }

            ones += wordOnes;
        }

        for(; nextSample < sampleCount; nextSample++)
        {
            selectSampleWord[nextSample] = upperWords;
            selectSampleOnesBefore[nextSample] = ones;
        }

        return new EliasFanoSequence(lower, upper, selectSampleWord, selectSampleOnesBefore, lowBits, count);
    }

    /// <summary>The value at an index.</summary>
    /// <param name="index">The index, in <c>[0, Count)</c>.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public uint Access(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        long high = SelectOne(index) - index;
        uint low = LowBits == 0 ? 0u : ReadLow((long)index * LowBits);

        return (uint)(((ulong)high << LowBits) | low);
    }

    /// <summary>
    /// The smallest index whose value is greater than or equal to
    /// <paramref name="target"/>, or <see cref="Count"/> when none is.
    /// </summary>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    public int NextGEQ(uint target)
    {
        int low = 0;
        int high = Count;
        while(low < high)
        {
            int mid = low + ((high - low) >> 1);
            if(Access(mid) < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>The position of the <paramref name="index"/>-th set bit (0-based) in <see cref="upper"/>, resuming the popcount scan from the nearest sample.</summary>
    /// <param name="index">The zero-based rank of the set bit.</param>
    /// <returns>The bit position.</returns>
    private long SelectOne(int index)
    {
        int sample = index / SelectSampleRate;
        int word = selectSampleWord[sample];
        int ones = selectSampleOnesBefore[sample];

        while(true)
        {
            int wordOnes = BitOperations.PopCount(upper[word]);
            if(ones + wordOnes > index)
            {
                return ((long)word << 6) + SelectInWord(upper[word], index - ones);
            }

            ones += wordOnes;
            word++;
        }
    }

    /// <summary>The bit position (0–63) of the <paramref name="rank"/>-th set bit within a word.</summary>
    /// <param name="word">The word.</param>
    /// <param name="rank">The zero-based rank.</param>
    /// <returns>The in-word bit position.</returns>
    private static int SelectInWord(ulong word, int rank)
    {
        for(int cleared = 0; cleared < rank; cleared++)
        {
            word &= word - 1;
        }

        return BitOperations.TrailingZeroCount(word);
    }

    /// <summary>Reads <see cref="LowBits"/> bits at a bit offset of <see cref="lower"/>.</summary>
    /// <param name="bitOffset">The bit offset.</param>
    /// <returns>The low value.</returns>
    private uint ReadLow(long bitOffset)
    {
        int word = (int)(bitOffset >> 6);
        int shift = (int)(bitOffset & 63);

        ulong bits = lower[word] >> shift;
        if(shift + LowBits > 64)
        {
            bits |= lower[word + 1] << (64 - shift);
        }

        return (uint)(bits & lowMask);
    }

    /// <summary>Writes <paramref name="width"/> low bits at a bit offset of <paramref name="target"/>.</summary>
    /// <param name="target">The lower payload.</param>
    /// <param name="bitOffset">The bit offset.</param>
    /// <param name="value">The low value.</param>
    /// <param name="width">The low-bit width.</param>
    private static void WriteLow(ulong[] target, long bitOffset, uint value, int width)
    {
        int word = (int)(bitOffset >> 6);
        int shift = (int)(bitOffset & 63);

        target[word] |= (ulong)value << shift;
        if(shift + width > 64)
        {
            target[word + 1] |= (ulong)value >> (64 - shift);
        }
    }
}
