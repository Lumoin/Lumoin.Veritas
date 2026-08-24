using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/crs-vertices.json</c> (62 unit vectors) for <see cref="Crs"/>: every
    /// vertex at |diff| &lt; 1e-13, and every vertex's magnitude at |diff| &lt; 0.5e-15.
    /// </summary>
    [TestClass]
    internal sealed class A5ProjectionsCrsTests
    {
        /// <summary>Bounds per-vertex comparisons against the CRS vertex fixture at |diff| &lt; 1e-13.</summary>
        private const double PrecisionArray13 = 1e-13;

        /// <summary>Bounds each vertex's magnitude comparison against unit length at |diff| &lt; 0.5e-15.</summary>
        private const double Precision15 = 0.5e-15;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that Crs.Vertices contains exactly sixty-two vertices.</summary>
        [TestMethod]
        public void HasExactlySixtyTwoVertices()
        {
            Assert.HasCount(62, Crs.Vertices);
        }

        /// <summary>Pins that every vertex in Crs.Vertices matches the fixture's expected coordinates field for field.</summary>
        [TestMethod]
        public async Task VerticesMatchExpectedFixtureFieldForField()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(fixture.RootElement.GetArrayLength(), Crs.Vertices);

            int index = 0;
            foreach(JsonElement expected in fixture.RootElement.EnumerateArray())
            {
                Cartesian actual = Crs.Vertices[index];

                Assert.AreEqual(expected[0].GetDouble(), actual.X, PrecisionArray13);
                Assert.AreEqual(expected[1].GetDouble(), actual.Y, PrecisionArray13);
                Assert.AreEqual(expected[2].GetDouble(), actual.Z, PrecisionArray13);

                index++;
            }
        }

        /// <summary>Pins that every vertex in Crs.Vertices has unit magnitude.</summary>
        [TestMethod]
        public void EveryVertexIsAUnitVector()
        {
            foreach(Cartesian vertex in Crs.Vertices)
            {
                double magnitude = Math.Sqrt((vertex.X * vertex.X) + (vertex.Y * vertex.Y) + (vertex.Z * vertex.Z));

                Assert.AreEqual(1.0, magnitude, Precision15);
            }
        }

        /// <summary>Pins that Crs.GetVertex throws InvalidOperationException for a point not present in the vertex table.</summary>
        [TestMethod]
        public void GetVertexThrowsForAPointNotInTheTable()
        {
            Assert.ThrowsExactly<InvalidOperationException>(static () => Crs.GetVertex(new Cartesian(1, 0, 0)));
        }

        /// <summary>Pins that Crs.GetVertex returns the exact table vertex matching a point already in the table.</summary>
        [TestMethod]
        public void GetVertexReturnsTheMatchingTableVertex()
        {
            Cartesian expected = Crs.Vertices[41];

            Cartesian actual = Crs.GetVertex(expected);

            Assert.AreEqual(expected, actual);
        }

        /// <summary>Pins that Crs.GetCanonicalTriangle returns the center, midpoint, and corner vertices in that order.</summary>
        [TestMethod]
        public void GetCanonicalTriangleReturnsTheCenterMidpointCornerTripleInThatOrder()
        {
            SphericalTriangle triangle = Crs.GetCanonicalTriangle();

            Assert.AreEqual(Crs.Vertices[0], triangle.A);
            Assert.AreEqual(Crs.Vertices[32], triangle.B);
            Assert.AreEqual(Crs.Vertices[12], triangle.C);
        }

        /// <summary>Loads <c>fixtures/crs-vertices.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/crs-vertices.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
