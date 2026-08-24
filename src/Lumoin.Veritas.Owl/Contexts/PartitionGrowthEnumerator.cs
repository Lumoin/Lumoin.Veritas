using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Owl.Contexts;

/// <summary>
/// The shared bounded-enumeration surface's set-partition kernel: enumerates
/// every partition of <c>[0, elementCount)</c> as a restricted growth string —
/// <c>blocks[i]</c> is element <c>i</c>'s block index, <c>blocks[0] = 0</c>,
/// and each entry exceeds the running maximum of its prefix by at most one —
/// advanced as an iterative odometer over Bell(<c>elementCount</c>) strings,
/// no recursion, no captured state. The string and its prefix-maximum shadow
/// are rented from a caller-threaded <see cref="VeritasMemoryPool{T}"/> and
/// returned on <see cref="Dispose"/>, closing the early-return rent/return
/// failure class structurally.
/// </summary>
internal struct PartitionGrowthEnumerator: IDisposable
{
    /// <summary>The pooled buffer's owner: the growth string in the first <see cref="elementCount"/> slots, the prefix-maximum shadow in the second; <see langword="null"/> for zero elements, which enumerates one empty partition without renting.</summary>
    private readonly IMemoryOwner<int>? owner;

    /// <summary>The number of partitioned elements.</summary>
    private readonly int elementCount;

    /// <summary>Whether the first <see cref="MoveNext"/> has produced the single-block initial partition.</summary>
    private bool started;

    /// <summary>Whether the odometer has swept past its last partition.</summary>
    private bool exhausted;

    /// <summary>Initialises the odometer over the pooled string-and-shadow buffer.</summary>
    /// <param name="owner">The pooled buffer's owner; <see langword="null"/> for zero elements.</param>
    /// <param name="elementCount">The number of partitioned elements.</param>
    private PartitionGrowthEnumerator(IMemoryOwner<int>? owner, int elementCount)
    {
        this.owner = owner;
        this.elementCount = elementCount;
        started = false;
        exhausted = false;
    }

    /// <summary>Creates the odometer over every partition of <paramref name="elementCount"/> elements. Zero elements enumerate one empty partition.</summary>
    /// <param name="pool">The buffer pool the string and shadow are rented from.</param>
    /// <param name="elementCount">The number of partitioned elements.</param>
    /// <returns>The enumerator, positioned before the first partition.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="elementCount"/> is negative.</exception>
    public static PartitionGrowthEnumerator Create(VeritasMemoryPool<int> pool, int elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);

        return new PartitionGrowthEnumerator(elementCount > 0 ? pool.Rent(elementCount * 2) : null, elementCount);
    }

    /// <summary>The current partition as a restricted growth string — <c>Current[i]</c> is element <c>i</c>'s block index; valid after <see cref="MoveNext"/> returned <see langword="true"/>.</summary>
    public readonly ReadOnlySpan<int> Current
    {
        get
        {
            return owner is null ? [] : owner.Memory.Span[..elementCount];
        }
    }

    /// <summary>The number of blocks in the current partition — one above the growth string's final prefix maximum.</summary>
    public readonly int BlockCount
    {
        get
        {
            return owner is null ? 0 : owner.Memory.Span[(elementCount * 2) - 1] + 1;
        }
    }

    /// <summary>Advances to the next partition: the all-zero single-block string first, then the rightmost entry that may still grow within the restricted-growth bound steps up and every entry to its right resets to zero.</summary>
    /// <returns><see langword="true"/> when a partition is available; <see langword="false"/> once the sweep is exhausted.</returns>
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
                for(int i = 0; i < elementCount * 2; i++)
                {
                    initial[i] = 0;
                }
            }

            return true;
        }

        if(owner is null)
        {
            exhausted = true;

            return false;
        }

        Span<int> buffer = owner.Memory.Span;
        Span<int> blocks = buffer[..elementCount];
        Span<int> prefixMax = buffer[elementCount..];
        int position = elementCount - 1;
        while(position >= 1 && blocks[position] > prefixMax[position - 1])
        {
            position--;
        }

        if(position < 1)
        {
            exhausted = true;

            return false;
        }

        blocks[position]++;
        prefixMax[position] = Math.Max(prefixMax[position - 1], blocks[position]);
        for(int i = position + 1; i < elementCount; i++)
        {
            blocks[i] = 0;
            prefixMax[i] = prefixMax[i - 1];
        }

        return true;
    }

    /// <summary>Returns the pooled string-and-shadow buffer.</summary>
    public readonly void Dispose()
    {
        owner?.Dispose();
    }
}
