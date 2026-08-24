using System;
using System.Collections.Generic;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The certified covering circle of any operand's point set, point-set-total like the
/// convex hull: candidates are the flat vertex column whole —
/// kind-blind, every part role, every collection depth — because no operand's
/// interior extends past its vertices' hull. The walk runs on the hull's open
/// counter-clockwise cycle: three survivors settle on the exact angle signs
/// alone (a triangle has at most one non-acute vertex — all acute answers the
/// circumcircle, else that vertex deletes; the radius keys of a triangle are a
/// structural three-way tie doubles must not break); four or more
/// survivors maximize <c>(radius, angle)</c> lexicographically over unconditional
/// circumcircle candidates with division-free squared keys, delete a non-acute
/// winner, and answer an acute winner's circumcircle. The one decision steering
/// topology — acute or not — is <see cref="ExactOrientation.DirectionDotSign"/>,
/// exact; magnitudes stay plain double per the house split. Every answer then
/// passes certification: the exact maximum squared vertex distance from the
/// center is compared against the returned radius by
/// <see cref="ExactCircleExcess"/>, and when the rounded carrier falls short the
/// radius — never the center — becomes the smallest representable double whose
/// square covers that maximum. The contract: every operand vertex certified
/// inside-or-on the returned circle, exactly, within the excess predicate's
/// documented quantum walls; every edge and interior point covered because a
/// disk is convex and the operand lies in its vertices' convex hull.
/// Deterministic: no randomization, no transcendentals, ties break to the
/// smallest original hull index, the winning circle materializes once through
/// the anchored circumcenter solve, and the lift is a pure function of the
/// vertex column and the walk's answer. Refusal is by emptiness, never by kind.
/// </summary>
/// <remarks>
/// The magnitude keys are plain double, so near-concyclic operands with hulls of four
/// or more vertices carry a measured residual: the answered circle can exceed the
/// minimal one (the circles always cover). The three-vertex case is settled on exact
/// predicates and is residual-free, and a fired lift returns the smallest
/// representable covering radius at the walk's center. The excess predicate's quantum
/// walls are the caller's contract: a high-wall violation throws, and ordinates below
/// roughly 1e-130 degrade silently — far below any geographic coordinate domain.
/// </remarks>
public static class GeometryBoundingCircle
{
    /// <summary>Far above the single-digit bound the lift's rounding argument yields; exceeding it means the operand broke the finite-ordinate contract.</summary>
    private const int LiftStepCeiling = 64;

    /// <summary>
    /// Computes the certified covering circle; false when the operand carries no
    /// positions (every typed empty, the empty collection, and <c>default</c>).
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="circle">The computed circle.</param>
    /// <returns><see langword="true"/> when the operand carries positions.</returns>
    public static bool TryCompute(in FlatGeometry geometry, out BoundingCircle circle)
    {
        return TryCompute(in geometry, out circle, out _, out _);
    }

