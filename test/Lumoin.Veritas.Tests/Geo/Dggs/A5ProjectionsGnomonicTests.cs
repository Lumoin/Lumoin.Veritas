using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>projections/fixtures/gnomonic.json</c> for <see cref="GnomonicProjection"/>.
    /// All array comparisons use the strict |diff| &lt; 1e-13 tolerance (not halved).
    /// </summary>
    [TestClass]
    internal sealed class A5ProjectionsGnomonicTests
    {
        /// <summary>Bounds forward/inverse gnomonic projection comparisons at strict |diff| &lt; 1e-13.</summary>
        private const double Precision13 = 1e-13;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that forward gnomonic polar projections match the fixture's expected values.</summary>
        [TestMethod]
        public async Task ForwardProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                Spherical input = ReadSpherical(testCase.GetProperty("input"));

                Polar actual = GnomonicProjection.Forward(input);

                JsonElement expected = testCase.GetProperty("expected");
                Assert.AreEqual(expected[0].GetDouble(), actual.Rho, Precision13);
                Assert.AreEqual(expected[1].GetDouble(), actual.Gamma, Precision13);
            }
        }

        /// <summary>Pins that forward gnomonic projection followed by inverse projection round-trips back to the original spherical input.</summary>
        [TestMethod]
        public async Task ForwardThenInverseRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                Spherical input = ReadSpherical(testCase.GetProperty("input"));

                Polar polar = GnomonicProjection.Forward(input);
                Spherical roundTripped = GnomonicProjection.Inverse(polar);

                Assert.AreEqual(input.Theta, roundTripped.Theta, Precision13);
                Assert.AreEqual(input.Phi, roundTripped.Phi, Precision13);
            }
        }

        /// <summary>Pins that inverse gnomonic spherical projections match the fixture's expected values.</summary>
        [TestMethod]
        public async Task InverseProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                Polar input = ReadPolar(testCase.GetProperty("input"));

                Spherical actual = GnomonicProjection.Inverse(input);

                JsonElement expected = testCase.GetProperty("expected");
                Assert.AreEqual(expected[0].GetDouble(), actual.Theta, Precision13);
                Assert.AreEqual(expected[1].GetDouble(), actual.Phi, Precision13);
            }
        }

        /// <summary>Pins that inverse gnomonic projection followed by forward projection round-trips back to the original polar input.</summary>
        [TestMethod]
        public async Task InverseThenForwardRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                Polar input = ReadPolar(testCase.GetProperty("input"));

                Spherical spherical = GnomonicProjection.Inverse(input);
                Polar roundTripped = GnomonicProjection.Forward(spherical);

                Assert.AreEqual(input.Rho, roundTripped.Rho, Precision13);
                Assert.AreEqual(input.Gamma, roundTripped.Gamma, Precision13);
            }
        }

        /// <summary>Reads a JSON <c>[theta, phi]</c> pair into a <see cref="Spherical"/> value.</summary>
        private static Spherical ReadSpherical(JsonElement element)
        {
            return new Spherical(element[0].GetDouble(), element[1].GetDouble());
        }

        /// <summary>Reads a JSON <c>[rho, gamma]</c> pair into a <see cref="Polar"/> value.</summary>
        private static Polar ReadPolar(JsonElement element)
        {
            return new Polar(element[0].GetDouble(), element[1].GetDouble());
        }

        /// <summary>Loads <c>projections/fixtures/gnomonic.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "projections/fixtures/gnomonic.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
