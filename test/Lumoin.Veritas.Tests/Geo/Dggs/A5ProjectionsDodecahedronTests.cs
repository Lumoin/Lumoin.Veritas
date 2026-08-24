using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>projections/fixtures/dodecahedron.json</c> for <see cref="DodecahedronProjection"/>:
    /// forward and inverse projections at strict |diff| &lt; 1e-13, for the fixture's pinned origin
    /// (<c>static.ORIGIN_ID</c>), plus both round-trip directions.
    /// </summary>
    [TestClass]
    internal sealed class A5ProjectionsDodecahedronTests
    {
        /// <summary>Bounds forward/inverse dodecahedron projection comparisons at strict |diff| &lt; 1e-13.</summary>
        private const double PrecisionArray13 = 1e-13;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that forward dodecahedron face projections match the fixture's expected values at the pinned origin.</summary>
        [TestMethod]
        public async Task ForwardProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int originId = ReadOriginId(fixture);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                Spherical input = ReadSpherical(testCase.GetProperty("input"));

                Face actual = DodecahedronProjection.Forward(input, originId);

                AssertFaceMatches(testCase.GetProperty("expected"), actual);
            }
        }

        /// <summary>Pins that forward dodecahedron projection followed by inverse projection round-trips back to the original spherical input.</summary>
        [TestMethod]
        public async Task ForwardThenInverseRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int originId = ReadOriginId(fixture);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                Spherical input = ReadSpherical(testCase.GetProperty("input"));

                Face face = DodecahedronProjection.Forward(input, originId);
                Spherical roundTripped = DodecahedronProjection.Inverse(face, originId);

                Assert.AreEqual(input.Theta, roundTripped.Theta, PrecisionArray13);
                Assert.AreEqual(input.Phi, roundTripped.Phi, PrecisionArray13);
            }
        }

        /// <summary>Pins that inverse dodecahedron face projections match the fixture's expected spherical values at the pinned origin.</summary>
        [TestMethod]
        public async Task InverseProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int originId = ReadOriginId(fixture);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                Face input = ReadFace(testCase.GetProperty("input"));

                Spherical actual = DodecahedronProjection.Inverse(input, originId);

                Assert.AreEqual(testCase.GetProperty("expected")[0].GetDouble(), actual.Theta, PrecisionArray13);
                Assert.AreEqual(testCase.GetProperty("expected")[1].GetDouble(), actual.Phi, PrecisionArray13);
            }
        }

        /// <summary>Pins that inverse dodecahedron projection followed by forward projection round-trips back to the original face input.</summary>
        [TestMethod]
        public async Task InverseThenForwardRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            int originId = ReadOriginId(fixture);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                Face input = ReadFace(testCase.GetProperty("input"));

                Spherical spherical = DodecahedronProjection.Inverse(input, originId);
                Face roundTripped = DodecahedronProjection.Forward(spherical, originId);

                AssertFaceMatches(testCase.GetProperty("input"), roundTripped);
            }
        }

        /// <summary>Reads the fixture's pinned <c>static.ORIGIN_ID</c>, which every case in the fixture uses.</summary>
        private static int ReadOriginId(JsonDocument fixture)
        {
            return fixture.RootElement.GetProperty("static").GetProperty("ORIGIN_ID").GetInt32();
        }

        /// <summary>Reads a fixture <c>[theta, phi]</c> pair into a <see cref="Spherical"/> value.</summary>
        private static Spherical ReadSpherical(JsonElement element)
        {
            return new Spherical(element[0].GetDouble(), element[1].GetDouble());
        }

        /// <summary>Reads a fixture <c>[x, y]</c> pair into a <see cref="Face"/> value.</summary>
        private static Face ReadFace(JsonElement element)
        {
            return new Face(element[0].GetDouble(), element[1].GetDouble());
        }

        /// <summary>Asserts a <see cref="Face"/> value matches a fixture <c>[x, y]</c> pair at the array tolerance.</summary>
        private static void AssertFaceMatches(JsonElement expected, Face actual)
        {
            Assert.AreEqual(expected[0].GetDouble(), actual.X, PrecisionArray13);
            Assert.AreEqual(expected[1].GetDouble(), actual.Y, PrecisionArray13);
        }

        /// <summary>Loads <c>projections/fixtures/dodecahedron.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "projections/fixtures/dodecahedron.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
