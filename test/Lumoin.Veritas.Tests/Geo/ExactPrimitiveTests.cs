using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The exact-primitive family of the relate engine: orientation sign
/// correctness through the filter and past it, the direction-cross and
/// direction-dot sign tables, the two-product property pair carried onto the
/// folder-local expansion arithmetic so the primitives ship with their own
/// cover, and the segment classifier's branch table including vertical
/// and horizontal collinear runs and the shared-vertex bitwise copy.
/// </summary>
[TestClass]
internal sealed class ExactPrimitiveTests
{
    /// <summary>The orientation sign follows the left-positive convention over the base table.</summary>
    /// <param name="firstX">The base segment start X.</param>
    /// <param name="firstY">The base segment start Y.</param>
    /// <param name="secondX">The base segment end X.</param>
    /// <param name="secondY">The base segment end Y.</param>
    /// <param name="thirdX">The queried point X.</param>
    /// <param name="thirdY">The queried point Y.</param>
    /// <param name="expected">The expected orientation sign.</param>
    [TestMethod]
    [DataRow(0.0, 0.0, 2.0, 0.0, 1.0, 1.0, 1, DisplayName = "Left of the base line")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 1.0, -1.0, -1, DisplayName = "Right of the base line")]
    [DataRow(0.0, 0.0, 2.0, 2.0, 1.0, 1.0, 0, DisplayName = "Exactly collinear")]
    [DataRow(0.0, 0.0, 0.0, 2.0, -1.0, 1.0, 1, DisplayName = "Left of a vertical line")]
    public void OrientationAnswersTheSignTable(double firstX, double firstY, double secondX, double secondY, double thirdX, double thirdY, int expected)
    {
        int sign = ExactOrientation.Orient2D(new Point2d(firstX, firstY), new Point2d(secondX, secondY), new Point2d(thirdX, thirdY));

        Assert.AreEqual(expected, sign, "The orientation sign follows the left-positive convention.");
    }

    /// <summary>One ulp off the diagonal certifies exactly where plain doubles cannot.</summary>
    [TestMethod]
    public void OrientationIsExactPastTheFilter()
    {
        //A filter-breaking triple: the third point sits on the segment's line
        //shifted by one unit in the last place — plain doubles cannot certify
        //the sign, the exact path must.
        var start = new Point2d(0.0, 0.0);
        var end = new Point2d(3.0, 3.0);
        double nudged = Math.BitIncrement(1.0);
        var nearlyOn = new Point2d(1.0, nudged);

        Assert.AreEqual(1, ExactOrientation.Orient2D(start, end, nearlyOn), "One ulp above the diagonal is exactly left.");
        Assert.AreEqual(-1, ExactOrientation.Orient2D(start, end, new Point2d(1.0, Math.BitDecrement(1.0))), "One ulp below is exactly right.");
        Assert.AreEqual(0, ExactOrientation.Orient2D(start, end, new Point2d(1.0, 1.0)), "The exact diagonal point is collinear.");
    }

    /// <summary>The direction-cross sign orders ray directions per the sign table.</summary>
    /// <param name="firstX">The first direction X.</param>
    /// <param name="firstY">The first direction Y.</param>
    /// <param name="secondX">The second direction X.</param>
    /// <param name="secondY">The second direction Y.</param>
    /// <param name="expected">The expected cross sign.</param>
    [TestMethod]
    [DataRow(1.0, 0.0, 0.0, 1.0, 1, DisplayName = "Second direction counter-clockwise of the first")]
    [DataRow(0.0, 1.0, 1.0, 0.0, -1, DisplayName = "Second direction clockwise of the first")]
    [DataRow(1.0, 1.0, 2.0, 2.0, 0, DisplayName = "Shared direction")]
    [DataRow(1.0, 1.0, -2.0, -2.0, 0, DisplayName = "Anti-parallel direction")]
    public void DirectionCrossAnswersTheSignTable(double firstX, double firstY, double secondX, double secondY, int expected)
    {
        var origin = new Point2d(0.0, 0.0);
        int sign = ExactOrientation.DirectionCrossSign(origin, new Point2d(firstX, firstY), origin, new Point2d(secondX, secondY));

        Assert.AreEqual(expected, sign, "The direction-cross sign orders ray directions.");
    }

    /// <summary>The direction-dot sign separates shared from anti-parallel directions.</summary>
    [TestMethod]
    public void DirectionDotSeparatesSharedFromAntiParallel()
    {
        var origin = new Point2d(0.0, 0.0);
        var direction = new Point2d(3.0, 1.0);

        Assert.AreEqual(1, ExactOrientation.DirectionDotSign(origin, direction, origin, direction), "A shared direction dots positive.");
        Assert.AreEqual(-1, ExactOrientation.DirectionDotSign(origin, direction, direction, origin), "An anti-parallel direction dots negative.");
    }

    /// <summary>The fused and split two-product formulations agree bitwise over a spread sweep.</summary>
    [TestMethod]
    public void TwoProductAgreesWithTheSplitFormulation()
    {
        ulong state = 422788UL;

        for(int iteration = 0; iteration < 512; iteration++)
        {
            double a = SpreadDouble(ref state);
            double b = SpreadDouble(ref state);
            (double fusedHigh, double fusedLow) = ExpansionArithmetic.TwoProduct(a, b);
            (double splitHigh, double splitLow) = ExpansionArithmetic.TwoProductBySplit(a, b);

            Assert.IsTrue(
                fusedHigh.Equals(splitHigh) && fusedLow.Equals(splitLow),
                $"The fused and split two-products agree for {a:R} * {b:R}.");
        }
    }

    /// <summary>The two-product's high and low parts represent the exact product.</summary>
    [TestMethod]
    public void TwoProductIsExactOverSpreadMagnitudes()
    {
        ulong state = 902141UL;

        for(int iteration = 0; iteration < 256; iteration++)
        {
            double a = SpreadDouble(ref state);
            double b = SpreadDouble(ref state);
            (double high, double low) = ExpansionArithmetic.TwoProduct(a, b);
            ExactRational exact = ExactRational.FromDouble(a) * ExactRational.FromDouble(b);
            ExactRational represented = ExactRational.FromDouble(high) + ExactRational.FromDouble(low);

            Assert.IsTrue(represented.ValueEquals(exact), $"high + low represents {a:R} * {b:R} exactly.");
        }
    }

    /// <summary>The expansion sum represents the exact sum of its inputs, losing nothing.</summary>
    [TestMethod]
    public void ExpansionSumRepresentsTheExactSum()
    {
        ulong state = 731665UL;
        Span<double> result = stackalloc double[4];

        for(int iteration = 0; iteration < 256; iteration++)
        {
            (double firstHigh, double firstLow) = ExpansionArithmetic.TwoProduct(SpreadDouble(ref state), SpreadDouble(ref state));
            (double secondHigh, double secondLow) = ExpansionArithmetic.TwoProduct(SpreadDouble(ref state), SpreadDouble(ref state));
            Span<double> first = [firstLow, firstHigh];
            Span<double> second = [secondLow, secondHigh];
            int written = ExpansionArithmetic.Sum(first, second, result);

            ExactRational exact = ExactRational.FromDouble(firstHigh) + ExactRational.FromDouble(firstLow)
                + ExactRational.FromDouble(secondHigh) + ExactRational.FromDouble(secondLow);
            ExactRational represented = ExactRational.FromDouble(0.0);

            for(int index = 0; index < written; index++)
            {
                represented += ExactRational.FromDouble(result[index]);
            }

            Assert.IsTrue(represented.ValueEquals(exact), "The expansion sum loses nothing.");
        }
    }

    /// <summary>The segment classifier follows the orientation-sign branch table.</summary>
    /// <param name="firstStartX">The first segment start X.</param>
    /// <param name="firstStartY">The first segment start Y.</param>
    /// <param name="firstEndX">The first segment end X.</param>
    /// <param name="firstEndY">The first segment end Y.</param>
    /// <param name="secondStartX">The second segment start X.</param>
    /// <param name="secondStartY">The second segment start Y.</param>
    /// <param name="secondEndX">The second segment end X.</param>
    /// <param name="secondEndY">The second segment end Y.</param>
    /// <param name="expected">The expected classification.</param>
    [TestMethod]
    [DataRow(0.0, 0.0, 2.0, 2.0, 0.0, 2.0, 2.0, 0.0, SegmentRelation.ProperCrossing, DisplayName = "An X crossing")]
    [DataRow(0.0, 0.0, 2.0, 2.0, 2.0, 2.0, 4.0, 0.0, SegmentRelation.VertexTouch, DisplayName = "A shared endpoint")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 1.0, 0.0, 1.0, 2.0, SegmentRelation.VertexTouch, DisplayName = "An endpoint on an interior")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 1.0, 0.0, 3.0, 0.0, SegmentRelation.CollinearOverlap, DisplayName = "A horizontal collinear overlap")]
    [DataRow(0.0, 0.0, 0.0, 2.0, 0.0, 1.0, 0.0, 3.0, SegmentRelation.CollinearOverlap, DisplayName = "A vertical collinear overlap")]
    [DataRow(0.0, 0.0, 1.0, 0.0, 2.0, 0.0, 3.0, 0.0, SegmentRelation.Disjoint, DisplayName = "Collinear but separated")]
    [DataRow(0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 1.0, 1.0, SegmentRelation.Disjoint, DisplayName = "Parallel and separated")]
    [DataRow(0.0, 0.0, 2.0, 0.0, 3.0, -1.0, 3.0, 1.0, SegmentRelation.Disjoint, DisplayName = "An endpoint zero off the segment box")]
    public void SegmentClassificationAnswersTheBranchTable(
        double firstStartX, double firstStartY, double firstEndX, double firstEndY,
        double secondStartX, double secondStartY, double secondEndX, double secondEndY,
        SegmentRelation expected)
    {
        SegmentIntersection intersection = SegmentTopology.Classify(
            new Point2d(firstStartX, firstStartY), new Point2d(firstEndX, firstEndY),
            new Point2d(secondStartX, secondStartY), new Point2d(secondEndX, secondEndY));

        Assert.AreEqual(expected, intersection.Relation, "The classifier follows the orientation-sign branch table.");
    }

    /// <summary>A vertex-coincident intersection copies the original vertex bitwise, never recomputes.</summary>
    [TestMethod]
    public void VertexTouchCopiesTheOriginalVertexBitwise()
    {
        var shared = new Point2d(Math.BitIncrement(2.0), Math.BitDecrement(3.0));
        SegmentIntersection intersection = SegmentTopology.Classify(
            new Point2d(0.0, 0.0), shared,
            shared, new Point2d(5.0, 0.0));

        Assert.AreEqual(SegmentRelation.VertexTouch, intersection.Relation, "A shared endpoint touches.");
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(shared.X),
            BitConverter.DoubleToInt64Bits(intersection.FirstPoint.X),
            "The touch point is the original vertex, copied bitwise, never recomputed.");
        Assert.AreEqual(
            BitConverter.DoubleToInt64Bits(shared.Y),
            BitConverter.DoubleToInt64Bits(intersection.FirstPoint.Y),
            "The touch ordinates copy bitwise.");
    }

    /// <summary>Collinear-overlap interval endpoints are original vertices.</summary>
    [TestMethod]
    public void CollinearOverlapEndpointsAreOriginalVertices()
    {
        SegmentIntersection intersection = SegmentTopology.Classify(
            new Point2d(0.0, 0.0), new Point2d(10.0, 0.0),
            new Point2d(2.0, 0.0), new Point2d(5.0, 0.0));

        Assert.AreEqual(SegmentRelation.CollinearOverlap, intersection.Relation, "Contained collinear segments overlap.");
        Assert.IsTrue(
            (intersection.FirstPoint.X == 2.0 && intersection.SecondPoint.X == 5.0)
            || (intersection.FirstPoint.X == 5.0 && intersection.SecondPoint.X == 2.0),
            "The overlap stretch is delimited by original vertices.");
    }

    /// <summary>A double with wide, sign-varied magnitude spread from the deterministic bit-mixing state.</summary>
    private static double SpreadDouble(ref ulong state)
    {
        double unit = (NextBitPattern(ref state) >> 11) * (1.0 / (1UL << 53));
        double mantissa = (unit * 2.0) - 1.0;
        int exponent = (int)(NextBitPattern(ref state) % 81) - 40;

        return mantissa * Math.Pow(2.0, exponent);
    }

    /// <summary>Advances the deterministic bit-mixing state and returns the next 64-bit pattern.</summary>
    private static ulong NextBitPattern(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong mixed = state;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;

        return mixed ^ (mixed >> 31);
    }
}
