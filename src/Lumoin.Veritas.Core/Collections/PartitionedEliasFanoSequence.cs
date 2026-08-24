using System;
using System.Diagnostics;
using System.Numerics;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A sequence that is non-decreasing WITHIN segments but resets at each segment
/// boundary — a concatenation of ascending runs, the shape of a columnar
/// index's within-group value column (the level-1 and level-2 values, one run
/// per parent group, that <see cref="EliasFanoSequence"/> cannot hold because
/// the column is not globally monotone). Each segment is stored Elias-Fano
/// RELATIVE to its own minimum, with one lower payload and one upper bit-vector
/// shared across all segments and per-segment parameters (base, low-bit width),
/// so a segment's footprint tends to its own LOCAL entropy
/// (≈ <c>2 + log2(localSpan / count)</c> bits per value) instead of paying the
/// global universe. The seek a worst-case-optimal descent needs is
/// segment-local — the descent already holds the segment bounds — so
/// <see cref="LowerBound"/> operates inside the one segment a child range names.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundaries are shared.</b> The segment boundaries ARE the index's
/// exclusive-end offset column; this structure borrows them and does not own a
/// second copy, so <see cref="BitCount"/> counts only the payloads and the
/// per-segment base and low-bit width — the figure a column footprint adds over
/// the offsets it already stores.
/// </para>
/// <para>
/// <b>Per-segment overhead.</b> The per-segment base and width are fixed
/// overhead amortised over the segment's values, so the bits per value fall as
/// segments grow. The <c>--profile-partitioned-elias-fano</c> soak measures the
/// footprint against frame of reference across the fan-out range.
/// </para>
/// </remarks>
[DebuggerDisplay("PartitionedEliasFanoSequence Count={Count} Segments={SegmentCount} Bits={BitCount}")]
public sealed partial class PartitionedEliasFanoSequence
{
    private readonly ulong[] lower;

    private readonly ulong[] upper;

    //Per segment: the value subtracted before Elias-Fano (the segment minimum).
    private readonly uint[] segmentBase;

    //Per segment: the explicit low-bit width.
    private readonly byte[] segmentLowBits;

    //Per segment: bit offset into `lower` where the segment's low bits begin —
    //a prefix sum of count·lowBits, reconstructable from the per-segment widths
    //and counts, so it is not part of the serialized footprint.
    private readonly long[] segmentLowerStart;

    //Per segment: bit offset into `upper` where the segment's bit-vector begins
    //— likewise a derived prefix sum, not serialized footprint.
    private readonly long[] segmentUpperStart;

    //Segment start indices in value space, exclusive-end (length SegmentCount+1);
    //the index's offset column, borrowed, not owned.
    private readonly int[] boundaries;

    /// <summary>The number of values across all segments.</summary>
    public int Count { get; }

    /// <summary>The number of segments.</summary>
    public int SegmentCount => boundaries.Length - 1;

    /// <summary>
    /// The serialized footprint in bits: the lower payload, the upper
    /// bit-vector, and the per-segment base and low-bit width. The borrowed
    /// boundaries and the derived per-segment bit-offset prefix sums are
    /// excluded — boundaries are the existing offset column, the prefix sums
    /// reconstruct on load.
    /// </summary>
    public long BitCount =>
        ((long)lower.Length * 64)
        + ((long)upper.Length * 64)
        + ((long)segmentBase.Length * 32)
        + ((long)segmentLowBits.Length * 8);

    private PartitionedEliasFanoSequence(
        ulong[] lower,
        ulong[] upper,
        uint[] segmentBase,
        byte[] segmentLowBits,
        long[] segmentLowerStart,
        long[] segmentUpperStart,
        int[] boundaries,
        int count)
    {
        this.lower = lower;
        this.upper = upper;
        this.segmentBase = segmentBase;
        this.segmentLowBits = segmentLowBits;
        this.segmentLowerStart = segmentLowerStart;
        this.segmentUpperStart = segmentUpperStart;
        this.boundaries = boundaries;
        Count = count;
    }

