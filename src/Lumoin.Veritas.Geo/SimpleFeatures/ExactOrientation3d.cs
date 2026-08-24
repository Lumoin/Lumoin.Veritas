using System;
using Lumoin.Veritas.Geo.Spatial3D;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The three-dimensional exact-sign predicates of the planar-embedded certified tier:
/// the orientation of a point against the exact plane through three others, the
/// in-plane steering orientation seen along that plane's normal, and the planarity
/// band comparison certifying squared point-to-plane distance against a squared band —
/// no quotient, no root. The plane is always the exact plane through the original
/// points: every difference forms inside the tracked computation as an exact
/// two-component expansion, never as a pre-rounded double. The steering predicate
/// answers behind a static error-bound filter and escalates to a degree-four exact
/// tail on a caller-held <see cref="Orientation3dScratch"/>; the orientation and band
/// signs are always exact, the band comparison riding compressed expansions whose
/// packed component counts the magnitude walls below guarantee. The steering
/// contract: the cross-dot annihilates every argument's off-plane component exactly —
/// a scalar triple with two parallel factors is zero — so the returned sign is the
/// orientation of the projections onto the plane, total, invariant to off-plane
/// displacement, and sign-consistent while every call shares one plane triple.
/// </summary>
internal static class ExactOrientation3d
{
    /// <summary>The machine epsilon of binary64 round-to-nearest: <c>2^-53</c>.</summary>
    private const double Epsilon = 1.1102230246251565e-16;

    /// <summary>
    /// The static filter bound for the steering determinant: with epsilon the machine
    /// epsilon of round-to-nearest binary64, a computed cross-dot whose magnitude
    /// strictly exceeds this multiple of the permanent — the sum over the axes of the
    /// cross pair's and the normal pair's absolute-value sums multiplied — has the
    /// exact sign. The envelope: each rounded monomial carries at most three roundings
    /// (two differences, one product), so a cross or normal component differs from its
    /// exact value by under four epsilon of its pair sum; each component product
    /// accumulates under nine epsilon of the pair-sum product and the two final
    /// additions two more, for eleven epsilon of the true permanent to first order;
    /// the computed permanent underestimates the true one by a comparable factor, and
    /// twelve epsilon with a second-order guard strictly dominates the whole envelope.
    /// The certification comparison is strict, so a zero determinant can never
    /// certify and exactly aligned inputs always answer through the exact tail. The
    /// envelope, and with it the certification claim, is scoped to operands inside
    /// the magnitude walls below: beneath them subnormal roundings escape the
    /// relative-error model and the filter is no longer sound.
    /// </summary>
    private const double SteeringFilterBound = (12.0 + (256.0 * Epsilon)) * Epsilon;

    /// <summary>
    /// The lower magnitude wall for every ordinate the degree-six planarity
    /// comparison consumes; zero is exempt. The wall is a quantum condition, not a
    /// magnitude one: every step of the evaluation is an error-free two-term
    /// transform, exact only while its residual is representable, and the deepest
    /// residuals sit in the band-scaled term — the band square's low component
    /// carries a quantum near the band square times 2⁻¹⁰⁶, and the product scales
    /// norm-square components whose quanta reach the fourth power of an ordinate ulp,
    /// near (2⁻⁵³ times the ordinate)⁴. Their product must stay at or above the
    /// smallest subnormal, 2⁻¹⁰⁷⁴; with the band at its own wall the ordinate floor
    /// lands near 1.3·10⁻³⁷, and the squared-determinant path's sixth-power condition
    /// lands near 1.2·10⁻³⁸. The wall stands more than three orders inside the
    /// binding floor. Below the floor the arithmetic degrades silently — the excess
    /// predicates' recorded low-end behavior — so staying inside is the caller's
    /// contract.
    /// </summary>
    internal const double MinimumMagnitude = 1e-33;

    /// <summary>
    /// The upper magnitude wall for every ordinate and the band. A range condition:
    /// with every operand magnitude at most M, the orientation expansion is bounded
    /// by 48·M³, its square by 2304·M⁶, and the band-scaled norm term by 192·M⁶, so
    /// every dominant component stays finite while M stays under the sixth root of
    /// the largest double's headroom — a ceiling near 10⁵⁰·⁸. The wall stands more
    /// than five orders inside it. Violations at the high end are guarded: a
    /// non-finite dominant throws rather than signing.
    /// </summary>
    internal const double MaximumMagnitude = 1e45;

