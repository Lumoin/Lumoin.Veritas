using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>geometry/fixtures/pentagon.json</c> for <see cref="PentagonShape"/>: area,
    /// center, point containment, the four in-place transforms, and edge splitting. Floating-point
    /// assertions (vertices, area, center) use |diff| &lt; 0.5e-6; booleans and vertex counts are exact.
    /// </summary>
    [TestClass]
    internal sealed class A5GeometryPentagonTests
    {
        /// <summary>Bounds the floating-point vertex, area, and center comparisons against the fixture.</summary>
        private const double Precision6 = 0.5e-6;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that point-containment matches the fixture's expected result for every pentagon and test point.</summary>
        [TestMethod]
        public async Task ContainsPointReturnsExpectedResultsForAllFixtures()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                foreach(JsonElement pointCase in testCase.GetProperty("containsPointTests").EnumerateArray())
                {
                    double actual = pentagon.ContainsPoint(ReadFace(pointCase.GetProperty("point")));

                    Assert.AreEqual(pointCase.GetProperty("result").GetDouble(), actual, Precision6);
                }
            }
        }

        /// <summary>Pins that the computed area matches the fixture's expected area for every pentagon.</summary>
        [TestMethod]
        public async Task GetAreaReturnsExpectedAreaForAllPentagons()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                double area = pentagon.GetArea();

                Assert.AreEqual(testCase.GetProperty("area").GetDouble(), area, Precision6);
            }
        }

        /// <summary>Pins that the computed center matches the fixture's expected center for every pentagon.</summary>
        [TestMethod]
        public async Task GetCenterReturnsExpectedCenterForAllPentagons()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                Face center = pentagon.GetCenter();
                JsonElement expected = testCase.GetProperty("center");

                Assert.AreEqual(expected[0].GetDouble(), center.X, Precision6);
                Assert.AreEqual(expected[1].GetDouble(), center.Y, Precision6);
            }
        }

        /// <summary>Pins that scaling a pentagon by a factor of 2 in place matches the fixture's scale-transform vertices.</summary>
        [TestMethod]
        public async Task ScaleTransformationMatchesFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                PentagonShape scaled = pentagon.Clone().Scale(2);

                AssertVerticesMatch(testCase.GetProperty("transformTests").GetProperty("scale"), scaled.GetVertices());
            }
        }

        /// <summary>Pins that rotating a pentagon 180 degrees in place matches the fixture's rotate180-transform vertices.</summary>
        [TestMethod]
        public async Task Rotate180TransformationMatchesFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                PentagonShape rotated = pentagon.Clone().Rotate180();

                AssertVerticesMatch(testCase.GetProperty("transformTests").GetProperty("rotate180"), rotated.GetVertices());
            }
        }

        /// <summary>Pins that reflecting a pentagon across the Y axis in place matches the fixture's reflectY-transform vertices.</summary>
        [TestMethod]
        public async Task ReflectYTransformationMatchesFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                PentagonShape reflected = pentagon.Clone().ReflectY();

                AssertVerticesMatch(testCase.GetProperty("transformTests").GetProperty("reflectY"), reflected.GetVertices());
            }
        }

        /// <summary>Pins that translating a pentagon by (1, 1) in place matches the fixture's translate-transform vertices.</summary>
        [TestMethod]
        public async Task TranslateTransformationMatchesFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));

                PentagonShape translated = pentagon.Clone().Translate(new Vector2d(1, 1));

                AssertVerticesMatch(testCase.GetProperty("transformTests").GetProperty("translate"), translated.GetVertices());
            }
        }

        /// <summary>Pins that splitting each edge into 2 or 3 segments produces the fixture's expected vertex count and positions.</summary>
        [TestMethod]
        public async Task SplitEdgesReturnsSplitVerticesForVariousSegmentCounts()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.EnumerateArray())
            {
                PentagonShape pentagon = new(ReadFaces(testCase.GetProperty("vertices")));
                JsonElement splitEdgesTests = testCase.GetProperty("splitEdgesTests");

                foreach(int segments in new[] { 2, 3 })
                {
                    PentagonShape split = pentagon.Clone().SplitEdges(segments);
                    JsonElement expectedVertices = splitEdgesTests.GetProperty($"segments{segments}");

                    Assert.HasCount(expectedVertices.GetArrayLength(), split.GetVertices());
                    AssertVerticesMatch(expectedVertices, split.GetVertices());
                }
            }
        }

        /// <summary>Reads a JSON array of <c>[x, y]</c> pairs into a <see cref="Face"/> array.</summary>
        private static Face[] ReadFaces(JsonElement verticesElement)
        {
            Face[] faces = new Face[verticesElement.GetArrayLength()];
            int index = 0;
            foreach(JsonElement vertexElement in verticesElement.EnumerateArray())
            {
                faces[index] = ReadFace(vertexElement);
                index++;
            }

            return faces;
        }

        /// <summary>Reads a JSON <c>[x, y]</c> pair into a <see cref="Face"/>.</summary>
        private static Face ReadFace(JsonElement vertexElement)
        {
            return new Face(vertexElement[0].GetDouble(), vertexElement[1].GetDouble());
        }

        /// <summary>Asserts that every vertex in <paramref name="actual"/> matches the corresponding fixture entry.</summary>
        private static void AssertVerticesMatch(JsonElement expectedVertices, ReadOnlySpan<Face> actual)
        {
            int index = 0;
            foreach(JsonElement expectedVertex in expectedVertices.EnumerateArray())
            {
                Assert.AreEqual(expectedVertex[0].GetDouble(), actual[index].X, Precision6);
                Assert.AreEqual(expectedVertex[1].GetDouble(), actual[index].Y, Precision6);
                index++;
            }
        }

        /// <summary>Loads <c>geometry/fixtures/pentagon.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "geometry/fixtures/pentagon.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