    /// <summary>
    /// The walk with its observables — the round count the topology tests pin on
    /// integer-exact operands, where the keys are bit-stable, and the certification
    /// lift count, zero on every input whose walk answer already covers. Each
    /// round of four or more survivors deletes at most one vertex, so deletions
    /// halt at the two-survivor diametral collapse; the one-survivor answer is an
    /// entry case only. Every successful exit routes through <see cref="Certify"/>.
    /// </summary>
    /// <param name="geometry">The operand.</param>
    /// <param name="circle">The computed circle.</param>
    /// <param name="deletionRounds">The number of rounds that deleted a survivor.</param>
    /// <param name="radiusLiftSteps">The certification lift count; zero when the walk's answer already covers.</param>
    /// <returns><see langword="true"/> when the operand carries positions.</returns>
    internal static bool TryCompute(in FlatGeometry geometry, out BoundingCircle circle, out int deletionRounds, out int radiusLiftSteps)
    {
        deletionRounds = 0;
        radiusLiftSteps = 0;

        if(geometry.IsEmpty)
        {
            circle = default;

            return false;
        }

        var hull = new List<Point2d>();
        GeometryConvexHull.ComputeHullVertices(in geometry, hull);

        var survivors = new List<int>(hull.Count);

        for(int index = 0; index < hull.Count; index++)
        {
            survivors.Add(index);
        }

        while(true)
        {
            if(survivors.Count == 1)
            {
                circle = new BoundingCircle(hull[survivors[0]], 0);
                radiusLiftSteps = Certify(in geometry, ref circle);

                return true;
            }

            if(survivors.Count == 2)
            {
                circle = Diametral(hull[survivors[0]], hull[survivors[1]]);
                radiusLiftSteps = Certify(in geometry, ref circle);

                return true;
            }

            if(survivors.Count == 3)
            {
                if(TrySettleTriangle(hull, survivors, out circle))
                {
                    radiusLiftSteps = Certify(in geometry, ref circle);

                    return true;
                }

                deletionRounds++;

                continue;
            }

            int winner = MaximizingVertex(hull, survivors, out int winnerDotSign);

            if(winnerDotSign > 0)
            {
                int position = survivors.IndexOf(winner);
                Point2d predecessor = hull[survivors[(position + survivors.Count - 1) % survivors.Count]];
                Point2d successor = hull[survivors[(position + 1) % survivors.Count]];
                circle = Circumcircle(predecessor, hull[winner], successor);
                radiusLiftSteps = Certify(in geometry, ref circle);

                return true;
            }

            survivors.Remove(winner);
            deletionRounds++;
        }
    }

    /// <summary>
    /// The certification pass, one column scan and one comparison: the exact maximum
    /// squared vertex distance from the center is tracked as an expansion (a radius
    /// covering the farthest vertex covers every vertex — the comparisons chain
    /// exactly), and the cover check is the exact excess sign of that maximum against
    /// the walk's radius. A covering carrier returns untouched. Otherwise the radius —
    /// never the center — becomes the SMALLEST representable double whose square
    /// covers the maximum: the square root of the expansion's folded approximation,
    /// bit-stepped up until the exact check clears — the seed provably never lands
    /// above the minimum, so the first clearing value is it and no downward phase
    /// exists. Every buffer is a stackalloc span and the maximum is a span copy,
    /// so the pass allocates nothing and is a pure function of the operand and the
    /// candidate. Returns the lift step count: zero on the covering path, otherwise
    /// the candidate plus each bit step. Internal so the suite drives the routine
    /// directly.
    /// </summary>
    /// <param name="geometry">The operand the candidate was computed from.</param>
    /// <param name="circle">The candidate; a fired lift replaces its radius in place.</param>
    /// <returns>The lift step count; zero when the candidate already covers.</returns>
    internal static int Certify(in FlatGeometry geometry, ref BoundingCircle circle)
    {
        Span<double> maximum = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> current = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> negation = stackalloc double[ExactCircleExcess.SquaredDistanceComponents];
        Span<double> difference = stackalloc double[2 * ExactCircleExcess.SquaredDistanceComponents];
        Span<double> radiusNegation = stackalloc double[2];
        Span<double> excess = stackalloc double[ExactCircleExcess.ExcessComponents];
        int maximumCount = 0;

        foreach(Point2d vertex in geometry.Vertices)
        {
            int currentCount = ExactCircleExcess.SquaredDistance(vertex, circle.Center, current);

            if(maximumCount == 0 || ExactCircleExcess.CompareSquaredDistances(current[..currentCount], maximum[..maximumCount], negation, difference) > 0)
            {
                current[..currentCount].CopyTo(maximum);
                maximumCount = currentCount;
            }
        }

        if(maximumCount == 0 || ExactCircleExcess.ExcessSign(circle.Radius, maximum[..maximumCount], radiusNegation, excess) <= 0)
        {
            return 0;
        }

        double approximation = 0.0;

        for(int index = 0; index < maximumCount; index++)
        {
            approximation += maximum[index];
        }

        double radius = Math.Max(circle.Radius, Math.Sqrt(approximation));

        if(!double.IsFinite(radius))
        {
            throw new InvalidOperationException("The certification radius candidate is not finite; the operand violates the finite-ordinate contract.");
        }

        //No downward phase exists because none is reachable: the fold of a
        //nonoverlapping increasing-magnitude expansion sits within about half a unit
        //of rounding of the true value, so the square-root seed can never exceed the
        //smallest representable covering radius — the ratchet's first clearing value
        //IS the minimum. The suite pins the never-overshoots property directly.
        int liftSteps = 1;

        while(ExactCircleExcess.ExcessSign(radius, maximum[..maximumCount], radiusNegation, excess) > 0)
        {
            radius = Math.BitIncrement(radius);
            liftSteps++;

            if(liftSteps > LiftStepCeiling)
            {
                throw new InvalidOperationException("The certification ratchet exceeded its ceiling; the operand violates the finite-ordinate contract.");
            }
        }

        circle = new BoundingCircle(circle.Center, radius);

        return liftSteps;
    }