    /// <summary>
    /// The lower magnitude wall for the planarity band, standing apart from the
    /// ordinate wall because the band is a budget fraction of a radius and sits
    /// orders below the ordinates it certifies — a millionth of an in-wall radius
    /// clears this wall with margin. With ordinates at their own wall the band floor
    /// from the quantum condition lands near 1.6·10⁻⁴⁸; this wall stands more than
    /// seven orders inside it, and the joint condition — ordinates at or above their
    /// wall, the band at or above this one, everything at or under the upper wall —
    /// is the documented sufficient window for exactness of the planarity comparison.
    /// </summary>
    internal const double MinimumBandMagnitude = 1e-40;

    /// <summary>An exact coordinate difference: at most two components.</summary>
    private const int DifferenceCapacity = 2;

    /// <summary>One product of two differences: <c>ProductCapacity(2, 2)</c> = 8.</summary>
    private const int MinorProductCapacity = 8;

    /// <summary>
    /// The shared scratch of the difference-by-difference products:
    /// <c>ProductScratchCapacity(2, 2)</c> = <c>ScaleCapacity(2) +
    /// ProductCapacity(2, 2)</c> = 4 + 8.
    /// </summary>
    private const int SmallProductScratchCapacity = 12;

    /// <summary>A cross or normal component — two minor products differenced: <c>SumCapacity(8, 8)</c> = 16.</summary>
    private const int CrossComponentCapacity = 16;

    /// <summary>One orientation term — a difference leaf times a minor: <c>ProductCapacity(2, 16)</c> = 64.</summary>
    private const int DeterminantTermCapacity = 64;

    /// <summary>The orientation term product's scratch: <c>ScaleCapacity(2) + ProductCapacity(2, 16)</c> = 4 + 64.</summary>
    private const int DeterminantTermScratchCapacity = 68;

    /// <summary>The first two orientation terms' sum: <c>SumCapacity(64, 64)</c> = 128.</summary>
    private const int DeterminantPartialCapacity = 128;

    /// <summary>The full orientation expansion: <c>SumCapacity(128, 64)</c> = 192.</summary>
    internal const int DeterminantCapacity = 192;

    /// <summary>
    /// The compressed orientation expansion's capacity, derived from the walls and
    /// the compression pass's packed-count bound: within the walls the expansion's
    /// value is at most 48·M³, near 2⁴⁵⁵, with quanta at least the cube of an
    /// ordinate ulp at the lower wall, near 2⁻⁴⁸⁸ — a bit span near 943 — and a
    /// packed expansion covers at least fifty-two bits per component, so at most
    /// twenty-one components arrive. Capacity twenty-four leaves headroom; the
    /// compression guard throws if an out-of-wall operand ever exceeds it, before
    /// the squared product consumes it.
    /// </summary>
    internal const int CompressedDeterminantCapacity = 24;

    /// <summary>
    /// The compressed norm-square expansion's capacity by the same derivation: the
    /// norm square is at most 192·M⁴, near 2⁶⁰⁶, with quanta at least the fourth
    /// power of an ordinate ulp at the lower wall, near 2⁻⁶⁵¹ — a bit span near 1257
    /// and at most twenty-seven packed components. Capacity thirty-two leaves
    /// headroom; the same guard applies.
    /// </summary>
    internal const int CompressedNormCapacity = 32;

    /// <summary>The band-scaled norm term: <c>ProductCapacity(2, 32)</c> = 128.</summary>
    private const int BandProductCapacity = 128;

    /// <summary>The band-scaled product's scratch: <c>ScaleCapacity(2) + ProductCapacity(2, 32)</c> = 4 + 128.</summary>
    private const int BandProductScratchCapacity = 132;

