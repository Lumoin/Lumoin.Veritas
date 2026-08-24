using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/line.json</c> (4 line segments) for
    /// <see cref="LineTraversal.LineStringToCells"/>: exact hex arrays, compared after an
    /// unsigned-ascending sort of the traversal-ordered result — the fixture records its rows in that
    /// sorted order while the function's own output order stays the semantic traversal order. Plus the
    /// empty/single-waypoint edge cases and junction deduplication.
    /// </summary>
    [TestClass]
    internal sealed class A5LineTraversalTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="LineTraversal.LineStringToCells"/>, sorted unsigned-ascending, matches the fixture's cell set for every line segment.</summary>
        [TestMethod]
        public async Task LineStringToCellsMatchesFixtureForEverySegment()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("lineSegment").EnumerateArray())
            {
                LonLat start = ReadLonLat(testCase.GetProperty("start"));
                LonLat end = ReadLonLat(testCase.GetProperty("end"));
                int resolution = testCase.GetProperty("resolution").GetInt32();

                ulong[] cells = LineTraversal.LineStringToCells([start, end], resolution);
                ulong[] sorted = (ulong[])cells.Clone();
                Array.Sort(sorted);

                string[] expected = ReadStringArray(testCase.GetProperty("cells"));
                Assert.AreSequenceEqual(expected, ToHex(sorted), testCase.GetProperty("name").GetString());
            }
        }

        /// <summary>Pins that an empty waypoint list produces an empty cell array.</summary>
        [TestMethod]
        public void LineStringToCellsReturnsEmptyForEmptyWaypoints()
        {
            Assert.IsEmpty(LineTraversal.LineStringToCells([], 5));
        }

        /// <summary>Pins that a single waypoint produces exactly one cell.</summary>
        [TestMethod]
        public void LineStringToCellsReturnsSingleCellForSingleWaypoint()
        {
            ulong[] cells = LineTraversal.LineStringToCells([new LonLat(10, 50)], 5);
            Assert.HasCount(1, cells);
        }

        /// <summary>Pins that adjacent segments sharing a junction waypoint do not produce a duplicate cell at that junction.</summary>
        [TestMethod]
        public void LineStringToCellsDeduplicatesCellsAtSegmentJunctions()
        {
            LonLat[] waypoints = [new LonLat(0, 50), new LonLat(10, 50), new LonLat(10, 45)];

            ulong[] cells = LineTraversal.LineStringToCells(waypoints, 3);

            HashSet<ulong> unique = [.. cells];
            Assert.HasCount(unique.Count, cells);
        }

        /// <summary>Reads a JSON <c>[longitude, latitude]</c> pair into a <see cref="LonLat"/>.</summary>
        private static LonLat ReadLonLat(JsonElement pairElement)
        {
            return new LonLat(pairElement[0].GetDouble(), pairElement[1].GetDouble());
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

        /// <summary>Loads <c>fixtures/traversal/line.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/line.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
