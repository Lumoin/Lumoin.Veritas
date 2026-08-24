using System;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.Spatial3D;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Certified linearization of circular arcs embedded in an arbitrary plane of
/// three-dimensional space: the approximation is constructed by arc bisection in plain
/// double arithmetic and verified per emission by exact predicates, with refusal —
/// never a shipped approximation — wherever the arithmetic cannot certify. The
/// certified object is the intersection of the sphere about the computed center at the
/// computed radius with the exact plane through the three document control points, and
/// the solve is never trusted in any of its four computed quantities: every emitted
/// vertex passes the exact radial band — the three-term squared-distance excess
/// against the one-bit-widened annulus radii — and the exact planarity band — squared
/// plane distance against the squared one-bit-widened planar band, no quotient, no
/// root — while the computed center itself passes the planarity band once per arc
/// before any seed is checked. That center gate closes the one direction the seed
/// checks provably cannot pin: the three seeds are equidistant from every point of the
/// exact circle's axis, so the seed bands leave the center a one-parameter blind
/// family along that axis, and only the gate fixes the center against the plane.
/// The quotable bound: every emitted vertex lies within 1.35e-6 times the computed
/// radius of the certified circle. The closed form — the radius times two to the
/// negative twentieth times the square root of two — is not strictly achieved and
/// enters only this derivation: a vertex at radial excess within the admitted annulus
/// and plane distance within the admitted band sits within the root of the sum of
/// their squares of the certified circle, corrected by the projection's denominator
/// dip — the in-plane radial shortfall a plane distance h adds, at most h squared over
/// twice the radius, about two to the negative forty-first of the radius — by the
/// one-bit widening of the admitted annulus against the published constants, by the
/// certified center height from the gate, whose contribution is second order at the
/// same scale, and by the chord-stop slab — the sagitta comparison measures distance
/// to the center, so its in-plane guarantee weakens by the same second-order plane
/// correction. Every correction is second order against the padded decimal's roughly
/// nine-hundredths-of-a-percent slack, which is the honest published claim. The
/// construction uses addition, subtraction, multiplication, division, and square root
/// only — every operation correctly rounded under IEEE 754 — so the output is
/// bit-identical across conforming machines; no transcendental function participates,
/// and the construction never calls a throwing member: degenerate normalization yields
/// non-finite values the acceptance-form walls refuse. All steering decisions — the
/// minor-against-major side, the diametral key, split membership — are exact in-plane
/// orientation signs seen along the control-point plane's normal; the steering
/// predicate annihilates every argument's off-plane component exactly, so steering is
/// total and invariant to off-plane displacement while every call threads the same
/// plane triple, and this kernel threads the three document control points into every
/// steering call. The travel sign is identically plus one by construction: the arc
/// runs from the first control point through the second to the third as seen from
/// the side the construction-order normal points to, so the content distinguishing
/// clockwise from counter-clockwise lives in that normal's control-point order, and
/// collinearity is refused solely by the exact test that the edge cross product is
/// zero in all three components — the squared norm of an exact expansion vanishes
/// exactly when every component does — never by a vacuous steering sign. The
/// magnitude walls are the exact orientation predicates' documented members, adopted
/// unchanged; a certified vertex stays within one radius of the in-wall center, which
/// the predicates' documented headroom covers with orders to spare. The kernel emits
/// vertices in arc order through an explicit bounded stack; nothing recurses, and the
/// only heap state is the caller's scratch carrier.
/// </summary>
internal static class CircularArcLinearization3d
{
    /// <summary>
    /// The largest fraction of the certified radius by which an emitted chord may sag
    /// inward from the circle: two to the negative sixteenth power. Every chord's
    /// midpoint passes an exact check against the comparison radius derived from this
    /// bound before the chord is emitted.
    /// </summary>
    internal const double MaximumRelativeSagitta = 1.52587890625e-05;

    /// <summary>
    /// The largest fraction of the certified radius by which any emitted vertex may
    /// sit off the certified sphere, on either side: two to the negative twentieth
    /// power. Every vertex — the document's own control points included — passes two
    /// exact checks against the annulus derived from this band before it is emitted.
    /// </summary>
    internal const double MaximumRelativeVertexDrift = 9.5367431640625e-07;

