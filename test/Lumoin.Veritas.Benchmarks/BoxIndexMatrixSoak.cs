using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Geo.Spatial;

using static Lumoin.Veritas.Benchmarks.BoxSoakWorkloads;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The box-index measurement matrix (<c>--profile-box-index-matrix</c>): both
/// packings across the capacity sweep over consumer-shaped workloads, under
/// the house measurement protocol — alternated combination order over two
/// rounds, five repeats per decision cell reporting min and median,
/// wall-clock plus precise allocated bytes, shared-pool trims between
/// combinations, and a candidate-set digest gate that must agree across every
/// configuration before any number counts. Workloads: dataset-scale synthetic
/// shapes at rising item counts, a join-cadence rung at tiny counts against a
/// brute-force scan baseline, and a thread-scaling rung over one built index.
/// The run ends by evaluating the pre-registered default rule over the
/// primary cells and printing the pick.
/// </summary>
internal static class BoxIndexMatrixSoak
{
    /// <summary>Repeats per decision cell; the reading reports the median and the min.</summary>
    private const int DecisionRepeats = 5;

    /// <summary>Queries per measured cell.</summary>
    private const int QueriesPerCell = 1_000;

    /// <summary>The capacity sweep of the full protocol.</summary>
    private static readonly int[] FullCapacities = [4, 8, 10, 16, 32, 64, 128, 256];

    /// <summary>The capacity sweep of the quick smoke protocol.</summary>
    private static readonly int[] QuickCapacities = [4, 16, 64];

    /// <summary>The capacity pair of the seven-figure coverage row.</summary>
    private static readonly int[] CoverageCapacities = [16, 64];

    /// <summary>Both packing families, in the order the alternation walks them.</summary>
    private static readonly BoxIndexPacking[] Packings = [BoxIndexPacking.SortTileRecursive, BoxIndexPacking.HilbertCurve];

    /// <summary>One measured cell: the repeat build, materialize, and query readings of one configuration over one workload.</summary>
    /// <param name="Workload">The workload name.</param>
    /// <param name="Packing">The packing family.</param>
    /// <param name="Capacity">The node capacity.</param>
    /// <param name="BuildMedianMs">The median build wall-clock in milliseconds.</param>
    /// <param name="BuildMinMs">The minimum build wall-clock in milliseconds.</param>
    /// <param name="MaterializeMedianMs">The median dominance-materialization wall-clock in milliseconds, timed as its own step between the build and the query sweep so neither absorbs it.</param>
    /// <param name="MaterializeMinMs">The minimum dominance-materialization wall-clock in milliseconds.</param>
    /// <param name="QueryMedianMs">The median query-sweep wall-clock in milliseconds.</param>
    /// <param name="QueryMinMs">The minimum query-sweep wall-clock in milliseconds.</param>
    /// <param name="AllocatedBytesPerQuery">The precise allocation per query in bytes.</param>
    /// <param name="Digest">The candidate-set digest of the cell.</param>
    private sealed record CellReading(
        string Workload,
        BoxIndexPacking Packing,
        int Capacity,
        double BuildMedianMs,
        double BuildMinMs,
        double MaterializeMedianMs,
        double MaterializeMinMs,
        double QueryMedianMs,
        double QueryMinMs,
        long AllocatedBytesPerQuery,
        string Digest);

    /// <summary>One configuration's aggregate over the primary cells.</summary>
    /// <param name="Packing">The packing family.</param>
    /// <param name="Capacity">The node capacity.</param>
    /// <param name="GeometricMeanMs">The geometric mean of the per-cell query medians.</param>
    /// <param name="NoiseBandMs">The summed median-minus-min spread across the cells.</param>
    /// <param name="BuildMedianMs">The mean build median across the cells.</param>
    private sealed record ConfigurationAggregate(
        BoxIndexPacking Packing,
        int Capacity,
        double GeometricMeanMs,
        double NoiseBandMs,
        double BuildMedianMs);

