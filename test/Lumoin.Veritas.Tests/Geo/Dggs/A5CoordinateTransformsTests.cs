using Lumoin.Veritas.Geo.Dggs.Core;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Parity for <see cref="CoordinateTransforms"/>: degree/radian conversion, barycentric round
    /// trips, spherical/Cartesian conversion, the 93-degree longitude offset in
    /// <see cref="CoordinateTransforms.FromLonLat"/> / <see cref="CoordinateTransforms.ToLonLat"/>, and
    /// antimeridian handling in <see cref="CoordinateTransforms.NormalizeLongitudes"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TestPoints"/> below transcribes the fixed (non-random) portion of a face-coordinate
    /// point table used to exercise the barycentric round trip: the 31 base points, the 8
    /// small-magnitude "difficult" points, and both sets' coordinate-swapped reflections, transcribed as
    /// C# literals rather than generated. Unseeded-random points are excluded: they are not reproducible
    /// literals, and the fixed 78 points already cover every edge case.
    /// </remarks>
    [TestClass]
    internal sealed class A5CoordinateTransformsTests
    {
        /// <summary>Bounds the barycentric and face-coordinate round-trip comparisons.</summary>
        private const double Precision12 = 1e-12;

        /// <summary>Bounds the spherical, Cartesian, and lon/lat round-trip comparisons.</summary>
        private const double Precision13 = 1e-13;

        /// <summary>The shared triangle's first vertex, at the face-coordinate origin.</summary>
        private static Face TriangleVertexA { get; } = new(0, 0);

        /// <summary>The shared triangle's second vertex.</summary>
        private static Face TriangleVertexB { get; } = new(1, 0);

        /// <summary>The shared triangle's third vertex.</summary>
        private static Face TriangleVertexC { get; } = new(0, 1);

        /// <summary>The transcribed, fixed face-coordinate test point table used to exercise the barycentric round trip.</summary>
        private static Face[] TestPoints { get; } = BuildTestPoints();

        /// <summary>Longitude/latitude points covering the equator, mid-latitudes, both hemispheres, the date line, the poles, and an arbitrary point, used to exercise the <c>FromLonLat</c>/<c>ToLonLat</c> round trip.</summary>
        private static LonLat[] TestPointsLonLat { get; } =
        [
            new(0, 0), // Equator
            new(90, 0), // Equator
            new(180, 0), // Equator
            new(0, 45), // Mid latitude
            new(0, -45), // Mid latitude
            new(-90, -45), // West hemisphere mid-latitude
            new(180, 45), // Date line mid-latitude
            new(90, 45), // East hemisphere mid-latitude
            new(0, 90), // North pole
            new(0, -90), // South pole
            new(123, 45), // Arbitrary point
        ];

        /// <summary>Pins that <see cref="CoordinateTransforms.DegreesToRadians"/> converts 180, 90, and 0 degrees exactly.</summary>
        [TestMethod]
        public void DegreesToRadiansConvertsExactly()
        {
            Assert.AreEqual(Math.PI, CoordinateTransforms.DegreesToRadians(180));
            Assert.AreEqual(Math.PI / 2, CoordinateTransforms.DegreesToRadians(90));
            Assert.AreEqual(0, CoordinateTransforms.DegreesToRadians(0));
        }

        /// <summary>Pins that <see cref="CoordinateTransforms.RadiansToDegrees"/> converts pi, pi/2, and 0 radians exactly.</summary>
        [TestMethod]
        public void RadiansToDegreesConvertsExactly()
        {
            Assert.AreEqual(180, CoordinateTransforms.RadiansToDegrees(Math.PI));
            Assert.AreEqual(90, CoordinateTransforms.RadiansToDegrees(Math.PI / 2));
            Assert.AreEqual(0, CoordinateTransforms.RadiansToDegrees(0));
        }

        /// <summary>Pins that every point in <see cref="TestPoints"/> round-trips through <see cref="CoordinateTransforms.FaceToBarycentric"/> and <see cref="CoordinateTransforms.BarycentricToFace"/>, with the barycentric weights summing to 1 and staying non-negative.</summary>
        [TestMethod]
        public void FaceToBarycentricAndBackRoundTripsEveryTestPoint()
        {
            foreach(Face point in TestPoints)
            {
                Barycentric bary = CoordinateTransforms.FaceToBarycentric(point, TriangleVertexA, TriangleVertexB, TriangleVertexC);
                Face result = CoordinateTransforms.BarycentricToFace(bary, TriangleVertexA, TriangleVertexB, TriangleVertexC);

                Assert.AreEqual(point.X, result.X, Precision12);
                Assert.AreEqual(point.Y, result.Y, Precision12);

                Assert.AreEqual(1, bary.B0 + bary.B1 + bary.B2, Precision12);

                Assert.IsGreaterThanOrEqualTo(0, bary.B0);
                Assert.IsGreaterThanOrEqualTo(0, bary.B1);
                Assert.IsGreaterThanOrEqualTo(0, bary.B2);
            }
        }

        /// <summary>Pins that a fixed set of barycentric coordinates round-trips through <see cref="CoordinateTransforms.BarycentricToFace"/> and <see cref="CoordinateTransforms.FaceToBarycentric"/>, with the recovered weights summing to 1.</summary>
        [TestMethod]
        public void BarycentricToFaceAndBackRoundTripsEveryTestBarycentric()
        {
            Barycentric[] testBarycentrics =
            [
                new(0.043821975867140296, 0.9561208684797726, 0.00005715565308705983),
                new(0.5, 0.3, 0.2),
                new(0.1, 0.8, 0.1),
                new(0.33, 0.33, 0.34),
                new(0.9, 0.05, 0.05),
                new(0.001, 0.999, 0.0)
            ];

            foreach(Barycentric bary in testBarycentrics)
            {
                Face face = CoordinateTransforms.BarycentricToFace(bary, TriangleVertexA, TriangleVertexB, TriangleVertexC);
                Barycentric result = CoordinateTransforms.FaceToBarycentric(face, TriangleVertexA, TriangleVertexB, TriangleVertexC);

                Assert.AreEqual(bary.B0, result.B0, Precision12);
                Assert.AreEqual(bary.B1, result.B1, Precision12);
                Assert.AreEqual(bary.B2, result.B2, Precision12);

                Assert.AreEqual(1, result.B0 + result.B1 + result.B2, Precision12);
            }
        }

        /// <summary>Pins that each triangle vertex maps to the corresponding unit barycentric coordinate.</summary>
        [TestMethod]
        public void FaceToBarycentricHandlesTriangleVerticesExactly()
        {
            AssertBarycentricRoundTrips(TriangleVertexA, new Barycentric(1, 0, 0));
            AssertBarycentricRoundTrips(TriangleVertexB, new Barycentric(0, 1, 0));
            AssertBarycentricRoundTrips(TriangleVertexC, new Barycentric(0, 0, 1));
        }

        /// <summary>Pins that each triangle edge's midpoint maps to a barycentric coordinate of (0.5, 0.5) on that edge's two vertices.</summary>
        [TestMethod]
        public void FaceToBarycentricHandlesEdgeMidpointsExactly()
        {
            AssertBarycentricRoundTrips(new Face(0.5, 0), new Barycentric(0.5, 0.5, 0));
            AssertBarycentricRoundTrips(new Face(0, 0.5), new Barycentric(0.5, 0, 0.5));
            AssertBarycentricRoundTrips(new Face(0.5, 0.5), new Barycentric(0, 0.5, 0.5));
        }

        /// <summary>Pins that <see cref="CoordinateTransforms.ToCartesian"/> converts the north pole and two equatorial points to their expected axis-aligned Cartesian coordinates.</summary>
        [TestMethod]
        public void ToCartesianConvertsKnownSphericalPoints()
        {
            Cartesian northPole = CoordinateTransforms.ToCartesian(new Spherical(0, 0));
            AssertCartesianEquals(0, 0, 1, northPole);

            Cartesian equator0 = CoordinateTransforms.ToCartesian(new Spherical(0, Math.PI / 2));
            AssertCartesianEquals(1, 0, 0, equator0);

            Cartesian equator90 = CoordinateTransforms.ToCartesian(new Spherical(Math.PI / 2, Math.PI / 2));
            AssertCartesianEquals(0, 1, 0, equator90);
        }

        /// <summary>Pins that an arbitrary spherical point round-trips through <see cref="CoordinateTransforms.ToCartesian"/> and <see cref="CoordinateTransforms.ToSpherical"/>.</summary>
        [TestMethod]
        public void ToCartesianThenToSphericalRoundTrips()
        {
            Spherical original = new(Math.PI / 4, Math.PI / 6);

            Cartesian cartesian = CoordinateTransforms.ToCartesian(original);
            Spherical roundTripped = CoordinateTransforms.ToSpherical(cartesian);

            Assert.AreEqual(original.Theta, roundTripped.Theta, Precision13);
            Assert.AreEqual(original.Phi, roundTripped.Phi, Precision13);
        }

        /// <summary>Pins that <see cref="CoordinateTransforms.FromLonLat"/> applies the fixed 93-degree longitude offset at Greenwich, the north pole, and the south pole.</summary>
        [TestMethod]
        public void FromLonLatAppliesTheNinetyThreeDegreeOffset()
        {
            Spherical greenwich = CoordinateTransforms.FromLonLat(new LonLat(0, 0));
            Assert.AreEqual(CoordinateTransforms.DegreesToRadians(93), greenwich.Theta, Precision13);
            Assert.AreEqual(Math.PI / 2, greenwich.Phi, Precision13);

            Spherical northPole = CoordinateTransforms.FromLonLat(new LonLat(0, 90));
            Assert.AreEqual(CoordinateTransforms.DegreesToRadians(93), northPole.Theta, Precision13);
            Assert.AreEqual(0, northPole.Phi, Precision13);

            Spherical southPole = CoordinateTransforms.FromLonLat(new LonLat(0, -90));
            Assert.AreEqual(CoordinateTransforms.DegreesToRadians(93), southPole.Theta, Precision13);
            Assert.AreEqual(Math.PI, southPole.Phi, Precision13);
        }

        /// <summary>Pins that every point in <see cref="TestPointsLonLat"/> round-trips through <see cref="CoordinateTransforms.FromLonLat"/> and <see cref="CoordinateTransforms.ToLonLat"/>, treating 180 and -180 as the equivalent antimeridian longitude.</summary>
        [TestMethod]
        public void FromLonLatThenToLonLatRoundTrips()
        {
            foreach(LonLat lonLat in TestPointsLonLat)
            {
                Spherical spherical = CoordinateTransforms.FromLonLat(lonLat);
                LonLat roundTripped = CoordinateTransforms.ToLonLat(spherical);

                // 180 and -180 are equivalent longitudes (the antimeridian).
                double expectedLongitude = lonLat.Longitude == 180 ? -180 : lonLat.Longitude;

                Assert.AreEqual(expectedLongitude, roundTripped.Longitude, Precision13);
                Assert.AreEqual(lonLat.Latitude, roundTripped.Latitude, Precision13);
            }
        }

        /// <summary>Pins that <see cref="CoordinateTransforms.NormalizeLongitudes"/> leaves a contour that never crosses the antimeridian unchanged.</summary>
        [TestMethod]
        public void NormalizeLongitudesLeavesASimpleContourWithoutWrappingUnchanged()
        {
            LonLat[] contour =
            [
                new(0, 0),
                new(10, 0),
                new(10, 10),
                new(0, 10),
                new(0, 0)
            ];

            LonLat[] normalized = CoordinateTransforms.NormalizeLongitudes(contour);

            Assert.AreSequenceEqual(contour, normalized);
        }

        /// <summary>Pins that <see cref="CoordinateTransforms.NormalizeLongitudes"/> unwraps a contour crossing the antimeridian westward, continuing the longitude past -180 rather than jumping back to positive values.</summary>
        [TestMethod]
        public void NormalizeLongitudesUnwrapsAContourCrossingTheAntimeridianInTheNegativeDirection()
        {
            LonLat[] contour =
            [
                new(-170, 0),
                new(-175, 0),
                new(-180, 0),
                new(175, 0), // This should become -185.
                new(170, 0), // This should become -190.
            ];

            LonLat[] normalized = CoordinateTransforms.NormalizeLongitudes(contour);

            Assert.AreEqual(-185, normalized[3].Longitude, 0.5e-2);
            Assert.AreEqual(-190, normalized[4].Longitude, 0.5e-2);
        }

        /// <summary>Asserts that converting <paramref name="point"/> to barycentric coordinates yields <paramref name="expected"/> at the module's tolerance.</summary>
        private static void AssertBarycentricRoundTrips(Face point, Barycentric expected)
        {
            Barycentric bary = CoordinateTransforms.FaceToBarycentric(point, TriangleVertexA, TriangleVertexB, TriangleVertexC);

            Assert.AreEqual(expected.B0, bary.B0, Precision12);
            Assert.AreEqual(expected.B1, bary.B1, Precision12);
            Assert.AreEqual(expected.B2, bary.B2, Precision12);

            Face result = CoordinateTransforms.BarycentricToFace(bary, TriangleVertexA, TriangleVertexB, TriangleVertexC);
            Assert.AreEqual(point.X, result.X, Precision12);
            Assert.AreEqual(point.Y, result.Y, Precision12);
        }

        /// <summary>Asserts a <see cref="Cartesian"/> point matches expected coordinates at the module's 1e-13 tolerance.</summary>
        private static void AssertCartesianEquals(double expectedX, double expectedY, double expectedZ, Cartesian actual)
        {
            Assert.AreEqual(expectedX, actual.X, Precision13);
            Assert.AreEqual(expectedY, actual.Y, Precision13);
            Assert.AreEqual(expectedZ, actual.Z, Precision13);
        }

        /// <summary>
        /// Builds the transcribed, fixed portion of the face-coordinate test point table:
        /// 31 base points plus 8 small-magnitude "difficult" points, followed by both sets'
        /// coordinate-swapped reflections, in the table's original construction order.
        /// </summary>
        private static Face[] BuildTestPoints()
        {
            Face[] basePoints =
            [
                new(0, 0), // vertex A
                new(1, 0), // vertex B
                new(0, 1), // vertex C
                new(0, 0),
                new(0, 0.001),
                new(0, 0.0001),
                new(0, 0.9),
                new(0, 0.99),
                new(0, 0.999),
                new(0, 0.9991),
                new(0, 0.9999),
                new(0, 0.99999),
                new(0, 0.999999),
                new(0, 0.9999999),
                new(0, 0.99999999),
                new(0, 0.999999999),
                new(0, 0.9999999999),
                new(0, 0.99999999999),
                new(0, 0.999999999999),
                new(0, 0.9999999999999),
                new(0.0, 0.4),
                new(0.2, 0.4),
                new(0.4, 0.4),
                new(0.0, 0.5),
                new(0.0, 0.6),
                new(0.0, 0.9),
                // Difficult points (thrown up by random testing).
                new(0.07014313993250365, 0.9298568600674963),
                new(0.9561208684797726, 0.043821975867140296),
                new(0.9801671359068279, 0.011065580403455679),
                new(0.8565287887089067, 0.14204220534719342),
                new(0.9960934042866861, 0.002268926948860536),
                // Small-magnitude points from the same transcribed table, originally added to exercise
                // a different projection's domain-clamped acos; kept here to preserve the table's
                // point count.
                new(0, 0.1),
                new(0, 0.01),
                new(0, 0.001),
                new(0, 0.0001),
                new(0, 0.00001),
                new(0, 0.000001),
                new(0, 0.0000001),
                new(0, 0.00000001)
            ];

            Face[] points = new Face[basePoints.Length * 2];
            Array.Copy(basePoints, points, basePoints.Length);
            for(int index = 0; index < basePoints.Length; index++)
            {
                points[basePoints.Length + index] = new Face(basePoints[index].Y, basePoints[index].X);
            }

            return points;
        }
    }
}
