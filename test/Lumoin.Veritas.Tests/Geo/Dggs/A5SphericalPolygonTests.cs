using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>geometry/fixtures/spherical-polygon.json</c> for
    /// <see cref="SphericalPolygonShape"/>, plus the inline degenerate-polygon cases. Floating-point
    /// assertions use |diff| &lt; 0.5e-6.
    /// </summary>
    [TestClass]
    internal sealed class A5SphericalPolygonTests
    {
        /// <summary>Bounds floating-point comparisons against the spherical polygon fixture at |diff| &lt; 0.5e-6.</summary>
        private const double Precision6 = 0.5e-6;

        /// <summary>Bounds how far a slerp-interpolated vector's length may drift from unit length.</summary>
        private const double NormalizationTolerance = 1e-10;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the polygon boundary matches the fixture's expected points at segment counts 1, 2, and 3.</summary>
        [TestMethod]
        public async Task GetBoundaryReturnsExpectedPointsForVariousSegmentCounts()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                SphericalPolygonShape polygon = new(ReadCartesians(testCase.GetProperty("vertices")));

                foreach(int segments in new[] { 1, 2, 3 })
                {
                    Cartesian[] boundary = polygon.GetBoundary(segments, true);
                    JsonElement expectedBoundary = testCase.GetProperty($"boundary{segments}");

                    Assert.HasCount(expectedBoundary.GetArrayLength(), boundary);
                    AssertCartesiansMatch(expectedBoundary, boundary);
                }
            }
        }

        /// <summary>Pins that spherical-linear interpolation between polygon vertices matches the fixture and stays unit-length.</summary>
        [TestMethod]
        public async Task SlerpInterpolatesBetweenVertices()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                SphericalPolygonShape polygon = new(ReadCartesians(testCase.GetProperty("vertices")));

                foreach(JsonElement slerpCase in testCase.GetProperty("slerpTests").EnumerateArray())
                {
                    Cartesian actual = polygon.Slerp(slerpCase.GetProperty("t").GetDouble());
                    JsonElement expected = slerpCase.GetProperty("result");

                    Assert.AreEqual(expected[0].GetDouble(), actual.X, Precision6);
                    Assert.AreEqual(expected[1].GetDouble(), actual.Y, Precision6);
                    Assert.AreEqual(expected[2].GetDouble(), actual.Z, Precision6);

                    double length = Math.Sqrt((actual.X * actual.X) + (actual.Y * actual.Y) + (actual.Z * actual.Z));
                    Assert.AreEqual(1, length, NormalizationTolerance);
                }
            }
        }

        /// <summary>Pins that point containment correctly identifies inside and outside points against the fixture.</summary>
        [TestMethod]
        public async Task ContainsPointIdentifiesInsideAndOutsidePoints()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                SphericalPolygonShape polygon = new(ReadCartesians(testCase.GetProperty("vertices")));

                foreach(JsonElement pointCase in testCase.GetProperty("containsPointTests").EnumerateArray())
                {
                    double actual = polygon.ContainsPoint(ReadCartesian(pointCase.GetProperty("point")));

                    Assert.AreEqual(pointCase.GetProperty("result").GetDouble(), actual, Precision6);
                }
            }
        }

        /// <summary>Pins that the computed spherical area matches the fixture and stays within (0, 2*pi] in magnitude.</summary>
        [TestMethod]
        public async Task GetAreaReturnsExpectedAreaForAllPolygons()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                SphericalPolygonShape polygon = new(ReadCartesians(testCase.GetProperty("vertices")));

                double area = polygon.GetArea();

                Assert.AreEqual(testCase.GetProperty("area").GetDouble(), area, Precision6);
                Assert.IsGreaterThan(0, Math.Abs(area));
                Assert.IsLessThanOrEqualTo(2 * Math.PI, Math.Abs(area));
            }
        }

        /// <summary>Pins that a polygon with zero, one, or two vertices has zero area.</summary>
        [TestMethod]
        public void GetAreaReturnsZeroForDegeneratePolygons()
        {
            Assert.AreEqual(0, new SphericalPolygonShape([]).GetArea());
            Assert.AreEqual(0, new SphericalPolygonShape([new Cartesian(1, 0, 0)]).GetArea());
            Assert.AreEqual(0, new SphericalPolygonShape([new Cartesian(1, 0, 0), new Cartesian(0, 1, 0)]).GetArea());
        }

        /// <summary>Reads a JSON array of <c>[x, y, z]</c> triples into a <see cref="Cartesian"/> array.</summary>
        private static Cartesian[] ReadCartesians(JsonElement verticesElement)
        {
            Cartesian[] vertices = new Cartesian[verticesElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement vertexElement in verticesElement.EnumerateArray())
            {
                vertices[index] = ReadCartesian(vertexElement);
                index++;
            }

            return vertices;
        }

        /// <summary>Reads a JSON <c>[x, y, z]</c> triple into a <see cref="Cartesian"/>.</summary>
        private static Cartesian ReadCartesian(JsonElement vertexElement)
        {
            return new Cartesian(vertexElement[0].GetDouble(), vertexElement[1].GetDouble(), vertexElement[2].GetDouble());
        }

        /// <summary>Asserts that every point in <paramref name="actual"/> matches the corresponding fixture entry.</summary>
        private static void AssertCartesiansMatch(JsonElement expectedPoints, Cartesian[] actual)
        {
            int index = 0;
            foreach(JsonElement expectedPoint in expectedPoints.EnumerateArray())
            {
                Assert.AreEqual(expectedPoint[0].GetDouble(), actual[index].X, Precision6);
                Assert.AreEqual(expectedPoint[1].GetDouble(), actual[index].Y, Precision6);
                Assert.AreEqual(expectedPoint[2].GetDouble(), actual[index].Z, Precision6);
                index++;
            }
        }

        /// <summary>Loads <c>geometry/fixtures/spherical-polygon.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "geometry/fixtures/spherical-polygon.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