    /// <summary>Runs the matrix; <c>--quick</c> selects the smoke protocol.</summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The task the run completes on.</returns>
    public static async Task RunBoxIndexMatrixSoak(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        int[] capacities = quick ? QuickCapacities : FullCapacities;
        long[] datasetSizes = quick ? [1_000L, 10_000L, 100_000L] : [100L, 1_000L, 10_000L, 100_000L, 1_000_000L];

        Console.WriteLine($"[box-index] matrix: {(quick ? "quick" : "full")} protocol - {Packings.Length} packings x {capacities.Length} capacities, {DecisionRepeats} repeats per decision cell, two alternated rounds, digest gate on.");

        var readings = new List<CellReading>();
        bool digestFailure = false;

        //Dataset-scale rung: per (shape, N) the digest must agree across every configuration.
        foreach(string shape in new[] { "uniform", "clustered", "archipelago", "point-box" })
        {
            foreach(long itemCount in datasetSizes)
            {
                BoundingBox[] items = BuildShape(shape, itemCount);
                BoundingBox[] probes = BuildProbes(items, QueriesPerCell);
                string? sharedDigest = null;

                foreach((BoxIndexPacking packing, int capacity) in AlternatedCombinations(capacities))
                {
                    CellReading reading = MeasureCell($"{shape}/N={itemCount}", packing, capacity, items, probes);
                    readings.Add(reading);

                    if(sharedDigest is null)
                    {
                        sharedDigest = reading.Digest;
                    }
                    else if(!string.Equals(sharedDigest, reading.Digest, StringComparison.Ordinal))
                    {
                        digestFailure = true;
                        Console.WriteLine($"  DIGEST MISMATCH at {shape}/N={itemCount} {packing}/cap {capacity}: {reading.Digest} != {sharedDigest}");
                    }

                    TrimSharedPools();
                }

                Console.WriteLine($"{shape}/N={itemCount}: digest {sharedDigest?[..16]} agreed across {Packings.Length * capacities.Length} configurations.");
            }
        }

        //The seven-figure coverage row runs once per configuration, not five times - a
        //single-repeat coverage reading, stated so the cap is never silent.
        if(!quick)
        {
            foreach(string shape in new[] { "uniform", "clustered" })
            {
                BoundingBox[] items = BuildShape(shape, 10_000_000L);
                BoundingBox[] probes = BuildProbes(items, 200);
                string? sharedDigest = null;

                foreach((BoxIndexPacking packing, int capacity) in AlternatedCombinations(CoverageCapacities))
                {
                    CellReading reading = MeasureCell($"{shape}/N=10000000 (coverage, single repeat)", packing, capacity, items, probes, repeats: 1);
                    readings.Add(reading);
                    sharedDigest ??= reading.Digest;
                    digestFailure |= !string.Equals(sharedDigest, reading.Digest, StringComparison.Ordinal);
                    TrimSharedPools();
                }

                Console.WriteLine($"{shape}/N=10000000: coverage row complete, digest {sharedDigest?[..16]} agreed.");
            }
        }

        //Join-cadence rung: build + query cycles at tiny counts; the crossover against a
        //brute-force scan is a named deliverable, so the scan baseline is measured beside it.
        RunJoinCadence(capacities, quick);

        //Thread-scaling rung: query throughput over one built index; the per-query stack
        //rental contends on the pool lock, which is what this rung watches.
        await RunThreadScalingAsync(quick).ConfigureAwait(false);

        EvaluateDefaultRule(readings);

        if(digestFailure)
        {
            Console.WriteLine("[box-index] RESULT: FAIL - candidate-set digests diverged across configurations.");

            return;
        }

        Console.WriteLine("[box-index] RESULT: OK - every configuration answered identical candidate sets on every workload.");
    }

