using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>projections/fixtures/authalic.json</c> for <see cref="AuthalicProjection"/>.
    /// Fixture comparisons use |diff| &lt; 0.5e-10; forward/inverse round-trips use the tighter
    /// |diff| &lt; 0.5e-15.
    /// </summary>
    [TestClass]
    internal sealed class A5ProjectionsAuthalicTests
    {
        /// <summary>Bounds fixture comparisons against the authalic projection at |diff| &lt; 0.5e-10.</summary>
        private const double Precision10 = 0.5e-10;

        /// <summary>Bounds forward/inverse round-trip comparisons at the tighter |diff| &lt; 0.5e-15.</summary>
        private const double Precision15 = 0.5e-15;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that forward authalic-latitude projections match the fixture's expected values within Precision10.</summary>
        [TestMethod]
        public async Task ForwardProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                double actual = AuthalicProjection.Forward(testCase.GetProperty("input").GetDouble());

                Assert.AreEqual(testCase.GetProperty("expected").GetDouble(), actual, Precision10);
            }
        }

        /// <summary>Pins that forward projection followed by inverse projection round-trips back to the original input within Precision15.</summary>
        [TestMethod]
        public async Task ForwardThenInverseRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                double input = testCase.GetProperty("input").GetDouble();

                double authalicLatitude = AuthalicProjection.Forward(input);
                double roundTripped = AuthalicProjection.Inverse(authalicLatitude);

                Assert.AreEqual(input, roundTripped, Precision15);
            }
        }

        /// <summary>Pins that inverse authalic-latitude projections match the fixture's expected values within Precision10.</summary>
        [TestMethod]
        public async Task InverseProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                double actual = AuthalicProjection.Inverse(testCase.GetProperty("input").GetDouble());

                Assert.AreEqual(testCase.GetProperty("expected").GetDouble(), actual, Precision10);
            }
        }

        /// <summary>Pins that inverse projection followed by forward projection round-trips back to the original input within Precision15.</summary>
        [TestMethod]
        public async Task InverseThenForwardRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                double input = testCase.GetProperty("input").GetDouble();

                double geodeticLatitude = AuthalicProjection.Inverse(input);
                double roundTripped = AuthalicProjection.Forward(geodeticLatitude);

                Assert.AreEqual(input, roundTripped, Precision15);
            }
        }

        /// <summary>Loads <c>projections/fixtures/authalic.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "projections/fixtures/authalic.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
