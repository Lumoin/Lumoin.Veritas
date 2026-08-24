using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using Lumoin.Veritas.Geo.Spatial;

using static Lumoin.Veritas.Benchmarks.BoxSoakWorkloads;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The containment head-to-head (<c>--profile-box-containment</c>): the two
/// shipped containment paths — the dominance tree's
/// <see cref="BoxContainmentIndex.Containers"/> and the packed index's
/// <see cref="PackedBoxIndex.Containing"/> at the shipped default plus one
/// alternate configuration — against the brute-force scan baseline, under the
/// house measurement protocol: cyclically rotated path order over four rounds
/// (every path occupies every measurement position exactly once), five
/// repeats per decision cell reporting min and median, wall-clock plus
/// precise allocated bytes, shared-pool trims between combinations, and a
/// cross-path candidate-set digest gate — a digest divergence fails the run
/// before the join-cadence rung or the default rule sees any number. The run
/// ends by evaluating the pre-registered default rule over the primary cells
/// and printing the pick, the per-shape winners, and the join-cadence
/// crossovers, whose cycle order reverses on alternating counts.
/// </summary>
internal static class BoxContainmentHeadToHeadSoak
{
    /// <summary>Repeats per decision cell; the reading reports the median and the min.</summary>
    private const int DecisionRepeats = 5;

    /// <summary>Queries per measured cell.</summary>
    private const int QueriesPerCell = 1_000;

    /// <summary>The alternate packed configuration: the packing the small-count rebuild-per-query regime measured faster, carried so the structure verdict is not an artifact of one packed configuration.</summary>
    private static readonly PackedBoxIndexOptions AlternatePackedOptions = new(BoxIndexPacking.SortTileRecursive, 16);

    /// <summary>The workload shapes; each shape's rationale is pre-registered in the stand record.</summary>
    private static readonly string[] Shapes = ["uniform", "clustered", "archipelago", "nested", "blanket"];

    /// <summary>The paths on the stand, in round one's order; later rounds rotate the start position.</summary>
    private static readonly ContainmentPath[] Paths =
    [
        ContainmentPath.DominanceKdTree,
        ContainmentPath.PackedHilbertDefault,
        ContainmentPath.PackedSortTileAlternate,
        ContainmentPath.BruteScan
    ];

    /// <summary>One containment path on the stand.</summary>
    private enum ContainmentPath
    {
        /// <summary>The dominance k-d tree's <see cref="BoxContainmentIndex.Containers"/> walk.</summary>
        DominanceKdTree,

        /// <summary>The packed index's <see cref="PackedBoxIndex.Containing"/> walk at the shipped default options.</summary>
        PackedHilbertDefault,

        /// <summary>The packed index's <see cref="PackedBoxIndex.Containing"/> walk at the alternate Sort-Tile-Recursive options.</summary>
        PackedSortTileAlternate,

        /// <summary>The brute-force scan over the item array — the anchor baseline and the digest gate's ground truth.</summary>
        BruteScan
    }

    /// <summary>One measured cell: the repeat build, materialize, and query readings of one path over one workload.</summary>
    /// <param name="Workload">The workload name.</param>
    /// <param name="Path">The containment path.</param>
    /// <param name="BuildMedianMs">The median build wall-clock in milliseconds; zero on the scan path, which builds nothing.</param>
    /// <param name="BuildMinMs">The minimum build wall-clock in milliseconds.</param>
    /// <param name="MaterializeMedianMs">The median dominance-materialization wall-clock in milliseconds on the packed paths, timed as its own step between the build and the query sweep; zero on the other paths.</param>
    /// <param name="QueryMedianMs">The median query-sweep wall-clock in milliseconds.</param>
    /// <param name="QueryMinMs">The minimum query-sweep wall-clock in milliseconds.</param>
    /// <param name="AllocatedBytesPerQuery">The precise allocation per query in bytes.</param>
    /// <param name="Digest">The candidate-set digest of the cell.</param>
    private sealed record CellReading(
        string Workload,
        ContainmentPath Path,
        double BuildMedianMs,
        double BuildMinMs,
        double MaterializeMedianMs,
        double QueryMedianMs,
        double QueryMinMs,
        long AllocatedBytesPerQuery,
        string Digest);