    /// <summary>
    /// The exact three-survivor rule: every candidate of a triangle is
    /// the same circle, so the round is settled on the three
    /// <see cref="ExactOrientation.DirectionDotSign"/> gates alone — all positive
    /// answers the circumcircle (computed with the smallest-index survivor as the
    /// anchor, for bitwise reproducibility), otherwise the first non-acute vertex
    /// (unique on a strict-turn triple) is deleted and the walk continues. False
    /// means a deletion happened.
    /// </summary>
    /// <param name="hull">The hull cycle the survivors index into.</param>
    /// <param name="survivors">The three surviving hull indices; a deletion mutates it.</param>
    /// <param name="circle">The settled circle when the triangle is acute.</param>
    /// <returns><see langword="false"/> when a vertex was deleted instead.</returns>
    private static bool TrySettleTriangle(List<Point2d> hull, List<int> survivors, out BoundingCircle circle)
    {
        for(int position = 0; position < 3; position++)
        {
            Point2d apex = hull[survivors[position]];
            Point2d before = hull[survivors[(position + 2) % 3]];
            Point2d after = hull[survivors[(position + 1) % 3]];

            if(ExactOrientation.DirectionDotSign(apex, before, apex, after) <= 0)
            {
                survivors.RemoveAt(position);
                circle = default;

                return false;
            }
        }

        circle = Circumcircle(hull[survivors[2]], hull[survivors[0]], hull[survivors[1]]);

        return true;
    }

    /// <summary>
    /// One maximization round over four or more survivors: the winner maximizes the
    /// lexicographic key — squared circumradius compared division-free as
    /// <c>N₁·D₂² ⋚ N₂·D₁²</c>, then the exact angle class (obtuse over right over
    /// acute), then the squared-cosine order within an open class — scanned in
    /// ascending original-index order keeping strict improvements only, so
    /// remaining ties break to the smallest original hull index.
    /// </summary>
    /// <param name="hull">The hull cycle the survivors index into.</param>
    /// <param name="survivors">The surviving hull indices in cycle order.</param>
    /// <param name="winnerDotSign">The winner's exact angle sign.</param>
    /// <returns>The winning hull index.</returns>
    private static int MaximizingVertex(List<Point2d> hull, List<int> survivors, out int winnerDotSign)
    {
        int bestPosition = 0;
        double bestProducts = 0;
        double bestCrossSquared = 0;
        int bestDotSign = 0;
        double bestDot = 0;
        double bestLengths = 0;
        bool haveBest = false;

        for(int position = 0; position < survivors.Count; position++)
        {
            Point2d q = hull[survivors[position]];
            Point2d p = hull[survivors[(position + survivors.Count - 1) % survivors.Count]];
            Point2d r = hull[survivors[(position + 1) % survivors.Count]];

            double ux = p.X - q.X;
            double uy = p.Y - q.Y;
            double vx = r.X - q.X;
            double vy = r.Y - q.Y;
            double uu = (ux * ux) + (uy * uy);
            double vv = (vx * vx) + (vy * vy);
            double wx = ux - vx;
            double wy = uy - vy;
            double ww = (wx * wx) + (wy * wy);
            double products = uu * vv * ww;
            double cross = (ux * vy) - (uy * vx);
            double crossSquared = cross * cross;
            int dotSign = ExactOrientation.DirectionDotSign(q, p, q, r);
            double dot = (ux * vx) + (uy * vy);
            double lengths = uu * vv;

            if(!haveBest || Beats(products, crossSquared, dotSign, dot, lengths, bestProducts, bestCrossSquared, bestDotSign, bestDot, bestLengths))
            {
                bestPosition = position;
                bestProducts = products;
                bestCrossSquared = crossSquared;
                bestDotSign = dotSign;
                bestDot = dot;
                bestLengths = lengths;
                haveBest = true;
            }
        }

        winnerDotSign = bestDotSign;

        return survivors[bestPosition];
    }

