using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The search primitives the columnar index and its cursors share:
/// lower-bound search over an ascending slice of a value column,
/// and its sibling over one position column of a permutation-sorted
/// triple run.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hybrid lower bound.</b> Binary search halves the slice until
/// it fits the scan window, then a vectorised forward scan finds
/// the bound: a compare-and-mask per vector of keys replaces the
/// final data-dependent probe chain, and the window's contiguous
/// loads come from at most a few cache lines. The vector paths
/// dispatch on <c>IsHardwareAccelerated</c>, which the JIT treats
/// as a constant — only the supported path is compiled, with no
/// per-call dispatch.
/// </para>
/// </remarks>
internal static class ColumnarSearch
{
    /// <summary>
    /// The slice length at or below which the lower-bound search
    /// switches from probe halving to a forward vector scan. Sized
    /// to a few vectors' worth of keys — the regime where the
    /// scan's predictable contiguous loads beat the binary search's
    /// dependent probes.
    /// </summary>
    private const int ScanWindow = 32;

    /// <summary>
    /// Returns the smallest index in <c>[lo, hi)</c> whose value is
    /// greater than or equal to <paramref name="target"/>, or
    /// <paramref name="hi"/> when no such index exists.
    /// </summary>
    /// <param name="values">The ascending value column.</param>
    /// <param name="lo">The slice's inclusive start.</param>
    /// <param name="hi">The slice's exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    internal static int LowerBound(ReadOnlySpan<uint> values, int lo, int hi, uint target)
    {
        int low = lo;
        int high = hi;

        while(high - low > ScanWindow)
        {
            int mid = low + ((high - low) >> 1);

            if(values[mid] < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return ScanGreaterEqual(values, low, high, target);
    }

    //Forward-scans the short ascending window [lo, hi) for the
    //first value at or above the target.
    private static int ScanGreaterEqual(ReadOnlySpan<uint> values, int lo, int hi, uint target)
    {
        int i = lo;

        if(Vector256.IsHardwareAccelerated)
        {
            ref uint first = ref MemoryMarshal.GetReference(values);
            Vector256<uint> targetVector = Vector256.Create(target);

            for(; i + Vector256<uint>.Count <= hi; i += Vector256<uint>.Count)
            {
                Vector256<uint> block = Vector256.LoadUnsafe(in first, (nuint)i);
                uint mask = Vector256.GreaterThanOrEqual(block, targetVector).ExtractMostSignificantBits();

                if(mask != 0)
                {
                    return i + BitOperations.TrailingZeroCount(mask);
                }
            }
        }
        else if(Vector128.IsHardwareAccelerated)
        {
            ref uint first = ref MemoryMarshal.GetReference(values);
            Vector128<uint> targetVector = Vector128.Create(target);

            for(; i + Vector128<uint>.Count <= hi; i += Vector128<uint>.Count)
            {
                Vector128<uint> block = Vector128.LoadUnsafe(in first, (nuint)i);
                uint mask = Vector128.GreaterThanOrEqual(block, targetVector).ExtractMostSignificantBits();

                if(mask != 0)
                {
                    return i + BitOperations.TrailingZeroCount(mask);
                }
            }
        }

        for(; i < hi; i++)
        {
            if(values[i] >= target)
            {
                return i;
            }
        }

        return hi;
    }

    /// <summary>
    /// Reads the value at the given RDF position of a triple
    /// (0 = subject, 1 = predicate, 2 = object).
    /// </summary>
    /// <param name="triple">The triple to read.</param>
    /// <param name="position">The RDF position.</param>
    /// <returns>The encoded value at that position.</returns>
    internal static uint ColumnAt(in EncodedTriple triple, byte position)
    {
        return position switch
        {
            0 => triple.Subject.Encoded,
            1 => triple.Predicate.Encoded,
            2 => triple.Object.Encoded,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Position must be 0 (subject), 1 (predicate), or 2 (object)."),
        };
    }

    /// <summary>
    /// Returns the smallest index in <c>[lo, hi)</c> of a triple
    /// run sorted by a permutation whose level column is
    /// <paramref name="position"/>, such that the triple's value at
    /// that column is greater than or equal to
    /// <paramref name="target"/> — or <paramref name="hi"/> when no
    /// such index exists. Within a fixed-prefix run the column is
    /// ascending, so this lands on the first triple of the target
    /// key's run.
    /// </summary>
    /// <param name="triples">The permutation-sorted triple run.</param>
    /// <param name="lo">The slice's inclusive start.</param>
    /// <param name="hi">The slice's exclusive end.</param>
    /// <param name="position">The RDF position the slice's level orders by.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    internal static int LowerBoundByColumn(ReadOnlySpan<EncodedTriple> triples, int lo, int hi, byte position, uint target)
    {
        int low = lo;
        int high = hi;

        while(low < high)
        {
            int mid = low + ((high - low) >> 1);

            if(ColumnAt(in triples[mid], position) < target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>
    /// Returns the smallest index in <c>[lo, hi)</c> whose value at
    /// <paramref name="position"/> is strictly greater than
    /// <paramref name="target"/> — the exclusive end of the target
    /// key's run.
    /// </summary>
    /// <param name="triples">The permutation-sorted triple run.</param>
    /// <param name="lo">The slice's inclusive start.</param>
    /// <param name="hi">The slice's exclusive end.</param>
    /// <param name="position">The RDF position the slice's level orders by.</param>
    /// <param name="target">The key whose run end is sought.</param>
    /// <returns>The upper-bound index.</returns>
    internal static int UpperBoundByColumn(ReadOnlySpan<EncodedTriple> triples, int lo, int hi, byte position, uint target)
    {
        int low = lo;
        int high = hi;

        while(low < high)
        {
            int mid = low + ((high - low) >> 1);

            if(ColumnAt(in triples[mid], position) <= target)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>
    /// Packs a triple's columns, in the descent order given by
    /// <paramref name="position0"/> / <paramref name="position1"/> /
    /// <paramref name="position2"/>, into one 96-bit key inside a
    /// <see cref="UInt128"/>. Lexicographic triple order under the
    /// permutation is exactly unsigned order of the packed keys, so
    /// comparison is a single wide compare with no per-column
    /// conditionals — and sorting reduces to sorting a key column.
    /// </summary>
    /// <param name="triple">The triple to pack.</param>
    /// <param name="position0">The first descent position.</param>
    /// <param name="position1">The second descent position.</param>
    /// <param name="position2">The third descent position.</param>
    /// <returns>The packed sort key.</returns>
    internal static UInt128 PackKey(in EncodedTriple triple, byte position0, byte position1, byte position2)
    {
        return ((UInt128)ColumnAt(in triple, position0) << 64)
            | ((ulong)ColumnAt(in triple, position1) << 32)
            | ColumnAt(in triple, position2);
    }

    /// <summary>
    /// Sorts <paramref name="triples"/> in place under the
    /// permutation whose descent positions are
    /// <paramref name="position0"/> / <paramref name="position1"/> /
    /// <paramref name="position2"/>, by extracting each triple's
    /// packed 96-bit key once and sorting the key column alongside
    /// the triples — no comparator delegate and no per-comparison
    /// column extraction.
    /// </summary>
    /// <param name="triples">The triples to sort.</param>
    /// <param name="position0">The first descent position.</param>
    /// <param name="position1">The second descent position.</param>
    /// <param name="position2">The third descent position.</param>
    internal static void SortByPermutation(EncodedTriple[] triples, byte position0, byte position1, byte position2)
    {
        UInt128[] keys = new UInt128[triples.Length];

        for(int i = 0; i < triples.Length; i++)
        {
            keys[i] = PackKey(in triples[i], position0, position1, position2);
        }

        Array.Sort(keys, triples);
    }
}
