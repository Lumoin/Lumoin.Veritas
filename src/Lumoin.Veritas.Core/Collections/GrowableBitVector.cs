using System;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A growable bit vector over a flat <see cref="ulong"/> word buffer — the mutable
/// member of the packed-bit family whose built, read-only members are
/// <see cref="RankSelectBitVector"/> and <see cref="BlockCompressedBitVector"/>. Bit
/// <c>i</c> lives at word <c>i &gt;&gt; 6</c>, position <c>i &amp; 63</c>, the layout
/// <see cref="BitsetOps"/> operates on, and every bit at or beyond <see cref="Count"/>
/// is zero, so the word-parallel reductions read <see cref="Words"/> without a tail
/// mask. The default value is the empty vector: it owns no buffer, answers
/// <see cref="GetOrDefault"/> <see langword="false"/> at every index, and allocates on
/// the first <see cref="Append"/> or <see cref="Set"/>, so a vector that is never
/// written costs one reference and one integer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Held in a field, never a property.</b> The vector is a MUTABLE value type: a
/// property getter hands out a copy, so a mutation through it is lost, and the
/// compiler refuses the write outright for an auto-property. A <c>readonly</c> field
/// is equally wrong, since calling a mutating member on one takes a defensive copy.
/// The vector owns its buffer outright and holds no reference, so it is a flat owned
/// buffer.
/// </para>
/// <para>
/// <b>Growth.</b> The first allocation is exactly the words the written index needs;
/// every later growth takes the larger of the required word count and twice the
/// current one, so an append is amortized O(1) and a set beyond the end costs one
/// copy of the words already held. The bits growth introduces are clear because a
/// fresh array is zero-initialised and the copy leaves the remainder untouched — the
/// vector never walks the gap it opened.
/// </para>
/// </remarks>
internal struct GrowableBitVector
{
    /// <summary>The shift mapping a bit index to its word index.</summary>
    private const int WordShift = 6;

    /// <summary>The mask selecting the bit position within a word.</summary>
    private const int BitMask = 63;

    /// <summary>The packed bits, or <see langword="null"/> while the vector owns no buffer. The array is the collection's own backing store, never a parameter, so its length is the capacity and <see cref="Count"/> is the logical size.</summary>
    private ulong[]? words;

    /// <summary>The number of bits the vector holds; every bit at or beyond it is zero.</summary>
    private int count;

    /// <summary>The number of bits the vector holds. Every bit at or beyond it is zero.</summary>
    public readonly int Count
    {
        get
        {
            return count;
        }
    }

    /// <summary>Whether the vector owns no buffer — the state the default value carries and the state a vector that was never written keeps.</summary>
    public readonly bool IsEmpty
    {
        get
        {
            return words is null;
        }
    }

    /// <summary>The words holding the vector's bits, exactly the words <see cref="Count"/> fills; empty while the vector owns no buffer. Bits at or beyond <see cref="Count"/> are zero, so a word-parallel reduction reads this span directly. The span is invalidated by the next mutation.</summary>
    public readonly ReadOnlySpan<ulong> Words
    {
        get
        {
            return words is null ? default : words.AsSpan(0, (count + BitMask) >>> WordShift);
        }
    }

    /// <summary>The bit at an index that is inside <c>[0, <see cref="Count"/>)</c> — the read whose index the caller establishes by construction, so an index outside the range is an invariant violation rather than an expected condition.</summary>
    /// <param name="index">The bit index.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative, or at or beyond <see cref="Count"/>.</exception>
    public readonly bool this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

            return (words![index >>> WordShift] & (1UL << (index & BitMask))) != 0UL;
        }
    }

    /// <summary>The bit at an index, reading <see langword="false"/> at an index at or beyond <see cref="Count"/> and at every index of a vector that owns no buffer — the read a lazily written record answers through.</summary>
    /// <param name="index">The bit index.</param>
    /// <returns><see langword="true"/> when the index is inside the vector and its bit is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public readonly bool GetOrDefault(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        return index < count && (words![index >>> WordShift] & (1UL << (index & BitMask))) != 0UL;
    }

    /// <summary>Appends one bit at index <see cref="Count"/>, growing the buffer when the word that index lands in is not yet held.</summary>
    /// <param name="value">The bit appended.</param>
    public void Append(bool value)
    {
        int index = count;
        EnsureCovers(index);
        count = index + 1;
        if(value)
        {
            words![index >>> WordShift] |= 1UL << (index & BitMask);
        }
    }

    /// <summary>Sets the bit at an index, extending the vector to cover it when the index is at or beyond <see cref="Count"/>; the bits the extension introduces are clear, so no gap is walked.</summary>
    /// <param name="index">The bit index.</param>
    /// <returns><see langword="true"/> when the bit was clear and is now set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public bool Set(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        EnsureCovers(index);
        if(index >= count)
        {
            count = index + 1;
        }

        ref ulong word = ref words![index >>> WordShift];
        ulong bit = 1UL << (index & BitMask);
        if((word & bit) != 0UL)
        {
            return false;
        }

        word |= bit;

        return true;
    }

    /// <summary>Clears the bit at an index; an index at or beyond <see cref="Count"/> is a no-op and the vector never grows here.</summary>
    /// <param name="index">The bit index.</param>
    /// <returns><see langword="true"/> when the bit was set and is now clear.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    public bool Clear(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if(index >= count)
        {
            return false;
        }

        ref ulong word = ref words![index >>> WordShift];
        ulong bit = 1UL << (index & BitMask);
        if((word & bit) == 0UL)
        {
            return false;
        }

        word &= ~bit;

        return true;
    }

    /// <summary>Ensures the buffer holds the word an index lands in: the first allocation is exactly the required word count, and every later growth takes the larger of that count and twice the current length, clamped to the largest array the runtime allows.</summary>
    /// <param name="index">The bit index the buffer must cover.</param>
    private void EnsureCovers(int index)
    {
        int required = (index >>> WordShift) + 1;
        if(words is not null && words.Length >= required)
        {
            return;
        }

        if(words is null)
        {
            words = new ulong[required];

            return;
        }

        int doubled = words.Length <= (Array.MaxLength >>> 1) ? words.Length * 2 : Array.MaxLength;
        ulong[] replacement = new ulong[Math.Max(required, doubled)];
        Array.Copy(words, replacement, words.Length);
        words = replacement;
    }
}