    /// <summary>
    /// The orientation of <paramref name="point"/> against the directed plane through
    /// the triple: the exact sign of
    /// <c>((planeSecond−planeFirst)×(planeThird−planeFirst))·(point−planeFirst)</c> —
    /// +1 on the side the right-handed normal points to, −1 on the opposite side,
    /// 0 exactly coplanar, including every degenerate triple. Always exact, fully
    /// stack-allocated; throws on a high-wall violation instead of signing on a
    /// non-finite dominant.
    /// </summary>
    public static int Sign(Vector3d planeFirst, Vector3d planeSecond, Vector3d planeThird, Vector3d point)
    {
        Span<double> determinant = stackalloc double[DeterminantCapacity];
        int count = DeterminantExpansion(planeFirst, planeSecond, planeThird, point, determinant);

        return ExpansionArithmetic.Sign(determinant[..count]);
    }

    /// <summary>
    /// The in-plane orientation of the directions from <paramref name="pivot"/>
    /// toward <paramref name="toFirst"/> and <paramref name="toSecond"/>, seen along
    /// the plane triple's normal: the exact sign of
    /// <c>((toFirst−pivot)×(toSecond−pivot))·((planeSecond−planeFirst)×(planeThird−planeFirst))</c> —
    /// +1 when the second direction lies counter-clockwise of the first seen from the
    /// normal's side, −1 clockwise, 0 exactly parallel, anti-parallel, or degenerate.
    /// Adaptive: the static filter certifies the plain-double sign when its magnitude
    /// strictly clears the derived error bound; otherwise the degree-four exact tail
    /// on the caller's scratch carrier decides over the original coordinates.
    /// </summary>
    public static int InPlaneSign(Orientation3dScratch scratch, Vector3d planeFirst, Vector3d planeSecond, Vector3d planeThird, Vector3d pivot, Vector3d toFirst, Vector3d toSecond)
    {
        double ux = toFirst.X - pivot.X;
        double uy = toFirst.Y - pivot.Y;
        double uz = toFirst.Z - pivot.Z;
        double vx = toSecond.X - pivot.X;
        double vy = toSecond.Y - pivot.Y;
        double vz = toSecond.Z - pivot.Z;
        double sx = planeSecond.X - planeFirst.X;
        double sy = planeSecond.Y - planeFirst.Y;
        double sz = planeSecond.Z - planeFirst.Z;
        double tx = planeThird.X - planeFirst.X;
        double ty = planeThird.Y - planeFirst.Y;
        double tz = planeThird.Z - planeFirst.Z;

        double uyvz = uy * vz;
        double uzvy = uz * vy;
        double uzvx = uz * vx;
        double uxvz = ux * vz;
        double uxvy = ux * vy;
        double uyvx = uy * vx;

        double sytz = sy * tz;
        double szty = sz * ty;
        double sztx = sz * tx;
        double sxtz = sx * tz;
        double sxty = sx * ty;
        double sytx = sy * tx;

        double det = (((uyvz - uzvy) * (sytz - szty))
            + ((uzvx - uxvz) * (sztx - sxtz)))
            + ((uxvy - uyvx) * (sxty - sytx));

        //The permanent bounds the accumulated rounding of det: for each axis the
        //cross pair's absolute sum multiplied by the normal pair's absolute sum.
        double permanent = (((Math.Abs(uyvz) + Math.Abs(uzvy)) * (Math.Abs(sytz) + Math.Abs(szty)))
            + ((Math.Abs(uzvx) + Math.Abs(uxvz)) * (Math.Abs(sztx) + Math.Abs(sxtz))))
            + ((Math.Abs(uxvy) + Math.Abs(uyvx)) * (Math.Abs(sxty) + Math.Abs(sytx)));

        double errorBound = SteeringFilterBound * permanent;
        if(det > errorBound || -det > errorBound)
        {
            return SignOf(det);
        }

        return InPlaneSignExact(scratch, planeFirst, planeSecond, planeThird, pivot, toFirst, toSecond);
    }