    /// <summary>
    /// Builds the sequence from values that are non-decreasing within each
    /// segment, the segments delimited by exclusive-end <paramref name="boundaries"/>.
    /// </summary>
    /// <param name="values">The values; non-decreasing within every segment.</param>
    /// <param name="boundaries">Segment start indices, exclusive-end: segment <c>g</c> is <c>[boundaries[g], boundaries[g+1])</c>. The first entry is 0, the last is <paramref name="values"/>.Length.</param>
    /// <returns>The packed sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="boundaries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The boundaries are malformed, or a segment is not non-decreasing.</exception>
    public static PartitionedEliasFanoSequence Build(ReadOnlySpan<uint> values, int[] boundaries)
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        if(boundaries.Length == 0 || boundaries[0] != 0 || boundaries[^1] != values.Length)
        {
            throw new ArgumentException("Boundaries must start at 0 and end at the value count.", nameof(boundaries));
        }

        int segmentCount = boundaries.Length - 1;
        uint[] segmentBase = new uint[segmentCount];
        byte[] segmentLowBits = new byte[segmentCount];
        long[] segmentLowerStart = new long[segmentCount + 1];
        long[] segmentUpperStart = new long[segmentCount + 1];

        for(int g = 0; g < segmentCount; g++)
        {
            int start = boundaries[g];
            int end = boundaries[g + 1];
            if(end < start)
            {
                throw new ArgumentException("Boundaries must be non-decreasing.", nameof(boundaries));
            }

            int count = end - start;
            int lowBits = 0;
            long upperLength = 0;
            if(count > 0)
            {
                uint baseValue = values[start];
                uint maxRelative = values[end - 1] - baseValue;
                for(int i = start + 1; i < end; i++)
                {
                    if(values[i] < values[i - 1])
                    {
                        throw new ArgumentException("Each segment must be non-decreasing.", nameof(values));
                    }
                }

                ulong localUniverse = (ulong)maxRelative + 1;
                ulong ratio = localUniverse / (ulong)count;
                if(ratio >= 2)
                {
                    lowBits = 63 - BitOperations.LeadingZeroCount(ratio);
                }

                long maxHigh = (long)((ulong)maxRelative >> lowBits);
                upperLength = count + maxHigh + 1;
                segmentBase[g] = baseValue;
            }

            segmentLowBits[g] = (byte)lowBits;
            segmentLowerStart[g + 1] = segmentLowerStart[g] + ((long)count * lowBits);
            segmentUpperStart[g + 1] = segmentUpperStart[g] + upperLength;
        }

        ulong[] lower = new ulong[(int)((segmentLowerStart[segmentCount] + 63) >> 6)];
        ulong[] upper = new ulong[(int)((segmentUpperStart[segmentCount] + 63) >> 6)];

        for(int g = 0; g < segmentCount; g++)
        {
            int start = boundaries[g];
            int count = boundaries[g + 1] - start;
            if(count == 0)
            {
                continue;
            }

            int lowBits = segmentLowBits[g];
            uint baseValue = segmentBase[g];
            uint lowMask = lowBits == 0 ? 0u : (uint)((1UL << lowBits) - 1);
            long lowerBit = segmentLowerStart[g];
            long upperBit = segmentUpperStart[g];

            for(int j = 0; j < count; j++)
            {
                uint relative = values[start + j] - baseValue;
                if(lowBits != 0)
                {
                    WriteLow(lower, lowerBit + ((long)j * lowBits), relative & lowMask, lowBits);
                }

                long position = upperBit + (long)((ulong)relative >> lowBits) + j;
                upper[(int)(position >> 6)] |= 1UL << (int)(position & 63);
            }
        }

