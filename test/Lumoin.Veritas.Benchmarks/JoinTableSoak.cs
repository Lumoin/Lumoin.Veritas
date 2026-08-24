using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak quantifying the open-addressed join table against the chained
/// (<see cref="Dictionary{TKey,TValue}"/>-backed) one on the build side's
/// key→chain-head map — the constant-factor the swap targets. Both store the
/// build rows identically (the columns and the chain); only the head map
/// differs, so the build-allocation delta and the head footprint isolate the
/// map, and the probe time isolates the lookup. Reported comprehensively:
/// build CPU, build allocation, probe CPU, and memory per distinct key.
/// </summary>
/// <remarks>
/// The rungs vary the distinct-key count (where the two maps diverge most) and
/// the fan-out (chain depth, shared by both). Per rung the two tables are
/// verified to agree on a probe before timing. Line-oriented output for
/// hand-collation, the same shape as the other <c>--profile-*</c> soaks.
/// </remarks>
internal static class JoinTableSoak
{
    /// <summary>Runs the soak ladder.</summary>
    public static void RunJoinTableSoak()
    {
        //Shallow (many distinct keys — the map-heavy case), then deeper fan-out.
        RunConfiguration(rows: 2_000_000, distinctKeys: 1_500_000);
        RunConfiguration(rows: 2_000_000, distinctKeys: 500_000);
        RunConfiguration(rows: 2_000_000, distinctKeys: 50_000);
        RunConfiguration(rows: 200_000, distinctKeys: 150_000);
    }

    /// <summary>Builds, verifies, and measures one rung.</summary>
    /// <param name="rows">The build row count.</param>
    /// <param name="distinctKeys">The distinct key count; the fan-out is <paramref name="rows"/> over this.</param>
    private static void RunConfiguration(int rows, int distinctKeys)
    {
        List<SolutionBatch> batches = BuildBatches(rows, distinctKeys);
        JoinKey[] probeKeys = ProbeKeys(distinctKeys);

        Console.WriteLine($"[jointable] rows={rows:N0} distinctKeys={distinctKeys:N0} fanout={(double)rows / distinctKeys:F1}");

        //Warm the JIT and confirm the two tables agree before timing.
        SolutionBatchHashTable warmChained = SolutionBatchHashTable.Build(batches, 2, 0, -1);
        OpenAddressedBatchHashTable warmOpen = OpenAddressedBatchHashTable.Build(batches, 2, 0, -1);
        bool agree = Probe(warmChained.FirstMatch, warmChained.NextMatch, warmChained.ValueAt, probeKeys)
            == Probe(warmOpen.FirstMatch, warmOpen.NextMatch, warmOpen.ValueAt, probeKeys);
        Console.WriteLine($"[jointable]   probe checksums {(agree ? "MATCH" : "MISMATCH")}; open slots={warmOpen.SlotCount:N0} head≈{warmOpen.SlotCount * 13L / (1024.0 * 1024.0):F1} MiB ({warmOpen.SlotCount * 13.0 / distinctKeys:F1} B/key)");

        (double chainedBuildMs, double chainedBuildMiB) = MeasureChainedBuild(batches);
        (double openBuildMs, double openBuildMiB) = MeasureOpenBuild(batches);
        Console.WriteLine($"[jointable]   build chained: {chainedBuildMs,8:F1} ms  {chainedBuildMiB,8:F1} MiB");
        Console.WriteLine($"[jointable]   build open:    {openBuildMs,8:F1} ms  {openBuildMiB,8:F1} MiB");
        Console.WriteLine($"[jointable]   build speedup: x{chainedBuildMs / Math.Max(openBuildMs, 0.01):F2}  alloc: x{chainedBuildMiB / Math.Max(openBuildMiB, 0.01):F2}");

        double chainedProbeMs = MeasureProbe(warmChained.FirstMatch, warmChained.NextMatch, warmChained.ValueAt, probeKeys);
        double openProbeMs = MeasureProbe(warmOpen.FirstMatch, warmOpen.NextMatch, warmOpen.ValueAt, probeKeys);
        Console.WriteLine($"[jointable]   probe chained: {chainedProbeMs,8:F1} ms");
        Console.WriteLine($"[jointable]   probe open:    {openProbeMs,8:F1} ms");
        Console.WriteLine($"[jointable]   probe speedup: x{chainedProbeMs / Math.Max(openProbeMs, 0.01):F2}");
    }

