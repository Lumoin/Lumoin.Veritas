using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/utils/great-circle.json</c> for <see cref="GreatCircle.GreatCircleDistance"/>
    /// and <see cref="GreatCircle.SampleGreatCircleArc"/>: distances and sample arrays at |diff| &lt; 0.5e-6,
    /// sample counts exact.
    /// </summary>
    [TestClass]
    internal sealed class A5GreatCircleTests
    {
        /// <summary>Bounds the distance and sample-point comparisons against the fixture.</summary>
        private const double Precision6 = 0.5e-6;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="GreatCircle.GreatCircleDistance"/> matches the fixture's distance for every case.</summary>
        [TestMethod]
        public async Task GreatCircleDistanceMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("sampleGreatCircleArc").EnumerateArray())
            {
                Cartesian a = ReadCartesian(testCase.GetProperty("aVec"));
                Cartesian b = ReadCartesian(testCase.GetProperty("bVec"));

                double distance = GreatCircle.GreatCircleDistance(a, b);

                Assert.AreEqual(testCase.GetProperty("distance").GetDouble(), distance, Precision6);
            }
        }

        /// <summary>Pins that <see cref="GreatCircle.SampleGreatCircleArc"/> matches the fixture's sample count and sample points for every case.</summary>
        [TestMethod]
        public async Task SampleGreatCircleArcMatchesFixtureForEveryCase()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("sampleGreatCircleArc").EnumerateArray())
            {
                Cartesian a = ReadCartesian(testCase.GetProperty("aVec"));
                Cartesian b = ReadCartesian(testCase.GetProperty("bVec"));
                double sampleInterval = testCase.GetProperty("sampleInterval").GetDouble();

                Cartesian[] samples = GreatCircle.SampleGreatCircleArc(a, b, sampleInterval);

                int expectedSampleCount = testCase.GetProperty("sampleCount").GetInt32();
                Assert.HasCount(expectedSampleCount, samples);

                JsonElement expectedSamples = testCase.GetProperty("samples");
                for(int index = 0; index < samples.Length; index++)
                {
                    JsonElement expected = expectedSamples[index];
                    Assert.AreEqual(expected[0].GetDouble(), samples[index].X, Precision6);
                    Assert.AreEqual(expected[1].GetDouble(), samples[index].Y, Precision6);
                    Assert.AreEqual(expected[2].GetDouble(), samples[index].Z, Precision6);
                }
            }
        }

        /// <summary>Reads a JSON <c>[x, y, z]</c> triple into a <see cref="Cartesian"/>.</summary>
        private static Cartesian ReadCartesian(JsonElement vectorElement)
        {
            return new Cartesian(vectorElement[0].GetDouble(), vectorElement[1].GetDouble(), vectorElement[2].GetDouble());
        }

        /// <summary>Loads <c>fixtures/utils/great-circle.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/utils/great-circle.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