    /// <summary>
    /// The planarity band comparison: the exact sign of
    /// <c>orientation² − band²·‖normal‖²</c> with the orientation and the normal
    /// taken over the plane triple and <paramref name="point"/> exactly as in
    /// <see cref="Sign"/> — positive when the point's distance to the exact plane
    /// strictly exceeds <paramref name="band"/>, zero exactly on the band, negative
    /// strictly inside. The band is a length, the budget fraction of a radius, and
    /// enters as one exact square; the norm square keeps the comparison
    /// quotient-free. An exactly collinear plane triple vanishes on both sides and
    /// answers zero, so the caller adjudicates degeneracy first. Always exact within
    /// the walls: the orientation expansion and the norm square are compressed to
    /// their wall-derived packed capacities before the one large product forms,
    /// which is what keeps the degree-six comparison inside the scratch carrier's
    /// fixed buffers; the compression guard and the finiteness guards throw on
    /// wall violations instead of signing.
    /// </summary>
    public static int PlaneBandComparisonSign(Orientation3dScratch scratch, Vector3d planeFirst, Vector3d planeSecond, Vector3d planeThird, Vector3d point, double band)
    {
        Span<double> sxLeaf = stackalloc double[DifferenceCapacity];
        Span<double> syLeaf = stackalloc double[DifferenceCapacity];
        Span<double> szLeaf = stackalloc double[DifferenceCapacity];
        Span<double> txLeaf = stackalloc double[DifferenceCapacity];
        Span<double> tyLeaf = stackalloc double[DifferenceCapacity];
        Span<double> tzLeaf = stackalloc double[DifferenceCapacity];

        int sxLength = Difference(planeSecond.X, planeFirst.X, sxLeaf);
        int syLength = Difference(planeSecond.Y, planeFirst.Y, syLeaf);
        int szLength = Difference(planeSecond.Z, planeFirst.Z, szLeaf);
        int txLength = Difference(planeThird.X, planeFirst.X, txLeaf);
        int tyLength = Difference(planeThird.Y, planeFirst.Y, tyLeaf);
        int tzLength = Difference(planeThird.Z, planeFirst.Z, tzLeaf);

        Span<double> normalX = stackalloc double[CrossComponentCapacity];
        Span<double> normalY = stackalloc double[CrossComponentCapacity];
        Span<double> normalZ = stackalloc double[CrossComponentCapacity];
        int normalXLength = MinorExpansion(syLeaf[..syLength], tzLeaf[..tzLength], szLeaf[..szLength], tyLeaf[..tyLength], normalX);
        int normalYLength = MinorExpansion(szLeaf[..szLength], txLeaf[..txLength], sxLeaf[..sxLength], tzLeaf[..tzLength], normalY);
        int normalZLength = MinorExpansion(sxLeaf[..sxLength], tyLeaf[..tyLength], syLeaf[..syLength], txLeaf[..txLength], normalZ);

        //The norm square rides the carrier's term buffers exactly as the steering
        //tail does, then compresses in place in the determinant buffer so the
        //buffer can serve as the squared product's scratch afterward.
        int squareXLength = ExpansionArithmetic.Product(normalX[..normalXLength], normalX[..normalXLength], scratch.TermScratch, scratch.TermA);
        int squareYLength = ExpansionArithmetic.Product(normalY[..normalYLength], normalY[..normalYLength], scratch.TermScratch, scratch.TermB);
        int squareZLength = ExpansionArithmetic.Product(normalZ[..normalZLength], normalZ[..normalZLength], scratch.TermScratch, scratch.TermC);
        int partialLength = ExpansionArithmetic.Sum(scratch.TermA.AsSpan(0, squareXLength), scratch.TermB.AsSpan(0, squareYLength), scratch.PartialSum);
        int normLength = ExpansionArithmetic.Sum(scratch.PartialSum.AsSpan(0, partialLength), scratch.TermC.AsSpan(0, squareZLength), scratch.Determinant);
        GuardFinite(scratch.Determinant[normLength - 1]);
        normLength = ExpansionCompression.Compress(scratch.Determinant.AsSpan(0, normLength), scratch.Determinant);
        GuardCompressed(normLength, CompressedNormCapacity);

        Span<double> normCompressed = stackalloc double[CompressedNormCapacity];
        scratch.Determinant.AsSpan(0, normLength).CopyTo(normCompressed);

        Span<double> orientation = stackalloc double[DeterminantCapacity];
        int orientationLength = DeterminantExpansion(planeFirst, planeSecond, planeThird, point, orientation);
        orientationLength = ExpansionCompression.Compress(orientation[..orientationLength], orientation);
        GuardCompressed(orientationLength, CompressedDeterminantCapacity);

        int squareLength = ExpansionArithmetic.Product(orientation[..orientationLength], orientation[..orientationLength], scratch.Determinant, scratch.DeterminantSquare);
        GuardFinite(scratch.DeterminantSquare[squareLength - 1]);

        (double bandHigh, double bandLow) = ExpansionArithmetic.TwoProduct(band, band);
        GuardFinite(bandHigh);

        Span<double> bandSquare = stackalloc double[DifferenceCapacity];
        int bandLength = Pack(bandHigh, bandLow, bandSquare);
        Span<double> bandScratch = stackalloc double[BandProductScratchCapacity];
        Span<double> bandTerm = stackalloc double[BandProductCapacity];
        int bandTermLength = ExpansionArithmetic.Product(bandSquare[..bandLength], normCompressed[..normLength], bandScratch, bandTerm);
        for(int index = 0; index < bandTermLength; index++)
        {
            bandTerm[index] = -bandTerm[index];
        }

        int differenceLength = ExpansionArithmetic.Sum(scratch.DeterminantSquare.AsSpan(0, squareLength), bandTerm[..bandTermLength], scratch.Determinant);

        return ExpansionArithmetic.Sign(scratch.Determinant.AsSpan(0, differenceLength));
    }

