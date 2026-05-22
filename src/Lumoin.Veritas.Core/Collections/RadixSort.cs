using System;
using System.Buffers;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// Least-significant-digit radix sort for unsigned integer keys.
/// Sorts a span in place with O(N) passes over the data; total
/// cost is N times the digit count (four passes for
/// <see cref="uint"/>, eight for <see cref="ulong"/>) plus the
/// linear histogram and prefix-sum work per pass.
/// </summary>
/// <remarks>
/// <para>
/// Radix sort wins over the BCL's introsort
/// (<see cref="Array.Sort{T}(T[])"/>) when the input is large
/// enough that <c>N × digit_count</c> beats <c>N log N</c> — in
/// practice from a few hundred elements upward, with the
/// crossover depending on cache behaviour and the digit
/// distribution. For the hypertrie build path's
/// chunk-grouping workload (sorting <see cref="uint"/> keys
/// in the millions, with non-trivial bucket sizes), radix is
/// the appropriate primitive because every key is touched
/// exactly four times and the work is allocation-light
/// (one pool-rented scratch buffer for the whole sort).
/// </para>
/// <para>
/// <b>Allocation profile.</b> Each call rents one
/// <see cref="IMemoryOwner{T}"/> from
/// <see cref="VeritasMemoryPool{T}.Shared"/> for a scratch
/// buffer sized to the input. Histograms are
/// <c>stackalloc</c>. No per-pass allocation; no per-element
/// allocation. The scratch is returned to the pool on
/// completion.
/// </para>
/// <para>
/// <b>Stability.</b> The implementation is stable — equal
/// keys retain their input order. This matters when sorting
/// pairs (<see cref="Sort(Span{uint}, Span{uint})"/>): a
/// stable sort on the primary key preserves the secondary
/// key's relative order within each primary-key group.
/// </para>
/// </remarks>
public static class RadixSort
{
    /// <summary>The radix base. Eight bits per digit; 256 buckets per pass.</summary>
    private const int RadixBits = 8;

    /// <summary>The number of buckets per pass (<c>1 &lt;&lt; RadixBits</c>).</summary>
    private const int BucketCount = 1 << RadixBits;

    /// <summary>The mask that extracts one digit from a key.</summary>
    private const int DigitMask = BucketCount - 1;

    /// <summary>The number of passes needed to cover a <see cref="uint"/>.</summary>
    private const int UintPassCount = sizeof(uint) * 8 / RadixBits;

    /// <summary>The number of passes needed to cover a <see cref="ulong"/>.</summary>
    private const int UlongPassCount = sizeof(ulong) * 8 / RadixBits;

    /// <summary>
    /// Sorts the <paramref name="keys"/> span in place using
    /// least-significant-digit radix sort. After the call,
    /// <paramref name="keys"/> contains the same values in
    /// ascending order; equal keys keep their relative order
    /// (the sort is stable).
    /// </summary>
    /// <param name="keys">The keys to sort. May be empty.</param>
    public static void Sort(Span<uint> keys)
    {
        if(keys.Length < 2)
        {
            return;
        }

        using IMemoryOwner<uint> scratchOwner = VeritasMemoryPool<uint>.Shared.Rent(keys.Length);
        Span<uint> scratch = scratchOwner.Memory.Span[..keys.Length];
        Span<int> histogram = stackalloc int[BucketCount];

        Span<uint> current = keys;
        Span<uint> next = scratch;

        for(int pass = 0; pass < UintPassCount; pass++)
        {
            int shift = pass * RadixBits;

            histogram.Clear();
            for(int i = 0; i < current.Length; i++)
            {
                int bucket = (int)((current[i] >> shift) & DigitMask);
                histogram[bucket]++;
            }

            int prefix = 0;
            for(int i = 0; i < BucketCount; i++)
            {
                int count = histogram[i];
                histogram[i] = prefix;
                prefix += count;
            }

            for(int i = 0; i < current.Length; i++)
            {
                int bucket = (int)((current[i] >> shift) & DigitMask);
                next[histogram[bucket]++] = current[i];
            }

            Span<uint> temp = current;
            current = next;
            next = temp;
        }

        //Four passes is even, so after the final swap
        //`current` aliases `keys` again — the sorted data
        //is already in place.
    }