        return new PartitionedEliasFanoSequence(lower, upper, segmentBase, segmentLowBits, segmentLowerStart, segmentUpperStart, boundaries, values.Length);
    }

    /// <summary>The value at a global index.</summary>
    /// <param name="index">The index, in <c>[0, Count)</c>.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public uint Access(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        int segment = SegmentOf(index);

        return ValueInSegment(segment, index - boundaries[segment]);
    }

    /// <summary>
    /// The smallest index in <c>[lo, hi)</c> whose value is greater than or
    /// equal to <paramref name="target"/>, or <paramref name="hi"/> when none
    /// is. The range must lie within a SINGLE segment — the descent contract:
    /// a child range is exactly one parent group.
    /// </summary>
    /// <param name="lo">The range's inclusive start.</param>
    /// <param name="hi">The range's exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    public int LowerBound(int lo, int hi, uint target)
    {
        if(lo >= hi)
        {
            return hi;
        }

        int segment = SegmentOf(lo);
        uint baseValue = segmentBase[segment];
        if(target <= baseValue)
        {
            return lo;
        }

        int lowBits = segmentLowBits[segment];
        int count = boundaries[segment + 1] - boundaries[segment];
        long upperLength = segmentUpperStart[segment + 1] - segmentUpperStart[segment];
        long maxHigh = upperLength - count - 1;

        uint relative = target - baseValue;
        long targetHigh = (long)((ulong)relative >> lowBits);
        if(targetHigh > maxHigh)
        {
            return hi;
        }

        //The segment's high group `targetHigh` occupies local indices
        //[select0(targetHigh-1) - (targetHigh-1), select0(targetHigh) - targetHigh),
        //the selects taken inside the segment's upper region.
        int localStart = targetHigh == 0 ? 0 : (int)(SelectZeroInRegion(segment, targetHigh - 1) - (targetHigh - 1));
        int localEnd = (int)(SelectZeroInRegion(segment, targetHigh) - targetHigh);

        uint low = lowBits == 0 ? 0u : (relative & (uint)((1UL << lowBits) - 1));
        long lowerBase = segmentLowerStart[segment];
        for(int local = localStart; local < localEnd; local++)
        {
            uint candidateLow = lowBits == 0 ? 0u : ReadLow(lowerBase + ((long)local * lowBits), lowBits);
            if(candidateLow >= low)
            {
                int global = boundaries[segment] + local;

                return global < hi ? global : hi;
            }
        }

        int after = boundaries[segment] + localEnd;

        return after < hi ? after : hi;
    }

    /// <summary>Decodes a whole segment into <paramref name="destination"/> in one linear pass over its bit-vector region.</summary>
    /// <param name="segment">The segment index, in <c>[0, SegmentCount)</c>.</param>
    /// <param name="destination">Receives the segment's values; at least the segment length long.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="segment"/> is out of range.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public void DecodeSegment(int segment, Span<uint> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segment);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(segment, SegmentCount);

        int count = boundaries[segment + 1] - boundaries[segment];
        if(destination.Length < count)
        {
            throw new ArgumentException("The destination is shorter than the segment.", nameof(destination));
        }

        if(count == 0)
        {
            return;
        }

        int lowBits = segmentLowBits[segment];
        uint baseValue = segmentBase[segment];
        long lowerBit = segmentLowerStart[segment];
        long position = segmentUpperStart[segment];
        long high = 0;

        for(int j = 0; j < count; j++)
        {
            //Advance to the j-th set bit, counting separators as high steps.
            while((upper[(int)(position >> 6)] & (1UL << (int)(position & 63))) == 0)
            {
                high++;
                position++;
            }

            uint low = lowBits == 0 ? 0u : ReadLow(lowerBit + ((long)j * lowBits), lowBits);
            destination[j] = baseValue + (uint)(((ulong)high << lowBits) | low);
            position++;
        }
    }

    /// <summary>The value at local index <paramref name="local"/> within a segment.</summary>
    /// <param name="segment">The segment.</param>
    /// <param name="local">The in-segment index.</param>
    /// <returns>The value.</returns>
    private uint ValueInSegment(int segment, int local)
    {
        long regionStart = segmentUpperStart[segment];
        long bitPosition = SelectOneInRegion(regionStart, local);
        long high = bitPosition - regionStart - local;
        int lowBits = segmentLowBits[segment];
        uint low = lowBits == 0 ? 0u : ReadLow(segmentLowerStart[segment] + ((long)local * lowBits), lowBits);

        return segmentBase[segment] + (uint)(((ulong)high << lowBits) | low);
    }

    /// <summary>The segment containing a global index — a binary search over the borrowed boundaries.</summary>
    /// <param name="index">The global index.</param>
    /// <returns>The segment index.</returns>
    private int SegmentOf(int index)
    {
        int low = 0;
        int high = boundaries.Length - 1;
        while(low < high)
        {
            int mid = low + ((high - low + 1) >> 1);
            if(boundaries[mid] <= index)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    /// <summary>The absolute position of the <paramref name="rank"/>-th set bit at or after <paramref name="regionStart"/> in <see cref="upper"/>.</summary>
    /// <param name="regionStart">The bit position the segment's region begins at.</param>
    /// <param name="rank">The zero-based rank of the set bit within the region.</param>
    /// <returns>The absolute bit position.</returns>
    private long SelectOneInRegion(long regionStart, int rank)
    {
        int word = (int)(regionStart >> 6);
        int shift = (int)(regionStart & 63);
        ulong bits = upper[word] >> shift;
        int ones = BitOperations.PopCount(bits);
        if(ones > rank)
        {
            return regionStart + BitSelect.InWord(bits, rank);
        }

        rank -= ones;
        word++;
        while(true)
        {
            ulong wordBits = upper[word];
            int wordOnes = BitOperations.PopCount(wordBits);
            if(wordOnes > rank)
            {
                return ((long)word << 6) + BitSelect.InWord(wordBits, rank);
            }

            rank -= wordOnes;
            word++;
        }
    }

    /// <summary>The position, RELATIVE to the segment's region start, of the <paramref name="rank"/>-th unset (separator) bit in the segment's upper region.</summary>
    /// <param name="segment">The segment.</param>
    /// <param name="rank">The zero-based rank of the separator, in <c>[0, maxHigh]</c> (always inside the region).</param>
    /// <returns>The bit position relative to the region start.</returns>
    private long SelectZeroInRegion(int segment, long rank)
    {
        long regionStart = segmentUpperStart[segment];
        int word = (int)(regionStart >> 6);
        int shift = (int)(regionStart & 63);
        ulong zeros = ~upper[word] & ~((1UL << shift) - 1);
        int count = BitOperations.PopCount(zeros);
        if(count > rank)
        {
            return ((long)word << 6) + BitSelect.InWord(zeros, (int)rank) - regionStart;
        }

        rank -= count;
        word++;
        while(true)
        {
            ulong wordZeros = ~upper[word];
            int wordCount = BitOperations.PopCount(wordZeros);
            if(wordCount > rank)
            {
                return ((long)word << 6) + BitSelect.InWord(wordZeros, (int)rank) - regionStart;
            }

            rank -= wordCount;
            word++;
        }
    }

    /// <summary>Reads <paramref name="width"/> bits at a bit offset of <see cref="lower"/>.</summary>
    /// <param name="bitOffset">The bit offset.</param>
    /// <param name="width">The low-bit width.</param>
    /// <returns>The low value.</returns>
    private uint ReadLow(long bitOffset, int width)
    {
        int word = (int)(bitOffset >> 6);
        int shift = (int)(bitOffset & 63);
        ulong bits = lower[word] >> shift;
        if(shift + width > 64)
        {
            bits |= lower[word + 1] << (64 - shift);
        }

        uint mask = width == 32 ? uint.MaxValue : (uint)((1UL << width) - 1);

        return (uint)(bits & mask);
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
