using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Lumoin.Veritas.Core.Collections.Internal.RoaringContainers;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// A roaring bitmap: a compressed set of unsigned integer keys
/// with O(1) average-case <see cref="Add"/>, <see cref="Contains"/>
/// and <see cref="Remove"/>, and asymptotically optimal set
/// operations against another bitmap of the same key type.
/// </summary>
/// <typeparam name="TKey">
/// The element type. Must be an unmanaged unsigned binary integer
/// (<see cref="ushort"/>, <see cref="uint"/>, or
/// <see cref="ulong"/>). The generic-math interfaces supply the
/// bit-manipulation operations the format needs without per-width
/// specialisation.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Format.</b> Each <typeparamref name="TKey"/> is split into
/// equal high and low halves. The high half indexes into a sorted
/// chunk dictionary; the low half lives in the chunk's container.
/// Each container picks one of two representations based on its
/// current density:
/// </para>
/// <list type="bullet">
///   <item><description>
///   <b>Array container</b> — sorted list of half-keys with
///   binary-search lookup. Used when cardinality is at or below
///   <c>2^halfWidth / 16</c>.
///   </description></item>
///   <item><description>
///   <b>Bitmap container</b> — flat bitmap of <c>2^halfWidth</c>
///   bits. Used when cardinality exceeds the threshold.
///   </description></item>
/// </list>
/// <para>
/// A run-length-encoded container is part of the standard roaring
/// format but is deferred to a future batch — the visited-set
/// workload this primitive was added for does not produce
/// contiguous ranges and would not benefit. The dispatcher comment
/// in <c>ExpandChunk</c> records this deliberate omission.
/// </para>
/// <para>
/// <b>Concurrency.</b> The bitmap is not thread-safe. Concurrent
/// mutation from multiple threads is undefined behaviour. Read-
/// only access from multiple threads after construction is safe
/// because the internal state is not modified by reads.
/// </para>
/// <para>
/// <b>Disposal.</b> The bitmap implements <see cref="IDisposable"/>
/// because its dense-chunk containers hold
/// <see cref="System.Buffers.IMemoryOwner{T}"/> rentals from
/// <see cref="Memory.VeritasMemoryPool{T}.Shared"/>. Dispose when
/// the bitmap is no longer needed so the rentals return to the
/// pool. Double dispose is safe.
/// </para>
/// </remarks>
[DebuggerDisplay("RoaringBitmap<{typeof(TKey).Name,nq}> Count={Count}, Chunks={ChunkCount}")]
public sealed class RoaringBitmap<TKey>: IDisposable, IEnumerable<TKey>
    where TKey : unmanaged, IBinaryInteger<TKey>, IUnsignedNumber<TKey>
{
    /// <summary>The bit width of <typeparamref name="TKey"/>. 16 for <see cref="ushort"/>, 32 for <see cref="uint"/>, 64 for <see cref="ulong"/>.</summary>
    private static int FullKeyBits { get; } = int.CreateChecked(TKey.PopCount(TKey.AllBitsSet));

    /// <summary>The bit width of each half: half of <see cref="FullKeyBits"/>.</summary>
    private static int HalfKeyBits { get; } = FullKeyBits / 2;

    /// <summary>Mask covering the low half of a key.</summary>
    private static ulong LowMask { get; } = HalfKeyBits == 64 ? ulong.MaxValue : (1UL << HalfKeyBits) - 1;

    /// <summary>The sorted dictionary of chunks keyed by high-half value.</summary>
    private SortedDictionary<ulong, Container> Chunks { get; } = [];

    private long CountField { get; set; }

    private bool DisposedField { get; set; }

    /// <summary>The number of elements in the bitmap.</summary>
    public long Count => CountField;

    /// <summary>The number of distinct high-half chunks currently present.</summary>
    internal int ChunkCount => Chunks.Count;

    /// <summary>
    /// Adds <paramref name="key"/> to the bitmap. Returns
    /// <c>true</c> when the key was newly added; <c>false</c>
    /// when it was already present.
    /// </summary>
    public bool Add(TKey key)
    {
        ObjectDisposedException.ThrowIf(DisposedField, this);

        (ulong high, ulong low) = Split(key);
        if(!Chunks.TryGetValue(high, out Container? container))
        {
            //Brand-new chunk. Start as an array container; it will
            //promote itself to a bitmap when its density exceeds
            //the threshold.
            container = new ArrayContainer();
            Chunks.Add(high, container);
        }

        Container next = container.Add(low, HalfKeyBits, out bool added);
        if(!ReferenceEquals(next, container))
        {
            //Container transitioned between representations.
            //Replace the chunk entry; the displaced instance does
            //not need disposal here because ArrayContainer.Dispose
            //is a no-op and BitmapContainer.Dispose is called by
            //the demotion path itself.
            Chunks[high] = next;
        }

        if(added)
        {
            CountField++;
        }

        return added;
    }

    /// <summary>Returns <c>true</c> when <paramref name="key"/> is in the bitmap.</summary>
    public bool Contains(TKey key)
    {
        ObjectDisposedException.ThrowIf(DisposedField, this);

        (ulong high, ulong low) = Split(key);
        if(!Chunks.TryGetValue(high, out Container? container))
        {
            return false;
        }

        return container.Contains(low);
    }

    /// <summary>
    /// Removes <paramref name="key"/> from the bitmap. Returns
    /// <c>true</c> when the key was present and removed;
    /// <c>false</c> when it was absent.
    /// </summary>
    public bool Remove(TKey key)
    {
        ObjectDisposedException.ThrowIf(DisposedField, this);

        (ulong high, ulong low) = Split(key);
        if(!Chunks.TryGetValue(high, out Container? container))
        {
            return false;
        }

        Container next = container.Remove(low, HalfKeyBits, out bool removed);
        if(removed)
        {
            CountField--;
        }

        if(next.Cardinality == 0)
        {
            //Empty chunk: drop the entry and dispose any rental.
            Chunks.Remove(high);
            next.Dispose();
        }
        else if(!ReferenceEquals(next, container))
        {
            Chunks[high] = next;
        }

        return removed;
    }

    /// <summary>Removes every key from the bitmap.</summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(DisposedField, this);

        foreach(KeyValuePair<ulong, Container> entry in Chunks)
        {
            entry.Value.Dispose();
        }

        Chunks.Clear();
        CountField = 0;
    }

    /// <summary>In-place union with <paramref name="other"/>; both bitmaps share the same key type.</summary>
    public void UnionWith(RoaringBitmap<TKey> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ObjectDisposedException.ThrowIf(DisposedField, this);

        foreach(KeyValuePair<ulong, Container> entry in other.Chunks)
        {
            if(Chunks.TryGetValue(entry.Key, out Container? existing))
            {
                int beforeCardinality = existing.Cardinality;
                Container merged = existing.UnionWith(entry.Value, HalfKeyBits);
                if(!ReferenceEquals(merged, existing))
                {
                    existing.Dispose();
                    Chunks[entry.Key] = merged;
                }

                CountField += merged.Cardinality - beforeCardinality;
            }
            else
            {
                //Other has a chunk we do not — deep-copy so
                //disposal stays independent.
                Container clone = entry.Value.Clone(HalfKeyBits);
                Chunks.Add(entry.Key, clone);
                CountField += clone.Cardinality;
            }
        }
    }

    /// <summary>In-place intersection with <paramref name="other"/>.</summary>
    public void IntersectWith(RoaringBitmap<TKey> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ObjectDisposedException.ThrowIf(DisposedField, this);

        //Iterate a snapshot of the keys so we can mutate the
        //chunk dictionary as we go. Chunks present in this but
        //absent from other become empty and get dropped.
        ulong[] highs = new ulong[Chunks.Count];
        int writeIndex = 0;
        foreach(KeyValuePair<ulong, Container> entry in Chunks)
        {
            highs[writeIndex++] = entry.Key;
        }

        long newCount = 0;
        for(int i = 0; i < highs.Length; i++)
        {
            ulong high = highs[i];
            Container existing = Chunks[high];
            if(!other.Chunks.TryGetValue(high, out Container? otherContainer))
            {
                Chunks.Remove(high);
                existing.Dispose();

                continue;
            }

            Container intersected = existing.IntersectWith(otherContainer, HalfKeyBits);
            if(!ReferenceEquals(intersected, existing))
            {
                //IntersectWith may have disposed the receiver
                //already (BitmapContainer's array-fallback path
                //does so); replace the entry with the new
                //container without a second dispose.
                Chunks[high] = intersected;
            }

            if(intersected.Cardinality == 0)
            {
                Chunks.Remove(high);
                intersected.Dispose();
            }
            else
            {
                newCount += intersected.Cardinality;
            }
        }

        CountField = newCount;
    }

    /// <summary>In-place set difference: this minus <paramref name="other"/>.</summary>
    public void ExceptWith(RoaringBitmap<TKey> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        ObjectDisposedException.ThrowIf(DisposedField, this);

        foreach(KeyValuePair<ulong, Container> entry in other.Chunks)
        {
            if(!Chunks.TryGetValue(entry.Key, out Container? existing))
            {
                continue;
            }

            int beforeCardinality = existing.Cardinality;
            Container after = existing.ExceptWith(entry.Value, HalfKeyBits);
            if(!ReferenceEquals(after, existing))
            {
                Chunks[entry.Key] = after;
            }

            CountField -= beforeCardinality - after.Cardinality;

            if(after.Cardinality == 0)
            {
                Chunks.Remove(entry.Key);
                after.Dispose();
            }
        }
    }

    /// <summary>Returns a struct enumerator over the bitmap's keys in ascending order.</summary>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Releases the bitmap's pool rentals. Idempotent.</summary>
    public void Dispose()
    {
        if(DisposedField)
        {
            return;
        }

        DisposedField = true;
        foreach(KeyValuePair<ulong, Container> entry in Chunks)
        {
            entry.Value.Dispose();
        }

        Chunks.Clear();
        CountField = 0;
    }

    private static (ulong High, ulong Low) Split(TKey key)
    {
        ulong full = ulong.CreateChecked(key);

        return (full >> HalfKeyBits, full & LowMask);
    }

    private static TKey Combine(ulong high, ulong low)
    {
        ulong full = (high << HalfKeyBits) | low;

        return TKey.CreateChecked(full);
    }

    /// <summary>
    /// Hand-written enumerator over a <see cref="RoaringBitmap{TKey}"/>.
    /// Iterates chunks in ascending high-key order and each chunk's
    /// contents in ascending low-key order, combining the halves
    /// into a full <typeparamref name="TKey"/> on each step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returned by value from
    /// <see cref="RoaringBitmap{TKey}.GetEnumerator"/> so that
    /// <c>foreach</c> over the bitmap binds to the struct method
    /// directly and skips the boxing path through
    /// <see cref="IEnumerable{T}.GetEnumerator"/>.
    /// </para>
    /// </remarks>
    public struct Enumerator: IEnumerator<TKey>
    {
        private SortedDictionary<ulong, Container>.Enumerator ChunksEnumerator;

        private IEnumerator<ulong>? CurrentChunkEnumerator;

        private ulong CurrentHigh;

        private TKey CurrentValue;

        internal Enumerator(RoaringBitmap<TKey> bitmap)
        {
            ChunksEnumerator = bitmap.Chunks.GetEnumerator();
            CurrentChunkEnumerator = null;
            CurrentHigh = 0;
            CurrentValue = default;
        }

        /// <summary>The current element.</summary>
        public readonly TKey Current => CurrentValue;

        readonly object IEnumerator.Current => CurrentValue;

        /// <summary>Advances to the next element; returns false at the end of the sequence.</summary>
        public bool MoveNext()
        {
            while(true)
            {
                if(CurrentChunkEnumerator is not null && CurrentChunkEnumerator.MoveNext())
                {
                    CurrentValue = Combine(CurrentHigh, CurrentChunkEnumerator.Current);

                    return true;
                }

                CurrentChunkEnumerator?.Dispose();
                CurrentChunkEnumerator = null;

                if(!ChunksEnumerator.MoveNext())
                {
                    return false;
                }

                KeyValuePair<ulong, Container> entry = ChunksEnumerator.Current;
                CurrentHigh = entry.Key;
                CurrentChunkEnumerator = entry.Value.GetEnumerator();
            }
        }

        /// <summary>Not supported; throws <see cref="NotSupportedException"/>.</summary>
        public void Reset()
        {
            throw new NotSupportedException();
        }

        /// <summary>Releases the inner enumerator if one is held.</summary>
        public void Dispose()
        {
            CurrentChunkEnumerator?.Dispose();
            CurrentChunkEnumerator = null;
            ChunksEnumerator.Dispose();
        }
    }
}
