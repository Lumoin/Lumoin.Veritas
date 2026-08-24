using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Traversal;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/traversal/grid-disk.json</c> (42 rows) for
    /// <see cref="GridDisk.GetGridDisk"/> and <see cref="GridDisk.GetGridDiskVertex"/>: each disk is
    /// expanded back to the center cell's resolution via <see cref="Compaction.Uncompact"/> and compared
    /// as exact hex arrays in unsigned-ascending order. The vertex disk's expectation is the edge-disk
    /// cells plus the row's extra vertex-only cells.
    /// </summary>
    [TestClass]
    internal sealed class A5GridDiskTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="GridDisk.GetGridDisk"/>, uncompacted to the center's resolution and sorted, matches the fixture's cell set for every row.</summary>
        [TestMethod]
        public async Task GetGridDiskMatchesFixtureForEveryRow()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("cellId").GetString()!);
                int k = testCase.GetProperty("k").GetInt32();
                int targetResolution = Serialization.GetResolution(cellId);

                ulong[] disk = Compaction.Uncompact(GridDisk.GetGridDisk(cellId, k), targetResolution);

                string[] expected = ToSortedHex(ReadHexArray(testCase.GetProperty("cells")));
                Assert.AreSequenceEqual(expected, ToSortedHex(disk));
            }
        }

        /// <summary>Pins that <see cref="GridDisk.GetGridDiskVertex"/>, uncompacted and sorted, matches the fixture's cell set plus its extra vertex-only cells for every row.</summary>
        [TestMethod]
        public async Task GetGridDiskVertexMatchesFixtureForEveryRow()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                ulong cellId = Hex.HexToU64(testCase.GetProperty("cellId").GetString()!);
                int k = testCase.GetProperty("k").GetInt32();
                int targetResolution = Serialization.GetResolution(cellId);

                ulong[] disk = Compaction.Uncompact(GridDisk.GetGridDiskVertex(cellId, k), targetResolution);

                ulong[] expectedCells = [.. ReadHexArray(testCase.GetProperty("cells")), .. ReadHexArray(testCase.GetProperty("extraVertexCells"))];
                Assert.AreSequenceEqual(ToSortedHex(expectedCells), ToSortedHex(disk));
            }
        }

        /// <summary>Pins that both <see cref="GridDisk.GetGridDisk"/> and <see cref="GridDisk.GetGridDiskVertex"/> return only the center cell when k is zero.</summary>
        [TestMethod]
        public async Task GetGridDiskReturnsOnlyCenterCellForZeroHops()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            ulong cellId = Hex.HexToU64(fixture.RootElement[0].GetProperty("cellId").GetString()!);

            Assert.AreSequenceEqual(new[] { cellId }, GridDisk.GetGridDisk(cellId, 0));
            Assert.AreSequenceEqual(new[] { cellId }, GridDisk.GetGridDiskVertex(cellId, 0));
        }

        /// <summary>Sorts cell ids unsigned-ascending and converts them to hex.</summary>
        private static string[] ToSortedHex(ulong[] cellIds)
        {
            ulong[] sorted = (ulong[])cellIds.Clone();
            Array.Sort(sorted);

            string[] hex = new string[sorted.Length];
            for(int index = 0; index < sorted.Length; index++)
            {
                hex[index] = Hex.U64ToHex(sorted[index]);
            }

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

        /// <summary>Loads <c>fixtures/traversal/grid-disk.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/traversal/grid-disk.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
