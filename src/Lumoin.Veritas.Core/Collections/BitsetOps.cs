using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumoin.Veritas.Core.Collections;

/// <summary>
/// Word-parallel set operations over flat <see cref="ulong"/>-packed bitsets — the
/// dense-small-domain substrate for the reasoner's node labels (subset and equality
/// blocking comparisons) and the worst-case-optimal join's set-intersection inner
/// loop. A set is a span of 64-bit words; bit <c>i</c> lives at word <c>i &gt;&gt; 6</c>,
/// position <c>i &amp; 63</c>. The flat layout wins for the small, bounded, dense
/// label/concept domains here — a compressed (run/array) representation loses in the
/// tight subset/equality loops that dominate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tail invariant (the load-bearing contract).</b> Every operand's bits at indices
/// at or beyond the logical domain size MUST be zero. <see cref="Set"/> only ever
/// touches a valid index, and <see cref="And"/>/<see cref="Or"/>/<see cref="AndNot"/>
/// preserve clear tails (<c>0&amp;x</c>, <c>0|0</c>, <c>0&amp;~x</c> are all zero), so the
/// whole-word reductions (<see cref="IsSubsetOf"/>, <see cref="SetEquals"/>,
/// <see cref="IsEmpty"/>, <see cref="PopCount"/>) are correct without per-call tail
/// masking. A caller that builds a bitset from raw external words must call
/// <see cref="MaskTail"/> once to establish the invariant. Operands compared or combined
/// together must share the same word length.
/// </para>
/// <para>
/// <b>SIMD.</b> The bulk word loops run on the portable <see cref="Vector{T}"/>, which the
/// JIT lowers to AVX2/AVX-512 on x64 and 128-bit PackedSimd on WebAssembly, with a scalar
/// tail and a scalar path when no vector unit is present. The scalar paths are exposed
/// internally as the differential oracle: the vectorised result must equal the scalar
/// result must equal naive set semantics (a fixed-width SIMD specialisation, should one
/// ever measure as worth it, slots in behind the same contract).
/// </para>
/// </remarks>
public static class BitsetOps
{
    /// <summary>The shift mapping a bit index to its word index.</summary>
    private const int WordShift = 6;

    /// <summary>The mask selecting the bit position within a word.</summary>
    private const int BitMask = 63;

    /// <summary>The number of words a bitset over <paramref name="bitCount"/> bits needs.</summary>
    /// <param name="bitCount">The domain size in bits; must be non-negative.</param>
    /// <returns>The word count, rounded up.</returns>
    public static int WordCount(int bitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bitCount);