    /// <summary>
    /// The largest fraction of the certified radius by which any emitted vertex or
    /// the computed center may sit off the exact plane through the document control
    /// points: two to the negative twentieth power. Constructed vertices pass the
    /// exact planarity band per emission; the computed center passes it once per arc
    /// through the gate; document seeds define the plane and are exactly planar, so
    /// their planarity is a theorem the family's tests assert rather than a check
    /// this kernel repeats. The band is the radius times a power of two, so the
    /// product is exact; its one-bit widening is uniformity and disclosure beside the
    /// radial band's genuinely rounding products.
    /// </summary>
    internal const double MaximumRelativePlanarDrift = 9.5367431640625e-07;

    /// <summary>
    /// The bisection depth cap per gap: sixteen. A certifiable gap clears by about
    /// depth ten — the sagitta shrinks roughly fourfold per level — so the cap is
    /// generous headroom, and it doubles as the hard resource bound: a gap can emit at
    /// most two to the sixteenth plus one vertices, and an arc has at most eight gaps.
    /// </summary>
    internal const int MaximumBisectionDepth = 16;

    /// <summary>
    /// The explicit gap stack's capacity: the depth cap plus headroom for the seed
    /// gaps and their half-turn pre-splits. One descent chain holds at most one entry
    /// per depth level plus the seed entry, so the cap can never be exceeded — the
    /// depth check refuses first.
    /// </summary>
    private const int GapStackCapacity = 24;

    /// <summary>
    /// The travel sign, identically plus one by construction: the arc's direction is
    /// first-through-second-to-third as seen from the side the construction-order
    /// normal points to, so every in-plane side test against that normal answers plus
    /// one exactly when the queried turn agrees with the travel. The planar kernel
    /// computed this sign from an orientation predicate; here that predicate's content
    /// moved into the normal's construction order, and a zero can never stand in for
    /// it because exact collinearity is refused by its own component test first.
    /// </summary>
    private const int Travel = 1;

    /// <summary>
    /// The membership sign a split vertex must answer on its own gap: the negation of
    /// the travel sign, so minus one. A split vertex lies on the gap's own sub-arc
    /// exactly when it sits on the opposite side of the chord from the sub-arc's
    /// complement, which is the clockwise side under plus-one travel.
    /// </summary>
    private const int MembershipSign = -1;

