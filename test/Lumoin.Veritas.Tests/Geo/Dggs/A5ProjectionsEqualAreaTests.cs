using System.Text.Json;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Projections;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity against <c>projections/fixtures/equal-area.json</c> for <see cref="EqualAreaProjection"/>:
    /// forward and inverse projections at strict |diff| &lt; 1e-13 against the fixture's static
    /// <c>TEST_SPHERICAL_TRIANGLE</c>/<c>TEST_FACE_TRIANGLE</c> pair, both round-trip directions, and
    /// the triangle-constants agreement invariant every congruent face triangle
    /// <see cref="DodecahedronProjection"/> can produce must satisfy.
    /// </summary>
    [TestClass]
    internal sealed class A5ProjectionsEqualAreaTests
    {
        /// <summary>Bounds forward/inverse equal-area projection array comparisons at strict |diff| &lt; 1e-13.</summary>
        private const double PrecisionArray13 = 1e-13;

        /// <summary>Bounds the triangle-constants agreement invariant's relative-tolerance comparisons at 1e-13.</summary>
        private const double RelativeTolerance13 = 1e-13;

        /// <summary>Bounds the round-trip real-world error, in millimeters, that the projection is allowed to accumulate.</summary>
        private const double DesiredMillimeterPrecision = 0.01;

        /// <summary>The authalic Earth radius, in kilometers, used to convert an angular round-trip error into a millimeter distance.</summary>
        private const double AuthalicRadiusKilometers = 6371.0072;

        /// <summary>The number of face triangles per dodecahedron origin the triangle-constants invariant sweeps.</summary>
        private const int FaceTriangleCount = 10;

        /// <summary>The number of dodecahedron origins the triangle-constants invariant sweeps.</summary>
        private const int OriginCount = 12;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that forward equal-area face projections match the fixture's expected values for the static test triangle.</summary>
        [TestMethod]
        public async Task ForwardProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            (SphericalTriangle sphericalTriangle, FaceTriangle faceTriangle) = ReadStaticTriangles(fixture.RootElement.GetProperty("static"));
            EqualAreaProjection equalArea = new(sphericalTriangle);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                Cartesian input = ReadCartesian(testCase.GetProperty("input"));

                Face actual = equalArea.Forward(input, sphericalTriangle, faceTriangle);

                AssertFaceMatches(testCase.GetProperty("expected"), actual);
            }
        }

        /// <summary>Pins that forward equal-area projection followed by inverse projection round-trips within DesiredMillimeterPrecision of the original input.</summary>
        [TestMethod]
        public async Task ForwardThenInverseRoundTripsBackToTheInputWithinMillimeterAccuracy()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            (SphericalTriangle sphericalTriangle, FaceTriangle faceTriangle) = ReadStaticTriangles(fixture.RootElement.GetProperty("static"));
            EqualAreaProjection equalArea = new(sphericalTriangle);

            double maxArcLengthMillimeters = AuthalicRadiusKilometers * MaxTriangleAngle(sphericalTriangle) * 1e9;
            double largestError = 0;

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("forward").EnumerateArray())
            {
                Cartesian input = ReadCartesian(testCase.GetProperty("input"));

                Face face = equalArea.Forward(input, sphericalTriangle, faceTriangle);
                Cartesian roundTripped = equalArea.Inverse(face, faceTriangle, sphericalTriangle);

                AssertCartesianMatches(testCase.GetProperty("input"), roundTripped);

                double error = Vector3d.Distance(CoordinateConversions.ToVector3d(roundTripped), CoordinateConversions.ToVector3d(input));
                largestError = Math.Max(largestError, error);
            }

            Assert.IsLessThan(DesiredMillimeterPrecision, largestError * maxArcLengthMillimeters);
        }

        /// <summary>Pins that inverse equal-area projections match the fixture's expected Cartesian values for the static test triangle.</summary>
        [TestMethod]
        public async Task InverseProjectionsMatchFixture()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            (SphericalTriangle sphericalTriangle, FaceTriangle faceTriangle) = ReadStaticTriangles(fixture.RootElement.GetProperty("static"));
            EqualAreaProjection equalArea = new(sphericalTriangle);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                Face input = ReadFace(testCase.GetProperty("input"));

                Cartesian actual = equalArea.Inverse(input, faceTriangle, sphericalTriangle);

                AssertCartesianMatches(testCase.GetProperty("expected"), actual);
            }
        }

        /// <summary>Pins that inverse equal-area projection followed by forward projection round-trips back to the original face input.</summary>
        [TestMethod]
        public async Task InverseThenForwardRoundTripsBackToTheInput()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            (SphericalTriangle sphericalTriangle, FaceTriangle faceTriangle) = ReadStaticTriangles(fixture.RootElement.GetProperty("static"));
            EqualAreaProjection equalArea = new(sphericalTriangle);

            foreach(JsonElement testCase in fixture.RootElement.GetProperty("inverse").EnumerateArray())
            {
                Face input = ReadFace(testCase.GetProperty("input"));

                Cartesian pointOnSphere = equalArea.Inverse(input, faceTriangle, sphericalTriangle);
                Face roundTripped = equalArea.Forward(pointOnSphere, sphericalTriangle, faceTriangle);

                AssertFaceMatches(testCase.GetProperty("input"), roundTripped);
            }
        }

        /// <summary>Pins that the equal-area triangle constants agree, within RelativeTolerance13, across every dodecahedron origin, face triangle, and reflection.</summary>
        [TestMethod]
        public async Task TriangleConstantsAgreeAcrossAllFaceTrianglesOriginsAndReflections()
        {
            using JsonDocument fixture = await LoadFixtureAsync(TestContext.CancellationToken).ConfigureAwait(false);
            (SphericalTriangle canonicalTriangle, FaceTriangle _) = ReadStaticTriangles(fixture.RootElement.GetProperty("static"));
            EqualAreaTriangleConstants canonical = EqualAreaProjection.ComputeConstants(canonicalTriangle);

            for(int originId = 0; originId < OriginCount; originId++)
            {
                for(int faceTriangleIndex = 0; faceTriangleIndex < FaceTriangleCount; faceTriangleIndex++)
                {
                    foreach(bool reflected in new[] { false, true })
                    {
                        SphericalTriangle triangle = DodecahedronProjection.GetSphericalTriangle(faceTriangleIndex, originId, reflected);
                        EqualAreaTriangleConstants constants = EqualAreaProjection.ComputeConstants(triangle);

                        string context = $"at face {faceTriangleIndex}, origin {originId}, reflected {reflected}.";
                        AssertWithinRelativeTolerance(canonical.V, constants.V, $"V mismatch {context}");
                        AssertWithinRelativeTolerance(canonical.C12, constants.C12, $"C12 mismatch {context}");
                        AssertWithinRelativeTolerance(canonical.S12, constants.S12, $"S12 mismatch {context}");
                        AssertWithinRelativeTolerance(canonical.KQ, constants.KQ, $"KQ mismatch {context}");
                        AssertWithinRelativeTolerance(canonical.TriangleArea, constants.TriangleArea, $"TriangleArea mismatch {context}");
                    }
                }
            }
        }

        /// <summary>Asserts <paramref name="actual"/> is within <see cref="RelativeTolerance13"/> of <paramref name="expected"/>, relative to <paramref name="expected"/>'s magnitude.</summary>
        private static void AssertWithinRelativeTolerance(double expected, double actual, string message)
        {
            double tolerance = Math.Abs(expected) * RelativeTolerance13;

            Assert.IsLessThan(tolerance, Math.Abs(actual - expected), message);
        }

        /// <summary>Reads the fixture's static <c>TEST_SPHERICAL_TRIANGLE</c>/<c>TEST_FACE_TRIANGLE</c> pair.</summary>
        private static (SphericalTriangle SphericalTriangle, FaceTriangle FaceTriangle) ReadStaticTriangles(JsonElement staticElement)
        {
            SphericalTriangle sphericalTriangle = ReadSphericalTriangle(staticElement.GetProperty("TEST_SPHERICAL_TRIANGLE"));
            FaceTriangle faceTriangle = ReadFaceTriangle(staticElement.GetProperty("TEST_FACE_TRIANGLE"));

            return (sphericalTriangle, faceTriangle);
        }

        /// <summary>The largest angle between any two vertices of a spherical triangle.</summary>
        private static double MaxTriangleAngle(SphericalTriangle triangle)
        {
            Vector3d vertexA = CoordinateConversions.ToVector3d(triangle.A);
            Vector3d vertexB = CoordinateConversions.ToVector3d(triangle.B);
            Vector3d vertexC = CoordinateConversions.ToVector3d(triangle.C);

            double angleAB = Vector3d.Angle(vertexA, vertexB);
            double angleBC = Vector3d.Angle(vertexB, vertexC);
            double angleCA = Vector3d.Angle(vertexC, vertexA);

            return Math.Max(angleAB, Math.Max(angleBC, angleCA));
        }

        /// <summary>Reads a fixture <c>[x, y, z]</c> triple into a <see cref="Cartesian"/> value.</summary>
        private static Cartesian ReadCartesian(JsonElement element)
        {
            return new Cartesian(element[0].GetDouble(), element[1].GetDouble(), element[2].GetDouble());
        }

        /// <summary>Reads a fixture <c>[x, y]</c> pair into a <see cref="Face"/> value.</summary>
        private static Face ReadFace(JsonElement element)
        {
            return new Face(element[0].GetDouble(), element[1].GetDouble());
        }

        /// <summary>Reads a fixture array of three <c>[x, y, z]</c> triples into a <see cref="SphericalTriangle"/>.</summary>
        private static SphericalTriangle ReadSphericalTriangle(JsonElement element)
        {
            return new SphericalTriangle(ReadCartesian(element[0]), ReadCartesian(element[1]), ReadCartesian(element[2]));
        }

        /// <summary>Reads a fixture array of three <c>[x, y]</c> pairs into a <see cref="FaceTriangle"/>.</summary>
        private static FaceTriangle ReadFaceTriangle(JsonElement element)
        {
            return new FaceTriangle(ReadFace(element[0]), ReadFace(element[1]), ReadFace(element[2]));
        }

        /// <summary>Asserts a <see cref="Face"/> value matches a fixture <c>[x, y]</c> pair at the array tolerance.</summary>
        private static void AssertFaceMatches(JsonElement expected, Face actual)
        {
            Assert.AreEqual(expected[0].GetDouble(), actual.X, PrecisionArray13);
            Assert.AreEqual(expected[1].GetDouble(), actual.Y, PrecisionArray13);
        }

        /// <summary>Asserts a <see cref="Cartesian"/> value matches a fixture <c>[x, y, z]</c> triple at the array tolerance.</summary>
        private static void AssertCartesianMatches(JsonElement expected, Cartesian actual)
        {
            Assert.AreEqual(expected[0].GetDouble(), actual.X, PrecisionArray13);
            Assert.AreEqual(expected[1].GetDouble(), actual.Y, PrecisionArray13);
            Assert.AreEqual(expected[2].GetDouble(), actual.Z, PrecisionArray13);
        }

        /// <summary>Loads <c>projections/fixtures/equal-area.json</c> from the copied corpus.</summary>
        private static async Task<JsonDocument> LoadFixtureAsync(CancellationToken cancellationToken)
        {
            using FileStream stream = File.OpenRead(TestPaths.Fixture("Geo/Dggs/Fixtures", "projections/fixtures/equal-area.json"));

            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
