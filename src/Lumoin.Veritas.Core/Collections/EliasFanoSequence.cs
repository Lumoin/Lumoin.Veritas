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
/// through <c>select₁</c>. It answers <see cref="NextGEQ"/> (successor / lower
/// bound) — the seek a worst-case-optimal descent needs — by a <c>select₀</c>
/// jump to the target's high group plus a short low-bit scan, so it is the
/// inverted-index intersection primitive, not a write-once blob.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> For a value <c>v</c> at index <c>i</c>: the low part
/// <c>v &amp; (2^ℓ − 1)</c> sits at bit <c>i·ℓ</c> of <see cref="lower"/>; the
/// high part <c>v &gt;&gt; ℓ</c> contributes a set bit at position
/// <c>(v &gt;&gt; ℓ) + i</c> of <see cref="upper"/>. The upper vector has
/// exactly <see cref="Count"/> set bits and <c>maxHigh + 1</c> unset bits (one
/// per possible high value, the group separators); <c>access(i)</c> recovers
/// the high part as <c>select₁(i) − i</c>, and the elements of high group
/// <c>h</c> occupy indices <c>[select₀(h−1) − (h−1), select₀(h) − h)</c>.
/// </para>
/// <para>
/// <b>Access / seek.</b> <see cref="Access"/> is one sampled <c>select₁</c>
/// plus a low read; <see cref="NextGEQ"/> jumps to the target's high group via
/// <c>select₀</c> and scans that group's low bits. Both selects resume from a
/// sample, so each is O(1) amortised. Single-writer at build, then read-only.
/// </para>
/// </remarks>
[DebuggerDisplay("EliasFanoSequence Count={Count} LowBits={LowBits} Bits={BitCount}")]
public sealed partial class EliasFanoSequence
{
    /// <summary>The default select-sample rate: one in this many marker bits is sampled so a select resumes its scan near the answer.</summary>
    private const int DefaultSelectSampleRate = 64;

    //One in this many marker bits is sampled (the select-density knob): sparser
    //shrinks the sample arrays but lengthens each select's resume scan.
    private readonly int selectSampleRate;

    //The injected bulk lane unpacker for Decode's low-bit runs, or null for the
    //portable per-value path; supplied by a caller owning vectorised kernels.
    private readonly BitLaneUnpacker? laneUnpacker;

    private readonly ulong[] lower;

    private readonly ulong[] upper;

    //Per sample: the word holding the (k·rate)-th set bit and the set-bit count
    //before that word — select₁ resumes there with an exact running count.
    private readonly int[] oneSampleWord;

    private readonly int[] oneSampleOnesBefore;

    //The same for the unset (separator) bits, driving select₀.
    private readonly int[] zeroSampleWord;

    private readonly int[] zeroSampleZerosBefore;

    private readonly uint lowMask;

    //Bits actually used in `upper` (count + maxHigh + 1); bits beyond are
    //padding and must not read as separator zeros.
    private readonly long upperBits;

    private readonly uint maxHigh;

    /// <summary>The number of low bits stored explicitly per value.</summary>
    public int LowBits { get; }

    /// <summary>The number of values in the sequence.</summary>
    public int Count { get; }

    /// <summary>The total footprint in bits — the lower payload, the upper bit-vector, and the select samples.</summary>
    public long BitCount =>
        ((long)lower.Length * 64)
        + ((long)upper.Length * 64)
        + ((long)(oneSampleWord.Length + oneSampleOnesBefore.Length + zeroSampleWord.Length + zeroSampleZerosBefore.Length) * 32);

