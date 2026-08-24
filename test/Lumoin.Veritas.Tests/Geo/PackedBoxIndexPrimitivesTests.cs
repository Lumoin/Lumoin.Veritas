using Lumoin.Veritas.Geo.Spatial;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Direct gates for the packed box index's pure computations. The index-local
/// Hilbert curve is oracled three ways, none of them its own output: an
/// independently written inverse decoder recovers every cell at the deepest
/// grid, the curve's defining properties (a bijection onto the distance range
/// whose consecutive cells are always one step apart) are checked exhaustively
/// on a small grid, and one deep-grid vector is pinned as a literal. The grid
/// normalization rows prove totality over sanitation-legal input:
/// rim-spanning extents that overflow a direct subtraction, subnormal extents
/// that overflow a scale-first form, and the zero-extent pin. The layout rows
/// probe the level/node/parade arithmetic at its boundaries — including past
/// the pooled array ceiling — without building an index.
/// </summary>
[TestClass]
internal sealed class PackedBoxIndexPrimitivesTests
{
    /// <summary>The deepest shipped grid: 31 bits per axis.</summary>
    private const uint DeepGridSide = 1u << 31;

    /// <summary>The small grid the curve canon is checked exhaustively on.</summary>
    private const uint CanonGridSide = 16u;

    /// <summary>The small-span control's expected order: the negative-rim box keys below the positive one.</summary>
    private static readonly int[] NegativeRimFirst = [1, 0];

    /// <summary>The independently written inverse recovers every cell of the deepest grid.</summary>
    [TestMethod]
    public void HilbertDistanceRoundTripsThroughAnIndependentInverse()
    {
        //The corners plus a fixed-start sweep. The inverse below is written from the curve's
        //own recurrence in the opposite direction — distance to cell — so it is not a second
        //copy of the code under test; a forward implementation that drifted could not also
        //survive inversion by it.
        AssertRoundTrips(0u, 0u);
        AssertRoundTrips(DeepGridSide - 1u, 0u);
        AssertRoundTrips(0u, DeepGridSide - 1u);
        AssertRoundTrips(DeepGridSide - 1u, DeepGridSide - 1u);

        ulong state = 0x50_4D_54UL;

        for(int trial = 0; trial < 2_000; trial++)
        {
            uint x = (uint)(DeterministicBitMixer.NextBitPattern(ref state) % DeepGridSide);
            uint y = (uint)(DeterministicBitMixer.NextBitPattern(ref state) % DeepGridSide);

            AssertRoundTrips(x, y);
        }
    }

    /// <summary>The curve's defining properties hold exhaustively on the canon grid.</summary>
    [TestMethod]
    public void HilbertCurveIsABijectionWithUnitStepAdjacency()
    {
        //What makes a curve a Hilbert curve rather than any other space-filling order: the
        //distances over a 2^k grid are exactly the range [0, side²) with no repeat, and cells
        //at consecutive distances are always orthogonally adjacent. Checked over every cell of
        //the sixteen-by-sixteen grid, plus the origin pin that fixes the curve's start.
        uint cellCount = CanonGridSide * CanonGridSide;
        var cellByDistance = new int[cellCount];
        Array.Fill(cellByDistance, -1);

        Assert.AreEqual(0UL, PackedBoxIndexPrimitives.HilbertDistance(CanonGridSide, 0u, 0u), "The curve starts at the origin cell.");

        for(uint x = 0u; x < CanonGridSide; x++)
        {
            for(uint y = 0u; y < CanonGridSide; y++)
            {
                ulong distance = PackedBoxIndexPrimitives.HilbertDistance(CanonGridSide, x, y);

                Assert.IsLessThan(cellCount, distance, $"The distance of ({x}, {y}) must stay inside the grid's distance range.");
                Assert.AreEqual(-1, cellByDistance[(int)distance], $"Distance {distance} was already taken; the curve must be injective.");

                cellByDistance[(int)distance] = (int)((x * CanonGridSide) + y);
            }
        }

        for(uint distance = 1u; distance < cellCount; distance++)
        {
            int previous = cellByDistance[distance - 1u];
            int current = cellByDistance[distance];
            int stepX = Math.Abs((previous / (int)CanonGridSide) - (current / (int)CanonGridSide));
            int stepY = Math.Abs((previous % (int)CanonGridSide) - (current % (int)CanonGridSide));

            Assert.AreEqual(1, stepX + stepY, $"Cells at distances {distance - 1u} and {distance} must be orthogonally adjacent.");
        }
    }

