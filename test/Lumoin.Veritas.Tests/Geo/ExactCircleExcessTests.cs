using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Gates for the exact point-versus-circle excess predicate behind the covering-circle
/// certification: sign agreement with a rational-arithmetic oracle across the numeric
/// boundaries, the seam invariants the certification scan rides (component counts, exact
/// ordering, measured-equals-written), the high-wall guard throwing instead of signing on
/// non-finite components, and the low-wall quantum degradation pinned as recorded behavior
/// rather than a certified claim. The oracle forms every difference in rational space —
/// never from a pre-rounded double difference — because that composition choice is the one
/// that changes its answer.
/// </summary>
[TestClass]
internal sealed class ExactCircleExcessTests
{
    /// <summary>The sign agrees with the rational oracle across representable on-circle, near-boundary, and mixed-magnitude vectors.</summary>
    /// <param name="pointX">The point's X ordinate.</param>
    /// <param name="pointY">The point's Y ordinate.</param>
    /// <param name="centerX">The center's X ordinate.</param>
    /// <param name="centerY">The center's Y ordinate.</param>
    /// <param name="radius">The circle's radius.</param>
    [TestMethod]
    [DataRow(3.0, 4.0, 0.0, 0.0, 5.0, DisplayName = "pythagorean point exactly on")]
    [DataRow(-3.0, -4.0, 0.0, 0.0, 5.0, DisplayName = "mirrored pythagorean point exactly on")]
    [DataRow(5.0, 0.0, 0.0, 0.0, 5.0, DisplayName = "axis point exactly on")]
    [DataRow(1.0, 2.0, 1.0, 2.0, 0.0, DisplayName = "center point on a zero circle")]
    [DataRow(1.0, 2.0, 1.0, 2.0, 3.0, DisplayName = "center point strictly inside")]
    [DataRow(1.5, 0.0, 0.0, 0.0, 1.0, DisplayName = "outside on the axis")]
    [DataRow(1e150, 0.0, 0.0, 0.0, 1e150, DisplayName = "huge magnitude exactly on")]
    [DataRow(1e-100, 0.0, 0.0, 0.0, 1e-100, DisplayName = "tiny magnitude exactly on")]
    [DataRow(100000000.5, 100000000.5, 100000000.0, 100000000.0, 0.7071067811865476, DisplayName = "offset near-diagonal")]
    public void SignAgreesWithTheRationalOracle(double pointX, double pointY, double centerX, double centerY, double radius)
    {
        int actual = ExactCircleExcess.Sign(new Point2d(pointX, pointY), new Point2d(centerX, centerY), radius);

        Assert.AreEqual(OracleSign(pointX, pointY, centerX, centerY, radius), actual);
    }

    /// <summary>
    /// One-bit perturbations around an exactly-on configuration flip the sign in both
    /// directions, at unit and at large magnitude — the resolution the certification
    /// ratchet leans on.
    /// </summary>
    /// <param name="radius">The exactly-on radius the perturbations step around.</param>
    [TestMethod]
    [DataRow(5.0, DisplayName = "unit-scale radius")]
    [DataRow(1e150, DisplayName = "large-scale radius")]
    [DataRow(1e-100, DisplayName = "small-scale radius")]
    public void OneBitPerturbationsFlipTheSignBothWays(double radius)
    {
        var center = new Point2d(0.0, 0.0);

        Assert.AreEqual(0, ExactCircleExcess.Sign(new Point2d(radius, 0.0), center, radius), "The axis point is exactly on.");
        Assert.AreEqual(1, ExactCircleExcess.Sign(new Point2d(Math.BitIncrement(radius), 0.0), center, radius), "One bit out is strictly outside.");
        Assert.AreEqual(-1, ExactCircleExcess.Sign(new Point2d(Math.BitDecrement(radius), 0.0), center, radius), "One bit in is strictly inside.");
        Assert.AreEqual(1, ExactCircleExcess.Sign(new Point2d(radius, 0.0), center, Math.BitDecrement(radius)), "One bit off the radius leaves the point outside.");
        Assert.AreEqual(-1, ExactCircleExcess.Sign(new Point2d(radius, 0.0), center, Math.BitIncrement(radius)), "One bit onto the radius swallows the point.");
    }

    /// <summary>
    /// A near-cancellation the plain-double determinant cannot resolve: the point sits one
    /// quantum off a circle whose radius matches that quantum, and the exact tail still
    /// answers with the oracle.
    /// </summary>
    [TestMethod]
    public void NearCancellationResolvesExactly()
    {
        double quantum = Math.Pow(2.0, -52);
        var point = new Point2d(1.0 + quantum, 0.0);
        var center = new Point2d(1.0, 0.0);

        Assert.AreEqual(0, ExactCircleExcess.Sign(point, center, quantum), "The offset equals the radius exactly.");
        Assert.AreEqual(OracleSign(point.X, point.Y, center.X, center.Y, quantum), ExactCircleExcess.Sign(point, center, quantum));
        Assert.AreEqual(1, ExactCircleExcess.Sign(point, center, Math.BitDecrement(quantum)), "A one-bit-smaller radius excludes it.");
    }

