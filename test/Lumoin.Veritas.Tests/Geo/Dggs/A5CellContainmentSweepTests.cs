using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Property sweep against <c>data/ne_50m_populated_places_nameonly.json</c> (1249 named places): for
    /// every place at every resolution 1 through 29, <see cref="Cell.LonLatToCell"/> followed by
    /// <see cref="Cell.A5CellContainsPoint"/> must find the place's own point inside the cell it was
    /// assigned to (resolution 30 is excluded; it is not exercised at that resolution by this sweep). A
    /// single, whole-corpus test method with an inner loop.
    /// </summary>
    [TestClass]
    internal sealed class A5CellContainmentSweepTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that for every place at every resolution 1 through 29, the cell assigned by <see cref="Cell.LonLatToCell"/> contains that place's own point.</summary>
        [TestMethod]
        public async Task LonLatToCellContainsEveryPlaceAtEveryResolution()
        {
            (string Name, LonLat Point)[] places = await LoadPopulatedPlacesAsync(TestContext.CancellationToken).ConfigureAwait(false);

            List<string> failures = [];
            int caseCount = 0;

            foreach((string name, LonLat point) in places)
            {
                Spherical spherical = CoordinateTransforms.FromLonLat(point);

                for(int resolution = 1; resolution < Serialization.MaxResolution; resolution++)
                {
                    caseCount++;

                    ulong cellId = Cell.LonLatToCell(point, resolution);
                    A5Cell cell = Serialization.Deserialize(cellId);

                    // A strictly negative containment distance is a failure; zero (an exact-boundary
                    // point) is not.
                    if(Cell.A5CellContainsPoint(cell, spherical) < 0)
                    {
                        failures.Add($"{name} ({point.Longitude}, {point.Latitude}) at resolution {resolution}: cell {Hex.U64ToHex(cellId)} does not contain the point.");
                    }
                }
            }

            TestContext.WriteLine($"Swept {places.Length} places x {Serialization.MaxResolution - 1} resolutions = {caseCount} cases.");
            Assert.HasCount(0, failures, string.Join(Environment.NewLine, failures));
        }

        /// <summary>Loads every named place from the populated-places fixture as a (name, point) pair.</summary>
        private static async Task<(string Name, LonLat Point)[]> LoadPopulatedPlacesAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "data/ne_50m_populated_places_nameonly.json"));
            using JsonDocument fixture = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            JsonElement features = fixture.RootElement.GetProperty("features");
            (string Name, LonLat Point)[] places = new (string Name, LonLat Point)[features.GetArrayLength()];

            int index = 0;
            foreach(JsonElement feature in features.EnumerateArray())
            {
                string name = feature.GetProperty("properties").GetProperty("name").GetString() ?? $"Unnamed {index}";
                JsonElement coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
                places[index] = (name, new LonLat(coordinates[0].GetDouble(), coordinates[1].GetDouble()));
                index++;
            }

            return places;
        }
    }
}
