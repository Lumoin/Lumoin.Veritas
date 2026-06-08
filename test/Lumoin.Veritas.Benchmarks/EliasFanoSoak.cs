using System;
using System.Diagnostics;
using Lumoin.Veritas.Core.Collections;
using Lumoin.Veritas.Core.Columnar;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak quantifying the Elias-Fano succinct column encoding against
/// <see cref="BlockPackedColumnMode.FrameOfReference"/> on monotone sequences —
/// the ~10-vs-~22 bits/triple comparison, measured here rather than recalled.
/// For each universe/count ratio it reports the bits per value of each encoding
/// (and the Elias-Fano information bound), then times the seek both serve:
/// Elias-Fano <c>NextGEQ</c> vs frame-of-reference <c>LowerBound</c>.
/// </summary>
/// <remarks>
/// The sequence is a cumulative walk whose average gap sets the universe/count
/// ratio, so the Elias-Fano bound ≈ <c>2 + log2(gap)</c>. Line-oriented output
/// for hand-collation, the same shape as the other <c>--profile-*</c> soaks.
/// </remarks>
internal static class EliasFanoSoak
{
    /// <summary>Runs the soak ladder over a spread of universe/count ratios.</summary>
    public static void RunEliasFanoSoak()
    {
        foreach(int gap in (ReadOnlySpan<int>)[4, 16, 64, 256, 1024])
        {
            RunConfiguration(count: 1_000_000, gap);
        }
    }

    /// <summary>Builds both encodings over a monotone sequence of the given average gap and reports size and seek cost.</summary>
    /// <param name="count">The value count.</param>
    /// <param name="gap">The average successive gap; sets the universe/count ratio.</param>
    private static void RunConfiguration(int count, int gap)
    {
        uint[] values = BuildMonotone(count, gap, out ulong universe);

        EliasFanoSequence eliasFano = EliasFanoSequence.Build(values);
        BlockPackedColumn frame = BlockPackedColumn.Build(values, BlockPackedColumnMode.FrameOfReference);

        double eliasFanoBits = (double)eliasFano.BitCount / count;
        double frameBits = frame.PackedByteCount * 8.0 / count;
        double bound = 2.0 + Math.Log2(gap);

        Console.WriteLine($"[elias-fano] count={count:N0} u/n≈{gap,5} | EF {eliasFanoBits,5:F1} bits/val | FoR {frameBits,5:F1} bits/val | bound≈{bound,4:F1} | size x{frameBits / eliasFanoBits:F2}");

        uint[] targets = BuildTargets(count, universe);
        double eliasFanoSeekMs = TimeEliasFanoSeek(eliasFano, targets);
        double frameSeekMs = TimeFrameSeek(frame, count, targets);

        Console.WriteLine($"[elias-fano]   seek: EF {eliasFanoSeekMs,7:F1} ms  FoR {frameSeekMs,7:F1} ms  (EF x{frameSeekMs / Math.Max(eliasFanoSeekMs, 0.01):F2})");
    }

    /// <summary>Times a full sweep of Elias-Fano successor queries.</summary>
    /// <param name="sequence">The sequence.</param>
    /// <param name="targets">The query targets.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double TimeEliasFanoSeek(EliasFanoSequence sequence, uint[] targets)
    {
        long start = Stopwatch.GetTimestamp();
        ulong checksum = 0;
        foreach(uint target in targets)
        {
            checksum += (ulong)sequence.NextGEQ(target);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        GC.KeepAlive(checksum);

        return elapsed.TotalMilliseconds;
    }

    /// <summary>Times a full sweep of frame-of-reference lower-bound seeks over the whole column.</summary>
    /// <param name="column">The column.</param>
    /// <param name="count">The column length.</param>
    /// <param name="targets">The query targets.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double TimeFrameSeek(BlockPackedColumn column, int count, uint[] targets)
    {
        uint[] scratch = new uint[BlockPackedColumn.BlockLength];
        long start = Stopwatch.GetTimestamp();
        ulong checksum = 0;
        foreach(uint target in targets)
        {
            int cachedBlock = -1;
            checksum += (ulong)column.LowerBound(0, count, target, scratch, ref cachedBlock);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        GC.KeepAlive(checksum);

        return elapsed.TotalMilliseconds;
    }

    /// <summary>A strictly-increasing sequence whose average successive gap is about <paramref name="gap"/>.</summary>
    /// <param name="count">The value count.</param>
    /// <param name="gap">The average gap.</param>
    /// <param name="universe">Receives the universe (max value plus one).</param>
    /// <returns>The monotone values.</returns>
    private static uint[] BuildMonotone(int count, int gap, out ulong universe)
    {
        uint[] values = new uint[count];
        ulong state = 99;
        ulong running = 0;
        uint span = (uint)((2 * gap) - 1);
        for(int i = 0; i < count; i++)
        {
            state = Mix(state);
            running += 1 + (state % span);
            values[i] = (uint)Math.Min(running, uint.MaxValue);
        }

        universe = (ulong)values[count - 1] + 1;

        return values;
    }

    /// <summary>Random query targets across the universe.</summary>
    /// <param name="count">The target count.</param>
    /// <param name="universe">The value universe.</param>
    /// <returns>The targets.</returns>
    private static uint[] BuildTargets(int count, ulong universe)
    {
        uint[] targets = new uint[count];
        ulong state = 1234;
        for(int i = 0; i < count; i++)
        {
            state = Mix(state);
            targets[i] = (uint)(state % universe);
        }

        return targets;
    }

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
}