    /// <summary>A committed deep-grid literal keeps the curve's exact orientation and width pinned.</summary>
    [TestMethod]
    public void PinnedVectorExercisesCellsAboveSixteenBits()
    {
        //A committed literal at the full grid depth: the squared-cell distance term reaches
        //two to the sixtieth at this scale, so an unwidened product would wrap on most
        //iterations and move this value. The literal is the contract — it is the same value a
        //separately pinned reference curve answered for this cell.
        ulong distance = PackedBoxIndexPrimitives.HilbertDistance(DeepGridSide, 1_234_567_890u, 987_654_321u);

        Assert.AreEqual(3_831_921_454_093_212_845UL, distance, "The pinned literal moved — the curve changed.");
        AssertRoundTrips(1_234_567_890u, 987_654_321u);
    }

    /// <summary>The grid normalization is total over every input sanitation admits.</summary>
    [TestMethod]
    public void GridCoordinateIsTotalOverSanitationLegalInput()
    {
        const uint GridMaximum = 2_147_483_647u;

        //Zero extent pins to zero for every center.
        Assert.AreEqual(0u, PackedBoxIndexPrimitives.GridCoordinate(5d, 5d, 0d, GridMaximum));

        //A rim-spanning extent: the direct subtraction of the two centers overflows to
        //infinity, the half form stays finite, and a mid-span center lands mid-grid.
        double rimExtentHalf = (double.MaxValue / 2d) - (double.MaxValue / -2d);

        Assert.IsTrue(double.IsFinite(rimExtentHalf), "The half-form extent must survive the rim span the direct subtraction overflows on.");

        uint low = PackedBoxIndexPrimitives.GridCoordinate(-double.MaxValue, -double.MaxValue, rimExtentHalf, GridMaximum);
        uint mid = PackedBoxIndexPrimitives.GridCoordinate(0d, -double.MaxValue, rimExtentHalf, GridMaximum);
        uint high = PackedBoxIndexPrimitives.GridCoordinate(double.MaxValue, -double.MaxValue, rimExtentHalf, GridMaximum);

        Assert.AreEqual(0u, low);
        Assert.IsGreaterThan(0u, mid, "A mid-span center must land strictly inside the grid.");
        Assert.IsGreaterThan(mid, high, "The top-of-span center must land above the middle.");
        Assert.IsLessThanOrEqualTo(GridMaximum, high, "No coordinate may exceed the grid maximum.");

        //A subnormal extent: ratio-before-scale keeps the quotient in range where a
        //scale-first form would overflow; distinct centers keep distinct coordinates.
        double subnormalExtentHalf = (1e-308d / 2d) - 0d;
        uint subnormalLow = PackedBoxIndexPrimitives.GridCoordinate(0d, 0d, subnormalExtentHalf, GridMaximum);
        uint subnormalHigh = PackedBoxIndexPrimitives.GridCoordinate(1e-308d, 0d, subnormalExtentHalf, GridMaximum);

        Assert.AreEqual(0u, subnormalLow);
        Assert.AreEqual(GridMaximum, subnormalHigh, "The subnormal-extent top center must still reach the grid maximum.");

        //The sixteen-bit width obeys the same bounds.
        Assert.AreEqual(65_535u, PackedBoxIndexPrimitives.GridCoordinate(1e-308d, 0d, subnormalExtentHalf, 65_535u));
    }

    /// <summary>The half-form center answers the rim where the naive midpoint overflows.</summary>
    [TestMethod]
    public void BoxCenterSurvivesTheRimWhereTheNaiveMidpointOverflows()
    {
        //Same-sign rim bounds: (min + max) / 2 overflows to infinity, the half form answers
        //the exact value. The mixed-sign row is the negative control — both forms answer it
        //correctly, which shows the rim rows target overflow, not magnitude.
        Assert.AreEqual(double.MaxValue, PackedBoxIndexPrimitives.BoxCenter(double.MaxValue, double.MaxValue));
        Assert.AreEqual(-double.MaxValue, PackedBoxIndexPrimitives.BoxCenter(-double.MaxValue, -double.MaxValue));
        Assert.AreEqual(0d, PackedBoxIndexPrimitives.BoxCenter(-double.MaxValue, double.MaxValue));
        Assert.AreEqual(4d, PackedBoxIndexPrimitives.BoxCenter(2d, 6d));
    }