    /// <summary>
    /// Linearizes a three-point arc — start, middle, end, the middle lying on the arc
    /// between the endpoints, each control point a coordinate triple in the arc's own
    /// plane — appending the certified vertex run to <paramref name="builder"/>
    /// half-open: the start seed is never emitted (the caller owns the run's opening
    /// vertex), the intermediates, the middle seed, and the end seed are. The three
    /// control points enter the output verbatim, bit-preserved in all three ordinates.
    /// The scratch carrier is the caller's single-owner state, created once and
    /// reused. False reports the outcome and the offending control-point index, and
    /// the builder holds exactly the vertices appended before the offense.
    /// </summary>
    public static bool TryLinearizeArc(Orientation3dScratch scratch, Vector3d start, Vector3d middle, Vector3d end, FlatGeometryBuilder builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex)
    {
        if(!TryCertifyCircle(scratch, start, middle, end, out CircleFrame frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        if(!TryCheckSeedAnnulus(start, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(middle, 1, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(end, 2, in frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        Vector3d current = start;

        if(!TryEmitGap(scratch, ref current, middle, in frame, builder, out outcome)
            || !TryEmitGap(scratch, ref current, end, in frame, builder, out outcome))
        {
            offendingSeedIndex = -1;

            return false;
        }

        outcome = CircularArcLinearization3dOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// Linearizes a full circle through three control points in the circle's own
    /// plane, appending the certified vertex run half-open: the first seed is never
    /// emitted (the caller owns the opening vertex), and the run walks three gaps
    /// through the second and third seeds back to the first control point, which
    /// closes the ring verbatim — the closing vertex is the opening vertex bit for
    /// bit in all three ordinates. There is no center-and-radius entry: a center and
    /// a radius alone carry no plane in three-dimensional space, so only the
    /// three-point forms reach this kernel.
    /// </summary>
    public static bool TryLinearizeCircle(Orientation3dScratch scratch, Vector3d first, Vector3d second, Vector3d third, FlatGeometryBuilder builder, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex)
    {
        if(!TryCertifyCircle(scratch, first, second, third, out CircleFrame frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        if(!TryCheckSeedAnnulus(first, 0, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(second, 1, in frame, out outcome, out offendingSeedIndex)
            || !TryCheckSeedAnnulus(third, 2, in frame, out outcome, out offendingSeedIndex))
        {
            return false;
        }

        Vector3d current = first;

        if(!TryEmitGap(scratch, ref current, second, in frame, builder, out outcome)
            || !TryEmitGap(scratch, ref current, third, in frame, builder, out outcome)
            || !TryEmitGap(scratch, ref current, first, in frame, builder, out outcome))
        {
            offendingSeedIndex = -1;

            return false;
        }

        outcome = CircularArcLinearization3dOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// The per-arc frame: the plane triple every steering call threads, the certified
    /// circle, the plain-double normal and in-plane basis the constructions ride, and
    /// the comparison values, each adjusted one bit in its conservative direction
    /// exactly once so every later check is a plain exact-predicate call.
    /// </summary>
    internal readonly record struct CircleFrame(
        Vector3d First,
        Vector3d Second,
        Vector3d Third,
        Vector3d Center,
        double Radius,
        Vector3d Normal,
        Vector3d BasisU,
        Vector3d BasisV,
        double ComparisonRadius,
        double AnnulusInner,
        double AnnulusOuter,
        double PlanarBand)
    {
        /// <summary>
        /// Builds the frame: the comparison radius rounds one bit upward so the
        /// product's rounding can only strengthen the sagitta check, the inner
        /// annulus radius one bit downward and the outer one bit upward so the
        /// admitted band is one bit wider than the published constants — the stated
        /// claim stays true whichever way the products rounded — and the planar band
        /// one bit upward for uniformity, its underlying power-of-two product being
        /// exact.
        /// </summary>
        public static CircleFrame Create(Vector3d first, Vector3d second, Vector3d third, Vector3d center, double radius, Vector3d normal, Vector3d basisU, Vector3d basisV)
        {
            double comparisonRadius = Math.BitIncrement(radius * (1.0 - MaximumRelativeSagitta));
            double annulusInner = Math.BitDecrement(radius * (1.0 - MaximumRelativeVertexDrift));
            double annulusOuter = Math.BitIncrement(radius * (1.0 + MaximumRelativeVertexDrift));
            double planarBand = Math.BitIncrement(radius * MaximumRelativePlanarDrift);

            return new CircleFrame(first, second, third, center, radius, normal, basisU, basisV, comparisonRadius, annulusInner, annulusOuter, planarBand);
        }
    }

    /// <summary>
    /// Degeneracy checks, the plane-basis construction, the anchored circumcenter
    /// solve in the plane's own coordinates, the wall checks on both the inputs and
    /// the computed circle, and the once-per-arc center-planarity gate. The exact
    /// collinearity pre-check blocks exact degeneracy only; exactly non-degenerate
    /// control points whose differences or projections round to a degenerate
    /// configuration poison the plain-double construction into garbage, infinity, or
    /// values that are not numbers, which is why the walls run on the computed values,
    /// why the caller's seed annulus checks never trust the solve, and why the gate
    /// never trusts the center's plane position — the seeds provably cannot see it.
    /// </summary>
    private static bool TryCertifyCircle(Orientation3dScratch scratch, Vector3d first, Vector3d second, Vector3d third, out CircleFrame frame, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex)
    {
        frame = default;

        if(!OrdinateInWall(first.X) || !OrdinateInWall(first.Y) || !OrdinateInWall(first.Z))
        {
            outcome = CircularArcLinearization3dOutcome.MagnitudeWall;
            offendingSeedIndex = 0;

            return false;
        }

        if(!OrdinateInWall(second.X) || !OrdinateInWall(second.Y) || !OrdinateInWall(second.Z))
        {
            outcome = CircularArcLinearization3dOutcome.MagnitudeWall;
            offendingSeedIndex = 1;

            return false;
        }

        if(!OrdinateInWall(third.X) || !OrdinateInWall(third.Y) || !OrdinateInWall(third.Z))
        {
            outcome = CircularArcLinearization3dOutcome.MagnitudeWall;
            offendingSeedIndex = 2;

            return false;
        }

        if(first == second)
        {
            outcome = CircularArcLinearization3dOutcome.CoincidentControlPoints;
            offendingSeedIndex = 1;

            return false;
        }

        if(second == third || first == third)
        {
            outcome = CircularArcLinearization3dOutcome.CoincidentControlPoints;
            offendingSeedIndex = 2;

            return false;
        }

        //The exact collinearity test: the steering predicate over the plane triple
        //against itself is the sign of the squared norm of the exact edge cross
        //product, which vanishes exactly when all three cross components do.
        if(ExactOrientation3d.InPlaneSign(scratch, first, second, third, first, second, third) == 0)
        {
            outcome = CircularArcLinearization3dOutcome.CollinearControlPoints;
            offendingSeedIndex = 2;

            return false;
        }

        //The plain-double plane frame: the construction-order normal, then an
        //orthonormal in-plane basis seeded by crossing the normal with the ordinate
        //axis of its smallest-magnitude component — the axis least aligned with the
        //normal, so the seed direction keeps the normal's two largest components
        //alive and degenerates only when the normal itself already has. The
        //normalizations divide by plain square roots and never throw: a collapsed
        //normal yields non-finite basis vectors whose garbage the computed walls
        //refuse below.
        Vector3d edgeSecond = Vector3d.Subtract(second, first);
        Vector3d edgeThird = Vector3d.Subtract(third, first);
        Vector3d normal = Vector3d.Cross(edgeSecond, edgeThird);
        Vector3d seedDirection = Vector3d.Cross(normal, SmallestComponentAxis(normal));
        double seedLength = seedDirection.Length();
        Vector3d basisU = new(seedDirection.X / seedLength, seedDirection.Y / seedLength, seedDirection.Z / seedLength);
        Vector3d crossDirection = Vector3d.Cross(normal, basisU);
        double crossLength = crossDirection.Length();
        Vector3d basisV = new(crossDirection.X / crossLength, crossDirection.Y / crossLength, crossDirection.Z / crossLength);

        //The anchored perpendicular-bisector solve in the plane's coordinates:
        //every input a difference from the second control point projected onto the
        //basis, so the system conditions at any offset; the center places back
        //through the basis, and the radius is the single square root of the
        //three-dimensional distance from the placed center to the anchor.
        Vector3d towardFirst = Vector3d.Subtract(first, second);
        Vector3d towardThird = Vector3d.Subtract(third, second);
        double towardFirstU = Vector3d.Dot(towardFirst, basisU);
        double towardFirstV = Vector3d.Dot(towardFirst, basisV);
        double towardThirdU = Vector3d.Dot(towardThird, basisU);
        double towardThirdV = Vector3d.Dot(towardThird, basisV);
        double towardFirstSquared = (towardFirstU * towardFirstU) + (towardFirstV * towardFirstV);
        double towardThirdSquared = (towardThirdU * towardThirdU) + (towardThirdV * towardThirdV);
        double cross = (towardFirstU * towardThirdV) - (towardFirstV * towardThirdU);
        double offsetU = ((towardThirdV * towardFirstSquared) - (towardFirstV * towardThirdSquared)) / (2.0 * cross);
        double offsetV = ((towardFirstU * towardThirdSquared) - (towardThirdU * towardFirstSquared)) / (2.0 * cross);
        Vector3d center = new(
            second.X + ((offsetU * basisU.X) + (offsetV * basisV.X)),
            second.Y + ((offsetU * basisU.Y) + (offsetV * basisV.Y)),
            second.Z + ((offsetU * basisU.Z) + (offsetV * basisV.Z)));
        double towardAnchorX = second.X - center.X;
        double towardAnchorY = second.Y - center.Y;
        double towardAnchorZ = second.Z - center.Z;
        double radius = Math.Sqrt(((towardAnchorX * towardAnchorX) + (towardAnchorY * towardAnchorY)) + (towardAnchorZ * towardAnchorZ));

        if(!OrdinateInWall(center.X) || !OrdinateInWall(center.Y) || !OrdinateInWall(center.Z) || !RadiusInWall(radius))
        {
            outcome = CircularArcLinearization3dOutcome.MagnitudeWall;
            offendingSeedIndex = -1;

            return false;
        }

        frame = CircleFrame.Create(first, second, third, center, radius, normal, basisU, basisV);

        //The once-per-arc center-planarity gate: the seed bands are blind along the
        //exact circle's axis — the seeds are equidistant from every point of it —
        //so the computed center's plane position is pinned here, exactly, or the
        //arc refuses with the computed-value convention.
        if(ExactOrientation3d.PlaneBandComparisonSign(scratch, first, second, third, center, frame.PlanarBand) > 0)
        {
            outcome = CircularArcLinearization3dOutcome.PlanarDrift;
            offendingSeedIndex = -1;

            return false;
        }

        outcome = CircularArcLinearization3dOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// The exact annulus check on a document seed, reporting the seed's index on
    /// failure — the check that exposes a mis-solved circle through the document's
    /// own points, so the plain-double solve is never trusted, only checked. The
    /// seed's planarity is not checked here because it cannot fail: the seed is one
    /// of the three points defining the exact plane, and a determinant with a
    /// repeated row is zero.
    /// </summary>
    private static bool TryCheckSeedAnnulus(Vector3d seed, int seedIndex, in CircleFrame frame, out CircularArcLinearization3dOutcome outcome, out int offendingSeedIndex)
    {
        if(!AnnulusHolds(seed, in frame))
        {
            outcome = CircularArcLinearization3dOutcome.VertexDrift;
            offendingSeedIndex = seedIndex;

            return false;
        }

        outcome = CircularArcLinearization3dOutcome.Certified;
        offendingSeedIndex = -1;

        return true;
    }

    /// <summary>
    /// Emits one gap half-open — the intermediates in arc order, then the far seed —
    /// advancing <paramref name="current"/> to the far seed. The explicit
    /// last-in-first-out stack holds pending far endpoints; the near half of every
    /// split is processed before the far half is popped, which is the visitation
    /// order argument: vertices emit strictly in arc order. Every gap decision is an
    /// exact sign — the minor-side test seen along the plane normal, the sagitta
    /// check, the split membership — and every constructed vertex passes both exact
    /// bands before it is stacked; a gap only terminates by clearing its exact check
    /// or refusing.
    /// </summary>
    private static bool TryEmitGap(Orientation3dScratch scratch, ref Vector3d current, Vector3d far, in CircleFrame frame, FlatGeometryBuilder builder, out CircularArcLinearization3dOutcome outcome)
    {
        Span<Vector3d> pendingTargets = stackalloc Vector3d[GapStackCapacity];
        Span<int> pendingDepths = stackalloc int[GapStackCapacity];
        pendingTargets[0] = far;
        pendingDepths[0] = 0;

        int pendingCount = 1;

        while(pendingCount > 0)
        {
            Vector3d target = pendingTargets[pendingCount - 1];
            int depth = pendingDepths[pendingCount - 1];
            int side = ExactOrientation3d.InPlaneSign(scratch, frame.First, frame.Second, frame.Third, current, target, frame.Center);

            if(side == Travel && ChordClears(current, target, in frame))
            {
                pendingCount--;
                builder.AddVertex(new Point2d(target.X, target.Y), target.Z, double.NaN);
                current = target;

                continue;
            }

            if(depth >= MaximumBisectionDepth)
            {
                outcome = CircularArcLinearization3dOutcome.DepthCeiling;

                return false;
            }

            if(!TryConstructSplit(scratch, current, target, side, in frame, out Vector3d split))
            {
                outcome = CircularArcLinearization3dOutcome.SplitMembership;

                return false;
            }

            //A constructed ordinate can cancel to a nonzero value far beneath the
            //inputs' scale — a tiny circle grazing an ordinate plane — and the
            //degree-six planarity comparison is exact only above the lower wall, so
            //every split re-takes the acceptance-form wall test before either band
            //is consulted.
            if(!OrdinateInWall(split.X) || !OrdinateInWall(split.Y) || !OrdinateInWall(split.Z))
            {
                outcome = CircularArcLinearization3dOutcome.MagnitudeWall;

                return false;
            }

            if(!AnnulusHolds(split, in frame))
            {
                outcome = CircularArcLinearization3dOutcome.VertexDrift;

                return false;
            }

            if(ExactOrientation3d.PlaneBandComparisonSign(scratch, frame.First, frame.Second, frame.Third, split, frame.PlanarBand) > 0)
            {
                outcome = CircularArcLinearization3dOutcome.PlanarDrift;

                return false;
            }

            pendingTargets[pendingCount] = split;
            pendingDepths[pendingCount] = depth + 1;
            pendingCount++;
        }

        outcome = CircularArcLinearization3dOutcome.Certified;

        return true;
    }

    /// <summary>
    /// The exact sagitta check: true when the chord midpoint of the gap sits at or
    /// outside the comparison radius, which bounds the remaining sub-arc's inward sag
    /// by the published fraction. Only a minor gap may take this check — the caller
    /// gates on the exact side test first, because a near-full-turn gap's chord
    /// midpoint also sits close to the circle, on the wrong side. The midpoint's own
    /// plane distance weakens the in-plane reading of this sphere-distance check only
    /// at second order, which the published bound's slack absorbs.
    /// </summary>
    private static bool ChordClears(Vector3d nearPoint, Vector3d farPoint, in CircleFrame frame)
    {
        Vector3d midpoint = new((nearPoint.X + farPoint.X) / 2.0, (nearPoint.Y + farPoint.Y) / 2.0, (nearPoint.Z + farPoint.Z) / 2.0);

        return ExactSphereExcess.Sign(midpoint, frame.Center, frame.ComparisonRadius) >= 0;
    }

    /// <summary>
    /// Constructs the split vertex for a gap and certifies its membership exactly.
    /// The diametral key is the exact side test: a zero side means the chord runs
    /// through the center's in-plane projection, and the split takes the pinned
    /// in-plane perpendicular of the chord — the plain-double cross of the normal
    /// with the chord — never the midpoint direction, whose length is rounding noise
    /// there. A nonzero side splits at the midpoint direction, toward the circle for
    /// a minor gap and away for a major one. Every candidate is placed through the
    /// plane basis and then passes the exact membership check — the split vertex must
    /// lie on the gap's own sub-arc, the opposite side of the chord from the
    /// sub-arc's complement, seen along the same threaded plane triple — and a
    /// midpoint-direction failure retries through the perpendicular, which is well
    /// conditioned exactly where the midpoint direction is noise. Both perpendicular
    /// signs failing is the membership refusal.
    /// </summary>
    internal static bool TryConstructSplit(Orientation3dScratch scratch, Vector3d nearPoint, Vector3d farPoint, int side, in CircleFrame frame, out Vector3d split)
    {
        if(side != 0)
        {
            Vector3d midpoint = new((nearPoint.X + farPoint.X) / 2.0, (nearPoint.Y + farPoint.Y) / 2.0, (nearPoint.Z + farPoint.Z) / 2.0);
            Vector3d towardMid = Vector3d.Subtract(midpoint, frame.Center);
            double sign = side == Travel ? 1.0 : -1.0;

            if(TryPlaceOnCircle(Vector3d.Multiply(towardMid, sign), in frame, out split)
                && ExactOrientation3d.InPlaneSign(scratch, frame.First, frame.Second, frame.Third, nearPoint, farPoint, split) == MembershipSign)
            {
                return true;
            }
        }

        Vector3d chord = Vector3d.Subtract(farPoint, nearPoint);
        Vector3d perpendicular = Vector3d.Cross(frame.Normal, chord);

        if(TryPlaceOnCircle(perpendicular, in frame, out split)
            && ExactOrientation3d.InPlaneSign(scratch, frame.First, frame.Second, frame.Third, nearPoint, farPoint, split) == MembershipSign)
        {
            return true;
        }

        if(TryPlaceOnCircle(Vector3d.Negate(perpendicular), in frame, out split)
            && ExactOrientation3d.InPlaneSign(scratch, frame.First, frame.Second, frame.Third, nearPoint, farPoint, split) == MembershipSign)
        {
            return true;
        }

        split = default;

        return false;
    }

    /// <summary>
    /// Places a vertex on the certified circle along a direction, through the plane
    /// basis: the direction's off-plane component is dropped by projecting onto the
    /// basis pair, the in-plane component is normalized by its own length, and the
    /// vertex is the center displaced by the radius along that unit direction — two
    /// multiplies and two additions per ordinate. The projection is what keeps a
    /// constructed vertex near the exact plane even when its driving direction
    /// carries off-plane noise. A zero-length or degenerate in-plane direction yields
    /// non-finite ordinates; the caller's membership check rejects them. Above, the
    /// result stays within one radius of the in-wall center whenever it is finite at
    /// all, which the predicates' documented headroom covers; below, an ordinate can
    /// cancel beneath the lower wall — the walls bound the inputs, never the
    /// construction — so the emitting caller re-takes the acceptance-form wall test
    /// on every split before the exact bands consume it.
    /// </summary>
    private static bool TryPlaceOnCircle(Vector3d direction, in CircleFrame frame, out Vector3d vertex)
    {
        double directionU = Vector3d.Dot(direction, frame.BasisU);
        double directionV = Vector3d.Dot(direction, frame.BasisV);
        double length = Math.Sqrt((directionU * directionU) + (directionV * directionV));
        double alongU = frame.Radius * (directionU / length);
        double alongV = frame.Radius * (directionV / length);
        vertex = new Vector3d(
            frame.Center.X + ((alongU * frame.BasisU.X) + (alongV * frame.BasisV.X)),
            frame.Center.Y + ((alongU * frame.BasisU.Y) + (alongV * frame.BasisV.Y)),
            frame.Center.Z + ((alongU * frame.BasisU.Z) + (alongV * frame.BasisV.Z)));

        return double.IsFinite(vertex.X) && double.IsFinite(vertex.Y) && double.IsFinite(vertex.Z);
    }

    /// <summary>
    /// The exact two-sided annulus check against the certified sphere: the vertex
    /// sits at or inside the outer radius and at or outside the inner one. Two exact
    /// predicate evaluations; no rounded comparison participates.
    /// </summary>
    private static bool AnnulusHolds(Vector3d vertex, in CircleFrame frame)
    {
        if(ExactSphereExcess.Sign(vertex, frame.Center, frame.AnnulusOuter) > 0)
        {
            return false;
        }

        return ExactSphereExcess.Sign(vertex, frame.Center, frame.AnnulusInner) >= 0;
    }

    /// <summary>
    /// The ordinate axis of the vector's smallest-magnitude component, ties resolved
    /// toward the earlier axis in X, Y, Z order — the deterministic selection the
    /// basis seed crosses the normal with. The smallest component marks the axis
    /// least aligned with the normal: crossing with it keeps the normal's two
    /// dominant components in the seed direction, so the seed degenerates only when
    /// the normal itself already has. Comparisons against values that are not
    /// numbers answer false and fall through to the last axis; the garbage they
    /// produce downstream is refused by the computed walls.
    /// </summary>
    private static Vector3d SmallestComponentAxis(Vector3d value)
    {
        double magnitudeX = Math.Abs(value.X);
        double magnitudeY = Math.Abs(value.Y);
        double magnitudeZ = Math.Abs(value.Z);
        if(magnitudeX <= magnitudeY && magnitudeX <= magnitudeZ)
        {
            return Vector3d.UnitX;
        }

        if(magnitudeY <= magnitudeZ)
        {
            return Vector3d.UnitY;
        }

        return Vector3d.UnitZ;
    }

    /// <summary>
    /// The acceptance-form wall test for an ordinate: zero, or a magnitude between
    /// the exact orientation predicates' documented walls, adopted unchanged as this
    /// kernel's walls. A value that is not a number fails both comparisons and is
    /// refused — the test never needs to name it.
    /// </summary>
    private static bool OrdinateInWall(double value)
    {
        if(value == 0.0)
        {
            return true;
        }

        double magnitude = Math.Abs(value);

        return magnitude >= ExactOrientation3d.MinimumMagnitude && magnitude <= ExactOrientation3d.MaximumMagnitude;
    }

    /// <summary>
    /// The acceptance-form wall test for a radius: strictly positive and between the
    /// walls — a circle of zero radius is degenerate whatever produced it, and a
    /// value that is not a number fails here too. A radius at or above the lower wall
    /// also places the planar band above its own documented wall, so the joint
    /// exactness window of the planarity comparison holds whenever this test does.
    /// </summary>
    private static bool RadiusInWall(double value)
    {
        return value >= ExactOrientation3d.MinimumMagnitude && value <= ExactOrientation3d.MaximumMagnitude;
    }
}
