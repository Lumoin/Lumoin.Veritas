using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Bit-identity gates for the SIMD batch kernel core (<see cref="PointToCellBatchCore"/>) at every
    /// lane width: the fixture corpus inputs, the full containment-sweep corpus (1,249 places ×
    /// resolutions 1–29 = 36,221 cases), and a seeded 1,000,000-point deterministic sweep including
    /// near-edge perturbations (1e-12 degrees around cell boundary vertices), near-pole and
    /// near-antimeridian bands. Every output cell id must equal the scalar reference kernel's bit for
    /// bit.
    /// </summary>
    /// <remarks>
    /// The widths run unconditionally on every host: the cross-platform vector APIs are functionally
    /// correct even where a width is not hardware-accelerated (they fall back to software lanes), so
    /// these gates test correctness everywhere while the ISA-named ladder rungs gate hardware
    /// availability separately in <c>A5PointToCellKernelLadderTests</c>.
    /// </remarks>
    [TestClass]
    internal sealed class A5PointToCellBatchCoreBitIdentityTests
    {
        /// <summary>Recorded seed of the pseudorandom sweep — the gate's determinism anchor.</summary>
        private const ulong SweepSeed = 0x9E3779B97F4A7C15UL;

        /// <summary>Total point count of the seeded sweep.</summary>
        private const int SweepPointCount = 1_000_000;

        /// <summary>The resolutions the seeded sweep cycles across (Hilbert range boundaries included).</summary>
        private static int[] SweepResolutions { get; } = [2, 3, 5, 8, 10, 12, 15, 20, 25, 29, 30];

        /// <summary>
        /// The seeded sweep's inputs and scalar-reference outputs, built once and shared across the
        /// width data rows so the deterministic corpus (and its scalar baseline) is computed exactly once.
        /// </summary>
        private static Task<SweepData> Sweep { get; } = BuildSweepAsync();

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that every fixture corpus row's batch-kernel output cell id is bit-identical to the scalar reference kernel and to the corpus pin, at the given lane width.</summary>
        [TestMethod]
        [DataRow(128)]
        [DataRow(256)]
        [DataRow(512)]
        public async Task CorpusRowsAreBitIdenticalToTheScalarKernel(int laneWidthBits)
        {
            Dictionary<int, List<(double Longitude, double Latitude, ulong PinnedCellId)>> rowsByResolution = [];
            using(FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/cell-to-lonlat.json")))
            {
                using JsonDocument fixture = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                foreach(JsonElement row in fixture.RootElement.EnumerateArray())
                {
                    JsonElement lonLat = row.GetProperty("input_lonlat");
                    int resolution = row.GetProperty("resolution").GetInt32();
                    ulong pinnedCellId = Hex.HexToU64(row.GetProperty("cell_id").GetString()!);
                    if(!rowsByResolution.TryGetValue(resolution, out List<(double, double, ulong)>? rows))
                    {
                        rows = [];
                        rowsByResolution[resolution] = rows;
                    }

                    rows.Add((lonLat[0].GetDouble(), lonLat[1].GetDouble(), pinnedCellId));
                }
            }

            int caseCount = 0;
            List<string> failures = [];
            foreach((int resolution, List<(double Longitude, double Latitude, ulong PinnedCellId)> rows) in rowsByResolution)
            {
                double[] source = new double[2 * rows.Count];
                for(int index = 0; index < rows.Count; index++)
                {
                    source[2 * index] = rows[index].Longitude;
                    source[(2 * index) + 1] = rows[index].Latitude;
                }

                A5CellId[] scalarCells = new A5CellId[rows.Count];
                A5CellId[] batchCells = new A5CellId[rows.Count];
                A5PointToCellKernelSelection.Scalar(source, resolution, scalarCells);
                RunBatch(laneWidthBits, source, resolution, batchCells);

                for(int index = 0; index < rows.Count; index++)
                {
                    caseCount++;
                    if(batchCells[index] != scalarCells[index])
                    {
                        failures.Add($"({rows[index].Longitude}, {rows[index].Latitude}) res {resolution}: batch {batchCells[index].Value:x} != scalar {scalarCells[index].Value:x}.");
                    }

                    // Cross-check the scalar baseline itself against the corpus pin, so a regression in
                    // the reference cannot silently re-anchor this gate.
                    if(scalarCells[index].Value != rows[index].PinnedCellId)
                    {
                        failures.Add($"({rows[index].Longitude}, {rows[index].Latitude}) res {resolution}: scalar {scalarCells[index].Value:x} != corpus pin {rows[index].PinnedCellId:x}.");
                    }
                }
            }

            TestContext.WriteLine($"Width {laneWidthBits}: {caseCount} corpus rows bit-identical.");
            Assert.HasCount(0, failures, string.Join(Environment.NewLine, failures));
        }

        /// <summary>Pins that the full containment-sweep corpus's batch-kernel output is bit-identical to the scalar reference kernel across every resolution 1 through MaxResolution-1, at the given lane width.</summary>
        [TestMethod]
        [DataRow(128)]
        [DataRow(256)]
        [DataRow(512)]
        public async Task ContainmentSweepCorpusIsBitIdenticalToTheScalarKernel(int laneWidthBits)
        {
            LonLat[] places = await LoadPopulatedPlacesAsync(TestContext.CancellationToken).ConfigureAwait(false);
            double[] source = new double[2 * places.Length];
            for(int index = 0; index < places.Length; index++)
            {
                source[2 * index] = places[index].Longitude;
                source[(2 * index) + 1] = places[index].Latitude;
            }

            A5CellId[] scalarCells = new A5CellId[places.Length];
            A5CellId[] batchCells = new A5CellId[places.Length];
            int caseCount = 0;
            List<string> failures = [];
            for(int resolution = 1; resolution < Serialization.MaxResolution; resolution++)
            {
                A5PointToCellKernelSelection.Scalar(source, resolution, scalarCells);
                RunBatch(laneWidthBits, source, resolution, batchCells);
                for(int index = 0; index < places.Length; index++)
                {
                    caseCount++;
                    if(batchCells[index] != scalarCells[index])
                    {
                        failures.Add($"({places[index].Longitude}, {places[index].Latitude}) res {resolution}: batch {batchCells[index].Value:x} != scalar {scalarCells[index].Value:x}.");
                    }
                }
            }

            TestContext.WriteLine($"Width {laneWidthBits}: {caseCount} containment-sweep cases bit-identical.");
            Assert.HasCount(0, failures, string.Join(Environment.NewLine, failures));
        }

        /// <summary>Pins that the seeded 1,000,000-point deterministic sweep's batch-kernel output is bit-identical to the scalar reference kernel, at the given lane width.</summary>
        [TestMethod]
        [DataRow(128)]
        [DataRow(256)]
        [DataRow(512)]
        public async Task MillionPointSeededSweepIsBitIdenticalToTheScalarKernel(int laneWidthBits)
        {
            SweepData sweep = await Sweep.ConfigureAwait(false);

            int caseCount = 0;
            int mismatchCount = 0;
            string? firstMismatch = null;
            foreach((int resolution, double[] source, A5CellId[] scalarCells) in sweep.Groups)
            {
                A5CellId[] batchCells = new A5CellId[scalarCells.Length];
                RunBatch(laneWidthBits, source, resolution, batchCells);
                for(int index = 0; index < scalarCells.Length; index++)
                {
                    caseCount++;
                    if(batchCells[index] != scalarCells[index])
                    {
                        mismatchCount++;
                        firstMismatch ??= $"({source[2 * index]}, {source[(2 * index) + 1]}) res {resolution}: batch {batchCells[index].Value:x} != scalar {scalarCells[index].Value:x}.";
                    }
                }
            }

            TestContext.WriteLine($"Width {laneWidthBits}: {caseCount} seeded-sweep points (seed 0x{SweepSeed:X}) bit-identical.");
            Assert.AreEqual(SweepPointCount, caseCount, "The sweep must cover exactly the pinned point count.");
            Assert.AreEqual(0, mismatchCount, $"{mismatchCount} mismatches; first: {firstMismatch}");
        }

        /// <summary>Pins that the batch kernel's output is bit-identical to the scalar kernel at every point count crossing a block/tail boundary, at the given lane width.</summary>
        [TestMethod]
        [DataRow(128)]
        [DataRow(256)]
        [DataRow(512)]
        public void BlockBoundaryAndTailLengthsAreBitIdentical(int laneWidthBits)
        {
            // Lengths 0 through 3 blocks + 3 at the widest lane count cross every block/tail boundary
            // of every width, the varint boundary-sweep precedent.
            const int Resolution = 15;
            const int MaxPointCount = (3 * 8) + 3;
            ulong state = SweepSeed;
            double[] source = new double[2 * MaxPointCount];
            for(int index = 0; index < MaxPointCount; index++)
            {
                source[2 * index] = -180 + (360 * NextUnitDouble(ref state));
                source[(2 * index) + 1] = -90 + (180 * NextUnitDouble(ref state));
            }

            for(int pointCount = 0; pointCount <= MaxPointCount; pointCount++)
            {
                A5CellId[] scalarCells = new A5CellId[pointCount];
                A5CellId[] batchCells = new A5CellId[pointCount];
                ReadOnlySpan<double> slice = source.AsSpan(0, 2 * pointCount);
                A5PointToCellKernelSelection.Scalar(slice, Resolution, scalarCells);
                RunBatch(laneWidthBits, slice, Resolution, batchCells);
                Assert.AreSequenceEqual(scalarCells, batchCells, $"Length {pointCount} diverged at width {laneWidthBits}.");
            }
        }

        /// <summary>Pins that resolutions below the SIMD-eligible range delegate to output bit-identical to the scalar kernel, at the given lane width.</summary>
        [TestMethod]
        [DataRow(128)]
        [DataRow(256)]
        [DataRow(512)]
        public void LowResolutionsDelegateBitIdenticallyToTheScalarPath(int laneWidthBits)
        {
            const int PointCount = 100;
            ulong state = SweepSeed;
            double[] source = new double[2 * PointCount];
            for(int index = 0; index < PointCount; index++)
            {
                source[2 * index] = -180 + (360 * NextUnitDouble(ref state));
                source[(2 * index) + 1] = -90 + (180 * NextUnitDouble(ref state));
            }

            foreach(int resolution in (int[])[-1, 0, 1])
            {
                A5CellId[] scalarCells = new A5CellId[PointCount];
                A5CellId[] batchCells = new A5CellId[PointCount];
                A5PointToCellKernelSelection.Scalar(source, resolution, scalarCells);
                RunBatch(laneWidthBits, source, resolution, batchCells);
                Assert.AreSequenceEqual(scalarCells, batchCells, $"Resolution {resolution} diverged at width {laneWidthBits}.");
            }
        }

        /// <summary>Pins that an odd-length source span or a mismatched destination span length throws ArgumentException with the correct parameter name, at the given lane width.</summary>
        [TestMethod]
        [DataRow(128)]
        [DataRow(256)]
        [DataRow(512)]
        public void SpanContractViolationsThrowLikeTheScalarKernel(int laneWidthBits)
        {
            bool oddSourceThrew = false;
            string? oddSourceParameterName = null;
            try
            {
                Span<double> oddSource = stackalloc double[3];
                Span<A5CellId> destination = stackalloc A5CellId[1];
                RunBatch(laneWidthBits, oddSource, 10, destination);
            }
            catch(ArgumentException exception)
            {
                oddSourceThrew = true;
                oddSourceParameterName = exception.ParamName;
            }

            Assert.IsTrue(oddSourceThrew, "An odd source length must throw.");
            Assert.AreEqual("sourceLongitudeLatitude", oddSourceParameterName);

            bool mismatchedDestinationThrew = false;
            string? mismatchedDestinationParameterName = null;
            try
            {
                Span<double> source = stackalloc double[4];
                Span<A5CellId> shortDestination = stackalloc A5CellId[1];
                RunBatch(laneWidthBits, source, 10, shortDestination);
            }
            catch(ArgumentException exception)
            {
                mismatchedDestinationThrew = true;
                mismatchedDestinationParameterName = exception.ParamName;
            }

            Assert.IsTrue(mismatchedDestinationThrew, "A mismatched destination length must throw.");
            Assert.AreEqual("destinationCellIds", mismatchedDestinationParameterName);
        }

        /// <summary>Dispatches to the shared batch core at the requested lane width.</summary>
        private static void RunBatch(int laneWidthBits, ReadOnlySpan<double> sourceLongitudeLatitude, int resolution, Span<A5CellId> destinationCellIds)
        {
            switch(laneWidthBits)
            {
                case 128:
                    PointToCellBatchCore.Run<PointToCellLanes128>(sourceLongitudeLatitude, resolution, destinationCellIds);
                    break;
                case 256:
                    PointToCellBatchCore.Run<PointToCellLanes256>(sourceLongitudeLatitude, resolution, destinationCellIds);
                    break;
                case 512:
                    PointToCellBatchCore.Run<PointToCellLanes512>(sourceLongitudeLatitude, resolution, destinationCellIds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(laneWidthBits), laneWidthBits, "Lane width must be 128, 256 or 512 bits.");
            }
        }

        /// <summary>
        /// Builds the deterministic 1,000,000-point sweep and its scalar baseline once: near-edge
        /// perturbations (8 compass offsets of 1e-12 degrees around the boundary vertices of the first
        /// 25 corpus places' cells at resolutions 10, 15 and 29), near-pole and near-antimeridian bands,
        /// and a seeded uniform fill, partitioned across <see cref="SweepResolutions"/>.
        /// </summary>
        private static async Task<SweepData> BuildSweepAsync()
        {
            LonLat[] places = await LoadPopulatedPlacesAsync(CancellationToken.None).ConfigureAwait(false);

            Dictionary<int, List<double>> pointsByResolution = [];
            foreach(int resolution in SweepResolutions)
            {
                pointsByResolution[resolution] = [];
            }

            int totalPoints = 0;

            // Near-edge block: perturb every boundary vertex of the containing cell by 1e-12 degrees in
            // the 8 compass directions, at the vertex's own resolution.
            const double Perturbation = 1e-12;
            ReadOnlySpan<(double DeltaLongitude, double DeltaLatitude)> offsets =
            [
                (Perturbation, 0), (-Perturbation, 0), (0, Perturbation), (0, -Perturbation),
                (Perturbation, Perturbation), (Perturbation, -Perturbation), (-Perturbation, Perturbation), (-Perturbation, -Perturbation)
            ];
            foreach(int resolution in (int[])[10, 15, 29])
            {
                List<double> group = pointsByResolution[resolution];
                for(int placeIndex = 0; placeIndex < 25; placeIndex++)
                {
                    ulong cellId = Cell.LonLatToCell(places[placeIndex], resolution);
                    LonLat[] boundary = Cell.CellToBoundary(cellId, closedRing: false);
                    foreach(LonLat vertex in boundary)
                    {
                        foreach((double deltaLongitude, double deltaLatitude) in offsets)
                        {
                            group.Add(vertex.Longitude + deltaLongitude);
                            group.Add(vertex.Latitude + deltaLatitude);
                            totalPoints++;
                        }
                    }
                }
            }

            // Near-pole and near-antimeridian bands (the documented spiral-fallback-reachable classes),
            // then a seeded uniform fill up to exactly the pinned total, round-robin across resolutions.
            // The appender is a parameter-taking static method — no state is captured lexically.
            ulong state = SweepSeed;
            int resolutionCursor = 0;

            for(int index = 0; index < 5000; index++)
            {
                double sign = (index % 2 == 0) ? 1 : -1;
                AddSweepPoint(pointsByResolution, ref resolutionCursor, ref totalPoints, -180 + (360 * NextUnitDouble(ref state)), sign * (88 + (1.999 * NextUnitDouble(ref state))));
            }

            for(int index = 0; index < 5000; index++)
            {
                double sign = (index % 2 == 0) ? 1 : -1;
                AddSweepPoint(pointsByResolution, ref resolutionCursor, ref totalPoints, sign * (179 + (0.9999 * NextUnitDouble(ref state))), -90 + (180 * NextUnitDouble(ref state)));
            }

            while(totalPoints < SweepPointCount)
            {
                AddSweepPoint(pointsByResolution, ref resolutionCursor, ref totalPoints, -180 + (360 * NextUnitDouble(ref state)), -90 + (180 * NextUnitDouble(ref state)));
            }

            List<(int Resolution, double[] Source, A5CellId[] ScalarCells)> groups = [];
            foreach(int resolution in SweepResolutions)
            {
                double[] source = [.. pointsByResolution[resolution]];
                A5CellId[] scalarCells = new A5CellId[source.Length / 2];
                A5PointToCellKernelSelection.Scalar(source, resolution, scalarCells);
                groups.Add((resolution, source, scalarCells));
            }

            return new SweepData(groups);
        }

        /// <summary>Appends one sweep point to the next resolution group in round-robin order.</summary>
        private static void AddSweepPoint(
            Dictionary<int, List<double>> pointsByResolution,
            ref int resolutionCursor,
            ref int totalPoints,
            double longitude,
            double latitude)
        {
            List<double> group = pointsByResolution[SweepResolutions[resolutionCursor]];
            resolutionCursor = (resolutionCursor + 1) % SweepResolutions.Length;
            group.Add(longitude);
            group.Add(latitude);
            totalPoints++;
        }

        /// <summary>Advances the xorshift64 state and returns the next value — the benchmark harness's generator.</summary>
        private static ulong NextXorShift(ref ulong state)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;

            return state;
        }

        /// <summary>Next deterministic double in [0, 1).</summary>
        private static double NextUnitDouble(ref ulong state)
        {
            return (NextXorShift(ref state) >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>Loads the populated-places corpus points (the containment sweep's own corpus).</summary>
        private static async Task<LonLat[]> LoadPopulatedPlacesAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "data/ne_50m_populated_places_nameonly.json"));
            using JsonDocument fixture = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            JsonElement features = fixture.RootElement.GetProperty("features");
            LonLat[] places = new LonLat[features.GetArrayLength()];

            int index = 0;
            foreach(JsonElement feature in features.EnumerateArray())
            {
                JsonElement coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
                places[index] = new LonLat(coordinates[0].GetDouble(), coordinates[1].GetDouble());
                index++;
            }

            return places;
        }

        /// <summary>The seeded sweep's per-resolution inputs with their scalar baseline, built once.</summary>
        private sealed record SweepData(List<(int Resolution, double[] Source, A5CellId[] ScalarCells)> Groups);
    }
}