    /// <summary>A rim-spanning build keys its entries distinctly and orders them deterministically.</summary>
    [TestMethod]
    public void RimSpanningBuildsKeepDistinctKeysAndDeterministicOrder()
    {
        //Three point-boxes at the negative rim, the origin, and the positive rim, registered
        //in descending-x order. A normalization that collapsed under the rim span would key
        //them identically and enumerate in registration order; the total form keys them
        //distinctly, so the origin-cell box — Hilbert distance zero — must enumerate first,
        //and two builds must agree exactly.
        BoundingBox[] items =
        [
            new BoundingBox(double.MaxValue, 0, double.MaxValue, 0),
            new BoundingBox(0, 0, 0, 0),
            new BoundingBox(-double.MaxValue, 0, -double.MaxValue, 0)
        ];

        var options = new PackedBoxIndexOptions(BoxIndexPacking.HilbertCurve, 2);
        using PackedBoxIndex first = PackedBoxIndex.Create(options);
        using PackedBoxIndex second = PackedBoxIndex.Create(options);

        Assert.IsTrue(first.TryBuild(items));
        Assert.IsTrue(second.TryBuild(items));

        var everything = new BoundingBox(-double.MaxValue, -1, double.MaxValue, 1);
        var firstSeen = new List<int>();

        foreach(int candidate in first.Intersecting(in everything))
        {
            firstSeen.Add(candidate);
        }

        var secondSeen = new List<int>();

        foreach(int candidate in second.Intersecting(in everything))
        {
            secondSeen.Add(candidate);
        }

        Assert.AreEqual(2, firstSeen[0], "The rim-normalized keys must place the origin-cell box first, not fall back to registration order.");
        Assert.AreSequenceEqual(firstSeen, secondSeen, "Rim-spanning builds must stay deterministic across rebuilds.");

        //The negative control: a small mixed-sign span behaves identically under the half
        //form and the naive form — the rim rows above target overflow, not sign mixing.
        BoundingBox[] smallSpan =
        [
            new BoundingBox(10, 0, 10, 0),
            new BoundingBox(-10, 0, -10, 0)
        ];

        using PackedBoxIndex control = PackedBoxIndex.Create(options);

        Assert.IsTrue(control.TryBuild(smallSpan));

        var controlSeen = new List<int>();
        var controlProbe = new BoundingBox(-20, -1, 20, 1);

        foreach(int candidate in control.Intersecting(in controlProbe))
        {
            controlSeen.Add(candidate);
        }

        Assert.AreSequenceEqual(NegativeRimFirst, controlSeen, "The negative-rim box keys below the positive one on the small span too.");
    }

    /// <summary>The layout arithmetic holds at the empty, single, billion, over-ceiling, and wide-tree boundaries.</summary>
    [TestMethod]
    public void LayoutArithmeticHoldsAtItsBoundaries()
    {
        //The pure sizing function makes the parade ceiling testable without a build. The
        //two-to-the-thirtieth row at capacity two produces a parade one short of two to the
        //thirty-first, which exceeds the pooled array ceiling; a billion items still fit.
        PackedBoxIndexLayout empty = PackedBoxIndexPrimitives.ComputeLayout(0L, 2);

        Assert.AreEqual(0L, empty.NodeCount);
        Assert.AreEqual(0, empty.LevelCount);
        Assert.AreEqual(0L, empty.TotalSlots);
        Assert.AreEqual(0L, empty.TraversalStackBound);

        PackedBoxIndexLayout single = PackedBoxIndexPrimitives.ComputeLayout(1L, 2);

        Assert.AreEqual(1L, single.NodeCount);
        Assert.AreEqual(1, single.LevelCount);
        Assert.AreEqual(2L, single.TotalSlots);
        Assert.AreEqual(1L, single.TraversalStackBound);

        PackedBoxIndexLayout billion = PackedBoxIndexPrimitives.ComputeLayout(1_000_000_000L, 2);

        Assert.IsLessThanOrEqualTo(Array.MaxLength, billion.TotalSlots, "A billion items at capacity two must still fit the parade ceiling.");

        PackedBoxIndexLayout overCeiling = PackedBoxIndexPrimitives.ComputeLayout(1L << 30, 2);

        Assert.AreEqual((1L << 31) - 1L, overCeiling.TotalSlots, "The capacity-two parade is one short of double the item count.");
        Assert.IsGreaterThan(Array.MaxLength, overCeiling.TotalSlots, "The two-to-the-thirtieth build must exceed the pooled array ceiling.");

        //The stack bound clamps to the node count where the pending-sibling formula would
        //over-provision a shallow, wide tree.
        PackedBoxIndexLayout wide = PackedBoxIndexPrimitives.ComputeLayout(65_537L, 65_536);

        Assert.AreEqual(2, wide.LevelCount);
        Assert.AreEqual(3L, wide.NodeCount);
        Assert.AreEqual(3L, wide.TraversalStackBound, "The bound must clamp to the node count, not rent the sixty-four-kilobyte formula answer.");
    }

