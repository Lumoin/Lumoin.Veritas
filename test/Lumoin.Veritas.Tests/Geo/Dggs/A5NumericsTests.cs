using Lumoin.Veritas.Geo.Dggs.Numerics;

namespace Lumoin.Veritas.Tests.Geo.Dggs
{
    /// <summary>
    /// Known-value pins for the numeric foundation: the operation formulas whose exact shape is fixture-visible,
    /// asserted against hand-derived values so a later "simplification" that changes semantics fails here
    /// before it can corrupt a whole fixture gate.
    /// </summary>
    [TestClass]
    internal sealed class A5NumericsTests
    {
        /// <summary>Bounds the numeric-foundation comparisons at |diff| &lt; 1e-13.</summary>
        private const double Precision13 = 1e-13;

        /// <summary>The test context, carrying the cancellation token.</summary>
        public TestContext TestContext { get; set; } = null!;

        /// <summary>Pins that JsMath.Round breaks ties toward positive infinity for representative half-integer inputs.</summary>
        [TestMethod]
        public void RoundIsHalfTowardPositiveInfinity()
        {
            Assert.AreEqual(1, JsMath.Round(0.5));
            Assert.AreEqual(2, JsMath.Round(1.5));
            Assert.AreEqual(-1, JsMath.Round(-1.5));
            Assert.AreEqual(-2, JsMath.Round(-2.5));
            Assert.AreEqual(0, JsMath.Round(-0.5));
            Assert.AreEqual(3, JsMath.Round(2.7));
            Assert.AreEqual(-3, JsMath.Round(-2.7));
        }

        /// <summary>Pins that JsMath.Hypot matches known Pythagorean values and propagates NaN/infinity per the expected precedence.</summary>
        [TestMethod]
        public void HypotMatchesKnownValues()
        {
            Assert.AreEqual(5, JsMath.Hypot(3, 4));
            Assert.AreEqual(13, JsMath.Hypot(3, 4, 12));
            Assert.AreEqual(0, JsMath.Hypot(0, 0, 0));
            Assert.AreEqual(double.PositiveInfinity, JsMath.Hypot(double.PositiveInfinity, double.NaN));
            Assert.IsTrue(double.IsNaN(JsMath.Hypot(double.NaN, 1)));
        }

        /// <summary>Pins that JsMath.Hypot avoids overflow for magnitudes near the double range's upper bound.</summary>
        [TestMethod]
        public void HypotIsOverflowSafe()
        {
            double large = 1e308;

            Assert.AreEqual(Math.Sqrt(2) * large, JsMath.Hypot(large, large), 1e293);
        }

        /// <summary>Pins that rotating a vector a quarter turn around the origin proceeds counter-clockwise.</summary>
        [TestMethod]
        public void RotateAroundOriginQuarterTurnIsCounterClockwise()
        {
            Vector2d rotated = new Vector2d(1, 0).RotateAround(new Vector2d(0, 0), Math.PI / 2);

            Assert.AreEqual(0, rotated.X, Precision13);
            Assert.AreEqual(1, rotated.Y, Precision13);
        }

        /// <summary>Pins that Matrix2x2d.FromRotation applies its rotation under the column-major transform convention.</summary>
        [TestMethod]
        public void RotationMatrixIsColumnMajor()
        {
            Matrix2x2d rotation = Matrix2x2d.FromRotation(Math.PI / 2);

            Vector2d transformed = rotation.Transform(new Vector2d(1, 0));

            Assert.AreEqual(0, transformed.X, Precision13);
            Assert.AreEqual(1, transformed.Y, Precision13);
        }

        /// <summary>Pins that inverting a rotation matrix and transforming with it undoes the original rotation.</summary>
        [TestMethod]
        public void InvertUndoesTheRotation()
        {
            Matrix2x2d rotation = Matrix2x2d.FromRotation(0.7);
            Vector2d point = new(0.3, -1.2);

            Vector2d roundTripped = rotation.Invert().Transform(rotation.Transform(point));

            Assert.AreEqual(point.X, roundTripped.X, Precision13);
            Assert.AreEqual(point.Y, roundTripped.Y, Precision13);
        }