    private EliasFanoSequence(
        ulong[] lower,
        ulong[] upper,
        int[] oneSampleWord,
        int[] oneSampleOnesBefore,
        int[] zeroSampleWord,
        int[] zeroSampleZerosBefore,
        long upperBits,
        uint maxHigh,
        int lowBits,
        int count,
        int selectSampleRate,
        BitLaneUnpacker? laneUnpacker)
    {
        this.selectSampleRate = selectSampleRate;
        this.laneUnpacker = laneUnpacker;
        this.lower = lower;
        this.upper = upper;
        this.oneSampleWord = oneSampleWord;
        this.oneSampleOnesBefore = oneSampleOnesBefore;
        this.zeroSampleWord = zeroSampleWord;
        this.zeroSampleZerosBefore = zeroSampleZerosBefore;
        this.upperBits = upperBits;
        this.maxHigh = maxHigh;
        LowBits = lowBits;
        Count = count;
        lowMask = lowBits == 0 ? 0u : (uint)((1UL << lowBits) - 1);
    }

    /// <summary>Builds the sequence from a non-decreasing run of values.</summary>
    /// <param name="values">The values, non-decreasing; duplicates allowed.</param>
    /// <param name="selectSampleRate">One in this many marker bits is sampled to bound a select's resume scan; sparser shrinks the sample footprint and lengthens select.</param>
    /// <param name="lanePacker">A bulk lane packer for the lower payload (the payload is exactly its lane layout), or <see langword="null"/> to pack portably. Supplied by a caller owning vectorised kernels.</param>
    /// <param name="laneUnpacker">A bulk lane unpacker retained for <see cref="Decode"/>'s low-bit runs, or <see langword="null"/> to read portably.</param>
    /// <returns>The packed sequence.</returns>
    /// <exception cref="ArgumentException">The values are not non-decreasing.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="selectSampleRate"/> is less than 1.</exception>
    public static EliasFanoSequence Build(
        ReadOnlySpan<uint> values,
        int selectSampleRate = DefaultSelectSampleRate,
        BitLanePacker? lanePacker = null,
        BitLaneUnpacker? laneUnpacker = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(selectSampleRate, 1);

        int count = values.Length;
        if(count == 0)
        {
            return new EliasFanoSequence([], [], [0], [0], [0], [0], 0, 0, 0, 0, selectSampleRate, laneUnpacker);
        }

        for(int i = 1; i < count; i++)
        {
            if(values[i] < values[i - 1])
            {
                throw new ArgumentException("Elias-Fano requires a non-decreasing sequence.", nameof(values));
            }
        }

        ulong universe = (ulong)values[count - 1] + 1;

        int lowBits = 0;
        ulong ratio = universe / (ulong)count;
        if(ratio >= 2)
        {
            lowBits = 63 - BitOperations.LeadingZeroCount(ratio);
        }

        //High-part shifts go through ulong: a uint >> 32 would mask the count to
        //0 and not shift, which lowBits == 32 (a single near-universe value) hits.
        uint maxHigh = (uint)((ulong)values[count - 1] >> lowBits);

        int lowerWords = lowBits == 0 ? 0 : (int)(((long)count * lowBits + 63) >> 6);
        ulong[] lower = new ulong[lowerWords];

        long upperBits = (long)count + maxHigh + 1;
        int upperWords = (int)((upperBits + 63) >> 6);
        ulong[] upper = new ulong[upperWords];

        //The lower payload is a packed lane array — value i's low bits at
        //payload bits [i·ℓ, (i+1)·ℓ), little-endian within each word — so an
        //injected packer builds it in one whole-column call (it masks to the
        //lane width itself); without one the portable loop packs it.
        if(lowBits != 0)
        {
            if(lanePacker is null)
            {
                uint lowMask = (uint)((1UL << lowBits) - 1);
                for(int i = 0; i < count; i++)
                {
                    WriteLow(lower, (long)i * lowBits, values[i] & lowMask, lowBits);
                }
            }
            else
            {
                lanePacker(values, lowBits, lower);
            }
        }

        for(int i = 0; i < count; i++)
        {
            long position = (long)((ulong)values[i] >> lowBits) + i;
            upper[(int)(position >> 6)] |= 1UL << (int)(position & 63);
        }

        (int[] oneSampleWord, int[] oneSampleOnesBefore) = BuildSamples(upper, upperWords, count, upperBits, selectSampleRate, ones: true);
        (int[] zeroSampleWord, int[] zeroSampleZerosBefore) = BuildSamples(upper, upperWords, (int)(maxHigh + 1), upperBits, selectSampleRate, ones: false);

        return new EliasFanoSequence(lower, upper, oneSampleWord, oneSampleOnesBefore, zeroSampleWord, zeroSampleZerosBefore, upperBits, maxHigh, lowBits, count, selectSampleRate, laneUnpacker);
    }

