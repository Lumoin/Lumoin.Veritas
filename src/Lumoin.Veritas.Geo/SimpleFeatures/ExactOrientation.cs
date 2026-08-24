using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The relate engine's exact-sign predicates: adaptive evaluation in hardware
/// doubles behind a static error-bound filter, escalating to exact expansion
/// arithmetic only when the filter cannot certify the sign. Every returned
/// sign is exact for the original coordinates — the difference-forming steps
/// happen inside the tracked computation, which is what the folder's
/// remainder-reduction determinant cannot offer its callers (it is exact only
/// for the already-rounded differences it receives). Every predicate here is
/// fully stack-allocated; nothing pools — the folder's one heap-allocating
/// exact path is <see cref="ExactInCircle"/>'s degree-four tail, whose
/// caller-held carrier states its own posture.
/// </summary>
internal static class ExactOrientation
{
    /// <summary>The machine epsilon of binary64 round-to-nearest: <c>2^-53</c>.</summary>
    private const double Epsilon = 1.1102230246251565e-16;

    /// <summary>
    /// The static filter bound for a determinant of two difference products:
    /// a computed value whose magnitude exceeds this multiple of the two
    /// products' magnitude sum has the exact sign.
    /// </summary>
    private const double DeterminantBound = (3.0 + (16.0 * Epsilon)) * Epsilon;

    /// <summary>
    /// Capacity for the exact accumulations: at most four two-component
    /// products merge pairwise into expansions of at most sixteen components.
    /// </summary>
    private const int ExactCapacity = 16;

    /// <summary>
    /// The orientation of point <paramref name="c"/> relative to the directed
    /// line from <paramref name="a"/> to <paramref name="b"/>: +1 to the
    /// left (counter-clockwise), −1 to the right, 0 exactly collinear.
    /// </summary>
    public static int Orient2D(Point2d a, Point2d b, Point2d c)
    {
        double detLeft = (a.X - c.X) * (b.Y - c.Y);
        double detRight = (a.Y - c.Y) * (b.X - c.X);

        if(!TryFilteredSign(detLeft, detRight, out int sign))
        {
            return Orient2DExact(a, b, c);
        }

        return sign;
    }

    /// <summary>
    /// The exact sign of the cross product of two direction vectors,
    /// <c>(toFirst − fromFirst) × (toSecond − fromSecond)</c>: +1 when the
    /// second direction lies counter-clockwise of the first, −1 clockwise,
    /// 0 exactly parallel or anti-parallel. This is the fan-ordering
    /// primitive: ray directions compare through it so no computed node
    /// coordinate ever carries an ordering sign.
    /// </summary>
    public static int DirectionCrossSign(Point2d fromFirst, Point2d toFirst, Point2d fromSecond, Point2d toSecond)
    {
        double detLeft = (toFirst.X - fromFirst.X) * (toSecond.Y - fromSecond.Y);
        double detRight = (toFirst.Y - fromFirst.Y) * (toSecond.X - fromSecond.X);

        if(!TryFilteredSign(detLeft, detRight, out int sign))
        {
            //cross = detLeft − detRight expanded over original coordinates:
            //positive terms t1x·t2y, f1x·f2y, t1y·f2x, f1y·t2x; negative
            //terms t1x·f2y, f1x·t2y, t1y·t2x, f1y·f2x.
            return ExactFourProductSign(
                toFirst.X, toSecond.Y,
                fromFirst.X, fromSecond.Y,
                toFirst.Y, fromSecond.X,
                fromFirst.Y, toSecond.X,
                toFirst.X, fromSecond.Y,
                fromFirst.X, toSecond.Y,
                toFirst.Y, toSecond.X,
                fromFirst.Y, fromSecond.X);
        }

        return sign;
    }