        /// <summary>Pins that Vector3d.Angle returns the correct angle for parallel, perpendicular, antipodal, and zero-magnitude vector pairs.</summary>
        [TestMethod]
        public void AngleHandlesParallelPerpendicularAndAntipodal()
        {
            Vector3d x = new(1, 0, 0);

            Assert.AreEqual(0, Vector3d.Angle(x, x), Precision13);
            Assert.AreEqual(Math.PI / 2, Vector3d.Angle(x, new Vector3d(0, 1, 0)), Precision13);
            Assert.AreEqual(Math.PI, Vector3d.Angle(x, new Vector3d(-1, 0, 0)), Precision13);

            // Zero magnitude short-circuits the cosine to 0, so the angle against a zero vector is acos(0).
            Assert.AreEqual(Math.PI / 2, Vector3d.Angle(x, default));
        }

        /// <summary>Pins that normalizing the zero vector returns the zero vector rather than dividing by zero.</summary>
        [TestMethod]
        public void NormalizeOfZeroVectorIsZero()
        {
            Vector3d normalized = default(Vector3d).Normalize();

            Assert.AreEqual(default, normalized);
        }

        /// <summary>Pins that QuaternionD.RotationTo produces a rotation that carries the first vector onto the second.</summary>
        [TestMethod]
        public void RotationToTakesTheFirstVectorToTheSecond()
        {
            Vector3d a = new(1, 0, 0);
            Vector3d b = new(0, 1, 0);

            Vector3d rotated = a.Transform(QuaternionD.RotationTo(a, b));

            Assert.AreEqual(b.X, rotated.X, Precision13);
            Assert.AreEqual(b.Y, rotated.Y, Precision13);
            Assert.AreEqual(b.Z, rotated.Z, Precision13);
        }

        /// <summary>Pins that QuaternionD.RotationTo falls back correctly for antipodal vector pairs.</summary>
        [TestMethod]
        public void RotationToHandlesTheAntipodalFallback()
        {
            Vector3d a = new(1, 0, 0);
            Vector3d b = new(-1, 0, 0);

            Vector3d rotated = a.Transform(QuaternionD.RotationTo(a, b));

            Assert.AreEqual(b.X, rotated.X, Precision13);
            Assert.AreEqual(b.Y, rotated.Y, Precision13);
            Assert.AreEqual(b.Z, rotated.Z, Precision13);

            Vector3d up = new(0, 1, 0);
            Vector3d down = new(0, -1, 0);

            Vector3d flipped = up.Transform(QuaternionD.RotationTo(up, down));

            Assert.AreEqual(down.X, flipped.X, Precision13);
            Assert.AreEqual(down.Y, flipped.Y, Precision13);
            Assert.AreEqual(down.Z, flipped.Z, Precision13);
        }

        /// <summary>Pins that QuaternionD.RotationTo of a vector with itself is the identity quaternion.</summary>
        [TestMethod]
        public void RotationToOfNearIdenticalVectorsIsIdentity()
        {
            Vector3d a = new(1, 0, 0);

            Assert.AreEqual(QuaternionD.Identity, QuaternionD.RotationTo(a, a));
        }

        /// <summary>Pins that Vector3d.Lerp computes via the add-scaled-difference form rather than a naive weighted sum.</summary>
        [TestMethod]
        public void LerpUsesTheAddScaledDifferenceForm()
        {
            double a = 0.1;
            double b = 0.30000000000000004;
            double t = 0.7;

            Vector3d result = Vector3d.Lerp(new Vector3d(a, 0, 0), new Vector3d(b, 0, 0), t);

            Assert.AreEqual(a + (t * (b - a)), result.X);
        }
    }
}