    /// <summary>One path's aggregate over the primary cells.</summary>
    /// <param name="Path">The containment path.</param>
    /// <param name="GeometricMeanMs">The geometric mean of the per-cell query medians.</param>
    /// <param name="NoiseBandMs">The summed median-minus-min spread across the cells.</param>
    /// <param name="BuildMedianMs">The mean build median across the cells.</param>
    /// <param name="CellCount">The number of primary cells this path aggregated — the path's own noise-band divisor.</param>
    private sealed record PathAggregate(
        ContainmentPath Path,
        double GeometricMeanMs,
        double NoiseBandMs,
        double BuildMedianMs,
        int CellCount);

    /// <summary>Runs the head-to-head; <c>--quick</c> selects the smoke protocol.</summary>
    /// <param name="args">The process arguments.</param>
    public static void RunBoxContainmentHeadToHead(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        long[] datasetSizes = quick ? [1_000L, 10_000L, 100_000L] : [1_000L, 10_000L, 100_000L, 1_000_000L];

        Console.WriteLine($"[box-containment] head-to-head: {(quick ? "quick" : "full")} protocol - {Paths.Length} paths x {Shapes.Length} shapes, {DecisionRepeats} repeats per decision cell, {Paths.Length} rotated rounds, digest gate on.");

        var readings = new List<CellReading>();
        bool digestFailure = false;

        //Dataset-scale rung: per (shape, N) the digest must agree across every path.
        foreach(string shape in Shapes)
        {
            foreach(long itemCount in datasetSizes)
            {
                BoundingBox[] items = BuildShape(shape, itemCount);
                BoundingBox[] probes = BuildContainmentProbes(items, QueriesPerCell);
                string? sharedDigest = null;

                foreach(ContainmentPath path in AlternatedPaths())
                {
                    CellReading reading = MeasureCell($"{shape}/N={itemCount}", path, items, probes);
                    readings.Add(reading);

                    if(sharedDigest is null)
                    {
                        sharedDigest = reading.Digest;
                    }
                    else if(!string.Equals(sharedDigest, reading.Digest, StringComparison.Ordinal))
                    {
                        digestFailure = true;
                        Console.WriteLine($"  DIGEST MISMATCH at {shape}/N={itemCount} {path}: {reading.Digest} != {sharedDigest}");
                    }

                    TrimSharedPools();
                }

                Console.WriteLine($"{shape}/N={itemCount}: digest {sharedDigest?[..16]} agreed across {Paths.Length * Paths.Length} path runs.");
            }
        }

        //Cross-adversary rung: coincident-centre interleaved slats whose every union is the
        //full field, probed off-arm so every answer set is empty by construction — the
        //conjunctive-emptiness shape the dominance descent's union prune exists for. The
        //digest gate covers these cells too; the cells stay out of the default rule (the
        //rule filters on the primary shapes).
        foreach(long slatCount in datasetSizes)
        {
            var crossItems = new BoundingBox[(int)slatCount];
            CrossSlatFixture.WriteSlats(crossItems, fieldExtent: 1_000_000d, thickness: 2d);
            var crossProbes = new BoundingBox[300];
            CrossSlatFixture.WriteOffArmProbes(crossProbes, fieldExtent: 1_000_000d);
            string? crossDigest = null;

            foreach(ContainmentPath path in AlternatedPaths())
            {
                CellReading reading = MeasureCell($"cross/N={slatCount}", path, crossItems, crossProbes);
                readings.Add(reading);

                if(crossDigest is null)
                {
                    crossDigest = reading.Digest;
                }
                else if(!string.Equals(crossDigest, reading.Digest, StringComparison.Ordinal))
                {
                    digestFailure = true;
                    Console.WriteLine($"  DIGEST MISMATCH at cross/N={slatCount} {path}: {reading.Digest} != {crossDigest}");
                }

                TrimSharedPools();
            }

            Console.WriteLine($"cross/N={slatCount}: digest {crossDigest?[..16]} agreed across every path run.");
        }

        //The digest gate closes BEFORE any number counts: a divergence means the paths
        //disagree on the answer set, so neither the join-cadence rung nor the default
        //rule runs over the readings.
        if(digestFailure)
        {
            Console.WriteLine("[box-containment] RESULT: FAIL - candidate-set digests diverged across paths; no rule is evaluated over the readings.");

            return;
        }

        //Join-cadence rung: build + query cycles at tiny counts; the crossover against the
        //brute-force cycle is a named deliverable of the stand.
        RunJoinCadence();

        EvaluateDefaultRule(readings, datasetSizes);

        RunCarriageComparison(datasetSizes);

        Console.WriteLine("[box-containment] RESULT: OK - every path answered identical container sets on every workload.");
    }