    /// <summary>Samples every <paramref name="sampleRate"/>-th set (or unset) bit: the word it lives in and the marker count before that word.</summary>
    /// <param name="upper">The bit-vector.</param>
    /// <param name="upperWords">The word count.</param>
    /// <param name="markerCount">The total set (or unset) bits.</param>
    /// <param name="upperBits">The valid bit length, so padding is not counted.</param>
    /// <param name="sampleRate">One in this many markers is sampled.</param>
    /// <param name="ones"><see langword="true"/> to sample set bits, <see langword="false"/> for unset.</param>
    /// <returns>The sample word and the running marker count per sample.</returns>
    private static (int[] Word, int[] Before) BuildSamples(ulong[] upper, int upperWords, int markerCount, long upperBits, int sampleRate, bool ones)
    {
        int sampleCount = (markerCount / sampleRate) + 1;
        int[] sampleWord = new int[sampleCount];
        int[] sampleBefore = new int[sampleCount];
        int markers = 0;
        int nextSample = 0;
        for(int word = 0; word < upperWords; word++)
        {
            int wordMarkers = ones ? BitOperations.PopCount(upper[word]) : BitOperations.PopCount(ZeroBits(upper, word, upperBits));
            while(nextSample < sampleCount && (long)nextSample * sampleRate < markers + wordMarkers)
            {
                sampleWord[nextSample] = word;
                sampleBefore[nextSample] = markers;
                nextSample++;
            }

            markers += wordMarkers;
        }

        for(; nextSample < sampleCount; nextSample++)
        {
            sampleWord[nextSample] = upperWords;
            sampleBefore[nextSample] = markers;
        }

        return (sampleWord, sampleBefore);
    }