        return (bitCount + BitMask) >>> WordShift;
    }

    /// <summary>Whether bit <paramref name="index"/> is set.</summary>
    /// <param name="words">The bitset words.</param>
    /// <param name="index">The bit index.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    public static bool Get(ReadOnlySpan<ulong> words, int index)
    {
        return (words[index >>> WordShift] & (1UL << (index & BitMask))) != 0UL;
    }

    /// <summary>Sets bit <paramref name="index"/>.</summary>
    /// <param name="words">The bitset words.</param>
    /// <param name="index">The bit index.</param>
    public static void Set(Span<ulong> words, int index)
    {
        words[index >>> WordShift] |= 1UL << (index & BitMask);
    }

    /// <summary>Clears bit <paramref name="index"/>.</summary>
    /// <param name="words">The bitset words.</param>
    /// <param name="index">The bit index.</param>
    public static void Clear(Span<ulong> words, int index)
    {
        words[index >>> WordShift] &= ~(1UL << (index & BitMask));
    }

    /// <summary>
    /// Zeros the bits at or beyond <paramref name="bitCount"/> in the last word,
    /// establishing the tail invariant for a bitset built from raw external words.
    /// </summary>
    /// <param name="words">The bitset words.</param>
    /// <param name="bitCount">The logical domain size in bits.</param>
    public static void MaskTail(Span<ulong> words, int bitCount)
    {
        int remainder = bitCount & BitMask;
        if(remainder == 0)
        {
            return;
        }

        int lastWord = bitCount >>> WordShift;
        if(lastWord < words.Length)
        {
            words[lastWord] &= (1UL << remainder) - 1UL;
        }
    }

    /// <summary>Intersects in place: <paramref name="target"/> becomes <c>target ∩ other</c>.</summary>
    /// <param name="target">The accumulator, overwritten with the intersection.</param>
    /// <param name="other">The other set; same length as <paramref name="target"/>.</param>
    public static void And(Span<ulong> target, ReadOnlySpan<ulong> other)
    {
        CheckSameLength(target.Length, other.Length);

        ref ulong t = ref MemoryMarshal.GetReference(target);
        ref ulong o = ref MemoryMarshal.GetReference(other);
        int length = target.Length;
        int i = 0;
        if(Vector.IsHardwareAccelerated)
        {
            int width = Vector<ulong>.Count;
            for(; i + width <= length; i += width)
            {
                (Vector.LoadUnsafe(ref t, (nuint)i) & Vector.LoadUnsafe(ref o, (nuint)i)).StoreUnsafe(ref t, (nuint)i);
            }
        }

        for(; i < length; i++)
        {
            target[i] &= other[i];
        }
    }

    /// <summary>Unions in place: <paramref name="target"/> becomes <c>target ∪ other</c>.</summary>
    /// <param name="target">The accumulator, overwritten with the union.</param>
    /// <param name="other">The other set; same length as <paramref name="target"/>.</param>
    public static void Or(Span<ulong> target, ReadOnlySpan<ulong> other)
    {
        CheckSameLength(target.Length, other.Length);

        ref ulong t = ref MemoryMarshal.GetReference(target);
        ref ulong o = ref MemoryMarshal.GetReference(other);
        int length = target.Length;
        int i = 0;
        if(Vector.IsHardwareAccelerated)
        {
            int width = Vector<ulong>.Count;
            for(; i + width <= length; i += width)
            {
                (Vector.LoadUnsafe(ref t, (nuint)i) | Vector.LoadUnsafe(ref o, (nuint)i)).StoreUnsafe(ref t, (nuint)i);
            }
        }

        for(; i < length; i++)
        {
            target[i] |= other[i];
        }
    }

    /// <summary>Subtracts in place: <paramref name="target"/> becomes <c>target ∖ other</c>.</summary>
    /// <param name="target">The accumulator, overwritten with the difference.</param>
    /// <param name="other">The set to remove; same length as <paramref name="target"/>.</param>
    public static void AndNot(Span<ulong> target, ReadOnlySpan<ulong> other)
    {
        CheckSameLength(target.Length, other.Length);

        ref ulong t = ref MemoryMarshal.GetReference(target);
        ref ulong o = ref MemoryMarshal.GetReference(other);
        int length = target.Length;
        int i = 0;
        if(Vector.IsHardwareAccelerated)
        {
            int width = Vector<ulong>.Count;
            for(; i + width <= length; i += width)
            {
                //Vector.AndNot(left, right) is left & ~right.
                Vector.AndNot(Vector.LoadUnsafe(ref t, (nuint)i), Vector.LoadUnsafe(ref o, (nuint)i)).StoreUnsafe(ref t, (nuint)i);
            }
        }

        for(; i < length; i++)
        {
            target[i] &= ~other[i];
        }
    }

    /// <summary>
    /// Whether <paramref name="subset"/> ⊆ <paramref name="superset"/> — no bit set in the
    /// subset is clear in the superset. Both sets must share the same word length and tail
    /// invariant.
    /// </summary>
    /// <param name="subset">The candidate subset.</param>
    /// <param name="superset">The candidate superset.</param>
    /// <returns><see langword="true"/> when every bit of the subset is in the superset.</returns>
    public static bool IsSubsetOf(ReadOnlySpan<ulong> subset, ReadOnlySpan<ulong> superset)
    {
        CheckSameLength(subset.Length, superset.Length);

        ref ulong a = ref MemoryMarshal.GetReference(subset);
        ref ulong b = ref MemoryMarshal.GetReference(superset);
        int length = subset.Length;
        int i = 0;
        if(Vector.IsHardwareAccelerated)
        {
            int width = Vector<ulong>.Count;
            Vector<ulong> violations = Vector<ulong>.Zero;
            for(; i + width <= length; i += width)
            {
                //A violation bit is one set in the subset but not the superset; accumulate
                //and test once at the end rather than branch every iteration.
                violations |= Vector.AndNot(Vector.LoadUnsafe(ref a, (nuint)i), Vector.LoadUnsafe(ref b, (nuint)i));
            }

            if(violations != Vector<ulong>.Zero)
            {
                return false;
            }
        }

        for(; i < length; i++)
        {
            if((subset[i] & ~superset[i]) != 0UL)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether two equal-length bitsets hold the same bits.</summary>
    /// <param name="first">The first set.</param>
    /// <param name="second">The second set; same length as <paramref name="first"/>.</param>
    /// <returns><see langword="true"/> when the sets are equal.</returns>
    public static bool SetEquals(ReadOnlySpan<ulong> first, ReadOnlySpan<ulong> second)
    {
        CheckSameLength(first.Length, second.Length);

        ref ulong a = ref MemoryMarshal.GetReference(first);
        ref ulong b = ref MemoryMarshal.GetReference(second);
        int length = first.Length;
        int i = 0;
        if(Vector.IsHardwareAccelerated)
        {
            int width = Vector<ulong>.Count;
            Vector<ulong> differences = Vector<ulong>.Zero;
            for(; i + width <= length; i += width)
            {
                differences |= Vector.LoadUnsafe(ref a, (nuint)i) ^ Vector.LoadUnsafe(ref b, (nuint)i);
            }

            if(differences != Vector<ulong>.Zero)
            {
                return false;
            }
        }

        for(; i < length; i++)
        {
            if(first[i] != second[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the bitset holds no bits.</summary>
    /// <param name="words">The bitset words.</param>
    /// <returns><see langword="true"/> when every word is zero.</returns>
    public static bool IsEmpty(ReadOnlySpan<ulong> words)
    {
        ref ulong w = ref MemoryMarshal.GetReference(words);
        int length = words.Length;
        int i = 0;
        if(Vector.IsHardwareAccelerated)
        {
            int width = Vector<ulong>.Count;
            Vector<ulong> bits = Vector<ulong>.Zero;
            for(; i + width <= length; i += width)
            {
                bits |= Vector.LoadUnsafe(ref w, (nuint)i);
            }

            if(bits != Vector<ulong>.Zero)
            {
                return false;
            }
        }

        for(; i < length; i++)
        {
            if(words[i] != 0UL)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The number of set bits — the set's cardinality.</summary>
    /// <param name="words">The bitset words.</param>
    /// <returns>The population count.</returns>
    public static int PopCount(ReadOnlySpan<ulong> words)
    {
        //The hardware POPCNT (BitOperations.PopCount) already makes the per-word count a
        //single instruction; the scalar fold is the right shape until a measured workload
        //justifies a vectorised population count (VPOPCNTDQ, gated and downclock-prone).
        int count = 0;
        for(int i = 0; i < words.Length; i++)
        {
            count += BitOperations.PopCount(words[i]);
        }

        return count;
    }

    //The pure-scalar references: the naive-correct default behind which the vectorised
    //paths above are differential-tested (the SatSolver-seam discipline). They are the
    //path that also runs verbatim where no vector unit exists.

    /// <summary>The scalar reference for <see cref="And"/>.</summary>
    /// <param name="target">The accumulator.</param>
    /// <param name="other">The other set.</param>
    internal static void AndScalar(Span<ulong> target, ReadOnlySpan<ulong> other)
    {
        CheckSameLength(target.Length, other.Length);

        for(int i = 0; i < target.Length; i++)
        {
            target[i] &= other[i];
        }
    }

    /// <summary>The scalar reference for <see cref="Or"/>.</summary>
    /// <param name="target">The accumulator.</param>
    /// <param name="other">The other set.</param>
    internal static void OrScalar(Span<ulong> target, ReadOnlySpan<ulong> other)
    {
        CheckSameLength(target.Length, other.Length);

        for(int i = 0; i < target.Length; i++)
        {
            target[i] |= other[i];
        }
    }

    /// <summary>The scalar reference for <see cref="AndNot"/>.</summary>
    /// <param name="target">The accumulator.</param>
    /// <param name="other">The set to remove.</param>
    internal static void AndNotScalar(Span<ulong> target, ReadOnlySpan<ulong> other)
    {
        CheckSameLength(target.Length, other.Length);

        for(int i = 0; i < target.Length; i++)
        {
            target[i] &= ~other[i];
        }
    }

    /// <summary>The scalar reference for <see cref="IsSubsetOf"/>.</summary>
    /// <param name="subset">The candidate subset.</param>
    /// <param name="superset">The candidate superset.</param>
    /// <returns><see langword="true"/> when every bit of the subset is in the superset.</returns>
    internal static bool IsSubsetOfScalar(ReadOnlySpan<ulong> subset, ReadOnlySpan<ulong> superset)
    {
        CheckSameLength(subset.Length, superset.Length);

        for(int i = 0; i < subset.Length; i++)
        {
            if((subset[i] & ~superset[i]) != 0UL)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The scalar reference for <see cref="SetEquals"/>.</summary>
    /// <param name="first">The first set.</param>
    /// <param name="second">The second set.</param>
    /// <returns><see langword="true"/> when the sets are equal.</returns>
    internal static bool SetEqualsScalar(ReadOnlySpan<ulong> first, ReadOnlySpan<ulong> second)
    {
        CheckSameLength(first.Length, second.Length);

        for(int i = 0; i < first.Length; i++)
        {
            if(first[i] != second[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The scalar reference for <see cref="IsEmpty"/>.</summary>
    /// <param name="words">The bitset words.</param>
    /// <returns><see langword="true"/> when every word is zero.</returns>
    internal static bool IsEmptyScalar(ReadOnlySpan<ulong> words)
    {
        for(int i = 0; i < words.Length; i++)
        {
            if(words[i] != 0UL)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when two operands that must align do not share a word length.</summary>
    /// <param name="first">The first length.</param>
    /// <param name="second">The second length.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    private static void CheckSameLength(int first, int second)
    {
        if(first != second)
        {
            throw new ArgumentException($"Bitset operands must share a word length; got {first} and {second}.");
        }
    }
}
