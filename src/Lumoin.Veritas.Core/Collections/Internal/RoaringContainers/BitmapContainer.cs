using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Collections.Internal.RoaringContainers;

/// <summary>
/// Dense-chunk representation: a flat bitmap covering the
/// full half-key space, backed by a pool-rented
/// <see cref="IMemoryOwner{T}"/> of <see cref="ulong"/> words.
/// Used when a chunk's element count exceeds the array
/// threshold (one-sixteenth of the half-key space).
/// </summary>
/// <remarks>
/// <para>
/// Storage is constant in cardinality: one bit per possible
/// half-key, padded to 64-bit words. For a uint outer key
/// (half-key bits = 16), the bitmap covers 65 536 bits = 1024
/// words = 8 KiB.
/// </para>
/// <para>
/// <see cref="Cardinality"/> is tracked incrementally rather
/// than recomputed via <see cref="BitOperations.PopCount(ulong)"/>
/// over the word array on every read; the incremental
/// bookkeeping keeps reads O(1).
/// </para>
/// <para>
/// <see cref="Remove"/> may demote the container back to an
/// <see cref="ArrayContainer"/> when cardinality drops below
/// the array threshold. The demotion happens on the way down
/// using a strict inequality matching the promotion's strict
/// inequality on the way up, so a sequence of adds and removes
/// at the boundary cannot oscillate between representations.
/// </para>
/// </remarks>
[DebuggerDisplay("BitmapContainer Cardinality={Cardinality}, WordCount={Owner.Memory.Length}")]
internal sealed class BitmapContainer: Container
{
    /// <summary>The pool-rented word array holding the bitmap.</summary>
    private IMemoryOwner<ulong> Owner { get; set; }

    private int CardinalityCounter { get; set; }

    /// <summary>Constructs an empty bitmap container sized for the given half-key bit width.</summary>
    public BitmapContainer(int halfKeyBits)
    {
        int wordCount = BitmapWordCountFor(halfKeyBits);
        Owner = VeritasMemoryPool<ulong>.Shared.Rent(wordCount);

        Debug.Assert(Owner.Memory.Length >= wordCount,
            $"VeritasMemoryPool returned a buffer smaller than the requested word count: got {Owner.Memory.Length}, requested {wordCount}.");

        Owner.Memory.Span[..wordCount].Clear();
        CardinalityCounter = 0;
    }

    /// <inheritdoc/>
    public override int Cardinality => CardinalityCounter;

    /// <inheritdoc/>
    public override Container Add(ulong halfKey, int halfKeyBits, out bool added)
    {
        _ = halfKeyBits;

        int wordIndex = (int)(halfKey >> 6);
        int bitIndex = (int)(halfKey & 63);
        ulong mask = 1UL << bitIndex;

        Span<ulong> words = Owner.Memory.Span;
        if((words[wordIndex] & mask) != 0)
        {
            added = false;

            return this;
        }

        words[wordIndex] |= mask;
        CardinalityCounter++;
        added = true;

        return this;
    }

    /// <inheritdoc/>
    public override bool Contains(ulong halfKey)
    {
        int wordIndex = (int)(halfKey >> 6);
        int bitIndex = (int)(halfKey & 63);
        ulong mask = 1UL << bitIndex;

        return (Owner.Memory.Span[wordIndex] & mask) != 0;
    }

    /// <inheritdoc/>
    public override Container Remove(ulong halfKey, int halfKeyBits, out bool removed)
    {
        int wordIndex = (int)(halfKey >> 6);
        int bitIndex = (int)(halfKey & 63);
        ulong mask = 1UL << bitIndex;

        Span<ulong> words = Owner.Memory.Span;
        if((words[wordIndex] & mask) == 0)
        {
            removed = false;

            return this;
        }

        words[wordIndex] &= ~mask;
        CardinalityCounter--;
        removed = true;

        if(CardinalityCounter < ArrayThresholdFor(halfKeyBits))
        {
            return DemoteToArray();
        }

        return this;
    }

    /// <inheritdoc/>
    public override Container UnionWith(Container other, int halfKeyBits)
    {
        _ = halfKeyBits;
        switch(other)
        {
            case BitmapContainer bitmapOther:
            {
                Span<ulong> words = Owner.Memory.Span;
                ReadOnlySpan<ulong> otherWords = bitmapOther.Owner.Memory.Span;
                int newCardinality = 0;
                for(int i = 0; i < words.Length; i++)
                {
                    words[i] |= otherWords[i];
                    newCardinality += BitOperations.PopCount(words[i]);
                }

                CardinalityCounter = newCardinality;

                return this;
            }

            case ArrayContainer arrayOther:
            {
                foreach(ulong key in arrayOther.KeysView)
                {
                    _ = Add(key, halfKeyBits, out _);
                }

                return this;
            }

            default:
            {
                throw new InvalidOperationException($"Unknown container type: {other.GetType().Name}.");
            }
        }
    }

