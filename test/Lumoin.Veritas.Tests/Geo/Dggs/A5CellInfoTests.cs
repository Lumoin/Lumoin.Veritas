using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/cell-info.json</c> for <see cref="CellInfo"/>: cell counts (both the
    /// <see cref="double"/>- and <see cref="BigInteger"/>-returning overloads of
    /// <see cref="CellInfo.GetNumCells(int)"/>/<see cref="CellInfo.GetNumCells(BigInteger)"/>), child
    /// counts, and cell areas. Every comparison here is exact — the fixture's own assertions use strict
    /// equality, with no tolerance.
    /// </summary>
    [TestClass]
    internal sealed class A5CellInfoTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that both the <see cref="double"/>- and <see cref="BigInteger"/>-returning overloads of <see cref="CellInfo.GetNumCells(int)"/> match the fixture at every resolution.</summary>
        [TestMethod]
        public async Task GetNumCellsMatchesFixtureForEveryResolution()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement entry in fixture.RootElement.GetProperty("numCells").EnumerateArray())
            {
                int resolution = entry.GetProperty("resolution").GetInt32();

                // The fixture's "count" field is the double overload's shortest round-trip decimal text
                // at resolution 28-30, not exact-integer text — deserialize it as a double, never as an
                // integer.
                double expectedCount = entry.GetProperty("count").GetDouble();
                Assert.AreEqual(expectedCount, CellInfo.GetNumCells(resolution));

                BigInteger expectedCountBigInteger = BigInteger.Parse(entry.GetProperty("countBigInt").GetString()!, CultureInfo.InvariantCulture);
                Assert.AreEqual(expectedCountBigInteger, CellInfo.GetNumCells((BigInteger)resolution));
            }
        }

        /// <summary>Pins that <see cref="CellInfo.GetNumChildren"/> matches the fixture for every parent-resolution/child-resolution pair.</summary>
        [TestMethod]
        public async Task GetNumChildrenMatchesFixtureForEveryParentChildPair()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement entry in fixture.RootElement.GetProperty("numChildren").EnumerateArray())
            {
                int parentResolution = entry.GetProperty("parentResolution").GetInt32();
                int childResolution = entry.GetProperty("childResolution").GetInt32();
                double expected = entry.GetProperty("numChildren").GetDouble();

                Assert.AreEqual(expected, CellInfo.GetNumChildren(parentResolution, childResolution));
            }
        }

        /// <summary>Pins that <see cref="CellInfo.CellArea"/> matches the fixture's area in square meters at every resolution.</summary>
        [TestMethod]
        public async Task CellAreaMatchesFixtureForEveryResolution()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement entry in fixture.RootElement.GetProperty("cellArea").EnumerateArray())
            {
                int resolution = entry.GetProperty("resolution").GetInt32();
                double expected = entry.GetProperty("areaM2").GetDouble();

                Assert.AreEqual(expected, CellInfo.CellArea(resolution));
            }
        }

        /// <summary>Loads <c>fixtures/cell-info.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/cell-info.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
