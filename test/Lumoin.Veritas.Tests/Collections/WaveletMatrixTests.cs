using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Collections;

namespace Lumoin.Veritas.Tests.Collections;

/// <summary>
/// The wavelet matrix's contract: <c>Access</c>, symbol <c>Rank</c> and
/// <c>Select</c>, and the range successor <c>TryRangeNextGEQ</c> agree with
/// linear scans of the plain sequence — across duplicate-heavy small
/// alphabets, wide sparse alphabets, binary, constant, sorted, single, and
/// empty sequences, and explicit widths beyond the values' need.
/// </summary>
[TestClass]
internal sealed class WaveletMatrixTests
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

    /// <summary>The number of occurrences of the symbol before the position — the linear oracle for <c>Rank</c>.</summary>
    /// <param name="values">The sequence.</param>
    /// <param name="symbol">The symbol.</param>
    /// <param name="position">The exclusive end position.</param>
    /// <returns>The occurrence count.</returns>
    private static int LinearRank(uint[] values, uint symbol, int position)
    {
        int count = 0;
        for(int i = 0; i < position; i++)
        {
            if(values[i] == symbol)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The smallest value at least the target within the range, or none — the linear oracle for <c>TryRangeNextGEQ</c>.</summary>
    /// <param name="values">The sequence.</param>
    /// <param name="low">The inclusive range start.</param>
    /// <param name="high">The exclusive range end.</param>
    /// <param name="target">The sought lower bound.</param>
    /// <returns>The successor, or <see langword="null"/> when none exists.</returns>
    private static uint? LinearNextGEQ(uint[] values, int low, int high, uint target)
    {
        uint? best = null;
        for(int i = low; i < high; i++)
        {
            if(values[i] >= target && (best is null || values[i] < best.Value))
            {
                best = values[i];
            }
        }

        return best;
    }

    /// <summary>Builds the matrix and asserts every operation against the linear oracles over a spread of symbols, positions, ranges, and targets.</summary>
    /// <param name="values">The sequence.</param>
    /// <param name="bitWidth">The explicit width, or <c>0</c> for automatic.</param>
    private static void AssertMatchesOracle(uint[] values, int bitWidth = 0)
    {
        WaveletMatrix matrix = WaveletMatrix.Build(values, bitWidth);

        Assert.AreEqual(values.Length, matrix.Count);

        for(int i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(values[i], matrix.Access(i), $"Access disagreed at {i}");
        }

        //Symbols: every distinct value, near-misses beside each, and an
        //absent extreme; positions: the ends, thirds, and a straddle.
        HashSet<uint> symbols = [0, uint.MaxValue];
        foreach(uint v in values)
        {
            symbols.Add(v);
            symbols.Add(v == 0 ? 0 : v - 1);
            symbols.Add(v == uint.MaxValue ? uint.MaxValue : v + 1);
        }

        int[] positions = [0, 1, values.Length / 3, values.Length / 2, Math.Max(0, values.Length - 1), values.Length];
        foreach(uint symbol in symbols)
        {
            foreach(int position in positions)
            {
                if(position > values.Length)
                {
                    continue;
                }

                Assert.AreEqual(LinearRank(values, symbol, position), matrix.Rank(symbol, position), $"Rank disagreed for symbol {symbol} at {position}");
            }

            int total = LinearRank(values, symbol, values.Length);
            foreach(int occurrence in (int[])[0, total / 2, total - 1])
            {
                if(occurrence < 0 || occurrence >= total)
                {
                    continue;
                }

                int expected = -1;
                int seen = 0;
                for(int i = 0; i < values.Length; i++)
                {
                    if(values[i] == symbol && seen++ == occurrence)
                    {
                        expected = i;

                        break;
                    }
                }

                Assert.AreEqual(expected, matrix.Select(symbol, occurrence), $"Select disagreed for symbol {symbol} occurrence {occurrence}");
            }
        }

        foreach((int low, int high) in (ReadOnlySpan<(int, int)>)[(0, values.Length), (0, values.Length / 2), (values.Length / 3, values.Length), (values.Length / 2, values.Length / 2)])
        {
            foreach(uint target in symbols)
            {
                uint? expected = LinearNextGEQ(values, low, high, target);
                bool found = matrix.TryRangeNextGEQ(low, high, target, out uint actual);
                Assert.AreEqual(expected is not null, found, $"TryRangeNextGEQ existence disagreed for target {target} in [{low}, {high})");
                if(expected is not null)
                {
                    Assert.AreEqual(expected.Value, actual, $"TryRangeNextGEQ value disagreed for target {target} in [{low}, {high})");
                }
            }
        }
    }

    /// <summary>A sequence of the given length with symbols drawn below the alphabet bound.</summary>
    /// <param name="length">The sequence length.</param>
    /// <param name="alphabet">The exclusive symbol bound.</param>
    /// <param name="seed">The mixer seed.</param>
    /// <returns>The sequence.</returns>
    private static uint[] Sequence(int length, uint alphabet, ulong seed)
    {
        uint[] values = new uint[length];
        ulong state = seed;
        for(int i = 0; i < length; i++)
        {
            state = Mix(state);
            values[i] = (uint)(state % alphabet);
        }

        return values;
    }

    [TestMethod]
    public void DuplicateHeavySmallAlphabetMatchesOracle()
    {
        AssertMatchesOracle(Sequence(2_000, 17, 3));
    }

    [TestMethod]
    public void WideSparseAlphabetMatchesOracle()
    {
        AssertMatchesOracle(Sequence(800, uint.MaxValue, 5));
    }

    [TestMethod]
    public void BinaryConstantSortedSingleAndEmpty()
    {
        AssertMatchesOracle(Sequence(1_500, 2, 7));

        uint[] constant = new uint[600];
        Array.Fill(constant, 42u);
        AssertMatchesOracle(constant);

        uint[] sorted = new uint[1_000];
        for(int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = (uint)(i / 3);
        }

        AssertMatchesOracle(sorted);
        AssertMatchesOracle([123_456_789u]);
        AssertMatchesOracle([]);
    }

    [TestMethod]
    public void ExplicitWidthBeyondTheValuesNeedStillAnswers()
    {
        AssertMatchesOracle(Sequence(700, 50, 11), bitWidth: 32);
    }

    [TestMethod]
    public void RejectsMisfitValuesAndBadArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => WaveletMatrix.Build([256], bitWidth: 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WaveletMatrix.Build([1], bitWidth: 33));

        WaveletMatrix matrix = WaveletMatrix.Build([5, 5, 9]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => matrix.Select(5, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => matrix.Select(7, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => matrix.Rank(5, 4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => matrix.Access(3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => matrix.TryRangeNextGEQ(2, 1, 0, out _));
    }

    [TestMethod]
    public void AbsentSymbolsRankZeroEverywhere()
    {
        WaveletMatrix matrix = WaveletMatrix.Build([1, 2, 3, 1, 2, 3]);

        Assert.AreEqual(0, matrix.Rank(4, 6));
        Assert.AreEqual(0, matrix.Rank(uint.MaxValue, 6));
        Assert.IsFalse(matrix.TryRangeNextGEQ(0, 6, 4, out _));
    }

    [TestMethod]
    public void RankScanModesAnswerIdentically()
    {
        //The mode is a scheduling knob, never a semantic one: every operation
        //must agree across all three modes on the same sequence.
        uint[] values = Sequence(3_000, 600, 23);
        WaveletMatrix sequential = WaveletMatrix.Build(values, rankScanMode: RankScanMode.Sequential);
        WaveletMatrix unrolled = WaveletMatrix.Build(values, rankScanMode: RankScanMode.Unrolled);
        WaveletMatrix vectored = WaveletMatrix.Build(values, rankScanMode: RankScanMode.VectorPopCount);

        for(int i = 0; i < values.Length; i++)
        {
            Assert.AreEqual(sequential.Access(i), unrolled.Access(i), $"Unrolled Access disagreed at {i}");
            Assert.AreEqual(sequential.Access(i), vectored.Access(i), $"VectorPopCount Access disagreed at {i}");
        }

        foreach(uint symbol in (ReadOnlySpan<uint>)[0, 1, 7, 299, 599, 600])
        {
            foreach(int position in (ReadOnlySpan<int>)[0, 1, 63, 64, 511, 512, 513, 1_500, 2_999, 3_000])
            {
                int expected = sequential.Rank(symbol, position);
                Assert.AreEqual(expected, unrolled.Rank(symbol, position), $"Unrolled Rank disagreed for symbol {symbol} at {position}");
                Assert.AreEqual(expected, vectored.Rank(symbol, position), $"VectorPopCount Rank disagreed for symbol {symbol} at {position}");
            }
        }
    }

    [TestMethod]
    public void FootprintStaysNearPayloadTimesWidth()
    {
        //100k symbols over a 10-bit alphabet: payload n·w bits plus the
        //per-level rank/select overhead — assert under 1.3 n·w.
        uint[] values = Sequence(100_000, 1024, 13);
        WaveletMatrix matrix = WaveletMatrix.Build(values);

        Assert.AreEqual(10, matrix.BitWidth);
        Assert.IsLessThan(1.3 * 100_000 * 10, (double)matrix.BitCount);
    }
}
