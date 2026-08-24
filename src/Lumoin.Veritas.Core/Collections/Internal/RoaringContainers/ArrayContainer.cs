using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Collections.Internal.RoaringContainers;

/// <summary>
/// Sparse-chunk representation: a sorted <see cref="List{T}"/> of
/// half-keys backed by binary-search lookup and ordered insert.
/// Used when a chunk's element count is at or below the array
/// threshold (one-sixteenth of the half-key space).
/// </summary>
/// <remarks>
/// <para>
/// Storage is one <see cref="ulong"/> slot per element. Memory
/// cost grows linearly with cardinality; lookup cost is
/// O(log&#160;Cardinality) via <see cref="List{T}.BinarySearch(T)"/>;
/// insert and remove cost is O(Cardinality) because the slice
/// after the affected index shifts.
/// </para>
/// <para>
/// When <see cref="Add"/> would push the cardinality past the
/// array threshold the container promotes itself to a
/// <see cref="BitmapContainer"/> and returns that. When
/// <see cref="Remove"/> on a bitmap container drops below the
/// threshold the bitmap demotes to an array; the demotion lives
/// on <see cref="BitmapContainer.Remove"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("ArrayContainer Cardinality={Cardinality}, Capacity={Keys.Capacity}")]
internal sealed class ArrayContainer: Container
{
    /// <summary>The sorted backing list of half-keys.</summary>
    private List<ulong> Keys { get; }

    /// <summary>Constructs an empty container with an optional initial capacity hint.</summary>
    public ArrayContainer(int initialCapacity = 0)
    {
        Keys = new List<ulong>(initialCapacity);
    }

    /// <summary>Internal constructor for clones; takes ownership of <paramref name="keys"/>.</summary>
    private ArrayContainer(List<ulong> keys)
    {
        Keys = keys;
    }

    /// <inheritdoc/>
    public override int Cardinality => Keys.Count;

    /// <inheritdoc/>
    public override Container Add(ulong halfKey, int halfKeyBits, out bool added)
    {
        int index = Keys.BinarySearch(halfKey);
        if(index >= 0)
        {
            added = false;

            return this;
        }

        int insertAt = ~index;
        Keys.Insert(insertAt, halfKey);
        added = true;

        if(Keys.Count > ArrayThresholdFor(halfKeyBits))
        {
            //Density crossed the array threshold — promote to a
            //bitmap container and let the caller swap us out.
            return PromoteToBitmap(halfKeyBits);
        }

        return this;
    }

    /// <inheritdoc/>
    public override bool Contains(ulong halfKey)
    {
        return Keys.BinarySearch(halfKey) >= 0;
    }

    /// <inheritdoc/>
    public override Container Remove(ulong halfKey, int halfKeyBits, out bool removed)
    {
        _ = halfKeyBits;
        int index = Keys.BinarySearch(halfKey);
        if(index < 0)
        {
            removed = false;

            return this;
        }

        Keys.RemoveAt(index);
        removed = true;

        return this;
    }

