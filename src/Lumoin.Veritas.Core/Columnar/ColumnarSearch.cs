using System;
using System.Collections.Generic;
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

    /// <summary>The number of radix buckets — one per byte value.</summary>
    private const int RadixBuckets = 256;

    /// <summary>The number of bytes in one 32-bit position field.</summary>
    private const int BytesPerField = 4;

    /// <summary>The number of bytes in the 96-bit composite key — three position fields.</summary>
    private const int TotalKeyBytes = 3 * BytesPerField;

    /// <summary>Sub-ranges at or below this length are insertion-sorted rather than partitioned further.</summary>
    private const int RadixInsertionThreshold = 32;

    /// <summary>
    /// Sorts <paramref name="triples"/> in place under the permutation whose descent positions are
    /// <paramref name="position0"/> / <paramref name="position1"/> / <paramref name="position2"/>, by an in-place
    /// most-significant-digit (American-flag) radix sort over the 96-bit composite key — the most significant byte
    /// of <paramref name="position0"/> first. Neither a packed-key column nor a ping-pong buffer is materialised:
    /// each pass reads the active byte straight from the triple's position value. Small sub-ranges fall to an
    /// insertion sort, and the descent is an explicit work-stack rather than method recursion, so a degenerate key
    /// distribution cannot overflow the call stack.
    /// </summary>
    /// <param name="triples">The triples to sort.</param>
    /// <param name="position0">The first descent position.</param>
    /// <param name="position1">The second descent position.</param>
    /// <param name="position2">The third descent position.</param>
    internal static void SortByPermutation(EncodedTriple[] triples, byte position0, byte position1, byte position2)
    {
        if(triples.Length < 2)
        {
            return;
        }

        //The three descent positions in key order; the composite key's twelve bytes are processed most-significant
        //byte first, so a completed prefix never needs revisiting.
        Span<byte> positions = [position0, position1, position2];

        //Radix scratch reused across every sub-range: the per-byte histogram and the start / moving / end cursor
        //of each bucket. Hoisted out of the loop so no stackalloc accumulates per range.
        Span<int> counts = stackalloc int[RadixBuckets];
        Span<int> bucketStart = stackalloc int[RadixBuckets];
        Span<int> next = stackalloc int[RadixBuckets];
        Span<int> bucketEnd = stackalloc int[RadixBuckets];

        Stack<RadixRange> work = new();
        work.Push(new RadixRange(0, triples.Length, 0));
        while(work.Count > 0)
        {
            RadixRange range = work.Pop();
            if(range.Hi - range.Lo <= RadixInsertionThreshold)
            {
                InsertionSortRange(triples, range.Lo, range.Hi, position0, position1, position2);

                continue;
            }

            byte field = positions[range.Digit / BytesPerField];
            int shift = (BytesPerField - 1 - (range.Digit % BytesPerField)) * 8;

            counts.Clear();
            for(int i = range.Lo; i < range.Hi; i++)
            {
                counts[(byte)(ColumnAt(in triples[i], field) >> shift)]++;
            }

            int offset = range.Lo;
            for(int b = 0; b < RadixBuckets; b++)
            {
                bucketStart[b] = offset;
                next[b] = offset;
                offset += counts[b];
                bucketEnd[b] = offset;
            }

            //In-place permutation: cycle the element at a bucket's cursor to its true bucket, following each
            //displaced element until one belongs at the cursor; a bucket is done when its cursor meets its end.
            for(int b = 0; b < RadixBuckets; b++)
            {
                while(next[b] < bucketEnd[b])
                {
                    EncodedTriple held = triples[next[b]];
                    byte target = (byte)(ColumnAt(in held, field) >> shift);
                    while(target != b)
                    {
                        EncodedTriple displaced = triples[next[target]];
                        triples[next[target]] = held;
                        next[target]++;
                        held = displaced;
                        target = (byte)(ColumnAt(in held, field) >> shift);
                    }

                    triples[next[b]] = held;
                    next[b]++;
                }
            }

            //Descend into each non-trivial bucket at the next byte; a fully-consumed key needs no further ordering.
            if(range.Digit + 1 < TotalKeyBytes)
            {
                for(int b = 0; b < RadixBuckets; b++)
                {
                    if(bucketEnd[b] - bucketStart[b] > 1)
                    {
                        work.Push(new RadixRange(bucketStart[b], bucketEnd[b], range.Digit + 1));
                    }
                }
            }
        }
    }

    /// <summary>Insertion-sorts the triples in <c>[lo, hi)</c> by the permutation's composite key — the cheap tail for sub-ranges a radix pass would not pay off on.</summary>
    /// <param name="triples">The triples being sorted.</param>
    /// <param name="lo">The inclusive range start.</param>
    /// <param name="hi">The exclusive range end.</param>
    /// <param name="position0">The first descent position.</param>
    /// <param name="position1">The second descent position.</param>
    /// <param name="position2">The third descent position.</param>
    private static void InsertionSortRange(EncodedTriple[] triples, int lo, int hi, byte position0, byte position1, byte position2)
    {
        for(int i = lo + 1; i < hi; i++)
        {
            EncodedTriple key = triples[i];
            int j = i - 1;
            while(j >= lo && ComparePermutation(in triples[j], in key, position0, position1, position2) > 0)
            {
                triples[j + 1] = triples[j];
                j--;
            }

            triples[j + 1] = key;
        }
    }

    /// <summary>Compares two triples by the permutation's composite key: <paramref name="position0"/>, then <paramref name="position1"/>, then <paramref name="position2"/>.</summary>
    /// <param name="left">The left triple.</param>
    /// <param name="right">The right triple.</param>
    /// <param name="position0">The first descent position.</param>
    /// <param name="position1">The second descent position.</param>
    /// <param name="position2">The third descent position.</param>
    /// <returns>A negative value, zero, or a positive value as <paramref name="left"/> sorts before, with, or after <paramref name="right"/>.</returns>
    private static int ComparePermutation(in EncodedTriple left, in EncodedTriple right, byte position0, byte position1, byte position2)
    {
        int byPosition0 = ColumnAt(in left, position0).CompareTo(ColumnAt(in right, position0));
        if(byPosition0 != 0)
        {
            return byPosition0;
        }

        int byPosition1 = ColumnAt(in left, position1).CompareTo(ColumnAt(in right, position1));
        if(byPosition1 != 0)
        {
            return byPosition1;
        }

        return ColumnAt(in left, position2).CompareTo(ColumnAt(in right, position2));
    }

    /// <summary>A pending radix sub-range: the triples in <c>[Lo, Hi)</c> agree on every byte before <see cref="Digit"/> and are partitioned on the byte at <see cref="Digit"/> next.</summary>
    /// <param name="Lo">The inclusive range start.</param>
    /// <param name="Hi">The exclusive range end.</param>
    /// <param name="Digit">The composite-key byte index to partition on — 0 (most significant byte of the first position) through 11.</param>
    private readonly record struct RadixRange(int Lo, int Hi, int Digit);
}