    /// <summary>
    /// The steering predicate's exact tail: the same cross-dot in expansion
    /// arithmetic over the original coordinates. The cross and normal components
    /// accumulate on the stack; the three component products and their two sums ride
    /// the caller's scratch carrier at the same shapes as the incircle tail.
    /// </summary>
    private static int InPlaneSignExact(Orientation3dScratch scratch, Vector3d planeFirst, Vector3d planeSecond, Vector3d planeThird, Vector3d pivot, Vector3d toFirst, Vector3d toSecond)
    {
        Span<double> uxLeaf = stackalloc double[DifferenceCapacity];
        Span<double> uyLeaf = stackalloc double[DifferenceCapacity];
        Span<double> uzLeaf = stackalloc double[DifferenceCapacity];
        Span<double> vxLeaf = stackalloc double[DifferenceCapacity];
        Span<double> vyLeaf = stackalloc double[DifferenceCapacity];
        Span<double> vzLeaf = stackalloc double[DifferenceCapacity];
        Span<double> sxLeaf = stackalloc double[DifferenceCapacity];
        Span<double> syLeaf = stackalloc double[DifferenceCapacity];
        Span<double> szLeaf = stackalloc double[DifferenceCapacity];
        Span<double> txLeaf = stackalloc double[DifferenceCapacity];
        Span<double> tyLeaf = stackalloc double[DifferenceCapacity];
        Span<double> tzLeaf = stackalloc double[DifferenceCapacity];

        int uxLength = Difference(toFirst.X, pivot.X, uxLeaf);
        int uyLength = Difference(toFirst.Y, pivot.Y, uyLeaf);
        int uzLength = Difference(toFirst.Z, pivot.Z, uzLeaf);
        int vxLength = Difference(toSecond.X, pivot.X, vxLeaf);
        int vyLength = Difference(toSecond.Y, pivot.Y, vyLeaf);
        int vzLength = Difference(toSecond.Z, pivot.Z, vzLeaf);
        int sxLength = Difference(planeSecond.X, planeFirst.X, sxLeaf);
        int syLength = Difference(planeSecond.Y, planeFirst.Y, syLeaf);
        int szLength = Difference(planeSecond.Z, planeFirst.Z, szLeaf);
        int txLength = Difference(planeThird.X, planeFirst.X, txLeaf);
        int tyLength = Difference(planeThird.Y, planeFirst.Y, tyLeaf);
        int tzLength = Difference(planeThird.Z, planeFirst.Z, tzLeaf);

        Span<double> crossX = stackalloc double[CrossComponentCapacity];
        Span<double> crossY = stackalloc double[CrossComponentCapacity];
        Span<double> crossZ = stackalloc double[CrossComponentCapacity];
        Span<double> normalX = stackalloc double[CrossComponentCapacity];
        Span<double> normalY = stackalloc double[CrossComponentCapacity];
        Span<double> normalZ = stackalloc double[CrossComponentCapacity];

        int crossXLength = MinorExpansion(uyLeaf[..uyLength], vzLeaf[..vzLength], uzLeaf[..uzLength], vyLeaf[..vyLength], crossX);
        int crossYLength = MinorExpansion(uzLeaf[..uzLength], vxLeaf[..vxLength], uxLeaf[..uxLength], vzLeaf[..vzLength], crossY);
        int crossZLength = MinorExpansion(uxLeaf[..uxLength], vyLeaf[..vyLength], uyLeaf[..uyLength], vxLeaf[..vxLength], crossZ);
        int normalXLength = MinorExpansion(syLeaf[..syLength], tzLeaf[..tzLength], szLeaf[..szLength], tyLeaf[..tyLength], normalX);
        int normalYLength = MinorExpansion(szLeaf[..szLength], txLeaf[..txLength], sxLeaf[..sxLength], tzLeaf[..tzLength], normalY);
        int normalZLength = MinorExpansion(sxLeaf[..sxLength], tyLeaf[..tyLength], syLeaf[..syLength], txLeaf[..txLength], normalZ);

        int termALength = ExpansionArithmetic.Product(crossX[..crossXLength], normalX[..normalXLength], scratch.TermScratch, scratch.TermA);
        int termBLength = ExpansionArithmetic.Product(crossY[..crossYLength], normalY[..normalYLength], scratch.TermScratch, scratch.TermB);
        int termCLength = ExpansionArithmetic.Product(crossZ[..crossZLength], normalZ[..normalZLength], scratch.TermScratch, scratch.TermC);

        int partialLength = ExpansionArithmetic.Sum(
            scratch.TermA.AsSpan(0, termALength), scratch.TermB.AsSpan(0, termBLength), scratch.PartialSum);
        int determinantLength = ExpansionArithmetic.Sum(
            scratch.PartialSum.AsSpan(0, partialLength), scratch.TermC.AsSpan(0, termCLength), scratch.Determinant);
        GuardFinite(scratch.Determinant[determinantLength - 1]);

        return ExpansionArithmetic.Sign(scratch.Determinant.AsSpan(0, determinantLength));
    }