    /// <summary>
    /// Whether the candidate key strictly beats the best key: squared radius first
    /// (<c>N·D²</c> cross-multiplied), then the exact angle class with the larger
    /// angle ranking higher (negative dot over zero over positive), then within an
    /// open class the squared-cosine cross-comparison — smaller wins among acute
    /// angles, larger wins among obtuse, and the right class is a vacuous tie.
    /// </summary>
    /// <param name="products">The candidate's squared-radius numerator factor.</param>
    /// <param name="crossSquared">The candidate's squared-radius denominator factor.</param>
    /// <param name="dotSign">The candidate's exact angle sign.</param>
    /// <param name="dot">The candidate's plain dot product.</param>
    /// <param name="lengths">The candidate's squared side-length product.</param>
    /// <param name="bestProducts">The incumbent's squared-radius numerator factor.</param>
    /// <param name="bestCrossSquared">The incumbent's squared-radius denominator factor.</param>
    /// <param name="bestDotSign">The incumbent's exact angle sign.</param>
    /// <param name="bestDot">The incumbent's plain dot product.</param>
    /// <param name="bestLengths">The incumbent's squared side-length product.</param>
    /// <returns><see langword="true"/> when the candidate strictly beats the incumbent.</returns>
    private static bool Beats(
        double products,
        double crossSquared,
        int dotSign,
        double dot,
        double lengths,
        double bestProducts,
        double bestCrossSquared,
        int bestDotSign,
        double bestDot,
        double bestLengths)
    {
        double candidateRadius = products * bestCrossSquared;
        double bestRadius = bestProducts * crossSquared;

        if(candidateRadius != bestRadius)
        {
            return candidateRadius > bestRadius;
        }

        if(dotSign != bestDotSign)
        {
            return dotSign < bestDotSign;
        }

        if(dotSign == 0)
        {
            return false;
        }

        double candidateCosine = dot * dot * bestLengths;
        double bestCosine = bestDot * bestDot * lengths;

        if(candidateCosine != bestCosine)
        {
            return dotSign > 0 ? candidateCosine < bestCosine : candidateCosine > bestCosine;
        }

        return false;
    }

    /// <summary>
    /// The anchored circumcenter solve: every input a difference from
    /// the apex, so the perpendicular-bisector system conditions at any offset;
    /// the radius is the distance from the center back to the apex — the single
    /// square root of the operation.
    /// </summary>
    /// <param name="p">The first triple position.</param>
    /// <param name="q">The apex the solve anchors on.</param>
    /// <param name="r">The third triple position.</param>
    /// <returns>The circumcircle.</returns>
    private static BoundingCircle Circumcircle(Point2d p, Point2d q, Point2d r)
    {
        double ux = p.X - q.X;
        double uy = p.Y - q.Y;
        double vx = r.X - q.X;
        double vy = r.Y - q.Y;
        double uu = (ux * ux) + (uy * uy);
        double vv = (vx * vx) + (vy * vy);
        double cross = (ux * vy) - (uy * vx);
        double offsetX = ((vy * uu) - (uy * vv)) / (2.0 * cross);
        double offsetY = ((ux * vv) - (vx * uu)) / (2.0 * cross);

        return new BoundingCircle(
            new Point2d(q.X + offsetX, q.Y + offsetY),
            Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY)));
    }

    /// <summary>The circle on the segment's midpoint with half its length as the radius.</summary>
    /// <param name="first">The segment's first endpoint.</param>
    /// <param name="second">The segment's second endpoint.</param>
    /// <returns>The diametral circle.</returns>
    private static BoundingCircle Diametral(Point2d first, Point2d second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;

        return new BoundingCircle(
            new Point2d((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0),
            Math.Sqrt((dx * dx) + (dy * dy)) / 2.0);
    }
}
