using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The Elias-Fano sequence's contract: it stores a non-decreasing run
/// losslessly and answers <c>Access</c> and <c>NextGEQ</c> exactly as a plain
/// sorted array would — across gappy, dense, duplicate-laden, single, empty,
/// and full-range sequences — and its footprint sits near the Elias-Fano bound.
/// A plain array is the differential oracle.
/// </summary>
[TestClass]
internal sealed class EliasFanoSequenceTests
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

    /// <summary>The smallest index whose value is at least the target, or the length — the linear oracle for <c>NextGEQ</c>.</summary>
    /// <param name="values">The sorted values.</param>
    /// <param name="target">The sought value.</param>
    /// <returns>The lower-bound index.</returns>
    private static int LinearNextGEQ(uint[] values, uint target)
    {
        for(int i = 0; i < values.Length; i++)
        {
            if(values[i] >= target)
            {
                return i;
            }
        }

        return values.Length;
    }

    /// <summary>Builds the sequence and asserts Access round-trips, NextGEQ matches the linear oracle over a spread of targets, and Decode reproduces array windows at varied offsets.</summary>
    /// <param name="values">The non-decreasing values.</param>
    private static void AssertMatchesOracle(uint[] values)
    {
        EliasFanoSequence sequence = EliasFanoSequence.Build(values);

        Assert.AreEqual(values.Length, sequence.Count);

        for(int i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(values[i], sequence.Access(i), $"Access disagreed at {i}");
        }

        //Targets: each value, each value ±1, and the extremes.
        HashSet<uint> targets = [0, uint.MaxValue];
        foreach(uint value in values)
        {
            targets.Add(value);
            targets.Add(value == 0 ? 0 : value - 1);
            targets.Add(value == uint.MaxValue ? uint.MaxValue : value + 1);
        }

        foreach(uint target in targets)
        {
            Assert.AreEqual(LinearNextGEQ(values, target), sequence.NextGEQ(target), $"NextGEQ disagreed at {target}");
        }

        //Decode windows at varied starts — including lane offsets off any word
        //boundary, where the kernel path takes a scalar head before its bulk
        //unpack. The portable build and the injected-kernel build must agree
        //bit-for-bit on every window.
        ColumnarKernelBackend backend = ColumnarKernelBackend.Default;
        EliasFanoSequence kernelSequence = EliasFanoSequence.Build(values, lanePacker: backend.Pack.Invoke, laneUnpacker: backend.DecodeFrame.Invoke);
        uint[] decoded = new uint[values.Length];
        uint[] kernelDecoded = new uint[values.Length];
        foreach(int start in (int[])[0, 1, 3, values.Length / 3, values.Length / 2, Math.Max(0, values.Length - 7)])
        {
            if(start > values.Length)
            {
                continue;
            }

            int count = Math.Min(values.Length - start, 300);
            sequence.Decode(start, count, decoded);
            kernelSequence.Decode(start, count, kernelDecoded);
            for(int k = 0; k < count; k++)
            {
                Assert.AreEqual(values[start + k], decoded[k], $"Decode disagreed at start {start} offset {k}");
                Assert.AreEqual(values[start + k], kernelDecoded[k], $"Kernel decode disagreed at start {start} offset {k}");
            }
        }
    }

    [TestMethod]
    public void GappySequenceRoundTripsAndSeeks()
    {
        uint[] values = new uint[5_000];
        ulong state = 7;
        ulong running = 0;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            running += 1 + (state % 1_000);
            values[i] = (uint)Math.Min(running, uint.MaxValue);
        }

        AssertMatchesOracle(values);
    }

    [TestMethod]
    public void DenseSequenceUsesNoLowBitsAndRoundTrips()
    {
        //universe ≈ count → ℓ = 0; everything rides in the upper vector.
        uint[] values = new uint[2_000];
        for(int i = 0; i < values.Length; i++)
        {
            values[i] = (uint)i;
        }

        EliasFanoSequence sequence = EliasFanoSequence.Build(values);
        Assert.AreEqual(0, sequence.LowBits);
        AssertMatchesOracle(values);
    }

    [TestMethod]
    public void DuplicatesAreSupported()
    {
        uint[] values = [0, 0, 5, 5, 5, 9, 9, 100, 100, 100, 100];
        AssertMatchesOracle(values);
    }

    [TestMethod]
    public void SingleAndEmptyAndFullRange()
    {
        AssertMatchesOracle([42]);
        AssertMatchesOracle([uint.MaxValue]);
        AssertMatchesOracle([0, uint.MaxValue]);

        EliasFanoSequence empty = EliasFanoSequence.Build([]);
        Assert.AreEqual(0, empty.Count);
        Assert.AreEqual(0, empty.NextGEQ(123));
    }

    [TestMethod]
    public void RejectsDescendingInput()
    {
        Assert.ThrowsExactly<ArgumentException>(() => EliasFanoSequence.Build([3, 2]));
    }

    [TestMethod]
    public void FootprintIsNearTheEliasFanoBound()
    {
        //A sequence with universe/count ≈ 256 → the bound is ~ (2 + 8) = 10
        //bits/value; assert the real footprint is within a small overhead.
        uint[] values = new uint[10_000];
        ulong state = 3;
        ulong running = 0;
        for(int i = 0; i < values.Length; i++)
        {
            state = Mix(state);
            running += 1 + (state % 512);
            values[i] = (uint)running;
        }

        EliasFanoSequence sequence = EliasFanoSequence.Build(values);
        double bitsPerValue = (double)sequence.BitCount / values.Length;

        //The Elias-Fano bound here is ~2 + log2(~256) ≈ 10; the select sample
        //adds a little. Generously bounded to catch gross regressions.
        Assert.IsLessThan(16.0, bitsPerValue);
    }
}
