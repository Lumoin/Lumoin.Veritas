using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/tiling.json</c> for <see cref="Tiling"/>:
    /// <see cref="Tiling.GetPentagonVertices"/>, <see cref="Tiling.GetQuintantVertices"/> and
    /// <see cref="Tiling.GetFaceVertices"/> vertices/area/center at |diff| &lt; 0.5e-15, and
    /// <see cref="Tiling.GetQuintantPolar"/> quintant indices, exact.
    /// </summary>
    [TestClass]
    internal sealed class A5TilingTests
    {
        /// <summary>Bounds vertex/area/center comparisons against the tiling fixture at |diff| &lt; 0.5e-15.</summary>
        private const double Precision15 = 0.5e-15;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that pentagon vertices, area, and center match the fixture for every resolution/quintant/anchor case.</summary>
        [TestMethod]
        public async Task GetPentagonVerticesMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("getPentagonVertices").EnumerateArray())
            {
                JsonElement input = testCase.GetProperty("input");
                int resolution = input.GetProperty("resolution").GetInt32();
                int quintant = input.GetProperty("quintant").GetInt32();
                Anchor anchor = ReadAnchor(input.GetProperty("anchor"));

                PentagonShape pentagon = Tiling.GetPentagonVertices(resolution, quintant, anchor);

                AssertPentagonMatchesFixture(pentagon, testCase.GetProperty("output"));
            }
        }

        /// <summary>Pins that quintant triangle vertices, area, and center match the fixture for every quintant.</summary>
        [TestMethod]
        public async Task GetQuintantVerticesMatchesFixtureForEveryQuintant()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("getQuintantVertices").EnumerateArray())
            {
                int quintant = testCase.GetProperty("input").GetProperty("quintant").GetInt32();

                PentagonShape triangle = Tiling.GetQuintantVertices(quintant);

                AssertPentagonMatchesFixture(triangle, testCase.GetProperty("output"));
            }
        }

        /// <summary>Pins that the face vertices, area, and center match the fixture.</summary>
        [TestMethod]
        public async Task GetFaceVerticesMatchesFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            PentagonShape face = Tiling.GetFaceVertices();

            AssertPentagonMatchesFixture(face, fixture.RootElement.GetProperty("getFaceVertices"));
        }

        /// <summary>Pins that the quintant resolved from a polar coordinate matches the fixture's expected quintant index exactly.</summary>
        [TestMethod]
        public async Task GetQuintantPolarMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("getQuintantPolar").EnumerateArray())
            {
                JsonElement polarElement = testCase.GetProperty("input").GetProperty("polar");
                Polar polar = new(polarElement[0].GetDouble(), polarElement[1].GetDouble());

                int quintant = Tiling.GetQuintantPolar(polar);

                Assert.AreEqual(testCase.GetProperty("output").GetProperty("quintant").GetInt32(), quintant);
            }
        }

        /// <summary>Asserts a pentagon's vertices, area and center all match a fixture's <c>output</c> object.</summary>
        private static void AssertPentagonMatchesFixture(PentagonShape pentagon, JsonElement expected)
        {
            ReadOnlySpan<Face> vertices = pentagon.GetVertices();
            JsonElement expectedVertices = expected.GetProperty("vertices");

            Assert.HasCount(expectedVertices.GetArrayLength(), vertices);

            int index = 0;
            foreach(JsonElement expectedVertex in expectedVertices.EnumerateArray())
            {
                Assert.AreEqual(expectedVertex[0].GetDouble(), vertices[index].X, Precision15);
                Assert.AreEqual(expectedVertex[1].GetDouble(), vertices[index].Y, Precision15);
                index++;
            }

            Assert.AreEqual(expected.GetProperty("area").GetDouble(), pentagon.GetArea(), Precision15);

            Face center = pentagon.GetCenter();
            JsonElement expectedCenter = expected.GetProperty("center");
            Assert.AreEqual(expectedCenter[0].GetDouble(), center.X, Precision15);
            Assert.AreEqual(expectedCenter[1].GetDouble(), center.Y, Precision15);
        }

        /// <summary>Reads a fixture's <c>anchor</c> object (<c>offset</c>, <c>flips</c>, <c>q</c>) into an <see cref="Anchor"/>.</summary>
        private static Anchor ReadAnchor(JsonElement anchorElement)
        {
            JsonElement offset = anchorElement.GetProperty("offset");
            JsonElement flips = anchorElement.GetProperty("flips");

            return new Anchor(
                anchorElement.GetProperty("q").GetInt32(),
                new IJ(offset[0].GetDouble(), offset[1].GetDouble()),
                new FlipPair((Flip)flips[0].GetInt32(), (Flip)flips[1].GetInt32()));
        }

        /// <summary>Loads <c>fixtures/tiling.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/tiling.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
