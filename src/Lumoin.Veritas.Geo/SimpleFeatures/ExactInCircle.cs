using System;
using System.Diagnostics;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The exact incircle sign predicate: adaptive evaluation in hardware
/// doubles behind a permanent-weighted static filter, escalating to exact
/// expansion arithmetic over the original coordinates only when the filter
/// cannot certify the sign. The returned sign is exact for the original
/// coordinates within the stated magnitude domain: differences form inside
/// the tracked computation as exact two-component expansions, never as
/// pre-rounded doubles. Unlike <see cref="ExactOrientation"/>'s fully
/// stack-allocated predicates, the degree-four tail's large buffers ride a
/// caller-held <see cref="InCircleScratch"/> — plain heap, allocated once and
/// reused, never pooled.
/// </summary>
/// <remarks>
/// <para>
/// The filter constant follows the published A-stage bound for this
/// determinant shape: with <c>ε</c> the machine epsilon of round-to-nearest
/// binary64, a computed incircle determinant whose magnitude strictly
/// exceeds <c>(10 + 96ε)ε</c> times the permanent — the lift-weighted sum of
/// the cross products' absolute magnitudes — has the exact sign. The
/// certification operator is strict (<c>&gt;</c>, unlike the orientation
/// paths' <c>&gt;=</c>): a zero determinant can therefore never certify, so
/// exactly cocircular inputs always take the exact tail and answer an exact
/// zero.
/// </para>
/// <para>
/// The exactness contract is two-sided: coordinate differences past
/// <c>|Δ| ≈ 6.8e76</c> (<c>(double.MaxValue / 8)^¼</c>) overflow the
/// degree-four products, and below <c>|Δ| ≈ 2e-77</c> a near-cocircular
/// determinant can underflow past the subnormal floor — outside those walls
/// the sign typically degrades to zero. Finite ordinates inside the walls
/// are caller contract.
/// </para>
/// </remarks>
internal static class ExactInCircle
{
    /// <summary>The machine epsilon of binary64 round-to-nearest: <c>2^-53</c>.</summary>
    private const double Epsilon = 1.1102230246251565e-16;

    /// <summary>The static filter bound for the incircle determinant's permanent.</summary>
    private const double FilterBound = (10.0 + (96.0 * Epsilon)) * Epsilon;

    /// <summary>An exact coordinate difference: at most two components.</summary>
    private const int DifferenceCapacity = 2;

    /// <summary>One product of two differences: <c>ProductCapacity(2, 2)</c> = 8.</summary>
    private const int CrossProductCapacity = 8;

    /// <summary>
    /// The shared scratch of the twelve difference-by-difference products:
    /// <c>ProductScratchCapacity(2, 2)</c> = <c>ScaleCapacity(2) +
    /// ProductCapacity(2, 2)</c> = 4 + 8.
    /// </summary>
    private const int SmallProductScratchCapacity = 12;

    /// <summary>A lift or cross-product pair accumulator: <c>SumCapacity(8, 8)</c> = 16.</summary>
    private const int AccumulatorCapacity = 16;