    /// <summary>
    /// The carriage comparison at the shipped default configuration: the same
    /// primary-shape cells under the deferred and the eager dominance
    /// carriage, plus a rebuild-and-query cycle ladder. The one-time pass
    /// relocates between the build and materialize columns while the
    /// steady-state query readings coincide — the observable the carriage
    /// choice is judged on: the eager option is taken only if a measured
    /// house workload improves under it.
    /// </summary>
    /// <param name="datasetSizes">The running protocol's dataset sizes; the largest is the primary cell size.</param>
    private static void RunCarriageComparison(long[] datasetSizes)
    {
        Console.WriteLine("[box-containment] carriage rung (deferred versus eager at the shipped default; the pass relocates between the build and materialize columns):");

        DominanceMaterializationMode[] carriages = [DominanceMaterializationMode.DeferredToFirstUse, DominanceMaterializationMode.EagerAtBuild];
        long primarySize = datasetSizes[^1];

        foreach(string shape in new[] { "uniform", "clustered" })
        {
            BoundingBox[] items = BuildShape(shape, primarySize);
            BoundingBox[] probes = BuildContainmentProbes(items, QueriesPerCell);

            foreach(DominanceMaterializationMode carriage in carriages)
            {
                PackedBoxIndexOptions options = PackedBoxIndexOptions.Default with { DominanceMaterialization = carriage };
                _ = MeasurePackedCell($"{shape}/N={primarySize} {carriage}", ContainmentPath.PackedHilbertDefault, options, items, probes);
                TrimSharedPools();
            }
        }

        //The per-epoch ladder: whole build-plus-64-containment-queries cycles, where both
        //carriages pay the pass inside the cycle — the honest per-epoch total.
        foreach(int itemCount in new[] { 64, 256, 1024, 4096 })
        {
            BoundingBox[] items = BuildShape("clustered", itemCount);
            BoundingBox[] probes = BuildContainmentProbes(items, 64);

            foreach(DominanceMaterializationMode carriage in carriages)
            {
                PackedBoxIndexOptions options = PackedBoxIndexOptions.Default with { DominanceMaterialization = carriage };
                using PackedBoxIndex index = PackedBoxIndex.Create(options);

                var cycleTimes = new double[DecisionRepeats];

                for(int repeat = 0; repeat < DecisionRepeats; repeat++)
                {
                    var watch = Stopwatch.StartNew();

                    if(!index.TryBuild(items))
                    {
                        throw new InvalidOperationException("The carriage-rung workload must build.");
                    }

                    long sink = 0L;

                    for(int probe = 0; probe < probes.Length; probe++)
                    {
                        foreach(int candidate in index.Containing(in probes[probe]))
                        {
                            sink += candidate;
                        }
                    }

                    watch.Stop();
                    cycleTimes[repeat] = watch.Elapsed.TotalMilliseconds;
                    _ = sink;
                }

                Array.Sort(cycleTimes);
                Console.WriteLine($"  N={itemCount,5} {carriage,-20}: cycle med {Median(cycleTimes) * 1000d,9:F1} us (min {cycleTimes[0] * 1000d,9:F1})");
            }

            TrimSharedPools();
        }
    }

    /// <summary>Measures one (workload, path) cell by dispatching to the path's own measurer.</summary>
    /// <param name="workload">The workload name.</param>
    /// <param name="path">The containment path.</param>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The cell reading.</returns>
    private static CellReading MeasureCell(string workload, ContainmentPath path, BoundingBox[] items, BoundingBox[] probes)
    {
        return path switch
        {
            ContainmentPath.DominanceKdTree => MeasureDominanceCell(workload, items, probes),
            ContainmentPath.PackedHilbertDefault => MeasurePackedCell(workload, ContainmentPath.PackedHilbertDefault, PackedBoxIndexOptions.Default, items, probes),
            ContainmentPath.PackedSortTileAlternate => MeasurePackedCell(workload, ContainmentPath.PackedSortTileAlternate, AlternatePackedOptions, items, probes),
            ContainmentPath.BruteScan => MeasureScanCell(workload, items, probes),
            _ => throw new InvalidOperationException($"The containment path {path} has no measurer.")
        };
    }