    /// <summary>
    /// The exact sign of the dot product of two direction vectors,
    /// <c>(toFirst − fromFirst) · (toSecond − fromSecond)</c>: +1 when the
    /// directions agree within a quarter turn, −1 when they oppose, 0 exactly
    /// perpendicular. Combined with a zero cross sign this separates
    /// shared-direction from anti-parallel collinear rays.
    /// </summary>
    public static int DirectionDotSign(Point2d fromFirst, Point2d toFirst, Point2d fromSecond, Point2d toSecond)
    {
        double dotLeft = (toFirst.X - fromFirst.X) * (toSecond.X - fromSecond.X);
        double dotRight = -((toFirst.Y - fromFirst.Y) * (toSecond.Y - fromSecond.Y));

        //dot = dotLeft − dotRight with dotRight negated above, reusing the
        //two-product filter shape unchanged.
        if(!TryFilteredSign(dotLeft, dotRight, out int sign))
        {
            //dot expanded over original coordinates: positive terms t1x·t2x,
            //f1x·f2x, t1y·t2y, f1y·f2y; negative terms t1x·f2x, f1x·t2x,
            //t1y·f2y, f1y·t2y.
            return ExactFourProductSign(
                toFirst.X, toSecond.X,
                fromFirst.X, fromSecond.X,
                toFirst.Y, toSecond.Y,
                fromFirst.Y, fromSecond.Y,
                toFirst.X, fromSecond.X,
                fromFirst.X, toSecond.X,
                toFirst.Y, fromSecond.Y,
                fromFirst.Y, toSecond.Y);
        }

        return sign;
    }

    /// <summary>
    /// The eight-way angular class of a direction starting at the positive X
    /// axis and walking counter-clockwise; the signs come from ordinate
    /// comparisons, which are exact by definition. Even classes are the four
    /// axis directions, odd classes the open quadrants between them — the
    /// shared coarse key of every angular sort (node fans, edge stars), with
    /// <see cref="DirectionCrossSign"/> refining within a class.
    /// </summary>
    public static int DirectionClass(Point2d from, Point2d to)
    {
        bool positiveX = to.X > from.X;
        bool negativeX = to.X < from.X;
        bool positiveY = to.Y > from.Y;
        bool negativeY = to.Y < from.Y;

        if(positiveX && !positiveY && !negativeY)
        {
            return 0;
        }

        if(positiveX && positiveY)
        {
            return 1;
        }

        if(!positiveX && !negativeX && positiveY)
        {
            return 2;
        }

        if(negativeX && positiveY)
        {
            return 3;
        }

        if(negativeX && !positiveY && !negativeY)
        {
            return 4;
        }

        if(negativeX && negativeY)
        {
            return 5;
        }

        if(!positiveX && !negativeX && negativeY)
        {
            return 6;
        }

        return 7;
    }

    /// <summary>
    /// The shared static filter over <c>left − right</c> where each side is
    /// one product of two coordinate differences: certifies the plain-double
    /// sign whenever the sides disagree in sign, either side is zero, or the
    /// magnitude clears the error bound. Returns false when only the exact
    /// path can decide.
    /// </summary>
    private static bool TryFilteredSign(double left, double right, out int sign)
    {
        double det = left - right;
        double detSum;

        if(left > 0.0)
        {
            if(right <= 0.0)
            {
                sign = SignOf(det);

                return true;
            }

            detSum = left + right;
        }
        else if(left < 0.0)
        {
            if(right >= 0.0)
            {
                sign = SignOf(det);

                return true;
            }

            detSum = -left - right;
        }
        else
        {
            sign = SignOf(det);

            return true;
        }

        double errorBound = DeterminantBound * detSum;

        if(det >= errorBound || -det >= errorBound)
        {
            sign = SignOf(det);

            return true;
        }

        sign = 0;

        return false;
    }

    /// <summary>
    /// The exact orientation fallback: evaluates
    /// <c>ax·by − ax·cy − cx·by − ay·bx + ay·cx + cy·bx</c> (the full
    /// expansion of <c>(a−c) × (b−c)</c>; the <c>cx·cy</c> terms cancel) in
    /// expansion arithmetic and returns the exact sign.
    /// </summary>
    private static int Orient2DExact(Point2d a, Point2d b, Point2d c)
    {
        Span<double> positive = stackalloc double[ExactCapacity];
        Span<double> negative = stackalloc double[ExactCapacity];
        Span<double> scratch = stackalloc double[ExactCapacity];

        int positiveLength = AccumulateProduct(positive, 0, a.X, b.Y, scratch);
        positiveLength = AccumulateProduct(positive, positiveLength, a.Y, c.X, scratch);
        positiveLength = AccumulateProduct(positive, positiveLength, c.Y, b.X, scratch);

        int negativeLength = AccumulateProduct(negative, 0, a.X, c.Y, scratch);
        negativeLength = AccumulateProduct(negative, negativeLength, c.X, b.Y, scratch);
        negativeLength = AccumulateProduct(negative, negativeLength, a.Y, b.X, scratch);

        return DifferenceSign(positive[..positiveLength], negative[..negativeLength]);
    }

