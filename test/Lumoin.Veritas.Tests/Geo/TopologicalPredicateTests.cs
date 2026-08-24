using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The predicate-matrix and branch families of the relate engine: the
/// twenty-four named predicates over the curated corpus, directional pairs as
/// transposed equivalences, the dimension-branched crosses and gated
/// overlaps, the observable sf/eh overlap divergence, and pattern-argument
/// validation.
/// </summary>
[TestClass]
internal sealed class TopologicalPredicateTests
{
    /// <summary>The reference square operand.</summary>
    private const string Square = "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))";

    /// <summary>A square strictly containing <see cref="Square"/> with a shared corner.</summary>
    private const string LargeSquare = "POLYGON ((0 0, 8 0, 8 8, 0 8, 0 0))";

    /// <summary>A square strictly inside <see cref="Square"/>.</summary>
    private const string InnerSquare = "POLYGON ((2 2, 3 2, 3 3, 2 3, 2 2))";

    /// <summary>A square disjoint from <see cref="Square"/>.</summary>
    private const string FarSquare = "POLYGON ((10 10, 12 10, 12 12, 10 12, 10 10))";

    /// <summary>A square overlapping <see cref="Square"/> in area.</summary>
    private const string OverlappingSquare = "POLYGON ((2 2, 6 2, 6 6, 2 6, 2 2))";

    /// <summary>A square touching <see cref="Square"/> at one corner.</summary>
    private const string TouchingSquare = "POLYGON ((4 4, 6 4, 6 6, 4 6, 4 4))";

    /// <summary>The diagonal line operand.</summary>
    private const string Diagonal = "LINESTRING (0 0, 2 2)";

    /// <summary>The anti-diagonal line crossing <see cref="Diagonal"/>.</summary>
    private const string AntiDiagonal = "LINESTRING (0 2, 2 0)";

