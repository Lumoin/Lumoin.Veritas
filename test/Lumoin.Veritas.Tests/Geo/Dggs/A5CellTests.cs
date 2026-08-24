using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/cell-to-lonlat.json</c> (95 rows, resolutions 4-20) for
    /// <see cref="Cell.LonLatToCell"/> and <see cref="Cell.CellToLonLat"/>, plus inline edge cases
    /// (WORLD_CELL, antimeridian boundaries). The fixture's <c>cell_id</c> field IS the recorded
    /// <see cref="Cell.LonLatToCell"/> output, so asserting it bit-for-bit end-to-end verifies the whole
    /// estimate-plus-fallback pipeline.
    /// </summary>
    [TestClass]
    internal sealed class A5CellTests
    {
        /// <summary>Bounds the longitude and latitude comparisons of a cell's center against the fixture.</summary>
        private const double CenterTolerance = 0.5e-10;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="Cell.LonLatToCell"/> reproduces the fixture's recorded cell id, bit-for-bit, for every row.</summary>
        [TestMethod]
        public async Task LonLatToCellMatchesFixtureCellIdForEveryRow()
        {
            using JsonDocument fixture = await LoadCellToLonLatFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement row in fixture.RootElement.EnumerateArray())
            {
                LonLat inputLonLat = ReadLonLat(row.GetProperty("input_lonlat"));
                int resolution = row.GetProperty("resolution").GetInt32();
                ulong expectedCellId = Hex.HexToU64(row.GetProperty("cell_id").GetString()!);

                ulong actualCellId = Cell.LonLatToCell(inputLonLat, resolution);
                Assert.AreEqual(
                    expectedCellId,
                    actualCellId,
                    $"cell_id mismatch at resolution {resolution} for input ({inputLonLat.Longitude}, {inputLonLat.Latitude}).");
            }
        }

        /// <summary>Pins that <see cref="Cell.CellToLonLat"/> matches the fixture's recorded cell center for every row.</summary>
        [TestMethod]
        public async Task CellToLonLatMatchesFixtureCenterForEveryRow()
        {
            using JsonDocument fixture = await LoadCellToLonLatFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement row in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(row.GetProperty("cell_id").GetString()!);
                LonLat expectedCenter = ReadLonLat(row.GetProperty("center_lonlat"));

                LonLat actualCenter = Cell.CellToLonLat(cellId);

                Assert.AreEqual(expectedCenter.Longitude, actualCenter.Longitude, CenterTolerance, $"cell {row.GetProperty("cell_id").GetString()} longitude");
                Assert.AreEqual(expectedCenter.Latitude, actualCenter.Latitude, CenterTolerance, $"cell {row.GetProperty("cell_id").GetString()} latitude");
            }
        }

        /// <summary>Pins that every cell center returned by <see cref="Cell.CellToLonLat"/> falls within valid longitude and latitude bounds.</summary>
        [TestMethod]
        public async Task CellToLonLatStaysWithinGeographicRangeForEveryRow()
        {
            using JsonDocument fixture = await LoadCellToLonLatFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement row in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(row.GetProperty("cell_id").GetString()!);
                LonLat center = Cell.CellToLonLat(cellId);

                Assert.IsGreaterThanOrEqualTo(-180, center.Longitude);
                Assert.IsLessThanOrEqualTo(180, center.Longitude);
                Assert.IsGreaterThanOrEqualTo(-90, center.Latitude);
                Assert.IsLessThanOrEqualTo(90, center.Latitude);
            }
        }

        /// <summary>Pins that resolution -1 always returns the world cell, regardless of the input point.</summary>
        [TestMethod]
        public void LonLatToCellAtResolutionNegativeOneReturnsWorldCell()
        {
            ulong cellId = Cell.LonLatToCell(new LonLat(0, 0), -1);
            Assert.AreEqual(Serialization.WorldCell, cellId);
        }

        /// <summary>Pins that the world cell's center is the origin, longitude 0 and latitude 0.</summary>
        [TestMethod]
        public void CellToLonLatOfWorldCellReturnsOrigin()
        {
            LonLat lonLat = Cell.CellToLonLat(Serialization.WorldCell);
            Assert.AreEqual(0, lonLat.Longitude);
            Assert.AreEqual(0, lonLat.Latitude);
        }

        /// <summary>Pins that the world cell's boundary is the empty array.</summary>
        [TestMethod]
        public void CellToBoundaryOfWorldCellReturnsEmpty()
        {
            LonLat[] boundary = Cell.CellToBoundary(Serialization.WorldCell);
            Assert.HasCount(0, boundary);
        }

        /// <summary>Pins that cells straddling the antimeridian produce a boundary whose longitude span stays under 180 degrees, across fixed, subdivided, and auto segment counts.</summary>
        [TestMethod]
        public void AntimeridianCellBoundarySpansLessThanOneHundredEightyDegrees()
        {
            ulong[] antimeridianCells = [Hex.HexToU64("eb60000000000000"), Hex.HexToU64("2e00000000000000")];
            int[] segmentsOptions = [1, 10, 0]; // 0 selects the auto formula.

            foreach(ulong cellId in antimeridianCells)
            {
                foreach(int segments in segmentsOptions)
                {
                    LonLat[] boundary = Cell.CellToBoundary(cellId, segments: segments);

                    double minLongitude = double.PositiveInfinity;
                    double maxLongitude = double.NegativeInfinity;
                    foreach(LonLat point in boundary)
                    {
                        minLongitude = Math.Min(minLongitude, point.Longitude);
                        maxLongitude = Math.Max(maxLongitude, point.Longitude);
                    }

                    Assert.IsLessThan(180, maxLongitude - minLongitude, $"cell {Hex.U64ToHex(cellId)} segments {segments}");
                }
            }
        }

        /// <summary>Reads a two-element JSON array as a <see cref="LonLat"/> (longitude first, then latitude).</summary>
        private static LonLat ReadLonLat(JsonElement array)
        {
            return new LonLat(array[0].GetDouble(), array[1].GetDouble());
        }

        /// <summary>Loads <c>fixtures/cell-to-lonlat.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadCellToLonLatFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/cell-to-lonlat.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
