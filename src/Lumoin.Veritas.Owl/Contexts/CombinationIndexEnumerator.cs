using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The shared bounded-enumeration surface's k-subset kernel: enumerates every
/// size-<see cref="subsetSize"/> index combination of <c>[0, itemCount)</c> in
/// lexicographic order as an iterative index odometer — no recursion, no
/// captured state, the exact sweep the counting rider's told-distinct clique
/// search runs. The index buffer is rented from a caller-threaded
/// <see cref="VeritasMemoryPool{T}"/> and returned on <see cref="Dispose"/>,
/// so every early exit from a sweep must ride a <c>using</c> scope — the
/// pooled rent/return lifecycle across early returns is a named failure class
/// of the surface, closed structurally by the disposable pattern.
/// </summary>
internal struct CombinationIndexEnumerator: IDisposable
{
    /// <summary>The pooled index buffer's owner; <see langword="null"/> for a zero-size subset, which enumerates one empty combination without renting.</summary>
    private readonly IMemoryOwner<int>? owner;

    /// <summary>The number of items the combinations draw from.</summary>
    private readonly int itemCount;

    /// <summary>The size of each enumerated subset.</summary>
    private readonly int subsetSize;

    /// <summary>Whether the first <see cref="MoveNext"/> has produced the initial combination.</summary>
    private bool started;

    /// <summary>Whether the odometer has swept past its last combination.</summary>
    private bool exhausted;

    /// <summary>Initialises the odometer over the pooled index buffer.</summary>
    /// <param name="owner">The pooled index buffer's owner; <see langword="null"/> for a zero-size subset.</param>
    /// <param name="itemCount">The number of items the combinations draw from.</param>
    /// <param name="subsetSize">The size of each enumerated subset.</param>
    private CombinationIndexEnumerator(IMemoryOwner<int>? owner, int itemCount, int subsetSize)
    {
        this.owner = owner;
        this.itemCount = itemCount;
        this.subsetSize = subsetSize;
        started = false;
        exhausted = subsetSize > itemCount;
    }

    /// <summary>Creates the odometer for every size-<paramref name="subsetSize"/> combination of <c>[0, itemCount)</c>. A subset size above the item count enumerates nothing; a zero subset size enumerates one empty combination.</summary>
    /// <param name="pool">The buffer pool the index buffer is rented from.</param>
    /// <param name="itemCount">The number of items the combinations draw from.</param>
    /// <param name="subsetSize">The size of each enumerated subset.</param>
    /// <returns>The enumerator, positioned before the first combination.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="itemCount"/> or <paramref name="subsetSize"/> is negative.</exception>
    public static CombinationIndexEnumerator Create(VeritasMemoryPool<int> pool, int itemCount, int subsetSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegative(subsetSize);

        return new CombinationIndexEnumerator(subsetSize > 0 ? pool.Rent(subsetSize) : null, itemCount, subsetSize);
    }

    /// <summary>The current combination's indices, ascending; valid after <see cref="MoveNext"/> returned <see langword="true"/>.</summary>
    public readonly ReadOnlySpan<int> Current
    {
        get
        {
            return owner is null ? [] : owner.Memory.Span[..subsetSize];
        }
    }

    /// <summary>Advances to the next combination: the initial <c>0, 1, …, k−1</c> prefix first, then the rightmost movable index steps and every index to its right resets tight against it.</summary>
    /// <returns><see langword="true"/> when a combination is available; <see langword="false"/> once the sweep is exhausted.</returns>
    public bool MoveNext()
    {
        if(exhausted)
        {
            return false;
        }

        if(!started)
        {
            started = true;
            if(owner is not null)
            {
                Span<int> initial = owner.Memory.Span;
                for(int i = 0; i < subsetSize; i++)
                {
                    initial[i] = i;
                }
            }

            return true;
        }

        if(owner is null)
        {
            exhausted = true;

            return false;
        }

        Span<int> indices = owner.Memory.Span;
        int position = subsetSize - 1;
        while(position >= 0 && indices[position] == itemCount - subsetSize + position)
        {
            position--;
        }

        if(position < 0)
        {
            exhausted = true;

            return false;
        }

        indices[position]++;
        for(int i = position + 1; i < subsetSize; i++)
        {
            indices[i] = indices[i - 1] + 1;
        }

        return true;
    }

    /// <summary>Returns the pooled index buffer.</summary>
    public readonly void Dispose()
    {
        owner?.Dispose();
    }
}
