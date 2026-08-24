using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against the eight <c>integration/wireframe-{0-3}.json</c> /
    /// <c>integration/wireframe-auto-edges-{0-3}.json</c> fixtures (the full resolution 0-3 surface, with
    /// 12/60/240/960 cells respectively) for <see cref="Cell.CellToBoundary"/>: segments=1 for the plain
    /// wireframe set, segments=0 (auto) for the auto-edges set. Coordinates are compared at |diff| &lt;
    /// 0.5e-6.
    /// </summary>
    [TestClass]
    internal sealed class A5CellBoundaryIntegrationTests
    {
        /// <summary>Bounds the longitude and latitude comparisons against the wireframe fixture coordinates.</summary>
        private const double CoordinateTolerance = 0.5e-6;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that resolution 0's twelve cells' segment-1 boundaries match the wireframe fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeResolutionZero()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-0.json", segments: 1, expectedFeatureCount: 12, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 1's sixty cells' segment-1 boundaries match the wireframe fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeResolutionOne()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-1.json", segments: 1, expectedFeatureCount: 60, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 2's two hundred forty cells' segment-1 boundaries match the wireframe fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeResolutionTwo()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-2.json", segments: 1, expectedFeatureCount: 240, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 3's nine hundred sixty cells' segment-1 boundaries match the wireframe fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeResolutionThree()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-3.json", segments: 1, expectedFeatureCount: 960, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 0's twelve cells' auto-segment boundaries match the wireframe auto-edges fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeAutoEdgesResolutionZero()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-auto-edges-0.json", segments: 0, expectedFeatureCount: 12, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 1's sixty cells' auto-segment boundaries match the wireframe auto-edges fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeAutoEdgesResolutionOne()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-auto-edges-1.json", segments: 0, expectedFeatureCount: 60, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 2's two hundred forty cells' auto-segment boundaries match the wireframe auto-edges fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeAutoEdgesResolutionTwo()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-auto-edges-2.json", segments: 0, expectedFeatureCount: 240, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Pins that resolution 3's nine hundred sixty cells' auto-segment boundaries match the wireframe auto-edges fixture.</summary>
        [TestMethod]
        public async Task CellToBoundaryMatchesWireframeAutoEdgesResolutionThree()
        {
            await AssertWireframeFixtureMatchesAsync("wireframe-auto-edges-3.json", segments: 0, expectedFeatureCount: 960, TestContext.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>Shared body: loads a wireframe fixture and checks every feature's boundary against <see cref="Cell.CellToBoundary"/>.</summary>
        private static async Task AssertWireframeFixtureMatchesAsync(string fileName, int segments, int expectedFeatureCount, CancellationToken cancellationToken)
        {
            using JsonDocument fixture = await LoadIntegrationFixtureAsync(fileName, cancellationToken).ConfigureAwait(false);
            JsonElement features = fixture.RootElement.GetProperty("features");

            Assert.AreEqual(expectedFeatureCount, features.GetArrayLength(), fileName);

            foreach(JsonElement feature in features.EnumerateArray())
            {
                string cellIdHex = feature.GetProperty("properties").GetProperty("cellIdHex").GetString()!;
                ulong cellId = Hex.HexToU64(cellIdHex);

                JsonElement expectedRing = feature.GetProperty("geometry").GetProperty("coordinates")[0];

                LonLat[] actualBoundary = Cell.CellToBoundary(cellId, closedRing: true, segments: segments);

                Assert.HasCount(expectedRing.GetArrayLength(), actualBoundary, $"{fileName} cell {cellIdHex}");

                for(int index = 0; index < actualBoundary.Length; index++)
                {
                    JsonElement expectedPoint = expectedRing[index];
                    Assert.AreEqual(expectedPoint[0].GetDouble(), actualBoundary[index].Longitude, CoordinateTolerance, $"{fileName} cell {cellIdHex} point {index} longitude");
                    Assert.AreEqual(expectedPoint[1].GetDouble(), actualBoundary[index].Latitude, CoordinateTolerance, $"{fileName} cell {cellIdHex} point {index} latitude");
                }
            }
        }

        /// <summary>Loads an <c>integration/*.json</c> fixture from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadIntegrationFixtureAsync(string fileName, CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", $"integration/{fileName}"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