    /// <summary>
    /// The exact fallback shared by the direction predicates: the sign of the
    /// sum of four positive and four negative exact coordinate products,
    /// passed as factor pairs in that order.
    /// </summary>
    private static int ExactFourProductSign(
        double positiveFirstA, double positiveFirstB,
        double positiveSecondA, double positiveSecondB,
        double positiveThirdA, double positiveThirdB,
        double positiveFourthA, double positiveFourthB,
        double negativeFirstA, double negativeFirstB,
        double negativeSecondA, double negativeSecondB,
        double negativeThirdA, double negativeThirdB,
        double negativeFourthA, double negativeFourthB)
    {
        Span<double> positive = stackalloc double[ExactCapacity];
        Span<double> negative = stackalloc double[ExactCapacity];
        Span<double> scratch = stackalloc double[ExactCapacity];

        int positiveLength = AccumulateProduct(positive, 0, positiveFirstA, positiveFirstB, scratch);
        positiveLength = AccumulateProduct(positive, positiveLength, positiveSecondA, positiveSecondB, scratch);
        positiveLength = AccumulateProduct(positive, positiveLength, positiveThirdA, positiveThirdB, scratch);
        positiveLength = AccumulateProduct(positive, positiveLength, positiveFourthA, positiveFourthB, scratch);

        int negativeLength = AccumulateProduct(negative, 0, negativeFirstA, negativeFirstB, scratch);
        negativeLength = AccumulateProduct(negative, negativeLength, negativeSecondA, negativeSecondB, scratch);
        negativeLength = AccumulateProduct(negative, negativeLength, negativeThirdA, negativeThirdB, scratch);
        negativeLength = AccumulateProduct(negative, negativeLength, negativeFourthA, negativeFourthB, scratch);

        return DifferenceSign(positive[..positiveLength], negative[..negativeLength]);
    }

    /// <summary>
    /// Folds the exact two-component product of <paramref name="factorA"/>
    /// and <paramref name="factorB"/> into the running expansion held in
    /// <paramref name="accumulator"/>, returning the new component count. An
    /// empty accumulator takes the product directly, zero-eliminated.
    /// </summary>
    private static int AccumulateProduct(Span<double> accumulator, int accumulatedLength, double factorA, double factorB, Span<double> scratch)
    {
        Span<double> term = stackalloc double[2];
        (double high, double low) = ExpansionArithmetic.TwoProduct(factorA, factorB);
        int termLength = 0;

        if(low != 0.0)
        {
            term[termLength] = low;
            termLength++;
        }

        if(high != 0.0 || termLength == 0)
        {
            term[termLength] = high;
            termLength++;
        }

        if(accumulatedLength == 0)
        {
            term[..termLength].CopyTo(accumulator);

            return termLength;
        }

        int written = ExpansionArithmetic.Sum(accumulator[..accumulatedLength], term[..termLength], scratch);
        scratch[..written].CopyTo(accumulator);

        return written;
    }

    /// <summary>
    /// The exact sign of <c>positive − negative</c> over two accumulated
    /// expansions: negation only flips component signs, so the difference is
    /// one exact expansion sum away.
    /// </summary>
    private static int DifferenceSign(Span<double> positive, Span<double> negative)
    {
        for(int index = 0; index < negative.Length; index++)
        {
            negative[index] = -negative[index];
        }

        Span<double> difference = stackalloc double[2 * ExactCapacity];
        int differenceLength = ExpansionArithmetic.Sum(positive, negative, difference);

        return ExpansionArithmetic.Sign(difference[..differenceLength]);
    }

    /// <summary>The three-way sign of a plain double.</summary>
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