    /// <summary>
    /// Writes the exact orientation expansion —
    /// <c>((planeSecond−planeFirst)×(planeThird−planeFirst))·(point−planeFirst)</c>
    /// as the scalar triple's three leaf-times-minor terms summed — into
    /// <paramref name="result"/> (at least <see cref="DeterminantCapacity"/>
    /// components) and returns its component count. The dominant component is
    /// verified finite before it is returned, so a high-wall violation throws here,
    /// ahead of any decision.
    /// </summary>
    private static int DeterminantExpansion(Vector3d planeFirst, Vector3d planeSecond, Vector3d planeThird, Vector3d point, Span<double> result)
    {
        Span<double> sxLeaf = stackalloc double[DifferenceCapacity];
        Span<double> syLeaf = stackalloc double[DifferenceCapacity];
        Span<double> szLeaf = stackalloc double[DifferenceCapacity];
        Span<double> txLeaf = stackalloc double[DifferenceCapacity];
        Span<double> tyLeaf = stackalloc double[DifferenceCapacity];
        Span<double> tzLeaf = stackalloc double[DifferenceCapacity];
        Span<double> wxLeaf = stackalloc double[DifferenceCapacity];
        Span<double> wyLeaf = stackalloc double[DifferenceCapacity];
        Span<double> wzLeaf = stackalloc double[DifferenceCapacity];

        int sxLength = Difference(planeSecond.X, planeFirst.X, sxLeaf);
        int syLength = Difference(planeSecond.Y, planeFirst.Y, syLeaf);
        int szLength = Difference(planeSecond.Z, planeFirst.Z, szLeaf);
        int txLength = Difference(planeThird.X, planeFirst.X, txLeaf);
        int tyLength = Difference(planeThird.Y, planeFirst.Y, tyLeaf);
        int tzLength = Difference(planeThird.Z, planeFirst.Z, tzLeaf);
        int wxLength = Difference(point.X, planeFirst.X, wxLeaf);
        int wyLength = Difference(point.Y, planeFirst.Y, wyLeaf);
        int wzLength = Difference(point.Z, planeFirst.Z, wzLeaf);

        //The scalar triple as s·(t×w): each minor is one component of t×w and
        //multiplies the matching component of s — equal to the cross-dot form by
        //the cyclic identity, term for term in exact arithmetic.
        Span<double> minor = stackalloc double[CrossComponentCapacity];
        Span<double> termScratch = stackalloc double[DeterminantTermScratchCapacity];
        Span<double> termX = stackalloc double[DeterminantTermCapacity];
        Span<double> termY = stackalloc double[DeterminantTermCapacity];
        Span<double> termZ = stackalloc double[DeterminantTermCapacity];

        int minorLength = MinorExpansion(tyLeaf[..tyLength], wzLeaf[..wzLength], tzLeaf[..tzLength], wyLeaf[..wyLength], minor);
        int termXLength = ExpansionArithmetic.Product(sxLeaf[..sxLength], minor[..minorLength], termScratch, termX);
        minorLength = MinorExpansion(tzLeaf[..tzLength], wxLeaf[..wxLength], txLeaf[..txLength], wzLeaf[..wzLength], minor);
        int termYLength = ExpansionArithmetic.Product(syLeaf[..syLength], minor[..minorLength], termScratch, termY);
        minorLength = MinorExpansion(txLeaf[..txLength], wyLeaf[..wyLength], tyLeaf[..tyLength], wxLeaf[..wxLength], minor);
        int termZLength = ExpansionArithmetic.Product(szLeaf[..szLength], minor[..minorLength], termScratch, termZ);

        Span<double> partial = stackalloc double[DeterminantPartialCapacity];
        int partialLength = ExpansionArithmetic.Sum(termX[..termXLength], termY[..termYLength], partial);
        int count = ExpansionArithmetic.Sum(partial[..partialLength], termZ[..termZLength], result);
        GuardFinite(result[count - 1]);

        return count;
    }

