using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>fixtures/dodecahedron-quaternions.json</c> for
    /// <see cref="DodecahedronQuaternions.Quaternions"/>. Most assertions use |diff| &lt; 0.5e-10 or
    /// 0.5e-15; the ring z-value distribution check mirrors the fixture's own loose |diff| &lt; 0.5e-5
    /// case.
    /// </summary>
    [TestClass]
    internal sealed class A5DodecahedronQuaternionsTests
    {
        /// <summary>Bounds the loose ring z-value distribution comparison, mirroring the fixture's own tolerance.</summary>
        private const double Precision5 = 0.5e-5;

        /// <summary>Bounds comparisons of normalization magnitude and rotated vector length.</summary>
        private const double Precision10 = 0.5e-10;

        /// <summary>Bounds the tight per-component quaternion comparisons against the fixture.</summary>
        private const double Precision15 = 0.5e-15;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the fixture-declared quaternion count matches <see cref="DodecahedronQuaternions.Quaternions"/>'s length.</summary>
        [TestMethod]
        public async Task QuaternionArrayHasTwelveEntries()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.HasCount(fixture.RootElement.GetProperty("metadata").GetProperty("totalQuaternions").GetInt32(), DodecahedronQuaternions.Quaternions);
        }

        /// <summary>Pins that every quaternion's X, Y, Z, W components match the fixture at tight tolerance.</summary>
        [TestMethod]
        public async Task QuaternionsMatchFixtureAtTightTolerance()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("quaternions").EnumerateArray())
            {
                int index = testCase.GetProperty("index").GetInt32();
                JsonElement expected = testCase.GetProperty("quaternion");
                QuaternionD actual = DodecahedronQuaternions.Quaternions[index];

                Assert.AreEqual(expected[0].GetDouble(), actual.X, Precision15);
                Assert.AreEqual(expected[1].GetDouble(), actual.Y, Precision15);
                Assert.AreEqual(expected[2].GetDouble(), actual.Z, Precision15);
                Assert.AreEqual(expected[3].GetDouble(), actual.W, Precision15);
            }
        }

        /// <summary>Pins that the north pole quaternion at index 0 is the hardcoded identity quaternion.</summary>
        [TestMethod]
        public async Task NorthPoleQuaternionIsTheHardcodedIdentity()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsTrue(fixture.RootElement.GetProperty("validationTests").GetProperty("northPoleIdentity").GetBoolean());
            Assert.AreEqual(new QuaternionD(0, 0, 0, 1), DodecahedronQuaternions.Quaternions[0]);
        }

        /// <summary>Pins that the south pole quaternion at index 11 is the hardcoded rotation quaternion.</summary>
        [TestMethod]
        public async Task SouthPoleQuaternionIsTheHardcodedRotation()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsTrue(fixture.RootElement.GetProperty("validationTests").GetProperty("southPoleCorrect").GetBoolean());
            Assert.AreEqual(new QuaternionD(0, -1, 0, 0), DodecahedronQuaternions.Quaternions[11]);
        }

        /// <summary>Pins that every quaternion in the array has unit magnitude.</summary>
        [TestMethod]
        public async Task AllQuaternionsAreNormalized()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);

            Assert.IsTrue(fixture.RootElement.GetProperty("validationTests").GetProperty("allNormalized").GetBoolean());

            foreach(QuaternionD quaternion in DodecahedronQuaternions.Quaternions)
            {
                double magnitude = Magnitude(quaternion);
                Assert.AreEqual(1.0, magnitude, Precision10);
            }
        }

        /// <summary>Pins that quaternions 1 through 5 share a zero Z component and the expected first-ring W value.</summary>
        [TestMethod]
        public void FirstRingQuaternionsHaveConsistentStructure()
        {
            for(int index = 1; index <= 5; index++)
            {
                QuaternionD quaternion = DodecahedronQuaternions.Quaternions[index];
                double cosAlpha = Math.Sqrt((1 + Math.Sqrt(0.2)) / 2);

                Assert.AreEqual(0, quaternion.Z, Precision15);
                Assert.AreEqual(cosAlpha, quaternion.W, Precision10);
            }
        }

        /// <summary>Pins that quaternions 6 through 10 share a zero Z component and the expected second-ring W value.</summary>
        [TestMethod]
        public void SecondRingQuaternionsHaveConsistentStructure()
        {
            for(int index = 6; index <= 10; index++)
            {
                QuaternionD quaternion = DodecahedronQuaternions.Quaternions[index];
                double sinAlpha = Math.Sqrt((1 - Math.Sqrt(0.2)) / 2);

                Assert.AreEqual(0, quaternion.Z, Precision15);
                Assert.AreEqual(sinAlpha, quaternion.W, Precision10);
            }
        }

        /// <summary>Pins that rotating the north pole by each quaternion yields a unit-length vector distinct from the pole itself, except at index 0.</summary>
        [TestMethod]
        public void RotatingTheNorthPoleByEachQuaternionPreservesUnitLength()
        {
            Vector3d northPole = new(0, 0, 1);

            for(int index = 0; index < DodecahedronQuaternions.Quaternions.Length; index++)
            {
                Vector3d rotated = northPole.Transform(DodecahedronQuaternions.Quaternions[index]);

                Assert.AreEqual(1.0, rotated.Length(), Precision10);

                if(index != 0)
                {
                    Assert.IsGreaterThan(0.1, Vector3d.Distance(rotated, northPole));
                }
            }
        }

        /// <summary>Pins that the sorted Z values of the rotated north pole across all quaternions match the expected pole and ring distribution.</summary>
        [TestMethod]
        public void RingZValuesMatchExpectedDistributionAtTheFixtureLooseTolerance()
        {
            Vector3d northPole = new(0, 0, 1);
            double[] zValues = new double[DodecahedronQuaternions.Quaternions.Length];
            for(int index = 0; index < DodecahedronQuaternions.Quaternions.Length; index++)
            {
                zValues[index] = northPole.Transform(DodecahedronQuaternions.Quaternions[index]).Z;
            }

            Array.Sort(zValues);
            Array.Reverse(zValues);

            double invSqrt5 = Math.Sqrt(0.2);

            Assert.AreEqual(1, zValues[0], Precision10);
            Assert.AreEqual(-1, zValues[11], Precision10);

            for(int index = 1; index <= 5; index++)
            {
                Assert.AreEqual(invSqrt5, zValues[index], Precision5);
            }

            for(int index = 6; index <= 10; index++)
            {
                Assert.AreEqual(-invSqrt5, zValues[index], Precision5);
            }
        }

        /// <summary>Pins that the fixture's declared quaternion count and constants match the source values.</summary>
        [TestMethod]
        public async Task FixtureMetadataAndConstantsAreConsistent()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            JsonElement constants = fixture.RootElement.GetProperty("constants");

            Assert.AreEqual(12, fixture.RootElement.GetProperty("metadata").GetProperty("totalQuaternions").GetInt32());
            Assert.AreEqual(Math.Sqrt(0.2), constants.GetProperty("INV_SQRT5").GetDouble(), Precision15);
            Assert.AreEqual(2 * Math.PI / 5, constants.GetProperty("expectedPentagonAngle").GetDouble(), Precision15);
        }

        /// <summary>Computes a quaternion's Euclidean magnitude for the normalization check.</summary>
        private static double Magnitude(QuaternionD quaternion)
        {
            return Math.Sqrt((quaternion.X * quaternion.X) + (quaternion.Y * quaternion.Y) + (quaternion.Z * quaternion.Z) + (quaternion.W * quaternion.W));
        }

        /// <summary>Loads <c>fixtures/dodecahedron-quaternions.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "fixtures/dodecahedron-quaternions.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