    /// <inheritdoc/>
    public override Container IntersectWith(Container other, int halfKeyBits)
    {
        switch(other)
        {
            case BitmapContainer bitmapOther:
            {
                Span<ulong> words = Owner.Memory.Span;
                ReadOnlySpan<ulong> otherWords = bitmapOther.Owner.Memory.Span;
                int newCardinality = 0;
                for(int i = 0; i < words.Length; i++)
                {
                    words[i] &= otherWords[i];
                    newCardinality += BitOperations.PopCount(words[i]);
                }

                CardinalityCounter = newCardinality;
                if(CardinalityCounter < ArrayThresholdFor(halfKeyBits))
                {
                    return DemoteToArray();
                }

                return this;
            }

            case ArrayContainer arrayOther:
            {
                //Build a new array of keys present in both.
                ArrayContainer result = new(arrayOther.Cardinality);
                foreach(ulong key in arrayOther.KeysView)
                {
                    if(Contains(key))
                    {
                        _ = result.Add(key, halfKeyBits, out _);
                    }
                }

                //Self-replace: caller's reference to this
                //BitmapContainer is now stale and will be disposed
                //via the returned-instance contract.
                Dispose();

                return result;
            }

            default:
            {
                throw new InvalidOperationException($"Unknown container type: {other.GetType().Name}.");
            }
        }
    }

    /// <inheritdoc/>
    public override Container ExceptWith(Container other, int halfKeyBits)
    {
        switch(other)
        {
            case BitmapContainer bitmapOther:
            {
                Span<ulong> words = Owner.Memory.Span;
                ReadOnlySpan<ulong> otherWords = bitmapOther.Owner.Memory.Span;
                int newCardinality = 0;
                for(int i = 0; i < words.Length; i++)
                {
                    words[i] &= ~otherWords[i];
                    newCardinality += BitOperations.PopCount(words[i]);
                }

                CardinalityCounter = newCardinality;
                if(CardinalityCounter < ArrayThresholdFor(halfKeyBits))
                {
                    return DemoteToArray();
                }

                return this;
            }

            case ArrayContainer arrayOther:
            {
                foreach(ulong key in arrayOther.KeysView)
                {
                    _ = Remove(key, halfKeyBits, out _);
                }

                //Remove may have demoted us already; if so,
                //subsequent removes have been against the array
                //and the return path has already swapped. The
                //happy path returns this.
                if(CardinalityCounter < ArrayThresholdFor(halfKeyBits))
                {
                    return DemoteToArray();
                }

                return this;
            }

            default:
            {
                throw new InvalidOperationException($"Unknown container type: {other.GetType().Name}.");
            }
        }
    }

    /// <inheritdoc/>
    public override Container Clone(int halfKeyBits)
    {
        BitmapContainer copy = new(halfKeyBits);
        Owner.Memory.Span.CopyTo(copy.Owner.Memory.Span);
        copy.CardinalityCounter = CardinalityCounter;

        return copy;
    }

    /// <inheritdoc/>
    public override IEnumerator<ulong> GetEnumerator()
    {
        return new BitmapEnumerator(Owner.Memory);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        Owner.Dispose();
        base.Dispose();
    }

    private ArrayContainer DemoteToArray()
    {
        ArrayContainer array = new(CardinalityCounter);
        ReadOnlySpan<ulong> words = Owner.Memory.Span;
        for(int wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            ulong wordBits = words[wordIndex];
            while(wordBits != 0)
            {
                int bit = BitOperations.TrailingZeroCount(wordBits);
                ulong halfKey = ((ulong)wordIndex << 6) | (uint)bit;

                //BulkAppendSorted bypasses the per-insert threshold
                //check Add applies. The bitmap enumeration yields
                //keys in ascending order, so the appended list stays
                //sorted; the array's cardinality is below the
                //threshold by precondition of this demotion path,
                //so no promotion check is needed.
                array.BulkAppendSorted(halfKey);

                //Clear the lowest set bit (the Brian Kernighan idiom)
                //so the next iteration emits the next-highest bit.
                wordBits &= wordBits - 1;
            }
        }

        //Once we have built the array container we own no further
        //references to our rental; release it back to the pool.
        Dispose();

        return array;
    }

    private sealed class BitmapEnumerator: IEnumerator<ulong>
    {
        private ReadOnlyMemory<ulong> Words { get; }

        private int WordIndex { get; set; }

        private ulong RemainingBits { get; set; }

        private ulong CurrentKey { get; set; }

        public BitmapEnumerator(ReadOnlyMemory<ulong> words)
        {
            Words = words;
            WordIndex = -1;
            RemainingBits = 0;
            CurrentKey = 0;
        }

        public ulong Current => CurrentKey;

        object System.Collections.IEnumerator.Current => CurrentKey;

        public bool MoveNext()
        {
            while(true)
            {
                if(RemainingBits != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(RemainingBits);
                    CurrentKey = ((ulong)WordIndex << 6) | (uint)bit;

                    //Clear the lowest set bit so the next call emits
                    //the next-highest bit in the same word.
                    RemainingBits &= RemainingBits - 1;

                    return true;
                }

                WordIndex++;
                if(WordIndex >= Words.Length)
                {
                    return false;
                }

                RemainingBits = Words.Span[WordIndex];
            }
        }

        public void Reset()
        {
            WordIndex = -1;
            RemainingBits = 0;
            CurrentKey = 0;
        }

        public void Dispose()
        {
        }
    }
}
