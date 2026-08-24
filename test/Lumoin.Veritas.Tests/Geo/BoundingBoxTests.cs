using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The box-algebra canon: the direct rows for <see cref="BoundingBox.Contains"/>
/// and the ladder for <see cref="BoundingBox.Intersects"/>. Both predicates are
/// closed-interval on every edge (touching counts), answer false on any NaN
/// ordinate by IEEE comparison semantics, and carry no operand invariant:
/// symmetry of <c>Intersects</c> holds unconditionally, while
/// <c>Contains ⇒ Intersects</c> holds for well-formed operands only — the
/// inverted-operand counter-example is pinned here as canon.
/// </summary>
[TestClass]
internal sealed class BoundingBoxTests
{
    /// <summary>The closed-interval intersection ladder, taken in both operand orders.</summary>
    /// <param name="aMinX">The left operand's minimum x.</param>
    /// <param name="aMinY">The left operand's minimum y.</param>
    /// <param name="aMaxX">The left operand's maximum x.</param>
    /// <param name="aMaxY">The left operand's maximum y.</param>
    /// <param name="bMinX">The right operand's minimum x.</param>
    /// <param name="bMinY">The right operand's minimum y.</param>
    /// <param name="bMaxX">The right operand's maximum x.</param>
    /// <param name="bMaxY">The right operand's maximum y.</param>
    /// <param name="expected">The expected answer.</param>
    [TestMethod]
    [DataRow(0d, 0d, 10d, 10d, 10d, 0d, 20d, 10d, true, DisplayName = "Touching edge: shared MaxX/MinX line meets in a segment, closed interval answers true")]
    [DataRow(0d, 0d, 10d, 10d, 10d, 10d, 20d, 20d, true, DisplayName = "Touching corner: the single shared point (10,10) intersects under closed intervals")]
    [DataRow(0d, 0d, 10d, 10d, 2d, 2d, 8d, 8d, true, DisplayName = "Nested: an enclosed box intersects its encloser")]
    [DataRow(0d, 0d, 10d, 10d, 0d, 0d, 10d, 10d, true, DisplayName = "Identical: a box intersects itself")]
    [DataRow(0d, 0d, 10d, 10d, 5d, 5d, 15d, 15d, true, DisplayName = "Partial overlap: intersects without containment in either direction")]
    [DataRow(0d, 0d, 10d, 10d, 10d, 5d, 10d, 5d, true, DisplayName = "Point-box on the edge: the degenerate box at (10,5) lies on the boundary, closed interval answers true")]
    [DataRow(0d, 0d, 10d, 10d, 4d, 6d, 4d, 6d, true, DisplayName = "Point-box in the interior intersects")]
    [DataRow(0d, 0d, 10d, 10d, 11d, 5d, 11d, 5d, false, DisplayName = "Point-box in the exterior does not intersect")]
    [DataRow(0d, 0d, 10d, 10d, 20d, 20d, 30d, 30d, false, DisplayName = "Disjoint: separated on both axes")]
    [DataRow(0d, 0d, 10d, 10d, 11d, 0d, 20d, 10d, false, DisplayName = "Disjoint on X alone: Y ranges overlap but MinX 11 exceeds MaxX 10")]
    public void IntersectsLadder(
        double aMinX, double aMinY, double aMaxX, double aMaxY,
        double bMinX, double bMinY, double bMaxX, double bMaxY,
        bool expected)
    {
        var a = new BoundingBox(aMinX, aMinY, aMaxX, aMaxY);
        var b = new BoundingBox(bMinX, bMinY, bMaxX, bMaxY);

        Assert.AreEqual(expected, a.Intersects(b), "Intersects must follow the closed-interval algebra.");
        Assert.AreEqual(expected, b.Intersects(a), "Intersects is symmetric; the mirrored call must agree.");
    }

    /// <summary>One representable step of separation misses; there is no epsilon in the predicate.</summary>
    [TestMethod]
    public void OneUlpSeparationDoesNotIntersect()
    {
        //The tightest possible miss: b starts one representable double above a's MaxX. The
        //closed-interval algebra admits exact touching (the ladder's edge row) and must reject
        //this — there is no epsilon anywhere in the predicate.
        var a = new BoundingBox(0, 0, 10, 10);
        var oneUlpPastX = new BoundingBox(Math.BitIncrement(10d), 0, 20, 10);
        var oneUlpPastY = new BoundingBox(0, Math.BitIncrement(10d), 10, 20);

        Assert.IsFalse(a.Intersects(oneUlpPastX), "One ulp of X separation must not intersect.");
        Assert.IsFalse(oneUlpPastX.Intersects(a));
        Assert.IsFalse(a.Intersects(oneUlpPastY), "One ulp of Y separation must not intersect.");
        Assert.IsFalse(oneUlpPastY.Intersects(a));
    }

