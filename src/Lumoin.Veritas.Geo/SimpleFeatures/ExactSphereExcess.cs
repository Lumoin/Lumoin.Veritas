using System;
using Lumoin.Veritas.Geo.Spatial3D;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The exact side of a point against a sphere given entirely as doubles: the sign of
/// <c>(px−cx)² + (py−cy)² + (pz−cz)² − r²</c> — positive strictly outside, zero on,
/// negative strictly inside. The third squared-distance term beside the planar
/// predicate's two, and the same posture throughout: always exact, no filter, no
/// derived constant. Each coordinate difference enters exactly through a two-term
/// transform, each square is an exact expansion product, the three squares sum
/// exactly, and the radius square enters as an exact two-term product whose components
/// are sign-flipped before the final sum — the flip and sum ride the planar excess
/// predicate's own internal combiner, so the two predicates share one excess seam.
/// Exactness holds provided no two-term transform underflows or overflows anywhere in
/// the evaluation; that is a condition on the operands' bit quanta, not their
/// magnitudes — a full-mantissa square is exact only from about 5·10⁻¹⁴⁷ up. The
/// practical sufficient caller condition: every ordinate and the radius is zero or has
/// magnitude between roughly 10⁻¹³⁰ and 3.8·10¹⁵³, the high end one square-root of a
/// third below the planar predicate's because three squares sum instead of two.
/// Violations at the high end flood the expansion with non-finite components; the
/// evaluation guards its dominant components and throws rather than trusting a sign
/// built on them. Violations at the low end degrade silently and remain the caller's
/// contract.
/// </summary>
internal static class ExactSphereExcess
{
    /// <summary>Components of a packed two-term transform.</summary>
    private const int LeafComponents = 2;

    /// <summary>Components of a squared leaf: the expansion product of a 2-component expansion with itself.</summary>
    private const int SquareComponents = 8;

    /// <summary>Scratch the expansion product needs alongside its result at the leaf shape.</summary>
    private const int ProductScratchComponents = 12;

    /// <summary>Components of the first two squares' sum.</summary>
    private const int PlanarSumComponents = 16;

    /// <summary>Components of the exact squared distance: the three squares summed.</summary>
    internal const int SquaredDistanceComponents = 24;

    /// <summary>Components of the full excess: the squared distance plus the negated radius square.</summary>
    internal const int ExcessComponents = 26;

    /// <summary>
    /// The sign of the point's excess over the sphere: +1 strictly outside, 0 on, −1
    /// strictly inside. Exact within the documented quantum walls; throws on a
    /// high-end wall violation instead of returning a sign built on non-finite
    /// components.
    /// </summary>
    public static int Sign(Vector3d point, Vector3d center, double radius)
    {
        Span<double> squaredDistance = stackalloc double[SquaredDistanceComponents];
        Span<double> radiusNegation = stackalloc double[LeafComponents];
        Span<double> excess = stackalloc double[ExcessComponents];
        int count = SquaredDistance(point, center, squaredDistance);

        return ExactCircleExcess.ExcessSign(radius, squaredDistance[..count], radiusNegation, excess);
    }

    /// <summary>
    /// Writes the exact <c>(px−cx)² + (py−cy)² + (pz−cz)²</c> expansion into
    /// <paramref name="result"/> (at least <see cref="SquaredDistanceComponents"/>
    /// components) and returns its component count. The dominant component is verified
    /// finite before it is returned — a non-finite dominant means a square overflowed,
    /// and a sign built on it would silently misclassify, so the violation throws here,
    /// ahead of any decision.
    /// </summary>
    internal static int SquaredDistance(Vector3d point, Vector3d center, Span<double> result)
    {
        Span<double> leafX = stackalloc double[LeafComponents];
        Span<double> leafY = stackalloc double[LeafComponents];
        Span<double> leafZ = stackalloc double[LeafComponents];
        Span<double> productScratch = stackalloc double[ProductScratchComponents];
        Span<double> squareX = stackalloc double[SquareComponents];
        Span<double> squareY = stackalloc double[SquareComponents];
        Span<double> squareZ = stackalloc double[SquareComponents];
        Span<double> planarSum = stackalloc double[PlanarSumComponents];

        (double xHigh, double xLow) = ExpansionArithmetic.TwoDiff(point.X, center.X);
        int leafXCount = Pack(xHigh, xLow, leafX);
        (double yHigh, double yLow) = ExpansionArithmetic.TwoDiff(point.Y, center.Y);
        int leafYCount = Pack(yHigh, yLow, leafY);
        (double zHigh, double zLow) = ExpansionArithmetic.TwoDiff(point.Z, center.Z);
        int leafZCount = Pack(zHigh, zLow, leafZ);

        int squareXCount = ExpansionArithmetic.Product(leafX[..leafXCount], leafX[..leafXCount], productScratch, squareX);
        int squareYCount = ExpansionArithmetic.Product(leafY[..leafYCount], leafY[..leafYCount], productScratch, squareY);
        int squareZCount = ExpansionArithmetic.Product(leafZ[..leafZCount], leafZ[..leafZCount], productScratch, squareZ);
        int planarCount = ExpansionArithmetic.Sum(squareX[..squareXCount], squareY[..squareYCount], planarSum);
        int count = ExpansionArithmetic.Sum(planarSum[..planarCount], squareZ[..squareZCount], result);
        GuardFinite(result[count - 1]);

        return count;
    }

    /// <summary>
    /// Packs a two-term transform's halves into a zero-eliminated expansion, low component
    /// first so magnitudes increase; a zero value packs as the single zero component every
    /// expansion consumer accepts.
    /// </summary>
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
    private static void GuardFinite(double dominant)
    {
        if(!double.IsFinite(dominant))
        {
            throw new InvalidOperationException("A squared magnitude overflowed; the operand violates the finite-ordinate contract of the exact sphere predicates.");
        }
    }
}
