using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The exact side of a point against a circle given entirely as doubles: the sign of
/// <c>(px−cx)² + (py−cy)² − r²</c> — positive strictly outside, zero on, negative strictly
/// inside. Always exact, no filter, no derived constant: each coordinate difference enters
/// exactly through a two-term transform, each square is an exact expansion product, the
/// squares sum exactly, and the radius square enters as an exact two-term product whose
/// components are sign-flipped before the final sum — expansion addition is the only
/// combiner, so subtraction is negation composed with it. Exactness holds provided no
/// two-term transform underflows or overflows anywhere in the evaluation; that is a
/// condition on the operands' bit quanta, not their magnitudes — a full-mantissa square is
/// exact only from about 5·10⁻¹⁴⁷ up, and a difference of magnitude one can carry a
/// smallest-quantum low component whose square underflows. The practical sufficient caller
/// condition: every ordinate and the radius is zero or has magnitude between roughly
/// 10⁻¹³⁰ and 6.7·10¹⁵³. Violations at the high end flood the expansion with non-finite
/// components; the evaluation guards its dominant components and throws rather than
/// trusting a sign built on them. Violations at the low end degrade silently and remain
/// the caller's contract.
/// </summary>
internal static class ExactCircleExcess
{
    /// <summary>Components of a packed two-term transform.</summary>
    private const int LeafComponents = 2;

    /// <summary>Components of a squared leaf: the expansion product of a 2-component expansion with itself.</summary>
    private const int SquareComponents = 8;

    /// <summary>Scratch the expansion product needs alongside its result at the leaf shape.</summary>
    private const int ProductScratchComponents = 12;

    /// <summary>Components of the exact squared distance: the two squares summed.</summary>
    internal const int SquaredDistanceComponents = 16;

    /// <summary>Components of the full excess: the squared distance plus the negated radius square.</summary>
    internal const int ExcessComponents = 18;

    /// <summary>
    /// The sign of the point's excess over the circle: +1 strictly outside, 0 on, −1
    /// strictly inside. Exact within the documented quantum walls; throws on a
    /// high-end wall violation instead of returning a sign built on non-finite
    /// components.
    /// </summary>
    /// <param name="point">The point under test.</param>
    /// <param name="center">The circle's center.</param>
    /// <param name="radius">The circle's radius.</param>
    /// <returns>The exact excess sign.</returns>
    public static int Sign(Point2d point, Point2d center, double radius)
    {
        Span<double> squaredDistance = stackalloc double[SquaredDistanceComponents];
        Span<double> radiusNegation = stackalloc double[LeafComponents];
        Span<double> excess = stackalloc double[ExcessComponents];
        int count = SquaredDistance(point, center, squaredDistance);

        return ExcessSign(radius, squaredDistance[..count], radiusNegation, excess);
    }

    /// <summary>
    /// Writes the exact <c>(px−cx)² + (py−cy)²</c> expansion into
    /// <paramref name="result"/> (at least <see cref="SquaredDistanceComponents"/>
    /// components) and returns its component count. The dominant component is verified
    /// finite before it is returned — a non-finite dominant means a square overflowed,
    /// and a sign built on it would silently misclassify, so the violation throws here,
    /// ahead of any decision.
    /// </summary>
    /// <param name="point">The point under test.</param>
    /// <param name="center">The circle's center.</param>
    /// <param name="result">The span the expansion writes into.</param>
    /// <returns>The expansion's component count.</returns>
    internal static int SquaredDistance(Point2d point, Point2d center, Span<double> result)
    {
        Span<double> leafX = stackalloc double[LeafComponents];
        Span<double> leafY = stackalloc double[LeafComponents];
        Span<double> productScratch = stackalloc double[ProductScratchComponents];
        Span<double> squareX = stackalloc double[SquareComponents];
        Span<double> squareY = stackalloc double[SquareComponents];

        (double xHigh, double xLow) = ExpansionArithmetic.TwoDiff(point.X, center.X);
        int leafXCount = Pack(xHigh, xLow, leafX);
        (double yHigh, double yLow) = ExpansionArithmetic.TwoDiff(point.Y, center.Y);
        int leafYCount = Pack(yHigh, yLow, leafY);

        int squareXCount = ExpansionArithmetic.Product(leafX[..leafXCount], leafX[..leafXCount], productScratch, squareX);
        int squareYCount = ExpansionArithmetic.Product(leafY[..leafYCount], leafY[..leafYCount], productScratch, squareY);
        int count = ExpansionArithmetic.Sum(squareX[..squareXCount], squareY[..squareYCount], result);
        GuardFinite(result[count - 1]);

        return count;
    }

