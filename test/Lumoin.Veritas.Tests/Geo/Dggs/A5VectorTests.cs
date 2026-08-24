using Lumoin.Veritas.Geo.Dggs.Numerics;
using Lumoin.Veritas.Geo.Dggs.Utils;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Spherical vector primitive cases: inline cases at tolerance |diff| &lt; 0.5e-6.
    /// </summary>
    [TestClass]
    internal sealed class A5VectorTests
    {
        /// <summary>Bounds the spherical vector primitive comparisons at |diff| &lt; 0.5e-6.</summary>
        private const double Precision6 = 0.5e-6;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that the vector difference of two identical vectors is zero.</summary>
        [TestMethod]
        public void VectorDifferenceOfIdenticalVectorsIsZero()
        {
            double result = VectorUtilities.VectorDifference(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0));

            Assert.AreEqual(0, result, Precision6);
        }

        /// <summary>Pins that the vector difference of two perpendicular unit vectors is the square root of one half.</summary>
        [TestMethod]
        public void VectorDifferenceOfPerpendicularVectorsIsSqrtHalf()
        {
            double result = VectorUtilities.VectorDifference(new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));

            Assert.AreEqual(Math.Sqrt(0.5), result, Precision6);
        }

        /// <summary>Pins that the vector difference of two nearly-identical vectors stays small and positive.</summary>
        [TestMethod]
        public void VectorDifferenceHandlesSmallAngles()
        {
            Vector3d b = new Vector3d(0.999, 0.001, 0).Normalize();

            double result = VectorUtilities.VectorDifference(new Vector3d(1, 0, 0), b);

            Assert.IsGreaterThan(0.0, result);
            Assert.IsLessThan(0.1, result);
        }

        /// <summary>Pins that the quadruple product of four linearly independent vectors is non-zero.</summary>
        [TestMethod]
        public void QuadrupleProductOfSpanningVectorsIsNonZero()
        {
            Vector3d a = new(1, 0, 0);
            Vector3d b = new(0, 1, 0);
            Vector3d c = new(0, 0, 1);
            Vector3d d = new Vector3d(1, 1, 1).Normalize();

            Vector3d result = VectorUtilities.QuadrupleProduct(a, b, c, d);

            Assert.IsTrue(result.X != 0 || result.Y != 0 || result.Z != 0);
        }

        /// <summary>Pins that the slerp midpoint between two perpendicular unit vectors lies on the diagonal between them.</summary>
        [TestMethod]
        public void SlerpMidpointOfPerpendicularVectorsIsDiagonal()
        {
            Vector3d result = VectorUtilities.Slerp(new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), 0.5);

            Assert.AreEqual(1 / Math.Sqrt(2), result.X, Precision6);
            Assert.AreEqual(1 / Math.Sqrt(2), result.Y, Precision6);
            Assert.AreEqual(0, result.Z, Precision6);
        }

        /// <summary>Pins that slerp at t=0 and t=1 returns the two input vectors unchanged.</summary>
        [TestMethod]
        public void SlerpEndpointsReturnTheInputs()
        {
            Vector3d a = new(1, 0, 0);
            Vector3d b = new(0, 1, 0);

            Vector3d atStart = VectorUtilities.Slerp(a, b, 0);
            Vector3d atEnd = VectorUtilities.Slerp(a, b, 1);

            Assert.AreEqual(1, atStart.X, Precision6);
            Assert.AreEqual(0, atStart.Y, Precision6);
            Assert.AreEqual(0, atEnd.X, Precision6);
            Assert.AreEqual(1, atEnd.Y, Precision6);
        }

        /// <summary>Pins that slerping between two identical vectors falls back to returning that vector unchanged.</summary>
        [TestMethod]
        public void SlerpOfIdenticalVectorsUsesTheLinearFallback()
        {
            Vector3d result = VectorUtilities.Slerp(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0), 0.5);

            Assert.AreEqual(1, result.X, Precision6);
            Assert.AreEqual(0, result.Y, Precision6);
            Assert.AreEqual(0, result.Z, Precision6);
        }

        /// <summary>Pins that slerp progresses monotonically across the arc between two perpendicular vectors.</summary>
        [TestMethod]
        public void SlerpInterpolatesMonotonicallyAcrossTheArc()
        {
            Vector3d a = new(1, 0, 0);
            Vector3d b = new(0, 1, 0);

            Vector3d quarter = VectorUtilities.Slerp(a, b, 0.25);
            Vector3d threeQuarter = VectorUtilities.Slerp(a, b, 0.75);

            Assert.IsGreaterThan(quarter.Y, quarter.X);
            Assert.IsGreaterThan(threeQuarter.X, threeQuarter.Y);
        }

        /// <summary>Pins that slerp using a precomputed context matches the direct form at every sampled step.</summary>
        [TestMethod]
        public void SlerpWithPrecomputedContextMatchesTheDirectForm()
        {
            Vector3d a = new(1, 0, 0);
            Vector3d b = new Vector3d(0.5, 0.5, 0.70710678118654752).Normalize();
            SlerpContext context = VectorUtilities.PrecomputeSlerp(a, b);

            for(int step = 0; step <= 4; step++)
            {
                double t = step / 4.0;
                Vector3d direct = VectorUtilities.Slerp(a, b, t);
                Vector3d contextual = VectorUtilities.Slerp(a, b, t, context);
                Assert.AreEqual(direct, contextual);
            }
        }
    }
}