    /// <summary>The dominance node bound follows the leaf-count arithmetic at its boundaries.</summary>
    /// <param name="itemCount">The item count of the build.</param>
    /// <param name="expected">The expected dominance node bound.</param>
    [TestMethod]
    [DataRow(0L, 0L, DisplayName = "zero items: the empty build")]
    [DataRow(1L, 1L, DisplayName = "one item: the sole leaf, one node bound")]
    [DataRow(4L, 1L, DisplayName = "four items: still one leaf bound")]
    [DataRow(8L, 3L, DisplayName = "eight items: the leaf ceiling itself")]
    [DataRow(9L, 5L, DisplayName = "nine items: one split above the leaf ceiling")]
    [DataRow(16L, 7L, DisplayName = "sixteen items")]
    [DataRow(17L, 9L, DisplayName = "seventeen items")]
    [DataRow(20L, 9L, DisplayName = "twenty items")]
    [DataRow(64L, 31L, DisplayName = "sixty-four items")]
    [DataRow(10001L, 5001L, DisplayName = "ten thousand and one items")]
    [DataRow(2000000000L, 999999999L, DisplayName = "two billion items")]
    public void DominanceNodeBoundIsTheLeafArithmetic(long itemCount, long expected)
    {
        //The bound is twice the quarter-count ceiling minus one — every split of a range
        //above eight items leaves both halves at least four, so leaves number at most the
        //quarter ceiling and a full binary tree doubles that minus one; the formula
        //intentionally over-covers small builds whose single leaf is cheaper than the bound.
        long actual = PackedBoxIndexPrimitives.ComputeDominanceNodeBound(itemCount);

        Assert.AreEqual(expected, actual, $"The dominance node bound diverged for {itemCount} items.");
    }

    /// <summary>The dominance traversal stack bound follows the depth arithmetic at its boundaries.</summary>
    /// <param name="itemCount">The item count of the build.</param>
    /// <param name="expected">The expected dominance traversal stack bound.</param>
    [TestMethod]
    [DataRow(0L, 0L, DisplayName = "zero items: the empty build")]
    [DataRow(1L, 2L, DisplayName = "one item: the constant floor")]
    [DataRow(4L, 2L, DisplayName = "four items: still the constant floor")]
    [DataRow(7L, 2L, DisplayName = "seven items: still the constant floor")]
    [DataRow(8L, 3L, DisplayName = "eight items: the first quarter step")]
    [DataRow(9L, 3L, DisplayName = "nine items")]
    [DataRow(16L, 4L, DisplayName = "sixteen items")]
    [DataRow(64L, 6L, DisplayName = "sixty-four items")]
    [DataRow(1000L, 10L, DisplayName = "one thousand items")]
    [DataRow(10001L, 14L, DisplayName = "ten thousand and one items")]
    public void DominanceTraversalStackBoundIsTheDepthArithmetic(long itemCount, long expected)
    {
        //Ceiling log2 of the quarter count plus two — the median split halves the range per
        //level down to eight-item leaves and a descent holds one pending sibling per level
        //plus the node in hand.
        long actual = PackedBoxIndexPrimitives.ComputeDominanceTraversalStackBound(itemCount);

        Assert.AreEqual(expected, actual, $"The dominance traversal stack bound diverged for {itemCount} items.");
    }

