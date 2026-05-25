using BenchmarkDotNet.Attributes;
using Lumoin.Veritas.Core.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Compares <see cref="RoaringBitmap{TKey}"/> against
/// <see cref="HashSet{T}"/> on the visited-set workload that
/// <see cref="Lumoin.Veritas.Rdf.PropertyPathEvaluator"/>'s
/// Kleene helpers drive: many Add calls during BFS, where
/// each Add returns a bool indicating "newly discovered."
/// </summary>
/// <remarks>
/// <para>
/// Two key patterns: <b>Sequential</b> mirrors the chain workload
/// (BFS over a linear graph visits ids 0..N-1 in order — the
/// pattern the prior soak's chain `:p+` exercises). <b>Random</b>
/// mirrors a sparser graph where the visited set is scattered
/// across the id space; the bitmap's array containers are the
/// active code path there.
/// </para>
/// <para>
/// The bench measures the cost of building the visited set from
/// scratch — fresh allocation included — since that mirrors
/// what <c>EvaluateZeroOrMoreAsync</c> does once per evaluation.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "BenchmarkDotNet instantiates this class via reflection.")]
[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "BenchmarkDotNet requires public types and members for its reflection-based runner.")]
public class VisitedSetBenchmark
{
    private uint[] sequentialKeys = null!;

    private uint[] randomKeys = null!;

    /// <summary>The number of distinct keys added to the visited set.</summary>
    [Params(1_000_000, 10_000_000)]
    public int Size { get; set; }

    /// <summary>
    /// Sets up the key arrays once per <see cref="Size"/>. The
    /// arrays are reused across all benchmark methods so the
    /// per-bench cost is just the visited-set construction.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        sequentialKeys = new uint[Size];
        for(int i = 0; i < Size; i++)
        {
            sequentialKeys[i] = (uint)i;
        }

        randomKeys = new uint[Size];
        Random random = new(Seed: 42);
        for(int i = 0; i < Size; i++)
        {
            randomKeys[i] = (uint)random.Next(int.MaxValue);
        }
    }

    /// <summary>Add Size sequential keys into a fresh <see cref="HashSet{T}"/>.</summary>
    [Benchmark(Baseline = true)]
    public int HashSetSequential()
    {
        HashSet<uint> set = [];
        int added = 0;
        for(int i = 0; i < sequentialKeys.Length; i++)
        {
            if(set.Add(sequentialKeys[i]))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>Add Size sequential keys into a fresh <see cref="RoaringBitmap{TKey}"/>.</summary>
    [Benchmark]
    public int RoaringSequential()
    {
        using RoaringBitmap<uint> bitmap = new();
        int added = 0;
        for(int i = 0; i < sequentialKeys.Length; i++)
        {
            if(bitmap.Add(sequentialKeys[i]))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>Add Size random keys into a fresh <see cref="HashSet{T}"/>.</summary>
    [Benchmark]
    public int HashSetRandom()
    {
        HashSet<uint> set = [];
        int added = 0;
        for(int i = 0; i < randomKeys.Length; i++)
        {
            if(set.Add(randomKeys[i]))
            {
                added++;
            }
        }

        return added;
    }

    /// <summary>Add Size random keys into a fresh <see cref="RoaringBitmap{TKey}"/>.</summary>
    [Benchmark]
    public int RoaringRandom()
    {
        using RoaringBitmap<uint> bitmap = new();
        int added = 0;
        for(int i = 0; i < randomKeys.Length; i++)
        {
            if(bitmap.Add(randomKeys[i]))
            {
                added++;
            }
        }

        return added;
    }
}