    /// <inheritdoc/>
    public override Container UnionWith(Container other, int halfKeyBits)
    {
        switch(other)
        {
            case ArrayContainer arrayOther:
            {
                //Merge two sorted lists by walking them in
                //parallel; the result is sorted. If the merged
                //cardinality crosses the array threshold, promote
                //to a bitmap.
                List<ulong> merged = new(Keys.Count + arrayOther.Keys.Count);
                int i = 0;
                int j = 0;
                while(i < Keys.Count && j < arrayOther.Keys.Count)
                {
                    ulong a = Keys[i];
                    ulong b = arrayOther.Keys[j];
                    if(a == b)
                    {
                        merged.Add(a);
                        i++;
                        j++;
                    }
                    else if(a < b)
                    {
                        merged.Add(a);
                        i++;
                    }
                    else
                    {
                        merged.Add(b);
                        j++;
                    }
                }

                while(i < Keys.Count)
                {
                    merged.Add(Keys[i++]);
                }

                while(j < arrayOther.Keys.Count)
                {
                    merged.Add(arrayOther.Keys[j++]);
                }

                Keys.Clear();
                Keys.AddRange(merged);

                if(Keys.Count > ArrayThresholdFor(halfKeyBits))
                {
                    return PromoteToBitmap(halfKeyBits);
                }

                return this;
            }

            case BitmapContainer bitmapOther:
            {
                //The union with a bitmap is at least as dense as
                //the bitmap; copy the bitmap and OR in this
                //container's keys. The result is a bitmap unless
                //post-merge density argues for demotion (which we
                //do not check here — the standard heuristic only
                //demotes after Remove operations).
                BitmapContainer result = (BitmapContainer)bitmapOther.Clone(halfKeyBits);
                foreach(ulong key in Keys)
                {
                    _ = result.Add(key, halfKeyBits, out _);
                }

                return result;
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
        _ = halfKeyBits;
        switch(other)
        {
            case ArrayContainer arrayOther:
            {
                List<ulong> retained = new(Math.Min(Keys.Count, arrayOther.Keys.Count));
                int i = 0;
                int j = 0;
                while(i < Keys.Count && j < arrayOther.Keys.Count)
                {
                    ulong a = Keys[i];
                    ulong b = arrayOther.Keys[j];
                    if(a == b)
                    {
                        retained.Add(a);
                        i++;
                        j++;
                    }
                    else if(a < b)
                    {
                        i++;
                    }
                    else
                    {
                        j++;
                    }
                }

                Keys.Clear();
                Keys.AddRange(retained);

                return this;
            }

            case BitmapContainer bitmapOther:
            {
                //Walk our sorted keys and keep only those the
                //bitmap also has. The array is already sparse so
                //the result stays an array container.
                int writeIndex = 0;
                for(int i = 0; i < Keys.Count; i++)
                {
                    if(bitmapOther.Contains(Keys[i]))
                    {
                        Keys[writeIndex++] = Keys[i];
                    }
                }

                Keys.RemoveRange(writeIndex, Keys.Count - writeIndex);

                return this;
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
        _ = halfKeyBits;
        int writeIndex = 0;
        for(int i = 0; i < Keys.Count; i++)
        {
            if(!other.Contains(Keys[i]))
            {
                Keys[writeIndex++] = Keys[i];
            }
        }

        Keys.RemoveRange(writeIndex, Keys.Count - writeIndex);

        return this;
    }

    /// <inheritdoc/>
    public override Container Clone(int halfKeyBits)
    {
        _ = halfKeyBits;
        List<ulong> copy = new(Keys.Count);
        copy.AddRange(Keys);

        return new ArrayContainer(copy);
    }

    /// <inheritdoc/>
    public override IEnumerator<ulong> GetEnumerator()
    {
        return Keys.GetEnumerator();
    }

    /// <summary>Returns the internal sorted-keys view for promotion helpers; not part of the public contract.</summary>
    internal IReadOnlyList<ulong> KeysView => Keys;

    /// <summary>
    /// Bulk-loads an already-sorted batch of half-keys into the
    /// container without binary search or threshold checks. Used
    /// by <see cref="BitmapContainer"/>'s demotion path, where
    /// the bitmap enumeration yields keys in ascending order
    /// already and we need to bypass the per-insert promotion
    /// check that <see cref="Add"/> applies.
    /// </summary>
    internal void BulkAppendSorted(ulong halfKey)
    {
        Keys.Add(halfKey);
    }

    private BitmapContainer PromoteToBitmap(int halfKeyBits)
    {
        BitmapContainer bitmap = new(halfKeyBits);
        foreach(ulong key in Keys)
        {
            _ = bitmap.Add(key, halfKeyBits, out _);
        }

        //The promoted bitmap owns its own pool rental; the array
        //container itself holds no unmanaged resources, so it is
        //safe to leave the List<ulong> to the GC.
        return bitmap;
    }
}