    /// <summary>The named predicates answer the curated corpus.</summary>
    /// <param name="predicate">The predicate under test.</param>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="expected">The expected verdict.</param>
    [TestMethod]
    [DataRow(TopologicalPredicate.SfEquals, Square, Square, true)]
    [DataRow(TopologicalPredicate.SfEquals, Square, InnerSquare, false)]
    [DataRow(TopologicalPredicate.EhEquals, Square, Square, true)]
    [DataRow(TopologicalPredicate.Rcc8Eq, Square, Square, true)]
    [DataRow(TopologicalPredicate.SfDisjoint, Square, FarSquare, true)]
    [DataRow(TopologicalPredicate.SfDisjoint, Square, OverlappingSquare, false)]
    [DataRow(TopologicalPredicate.EhDisjoint, Square, FarSquare, true)]
    [DataRow(TopologicalPredicate.SfIntersects, Square, OverlappingSquare, true)]
    [DataRow(TopologicalPredicate.SfIntersects, Square, FarSquare, false)]
    [DataRow(TopologicalPredicate.SfTouches, Square, TouchingSquare, true)]
    [DataRow(TopologicalPredicate.SfTouches, Square, OverlappingSquare, false)]
    [DataRow(TopologicalPredicate.EhMeet, Square, TouchingSquare, true)]
    [DataRow(TopologicalPredicate.SfWithin, InnerSquare, Square, true)]
    [DataRow(TopologicalPredicate.SfWithin, Square, InnerSquare, false)]
    [DataRow(TopologicalPredicate.SfContains, Square, InnerSquare, true)]
    [DataRow(TopologicalPredicate.SfOverlaps, Square, OverlappingSquare, true)]
    [DataRow(TopologicalPredicate.SfOverlaps, Square, InnerSquare, false)]
    [DataRow(TopologicalPredicate.EhOverlap, Square, OverlappingSquare, true)]
    [DataRow(TopologicalPredicate.EhInside, InnerSquare, Square, true)]
    [DataRow(TopologicalPredicate.EhContains, Square, InnerSquare, true)]
    [DataRow(TopologicalPredicate.EhCovers, LargeSquare, Square, true)]
    [DataRow(TopologicalPredicate.EhCovers, Square, InnerSquare, false)]
    [DataRow(TopologicalPredicate.EhCoveredBy, Square, LargeSquare, true)]
    [DataRow(TopologicalPredicate.Rcc8Dc, Square, FarSquare, true)]
    [DataRow(TopologicalPredicate.Rcc8Ec, Square, TouchingSquare, true)]
    [DataRow(TopologicalPredicate.Rcc8Po, Square, OverlappingSquare, true)]
    [DataRow(TopologicalPredicate.Rcc8Ntpp, InnerSquare, Square, true)]
    [DataRow(TopologicalPredicate.Rcc8Ntppi, Square, InnerSquare, true)]
    [DataRow(TopologicalPredicate.Rcc8Tpp, Square, LargeSquare, true)]
    [DataRow(TopologicalPredicate.Rcc8Tppi, LargeSquare, Square, true)]
    [DataRow(TopologicalPredicate.Rcc8Ntpp, Square, LargeSquare, false)]
    public void PredicatesAnswerTheCuratedCorpus(TopologicalPredicate predicate, string firstText, string secondText, bool expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, predicate, out bool result), "Non-collection operands always evaluate.");

        Assert.AreEqual(expected, result, $"{predicate}('{firstText}', '{secondText}').");
    }

    /// <summary>Directional predicate pairs agree under operand swap.</summary>
    /// <param name="forward">The forward-direction predicate.</param>
    /// <param name="backward">The inverse-direction predicate.</param>
    [TestMethod]
    [DataRow(TopologicalPredicate.SfWithin, TopologicalPredicate.SfContains)]
    [DataRow(TopologicalPredicate.EhInside, TopologicalPredicate.EhContains)]
    [DataRow(TopologicalPredicate.EhCoveredBy, TopologicalPredicate.EhCovers)]
    [DataRow(TopologicalPredicate.Rcc8Tpp, TopologicalPredicate.Rcc8Tppi)]
    [DataRow(TopologicalPredicate.Rcc8Ntpp, TopologicalPredicate.Rcc8Ntppi)]
    public void DirectionalPairsAgreeUnderOperandSwap(TopologicalPredicate forward, TopologicalPredicate backward)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(InnerSquare, out FlatGeometry inner, out _), "The inner square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(Square, out FlatGeometry outer, out _), "The outer square must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(inner, outer, forward, out bool forwardResult), "The forward pair must evaluate.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(outer, inner, backward, out bool backwardResult), "The swapped pair must evaluate.");

        Assert.AreEqual(forwardResult, backwardResult, $"{forward} forward equals {backward} with swapped operands.");
    }

    /// <summary>Symmetric predicates are order-invariant across the corpus product.</summary>
    /// <param name="predicate">The symmetric predicate under test.</param>
    [TestMethod]
    [DataRow(TopologicalPredicate.SfEquals)]
    [DataRow(TopologicalPredicate.SfDisjoint)]
    [DataRow(TopologicalPredicate.SfIntersects)]
    [DataRow(TopologicalPredicate.SfTouches)]
    [DataRow(TopologicalPredicate.SfCrosses)]
    [DataRow(TopologicalPredicate.SfOverlaps)]
    public void SymmetricPredicatesAreOrderInvariant(TopologicalPredicate predicate)
    {
        string[] corpus = [Square, OverlappingSquare, TouchingSquare, FarSquare, Diagonal];

        foreach(string firstText in corpus)
        {
            foreach(string secondText in corpus)
            {
                Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
                Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
                Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, predicate, out bool forward), "The forward pair must evaluate.");
                Assert.IsTrue(GeometryRelate.TryEvaluate(second, first, predicate, out bool backward), "The backward pair must evaluate.");

                Assert.AreEqual(forward, backward, $"{predicate} is order-invariant on ('{firstText}', '{secondText}').");
            }
        }
    }

    /// <summary>Crosses follows the dimension branches over the fixture table.</summary>
    /// <param name="firstText">The first WKT operand.</param>
    /// <param name="secondText">The second WKT operand.</param>
    /// <param name="expected">The expected crosses verdict.</param>
    [TestMethod]
    [DataRow("POINT (1 1)", Diagonal, false, DisplayName = "Point on a line is within, not crossing")]
    [DataRow("MULTIPOINT ((1 1), (9 9))", Diagonal, true, DisplayName = "Points split across a line cross it")]
    [DataRow(Diagonal, Square, false, DisplayName = "Line inside a polygon is within, not crossing")]
    [DataRow("LINESTRING (2 2, 9 9)", Square, true, DisplayName = "Line leaving a polygon crosses it")]
    [DataRow(Diagonal, AntiDiagonal, true, DisplayName = "Lines meeting at a point cross")]
    [DataRow("LINESTRING (0 0, 1 1)", "LINESTRING (0 0, 1 1)", false, DisplayName = "Equal lines share a linear intersection, not a crossing")]
    public void CrossesFollowsTheDimensionBranches(string firstText, string secondText, bool expected)
    {
        Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, TopologicalPredicate.SfCrosses, out bool result), "The pair must evaluate.");

        Assert.AreEqual(expected, result, $"crosses('{firstText}', '{secondText}') under the dimension branches.");
    }

    /// <summary>Crosses on equal extreme dimensions is a defined false result, never a refusal.</summary>
    [TestMethod]
    public void CrossesAnswersFalseThroughTheResultOnEqualExtremeDimensions()
    {
        Assert.IsTrue(WktGeometryReader.TryRead(Square, out FlatGeometry first, out _), "The square must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(OverlappingSquare, out FlatGeometry second, out _), "The overlapping square must parse.");

        Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, TopologicalPredicate.SfCrosses, out bool areal), "Area against area evaluates.");
        Assert.IsFalse(areal, "Area/area crosses is a defined false result, never a refusal.");

        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry firstPoint, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(firstPoint, firstPoint, TopologicalPredicate.SfCrosses, out bool puntal), "Point against point evaluates.");
        Assert.IsFalse(puntal, "Point/point crosses is a defined false result.");
    }

    /// <summary>Overlaps requires equal dimensions and, for lines, a linear intersection.</summary>
    [TestMethod]
    public void OverlapsRequiresEqualDimensionsAndALinearLineIntersection()
    {
        Assert.IsTrue(WktGeometryReader.TryRead(Diagonal, out FlatGeometry line, out _), "The line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(Square, out FlatGeometry polygon, out _), "The polygon must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(line, polygon, TopologicalPredicate.SfOverlaps, out bool mixed), "Mixed dimensions evaluate.");
        Assert.IsFalse(mixed, "Overlaps of unequal dimensions is a defined false result.");

        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (0 0, 4 4)", out FlatGeometry longDiagonal, out _), "The long line must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING (2 2, 6 6)", out FlatGeometry shifted, out _), "The shifted line must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(longDiagonal, shifted, TopologicalPredicate.SfOverlaps, out bool linear), "Partially overlapping lines evaluate.");
        Assert.IsTrue(linear, "A shared linear stretch with both lines extending beyond overlaps.");
    }

    /// <summary>The ungated Egenhofer overlap and the refined Simple Features overlap diverge on crossing lines.</summary>
    [TestMethod]
    public void OverlapNamesDivergeOnCrossingLines()
    {
        Assert.IsTrue(WktGeometryReader.TryRead(Diagonal, out FlatGeometry first, out _), "The diagonal must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(AntiDiagonal, out FlatGeometry second, out _), "The anti-diagonal must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, TopologicalPredicate.EhOverlap, out bool egenhofer), "The Egenhofer name evaluates.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, TopologicalPredicate.SfOverlaps, out bool simpleFeatures), "The Simple Features name evaluates.");

        Assert.IsTrue(egenhofer, "The ungated Egenhofer pattern accepts a point crossing of two lines.");
        Assert.IsFalse(simpleFeatures, "The refined Simple Features pattern demands a linear interior intersection.");
    }

    /// <summary>Mutual-within equality and the literal patterns diverge on coincident points.</summary>
    [TestMethod]
    public void EqualsNamesDivergeOnCoincidentPoints()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(point, point, TopologicalPredicate.SfEquals, out bool simpleFeatures), "The Simple Features name evaluates.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(point, point, TopologicalPredicate.EhEquals, out bool egenhofer), "The Egenhofer name evaluates.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(point, point, TopologicalPredicate.Rcc8Eq, out bool region), "The RCC8 name evaluates.");

        Assert.IsTrue(simpleFeatures, "Mutual within holds for coincident points.");
        Assert.IsFalse(egenhofer, "The literal pattern demands boundary contact points cannot supply.");
        Assert.IsFalse(region, "The RCC8 literal answers the same honest false outside regions.");
    }

    /// <summary>Touches is unsatisfiable for two points — their boundaries are empty.</summary>
    [TestMethod]
    public void TouchesIsUnsatisfiableForTwoPoints()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry first, out _), "The point must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(first, first, TopologicalPredicate.SfTouches, out bool coincident), "Coincident points evaluate.");
        Assert.IsFalse(coincident, "Points have empty boundaries, so no touches disjunct can hold.");
    }

    /// <summary>Only the disjoint names hold when one operand is empty.</summary>
    [TestMethod]
    public void EmptyOperandsAnswerOnlyDisjointTrue()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT EMPTY", out FlatGeometry empty, out _), "The empty point must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(Square, out FlatGeometry square, out _), "The square must parse.");

        for(TopologicalPredicate predicate = TopologicalPredicate.SfEquals; predicate <= TopologicalPredicate.Rcc8Ntppi; predicate++)
        {
            Assert.IsTrue(GeometryRelate.TryEvaluate(empty, square, predicate, out bool result), "Empty operands evaluate.");
            bool expected = predicate is TopologicalPredicate.SfDisjoint or TopologicalPredicate.EhDisjoint;

            Assert.AreEqual(expected, result, $"{predicate}(EMPTY, square): only disjoint holds on an empty operand.");
        }
    }

    /// <summary>Equals of two empties is false, agreeing with the pattern form.</summary>
    [TestMethod]
    public void EmptyEqualsEmptyIsFalseInAgreementWithThePatternForm()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT EMPTY", out FlatGeometry first, out _), "The empty point must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead("LINESTRING EMPTY", out FlatGeometry second, out _), "The empty line must parse.");
        Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, TopologicalPredicate.SfEquals, out bool predicateAnswer), "The predicate evaluates.");
        Assert.IsTrue(GeometryRelate.TryRelate(first, second, "TFFFTFFFT", out bool patternAnswer), "The pattern form evaluates.");

        Assert.IsFalse(predicateAnswer, "Pure matrix algebra: the equals pattern cannot match an empty interior intersection.");
        Assert.AreEqual(predicateAnswer, patternAnswer, "The named predicate and the pattern form must never disagree.");
    }

    /// <summary>Every pattern-defined predicate agrees with its pinned pattern across the corpus.</summary>
    [TestMethod]
    public void PredicateAgreesWithItsPatternAcrossTheCorpus()
    {
        (TopologicalPredicate Predicate, string Pattern)[] pinned =
        [
            (TopologicalPredicate.SfEquals, "TFFFTFFFT"),
            (TopologicalPredicate.SfDisjoint, "FF*FF****"),
            (TopologicalPredicate.SfWithin, "T*F**F***"),
            (TopologicalPredicate.SfContains, "T*****FF*"),
            (TopologicalPredicate.EhOverlap, "T*T***T**"),
            (TopologicalPredicate.EhCovers, "T*TFT*FF*"),
            (TopologicalPredicate.EhCoveredBy, "TFF*TFT**"),
            (TopologicalPredicate.EhInside, "TFF*FFT**"),
            (TopologicalPredicate.EhContains, "T*TFF*FF*"),
            (TopologicalPredicate.Rcc8Dc, "FFTFFTTTT"),
            (TopologicalPredicate.Rcc8Ec, "FFTFTTTTT"),
            (TopologicalPredicate.Rcc8Po, "TTTTTTTTT"),
            (TopologicalPredicate.Rcc8Tpp, "TFFTTFTTT"),
            (TopologicalPredicate.Rcc8Tppi, "TTTFTTFFT"),
            (TopologicalPredicate.Rcc8Ntpp, "TFFTFFTTT"),
            (TopologicalPredicate.Rcc8Ntppi, "TTTFFTFFT"),
        ];
        string[] corpus = [Square, LargeSquare, InnerSquare, FarSquare, OverlappingSquare, TouchingSquare];

        foreach((TopologicalPredicate predicate, string pattern) in pinned)
        {
            foreach(string firstText in corpus)
            {
                foreach(string secondText in corpus)
                {
                    Assert.IsTrue(WktGeometryReader.TryRead(firstText, out FlatGeometry first, out _), $"'{firstText}' must parse.");
                    Assert.IsTrue(WktGeometryReader.TryRead(secondText, out FlatGeometry second, out _), $"'{secondText}' must parse.");
                    Assert.IsTrue(GeometryRelate.TryEvaluate(first, second, predicate, out bool byName), "The named form evaluates.");
                    Assert.IsTrue(GeometryRelate.TryRelate(first, second, pattern, out bool byPattern), "The pattern form evaluates.");

                    Assert.AreEqual(byName, byPattern, $"{predicate} agrees with its pinned pattern on ('{firstText}', '{secondText}') — the gated and branched predicates are carved out by design.");
                }
            }
        }
    }

    /// <summary>A malformed relate pattern refuses instead of answering.</summary>
    /// <param name="pattern">The malformed pattern under test.</param>
    [TestMethod]
    [DataRow("TFFFTFFF", DisplayName = "Too short")]
    [DataRow("TFFFTFFFTT", DisplayName = "Too long")]
    [DataRow("tFFFTFFFT", DisplayName = "Lowercase symbol")]
    [DataRow("TFFFTFFF3", DisplayName = "Digit outside the alphabet")]
    [DataRow("TFFFTFFF ", DisplayName = "Whitespace")]
    public void MalformedPatternsRefuse(string pattern)
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");

        Assert.IsFalse(GeometryRelate.TryRelate(point, point, pattern, out _), $"'{pattern}' is a malformed argument, refused as Try false.");
    }

    /// <summary>Nine wildcards match every matrix.</summary>
    [TestMethod]
    public void AllWildcardPatternHoldsForAnyPair()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry point, out _), "The point must parse.");
        Assert.IsTrue(WktGeometryReader.TryRead(FarSquare, out FlatGeometry square, out _), "The square must parse.");
        Assert.IsTrue(GeometryRelate.TryRelate(point, square, "*********", out bool matches), "The all-wildcard pattern evaluates.");

        Assert.IsTrue(matches, "Nine wildcards match every matrix.");
    }

    /// <summary>An out-of-range predicate value is a caller contract violation and throws.</summary>
    [TestMethod]
    public void OutOfRangePredicateThrows()
    {
        Assert.IsTrue(WktGeometryReader.TryRead("POINT (1 1)", out FlatGeometry pointOne, out _), "The point must parse.");
        FlatGeometry pointTwo = pointOne;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => GeometryRelate.TryEvaluate(pointOne, pointTwo, (TopologicalPredicate)99, out _),
            "An out-of-range predicate value is a caller contract violation, never a Try false.");
    }
}