    /// <summary>A NaN ordinate poisons the conjunction in both predicates and both operand positions.</summary>
    /// <param name="minX">The poisoned operand's minimum x.</param>
    /// <param name="minY">The poisoned operand's minimum y.</param>
    /// <param name="maxX">The poisoned operand's maximum x.</param>
    /// <param name="maxY">The poisoned operand's maximum y.</param>
    [TestMethod]
    [DataRow(double.NaN, 0d, 10d, 10d, DisplayName = "NaN MinX on the left operand answers false")]
    [DataRow(0d, double.NaN, 10d, 10d, DisplayName = "NaN MinY on the left operand answers false")]
    [DataRow(0d, 0d, double.NaN, 10d, DisplayName = "NaN MaxX on the left operand answers false")]
    [DataRow(0d, 0d, 10d, double.NaN, DisplayName = "NaN MaxY on the left operand answers false")]
    public void NaNOrdinateAnswersFalseInBothPredicates(double minX, double minY, double maxX, double maxY)
    {
        //Every comparison with NaN is false, so a NaN ordinate poisons the conjunction on
        //whichever side it sits — both predicates, both operand positions.
        var poisoned = new BoundingBox(minX, minY, maxX, maxY);
        var wellFormed = new BoundingBox(0, 0, 10, 10);

        Assert.IsFalse(poisoned.Intersects(wellFormed));
        Assert.IsFalse(wellFormed.Intersects(poisoned));
        Assert.IsFalse(poisoned.Contains(wellFormed));
        Assert.IsFalse(wellFormed.Contains(poisoned));
    }

    /// <summary>The non-strict containment ladder.</summary>
    /// <param name="aMinX">The enclosing candidate's minimum x.</param>
    /// <param name="aMinY">The enclosing candidate's minimum y.</param>
    /// <param name="aMaxX">The enclosing candidate's maximum x.</param>
    /// <param name="aMaxY">The enclosing candidate's maximum y.</param>
    /// <param name="bMinX">The enclosed candidate's minimum x.</param>
    /// <param name="bMinY">The enclosed candidate's minimum y.</param>
    /// <param name="bMaxX">The enclosed candidate's maximum x.</param>
    /// <param name="bMaxY">The enclosed candidate's maximum y.</param>
    /// <param name="expected">The expected answer.</param>
    [TestMethod]
    [DataRow(0d, 0d, 10d, 10d, 0d, 0d, 10d, 5d, true, DisplayName = "Flush inner box touching three outer edges is contained, non-strict")]
    [DataRow(0d, 0d, 10d, 10d, 0d, 0d, 10d, 10d, true, DisplayName = "Identical boxes contain each other")]
    [DataRow(0d, 0d, 10d, 10d, 2d, 2d, 8d, 8d, true, DisplayName = "Strictly nested box is contained")]
    [DataRow(2d, 2d, 8d, 8d, 0d, 0d, 10d, 10d, false, DisplayName = "The inner box does not contain its encloser")]
    [DataRow(0d, 0d, 10d, 10d, 5d, 5d, 15d, 15d, false, DisplayName = "Partial overlap is not containment")]
    [DataRow(0d, 0d, 10d, 10d, 20d, 20d, 30d, 30d, false, DisplayName = "Disjoint boxes contain nothing of each other")]
    [DataRow(0d, 0d, 10d, 10d, 4d, 6d, 4d, 6d, true, DisplayName = "Point-box in the interior is contained")]
    [DataRow(0d, 0d, 10d, 10d, 10d, 10d, 10d, 10d, true, DisplayName = "Point-box on the corner is contained, non-strict")]
    [DataRow(0d, 0d, 10d, 10d, 11d, 5d, 11d, 5d, false, DisplayName = "Point-box in the exterior is not contained")]
    public void ContainsLadder(
        double aMinX, double aMinY, double aMaxX, double aMaxY,
        double bMinX, double bMinY, double bMaxX, double bMaxY,
        bool expected)
    {
        var a = new BoundingBox(aMinX, aMinY, aMaxX, aMaxY);
        var b = new BoundingBox(bMinX, bMinY, bMaxX, bMaxY);

        Assert.AreEqual(expected, a.Contains(b), "Contains must follow the closed-interval algebra.");
    }

