using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A wavelet matrix over an unsigned integer sequence: one
/// <see cref="RankSelectBitVector"/> per bit of the symbol width, most
/// significant bit first, each level a stable zeros-then-ones partition of the
/// one above. It answers <see cref="Access"/>, symbol <see cref="Rank"/> and
/// <see cref="Select"/>, and the range successor
/// <see cref="TryRangeNextGEQ"/> — each in one bounded walk over the
/// <see cref="BitWidth"/> levels. Single-writer at build, then read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> Level <c>ℓ</c> stores bit <c>BitWidth − 1 − ℓ</c> of every
/// symbol in that level's permutation; a position maps to the next level as
/// <c>rank₀(i)</c> when the bit is unset or <c>zeros + rank₁(i)</c> when set,
/// where <c>zeros</c> is the level's unset-bit total. An interval mapped
/// through all levels lands on a contiguous block per symbol, which is what
/// the rank, select, and range-successor walks exploit.
/// </para>
/// </remarks>
[DebuggerDisplay("WaveletMatrix Count={Count} BitWidth={BitWidth}")]
public sealed class WaveletMatrix
{
    //One bit-vector per symbol bit, most significant first.
    private readonly RankSelectBitVector[] levels;

    //Per level: the total unset bits — the 1-side block offset in the next
    //level's permutation.
    private readonly int[] zeroCounts;

    /// <summary>The number of symbols in the sequence.</summary>
    public int Count { get; }

    /// <summary>The number of bits per symbol; symbols occupy <c>[0, 2^BitWidth)</c>.</summary>
    public int BitWidth { get; }

    /// <summary>The total footprint in bits across the level bit-vectors and the per-level zero counts.</summary>
    public long BitCount
    {
        get
        {
            long bits = (long)zeroCounts.Length * 32;
            foreach(RankSelectBitVector level in levels)
            {
                bits += level.BitCount;
            }

            return bits;
        }
    }

    private WaveletMatrix(RankSelectBitVector[] levels, int[] zeroCounts, int count, int bitWidth)
    {
        this.levels = levels;
        this.zeroCounts = zeroCounts;
        Count = count;
        BitWidth = bitWidth;
    }