    /// <summary>Measures one (workload, packing, capacity) cell: build and query wall-clock over the repeats, precise allocation per query, and the candidate digest.</summary>
    /// <param name="workload">The workload name.</param>
    /// <param name="packing">The packing family.</param>
    /// <param name="capacity">The node capacity.</param>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <param name="repeats">The repeat count; the decision default unless the caller caps it.</param>
    /// <returns>The cell reading.</returns>
    private static CellReading MeasureCell(string workload, BoxIndexPacking packing, int capacity, BoundingBox[] items, BoundingBox[] probes, int repeats = DecisionRepeats)
    {
        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

        var buildTimes = new double[repeats];
        var materializeTimes = new double[repeats];
        var queryTimes = new double[repeats];
        long allocatedPerQuery = 0L;
        string digest = string.Empty;

        for(int repeat = 0; repeat < repeats; repeat++)
        {
            var buildWatch = Stopwatch.StartNew();

            if(!index.TryBuild(items))
            {
                throw new InvalidOperationException($"The {workload} workload must build.");
            }

            buildWatch.Stop();
            buildTimes[repeat] = buildWatch.Elapsed.TotalMilliseconds;

            //The dominance materialization is timed as its own step, after the build watch
            //and before the per-query allocation bracket opens: build rows stay
            //TryBuild-only and query rows stay steady-state, comparable across carriages
            //and to prior runs.
            var materializeWatch = Stopwatch.StartNew();
            index.EnsureDominanceMaterialized();
            materializeWatch.Stop();
            materializeTimes[repeat] = materializeWatch.Elapsed.TotalMilliseconds;

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var queryWatch = Stopwatch.StartNew();
            long candidateTotal = 0L;

            for(int probe = 0; probe < probes.Length; probe++)
            {
                foreach(int candidate in SelectMode(index, probe % 3, probes[probe]))
                {
                    candidateTotal += candidate;
                }
            }

            queryWatch.Stop();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            queryTimes[repeat] = queryWatch.Elapsed.TotalMilliseconds;
            allocatedPerQuery = (allocatedAfter - allocatedBefore) / probes.Length;

            if(repeat == 0)
            {
                digest = ComputeCandidateDigest(index, probes);
            }

            _ = candidateTotal;
        }

        Array.Sort(buildTimes);
        Array.Sort(materializeTimes);
        Array.Sort(queryTimes);

        var reading = new CellReading(
            workload, packing, capacity,
            Median(buildTimes), buildTimes[0],
            Median(materializeTimes), materializeTimes[0],
            Median(queryTimes), queryTimes[0],
            allocatedPerQuery, digest);

        Console.WriteLine(
            $"  {workload,-42} {packing,-18} cap {capacity,3}: build med {reading.BuildMedianMs,9:F2} ms (min {reading.BuildMinMs,9:F2}), " +
            $"mat med {reading.MaterializeMedianMs,8:F2} ms, " +
            $"query med {reading.QueryMedianMs,8:F2} ms (min {reading.QueryMinMs,8:F2}), {reading.AllocatedBytesPerQuery,5} B/query");

        return reading;
    }

    /// <summary>The join-cadence rung: build-and-query cycles at tiny counts beside the brute-force scan the crossover is read against.</summary>
    /// <param name="capacities">The capacity sweep of the running protocol.</param>
    /// <param name="quick">Whether the quick protocol is running.</param>
    private static void RunJoinCadence(int[] capacities, bool quick)
    {
        Console.WriteLine("[box-index] join-cadence rung (build + 64 three-mode queries per cycle; scan = the brute-force baseline the crossover is read against):");

        int[] joinCapacities = quick ? [16] : [4, 16, 64];

        foreach(int itemCount in new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 })
        {
            BoundingBox[] items = BuildShape("clustered", itemCount);
            BoundingBox[] probes = BuildProbes(items, 64);

            double scanMs = MeasureScanBaseline(items, probes);

            foreach(int capacity in joinCapacities)
            {
                foreach(BoxIndexPacking packing in Packings)
                {
                    using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(packing, capacity));

                    var cycleTimes = new double[DecisionRepeats];

                    for(int repeat = 0; repeat < DecisionRepeats; repeat++)
                    {
                        var watch = Stopwatch.StartNew();

                        if(!index.TryBuild(items))
                        {
                            throw new InvalidOperationException("The join-cadence workload must build.");
                        }

                        long sink = 0L;

                        for(int probe = 0; probe < probes.Length; probe++)
                        {
                            foreach(int candidate in SelectMode(index, probe % 3, probes[probe]))
                            {
                                sink += candidate;
                            }
                        }

                        watch.Stop();
                        cycleTimes[repeat] = watch.Elapsed.TotalMilliseconds;
                        _ = sink;
                    }

                    Array.Sort(cycleTimes);
                    Console.WriteLine($"  N={itemCount,5} {packing,-18} cap {capacity,3}: cycle med {Median(cycleTimes) * 1000d,9:F1} us (min {cycleTimes[0] * 1000d,9:F1}), scan {scanMs * 1000d,9:F1} us");
                }
            }

