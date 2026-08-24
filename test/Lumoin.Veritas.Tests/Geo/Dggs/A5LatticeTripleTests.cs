using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/lattice/triple.json</c> for the triangular-grid triple coordinate
    /// system: anchor-to-triple conversion, parity, the triple-to-Hilbert-index round trip, and the
    /// triple-to-anchor inverse checked against the fixture's own <c>sToAnchor</c> result. All
    /// assertions are exact integer/enum equality — no tolerances.
    /// </summary>
    [TestClass]
    internal sealed class A5LatticeTripleTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="TripleCoordinates.AnchorToTriple"/> matches the fixture's X, Y, Z triple for every case.</summary>
        [TestMethod]
        public async Task AnchorToTripleProducesCorrectTripleCoordinates()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("anchorToTriple").EnumerateArray())
            {
                Anchor anchor = ReadAnchorFromSToAnchorCase(testCase);

                Triple triple = TripleCoordinates.AnchorToTriple(anchor);

                Assert.AreEqual(testCase.GetProperty("x").GetInt32(), triple.X);
                Assert.AreEqual(testCase.GetProperty("y").GetInt32(), triple.Y);
                Assert.AreEqual(testCase.GetProperty("z").GetInt32(), triple.Z);
            }
        }

        /// <summary>Pins that <see cref="TripleCoordinates.TripleParity"/> matches the fixture's expected parity for every case.</summary>
        [TestMethod]
        public async Task TripleParityReturnsCorrectParity()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("anchorToTriple").EnumerateArray())
            {
                Triple triple = new(
                    testCase.GetProperty("x").GetInt32(),
                    testCase.GetProperty("y").GetInt32(),
                    testCase.GetProperty("z").GetInt32());

                int parity = TripleCoordinates.TripleParity(triple);

                Assert.AreEqual(testCase.GetProperty("parity").GetInt32(), parity);
            }
        }

        /// <summary>Pins that <see cref="TripleCoordinates.TripleToS"/> returns a non-null value matching the fixture's original <c>s</c> for every case.</summary>
        [TestMethod]
        public async Task TripleToSRoundTripsBackToTheOriginalSValue()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("anchorToTriple").EnumerateArray())
            {
                Triple triple = new(
                    testCase.GetProperty("x").GetInt32(),
                    testCase.GetProperty("y").GetInt32(),
                    testCase.GetProperty("z").GetInt32());
                int resolution = testCase.GetProperty("resolution").GetInt32();
                Orientation orientation = ParseOrientation(testCase.GetProperty("orientation").GetString());

                ulong? s = TripleCoordinates.TripleToS(triple, resolution, orientation);

                Assert.IsNotNull(s);
                Assert.AreEqual(testCase.GetProperty("s").GetUInt64(), s.Value);
            }
        }

        /// <summary>Pins that <see cref="TripleCoordinates.TripleToAnchor"/> returns a non-null anchor matching the anchor produced independently by <see cref="HilbertCurve.SToAnchor"/> for every case.</summary>
        [TestMethod]
        public async Task TripleToAnchorProducesAnAnchorMatchingSToAnchor()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("anchorToTriple").EnumerateArray())
            {
                Triple triple = new(
                    testCase.GetProperty("x").GetInt32(),
                    testCase.GetProperty("y").GetInt32(),
                    testCase.GetProperty("z").GetInt32());
                int resolution = testCase.GetProperty("resolution").GetInt32();
                Orientation orientation = ParseOrientation(testCase.GetProperty("orientation").GetString());

                Anchor expected = ReadAnchorFromSToAnchorCase(testCase);
                Anchor? actual = TripleCoordinates.TripleToAnchor(triple, resolution, orientation);

                Assert.IsNotNull(actual);
                Assert.AreEqual(expected.Offset.I, actual.Value.Offset.I);
                Assert.AreEqual(expected.Offset.J, actual.Value.Offset.J);
                Assert.AreEqual(expected.Flips.FlipX, actual.Value.Flips.FlipX);
                Assert.AreEqual(expected.Flips.FlipY, actual.Value.Flips.FlipY);
            }
        }

        /// <summary>Pins that <see cref="TripleCoordinates.TripleInBounds"/> matches the fixture's expected boolean for every case.</summary>
        [TestMethod]
        public async Task TripleInBoundsValidatesQuintantBoundsCorrectly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("tripleInBounds").EnumerateArray())
            {
                Triple triple = new(
                    testCase.GetProperty("x").GetInt32(),
                    testCase.GetProperty("y").GetInt32(),
                    testCase.GetProperty("z").GetInt32());
                int maxRow = testCase.GetProperty("maxRow").GetInt32();

                bool inBounds = TripleCoordinates.TripleInBounds(triple, maxRow);

                Assert.AreEqual(testCase.GetProperty("expected").GetBoolean(), inBounds);
            }
        }

        /// <summary>
        /// Computes the expected anchor for an <c>anchorToTriple</c> fixture row by running
        /// <see cref="HilbertCurve.SToAnchor"/> on the row's own <c>s</c>/<c>resolution</c>/<c>orientation</c>
        /// fields.
        /// </summary>
        private static Anchor ReadAnchorFromSToAnchorCase(JsonElement testCase)
        {
            ulong s = testCase.GetProperty("s").GetUInt64();
            int resolution = testCase.GetProperty("resolution").GetInt32();
            Orientation orientation = ParseOrientation(testCase.GetProperty("orientation").GetString());

            return HilbertCurve.SToAnchor(s, resolution, orientation);
        }

        /// <summary>Parses a fixture orientation string (e.g. <c>"wv"</c>) into an <see cref="Orientation"/> value.</summary>
        private static Orientation ParseOrientation(string? orientation)
        {
            return orientation switch
            {
                "uv" => Orientation.UV,
                "vu" => Orientation.VU,
                "uw" => Orientation.UW,
                "wu" => Orientation.WU,
                "vw" => Orientation.VW,
                "wv" => Orientation.WV,
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown fixture orientation value."),
            };
        }

        /// <summary>Loads <c>fixtures/lattice/triple.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/lattice/triple.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
