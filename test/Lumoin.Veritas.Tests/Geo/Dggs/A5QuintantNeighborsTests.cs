using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Lattice;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/quintant-neighbors.json</c> (60 rows) for
    /// <see cref="QuintantNeighbors.GetCellNeighbors"/>: exact integer arrays, order-sensitive.
    /// </summary>
    [TestClass]
    internal sealed class A5QuintantNeighborsTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the neighbor array matches the fixture's expected order-sensitive array for every row.</summary>
        [TestMethod]
        public async Task GetCellNeighborsMatchesFixtureForEveryRow()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                JsonElement input = testCase.GetProperty("input");
                ulong s = input.GetProperty("s").GetUInt64();
                int hilbertResolution = input.GetProperty("resolution").GetInt32();
                Orientation orientation = ParseOrientation(input.GetProperty("orientation").GetString());

                ulong[] neighbors = QuintantNeighbors.GetCellNeighbors(s, hilbertResolution, orientation);

                ulong[] expected = ReadUlongArray(testCase.GetProperty("output").GetProperty("neighbors"));
                Assert.AreSequenceEqual(expected, neighbors);
            }
        }

        /// <summary>Reads a JSON array of numbers into a <see cref="ulong"/> array, preserving order.</summary>
        private static ulong[] ReadUlongArray(JsonElement arrayElement)
        {
            ulong[] result = new ulong[arrayElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement element in arrayElement.EnumerateArray())
            {
                result[index] = element.GetUInt64();
                index++;
            }

            return result;
        }

        /// <summary>Parses a fixture orientation string (e.g. <c>"wv"</c>) into an <see cref="Orientation"/> value.</summary>
        private static Orientation ParseOrientation(string? orientation)
        {
            return orientation switch
            {
                "uv" => Orientation.UV,
                "vu" => Orientation.VU,
                "uw" => Orientation.UW,
                "wu" => Orientation.WU,
                "vw" => Orientation.VW,
                "wv" => Orientation.WV,
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown fixture orientation value."),
            };
        }

        /// <summary>Loads <c>fixtures/traversal/quintant-neighbors.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/quintant-neighbors.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