            TrimSharedPools();
        }
    }

    /// <summary>The brute-force scan baseline of one join-cadence rung.</summary>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The median scan time in milliseconds.</returns>
    private static double MeasureScanBaseline(BoundingBox[] items, BoundingBox[] probes)
    {
        var times = new double[DecisionRepeats];

        for(int repeat = 0; repeat < DecisionRepeats; repeat++)
        {
            var watch = Stopwatch.StartNew();
            long sink = 0L;

            for(int probe = 0; probe < probes.Length; probe++)
            {
                BoundingBox query = probes[probe];

                for(int item = 0; item < items.Length; item++)
                {
                    bool hit = (probe % 3) switch
                    {
                        0 => query.Intersects(items[item]),
                        1 => query.Contains(items[item]),
                        _ => items[item].Contains(query)
                    };

                    if(hit)
                    {
                        sink += item;
                    }
                }
            }

            watch.Stop();
            times[repeat] = watch.Elapsed.TotalMilliseconds;
            _ = sink;
        }

        Array.Sort(times);

        return Median(times);
    }

    /// <summary>The thread-scaling rung: query throughput over one built index across a worker ladder.</summary>
    /// <param name="quick">Whether the quick protocol is running.</param>
    /// <returns>The task the rung completes on.</returns>
    private static async Task RunThreadScalingAsync(bool quick)
    {
        Console.WriteLine("[box-index] thread-scaling rung (query throughput over one built index; the per-query rental serializes on the pool lock):");

        BoundingBox[] items = BuildShape("uniform", quick ? 100_000L : 1_000_000L);
        BoundingBox[] probes = BuildProbes(items, 100_000);

        using PackedBoxIndex index = PackedBoxIndex.Create(new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16));

        if(!index.TryBuild(items))
        {
            throw new InvalidOperationException("The thread-scaling workload must build.");
        }

        //The rung measures steady-state throughput, so the dominance structure materializes
        //before the timed region; the alternative would measure the first worker paying the
        //pass while the others block on the lock, not the queries.
        index.EnsureDominanceMaterialized();

        foreach(int parallelism in new[] { 1, 2, 4, 8 })
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var watch = Stopwatch.StartNew();
            var workers = new Task[parallelism];

            for(int worker = 0; worker < parallelism; worker++)
            {
                workers[worker] = Task.Factory.StartNew(
                    RunProbeSlice,
                    new ProbeSlice(index, probes, worker, parallelism),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);

            watch.Stop();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            double queriesPerSecond = probes.Length / watch.Elapsed.TotalSeconds;

            Console.WriteLine($"  P={parallelism}: {queriesPerSecond,12:N0} queries/s, {(allocatedAfter - allocatedBefore) / probes.Length,5} B/query");
        }
    }

    /// <summary>One worker's strided share of the thread-scaling sweep; bound as a static callback so nothing closes over the rung body.</summary>
    /// <param name="state">The worker's <see cref="ProbeSlice"/>.</param>
    private static void RunProbeSlice(object? state)
    {
        var slice = (ProbeSlice)state!;
        long sink = 0L;

        for(int probe = slice.First; probe < slice.Probes.Length; probe += slice.Stride)
        {
            foreach(int candidate in SelectMode(slice.Index, probe % 3, slice.Probes[probe]))
            {
                sink += candidate;
            }
        }

        _ = sink;
    }

    /// <summary>
    /// The pre-registered default rule over the primary cells: geometric mean
    /// of per-cell query medians per configuration, noise band from the
    /// median-minus-min spread, build-cost median as the within-band
    /// tie-break, the zero-member packing as the residual convention; plus the
    /// two-times falsification trigger.
    /// </summary>
    /// <param name="readings">Every cell reading of the run.</param>
    private static void EvaluateDefaultRule(List<CellReading> readings)
    {
        Console.WriteLine("[box-index] default rule over the primary cells (uniform + clustered at N=100000 and N=1000000, query medians):");

        var primary = new List<CellReading>();

        foreach(CellReading reading in readings)
        {
            bool primaryShape = reading.Workload.StartsWith("uniform/", StringComparison.Ordinal)
                || reading.Workload.StartsWith("clustered/", StringComparison.Ordinal);
            bool primarySize = reading.Workload.EndsWith("N=100000", StringComparison.Ordinal)
                || reading.Workload.EndsWith("N=1000000", StringComparison.Ordinal);

            if(primaryShape && primarySize)
            {
                primary.Add(reading);
            }
        }

        if(primary.Count == 0)
        {
            Console.WriteLine("  No primary cells in this run (quick mode measures below the primary sizes); the default decision needs the full protocol.");

            return;
        }

        var keys = new List<(BoxIndexPacking Packing, int Capacity)>();
        var logSums = new List<double>();
        var noiseSums = new List<double>();
        var buildSums = new List<double>();
        var cellCounts = new List<int>();

        foreach(CellReading cell in primary)
        {
            int slot = -1;

            for(int candidate = 0; candidate < keys.Count; candidate++)
            {
                if(keys[candidate].Packing == cell.Packing && keys[candidate].Capacity == cell.Capacity)
                {
                    slot = candidate;

                    break;
                }
            }

            if(slot < 0)
            {
                keys.Add((cell.Packing, cell.Capacity));
                logSums.Add(0d);
                noiseSums.Add(0d);
                buildSums.Add(0d);
                cellCounts.Add(0);
                slot = keys.Count - 1;
            }

            logSums[slot] += Math.Log(Math.Max(cell.QueryMedianMs, 1e-6));
            noiseSums[slot] += cell.QueryMedianMs - cell.QueryMinMs;
            buildSums[slot] += cell.BuildMedianMs;
            cellCounts[slot]++;
        }

        var aggregates = new List<ConfigurationAggregate>();

        for(int slot = 0; slot < keys.Count; slot++)
        {
            aggregates.Add(new ConfigurationAggregate(
                keys[slot].Packing,
                keys[slot].Capacity,
                Math.Exp(logSums[slot] / cellCounts[slot]),
                noiseSums[slot],
                buildSums[slot] / cellCounts[slot]));
        }

        aggregates.Sort(static (left, right) => left.GeometricMeanMs.CompareTo(right.GeometricMeanMs));

        foreach(ConfigurationAggregate aggregate in aggregates)
        {
            Console.WriteLine($"  {aggregate.Packing,-18} cap {aggregate.Capacity,3}: geo-mean {aggregate.GeometricMeanMs,8:F3} ms, noise band {aggregate.NoiseBandMs,7:F3} ms, build med {aggregate.BuildMedianMs,9:F2} ms");
        }

        ConfigurationAggregate best = aggregates[0];
        ConfigurationAggregate? withinBand = null;
        int cellsPerConfiguration = Math.Max(1, primary.Count / aggregates.Count);

        foreach(ConfigurationAggregate aggregate in aggregates)
        {
            double band = Math.Max(best.NoiseBandMs, aggregate.NoiseBandMs) / cellsPerConfiguration;

            if(aggregate.GeometricMeanMs - best.GeometricMeanMs > band)
            {
                continue;
            }

            bool cheaperBuild = withinBand is null || aggregate.BuildMedianMs < withinBand.BuildMedianMs;
            bool sameBuildLowerPacking = withinBand is not null
                && aggregate.BuildMedianMs.Equals(withinBand.BuildMedianMs)
                && aggregate.Packing < withinBand.Packing;

            if(cheaperBuild || sameBuildLowerPacking)
            {
                withinBand = aggregate;
            }
        }

        ConfigurationAggregate pick = withinBand ?? best;

        Console.WriteLine($"  PICK: {pick.Packing} at capacity {pick.Capacity} (ties within the noise band broke by build cost, then by the zero-member convention).");

        //The two-times falsification trigger: a packing losing every primary cell by more
        //than a factor of two is retired from default candidacy (it stays selectable).
        foreach(BoxIndexPacking packing in Packings)
        {
            if(LosesEveryPrimaryCellByTwice(primary, packing))
            {
                Console.WriteLine($"  TRIGGER: {packing} loses every primary cell by more than 2x and is retired from default candidacy (stays selectable).");
            }
        }
    }

    /// <summary>Whether one packing's best reading loses every primary workload by more than a factor of two.</summary>
    /// <param name="primary">The primary cell readings.</param>
    /// <param name="packing">The packing family under test.</param>
    /// <returns><see langword="true"/> when the trigger fires.</returns>
    private static bool LosesEveryPrimaryCellByTwice(List<CellReading> primary, BoxIndexPacking packing)
    {
        var workloads = new List<string>();

        foreach(CellReading cell in primary)
        {
            if(!workloads.Contains(cell.Workload))
            {
                workloads.Add(cell.Workload);
            }
        }

        foreach(string workload in workloads)
        {
            double own = double.PositiveInfinity;
            double other = double.PositiveInfinity;

            foreach(CellReading cell in primary)
            {
                if(!string.Equals(cell.Workload, workload, StringComparison.Ordinal))
                {
                    continue;
                }

                if(cell.Packing == packing)
                {
                    own = Math.Min(own, cell.QueryMedianMs);
                }
                else
                {
                    other = Math.Min(other, cell.QueryMedianMs);
                }
            }

            if(own <= 2d * other)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The alternated combination order: packings interleave inside each capacity so neither family systematically runs on a warmer machine, and the full list runs twice.</summary>
    /// <param name="capacities">The capacity sweep.</param>
    /// <returns>The (packing, capacity) combinations in measurement order.</returns>
    private static IEnumerable<(BoxIndexPacking Packing, int Capacity)> AlternatedCombinations(int[] capacities)
    {
        for(int round = 0; round < 2; round++)
        {
            foreach(int capacity in capacities)
            {
                if(round == 0)
                {
                    yield return (BoxIndexPacking.SortTileRecursive, capacity);
                    yield return (BoxIndexPacking.HilbertCurve, capacity);
                }
                else
                {
                    yield return (BoxIndexPacking.HilbertCurve, capacity);
                    yield return (BoxIndexPacking.SortTileRecursive, capacity);
                }
            }
        }
    }

    /// <summary>Selects one query mode by ordinal.</summary>
    /// <param name="index">The index to query.</param>
    /// <param name="mode">The mode ordinal: 0 intersecting, 1 contained-in, 2 containing.</param>
    /// <param name="probe">The query box.</param>
    /// <returns>The candidate view.</returns>
    private static PackedBoxIndex.Candidates SelectMode(PackedBoxIndex index, int mode, in BoundingBox probe)
    {
        return mode switch
        {
            0 => index.Intersecting(in probe),
            1 => index.ContainedIn(in probe),
            _ => index.Containing(in probe)
        };
    }

    /// <summary>Per probe, the sorted candidate set hashes once; the per-probe hashes chain in fixed probe order into the combination digest — order-free within a query, order-fixed across queries.</summary>
    /// <param name="index">The built index.</param>
    /// <param name="probes">The probe set, in digest order.</param>
    /// <returns>The digest as upper-case hexadecimal.</returns>
    private static string ComputeCandidateDigest(PackedBoxIndex index, BoundingBox[] probes)
    {
        using IncrementalHash chained = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash perQuery = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var candidates = new List<int>();
        Span<byte> intBytes = stackalloc byte[sizeof(int)];
        Span<byte> queryHash = stackalloc byte[32];

        foreach(BoundingBox probe in probes)
        {
            for(int mode = 0; mode < 3; mode++)
            {
                candidates.Clear();

                foreach(int candidate in SelectMode(index, mode, probe))
                {
                    candidates.Add(candidate);
                }

                candidates.Sort();

                foreach(int candidate in candidates)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(intBytes, candidate);
                    perQuery.AppendData(intBytes);
                }

                _ = perQuery.GetHashAndReset(queryHash);
                chained.AppendData(queryHash);
            }
        }

        return Convert.ToHexString(chained.GetHashAndReset());
    }

    /// <summary>The explicit state one thread-scaling worker receives: the shared index and probe set plus the strided probe range this worker walks.</summary>
    private sealed class ProbeSlice
    {
        /// <summary>The index every worker queries concurrently.</summary>
        public PackedBoxIndex Index { get; }

        /// <summary>The shared probe set.</summary>
        public BoundingBox[] Probes { get; }

        /// <summary>This worker's first probe index.</summary>
        public int First { get; }

        /// <summary>The stride between this worker's probe indices.</summary>
        public int Stride { get; }

        /// <summary>Captures the worker's share of the sweep.</summary>
        /// <param name="index">The index every worker queries concurrently.</param>
        /// <param name="probes">The shared probe set.</param>
        /// <param name="first">This worker's first probe index.</param>
        /// <param name="stride">The stride between this worker's probe indices.</param>
        public ProbeSlice(PackedBoxIndex index, BoundingBox[] probes, int first, int stride)
        {
            Index = index;
            Probes = probes;
            First = first;
            Stride = stride;
        }
    }
}