    /// <summary>
    /// Sorts the <paramref name="keys"/> span in place and
    /// permutes <paramref name="values"/> so that pair
    /// (keys[i], values[i]) is preserved through the
    /// reordering. Both spans must be the same length.
    /// </summary>
    /// <param name="keys">The keys to sort. May be empty.</param>
    /// <param name="values">The values to permute alongside the keys. Must have the same length as <paramref name="keys"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="values"/> is a different length from <paramref name="keys"/>.</exception>
    public static void Sort(Span<uint> keys, Span<uint> values)
    {
        if(keys.Length != values.Length)
        {
            throw new ArgumentException(
                $"Keys and values must be the same length; got keys.Length={keys.Length}, values.Length={values.Length}.",
                nameof(values));
        }

        if(keys.Length < 2)
        {
            return;
        }

        using IMemoryOwner<uint> keyScratchOwner = VeritasMemoryPool<uint>.Shared.Rent(keys.Length);
        using IMemoryOwner<uint> valueScratchOwner = VeritasMemoryPool<uint>.Shared.Rent(values.Length);
        Span<uint> keyScratch = keyScratchOwner.Memory.Span[..keys.Length];
        Span<uint> valueScratch = valueScratchOwner.Memory.Span[..values.Length];
        Span<int> histogram = stackalloc int[BucketCount];

        Span<uint> currentKeys = keys;
        Span<uint> currentValues = values;
        Span<uint> nextKeys = keyScratch;
        Span<uint> nextValues = valueScratch;

        for(int pass = 0; pass < UintPassCount; pass++)
        {
            int shift = pass * RadixBits;

            histogram.Clear();
            for(int i = 0; i < currentKeys.Length; i++)
            {
                int bucket = (int)((currentKeys[i] >> shift) & DigitMask);
                histogram[bucket]++;
            }

            int prefix = 0;
            for(int i = 0; i < BucketCount; i++)
            {
                int count = histogram[i];
                histogram[i] = prefix;
                prefix += count;
            }

            for(int i = 0; i < currentKeys.Length; i++)
            {
                int bucket = (int)((currentKeys[i] >> shift) & DigitMask);
                int writeAt = histogram[bucket]++;
                nextKeys[writeAt] = currentKeys[i];
                nextValues[writeAt] = currentValues[i];
            }

            Span<uint> tempKeys = currentKeys;
            currentKeys = nextKeys;
            nextKeys = tempKeys;

            Span<uint> tempValues = currentValues;
            currentValues = nextValues;
            nextValues = tempValues;
        }
    }

    /// <summary>
    /// Sorts the <paramref name="keys"/> span of
    /// <see cref="ulong"/> values in place. Eight passes
    /// cover a 64-bit key; otherwise identical to the
    /// <see cref="uint"/> overload.
    /// </summary>
    /// <param name="keys">The keys to sort. May be empty.</param>
    public static void Sort(Span<ulong> keys)
    {
        if(keys.Length < 2)
        {
            return;
        }

        using IMemoryOwner<ulong> scratchOwner = VeritasMemoryPool<ulong>.Shared.Rent(keys.Length);
        Span<ulong> scratch = scratchOwner.Memory.Span[..keys.Length];
        Span<int> histogram = stackalloc int[BucketCount];

        Span<ulong> current = keys;
        Span<ulong> next = scratch;

        for(int pass = 0; pass < UlongPassCount; pass++)
        {
            int shift = pass * RadixBits;

            histogram.Clear();
            for(int i = 0; i < current.Length; i++)
            {
                int bucket = (int)((current[i] >> shift) & DigitMask);
                histogram[bucket]++;
            }

            int prefix = 0;
            for(int i = 0; i < BucketCount; i++)
            {
                int count = histogram[i];
                histogram[i] = prefix;
                prefix += count;
            }

            for(int i = 0; i < current.Length; i++)
            {
                int bucket = (int)((current[i] >> shift) & DigitMask);
                next[histogram[bucket]++] = current[i];
            }

            Span<ulong> temp = current;
            current = next;
            next = temp;
        }

        //Eight passes is even, so the result is already in
        //`keys` after the final swap.
    }
}