    /// <summary>The word's unset bits with the trailing padding (bits at or beyond <paramref name="upperBits"/>) forced set, so they do not read as separator zeros.</summary>
    /// <param name="upper">The bit-vector.</param>
    /// <param name="word">The word index.</param>
    /// <param name="upperBits">The valid bit length.</param>
    /// <returns>A word whose set bits are exactly the valid unset bits.</returns>
    private static ulong ZeroBits(ulong[] upper, int word, long upperBits)
    {
        ulong bits = upper[word];
        long validBits = upperBits - ((long)word << 6);
        if(validBits < 64)
        {
            bits |= ~((1UL << (int)validBits) - 1);
        }

        return ~bits;
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
    /// <paramref name="target"/>, or <see cref="Count"/> when none is — a
    /// <c>select₀</c> jump to the target's high group then a low-bit scan.
    /// </summary>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    public int NextGEQ(uint target)
    {
        if(Count == 0)
        {
            return 0;
        }

        uint high = (uint)((ulong)target >> LowBits);
        if(high > maxHigh)
        {
            return Count;
        }

        //The elements of high group `high` occupy [start, end); every later
        //group is strictly greater than the target, every earlier strictly less.
        int start = high == 0 ? 0 : (int)(SelectZero((int)high - 1) - (high - 1));
        if(start >= Count)
        {
            return Count;
        }

        int end = (int)(SelectZero((int)high) - high);
        if(end > Count)
        {
            end = Count;
        }

        uint low = LowBits == 0 ? 0u : (target & lowMask);
        for(int index = start; index < end; index++)
        {
            uint candidateLow = LowBits == 0 ? 0u : ReadLow((long)index * LowBits);
            if(candidateLow >= low)
            {
                return index;
            }
        }

        return end;
    }

    /// <summary>
    /// Decodes the contiguous run <c>[start, start + count)</c> into
    /// <paramref name="destination"/> in a single linear pass: the high parts
    /// come from one forward walk of <see cref="upper"/> (each separator zero
    /// advances the high group), the low parts from the packed
    /// <see cref="lower"/> payload — so a whole run costs one upper-vector walk
    /// rather than <paramref name="count"/> independent selects.
    /// </summary>
    /// <param name="start">The first index, in <c>[0, Count]</c>.</param>
    /// <param name="count">The number of values to decode; <c>start + count</c> must not exceed <see cref="Count"/>.</param>
    /// <param name="destination">Receives the values; at least <paramref name="count"/> long.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range falls outside the sequence.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than the run.</exception>
    public void Decode(int start, int count, Span<uint> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, Count);
        if(destination.Length < count)
        {
            throw new ArgumentException("The destination is shorter than the requested run.", nameof(destination));
        }

        if(count == 0)
        {
            return;
        }

        //The low parts first. With an injected unpacker: a scalar head until
        //the lane offset reaches a word boundary, then one bulk call unpacks
        //the rest of the run (a zero frame base makes it a pure unpack);
        //without one, the portable per-value read covers the whole run.
        if(LowBits == 0)
        {
            destination[..count].Clear();
        }
        else
        {
            int k = 0;
            while(k < count && (laneUnpacker is null || (((long)(start + k) * LowBits) & 63) != 0))
            {
                destination[k] = ReadLow((long)(start + k) * LowBits);
                k++;
            }

            if(k < count)
            {
                int word = (int)(((long)(start + k) * LowBits) >> 6);
                laneUnpacker!(lower.AsSpan(word), LowBits, 0, destination[k..count]);
            }
        }

        //Seed the high part from one sampled select; thereafter it advances by
        //the separator (zero) bits between consecutive set bits, so the run is
        //one upper-vector walk, not `count` selects.
        long position = SelectOne(start);
        long high = position - start;

        for(int k = 0; k < count; k++)
        {
            destination[k] = (uint)(((ulong)high << LowBits) | destination[k]);

            position++;
            while(position < upperBits && (upper[(int)(position >> 6)] & (1UL << (int)(position & 63))) == 0)
            {
                high++;
                position++;
            }
        }
    }

    /// <summary>The position of the <paramref name="rank"/>-th set bit (0-based) in <see cref="upper"/>.</summary>
    /// <param name="rank">The zero-based rank of the set bit.</param>
    /// <returns>The bit position.</returns>
    private long SelectOne(int rank)
    {
        int sample = rank / selectSampleRate;
        int word = oneSampleWord[sample];
        int ones = oneSampleOnesBefore[sample];

        while(true)
        {
            int wordOnes = BitOperations.PopCount(upper[word]);
            if(ones + wordOnes > rank)
            {
                return ((long)word << 6) + BitSelect.InWord(upper[word], rank - ones);
            }

            ones += wordOnes;
            word++;
        }
    }

    /// <summary>The position of the <paramref name="rank"/>-th unset (separator) bit (0-based) in <see cref="upper"/>.</summary>
    /// <param name="rank">The zero-based rank of the unset bit.</param>
    /// <returns>The bit position.</returns>
    private long SelectZero(int rank)
    {
        int sample = rank / selectSampleRate;
        int word = zeroSampleWord[sample];
        int zeros = zeroSampleZerosBefore[sample];

        while(true)
        {
            ulong zeroBits = ZeroBits(upper, word, upperBits);
            int wordZeros = BitOperations.PopCount(zeroBits);
            if(zeros + wordZeros > rank)
            {
                return ((long)word << 6) + BitSelect.InWord(zeroBits, rank - zeros);
            }

            zeros += wordZeros;
            word++;
        }
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

    /// <summary>Writes <paramref name="width"/> low bits at a bit offset of <paramref name="target"/> — the portable lane pack used when no <see cref="BitLanePacker"/> is injected.</summary>
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