    /// <summary>Times and measures the allocations of building the chained table.</summary>
    /// <param name="batches">The build batches.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureChainedBuild(List<SolutionBatch> batches)
    {
        SoakWindow window = SoakWindow.Open();
        SolutionBatchHashTable table = SolutionBatchHashTable.Build(batches, 2, 0, -1);
        SoakSample sample = window.Close();
        GC.KeepAlive(table);

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Times and measures the allocations of building the open-addressed table.</summary>
    /// <param name="batches">The build batches.</param>
    /// <returns>The elapsed milliseconds and allocated mebibytes.</returns>
    private static (double Milliseconds, double Mebibytes) MeasureOpenBuild(List<SolutionBatch> batches)
    {
        SoakWindow window = SoakWindow.Open();
        OpenAddressedBatchHashTable table = OpenAddressedBatchHashTable.Build(batches, 2, 0, -1);
        SoakSample sample = window.Close();
        GC.KeepAlive(table);

        return (sample.Milliseconds, sample.ThreadAllocatedBytes / (1024.0 * 1024.0));
    }

    /// <summary>Times one full probe pass over the table accessors.</summary>
    /// <param name="first">The table's FirstMatch.</param>
    /// <param name="nextMatch">The table's NextMatch.</param>
    /// <param name="valueAt">The table's ValueAt.</param>
    /// <param name="probeKeys">The keys to probe.</param>
    /// <returns>The elapsed milliseconds.</returns>
    private static double MeasureProbe(Func<JoinKey, int> first, Func<int, int> nextMatch, Func<int, int, uint> valueAt, JoinKey[] probeKeys)
    {
        long start = Stopwatch.GetTimestamp();
        ulong checksum = Probe(first, nextMatch, valueAt, probeKeys);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        GC.KeepAlive(checksum);

        return elapsed.TotalMilliseconds;
    }

    /// <summary>Walks every probe key's chain, folding the matched rows' payloads into a checksum that defeats dead-code elimination and verifies agreement.</summary>
    /// <param name="first">The table's FirstMatch.</param>
    /// <param name="nextMatch">The table's NextMatch.</param>
    /// <param name="valueAt">The table's ValueAt.</param>
    /// <param name="probeKeys">The keys to probe.</param>
    /// <returns>The checksum.</returns>
    private static ulong Probe(Func<JoinKey, int> first, Func<int, int> nextMatch, Func<int, int, uint> valueAt, JoinKey[] probeKeys)
    {
        ulong checksum = 0;
        foreach(JoinKey key in probeKeys)
        {
            for(int rowId = first(key); rowId >= 0; rowId = nextMatch(rowId))
            {
                checksum = (checksum * 1_000_003UL) + valueAt(1, rowId);
            }
        }

        return checksum;
    }

    /// <summary>Builds <paramref name="rows"/> two-column build rows over <paramref name="distinctKeys"/> keys: column 0 the key, column 1 a distinct payload.</summary>
    /// <param name="rows">The row count.</param>
    /// <param name="distinctKeys">The distinct key count.</param>
    /// <returns>The build batches.</returns>
    private static List<SolutionBatch> BuildBatches(int rows, int distinctKeys)
    {
        VariableRegistry registry = new();
        IReadOnlyList<Variable> schema = [registry.GetOrAdd("c0"), registry.GetOrAdd("c1")];

        List<SolutionBatch> batches = [];
        SolutionBatch batch = new(schema);
        int filled = 0;
        for(int i = 0; i < rows; i++)
        {
            batch.ColumnSpan(0)[filled] = (uint)(i % distinctKeys);
            batch.ColumnSpan(1)[filled] = (uint)i;
            filled++;

            if(filled == SolutionBatch.BatchLength)
            {
                batch.SetCount(filled);
                batches.Add(batch);
                batch = new SolutionBatch(schema);
                filled = 0;
            }
        }

        if(filled > 0)
        {
            batch.SetCount(filled);
            batches.Add(batch);
        }

        return batches;
    }

    /// <summary>
    /// Every distinct key plus a quarter again of absent keys, shuffled so the
    /// probe order is independent of the insertion order. Without the shuffle a
    /// table whose storage follows insertion order (the chained one's entries)
    /// would be walked sequentially and prefetched, which is not a join's
    /// access pattern; the shuffle makes both tables face random access.
    /// </summary>
    /// <param name="distinctKeys">The distinct key count.</param>
    /// <returns>The shuffled probe keys.</returns>
    private static JoinKey[] ProbeKeys(int distinctKeys)
    {
        int absent = distinctKeys / 4;
        int total = distinctKeys + absent;
        JoinKey[] probeKeys = new JoinKey[total];
        for(int i = 0; i < total; i++)
        {
            probeKeys[i] = JoinKey.Pack((uint)i, 0);
        }

        //Fisher-Yates over a deterministic mixer (no wall-clock seed).
        ulong state = 0x1234_5678_9ABC_DEF0UL;
        for(int i = total - 1; i > 0; i--)
        {
            state = Mix(state);
            int j = (int)(state % (ulong)(i + 1));
            (probeKeys[i], probeKeys[j]) = (probeKeys[j], probeKeys[i]);
        }

        return probeKeys;
    }

    /// <summary>A deterministic 64-bit mixer for the probe-key shuffle.</summary>
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
