using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A read-only bit-vector answering <c>rank</c> — the number of set (or unset)
/// bits before a position — through a superblock count directory, and
/// <c>select</c> — the position of the k-th set (or unset) bit — through
/// sampled marker positions, the counting primitives succinct sequences build
/// on. Single-writer at build, then read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> The payload is the caller's packed word array, little-endian
/// within each 64-bit word. The rank directory stores the absolute set-bit
/// count before every 512-bit superblock, so <see cref="Rank1"/> is one
/// directory read plus at most eight popcounts. Each select kind samples every
/// <c>selectSampleRate</c>-th marker's exact bit position; a select resumes a
/// word walk there, recovering its running count from the sampled word's low
/// bits, so the scan is bounded by the sample spacing.
/// </para>
/// </remarks>
[DebuggerDisplay("RankSelectBitVector Length={Length} OneCount={OneCount}")]
public sealed class RankSelectBitVector
{
    /// <summary>The number of bits per rank superblock; the directory stores one absolute count per superblock.</summary>
    private const int SuperblockBits = 512;

    /// <summary>The number of 64-bit words per rank superblock.</summary>
    private const int SuperblockWords = SuperblockBits / 64;

    /// <summary>The default select-sample rate: one in this many marker bits has its position sampled so a select resumes its scan near the answer.</summary>
    private const int DefaultSelectSampleRate = 512;

    //One in this many marker bits is sampled (the select-density knob): sparser
    //shrinks the sample arrays but lengthens each select's resume scan.
    private readonly int selectSampleRate;

    //How Rank1 counts the superblock-relative word run — the measured
    //per-deployment knob; every mode returns identical counts.
    private readonly RankScanMode rankScanMode;

    private readonly ulong[] words;

    //Set bits before each 512-bit superblock — the absolute tier of the rank
    //directory; one extra entry covers a rank query at the vector's end.
    private readonly int[] superblockOnes;

    //The exact bit position of every (k·rate)-th set bit — select₁ resumes
    //its word walk there.
    private readonly int[] oneSamplePosition;

    //The same for the unset bits, driving select₀.
    private readonly int[] zeroSamplePosition;

    /// <summary>The number of bits in the vector.</summary>
    public int Length { get; }

    /// <summary>The number of set bits in the vector.</summary>
    public int OneCount { get; }

    /// <summary>The number of unset bits in the vector.</summary>
    public int ZeroCount => Length - OneCount;

    /// <summary>The total footprint in bits — the payload words, the rank directory, and the select samples.</summary>
    public long BitCount =>
        ((long)words.Length * 64)
        + ((long)superblockOnes.Length * 32)
        + ((long)(oneSamplePosition.Length + zeroSamplePosition.Length) * 32);

    private RankSelectBitVector(
        ulong[] words,
        int[] superblockOnes,
        int[] oneSamplePosition,
        int[] zeroSamplePosition,
        int length,
        int oneCount,
        int selectSampleRate,
        RankScanMode rankScanMode)
    {
        this.words = words;
        this.superblockOnes = superblockOnes;
        this.oneSamplePosition = oneSamplePosition;
        this.zeroSamplePosition = zeroSamplePosition;
        this.selectSampleRate = selectSampleRate;
        this.rankScanMode = rankScanMode;
        Length = length;
        OneCount = oneCount;
    }

    /// <summary>Builds the vector over a packed word payload, taking ownership of the array.</summary>
    /// <param name="payload">The packed bits, little-endian within each word; the vector owns the array from here on and clears any padding bits beyond <paramref name="bitLength"/>. Its length must be exactly the words <paramref name="bitLength"/> fills.</param>
    /// <param name="bitLength">The number of valid bits.</param>
    /// <param name="selectSampleRate">One in this many marker bits has its position sampled to bound a select's resume scan; sparser shrinks the sample footprint and lengthens select.</param>
    /// <param name="rankScanMode">How <see cref="Rank1"/> counts the superblock-relative word run; every mode returns identical counts.</param>
    /// <returns>The built vector.</returns>
    /// <exception cref="ArgumentException">The payload length does not match <paramref name="bitLength"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitLength"/> is negative or <paramref name="selectSampleRate"/> is less than 1.</exception>
    public static RankSelectBitVector Build(ulong[] payload, int bitLength, int selectSampleRate = DefaultSelectSampleRate, RankScanMode rankScanMode = RankScanMode.Sequential)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentOutOfRangeException.ThrowIfNegative(bitLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(selectSampleRate, 1);

        int wordCount = (bitLength + 63) >> 6;
        if(payload.Length != wordCount)
        {
            throw new ArgumentException("The payload length does not match the bit length.", nameof(payload));
        }

        //Padding bits beyond the valid length are forced clear so whole-word
        //popcounts in the rank directory and the one-samples never see them;
        //zero-side counting masks them back out via ZeroBits.
        int paddingBits = (wordCount << 6) - bitLength;
        if(paddingBits > 0)
        {
            payload[wordCount - 1] &= ulong.MaxValue >> paddingBits;
        }

        int[] superblockOnes = new int[(wordCount >> 3) + 1];
        int ones = 0;
        for(int word = 0; word < wordCount; word++)
        {
            if((word & (SuperblockWords - 1)) == 0)
            {
                superblockOnes[word >> 3] = ones;
            }

            ones += BitOperations.PopCount(payload[word]);
        }

        if((wordCount & (SuperblockWords - 1)) == 0)
        {
            superblockOnes[wordCount >> 3] = ones;
        }

        int[] oneSamplePosition = BuildSamples(payload, wordCount, ones, bitLength, selectSampleRate, ones: true);
        int[] zeroSamplePosition = BuildSamples(payload, wordCount, bitLength - ones, bitLength, selectSampleRate, ones: false);

        return new RankSelectBitVector(payload, superblockOnes, oneSamplePosition, zeroSamplePosition, bitLength, ones, selectSampleRate, rankScanMode);
    }

