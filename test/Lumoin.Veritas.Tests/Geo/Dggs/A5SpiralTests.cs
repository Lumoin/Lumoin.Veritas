using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/utils/spiral.json</c> for <see cref="Spiral"/>: the sample count
    /// constant and every sample of every fixture spiral, at |diff| &lt; 0.5e-6 per component.
    /// </summary>
    [TestClass]
    internal sealed class A5SpiralTests
    {
        /// <summary>Bounds per-component spiral sample comparisons against the fixture at |diff| &lt; 0.5e-6.</summary>
        private const double Precision6 = 0.5e-6;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that Spiral.SampleCount matches the fixture's recorded sample count.</summary>
        [TestMethod]
        public async Task SampleCountMatchesFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.AreEqual(Spiral.SampleCount, fixture.RootElement.GetProperty("sampleCount").GetInt32());
        }

        /// <summary>Pins that every sample of every fixture spiral matches the fixture's expected Cartesian value.</summary>
        [TestMethod]
        public async Task SampleMatchesFixtureForEverySpiral()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("spiral").EnumerateArray())
            {
                JsonElement centerElement = testCase.GetProperty("center");
                Spherical center = new(centerElement[0].GetDouble(), centerElement[1].GetDouble());
                double scaleRadians = testCase.GetProperty("scaleRad").GetDouble();
                int sampleCount = testCase.GetProperty("sampleCount").GetInt32();
                Assert.AreEqual(Spiral.SampleCount, sampleCount);

                Spiral spiral = new(center, scaleRadians);
                JsonElement samples = testCase.GetProperty("samples");

                for(int index = 0; index < Spiral.SampleCount; index++)
                {
                    Cartesian sample = spiral.Sample(index);
                    JsonElement expected = samples[index];

                    Assert.AreEqual(expected[0].GetDouble(), sample.X, Precision6);
                    Assert.AreEqual(expected[1].GetDouble(), sample.Y, Precision6);
                    Assert.AreEqual(expected[2].GetDouble(), sample.Z, Precision6);
                }
            }
        }

        /// <summary>Loads <c>fixtures/utils/spiral.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/utils/spiral.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
