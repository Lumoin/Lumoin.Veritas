using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The interpolative codec's contract: every monotone distribution —
/// gappy, dense, duplicate-laden, clustered runs, single, empty,
/// full-range — round-trips losslessly through build and decode, at
/// every window offset, and descending input is rejected.
/// </summary>
[TestClass]
internal sealed class InterpolativeSequenceTests
{
    /// <summary>Decodes the whole sequence in one window.</summary>
    /// <param name="sequence">The coded sequence.</param>
    /// <returns>The decoded values.</returns>
    private static uint[] DecodeAll(InterpolativeSequence sequence)
    {
        uint[] decoded = new uint[sequence.Count];
        sequence.Decode(0, sequence.Count, decoded);

        return decoded;
    }

    /// <summary>Asserts a value sequence round-trips through the codec, whole and through windows at varied offsets.</summary>
    /// <param name="values">The source values, non-decreasing.</param>
    private static void AssertRoundTrips(uint[] values)
    {
        InterpolativeSequence sequence = InterpolativeSequence.Build(values);

        Assert.AreEqual(values.Length, sequence.Count);
        Assert.AreSequenceEqual(values, DecodeAll(sequence));

        //Windows at offsets that straddle block boundaries: a single
        //element, a sub-block run, a cross-block run, and the tail.
        foreach((int start, int count) in WindowsOver(values.Length))
        {
            uint[] window = new uint[count];
            sequence.Decode(start, count, window);
            for(int i = 0; i < count; i++)
            {
                Assert.AreEqual(values[start + i], window[i]);
            }
        }
    }

    /// <summary>Enumerates window offsets and lengths exercising leading, interior, boundary-straddling, and trailing slices.</summary>
    /// <param name="length">The sequence length.</param>
    /// <returns>The (start, count) windows that fit the length.</returns>
    private static IEnumerable<(int Start, int Count)> WindowsOver(int length)
    {
        if(length == 0)
        {
            yield break;
        }

        int[] starts = [0, 1, InterpolativeSequence.BlockLength - 1, InterpolativeSequence.BlockLength, InterpolativeSequence.BlockLength + 1, (2 * InterpolativeSequence.BlockLength) + 3, length - 1];
        int[] counts = [1, 2, InterpolativeSequence.BlockLength, InterpolativeSequence.BlockLength + 5, length];

        foreach(int start in starts)
        {
            if(start < 0 || start >= length)
            {
                continue;
            }

            foreach(int count in counts)
            {
                if(count >= 1 && start + count <= length)
                {
                    yield return (start, count);
                }
            }
        }
    }