    /// <summary>
    /// The scan seam's invariants: the squared-distance expansion never exceeds its
    /// declared component bound, the exact ordering ranks squared distances correctly,
    /// and equal distances compare as equal.
    /// </summary>
    [TestMethod]
    public void SquaredDistanceSeamOrdersExactly()
    {
        Span<double> near = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> far = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> mirrored = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> negation = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> difference = stackalloc double[2 * ExactCircleExcess.SquaredDistanceComponents];
        var center = new Point2d(0.3, 0.7);

        int nearCount = ExactCircleExcess.SquaredDistance(new Point2d(1.1, 2.2), center, near);
        int farCount = ExactCircleExcess.SquaredDistance(new Point2d(3.3, -4.4), center, far);
        int mirroredCount = ExactCircleExcess.SquaredDistance(new Point2d(-0.5, 3.4), center, mirrored);

        Assert.IsLessThan(ExactCircleExcess.SquaredDistanceComponents + 1, nearCount, "The component count stays within the declared bound.");
        Assert.IsLessThan(ExactCircleExcess.SquaredDistanceComponents + 1, farCount, "The component count stays within the declared bound.");
        Assert.AreEqual(1, ExactCircleExcess.CompareSquaredDistances(far[..farCount], near[..nearCount], negation, difference), "The farther point ranks above.");
        Assert.AreEqual(-1, ExactCircleExcess.CompareSquaredDistances(near[..nearCount], far[..farCount], negation, difference), "The nearer point ranks below.");
        Assert.AreEqual(0, ExactCircleExcess.CompareSquaredDistances(near[..nearCount], near[..nearCount], negation, difference), "A distance equals itself.");

        //(-0.5, 3.4) and (1.1, 2.2) differ from the center by (-0.8, 2.7) and (0.8, 1.5):
        //distinct squared distances, ranked by the exact comparison, agreeing with the oracle.
        int mirroredAgainstNear = ExactCircleExcess.CompareSquaredDistances(mirrored[..mirroredCount], near[..nearCount], negation, difference);

        Assert.AreEqual(
            (OracleSquaredDistance(-0.5, 3.4, center.X, center.Y) - OracleSquaredDistance(1.1, 2.2, center.X, center.Y)).Sign,
            mirroredAgainstNear,
            "The exact ordering agrees with the rational oracle.");
    }

    /// <summary>
    /// The high wall is guarded, not silent: a squared magnitude that overflows throws the
    /// contract exception instead of signing on a non-finite expansion — the unguarded
    /// path folds to a sign of zero, which would certify an uncovering carrier vacuously.
    /// </summary>
    [TestMethod]
    public void HighWallViolationsThrowInsteadOfSigning()
    {
        bool distanceThrew = false;

        try
        {
            ExactCircleExcess.Sign(new Point2d(1e200, 0.0), new Point2d(-1e200, 0.0), 1.0);
        }
        catch(InvalidOperationException)
        {
            distanceThrew = true;
        }

        Assert.IsTrue(distanceThrew, "An overflowing squared difference must throw the finite-ordinate-contract exception.");

        bool radiusThrew = false;

        try
        {
            ExactCircleExcess.Sign(new Point2d(1.0, 1.0), new Point2d(0.0, 0.0), 1e200);
        }
        catch(InvalidOperationException)
        {
            radiusThrew = true;
        }

        Assert.IsTrue(radiusThrew, "An overflowing radius square must throw the finite-ordinate-contract exception.");
    }

    /// <summary>
    /// The low wall's quantum degradation, pinned as RECORDED BEHAVIOR — not a certified
    /// claim: scaled 3-4-5 constructions whose squares' rounding residuals fall under the
    /// subnormal floor lose their sub-quantum excess silently, and the predicate answers
    /// on-the-circle where the rational oracle answers strictly outside. The vector
    /// documents where the caller contract ends; geo-domain operands sit about 140
    /// decades above it.
    /// </summary>
    [TestMethod]
    public void LowWallQuantumDegradationIsRecordedBehavior()
    {
        double scale = Math.Pow(2.0, -502);
        double lowQuantum = Math.Pow(2.0, -51);
        var point = new Point2d((3.0 + (6.0 * lowQuantum)) * scale, (4.0 - (2.0 * lowQuantum)) * scale);
        var center = new Point2d(0.0, 0.0);
        double radius = (5.0 + (2.0 * lowQuantum)) * scale;

        Assert.AreEqual(1, OracleSign(point.X, point.Y, center.X, center.Y, radius), "The rational oracle sees the sub-quantum excess.");
        Assert.AreEqual(0, ExactCircleExcess.Sign(point, center, radius), "Below the quantum floor the tail loses the excess — the recorded degradation this vector exists to document.");
    }

    /// <summary>The rational oracle's excess sign, every difference formed in rational space.</summary>
    /// <param name="pointX">The point's X ordinate.</param>
    /// <param name="pointY">The point's Y ordinate.</param>
    /// <param name="centerX">The center's X ordinate.</param>
    /// <param name="centerY">The center's Y ordinate.</param>
    /// <param name="radius">The circle's radius.</param>
    /// <returns>The oracle's sign.</returns>
    private static int OracleSign(double pointX, double pointY, double centerX, double centerY, double radius)
    {
        ExactRational radiusExact = ExactRational.FromDouble(radius);
        ExactRational excess = OracleSquaredDistance(pointX, pointY, centerX, centerY) - (radiusExact * radiusExact);

        return excess.Sign;
    }

    /// <summary>The rational oracle's squared distance, both differences formed in rational space.</summary>
    /// <param name="pointX">The point's X ordinate.</param>
    /// <param name="pointY">The point's Y ordinate.</param>
    /// <param name="centerX">The center's X ordinate.</param>
    /// <param name="centerY">The center's Y ordinate.</param>
    /// <returns>The exact squared distance.</returns>
    private static ExactRational OracleSquaredDistance(double pointX, double pointY, double centerX, double centerY)
    {
        ExactRational dx = ExactRational.FromDouble(pointX) - ExactRational.FromDouble(centerX);
        ExactRational dy = ExactRational.FromDouble(pointY) - ExactRational.FromDouble(centerY);

        return (dx * dx) + (dy * dy);
    }
}