    /// <summary>Builds the matrix from a symbol sequence.</summary>
    /// <param name="values">The symbols.</param>
    /// <param name="bitWidth">The symbol width in bits (1–32), or <c>0</c> to use the width of the largest value; every value must fit the width.</param>
    /// <param name="selectSampleRate">The select-sample rate passed to every level bit-vector.</param>
    /// <returns>The built matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitWidth"/> is negative or above 32, or <paramref name="selectSampleRate"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">A value does not fit <paramref name="bitWidth"/>.</exception>
    public static WaveletMatrix Build(ReadOnlySpan<uint> values, int bitWidth = 0, int selectSampleRate = 512)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitWidth);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bitWidth, 32);

        uint maxValue = 0;
        foreach(uint value in values)
        {
            maxValue |= value;
        }

        if(bitWidth == 0)
        {
            bitWidth = maxValue == 0 ? 1 : 32 - BitOperations.LeadingZeroCount(maxValue);
        }
        else if(((ulong)maxValue >> bitWidth) != 0)
        {
            throw new ArgumentException("A value does not fit the requested bit width.", nameof(values));
        }

        int count = values.Length;
        RankSelectBitVector[] levels = new RankSelectBitVector[bitWidth];
        int[] zeroCounts = new int[bitWidth];

        if(count == 0)
        {
            for(int level = 0; level < bitWidth; level++)
            {
                levels[level] = RankSelectBitVector.Build([], 0, selectSampleRate);
            }

            return new WaveletMatrix(levels, zeroCounts, 0, bitWidth);
        }

        //The two permutation buffers ping-pong between levels: read the
        //current order, emit the level's bits, then stable-partition into the
        //next order (zeros keep their order first, ones after).
        using IMemoryOwner<uint> currentOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        using IMemoryOwner<uint> nextOwner = VeritasMemoryPool<uint>.Shared.Rent(count);
        Span<uint> current = currentOwner.Memory.Span[..count];
        Span<uint> next = nextOwner.Memory.Span[..count];
        values.CopyTo(current);

        for(int level = 0; level < bitWidth; level++)
        {
            int shift = bitWidth - 1 - level;
            ulong[] payload = new ulong[(count + 63) >> 6];
            int zeros = 0;
            for(int i = 0; i < count; i++)
            {
                if(((current[i] >> shift) & 1) != 0)
                {
                    payload[i >> 6] |= 1UL << (i & 63);
                }
                else
                {
                    zeros++;
                }
            }

            levels[level] = RankSelectBitVector.Build(payload, count, selectSampleRate);
            zeroCounts[level] = zeros;

            int zeroCursor = 0;
            int oneCursor = zeros;
            for(int i = 0; i < count; i++)
            {
                if(((current[i] >> shift) & 1) != 0)
                {
                    next[oneCursor++] = current[i];
                }
                else
                {
                    next[zeroCursor++] = current[i];
                }
            }

            Span<uint> swap = current;
            current = next;
            next = swap;
        }

        return new WaveletMatrix(levels, zeroCounts, count, bitWidth);
    }

    /// <summary>The symbol at a position.</summary>
    /// <param name="position">The position, in <c>[0, Count)</c>.</param>
    /// <returns>The symbol.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public uint Access(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, Count);

        uint symbol = 0;
        for(int level = 0; level < BitWidth; level++)
        {
            RankSelectBitVector bits = levels[level];
            if(bits.Access(position))
            {
                symbol |= 1u << (BitWidth - 1 - level);
                position = zeroCounts[level] + bits.Rank1(position);
            }
            else
            {
                position = bits.Rank0(position);
            }
        }

        return symbol;
    }

    /// <summary>The number of occurrences of a symbol strictly before a position.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <param name="position">The exclusive end position, in <c>[0, Count]</c>.</param>
    /// <returns>The occurrence count in <c>[0, position)</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is out of range.</exception>
    public int Rank(uint symbol, int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Count);

        if(BitWidth < 32 && (symbol >> BitWidth) != 0)
        {
            return 0;
        }

        int low = 0;
        int high = position;
        for(int level = 0; level < BitWidth; level++)
        {
            RankSelectBitVector bits = levels[level];
            if(((symbol >> (BitWidth - 1 - level)) & 1) != 0)
            {
                int zeros = zeroCounts[level];
                low = zeros + low - bits.Rank0(low);
                high = zeros + high - bits.Rank0(high);
            }
            else
            {
                low = bits.Rank0(low);
                high = bits.Rank0(high);
            }
        }

        return high - low;
    }

    /// <summary>The position of the <paramref name="occurrence"/>-th occurrence (0-based) of a symbol — a block-start descent then a select walk back up.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <param name="occurrence">The zero-based occurrence index; must be below the symbol's total occurrence count.</param>
    /// <returns>The position.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="occurrence"/> is negative or not below the symbol's occurrence count.</exception>
    public int Select(uint symbol, int occurrence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(occurrence);

        if(BitWidth < 32 && (symbol >> BitWidth) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence), "The symbol does not occur.");
        }

        //Down: map the full interval to the symbol's bottom block, tracking
        //both ends so the occurrence bound is validated without a second walk.
        int low = 0;
        int high = Count;
        for(int level = 0; level < BitWidth; level++)
        {
            RankSelectBitVector bits = levels[level];
            if(((symbol >> (BitWidth - 1 - level)) & 1) != 0)
            {
                int zeros = zeroCounts[level];
                low = zeros + low - bits.Rank0(low);
                high = zeros + high - bits.Rank0(high);
            }
            else
            {
                low = bits.Rank0(low);
                high = bits.Rank0(high);
            }
        }

        if(occurrence >= high - low)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence), "The symbol does not occur that often.");
        }

        //Up: invert each level's mapping with the matching select.
        int position = low + occurrence;
        for(int level = BitWidth - 1; level >= 0; level--)
        {
            RankSelectBitVector bits = levels[level];
            position = ((symbol >> (BitWidth - 1 - level)) & 1) != 0
                ? bits.Select1(position - zeroCounts[level])
                : bits.Select0(position);
        }

        return position;
    }

    /// <summary>
    /// Finds the smallest symbol greater than or equal to
    /// <paramref name="target"/> occurring in the position range
    /// <c>[low, high)</c> — the range-successor walk: follow the target's bits
    /// while possible, keeping each passed-over upper sibling as a fallback,
    /// and resolve a divergence by taking the fallback subtree's minimum.
    /// </summary>
    /// <param name="low">The inclusive range start, in <c>[0, Count]</c>.</param>
    /// <param name="high">The exclusive range end, in <c>[low, Count]</c>.</param>
    /// <param name="target">The sought lower bound.</param>
    /// <param name="value">Receives the successor symbol when one exists.</param>
    /// <returns><see langword="true"/> when a successor exists in the range.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is invalid.</exception>
    public bool TryRangeNextGEQ(int low, int high, uint target, out uint value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(low);
        ArgumentOutOfRangeException.ThrowIfLessThan(high, low);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(high, Count);

        value = 0;
        if(low == high)
        {
            return false;
        }

        if(BitWidth < 32 && (target >> BitWidth) != 0)
        {
            return false;
        }

        //At most one fallback per level: the 1-side interval passed over while
        //following a 0 bit of the target. LIFO order resolves the deepest —
        //smallest — fallback first.
        Span<int> fallbackLevel = stackalloc int[BitWidth];
        Span<int> fallbackLow = stackalloc int[BitWidth];
        Span<int> fallbackHigh = stackalloc int[BitWidth];
        Span<uint> fallbackPrefix = stackalloc uint[BitWidth];
        int fallbacks = 0;

        int level = 0;
        uint prefix = 0;
        while(level < BitWidth)
        {
            RankSelectBitVector bits = levels[level];
            int zeros = zeroCounts[level];
            uint levelBit = 1u << (BitWidth - 1 - level);
            int low0 = bits.Rank0(low);
            int high0 = bits.Rank0(high);
            int low1 = zeros + low - low0;
            int high1 = zeros + high - high0;

            if((target & levelBit) == 0)
            {
                if(high1 > low1 && high0 > low0)
                {
                    fallbackLevel[fallbacks] = level + 1;
                    fallbackLow[fallbacks] = low1;
                    fallbackHigh[fallbacks] = high1;
                    fallbackPrefix[fallbacks] = prefix | levelBit;
                    fallbacks++;
                }

                if(high0 > low0)
                {
                    low = low0;
                    high = high0;
                    level++;

                    continue;
                }

                if(high1 > low1)
                {
                    //No exact branch, but the upper sibling exists: its
                    //minimum is the successor.
                    value = MinimumInSubtree(level + 1, low1, high1, prefix | levelBit);

                    return true;
                }
            }
            else if(high1 > low1)
            {
                low = low1;
                high = high1;
                prefix |= levelBit;
                level++;

                continue;
            }

            //The exact path died; resume from the deepest passed-over sibling.
            if(fallbacks == 0)
            {
                return false;
            }

            fallbacks--;
            value = MinimumInSubtree(fallbackLevel[fallbacks], fallbackLow[fallbacks], fallbackHigh[fallbacks], fallbackPrefix[fallbacks]);

            return true;
        }

        value = prefix;

        return true;
    }

    /// <summary>The smallest symbol in a subtree interval: descend preferring the 0-side at every remaining level.</summary>
    /// <param name="level">The level the interval lives at.</param>
    /// <param name="low">The inclusive interval start.</param>
    /// <param name="high">The exclusive interval end; the interval is non-empty.</param>
    /// <param name="prefix">The symbol bits fixed above the level.</param>
    /// <returns>The smallest symbol.</returns>
    private uint MinimumInSubtree(int level, int low, int high, uint prefix)
    {
        for(; level < BitWidth; level++)
        {
            RankSelectBitVector bits = levels[level];
            int low0 = bits.Rank0(low);
            int high0 = bits.Rank0(high);
            if(high0 > low0)
            {
                low = low0;
                high = high0;
            }
            else
            {
                int zeros = zeroCounts[level];
                prefix |= 1u << (BitWidth - 1 - level);
                low = zeros + low - low0;
                high = zeros + high - high0;
            }
        }

        return prefix;
    }
}