    /// <summary>
    /// Writes <c>firstA·firstB − secondA·secondB</c> over difference leaves as an
    /// exact expansion into <paramref name="result"/> (at least
    /// <see cref="CrossComponentCapacity"/> components) and returns its component
    /// count — the shared shape of every cross and normal component.
    /// </summary>
    private static int MinorExpansion(
        ReadOnlySpan<double> firstA,
        ReadOnlySpan<double> firstB,
        ReadOnlySpan<double> secondA,
        ReadOnlySpan<double> secondB,
        Span<double> result)
    {
        Span<double> productScratch = stackalloc double[SmallProductScratchCapacity];
        Span<double> firstProduct = stackalloc double[MinorProductCapacity];
        Span<double> secondProduct = stackalloc double[MinorProductCapacity];
        Span<double> negationScratch = stackalloc double[MinorProductCapacity];
        int firstLength = ExpansionArithmetic.Product(firstA, firstB, productScratch, firstProduct);
        int secondLength = ExpansionArithmetic.Product(secondA, secondB, productScratch, secondProduct);

        return SubtractInto(firstProduct[..firstLength], secondProduct[..secondLength], negationScratch, result);
    }

    /// <summary>
    /// Writes <c>a − b</c> as an exact, zero-eliminated expansion into
    /// <paramref name="result"/> (capacity two) and returns its component count: the
    /// roundoff first, the rounded difference last.
    /// </summary>
    private static int Difference(double a, double b, Span<double> result)
    {
        (double high, double low) = ExpansionArithmetic.TwoDiff(a, b);

        return Pack(high, low, result);
    }

    /// <summary>
    /// Writes <c>e − f</c> into <paramref name="result"/> by negating
    /// <paramref name="f"/> into <paramref name="negationScratch"/> — exact, since
    /// negation only flips component signs — and summing. Returns the component
    /// count of the result.
    /// </summary>
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

    /// <summary>
    /// Packs a two-term transform's halves into a zero-eliminated expansion, low
    /// component first so magnitudes increase; a zero value packs as the single zero
    /// component every expansion consumer accepts.
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
            throw new InvalidOperationException("A dominant component overflowed; the operand violates the magnitude-wall contract of the exact orientation predicates.");
        }
    }

    /// <summary>Throws when a compressed expansion exceeds its wall-derived capacity: the operands sit outside the documented magnitude walls and the fixed buffers ahead cannot hold the result.</summary>
    private static void GuardCompressed(int count, int capacity)
    {
        if(count > capacity)
        {
            throw new InvalidOperationException("A compressed expansion exceeded its wall-derived capacity; the operands violate the magnitude-wall contract of the exact orientation predicates.");
        }
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