    /// <summary>
    /// The sign of <c>squaredDistance − radius²</c>: positive when the distance exceeds
    /// the radius, zero on the circle, negative inside. The radius square is verified
    /// finite before its negation enters the sum. <paramref name="radiusNegation"/> needs
    /// two components and <paramref name="excess"/> at least
    /// <see cref="ExcessComponents"/>.
    /// </summary>
    /// <param name="radius">The circle's radius.</param>
    /// <param name="squaredDistance">The exact squared-distance expansion.</param>
    /// <param name="radiusNegation">The two-component scratch the negated radius square packs into.</param>
    /// <param name="excess">The span the excess expansion writes into.</param>
    /// <returns>The exact excess sign.</returns>
    internal static int ExcessSign(double radius, ReadOnlySpan<double> squaredDistance, Span<double> radiusNegation, Span<double> excess)
    {
        (double radiusHigh, double radiusLow) = ExpansionArithmetic.TwoProduct(radius, radius);
        GuardFinite(radiusHigh);
        int radiusCount = Pack(-radiusHigh, -radiusLow, radiusNegation);
        int excessCount = ExpansionArithmetic.Sum(squaredDistance, radiusNegation[..radiusCount], excess);

        return ExpansionArithmetic.Sign(excess[..excessCount]);
    }

    /// <summary>
    /// The exact ordering of two squared-distance expansions: the sign of
    /// <c>left − right</c>. <paramref name="negation"/> needs as many components as
    /// <paramref name="right"/> and <paramref name="difference"/> the sum of both
    /// lengths.
    /// </summary>
    /// <param name="left">The left squared-distance expansion.</param>
    /// <param name="right">The right squared-distance expansion.</param>
    /// <param name="negation">The scratch the right expansion negates into.</param>
    /// <param name="difference">The span the difference expansion writes into.</param>
    /// <returns>The exact ordering sign.</returns>
    internal static int CompareSquaredDistances(ReadOnlySpan<double> left, ReadOnlySpan<double> right, Span<double> negation, Span<double> difference)
    {
        for(int index = 0; index < right.Length; index++)
        {
            negation[index] = -right[index];
        }

        int count = ExpansionArithmetic.Sum(left, negation[..right.Length], difference);

        return ExpansionArithmetic.Sign(difference[..count]);
    }

    /// <summary>
    /// Packs a two-term transform's halves into a zero-eliminated expansion, low component
    /// first so magnitudes increase; a zero value packs as the single zero component every
    /// expansion consumer accepts.
    /// </summary>
    /// <param name="high">The transform's high half.</param>
    /// <param name="low">The transform's low half.</param>
    /// <param name="result">The span the packed expansion writes into.</param>
    /// <returns>The packed component count.</returns>
    private static int Pack(double high, double low, Span<double> result)
    {
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

    /// <summary>Throws when a dominant component is not finite: the operand crossed the high wall and no sign built on the expansion can be trusted.</summary>
    /// <param name="dominant">The expansion's dominant component.</param>
    private static void GuardFinite(double dominant)
    {
        if(!double.IsFinite(dominant))
        {
            throw new InvalidOperationException("A squared magnitude overflowed; the operand violates the finite-ordinate contract of the exact circle predicates.");
        }
    }
}
