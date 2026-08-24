using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/geometry/spherical-polygon-primitives.json</c> for the free
    /// functions <see cref="SphericalPolygonPrimitives.PointInSphericalPolygon"/> and
    /// <see cref="SphericalPolygonPrimitives.RingWindingSign"/>. All assertions are exact booleans or
    /// integer winding signs — no tolerances.
    /// </summary>
    [TestClass]
    internal sealed class A5SphericalPolygonPrimitivesTests
    {
        /// <summary>The factor that converts degrees to radians for the fixture's longitude/latitude convention.</summary>
        private const double DegreesToRadians = Math.PI / 180;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that point-in-spherical-polygon containment matches the fixture's expected boolean for every point of every ring.</summary>
        [TestMethod]
        public async Task PointInSphericalPolygonMatchesFixtureForEveryRing()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("pointInSphericalPolygon").EnumerateArray())
            {
                Cartesian[] ring = ReadRing(testCase.GetProperty("ring"));

                foreach(JsonElement pointCase in testCase.GetProperty("points").EnumerateArray())
                {
                    Cartesian point = ReadCartesian(pointCase.GetProperty("vec"));

                    bool actual = SphericalPolygonPrimitives.PointInSphericalPolygon(point, ring);

                    Assert.AreEqual(pointCase.GetProperty("inside").GetBoolean(), actual);
                }
            }
        }

        /// <summary>Pins that the computed ring winding sign matches the fixture's expected integer for every ring.</summary>
        [TestMethod]
        public async Task RingWindingSignMatchesFixtureForEveryRing()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("ringWindingSign").EnumerateArray())
            {
                Cartesian[] ring = ReadRing(testCase.GetProperty("ring"));

                int actual = SphericalPolygonPrimitives.RingWindingSign(ring);

                Assert.AreEqual(testCase.GetProperty("sign").GetInt32(), actual);
            }
        }

        /// <summary>Reads a JSON array of <c>[longitude, latitude]</c> degree pairs into unit Cartesian vectors.</summary>
        private static Cartesian[] ReadRing(JsonElement ringElement)
        {
            Cartesian[] ring = new Cartesian[ringElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement pointElement in ringElement.EnumerateArray())
            {
                ring[index] = LonLatDegreesToCartesian(pointElement[0].GetDouble(), pointElement[1].GetDouble());
                index++;
            }

            return ring;
        }

        /// <summary>Converts longitude/latitude in degrees to a unit Cartesian vector, matching the fixture's own convention.</summary>
        private static Cartesian LonLatDegreesToCartesian(double longitudeDegrees, double latitudeDegrees)
        {
            double latitude = latitudeDegrees * DegreesToRadians;
            double longitude = longitudeDegrees * DegreesToRadians;
            double cosLatitude = Math.Cos(latitude);

            return new Cartesian(cosLatitude * Math.Cos(longitude), cosLatitude * Math.Sin(longitude), Math.Sin(latitude));
        }

        /// <summary>Reads a JSON <c>[x, y, z]</c> triple into a <see cref="Cartesian"/>.</summary>
        private static Cartesian ReadCartesian(JsonElement vectorElement)
        {
            return new Cartesian(vectorElement[0].GetDouble(), vectorElement[1].GetDouble(), vectorElement[2].GetDouble());
        }

        /// <summary>Loads <c>fixtures/geometry/spherical-polygon-primitives.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/geometry/spherical-polygon-primitives.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
