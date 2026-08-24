using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/lattice/hilbert.json</c> for both directions of the Hilbert curve
    /// index conversion: <c>s</c>-value to <see cref="Anchor"/> and back. All assertions are exact
    /// integer/enum equality — no tolerances.
    /// </summary>
    [TestClass]
    internal sealed class A5LatticeHilbertTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that converting an <c>s</c> value to an anchor matches the fixture's expected Q, offset, and flips.</summary>
        [TestMethod]
        public async Task SToAnchorProducesCorrectAnchorForAllCases()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("sToAnchor").EnumerateArray())
            {
                ulong s = testCase.GetProperty("s").GetUInt64();
                int resolution = testCase.GetProperty("resolution").GetInt32();
                Orientation orientation = ParseOrientation(testCase.GetProperty("orientation").GetString());

                Anchor anchor = HilbertCurve.SToAnchor(s, resolution, orientation);

                JsonElement offset = testCase.GetProperty("offset");
                JsonElement flips = testCase.GetProperty("flips");

                Assert.AreEqual(testCase.GetProperty("q").GetInt32(), anchor.Q);
                Assert.AreEqual(offset[0].GetDouble(), anchor.Offset.I);
                Assert.AreEqual(offset[1].GetDouble(), anchor.Offset.J);
                Assert.AreEqual((Flip)flips[0].GetInt32(), anchor.Flips.FlipX);
                Assert.AreEqual((Flip)flips[1].GetInt32(), anchor.Flips.FlipY);
            }
        }

        /// <summary>Pins that converting an anchor built from the fixture's own <c>sToAnchor</c> data back to an <c>s</c> value round-trips to the original.</summary>
        [TestMethod]
        public async Task AnchorToSRoundTripsBackToTheOriginalSValue()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("sToAnchor").EnumerateArray())
            {
                int resolution = testCase.GetProperty("resolution").GetInt32();
                Orientation orientation = ParseOrientation(testCase.GetProperty("orientation").GetString());

                JsonElement offset = testCase.GetProperty("offset");
                JsonElement flips = testCase.GetProperty("flips");
                Anchor anchor = new(
                    testCase.GetProperty("q").GetInt32(),
                    new IJ(offset[0].GetDouble(), offset[1].GetDouble()),
                    new FlipPair((Flip)flips[0].GetInt32(), (Flip)flips[1].GetInt32()));

                ulong s = HilbertCurve.AnchorToS(anchor, resolution, orientation);

                Assert.AreEqual(testCase.GetProperty("s").GetUInt64(), s);
            }
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

        /// <summary>Loads <c>fixtures/lattice/hilbert.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/lattice/hilbert.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