    /// <summary>Samples every <paramref name="sampleRate"/>-th set (or unset) bit's exact position.</summary>
    /// <param name="words">The payload words, padding already cleared.</param>
    /// <param name="wordCount">The word count.</param>
    /// <param name="markerCount">The total set (or unset) bits.</param>
    /// <param name="bitLength">The valid bit length, so padding is not counted as zeros.</param>
    /// <param name="sampleRate">One in this many markers is sampled.</param>
    /// <param name="ones"><see langword="true"/> to sample set bits, <see langword="false"/> for unset.</param>
    /// <returns>The sampled bit positions.</returns>
    private static int[] BuildSamples(ulong[] words, int wordCount, int markerCount, int bitLength, int sampleRate, bool ones)
    {
        if(markerCount == 0)
        {
            return [];
        }

        int sampleCount = ((markerCount - 1) / sampleRate) + 1;
        int[] samplePosition = new int[sampleCount];
        int markers = 0;
        int nextSample = 0;
        for(int word = 0; word < wordCount && nextSample < sampleCount; word++)
        {
            ulong markerBits = ones ? words[word] : ZeroBits(words, word, bitLength);
            int wordMarkers = BitOperations.PopCount(markerBits);
            while(nextSample < sampleCount && (long)nextSample * sampleRate < markers + wordMarkers)
            {
                samplePosition[nextSample] = (word << 6) + BitSelect.InWord(markerBits, (int)(((long)nextSample * sampleRate) - markers));
                nextSample++;
            }

            markers += wordMarkers;
        }

        return samplePosition;
    }

    /// <summary>The word's unset bits with the trailing padding (bits at or beyond <paramref name="bitLength"/>) forced set, so they do not read as zeros.</summary>
    /// <param name="words">The payload words.</param>
    /// <param name="word">The word index.</param>
    /// <param name="bitLength">The valid bit length.</param>
    /// <returns>A word whose set bits are exactly the valid unset bits.</returns>
    private static ulong ZeroBits(ulong[] words, int word, int bitLength)
    {
        ulong bits = words[word];
        int validBits = bitLength - (word << 6);
        if(validBits < 64)
        {
            bits |= ~((1UL << validBits) - 1);
        }

        return ~bits;
    }

    /// <summary>Whether the bit at a position is set.</summary>
    /// <param name="position">The bit position, in <c>[0, Length)</c>.</param>
    /// <returns><see langword="true"/> when set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public bool Access(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Length);