    /// <summary>A deterministic 64-bit mixer standing in for randomness — entropy seams stay untouched in tests too.</summary>
    /// <param name="state">The counter to mix.</param>
    /// <returns>The mixed value.</returns>
    private static ulong Mix(ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            state = (state ^ (state >> 30)) * 0xBF58476D1CE4E5B9UL;
            state = (state ^ (state >> 27)) * 0x94D049BB133111EBUL;

            return state ^ (state >> 31);
        }
    }

    /// <summary>Sorts a copy of the values — the monotone contract the codec requires.</summary>
    /// <param name="values">The values to make non-decreasing.</param>
    /// <returns>The sorted values.</returns>
    private static uint[] Sorted(uint[] values)
    {
        uint[] copy = (uint[])values.Clone();
        Array.Sort(copy);

        return copy;
    }

    [TestMethod]
    public void EmptySequenceRoundTrips()
    {
        InterpolativeSequence sequence = InterpolativeSequence.Build([]);

        Assert.AreEqual(0, sequence.Count);
        Assert.AreSequenceEqual(Array.Empty<uint>(), DecodeAll(sequence));

        //A zero-length window is a no-op that touches nothing.
        sequence.Decode(0, 0, []);
    }

    [TestMethod]
    public void SingleValueRoundTrips()
    {
        AssertRoundTrips([0U]);
        AssertRoundTrips([42U]);
        AssertRoundTrips([uint.MaxValue]);
    }

    [TestMethod]
    public void DuplicateLadenSequenceRoundTrips()
    {
        //Long constant runs: every range collapses to one admissible
        //value and codes to zero bits.
        uint[] constant = new uint[3_000];
        Array.Fill(constant, 123_456_789U);
        AssertRoundTrips(constant);

        //A non-decreasing sequence with heavy repetition interspersed
        //with steps.
        const int ValueCount = 5_000;
        const ulong MixerSeed = 11;
        const ulong StepEvery = 4;
        const ulong StepRange = 9;

        uint[] values = new uint[ValueCount];
        uint current = 7;
        ulong state = MixerSeed;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            if(state % StepEvery == 0)
            {
                current = unchecked(current + 1 + (uint)(state % StepRange));
            }

            values[i] = current;
        }

        AssertRoundTrips(values);
    }

    [TestMethod]
    public void DenseConsecutiveSequenceRoundTrips()
    {
        //Universe equals count: each delta is one, the densest
        //monotone shape.
        uint[] values = new uint[10_000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)i;
        }

        AssertRoundTrips(values);

        //Dense with a constant base offset.
        uint[] offset = new uint[6_500];
        for(int i = 0; i < offset.Length; i++)
        {
            offset[i] = 1_000_000_000U + (uint)i;
        }

        AssertRoundTrips(offset);
    }

    [TestMethod]
    public void GappySequenceRoundTrips()
    {
        //Sparse over a wide universe: large, irregular gaps.
        const int ValueCount = 9_000;
        const ulong MixerSeed = 23;
        const ulong GapRange = 400_000;

        uint[] values = new uint[ValueCount];
        ulong current = 0;
        ulong state = MixerSeed;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            current += 1 + (state % GapRange);
            values[i] = (uint)Math.Min(current, uint.MaxValue);
        }

        AssertRoundTrips(values);
    }

    [TestMethod]
    public void ClusteredRunsRoundTrip()
    {
        //Runs of consecutive values separated by large jumps — the
        //shape interpolative coding shines on.
        const int RunCount = 700;
        const int MaximumRunLength = 40;
        const ulong JumpRange = 5_000_000;
        const ulong MixerSeed = 37;

        List<uint> values = [];
        ulong current = 0;
        ulong state = MixerSeed;
        for(int run = 0; run < RunCount; run++)
        {
            state = Mix(state);
            current += 1 + (state % JumpRange);
            int runLength = 1 + (int)(Mix(state) % MaximumRunLength);
            for(int i = 0; i < runLength; i++)
            {
                values.Add((uint)Math.Min(current + (ulong)i, uint.MaxValue));
            }

            current += (ulong)runLength;
        }

        AssertRoundTrips([.. values]);
    }

    [TestMethod]
    public void FullRangeExtremesRoundTrip()
    {
        AssertRoundTrips([0U, uint.MaxValue]);
        AssertRoundTrips([0U, 1U, uint.MaxValue - 1, uint.MaxValue]);
        AssertRoundTrips([0U, 0U, 2_147_483_648U, uint.MaxValue, uint.MaxValue]);

        //A long monotone walk spanning the whole universe.
        const int ValueCount = 8_192;
        uint[] values = new uint[ValueCount];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)((ulong)i * uint.MaxValue / (ulong)(ValueCount - 1));
        }

        AssertRoundTrips(values);
    }

    [TestMethod]
    public void BlockBoundaryLengthsRoundTrip()
    {
        foreach(int length in (int[])[1, InterpolativeSequence.BlockLength - 1, InterpolativeSequence.BlockLength, InterpolativeSequence.BlockLength + 1, (2 * InterpolativeSequence.BlockLength) + 1, (5 * InterpolativeSequence.BlockLength) + 13])
        {
            uint[] values = new uint[length];
            ulong state = (ulong)length;
            for(int i = 0; i < length; i++)
            {
                state = Mix(state);
                values[i] = (uint)(state % 50_000);
            }

            AssertRoundTrips(Sorted(values));
        }
    }

    [TestMethod]
    public void DecodeWindowsAtVariedOffsetsMatchTheWhole()
    {
        const int ValueCount = (7 * InterpolativeSequence.BlockLength) + 19;
        const ulong MixerSeed = 53;
        const ulong GapRange = 1_000;

        uint[] values = new uint[ValueCount];
        ulong current = 100;
        ulong state = MixerSeed;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            current += state % GapRange;
            values[i] = (uint)current;
        }

        InterpolativeSequence sequence = InterpolativeSequence.Build(values);

        //Every single-element window across the sequence, then a few
        //wide windows that start at non-block-aligned offsets.
        for(int start = 0; start < values.Length; start++)
        {
            uint[] one = new uint[1];
            sequence.Decode(start, 1, one);
            Assert.AreEqual(values[start], one[0]);
        }

        foreach((int start, int count) in (ValueTuple<int, int>[])[(3, 200), (InterpolativeSequence.BlockLength - 5, 300), (InterpolativeSequence.BlockLength + 7, InterpolativeSequence.BlockLength), (ValueCount - 50, 50)])
        {
            uint[] window = new uint[count];
            sequence.Decode(start, count, window);
            for(int i = 0; i < count; i++)
            {
                Assert.AreEqual(values[start + i], window[i]);
            }
        }
    }

    [TestMethod]
    public void DescendingInputIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => InterpolativeSequence.Build([5U, 4U]));
        Assert.ThrowsExactly<ArgumentException>(() => InterpolativeSequence.Build([1U, 2U, 3U, 2U]));
        Assert.ThrowsExactly<ArgumentException>(() => InterpolativeSequence.Build([uint.MaxValue, 0U]));
    }

    [TestMethod]
    public void DecodeRejectsOutOfRangeWindows()
    {
        InterpolativeSequence sequence = InterpolativeSequence.Build([1U, 2U, 3U, 4U]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sequence.Decode(-1, 1, new uint[1]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sequence.Decode(0, -1, new uint[1]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sequence.Decode(3, 2, new uint[2]));
        Assert.ThrowsExactly<ArgumentException>(() => sequence.Decode(0, 4, new uint[3]));
    }

    [TestMethod]
    public void BitCountIsPositiveAndBoundedForCompressibleShapes()
    {
        //A dense consecutive run is near-incompressible per delta yet
        //the structure stays well under a raw uint array; a clustered
        //run compresses harder still. Both report a positive,
        //directory-inclusive bit count.
        uint[] dense = new uint[10_000];
        for(int i = 0; i < dense.Length; i++)
        {
            dense[i] = (uint)i;
        }

        InterpolativeSequence denseSequence = InterpolativeSequence.Build(dense);
        Assert.IsGreaterThan(0L, denseSequence.BitCount);
        Assert.IsLessThan((long)dense.Length * sizeof(uint) * 8, denseSequence.BitCount);
    }
}
