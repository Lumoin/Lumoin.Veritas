using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Core.Collections.Internal.RoaringContainers;

/// <summary>
/// Base type for the per-chunk containers in
/// <see cref="RoaringBitmap{TKey}"/>. A chunk holds the low-half
/// keys of all elements sharing the same high-half value; the
/// container chooses its internal representation based on the
/// chunk's density.
/// </summary>
/// <remarks>
/// <para>
/// Half-keys are stored as <see cref="ulong"/> throughout the
/// container layer regardless of the bitmap's outer
/// <c>TKey</c> width. The bitmap masks each full key to its low
/// half before handing the value down. This keeps the container
/// layer non-generic and lets every concrete container share one
/// runtime representation.
/// </para>
/// <para>
/// Mutating operations (<see cref="Add"/>, <see cref="Remove"/>)
/// return a <see cref="Container"/> reference. The returned
/// container may be the same instance (in-place mutation) or a
/// different instance (the operation triggered a transition
/// between container kinds — for example an
/// <see cref="ArrayContainer"/> growing past its density
/// threshold becomes a <see cref="BitmapContainer"/>). Callers
/// that hold a reference to the original container must replace
/// it with the return value and dispose any displaced instance.
/// </para>
/// <para>
/// Set operations (<see cref="UnionWith"/>,
/// <see cref="IntersectWith"/>, <see cref="ExceptWith"/>) are
/// in-place on the receiver where possible; like the mutating
/// operations they may return a different container instance
/// when the result's density crosses a transition threshold.
/// </para>
/// </remarks>
[DebuggerDisplay("Container Cardinality={Cardinality}")]
internal abstract class Container: IDisposable
{
    /// <summary>The threshold at which an array container should grow into a bitmap container, expressed in half-key bits. The standard roaring value is one-sixteenth of the total half-key space.</summary>
    public static int ArrayThresholdFor(int halfKeyBits)
    {
        return (int)((1L << halfKeyBits) / 16);
    }

    /// <summary>The number of <see cref="ulong"/> words a bitmap container needs to cover the full half-key space.</summary>
    public static int BitmapWordCountFor(int halfKeyBits)
    {
        return (int)(((1L << halfKeyBits) + 63) / 64);
    }

    /// <summary>The number of elements currently stored in the container.</summary>
    public abstract int Cardinality { get; }

    /// <summary>
    /// Adds <paramref name="halfKey"/> to the container. Returns
    /// the (possibly new) container holding the result and sets
    /// <paramref name="added"/> to <c>true</c> when the key was
    /// newly added or <c>false</c> when it was already present.
    /// </summary>
    public abstract Container Add(ulong halfKey, int halfKeyBits, out bool added);

    /// <summary>Tests whether <paramref name="halfKey"/> is present in the container.</summary>
    public abstract bool Contains(ulong halfKey);

    /// <summary>
    /// Removes <paramref name="halfKey"/> from the container.
    /// Returns the (possibly new) container holding the result
    /// and sets <paramref name="removed"/> to <c>true</c> when
    /// the key was present and removed, <c>false</c> when it
    /// was absent.
    /// </summary>
    public abstract Container Remove(ulong halfKey, int halfKeyBits, out bool removed);

    /// <summary>
    /// In-place union of this container with <paramref name="other"/>.
    /// Returns the (possibly new) container holding the union;
    /// callers must replace their reference and dispose any
    /// displaced instance.
    /// </summary>
    public abstract Container UnionWith(Container other, int halfKeyBits);

    /// <summary>
    /// In-place intersection of this container with
    /// <paramref name="other"/>. Returns the (possibly new)
    /// container holding the intersection.
    /// </summary>
    public abstract Container IntersectWith(Container other, int halfKeyBits);

    /// <summary>
    /// In-place set difference (this minus <paramref name="other"/>).
    /// Returns the (possibly new) container holding the result.
    /// </summary>
    public abstract Container ExceptWith(Container other, int halfKeyBits);

    /// <summary>
    /// Deep-copies the container's contents into a new instance
    /// that owns its own backing storage. Used by
    /// <see cref="RoaringBitmap{TKey}.UnionWith"/> when the
    /// receiver does not yet have a chunk corresponding to the
    /// other bitmap's chunk and must take its own copy so
    /// disposal semantics remain independent.
    /// </summary>
    public abstract Container Clone(int halfKeyBits);

    /// <summary>Iterates the container's contents in ascending key order.</summary>
    public abstract IEnumerator<ulong> GetEnumerator();

    /// <summary>
    /// Releases any unmanaged or pool-rented backing storage. The
    /// base implementation is a no-op; the
    /// <see cref="BitmapContainer"/> overrides this to return its
    /// rental to the pool.
    /// </summary>
    public virtual void Dispose()
    {
    }
}
