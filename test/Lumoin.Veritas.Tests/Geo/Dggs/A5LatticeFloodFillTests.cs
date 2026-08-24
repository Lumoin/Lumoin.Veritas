using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/lattice-flood-fill.json</c> for
    /// <see cref="LatticeFloodFill.TripleSpaceFloodFill(HashSet{ulong}, ReadOnlySpan{ulong}, int, int?)"/>:
    /// exact interior/frontier cell sets for every case, compared as hex arrays sorted ordinally, since
    /// the search visits its per-quintant dictionary and per-key results in no particular
    /// externally-meaningful order.
    /// </summary>
    [TestClass]
    internal sealed class A5LatticeFloodFillTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="LatticeFloodFill.TripleSpaceFloodFill(HashSet{ulong}, ReadOnlySpan{ulong}, int, int?)"/>'s interior and frontier cell sets, sorted ordinally, match the fixture for every case.</summary>
        [TestMethod]
        public async Task TripleSpaceFloodFillMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
            {
                int resolution = testCase.GetProperty("resolution").GetInt32();
                ulong[] seedCells = ReadHexArray(testCase.GetProperty("seedCells"));
                HashSet<ulong> firewall = [.. ReadHexArray(testCase.GetProperty("firewallCells"))];
                int? maxLayers = testCase.TryGetProperty("maxLayers", out JsonElement maxLayersElement) ? maxLayersElement.GetInt32() : null;

                LatticeFloodFillResult result = LatticeFloodFill.TripleSpaceFloodFill(firewall, seedCells, resolution, maxLayers);

                string[] expectedInterior = ReadStringArray(testCase.GetProperty("interiorCells"));
                string[] expectedFrontier = ReadStringArray(testCase.GetProperty("frontierCells"));

                Assert.AreSequenceEqual(expectedInterior, ToSortedHex(result.InteriorCells), testCase.GetProperty("name").GetString());
                Assert.AreSequenceEqual(expectedFrontier, ToSortedHex(result.FrontierCellIds), testCase.GetProperty("name").GetString());
            }
        }

        /// <summary>Converts cell ids to hex and sorts them ordinally, matching the fixture's recorded string order.</summary>
        private static string[] ToSortedHex(ulong[] cellIds)
        {
            string[] hex = new string[cellIds.Length];
            for(int index = 0; index < cellIds.Length; index++)
            {
                hex[index] = Hex.U64ToHex(cellIds[index]);
            }

            Array.Sort(hex, StringComparer.Ordinal);

            return hex;
        }

        /// <summary>Parses a fixture array of hex strings into raw cell ids.</summary>
        private static ulong[] ReadHexArray(JsonElement arrayElement)
        {
            ulong[] result = new ulong[arrayElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement element in arrayElement.EnumerateArray())
            {
                result[index] = Hex.HexToU64(element.GetString()!);
                index++;
            }

            return result;
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

        /// <summary>Loads <c>fixtures/traversal/lattice-flood-fill.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/lattice-flood-fill.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
