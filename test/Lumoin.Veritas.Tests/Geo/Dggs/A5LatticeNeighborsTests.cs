using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/lattice-neighbors.json</c> (108 cases) for
    /// <see cref="LatticeNeighbors.GetLatticeNeighbors"/>: <see cref="LatticeNeighbors"/>'s own output is
    /// unsorted and undeduped by design, so both the <c>edgeOnlyNeighbors</c> and
    /// <c>supersetNeighbors</c> fixture fields — themselves recorded in ordinal string order over the
    /// hex-encoded ids — are compared against the hex-encoded result sorted the same way; both are EXACT
    /// arrays, order-sensitive after that sort. Lattice-boundary neighbor finding has no standalone
    /// fixture of its own — its behavior is pinned entirely through this fixture and
    /// <c>fixtures/traversal/global-neighbors.json</c>.
    /// </summary>
    [TestClass]
    internal sealed class A5LatticeNeighborsTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="LatticeNeighbors.GetLatticeNeighbors"/>'s edge-only and superset results, sorted ordinally, match the fixture for every case.</summary>
        [TestMethod]
        public async Task GetLatticeNeighborsMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
            {
                ulong cell = Hex.HexToU64(testCase.GetProperty("cell").GetString()!);

                string[] edgeOnly = ToSortedHex(LatticeNeighbors.GetLatticeNeighbors(cell, true));
                string[] expectedEdgeOnly = ReadStringArray(testCase.GetProperty("edgeOnlyNeighbors"));
                Assert.AreSequenceEqual(expectedEdgeOnly, edgeOnly);

                string[] superset = ToSortedHex(LatticeNeighbors.GetLatticeNeighbors(cell, false));
                string[] expectedSuperset = ReadStringArray(testCase.GetProperty("supersetNeighbors"));
                Assert.AreSequenceEqual(expectedSuperset, superset);
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

        /// <summary>Loads <c>fixtures/traversal/lattice-neighbors.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/lattice-neighbors.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
