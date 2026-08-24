using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Regions;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/regions/polygon.json</c> for <see cref="PolygonToCells"/>: the 30
    /// <c>polygon</c> cases as exact hex arrays (expanded via <see cref="Compaction.Uncompact"/> and
    /// sorted unsigned-ascending before comparison — the order the fixture records), the 16
    /// <c>country</c> cases as exact unique-cell counts, and the degenerate-ring/closed-ring/flat-ring
    /// edge cases.
    /// </summary>
    [TestClass]
    internal sealed class A5PolygonToCellsTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the uncompacted, sorted cell set matches the fixture's expected hex array for every polygon case.</summary>
        [TestMethod]
        public async Task GetCellsMatchesFixtureForEveryPolygonCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("polygon").EnumerateArray())
            {
                LonLat[][] polygon = ReadRings(testCase.GetProperty("polygon"));
                int resolution = testCase.GetProperty("resolution").GetInt32();

                ulong[] compacted = PolygonToCells.GetCells(polygon, resolution);
                ulong[] expanded = Compaction.Uncompact(compacted, resolution);
                Array.Sort(expanded);

                string[] expected = ReadStringArray(testCase.GetProperty("cells"));
                Assert.AreSequenceEqual(expected, ToHex(expanded), testCase.GetProperty("name").GetString());
            }
        }

        /// <summary>Pins that the count of unique uncompacted cells matches the fixture's expected count for every country case.</summary>
        [TestMethod]
        public async Task GetCellsMatchesFixtureCountForEveryCountryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("country").EnumerateArray())
            {
                LonLat[][] polygon = ReadRings(testCase.GetProperty("polygon"));
                int resolution = testCase.GetProperty("resolution").GetInt32();

                ulong[] compacted = PolygonToCells.GetCells(polygon, resolution);
                ulong[] expanded = Compaction.Uncompact(compacted, resolution);

                HashSet<ulong> unique = [.. expanded];
                Assert.HasCount(testCase.GetProperty("cellCount").GetInt32(), unique, testCase.GetProperty("name").GetString());
            }
        }

        /// <summary>Pins that a ring with fewer than three distinct vertices, in any accepted input shape, yields no cells.</summary>
        [TestMethod]
        public void GetCellsReturnsEmptyForFewerThanThreeVertices()
        {
            Assert.IsEmpty(PolygonToCells.GetCells(Array.Empty<LonLat>(), 5));
            Assert.IsEmpty(PolygonToCells.GetCells([new LonLat(0, 0), new LonLat(1, 1)], 5));

            // Nested form with a degenerate outer ring.
            Assert.IsEmpty(PolygonToCells.GetCells([[new LonLat(0, 0), new LonLat(1, 1)]], 5));

            // Closed ring with only 2 distinct vertices.
            Assert.IsEmpty(PolygonToCells.GetCells([new LonLat(0, 0), new LonLat(1, 1), new LonLat(0, 0)], 5));
        }

        /// <summary>Pins that closed rings (first vertex repeated at the end) yield the same cells as the equivalent open rings.</summary>
        [TestMethod]
        public void GetCellsAcceptsClosedRings()
        {
            LonLat[] ring = [new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44), new LonLat(-5, 44)];
            LonLat[] hole = [new LonLat(2, 51), new LonLat(8, 51), new LonLat(8, 47), new LonLat(2, 47)];

            ulong[] fromClosed = PolygonToCells.GetCells([CloseRing(ring), CloseRing(hole)], 6);
            ulong[] fromOpen = PolygonToCells.GetCells([ring, hole], 6);

            Assert.AreSequenceEqual(fromOpen, fromClosed);
        }

        /// <summary>Pins that a flat (single-ring) array input is treated the same as a nested single-ring polygon without holes.</summary>
        [TestMethod]
        public void GetCellsTreatsFlatRingAsPolygonWithoutHoles()
        {
            LonLat[] ring = [new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44), new LonLat(-5, 44)];

            Assert.AreSequenceEqual(PolygonToCells.GetCells([ring], 5), PolygonToCells.GetCells(ring, 5));
        }

        /// <summary>Pins that a hole ring with fewer than three vertices is ignored, leaving the outer ring's cells unchanged.</summary>
        [TestMethod]
        public void GetCellsIgnoresDegenerateHoles()
        {
            LonLat[] ring = [new LonLat(-5, 54), new LonLat(15, 54), new LonLat(15, 44), new LonLat(-5, 44)];
            LonLat[] degenerateHole = [new LonLat(2, 50), new LonLat(3, 49)];

            Assert.AreSequenceEqual(PolygonToCells.GetCells([ring], 5), PolygonToCells.GetCells([ring, degenerateHole], 5));
        }

        /// <summary>Closes a ring GeoJSON-style by repeating the first vertex at the end.</summary>
        private static LonLat[] CloseRing(LonLat[] ring)
        {
            return [.. ring, ring[0]];
        }

        /// <summary>Reads a fixture polygon (array of rings of <c>[longitude, latitude]</c> pairs).</summary>
        private static LonLat[][] ReadRings(JsonElement polygonElement)
        {
            LonLat[][] rings = new LonLat[polygonElement.GetArrayLength()][];
            int ringIndex = 0;
            foreach(JsonElement ringElement in polygonElement.EnumerateArray())
            {
                LonLat[] ring = new LonLat[ringElement.GetArrayLength()];
                int vertexIndex = 0;
                foreach(JsonElement vertexElement in ringElement.EnumerateArray())
                {
                    ring[vertexIndex] = new LonLat(vertexElement[0].GetDouble(), vertexElement[1].GetDouble());
                    vertexIndex++;
                }

                rings[ringIndex] = ring;
                ringIndex++;
            }

            return rings;
        }

        /// <summary>Converts cell ids to hex, preserving order.</summary>
        private static string[] ToHex(ulong[] cellIds)
        {
            string[] hex = new string[cellIds.Length];
            for(int index = 0; index < cellIds.Length; index++)
            {
                hex[index] = Hex.U64ToHex(cellIds[index]);
            }

            return hex;
        }

        /// <summary>Reads a JSON array of strings, preserving order.</summary>
        private static string[] ReadStringArray(JsonElement arrayElement)
        {
            string[] result = new string[arrayElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement element in arrayElement.EnumerateArray())
            {
                result[index] = element.GetString()!;
                index++;
            }

            return result;
        }

        /// <summary>Loads <c>fixtures/regions/polygon.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/regions/polygon.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
