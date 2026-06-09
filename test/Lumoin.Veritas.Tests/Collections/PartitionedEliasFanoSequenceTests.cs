using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The partitioned Elias-Fano sequence's contract: a concatenation of ascending
/// runs that reset across segment boundaries, stored losslessly, with
/// <c>Access</c>, <c>DecodeSegment</c>, and the segment-local <c>LowerBound</c>
/// matching a plain-array oracle across mixed segment sizes, single-element
/// segments, dense (no-low-bit) segments, and duplicate-laden runs. A plain
/// array plus the boundaries is the differential oracle.
/// </summary>
[TestClass]
internal sealed class PartitionedEliasFanoSequenceTests
{
    /// <summary>A deterministic 64-bit mixer standing in for randomness.</summary>
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

    /// <summary>The smallest index in <c>[start, end)</c> whose value is at least the target, or <paramref name="end"/> — the linear oracle for the segment-local lower bound.</summary>
    /// <param name="values">The values.</param>
    /// <param name="start">The segment's inclusive start.</param>
    /// <param name="end">The segment's exclusive end.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    private static int LinearLowerBound(uint[] values, int start, int end, uint target)
    {
        for(int i = start; i < end; i++)
        {
            if(values[i] >= target)
            {
                return i;
            }
        }

        return end;
    }

    /// <summary>Builds the sequence and asserts Access, DecodeSegment, and the segment-local LowerBound all match the oracle.</summary>
    /// <param name="values">The values, non-decreasing within each segment.</param>
    /// <param name="boundaries">The exclusive-end segment boundaries.</param>
    private static void AssertMatchesOracle(uint[] values, int[] boundaries)
    {
        PartitionedEliasFanoSequence sequence = PartitionedEliasFanoSequence.Build(values, boundaries);

        Assert.AreEqual(values.Length, sequence.Count);
        Assert.AreEqual(boundaries.Length - 1, sequence.SegmentCount);

        for(int i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(values[i], sequence.Access(i), $"Access disagreed at {i}");
        }

        for(int g = 0; g < boundaries.Length - 1; g++)
        {
            int start = boundaries[g];
            int end = boundaries[g + 1];
            int count = end - start;

            uint[] decoded = new uint[count];
            sequence.DecodeSegment(g, decoded);
            for(int j = 0; j < count; j++)
            {
                Assert.AreEqual(values[start + j], decoded[j], $"DecodeSegment disagreed at segment {g} index {j}");
            }

            HashSet<uint> targets = [0, uint.MaxValue];
            for(int j = start; j < end; j++)
            {
                targets.Add(values[j]);
                targets.Add(values[j] == 0 ? 0 : values[j] - 1);
                targets.Add(values[j] == uint.MaxValue ? uint.MaxValue : values[j] + 1);
            }

            foreach(uint target in targets)
            {
                Assert.AreEqual(LinearLowerBound(values, start, end, target), sequence.LowerBound(start, end, target), $"LowerBound disagreed at segment {g} target {target}");
            }
        }
    }

    /// <summary>Builds a corpus of many segments of mixed size, each an ascending run from its own base — segment minima reset arbitrarily across boundaries.</summary>
    /// <param name="segments">The segment count.</param>
    /// <param name="seed">The mixer seed.</param>
    /// <returns>The values and the exclusive-end boundaries.</returns>
    private static (uint[] Values, int[] Boundaries) BuildMixed(int segments, ulong seed)
    {
        List<uint> values = [];
        List<int> boundaries = [0];
        ulong state = seed;
        for(int g = 0; g < segments; g++)
        {
            state = Mix(state);
            int size = 1 + (int)(state % 40);
            state = Mix(state);
            ulong running = state % 5_000_000;
            for(int j = 0; j < size; j++)
            {
                state = Mix(state);
                running += state % 200;
                values.Add((uint)Math.Min(running, uint.MaxValue));
            }

            boundaries.Add(values.Count);
        }

        return (values.ToArray(), boundaries.ToArray());
    }

    [TestMethod]
    public void MixedSegmentSizesRoundTripAndSeek()
    {
        (uint[] values, int[] boundaries) = BuildMixed(300, seed: 11);
        AssertMatchesOracle(values, boundaries);
    }

    [TestMethod]
    public void SingleElementSegmentsRoundTripAndSeek()
    {
        uint[] values = new uint[500];
        int[] boundaries = new int[values.Length + 1];
        ulong state = 5;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            values[i] = (uint)(state % 9_000_000);
            boundaries[i + 1] = i + 1;
        }

        AssertMatchesOracle(values, boundaries);
    }

    [TestMethod]
    public void DenseSegmentUsesNoLowBitsAndRoundTrips()
    {
        //One segment, universe ≈ count → ℓ = 0; everything rides in the upper vector.
        uint[] values = new uint[2_000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)i;
        }

        AssertMatchesOracle(values, [0, values.Length]);
    }

    [TestMethod]
    public void DuplicatesWithinSegmentsAreSupported()
    {
        uint[] values = [10, 10, 12, 12, 12, /* seg */ 3, 3, 9, /* seg */ 100, 100];
        AssertMatchesOracle(values, [0, 5, 8, 10]);
    }

    [TestMethod]
    public void LargeClusteredSegmentFootprintTracksLocalEntropy()
    {
        //A single large, tightly-clustered run: local span ≈ 16·count, so the
        //bound is ~2 + log2(16) ≈ 6 bits/value — far below the 32-bit raw width.
        uint[] values = new uint[50_000];
        ulong running = 7_000_000;
        ulong state = 99;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            running += 1 + (state % 31);
            values[i] = (uint)running;
        }

        PartitionedEliasFanoSequence sequence = PartitionedEliasFanoSequence.Build(values, [0, values.Length]);
        double bitsPerValue = (double)sequence.BitCount / values.Length;
        Assert.IsLessThan(12.0, bitsPerValue);
    }

    [TestMethod]
    public void RejectsMalformedBoundaries()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PartitionedEliasFanoSequence.Build([1, 2, 3], [1, 3]));
        Assert.ThrowsExactly<ArgumentException>(() => PartitionedEliasFanoSequence.Build([1, 2, 3], [0, 2]));
    }

    [TestMethod]
    public void RejectsNonMonotoneSegment()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PartitionedEliasFanoSequence.Build([5, 3], [0, 2]));
    }
}