    /// <summary>
    /// The sign of the incircle determinant for the strict counter-clockwise
    /// triple <paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>
    /// and the query point <paramref name="d"/>: +1 when <paramref name="d"/>
    /// lies strictly inside the triple's circumcircle, −1 strictly outside,
    /// 0 exactly cocircular. The counter-clockwise premise is contract — the
    /// triangulation's strict-turn invariant guarantees it — and a clockwise
    /// triple would silently negate every answer, so the premise is guarded in
    /// debug builds.
    /// </summary>
    /// <param name="scratch">The caller-held buffer carrier the exact tail rides.</param>
    /// <param name="a">The triple's first position.</param>
    /// <param name="b">The triple's second position.</param>
    /// <param name="c">The triple's third position.</param>
    /// <param name="d">The queried position.</param>
    /// <returns>The exact three-way sign.</returns>
    public static int Sign(InCircleScratch scratch, Point2d a, Point2d b, Point2d c, Point2d d)
    {
        Debug.Assert(
            ExactOrientation.Orient2D(a, b, c) > 0,
            "The incircle sign requires a strict counter-clockwise (a, b, c) triple.");

        double adx = a.X - d.X;
        double ady = a.Y - d.Y;
        double bdx = b.X - d.X;
        double bdy = b.Y - d.Y;
        double cdx = c.X - d.X;
        double cdy = c.Y - d.Y;

        double bdxcdy = bdx * cdy;
        double cdxbdy = cdx * bdy;
        double alift = (adx * adx) + (ady * ady);

        double cdxady = cdx * ady;
        double adxcdy = adx * cdy;
        double blift = (bdx * bdx) + (bdy * bdy);

        double adxbdy = adx * bdy;
        double bdxady = bdx * ady;
        double clift = (cdx * cdx) + (cdy * cdy);

        double det = (alift * (bdxcdy - cdxbdy))
            + (blift * (cdxady - adxcdy))
            + (clift * (adxbdy - bdxady));

        //The permanent bounds the accumulated rounding of det: each lift
        //weights the absolute magnitudes of the two cross products it scales.
        double permanent = ((Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * alift)
            + ((Math.Abs(cdxady) + Math.Abs(adxcdy)) * blift)
            + ((Math.Abs(adxbdy) + Math.Abs(bdxady)) * clift);

        double errorBound = FilterBound * permanent;

        if(det > errorBound || -det > errorBound)
        {
            return SignOf(det);
        }

        return SignExact(scratch, a, b, c, d);
    }

    /// <summary>
    /// The exact tail: the same difference-form determinant in expansion
    /// arithmetic over the original coordinates. Each difference is an exact
    /// two-component expansion, the lifts and cross-product pairs accumulate
    /// on the stack (exactly 144 doubles), and the three lift-weighted terms
    /// with their two final sums ride the caller's scratch carrier.
    /// </summary>
    /// <param name="scratch">The caller-held buffer carrier.</param>
    /// <param name="a">The triple's first position.</param>
    /// <param name="b">The triple's second position.</param>
    /// <param name="c">The triple's third position.</param>
    /// <param name="d">The queried position.</param>
    /// <returns>The exact three-way sign.</returns>
    private static int SignExact(InCircleScratch scratch, Point2d a, Point2d b, Point2d c, Point2d d)
    {
        Span<double> adx = stackalloc double[DifferenceCapacity];
        Span<double> ady = stackalloc double[DifferenceCapacity];
        Span<double> bdx = stackalloc double[DifferenceCapacity];
        Span<double> bdy = stackalloc double[DifferenceCapacity];
        Span<double> cdx = stackalloc double[DifferenceCapacity];
        Span<double> cdy = stackalloc double[DifferenceCapacity];

        int adxLength = Difference(a.X, d.X, adx);
        int adyLength = Difference(a.Y, d.Y, ady);
        int bdxLength = Difference(b.X, d.X, bdx);
        int bdyLength = Difference(b.Y, d.Y, bdy);
        int cdxLength = Difference(c.X, d.X, cdx);
        int cdyLength = Difference(c.Y, d.Y, cdy);

        Span<double> productScratch = stackalloc double[SmallProductScratchCapacity];
        Span<double> firstProduct = stackalloc double[CrossProductCapacity];
        Span<double> secondProduct = stackalloc double[CrossProductCapacity];
        Span<double> negationScratch = stackalloc double[CrossProductCapacity];

        //Lifts: alift = adx² + ady², and likewise for b and c.
        Span<double> alift = stackalloc double[AccumulatorCapacity];
        Span<double> blift = stackalloc double[AccumulatorCapacity];
        Span<double> clift = stackalloc double[AccumulatorCapacity];

        int firstLength = ExpansionArithmetic.Product(adx[..adxLength], adx[..adxLength], productScratch, firstProduct);
        int secondLength = ExpansionArithmetic.Product(ady[..adyLength], ady[..adyLength], productScratch, secondProduct);
        int aliftLength = ExpansionArithmetic.Sum(firstProduct[..firstLength], secondProduct[..secondLength], alift);

        firstLength = ExpansionArithmetic.Product(bdx[..bdxLength], bdx[..bdxLength], productScratch, firstProduct);
        secondLength = ExpansionArithmetic.Product(bdy[..bdyLength], bdy[..bdyLength], productScratch, secondProduct);
        int bliftLength = ExpansionArithmetic.Sum(firstProduct[..firstLength], secondProduct[..secondLength], blift);

        firstLength = ExpansionArithmetic.Product(cdx[..cdxLength], cdx[..cdxLength], productScratch, firstProduct);
        secondLength = ExpansionArithmetic.Product(cdy[..cdyLength], cdy[..cdyLength], productScratch, secondProduct);
        int cliftLength = ExpansionArithmetic.Sum(firstProduct[..firstLength], secondProduct[..secondLength], clift);

        //Cross-product pairs, each a difference of two products of differences.
        Span<double> pairA = stackalloc double[AccumulatorCapacity];
        Span<double> pairB = stackalloc double[AccumulatorCapacity];
        Span<double> pairC = stackalloc double[AccumulatorCapacity];

        firstLength = ExpansionArithmetic.Product(bdx[..bdxLength], cdy[..cdyLength], productScratch, firstProduct);
        secondLength = ExpansionArithmetic.Product(cdx[..cdxLength], bdy[..bdyLength], productScratch, secondProduct);
        int pairALength = SubtractInto(firstProduct[..firstLength], secondProduct[..secondLength], negationScratch, pairA);

        firstLength = ExpansionArithmetic.Product(cdx[..cdxLength], ady[..adyLength], productScratch, firstProduct);
        secondLength = ExpansionArithmetic.Product(adx[..adxLength], cdy[..cdyLength], productScratch, secondProduct);
        int pairBLength = SubtractInto(firstProduct[..firstLength], secondProduct[..secondLength], negationScratch, pairB);

        firstLength = ExpansionArithmetic.Product(adx[..adxLength], bdy[..bdyLength], productScratch, firstProduct);
        secondLength = ExpansionArithmetic.Product(bdx[..bdxLength], ady[..adyLength], productScratch, secondProduct);
        int pairCLength = SubtractInto(firstProduct[..firstLength], secondProduct[..secondLength], negationScratch, pairC);

        //det = alift·pairA + blift·pairB + clift·pairC over the carrier's
        //heap buffers: three products of sixteen-component expansions and
        //two exact sums.
        int termALength = ExpansionArithmetic.Product(alift[..aliftLength], pairA[..pairALength], scratch.TermScratch, scratch.TermA);
        int termBLength = ExpansionArithmetic.Product(blift[..bliftLength], pairB[..pairBLength], scratch.TermScratch, scratch.TermB);
        int termCLength = ExpansionArithmetic.Product(clift[..cliftLength], pairC[..pairCLength], scratch.TermScratch, scratch.TermC);

        int partialLength = ExpansionArithmetic.Sum(
            scratch.TermA.AsSpan(0, termALength), scratch.TermB.AsSpan(0, termBLength), scratch.PartialSum);
        int determinantLength = ExpansionArithmetic.Sum(
            scratch.PartialSum.AsSpan(0, partialLength), scratch.TermC.AsSpan(0, termCLength), scratch.Determinant);

        return ExpansionArithmetic.Sign(scratch.Determinant.AsSpan(0, determinantLength));
    }

    /// <summary>
    /// Writes <c>a − b</c> as an exact, zero-eliminated expansion into
    /// <paramref name="result"/> (capacity two) and returns its component
    /// count: the roundoff first, the rounded difference last.
    /// </summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <param name="result">The destination buffer.</param>
    /// <returns>The written component count.</returns>
    private static int Difference(double a, double b, Span<double> result)
    {
        (double high, double low) = ExpansionArithmetic.TwoDiff(a, b);
        int written = 0;

        if(low != 0.0)
        {
            result[written] = low;
            written++;
        }

        if(high != 0.0 || written == 0)
        {
            result[written] = high;
            written++;
        }

        return written;
    }

    /// <summary>
    /// Writes <c>e − f</c> into <paramref name="result"/> by negating
    /// <paramref name="f"/> into <paramref name="negationScratch"/> — exact,
    /// since negation only flips component signs — and summing. Returns the
    /// component count of the result.
    /// </summary>
    /// <param name="e">The minuend expansion.</param>
    /// <param name="f">The subtrahend expansion.</param>
    /// <param name="negationScratch">The buffer the negated subtrahend is written into.</param>
    /// <param name="result">The destination buffer.</param>
    /// <returns>The written component count.</returns>
    private static int SubtractInto(
        ReadOnlySpan<double> e,
        ReadOnlySpan<double> f,
        Span<double> negationScratch,
        Span<double> result)
    {
        Span<double> negated = negationScratch[..f.Length];

        for(int index = 0; index < f.Length; index++)
        {
            negated[index] = -f[index];
        }

        return ExpansionArithmetic.Sum(e, negated, result);
    }

    /// <summary>The three-way sign of a plain double.</summary>
    /// <param name="value">The value to classify.</param>
    /// <returns>The three-way sign.</returns>
    private static int SignOf(double value)
    {
        if(value > 0.0)
        {
            return 1;
        }

        if(value < 0.0)
        {
            return -1;
        }

        return 0;
    }
}
