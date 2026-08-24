using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Dggs.Geometry;
using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Pins for <see cref="PentagonConstants"/>: the five pentagon interior angles (exact literals),
    /// the five pentagon vertices, the triangle vertices and angle, the pentagon/triangle shapes, and
    /// the basis matrix pair. The angle constants are plain literals and asserted bit-exact; every
    /// vertex and matrix value is trigonometry-derived (through <c>cos</c>/<c>sin</c>/<c>atan2</c>
    /// chains), so those are asserted at |diff| &lt; 1e-13, the cross-runtime libm-divergence tolerance
    /// this project uses elsewhere for trig-derived constants.
    /// </summary>
    [TestClass]
    internal sealed class A5PentagonConstantsTests
    {
        /// <summary>Bounds trigonometry-derived pentagon constant comparisons at |diff| &lt; 1e-13.</summary>
        private const double Precision13 = 1e-13;

        /// <summary>Bounds the basis/inverse round-trip composition comparisons at |diff| &lt; 1e-10.</summary>
        private const double Precision10 = 1e-10;

        /// <summary>Pins the five pentagon interior angle constants to their expected literal values.</summary>
        [TestMethod]
        public void PentagonAnglesHaveTheExpectedLiteralValues()
        {
            // Read through an array rather than comparing the const fields directly against literals:
            // both sides would otherwise be compile-time constants, which the analyzer (correctly)
            // flags as a tautological assertion.
            double[] expected = [72, 127.94543761193603, 108, 82.29202980963508, 149.7625318412527];
            double[] actual = [PentagonConstants.AngleA, PentagonConstants.AngleB, PentagonConstants.AngleC, PentagonConstants.AngleD, PentagonConstants.AngleE];

            Assert.AreSequenceEqual(expected, actual);
        }

        /// <summary>Pins the five pentagon vertex constants to their expected coordinates.</summary>
        [TestMethod]
        public void PentagonVerticesMatchTheExpectedCoordinates()
        {
            AssertFaceEquals(0, 0, PentagonConstants.VertexA);
            AssertFaceEquals(0.1993818474311588, 0.3754138223914238, PentagonConstants.VertexB);
            AssertFaceEquals(0.6180339887498949, 0.4490279765795854, PentagonConstants.VertexC);
            AssertFaceEquals(0.8174158361810537, 0.0736141541881617, PentagonConstants.VertexD);
            AssertFaceEquals(0.418652141318736, -0.07361415418816161, PentagonConstants.VertexE);
        }

        /// <summary>Pins that the constant Pentagon shape's vertices match the expected coordinates.</summary>
        [TestMethod]
        public void PentagonShapeHasTheExpectedVertices()
        {
            Face[] expected =
            [
                new(0, 0),
                new(0.1993818474311588, 0.3754138223914238),
                new(0.6180339887498949, 0.4490279765795854),
                new(0.8174158361810537, 0.0736141541881617),
                new(0.418652141318736, -0.07361415418816161)
            ];

            ReadOnlySpan<Face> vertices = PentagonConstants.Pentagon.GetVertices();
            Assert.HasCount(expected.Length, vertices);

            for(int index = 0; index < expected.Length; index++)
            {
                AssertFaceEquals(expected[index].X, expected[index].Y, vertices[index]);
            }
        }

        /// <summary>Pins the triangle vertex and angle constants to their expected values.</summary>
        [TestMethod]
        public void TriangleVerticesAndAngleMatchTheExpectedValues()
        {
            AssertFaceEquals(0, 0, PentagonConstants.VertexU);
            AssertFaceEquals(0.6180339887498949, 0.4490279765795854, PentagonConstants.VertexV);
            AssertFaceEquals(0.6180339887498949, -0.4490279765795854, PentagonConstants.VertexW);
            Assert.AreEqual(0.6283185307179586, PentagonConstants.AngleV, Precision13);
        }

        /// <summary>Pins that the constant Triangle shape's vertices match the expected coordinates.</summary>
        [TestMethod]
        public void TriangleShapeHasTheExpectedVertices()
        {
            Face[] expected =
            [
                new(0, 0),
                new(0.6180339887498949, 0.4490279765795854),
                new(0.6180339887498949, -0.4490279765795854)
            ];

            ReadOnlySpan<Face> vertices = PentagonConstants.Triangle.GetVertices();
            Assert.HasCount(expected.Length, vertices);

            for(int index = 0; index < expected.Length; index++)
            {
                AssertFaceEquals(expected[index].X, expected[index].Y, vertices[index]);
            }
        }

        /// <summary>Pins the basis and basis-inverse matrix constants to their expected values and that composing them recovers the identity.</summary>
        [TestMethod]
        public void BasisAndItsInverseMatchTheExpectedMatricesAndComposeToTheIdentity()
        {
            Matrix2x2d basis = PentagonConstants.Basis;
            Assert.AreEqual(0.6180339887498949, basis.M0, Precision13);
            Assert.AreEqual(0.4490279765795854, basis.M1, Precision13);
            Assert.AreEqual(0.6180339887498949, basis.M2, Precision13);
            Assert.AreEqual(-0.4490279765795854, basis.M3, Precision13);

            Matrix2x2d basisInverse = PentagonConstants.BasisInverse;
            Assert.AreEqual(0.8090169943749475, basisInverse.M0, Precision13);
            Assert.AreEqual(0.8090169943749475, basisInverse.M1, Precision13);
            Assert.AreEqual(1.1135163644116068, basisInverse.M2, Precision13);
            Assert.AreEqual(-1.1135163644116068, basisInverse.M3, Precision13);

            Vector2d columnZeroThroughInverse = basisInverse.Transform(basis.Transform(new Vector2d(1, 0)));
            Vector2d columnOneThroughInverse = basisInverse.Transform(basis.Transform(new Vector2d(0, 1)));
            Assert.AreEqual(1, columnZeroThroughInverse.X, Precision10);
            Assert.AreEqual(0, columnZeroThroughInverse.Y, Precision10);
            Assert.AreEqual(0, columnOneThroughInverse.X, Precision10);
            Assert.AreEqual(1, columnOneThroughInverse.Y, Precision10);
        }

        /// <summary>Asserts a <see cref="Face"/> matches expected coordinates at the module's 1e-13 tolerance.</summary>
        private static void AssertFaceEquals(double expectedX, double expectedY, Face actual)
        {
            Assert.AreEqual(expectedX, actual.X, Precision13);
            Assert.AreEqual(expectedY, actual.Y, Precision13);
        }
    }
}
