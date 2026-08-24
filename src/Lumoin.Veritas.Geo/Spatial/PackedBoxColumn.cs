using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// One pooled column of a <see cref="PackedBoxIndex"/> build: a single rental from a
/// caller-owned <see cref="VeritasMemoryPool{T}"/> that grows on demand. The pool's
/// exact-size rental contract is the column's capacity contract, so no adapter sits
/// between them; a grow returns the current rental and takes a larger one. Under
/// <see cref="EnsureCapacity"/> the column is scratch space — contents do not survive
/// the grow — while <see cref="GrowPreservingContents"/> copies a caller-stated prefix
/// into the replacement before the old rental returns; every span previously read from
/// <see cref="Span"/> is invalid after any grow.
/// </summary>
/// <typeparam name="T">The column's element type.</typeparam>
/// <remarks>
/// Not thread-safe. A column belongs to one index and is disposed with it; the pool
/// stays the caller's to dispose.
/// </remarks>
internal sealed class PackedBoxColumn<T>: IDisposable
{
    /// <summary>The pool the column rents from; never disposed here.</summary>
    private VeritasMemoryPool<T> Pool { get; }

    /// <summary>The current rental, replaced on every grow and released on dispose.</summary>
    private IMemoryOwner<T>? Rental { get; set; }

    /// <summary>Whether this column has been disposed.</summary>
    private bool Disposed { get; set; }

    /// <summary>The current capacity in elements; zero once disposed.</summary>
    public int Capacity => Rental?.Memory.Length ?? 0;

    /// <summary>The full rented span, invalidated by any call that grows the column.</summary>
    public Span<T> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            return Rental!.Memory.Span;
        }
    }

    /// <summary>Rents the initial segment from the caller's pool.</summary>
    /// <param name="pool">The caller-owned pool this column rents from.</param>
    /// <param name="initialCapacity">The initial element count; must be positive.</param>
    public PackedBoxColumn(VeritasMemoryPool<T> pool, int initialCapacity)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        Pool = pool;
        Rental = pool.Rent(initialCapacity);
    }

    /// <summary>
    /// Ensures the column holds at least <paramref name="required"/> elements. A capacity
    /// that already suffices makes the call a no-op; otherwise the current rental returns
    /// and a grown one takes its place.
    /// </summary>
    /// <param name="required">The minimum element count; must be positive.</param>
    public void EnsureCapacity(int required)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(required);

        if(Rental!.Memory.Length >= required)
        {
            return;
        }

        int grown = GrownCapacity(required);
        Rental.Dispose();
        Rental = Pool.Rent(grown);
    }

    /// <summary>
    /// Ensures the column holds at least <paramref name="required"/> elements while
    /// carrying the first <paramref name="preservedCount"/> elements across the
    /// replacement. A capacity that already suffices makes the call a no-op and the
    /// current rental is retained; otherwise the grown rental is taken first, the
    /// preserved prefix is copied into it, and only then does the old rental return.
    /// </summary>
    /// <param name="required">The minimum element count; must be positive.</param>
    /// <param name="preservedCount">The number of leading elements to carry across the replacement; must not exceed the current capacity.</param>
    public void GrowPreservingContents(int required, int preservedCount)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(required);
        ArgumentOutOfRangeException.ThrowIfNegative(preservedCount);

        if(preservedCount > Rental!.Memory.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preservedCount),
                $"The preserved count {preservedCount} cannot exceed the current capacity {Rental.Memory.Length}.");
        }

        if(Rental.Memory.Length >= required)
        {
            return;
        }

        int grown = GrownCapacity(required);
        IMemoryOwner<T> replacement = Pool.Rent(grown);

        //The copy precedes the old rental's return: a disposed segment re-enters its shared
        //size class immediately, and a concurrent renter would race the copy. A failed rent
        //leaves the old rental untouched.
        if(preservedCount > 0)
        {
            Rental.Memory.Span[..preservedCount].CopyTo(replacement.Memory.Span);
        }

        Rental.Dispose();
        Rental = replacement;
    }

    /// <summary>
    /// The growth rule: the larger of the requirement and double the current capacity.
    /// The doubling widens before it can wrap — above one gibi-element the narrow product
    /// turns negative and the maximum would silently degrade amortised doubling into
    /// exact-size growth — and caps at <see cref="Array.MaxLength"/>, which no managed
    /// backing array can exceed.
    /// </summary>
    /// <param name="required">The minimum element count.</param>
    /// <returns>The element count to rent.</returns>
    private int GrownCapacity(int required)
    {
        int doubled = (int)Math.Min((long)Rental!.Memory.Length * 2L, Array.MaxLength);

        return Math.Max(required, doubled);
    }

    /// <summary>Returns the current rental to the pool; idempotent.</summary>
    public void Dispose()
    {
        if(Disposed)
        {
            return;
        }

        Disposed = true;
        Rental?.Dispose();
        Rental = null;
    }
}
