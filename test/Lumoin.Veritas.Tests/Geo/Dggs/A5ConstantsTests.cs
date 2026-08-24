using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Constants parity against <c>fixtures/constants.json</c>. Assertion regimes: constants derived purely
    /// from IEEE-754 correctly-rounded operations (sqrt, +, −, ×, ÷ on the shared π literal) are asserted
    /// bit-exact — conforming runtimes must agree; the three constants routed through platform-libm
    /// transcendentals (atan, acos) are asserted at the fixture tolerance |diff| &lt; 0.5e-15, since exact
    /// equality there is a same-runtime self-oracle, not a cross-language contract.
    /// </summary>
    [TestClass]
    internal sealed class A5ConstantsTests
    {
        /// <summary>Bounds the fixture-tolerance comparisons of the libm-transcendental-derived constants.</summary>
        private const double Precision15 = 0.5e-15;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that <see cref="Constants.Phi"/> and its derived identities match the fixture bit-exactly and at fixture tolerance.</summary>
        [TestMethod]
        public async Task GoldenRatioMatchesFixtureExactly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement phi = fixture.RootElement.GetProperty("φ");

            Assert.AreEqual(phi.GetProperty("value").GetDouble(), Constants.Phi);
            Assert.AreEqual(phi.GetProperty("expectedValue").GetDouble(), Constants.Phi, Precision15);

            JsonElement properties = phi.GetProperty("properties");
            Assert.AreEqual(
                properties.GetProperty("goldenRatioPlusOne").GetDouble(),
                properties.GetProperty("goldenRatioSquared").GetDouble(),
                Precision15);
            Assert.AreEqual(
                properties.GetProperty("reciprocalMinusOne").GetDouble(),
                properties.GetProperty("reciprocal").GetDouble(),
                Precision15);
        }

        /// <summary>Pins that the angular constants (two pi, two pi over 5, pi over 5, pi over 10) match the fixture bit-exactly.</summary>
        [TestMethod]
        public async Task AngularConstantsMatchFixtureExactly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement angles = fixture.RootElement.GetProperty("angles");

            Assert.AreEqual(Constants.TwoPi, angles.GetProperty("TWO_PI").GetProperty("value").GetDouble());
            Assert.AreEqual(Constants.TwoPiOver5, angles.GetProperty("TWO_PI_OVER_5").GetProperty("value").GetDouble());
            Assert.AreEqual(Constants.PiOver5, angles.GetProperty("PI_OVER_5").GetProperty("value").GetDouble());
            Assert.AreEqual(Constants.PiOver10, angles.GetProperty("PI_OVER_10").GetProperty("value").GetDouble());
        }

        /// <summary>Pins that the dodecahedron's dihedral, interhedral, and face-edge angles match the fixture at fixture tolerance and sum to pi.</summary>
        [TestMethod]
        public async Task DodecahedronAnglesMatchFixtureAtFixtureTolerance()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement angles = fixture.RootElement.GetProperty("dodecahedronAngles");

            Assert.AreEqual(angles.GetProperty("dihedralAngle").GetProperty("expectedValue").GetDouble(), Constants.DihedralAngle, Precision15);
            Assert.AreEqual(angles.GetProperty("interhedralAngle").GetProperty("expectedValue").GetDouble(), Constants.InterhedralAngle, Precision15);
            Assert.AreEqual(angles.GetProperty("faceEdgeAngle").GetProperty("expectedValue").GetDouble(), Constants.FaceEdgeAngle, Precision15);
            Assert.AreEqual(Math.PI, angles.GetProperty("angleSum").GetDouble(), Precision15);
        }

        /// <summary>Pins that <see cref="Constants.DistanceToEdge"/> and <see cref="Constants.DistanceToVertex"/> match the fixture's value and alternative-formula fields.</summary>
        [TestMethod]
        public async Task DistanceConstantsMatchFixtureExactly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement distances = fixture.RootElement.GetProperty("distances");

            JsonElement distanceToEdge = distances.GetProperty("distanceToEdge");
            Assert.AreEqual(distanceToEdge.GetProperty("value").GetDouble(), Constants.DistanceToEdge);
            Assert.AreEqual(distanceToEdge.GetProperty("alternativeFormula").GetDouble(), Constants.DistanceToEdge, Precision15);

            JsonElement distanceToVertex = distances.GetProperty("distanceToVertex");
            Assert.AreEqual(distanceToVertex.GetProperty("value").GetDouble(), Constants.DistanceToVertex);
            Assert.AreEqual(distanceToVertex.GetProperty("alternativeFormula").GetDouble(), Constants.DistanceToVertex, Precision15);
        }

        /// <summary>Pins that the inscribed, mid-edge, and circumscribed sphere radii match the fixture and preserve the fixture's ordering relationships.</summary>
        [TestMethod]
        public async Task SphereRadiiMatchFixtureExactly()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement radii = fixture.RootElement.GetProperty("sphereRadii");

            Assert.AreEqual(Constants.RadiusInscribed, radii.GetProperty("Rinscribed").GetProperty("value").GetDouble());
            Assert.AreEqual(radii.GetProperty("Rmidedge").GetProperty("value").GetDouble(), Constants.RadiusMidEdge);
            Assert.AreEqual(radii.GetProperty("Rcircumscribed").GetProperty("value").GetDouble(), Constants.RadiusCircumscribed);

            JsonElement relationships = radii.GetProperty("relationships");
            Assert.IsTrue(relationships.GetProperty("inscribedLessThanMidedge").GetBoolean());
            Assert.IsTrue(relationships.GetProperty("midedgeLessThanCircumscribed").GetBoolean());
        }

        /// <summary>Pins that the fixture's finiteness and positivity validation checks hold for the corresponding constants.</summary>
        [TestMethod]
        public async Task ValidationTestsHold()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement validation = fixture.RootElement.GetProperty("validationTests");

            foreach(JsonElement entry in validation.GetProperty("finiteNumbers").EnumerateArray())
            {
                Assert.IsTrue(entry.GetProperty("isFinite").GetBoolean());
                Assert.IsFalse(entry.GetProperty("isNaN").GetBoolean());
            }

            foreach(JsonElement entry in validation.GetProperty("positiveConstants").EnumerateArray())
            {
                Assert.IsTrue(entry.GetProperty("isPositive").GetBoolean());
            }
        }

        /// <summary>Loads <c>fixtures/constants.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/constants.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
