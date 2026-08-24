using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The incircle floor: the agreement sweep against the exact-rational oracle as
/// the primary gate, plus the lattice sign canon over all four query classes, the
/// filter-breaking near-cocircular rows that force the exact tail, the
/// guaranteed-miss cocircular rows, and the far-offset certification row.
/// </summary>
[TestClass]
internal sealed class ExactInCircleTests
{
    /// <summary>The number of quadruples the agreement sweep draws.</summary>
    private const int IterationCount = 1000;

    /// <summary>The number of coordinates one quadruple consumes.</summary>
    private const int QuadrupleCoordinateCount = 8;

    /// <summary>
    /// One carrier for the whole class, matching the reuse posture the
    /// triangulation runs under. The carrier is single-owner state, so every
    /// sweep that shares it runs on one thread.
    /// </summary>
    private static InCircleScratch Scratch { get; } = InCircleScratch.Create();

    /// <summary>The predicate agrees with the exact-rational oracle across the magnitude sweep.</summary>
    [TestMethod]
    public void ExactInCircleMatchesTheRationalOracle()
    {
        ulong state = 517349UL;
        Span<double> coordinates = stackalloc double[QuadrupleCoordinateCount];

        for(int iteration = 0; iteration < IterationCount; iteration++)
        {
            for(int index = 0; index < coordinates.Length; index++)
            {
                coordinates[index] = SpreadCoordinate(ref state);
            }

            if(!TryOrientCounterClockwise(coordinates, out Point2d a, out Point2d b, out Point2d third, out Point2d d))
            {
                continue;
            }

            Assert.AreEqual(
                RationalSign(a, b, third, d),
                ExactInCircle.Sign(Scratch, a, b, third, d),
                $"The predicate and the rational oracle agree on quadruple {iteration}.");
        }
    }