        return (words[position >> 6] & (1UL << (position & 63))) != 0;
    }

    /// <summary>The number of set bits strictly before a position — one directory read plus at most eight popcounts.</summary>
    /// <param name="position">The exclusive end position, in <c>[0, Length]</c>.</param>
    /// <returns>The set-bit count in <c>[0, position)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public int Rank1(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Length);

        int word = position >> 6;
        int ones = superblockOnes[word >> 3] + ScanSuperblockRun(word & ~(SuperblockWords - 1), word);

        int bit = position & 63;
        if(bit != 0)
        {
            ones += BitOperations.PopCount(words[word] & ((1UL << bit) - 1));
        }

        return ones;
    }

    /// <summary>The set bits in the words <c>[start, end)</c> — the superblock-relative run below a rank position — counted per the configured <see cref="RankScanMode"/>.</summary>
    /// <param name="start">The run's first word — the superblock boundary.</param>
    /// <param name="end">The run's exclusive end word; at most <see cref="SuperblockWords"/> beyond <paramref name="start"/>.</param>
    /// <returns>The set-bit count of the run.</returns>
    private int ScanSuperblockRun(int start, int end) => rankScanMode switch
    {
        RankScanMode.Unrolled => ScanUnrolled(start, end),
        RankScanMode.VectorPopCount => ScanVectorPopCount(start, end),
        _ => ScanSequential(start, end),
    };

    /// <summary>The run's set bits via one serially accumulated popcount per word.</summary>
    /// <param name="start">The run's first word.</param>
    /// <param name="end">The run's exclusive end word.</param>
    /// <returns>The set-bit count of the run.</returns>
    private int ScanSequential(int start, int end)
    {
        int ones = 0;
        for(int w = start; w < end; w++)
        {
            ones += BitOperations.PopCount(words[w]);
        }

        return ones;
    }

    /// <summary>The run's set bits via two independent accumulators, breaking the serial add chain.</summary>
    /// <param name="start">The run's first word.</param>
    /// <param name="end">The run's exclusive end word.</param>
    /// <returns>The set-bit count of the run.</returns>
    private int ScanUnrolled(int start, int end)
    {
        int first = 0;
        int second = 0;
        int w = start;
        for(; w + 1 < end; w += 2)
        {
            first += BitOperations.PopCount(words[w]);
            second += BitOperations.PopCount(words[w + 1]);
        }

        if(w < end)
        {
            first += BitOperations.PopCount(words[w]);
        }

        return first + second;
    }

    /// <summary>The run's set bits via one 512-bit shuffle-table popcount over the whole superblock, lanes at or beyond the run's end masked to zero; the hardware path needs <see cref="Avx512BW"/> and the full superblock in bounds, so the final partial superblock — and hardware without the instruction set — takes <see cref="ScanUnrolled"/>.</summary>
    /// <param name="start">The run's first word — the superblock boundary.</param>
    /// <param name="end">The run's exclusive end word.</param>
    /// <returns>The set-bit count of the run.</returns>
    private int ScanVectorPopCount(int start, int end)
    {
        if(Avx512BW.IsSupported && start + SuperblockWords <= words.Length)
        {
            Vector512<ulong> block = Vector512.LoadUnsafe(in words[start]);
            Vector512<ulong> laneIndex = Vector512.Create(0UL, 1UL, 2UL, 3UL, 4UL, 5UL, 6UL, 7UL);
            Vector512<ulong> inRun = Vector512.LessThan(laneIndex, Vector512.Create((ulong)(end - start)));
            Vector512<byte> masked = (block & inRun).AsByte();

            //Shuffle-table popcount: each byte splits into nibbles, the table
            //maps a nibble to its set-bit count, and the per-byte counts reduce
            //through the absolute-difference sum against zero into 64-bit lane
            //totals.
            Vector128<byte> nibbleCounts = Vector128.Create((byte)0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4);
            Vector512<byte> table = Vector512.Create(Vector256.Create(nibbleCounts, nibbleCounts), Vector256.Create(nibbleCounts, nibbleCounts));
            Vector512<byte> lowMask = Vector512.Create((byte)0x0F);
            Vector512<byte> low = Avx512BW.Shuffle(table, masked & lowMask);
            Vector512<byte> high = Avx512BW.Shuffle(table, Avx512BW.ShiftRightLogical(masked.AsUInt16(), 4).AsByte() & lowMask);

            return (int)Vector512.Sum(Avx512BW.SumAbsoluteDifferences(low + high, Vector512<byte>.Zero));
        }

        return ScanUnrolled(start, end);
    }

    /// <summary>The number of unset bits strictly before a position.</summary>
    /// <param name="position">The exclusive end position, in <c>[0, Length]</c>.</param>
    /// <returns>The unset-bit count in <c>[0, position)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public int Rank0(int position)
    {
        return position - Rank1(position);
    }

    /// <summary>The position of the <paramref name="rank"/>-th set bit (0-based) — a sampled resume point plus a bounded word walk.</summary>
    /// <param name="rank">The zero-based rank, in <c>[0, OneCount)</c>.</param>
    /// <returns>The bit position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is out of range.</exception>
    public int Select1(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rank, OneCount);

        int samplePosition = oneSamplePosition[rank / selectSampleRate];
        int word = samplePosition >> 6;

        //The sampled marker's rank is exact, so the set bits in its word below
        //it recover the running count at the word's start.
        int ones = ((rank / selectSampleRate) * selectSampleRate)
            - BitOperations.PopCount(words[word] & ((1UL << (samplePosition & 63)) - 1));

        while(true)
        {
            int wordOnes = BitOperations.PopCount(words[word]);
            if(ones + wordOnes > rank)
            {
                return (word << 6) + BitSelect.InWord(words[word], rank - ones);
            }

            ones += wordOnes;
            word++;
        }
    }

    /// <summary>The position of the <paramref name="rank"/>-th unset bit (0-based) — a sampled resume point plus a bounded word walk.</summary>
    /// <param name="rank">The zero-based rank, in <c>[0, ZeroCount)</c>.</param>
    /// <returns>The bit position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rank"/> is out of range.</exception>
    public int Select0(int rank)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rank);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rank, ZeroCount);

        int samplePosition = zeroSamplePosition[rank / selectSampleRate];
        int word = samplePosition >> 6;
        ulong zeroBits = ZeroBits(words, word, Length);

        int zeros = ((rank / selectSampleRate) * selectSampleRate)
            - BitOperations.PopCount(zeroBits & ((1UL << (samplePosition & 63)) - 1));

        while(true)
        {
            int wordZeros = BitOperations.PopCount(zeroBits);
            if(zeros + wordZeros > rank)
            {
                return (word << 6) + BitSelect.InWord(zeroBits, rank - zeros);
            }

            zeros += wordZeros;
            word++;
            zeroBits = ZeroBits(words, word, Length);
        }
    }
}