    /// <summary>A built dominance structure never exceeds its derived node and traversal-stack bounds.</summary>
    /// <param name="itemCount">The item count of the build.</param>
    [TestMethod]
    [DataRow(1, DisplayName = "one item: the single dominance leaf")]
    [DataRow(8, DisplayName = "eight items: the leaf ceiling itself")]
    [DataRow(9, DisplayName = "nine items: one split above the leaf ceiling")]
    [DataRow(20, DisplayName = "twenty items")]
    [DataRow(64, DisplayName = "sixty-four items")]
    [DataRow(1000, DisplayName = "one thousand items")]
    [DataRow(10001, DisplayName = "ten thousand and one items")]
    public void BuiltDominanceStructureStaysWithinTheDerivedBounds(int itemCount)
    {
        var items = new BoundingBox[itemCount];

        for(int item = 0; item < itemCount; item++)
        {
            double offset = item * 2d;
            items[item] = new BoundingBox(offset, 0d, offset + 1d, 1d);
        }

        var options = new PackedBoxIndexOptions(BoxIndexPacking.SortTileRecursive, 16);
        using PackedBoxIndex index = PackedBoxIndex.Create(options);

        Assert.IsTrue(index.TryBuild(items), $"The spaced unit-box build of {itemCount} items must succeed.");

        long nodeBound = PackedBoxIndexPrimitives.ComputeDominanceNodeBound(itemCount);
        long stackBound = PackedBoxIndexPrimitives.ComputeDominanceTraversalStackBound(itemCount);

        Assert.IsLessThanOrEqualTo(nodeBound, index.DominanceNodeCount, "The built dominance node count must not exceed the derived bound.");
        Assert.IsLessThanOrEqualTo((2L * index.DominanceNodeCount) + 8L, nodeBound, "The bound must stay within roughly double the built node count — the tightness witness.");
        Assert.AreEqual((int)stackBound, index.DominanceTraversalStackBound, "The built traversal stack rental must equal the derived bound exactly.");
    }

    /// <summary>Asserts the independent inverse recovers the exact cell from the forward distance.</summary>
    /// <param name="x">The cell's x coordinate.</param>
    /// <param name="y">The cell's y coordinate.</param>
    private static void AssertRoundTrips(uint x, uint y)
    {
        ulong distance = PackedBoxIndexPrimitives.HilbertDistance(DeepGridSide, x, y);
        (uint decodedX, uint decodedY) = InverseHilbertCell(DeepGridSide, distance);

        Assert.AreEqual(x, decodedX, $"The inverse recovered the wrong X for ({x}, {y}).");
        Assert.AreEqual(y, decodedY, $"The inverse recovered the wrong Y for ({x}, {y}).");
    }

    /// <summary>
    /// The independent oracle: the cell at <paramref name="distance"/> on a
    /// <paramref name="gridSide"/> × <paramref name="gridSide"/> grid, built
    /// bottom-up from the curve's recurrence — the opposite direction from the
    /// top-down cell-halving code under test, so agreement is evidence rather
    /// than tautology.
    /// </summary>
    /// <param name="gridSide">The grid side length; a power of two.</param>
    /// <param name="distance">The distance along the curve.</param>
    /// <returns>The cell at that distance.</returns>
    private static (uint X, uint Y) InverseHilbertCell(uint gridSide, ulong distance)
    {
        uint x = 0u;
        uint y = 0u;
        ulong remaining = distance;

        for(uint cell = 1u; cell < gridSide; cell *= 2u)
        {
            uint quadrantX = (uint)((remaining >> 1) & 1UL);
            uint quadrantY = (uint)((remaining ^ quadrantX) & 1UL);

            if(quadrantY == 0u)
            {
                if(quadrantX != 0u)
                {
                    x = cell - 1u - x;
                    y = cell - 1u - y;
                }

                (x, y) = (y, x);
            }

            x += cell * quadrantX;
            y += cell * quadrantY;
            remaining >>= 2;
        }

        return (x, y);
    }
}