    /// <summary>Measures the dominance-tree cell: build and containment-query wall-clock over the repeats, precise allocation per query, and the candidate digest.</summary>
    /// <param name="workload">The workload name.</param>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The cell reading.</returns>
    private static CellReading MeasureDominanceCell(string workload, BoundingBox[] items, BoundingBox[] probes)
    {
        using BoxContainmentIndex index = BoxContainmentIndex.Create();

        var buildTimes = new double[DecisionRepeats];
        var queryTimes = new double[DecisionRepeats];
        long allocatedPerQuery = 0L;
        string digest = string.Empty;

        for(int repeat = 0; repeat < DecisionRepeats; repeat++)
        {
            var buildWatch = Stopwatch.StartNew();
            index.Build(items);
            buildWatch.Stop();
            buildTimes[repeat] = buildWatch.Elapsed.TotalMilliseconds;

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var queryWatch = Stopwatch.StartNew();
            long candidateTotal = 0L;

            for(int probe = 0; probe < probes.Length; probe++)
            {
                foreach(int candidate in index.Containers(in probes[probe]))
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
                digest = ComputeDominanceDigest(index, probes);
            }

            _ = candidateTotal;
        }

        return FinishCell(workload, ContainmentPath.DominanceKdTree, buildTimes, materializeTimes: new double[DecisionRepeats], queryTimes, allocatedPerQuery, digest);
    }

    /// <summary>Measures one packed-index cell: build and containment-query wall-clock over the repeats, precise allocation per query, and the candidate digest.</summary>
    /// <param name="workload">The workload name.</param>
    /// <param name="path">The packed path being measured, which names the configuration in the reading.</param>
    /// <param name="options">The packed configuration.</param>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The cell reading.</returns>
    private static CellReading MeasurePackedCell(string workload, ContainmentPath path, PackedBoxIndexOptions options, BoundingBox[] items, BoundingBox[] probes)
    {
        using PackedBoxIndex index = PackedBoxIndex.Create(options);

        var buildTimes = new double[DecisionRepeats];
        var materializeTimes = new double[DecisionRepeats];
        var queryTimes = new double[DecisionRepeats];
        long allocatedPerQuery = 0L;
        string digest = string.Empty;

        for(int repeat = 0; repeat < DecisionRepeats; repeat++)
        {
            var buildWatch = Stopwatch.StartNew();

            if(!index.TryBuild(items))
            {
                throw new InvalidOperationException($"The {workload} workload must build.");
            }

            buildWatch.Stop();
            buildTimes[repeat] = buildWatch.Elapsed.TotalMilliseconds;

            //The dominance materialization is timed as its own step so the build rows stay
            //TryBuild-only and the query rows stay steady-state, comparable to the other
            //paths and to prior runs of this stand.
            var materializeWatch = Stopwatch.StartNew();
            index.EnsureDominanceMaterialized();
            materializeWatch.Stop();
            materializeTimes[repeat] = materializeWatch.Elapsed.TotalMilliseconds;

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var queryWatch = Stopwatch.StartNew();
            long candidateTotal = 0L;

            for(int probe = 0; probe < probes.Length; probe++)
            {
                foreach(int candidate in index.Containing(in probes[probe]))
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
                digest = ComputePackedDigest(index, probes);
            }

            _ = candidateTotal;
        }

        return FinishCell(workload, path, buildTimes, materializeTimes, queryTimes, allocatedPerQuery, digest);
    }

    /// <summary>Measures the brute-scan cell: containment-query wall-clock over the repeats with no build, precise allocation per query, and the candidate digest.</summary>
    /// <param name="workload">The workload name.</param>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The cell reading.</returns>
    private static CellReading MeasureScanCell(string workload, BoundingBox[] items, BoundingBox[] probes)
    {
        var buildTimes = new double[DecisionRepeats];
        var queryTimes = new double[DecisionRepeats];
        long allocatedPerQuery = 0L;
        string digest = string.Empty;

        for(int repeat = 0; repeat < DecisionRepeats; repeat++)
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var queryWatch = Stopwatch.StartNew();
            long candidateTotal = 0L;

            for(int probe = 0; probe < probes.Length; probe++)
            {
                BoundingBox query = probes[probe];

                for(int item = 0; item < items.Length; item++)
                {
                    if(items[item].Contains(query))
                    {
                        candidateTotal += item;
                    }
                }
            }

            queryWatch.Stop();
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            queryTimes[repeat] = queryWatch.Elapsed.TotalMilliseconds;
            allocatedPerQuery = (allocatedAfter - allocatedBefore) / probes.Length;

            if(repeat == 0)
            {
                digest = ComputeScanDigest(items, probes);
            }

            _ = candidateTotal;
        }

