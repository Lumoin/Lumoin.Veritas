using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Lattice;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/lattice/quaternary.json</c> for the conversions between a quaternary
    /// digit and its <see cref="KJ"/> / flip / <see cref="IJ"/> representations. All assertions are
    /// exact integer equality — no tolerances.
    /// </summary>
    [TestClass]
    internal sealed class A5LatticeQuaternaryTests
    {
        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that converting an <see cref="IJ"/> offset and flip pair to a quaternary digit matches the fixture for every case.</summary>
        [TestMethod]
        public async Task IJToQuaternaryProducesCorrectDigitForAllCases()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("IJToQuaternary").EnumerateArray())
            {
                JsonElement ij = testCase.GetProperty("ij");
                IJ offset = new(ij[0].GetDouble(), ij[1].GetDouble());
                FlipPair flips = ReadFlipPair(testCase.GetProperty("flips"));

                int expectedDigit = testCase.GetProperty("digit").GetInt32();
                int actualDigit = QuaternaryConversions.IJToQuaternary(offset, flips);

                Assert.AreEqual(expectedDigit, actualDigit);
            }
        }

        /// <summary>Pins that converting a quaternary digit and flip pair to a <see cref="KJ"/> matches the fixture for every case.</summary>
        [TestMethod]
        public async Task QuaternaryToKJProducesCorrectKJForAllCases()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("quaternaryToKJ").EnumerateArray())
            {
                int quaternary = testCase.GetProperty("q").GetInt32();
                FlipPair flips = ReadFlipPair(testCase.GetProperty("flips"));
                JsonElement kj = testCase.GetProperty("kj");

                KJ actual = QuaternaryConversions.QuaternaryToKJ(quaternary, flips);

                Assert.AreEqual(kj[0].GetDouble(), actual.K);
                Assert.AreEqual(kj[1].GetDouble(), actual.J);
            }
        }

        /// <summary>Pins that converting a quaternary digit to a <see cref="FlipPair"/> matches the fixture for every value.</summary>
        [TestMethod]
        public async Task QuaternaryToFlipsProducesCorrectFlipsForAllValues()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("quaternaryToFlips").EnumerateArray())
            {
                int quaternary = testCase.GetProperty("q").GetInt32();
                FlipPair expected = ReadFlipPair(testCase.GetProperty("flips"));

                FlipPair actual = QuaternaryConversions.QuaternaryToFlips(quaternary);

                Assert.AreEqual(expected.FlipX, actual.FlipX);
                Assert.AreEqual(expected.FlipY, actual.FlipY);
            }
        }

        /// <summary>Reads a two-element <c>[flipX, flipY]</c> JSON array of ±1 integers as a <see cref="FlipPair"/>.</summary>
        private static FlipPair ReadFlipPair(JsonElement flipsElement)
        {
            return new FlipPair((Flip)flipsElement[0].GetInt32(), (Flip)flipsElement[1].GetInt32());
        }

        /// <summary>Loads <c>fixtures/lattice/quaternary.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/lattice/quaternary.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