    /// <summary>Non-strict bounds admit equality but reject one representable step of escape.</summary>
    [TestMethod]
    public void ContainsRejectsOneUlpEscape()
    {
        //The tightest possible escape: the inner box's MaxX is one representable double past
        //the outer's. Non-strict bounds admit equality (the flush row) and must reject this.
        var outer = new BoundingBox(0, 0, 10, 10);
        var escaping = new BoundingBox(2, 2, Math.BitIncrement(10d), 8);

        Assert.IsFalse(outer.Contains(escaping));
    }

    /// <summary>Symmetry of the intersection predicate holds for malformed operands too.</summary>
    [TestMethod]
    public void IntersectsIsSymmetricUnconditionally()
    {
        //Symmetry carries no operand precondition. The ordinate pool deliberately
        //includes NaN, infinities, and values that produce inverted axes, so the sweep
        //exercises malformed operands as well as well-formed ones. Fixed start value: the
        //sweep is reproducible, not a coverage lottery.
        double[] ordinatePool = [double.NaN, double.NegativeInfinity, -10d, -0.0d, 0.0d, 3d, 10d, double.PositiveInfinity];
        ulong state = 626262UL;

        for(int trial = 0; trial < 20_000; trial++)
        {
            var a = new BoundingBox(
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)],
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)],
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)],
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)]);
            var b = new BoundingBox(
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)],
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)],
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)],
                ordinatePool[DeterministicBitMixer.NextBelow(ref state, ordinatePool.Length)]);

            Assert.AreEqual(a.Intersects(b), b.Intersects(a),
                $"Symmetry must hold unconditionally; failed for {a} vs {b}.");
        }
    }

    /// <summary>Containment implies intersection wherever both operands are well-formed.</summary>
    [TestMethod]
    public void ContainsImpliesIntersectsForWellFormedOperands()
    {
        //The implication is real but preconditioned: for well-formed operands,
        //a.Contains(b) chains a.MinX <= b.MinX <= b.MaxX and b.MinX <= b.MaxX <= a.MaxX, which
        //is exactly Intersects' conjunction. The chain needs b.MinX <= b.MaxX — hence the
        //well-formedness restriction. Fixed-start sweep over finite, non-inverted boxes.
        ulong state = 620862UL;

        for(int trial = 0; trial < 20_000; trial++)
        {
            BoundingBox a = NextWellFormedBox(ref state);
            BoundingBox b = NextWellFormedBox(ref state);

            if(a.Contains(b))
            {
                Assert.IsTrue(a.Intersects(b),
                    $"Contains must imply Intersects on well-formed operands; failed for {a} contains {b}.");
            }
        }
    }

    /// <summary>The canonical counter-example that keeps the implication conditional.</summary>
    [TestMethod]
    public void InvertedOperandBreaksTheImplicationCanonically()
    {
        //The canon counter-example: b's X axis is inverted (MinX 8 > MaxX 3).
        //Contains reads 5 <= 8 && 10 >= 3 && 0 <= 0 && 10 >= 10 — true; Intersects reads
        //5 <= 3 — false. BoundingBox carries no invariant, so the implication is contract
        //only for well-formed operands.
        var a = new BoundingBox(5, 0, 10, 10);
        var inverted = new BoundingBox(8, 0, 3, 10);

        Assert.IsTrue(a.Contains(inverted), "The counter-example's Contains half must hold, or the canon row is miscopied.");
        Assert.IsFalse(a.Intersects(inverted), "The counter-example's Intersects half must fail, or the canon row is miscopied.");
    }

    /// <summary>
    /// A finite box with each axis ordered min ≤ max, ordinates in [−1000, 1000] —
    /// the well-formedness bucket the implication row is restricted to.
    /// </summary>
    /// <param name="state">The mixing state, advanced in place.</param>
    /// <returns>The drawn box.</returns>
    private static BoundingBox NextWellFormedBox(ref ulong state)
    {
        double x1 = (DeterministicBitMixer.NextUnitDouble(ref state) * 2000d) - 1000d;
        double x2 = (DeterministicBitMixer.NextUnitDouble(ref state) * 2000d) - 1000d;
        double y1 = (DeterministicBitMixer.NextUnitDouble(ref state) * 2000d) - 1000d;
        double y2 = (DeterministicBitMixer.NextUnitDouble(ref state) * 2000d) - 1000d;

        return new BoundingBox(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }
}