        return FinishCell(workload, ContainmentPath.BruteScan, buildTimes, materializeTimes: new double[DecisionRepeats], queryTimes, allocatedPerQuery, digest);
    }

    /// <summary>Sorts the repeat readings, assembles the cell record, and prints the cell line.</summary>
    /// <param name="workload">The workload name.</param>
    /// <param name="path">The containment path.</param>
    /// <param name="buildTimes">The per-repeat build times, unsorted.</param>
    /// <param name="materializeTimes">The per-repeat dominance-materialization times, unsorted; all-zero on the paths that carry none.</param>
    /// <param name="queryTimes">The per-repeat query times, unsorted.</param>
    /// <param name="allocatedPerQuery">The precise allocation per query in bytes.</param>
    /// <param name="digest">The candidate-set digest.</param>
    /// <returns>The cell reading.</returns>
    private static CellReading FinishCell(string workload, ContainmentPath path, double[] buildTimes, double[] materializeTimes, double[] queryTimes, long allocatedPerQuery, string digest)
    {
        Array.Sort(buildTimes);
        Array.Sort(materializeTimes);
        Array.Sort(queryTimes);

        var reading = new CellReading(
            workload, path,
            Median(buildTimes), buildTimes[0],
            Median(materializeTimes),
            Median(queryTimes), queryTimes[0],
            allocatedPerQuery, digest);

        Console.WriteLine(
            $"  {workload,-24} {path,-24}: build med {reading.BuildMedianMs,9:F2} ms (min {reading.BuildMinMs,9:F2}), " +
            $"mat med {reading.MaterializeMedianMs,8:F2} ms, " +
            $"query med {reading.QueryMedianMs,8:F2} ms (min {reading.QueryMinMs,8:F2}), {reading.AllocatedBytesPerQuery,5} B/query");

        return reading;
    }

    /// <summary>The join-cadence rung: build-and-query cycles at tiny counts for every indexed path beside the brute-force cycle the crossovers are read against. The measurement order reverses on alternating counts so no cycle is always measured in the same relative position.</summary>
    private static void RunJoinCadence()
    {
        Console.WriteLine("[box-containment] join-cadence rung (build + 64 containment queries per cycle; scan = the brute-force cycle the crossovers are read against):");

        int ladderPosition = 0;

        foreach(int itemCount in new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 })
        {
            BoundingBox[] items = BuildShape("clustered", itemCount);
            BoundingBox[] probes = BuildContainmentProbes(items, 64);

            double scanMs;
            double dominanceMs;
            double packedDefaultMs;
            double packedAlternateMs;

            if(ladderPosition % 2 == 0)
            {
                scanMs = MeasureScanCycle(items, probes);
                dominanceMs = MeasureDominanceCycle(items, probes);
                packedDefaultMs = MeasurePackedCycle(PackedBoxIndexOptions.Default, items, probes);
                packedAlternateMs = MeasurePackedCycle(AlternatePackedOptions, items, probes);
            }
            else
            {
                packedAlternateMs = MeasurePackedCycle(AlternatePackedOptions, items, probes);
                packedDefaultMs = MeasurePackedCycle(PackedBoxIndexOptions.Default, items, probes);
                dominanceMs = MeasureDominanceCycle(items, probes);
                scanMs = MeasureScanCycle(items, probes);
            }

            Console.WriteLine(
                $"  N={itemCount,5}: dominance {dominanceMs * 1000d,9:F1} us, packed default {packedDefaultMs * 1000d,9:F1} us, " +
                $"packed alternate {packedAlternateMs * 1000d,9:F1} us, scan {scanMs * 1000d,9:F1} us");

            TrimSharedPools();
            ladderPosition++;
        }
    }

    /// <summary>The brute-force cycle of one join-cadence count: 64 containment queries, no build.</summary>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The median cycle time in milliseconds.</returns>
    private static double MeasureScanCycle(BoundingBox[] items, BoundingBox[] probes)
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
                    if(items[item].Contains(query))
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

    /// <summary>The dominance-tree cycle of one join-cadence count: rebuild plus 64 containment queries.</summary>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The median cycle time in milliseconds.</returns>
    private static double MeasureDominanceCycle(BoundingBox[] items, BoundingBox[] probes)
    {
        using BoxContainmentIndex index = BoxContainmentIndex.Create();

        var times = new double[DecisionRepeats];

        for(int repeat = 0; repeat < DecisionRepeats; repeat++)
        {
            var watch = Stopwatch.StartNew();
            index.Build(items);

            long sink = 0L;

            for(int probe = 0; probe < probes.Length; probe++)
            {
                foreach(int candidate in index.Containers(in probes[probe]))
                {
                    sink += candidate;
                }
            }

            watch.Stop();
            times[repeat] = watch.Elapsed.TotalMilliseconds;
            _ = sink;
        }

        Array.Sort(times);

        return Median(times);
    }

    /// <summary>The packed-index cycle of one join-cadence count: rebuild plus 64 containment queries.</summary>
    /// <param name="options">The packed configuration.</param>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The workload probes.</param>
    /// <returns>The median cycle time in milliseconds.</returns>
    private static double MeasurePackedCycle(PackedBoxIndexOptions options, BoundingBox[] items, BoundingBox[] probes)
    {
        using PackedBoxIndex index = PackedBoxIndex.Create(options);

        var times = new double[DecisionRepeats];

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
                foreach(int candidate in index.Containing(in probes[probe]))
                {
                    sink += candidate;
                }
            }

            watch.Stop();
            times[repeat] = watch.Elapsed.TotalMilliseconds;
            _ = sink;
        }

        Array.Sort(times);

        return Median(times);
    }

    /// <summary>
    /// The pre-registered default rule over the primary cells: geometric mean
    /// of per-cell containment-query medians per path, noise band from the
    /// median-minus-min spread, build-cost median as the within-band
    /// tie-break, the dedicated containment structure as the residual
    /// convention. Candidacy is gated first: the brute scan is the anchor
    /// baseline and never a default candidate, and the two-times falsification
    /// trigger retires a path BEFORE the pick is computed, so the rule can
    /// never pick what it simultaneously retires. A run that measured only one
    /// of the two primary sizes prints its aggregates and stops — no pick from
    /// a partial primary set. The per-shape winners at the largest primary
    /// size and the split-regime check close the evaluation.
    /// </summary>
    /// <param name="readings">Every cell reading of the run.</param>
    /// <param name="datasetSizes">The dataset sizes the run measured.</param>
    private static void EvaluateDefaultRule(List<CellReading> readings, long[] datasetSizes)
    {
        Console.WriteLine("[box-containment] default rule over the primary cells (every shape at N=100000 and N=1000000, containment-query medians):");

        var primary = new List<CellReading>();
        bool millionTierPresent = false;

        foreach(CellReading reading in readings)
        {
            bool millionTier = reading.Workload.EndsWith("N=1000000", StringComparison.Ordinal);
            millionTierPresent |= millionTier;

            if(millionTier || reading.Workload.EndsWith("N=100000", StringComparison.Ordinal))
            {
                primary.Add(reading);
            }
        }

        if(primary.Count == 0)
        {
            Console.WriteLine("  No primary cells in this run; the default decision needs the full protocol.");

            return;
        }

        var aggregates = ComputeAggregates(primary);

        foreach(PathAggregate aggregate in aggregates)
        {
            Console.WriteLine($"  {aggregate.Path,-24}: geo-mean {aggregate.GeometricMeanMs,8:F3} ms, noise band {aggregate.NoiseBandMs,7:F3} ms, build med {aggregate.BuildMedianMs,9:F2} ms, cells {aggregate.CellCount}");
        }

        if(!millionTierPresent)
        {
            Console.WriteLine("  PARTIAL: only the N=100000 primary tier was measured (quick protocol); no pick is evaluated - the default decision needs the full protocol.");

            return;
        }

        //Candidacy gates run BEFORE the pick: the brute scan is the anchor baseline and
        //never a default candidate, and the two-times falsification trigger retires a
        //path from candidacy (it stays selectable and stays in every diagnostic).
        var candidates = new List<PathAggregate>();

        foreach(PathAggregate aggregate in aggregates)
        {
            if(aggregate.Path == ContainmentPath.BruteScan)
            {
                continue;
            }

            if(LosesEveryPrimaryCellByTwice(primary, aggregate.Path))
            {
                Console.WriteLine($"  TRIGGER: {aggregate.Path} loses every primary cell by more than 2x and is retired from default candidacy (stays selectable).");

                continue;
            }

            candidates.Add(aggregate);
        }

        if(LosesEveryPrimaryCellByTwice(primary, ContainmentPath.BruteScan))
        {
            Console.WriteLine("  TRIGGER: BruteScan loses every primary cell by more than 2x (it is the anchor baseline and was never a default candidate).");
        }

        if(candidates.Count == 0)
        {
            Console.WriteLine("  NO PICK: every candidate path is retired by the falsification trigger; the default is an owner decision.");

            return;
        }

        PathAggregate best = candidates[0];
        PathAggregate? withinBand = null;

        foreach(PathAggregate aggregate in candidates)
        {
            double band = Math.Max(
                best.NoiseBandMs / Math.Max(1, best.CellCount),
                aggregate.NoiseBandMs / Math.Max(1, aggregate.CellCount));

            if(aggregate.GeometricMeanMs - best.GeometricMeanMs > band)
            {
                continue;
            }

            bool cheaperBuild = withinBand is null || aggregate.BuildMedianMs < withinBand.BuildMedianMs;
            bool sameBuildDedicated = withinBand is not null
                && aggregate.BuildMedianMs.Equals(withinBand.BuildMedianMs)
                && aggregate.Path == ContainmentPath.DominanceKdTree;

            if(cheaperBuild || sameBuildDedicated)
            {
                withinBand = aggregate;
            }
        }

        PathAggregate pick = withinBand ?? best;

        Console.WriteLine($"  PICK: {pick.Path} (candidacy gated by the anchor-baseline exclusion and the falsification trigger; ties within the noise band broke by build cost, then by the dedicated-structure convention).");
        Console.WriteLine("  NOTE: the dominance path serves one query at a time per instance (shared traversal stack); the packed path serves concurrent queries (per-query rentals).");

        //Per-shape winners at the largest measured primary size; a pick losing any shape by
        //more than 2x makes the ruling split-regime rather than a single default.
        long largestSize = datasetSizes[^1];
        bool splitRegime = false;

        foreach(string shape in Shapes)
        {
            string workload = $"{shape}/N={largestSize}";
            ContainmentPath winner = pick.Path;
            double winnerMedian = double.PositiveInfinity;
            double pickMedian = double.PositiveInfinity;

            foreach(CellReading cell in primary)
            {
                if(!string.Equals(cell.Workload, workload, StringComparison.Ordinal))
                {
                    continue;
                }

                if(cell.QueryMedianMs < winnerMedian)
                {
                    winnerMedian = cell.QueryMedianMs;
                    winner = cell.Path;
                }

                if(cell.Path == pick.Path)
                {
                    pickMedian = Math.Min(pickMedian, cell.QueryMedianMs);
                }
            }

            if(double.IsPositiveInfinity(winnerMedian))
            {
                continue;
            }

            bool pickLosesTwice = pickMedian > 2d * winnerMedian;
            splitRegime |= pickLosesTwice;
            Console.WriteLine($"  SHAPE {shape,-12} at N={largestSize}: winner {winner,-24} med {winnerMedian,8:F2} ms{(pickLosesTwice ? $" - the pick loses this shape by more than 2x ({pickMedian:F2} ms)" : string.Empty)}");
        }

        if(splitRegime)
        {
            Console.WriteLine("  SPLIT-REGIME: the aggregate pick loses at least one shape by more than 2x; the ruling is per-regime defaults, not a single default.");
        }
    }

    /// <summary>Aggregates the primary cells per path: geometric mean, noise band, and mean build median, sorted ascending by geometric mean.</summary>
    /// <param name="primary">The primary cell readings.</param>
    /// <returns>The sorted per-path aggregates.</returns>
    private static List<PathAggregate> ComputeAggregates(List<CellReading> primary)
    {
        var keys = new List<ContainmentPath>();
        var logSums = new List<double>();
        var noiseSums = new List<double>();
        var buildSums = new List<double>();
        var cellCounts = new List<int>();

        foreach(CellReading cell in primary)
        {
            int slot = keys.IndexOf(cell.Path);

            if(slot < 0)
            {
                keys.Add(cell.Path);
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

        var aggregates = new List<PathAggregate>();

        for(int slot = 0; slot < keys.Count; slot++)
        {
            aggregates.Add(new PathAggregate(
                keys[slot],
                Math.Exp(logSums[slot] / cellCounts[slot]),
                noiseSums[slot],
                buildSums[slot] / cellCounts[slot],
                cellCounts[slot]));
        }

        aggregates.Sort(static (left, right) => left.GeometricMeanMs.CompareTo(right.GeometricMeanMs));

        return aggregates;
    }

    /// <summary>Whether one path's best reading loses every primary workload by more than a factor of two against the best of the other paths.</summary>
    /// <param name="primary">The primary cell readings.</param>
    /// <param name="path">The path under test.</param>
    /// <returns><see langword="true"/> when the trigger fires.</returns>
    private static bool LosesEveryPrimaryCellByTwice(List<CellReading> primary, ContainmentPath path)
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

                if(cell.Path == path)
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

    /// <summary>The rotated path order: as many rounds as paths, each round cyclically shifted by one, so every path occupies every measurement position exactly once and no path systematically runs on a warmer machine.</summary>
    /// <returns>The paths in measurement order.</returns>
    private static IEnumerable<ContainmentPath> AlternatedPaths()
    {
        for(int round = 0; round < Paths.Length; round++)
        {
            for(int position = 0; position < Paths.Length; position++)
            {
                yield return Paths[(position + round) % Paths.Length];
            }
        }
    }

    /// <summary>Computes the dominance-tree candidate digest: per probe, the sorted container set hashes once; the per-probe hashes chain in fixed probe order.</summary>
    /// <param name="index">The built dominance tree.</param>
    /// <param name="probes">The probe set, in digest order.</param>
    /// <returns>The digest as upper-case hexadecimal.</returns>
    private static string ComputeDominanceDigest(BoxContainmentIndex index, BoundingBox[] probes)
    {
        using IncrementalHash chained = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash perQuery = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var candidates = new List<int>();
        Span<byte> intBytes = stackalloc byte[sizeof(int)];
        Span<byte> queryHash = stackalloc byte[32];

        foreach(BoundingBox probe in probes)
        {
            candidates.Clear();

            foreach(int candidate in index.Containers(in probe))
            {
                candidates.Add(candidate);
            }

            HashProbe(chained, perQuery, candidates, intBytes, queryHash);
        }

        return Convert.ToHexString(chained.GetHashAndReset());
    }

    /// <summary>Computes the packed-index candidate digest: per probe, the sorted container set hashes once; the per-probe hashes chain in fixed probe order.</summary>
    /// <param name="index">The built packed index.</param>
    /// <param name="probes">The probe set, in digest order.</param>
    /// <returns>The digest as upper-case hexadecimal.</returns>
    private static string ComputePackedDigest(PackedBoxIndex index, BoundingBox[] probes)
    {
        using IncrementalHash chained = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash perQuery = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var candidates = new List<int>();
        Span<byte> intBytes = stackalloc byte[sizeof(int)];
        Span<byte> queryHash = stackalloc byte[32];

        foreach(BoundingBox probe in probes)
        {
            candidates.Clear();

            foreach(int candidate in index.Containing(in probe))
            {
                candidates.Add(candidate);
            }

            HashProbe(chained, perQuery, candidates, intBytes, queryHash);
        }

        return Convert.ToHexString(chained.GetHashAndReset());
    }

    /// <summary>Computes the brute-scan candidate digest — the gate's ground truth: per probe, the sorted container set hashes once; the per-probe hashes chain in fixed probe order.</summary>
    /// <param name="items">The workload items.</param>
    /// <param name="probes">The probe set, in digest order.</param>
    /// <returns>The digest as upper-case hexadecimal.</returns>
    private static string ComputeScanDigest(BoundingBox[] items, BoundingBox[] probes)
    {
        using IncrementalHash chained = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using IncrementalHash perQuery = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var candidates = new List<int>();
        Span<byte> intBytes = stackalloc byte[sizeof(int)];
        Span<byte> queryHash = stackalloc byte[32];

        foreach(BoundingBox probe in probes)
        {
            candidates.Clear();

            for(int item = 0; item < items.Length; item++)
            {
                if(items[item].Contains(probe))
                {
                    candidates.Add(item);
                }
            }

            HashProbe(chained, perQuery, candidates, intBytes, queryHash);
        }

        return Convert.ToHexString(chained.GetHashAndReset());
    }

    /// <summary>Hashes one probe's candidate set into the chained digest: the set sorts, each id appends little-endian, and the per-probe hash chains — order-free within a query, order-fixed across queries.</summary>
    /// <param name="chained">The chained digest accumulating per-probe hashes.</param>
    /// <param name="perQuery">The per-probe hash, reset after each probe.</param>
    /// <param name="candidates">The probe's candidate ids, sorted in place.</param>
    /// <param name="intBytes">The four-byte scratch an id serializes through.</param>
    /// <param name="queryHash">The thirty-two-byte scratch the per-probe hash lands in.</param>
    private static void HashProbe(IncrementalHash chained, IncrementalHash perQuery, List<int> candidates, Span<byte> intBytes, Span<byte> queryHash)
    {
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