    /// <summary>Single-ulp offsets around a circle point force the exact tail and answer exactly.</summary>
    [TestMethod]
    public void ExactInCircleIsExactAcrossTheNearCocircularSweep()
    {
        //Single-ulp offsets around (-3, 4) on the radius-five circle through
        //the counter-clockwise 3-4-5 anchors: the region where plain doubles
        //cannot judge the sign and every answer must come off the exact tail.
        double ulp = Math.ScaleB(1.0, -53);
        var anchorA = new Point2d(5.0, 0.0);
        var anchorB = new Point2d(4.0, 3.0);
        var anchorC = new Point2d(0.0, 5.0);

        for(int i = 0; i < 16; i++)
        {
            for(int j = 0; j < 16; j++)
            {
                var query = new Point2d(-3.0 + (ulp * i), 4.0 + (ulp * j));
                int folder = ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, query);

                Assert.AreEqual(
                    RationalSign(anchorA, anchorB, anchorC, query),
                    folder,
                    $"The exact tail judges the near-cocircular grid offset ({i}, {j}).");
            }
        }
    }

    /// <summary>The incircle sign follows the inside-positive convention over the lattice table.</summary>
    /// <param name="queryX">The queried X ordinate.</param>
    /// <param name="queryY">The queried Y ordinate.</param>
    /// <param name="expected">The expected sign.</param>
    [TestMethod]
    [DataRow(0.0, 0.0, 1, DisplayName = "The center is strictly inside")]
    [DataRow(1.0, 1.0, 1, DisplayName = "An interior lattice point is inside")]
    [DataRow(6.0, 0.0, -1, DisplayName = "Past the circle on the axis is outside")]
    [DataRow(-4.0, -4.0, -1, DisplayName = "The far quadrant is outside")]
    [DataRow(-3.0, 4.0, 0, DisplayName = "A fourth lattice point of the circle is cocircular")]
    [DataRow(3.0, 4.0, 0, DisplayName = "The mirrored lattice point is cocircular")]
    [DataRow(0.0, -5.0, 0, DisplayName = "The bottom of the circle is cocircular")]
    [DataRow(5.0, 0.0, 0, DisplayName = "A query coincident with a triple vertex is cocircular")]
    public void IncircleAnswersTheLatticeSignTable(double queryX, double queryY, int expected)
    {
        //The counter-clockwise 3-4-5 anchors on the radius-five circle about
        //the origin carry every query class exactly.
        int sign = ExactInCircle.Sign(
            Scratch, new Point2d(5.0, 0.0), new Point2d(4.0, 3.0), new Point2d(0.0, 5.0), new Point2d(queryX, queryY));

        Assert.AreEqual(expected, sign, "The incircle sign follows the inside-positive convention for counter-clockwise triples.");
    }

    /// <summary>One ulp either side of a circle point splits exactly, and the point itself is cocircular.</summary>
    [TestMethod]
    public void IncircleIsExactPastTheFilter()
    {
        //One ulp off the circle in either direction: the filter cannot
        //certify (the determinant sits under the error bound), so these
        //answers are the exact tail's, and they must split the ulp exactly.
        var anchorA = new Point2d(5.0, 0.0);
        var anchorB = new Point2d(4.0, 3.0);
        var anchorC = new Point2d(0.0, 5.0);

        Assert.AreEqual(
            -1,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(-3.0, Math.BitIncrement(4.0))),
            "One ulp above the circle point is exactly outside.");
        Assert.AreEqual(
            1,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(-3.0, Math.BitDecrement(4.0))),
            "One ulp below the circle point is exactly inside.");
        Assert.AreEqual(
            0,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(-3.0, 4.0)),
            "The exact circle point is exactly cocircular.");
    }

    /// <summary>Cocircular quadruples answer zero through the tail at every power-of-two scale.</summary>
    /// <param name="scale">The power-of-two scale the family is drawn at.</param>
    [TestMethod]
    [DataRow(1.0, DisplayName = "Unit scale")]
    [DataRow(1024.0, DisplayName = "Scaled by a power of two")]
    [DataRow(0.0009765625, DisplayName = "Scaled down by a power of two")]
    public void CocircularQuadruplesAnswerZeroThroughTheTail(double scale)
    {
        //A zero determinant can never clear the strict filter bound, so every
        //one of these rows runs the exact tail by construction — the
        //guaranteed-miss family, exact at every power-of-two scale.
        var anchorA = new Point2d(5.0 * scale, 0.0);
        var anchorB = new Point2d(4.0 * scale, 3.0 * scale);
        var anchorC = new Point2d(0.0, 5.0 * scale);

        Assert.AreEqual(
            0,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(-3.0 * scale, 4.0 * scale)),
            "The scaled circle family stays exactly cocircular.");
        Assert.AreEqual(
            0,
            ExactInCircle.Sign(
                Scratch,
                new Point2d(0.0, 0.0),
                new Point2d(2.0 * scale, 0.0),
                new Point2d(2.0 * scale, 2.0 * scale),
                new Point2d(0.0, 2.0 * scale)),
            "An axis-aligned lattice rectangle is exactly cocircular.");
    }

    /// <summary>The difference-form evaluation certifies at a far translation.</summary>
    [TestMethod]
    public void FarOffsetCertificationHolds()
    {
        //The 3-4-5 family translated by 1e8: every ordinate is exactly
        //representable, the differences condition the determinant, and the
        //filter certifies the clear cases — the row that gates the
        //difference-form evaluation at offset.
        const double Offset = 1e8;
        var anchorA = new Point2d(Offset + 5.0, Offset);
        var anchorB = new Point2d(Offset + 4.0, Offset + 3.0);
        var anchorC = new Point2d(Offset, Offset + 5.0);

        Assert.AreEqual(
            1,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(Offset, Offset)),
            "The translated center stays strictly inside.");
        Assert.AreEqual(
            -1,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(Offset + 6.0, Offset)),
            "The translated outside point stays strictly outside.");
        Assert.AreEqual(
            0,
            ExactInCircle.Sign(Scratch, anchorA, anchorB, anchorC, new Point2d(Offset - 3.0, Offset + 4.0)),
            "The translated circle point stays exactly cocircular.");
    }

    /// <summary>
    /// Orders a drawn quadruple into the predicate's counter-clockwise
    /// contract: false skips exactly the collinear triples the contract
    /// excludes, which the mesh never forms.
    /// </summary>
    /// <param name="coordinates">The eight drawn ordinates.</param>
    /// <param name="a">The triple's first position.</param>
    /// <param name="b">The triple's second position.</param>
    /// <param name="third">The triple's third position.</param>
    /// <param name="d">The queried position.</param>
    /// <returns><see langword="false"/> when the triple is collinear.</returns>
    private static bool TryOrientCounterClockwise(ReadOnlySpan<double> coordinates, out Point2d a, out Point2d b, out Point2d third, out Point2d d)
    {
        a = new Point2d(coordinates[0], coordinates[1]);
        b = new Point2d(coordinates[2], coordinates[3]);
        third = new Point2d(coordinates[4], coordinates[5]);
        d = new Point2d(coordinates[6], coordinates[7]);
        int orientation = ExactOrientation.Orient2D(a, b, third);

        if(orientation == 0)
        {
            return false;
        }

        if(orientation < 0)
        {
            (b, third) = (third, b);
        }

        return true;
    }

    /// <summary>The oracle: the incircle determinant evaluated in exact rational arithmetic.</summary>
    /// <param name="a">The triple's first position.</param>
    /// <param name="b">The triple's second position.</param>
    /// <param name="c">The triple's third position.</param>
    /// <param name="d">The queried position.</param>
    /// <returns>The exact three-way sign.</returns>
    private static int RationalSign(Point2d a, Point2d b, Point2d c, Point2d d)
    {
        ExactRational adx = ExactRational.FromDouble(a.X) - ExactRational.FromDouble(d.X);
        ExactRational ady = ExactRational.FromDouble(a.Y) - ExactRational.FromDouble(d.Y);
        ExactRational bdx = ExactRational.FromDouble(b.X) - ExactRational.FromDouble(d.X);
        ExactRational bdy = ExactRational.FromDouble(b.Y) - ExactRational.FromDouble(d.Y);
        ExactRational cdx = ExactRational.FromDouble(c.X) - ExactRational.FromDouble(d.X);
        ExactRational cdy = ExactRational.FromDouble(c.Y) - ExactRational.FromDouble(d.Y);

        ExactRational alift = (adx * adx) + (ady * ady);
        ExactRational blift = (bdx * bdx) + (bdy * bdy);
        ExactRational clift = (cdx * cdx) + (cdy * cdy);

        ExactRational determinant = (alift * ((bdx * cdy) - (cdx * bdy)))
            + (blift * ((cdx * ady) - (adx * cdy)))
            + (clift * ((adx * bdy) - (bdx * ady)));

        return determinant.Sign;
    }

    /// <summary>A coordinate spread across magnitudes from the deterministic bit-mixing state.</summary>
    /// <param name="state">The mixing state, advanced in place.</param>
    /// <returns>The drawn coordinate.</returns>
    private static double SpreadCoordinate(ref ulong state)
    {
        double unit = (NextBitPattern(ref state) >> 11) * (1.0 / (1UL << 53));
        double mantissa = (unit * 2.0) - 1.0;
        int exponent = (int)(NextBitPattern(ref state) % 61) - 30;

        return Math.ScaleB(mantissa, exponent);
    }

    /// <summary>Advances the deterministic bit-mixing state and returns the next 64-bit pattern.</summary>
    /// <param name="state">The mixing state, advanced in place.</param>
    /// <returns>The next 64-bit pattern.</returns>
    private static ulong NextBitPattern(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong mixed = state;
        mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
        mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;

        return mixed ^ (mixed >> 31);
    }
}
