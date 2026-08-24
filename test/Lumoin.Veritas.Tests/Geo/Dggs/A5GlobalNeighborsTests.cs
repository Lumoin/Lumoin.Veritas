using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/global-neighbors.json</c> (168 rows) for
    /// <see cref="GlobalNeighbors.GetGlobalCellNeighbors"/>: exact hex arrays for both
    /// <c>neighbors</c> and <c>edgeNeighbors</c>, order-sensitive — the result's ascending-by-id order
    /// is compared directly, with no additional sort in the test.
    /// </summary>
    [TestClass]
    internal sealed class A5GlobalNeighborsTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="GlobalNeighbors.GetGlobalCellNeighbors"/>'s full neighbor list matches the fixture's ordered hex array for every row.</summary>
        [TestMethod]
        public async Task GetGlobalCellNeighborsMatchesFixtureForEveryRow()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("input").GetProperty("cellId").GetString()!);

                string[] neighbors = ToHex(GlobalNeighbors.GetGlobalCellNeighbors(cellId));

                string[] expected = ReadStringArray(testCase.GetProperty("output").GetProperty("neighbors"));
                Assert.AreSequenceEqual(expected, neighbors);
            }
        }

        /// <summary>Pins that <see cref="GlobalNeighbors.GetGlobalCellNeighbors"/> with <c>edgeOnly: true</c> matches the fixture's ordered edge-neighbor hex array for every row.</summary>
        [TestMethod]
        public async Task GetGlobalCellNeighborsFindsEdgeOnlyNeighborsForEveryRow()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("input").GetProperty("cellId").GetString()!);

                string[] edgeNeighbors = ToHex(GlobalNeighbors.GetGlobalCellNeighbors(cellId, edgeOnly: true));

                string[] expected = ReadStringArray(testCase.GetProperty("output").GetProperty("edgeNeighbors"));
                Assert.AreSequenceEqual(expected, edgeNeighbors);
            }
        }

        /// <summary>Pins that the fixture's edge-neighbor count is 3 at resolution 1 and 5 at every other resolution.</summary>
        [TestMethod]
        public async Task EdgeNeighborCountMatchesResolutionExpectation()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("input").GetProperty("cellId").GetString()!);
                int resolution = Serialization.GetResolution(cellId);

                // Res 0: pentagonal face -> 5 edge neighbors. Res 1: triangular quintant -> 3 edge
                // neighbors. Res 2+: pentagonal cell -> 5 edge neighbors.
                int expectedEdgeCount = resolution == 1 ? 3 : 5;

                string[] edgeNeighbors = ReadStringArray(testCase.GetProperty("output").GetProperty("edgeNeighbors"));
                Assert.HasCount(expectedEdgeCount, edgeNeighbors);
            }
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

        /// <summary>Loads <c>fixtures/traversal/global-neighbors.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/global-neighbors.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
