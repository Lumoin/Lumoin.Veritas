using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The standalone datatype satisfiability checker over the exact-real interval
/// algebra, the finite enumeration procedure, and the disjoint value-space
/// families. The decided cases mirror the value-space reductions of the W3C
/// OWL 2 datatype conformance cases (the <c>FragmentGaps</c> cluster the tableau
/// abstains on today); the abstention cases pin the sound boundary where the
/// checker reports <see cref="DatatypeSatisfiability.Unknown"/> rather than guess.
/// </summary>
[TestClass]
internal sealed class DatatypeSatisfiabilityCheckerTests
{
    /// <summary>
    /// Mirrors <c>Consistent owl:real range with DataOneOf</c>: a value in
    /// <c>owl:real</c> drawn from <c>{"-INF"^^xsd:float, "-0"^^xsd:integer}</c>
    /// exists, because <c>-0</c> denotes the integer <c>0</c> (a real) while the
    /// float <c>-INF</c> is in the disjoint float space.
    /// </summary>
    [TestMethod]
    public void OwlRealIntersectEnumerationWithRealMemberIsSatisfiable()
    {
        OwlDataRange enumeration = OneOf(Lit("-INF", Vocabulary.Xsd.Float), Lit("-0", Vocabulary.Xsd.Integer));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(OwlVocabulary.Real), enumeration], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Minus Infinity is not in owl:real</c>: once the integer <c>0</c>
    /// filler is excluded the only candidate is the float <c>-INF</c>, which lies
    /// outside the exact-real space of <c>owl:real</c>, so the value space is empty.
    /// </summary>
    [TestMethod]
    public void OwlRealIntersectFloatOnlyEnumerationIsUnsatisfiable()
    {
        OwlDataRange enumeration = OneOf(Lit("-INF", Vocabulary.Xsd.Float));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(OwlVocabulary.Real), enumeration], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Consistent Datatype restrictions with Different Types</c>:
    /// <c>{3, 4}</c> over <c>xsd:integer</c>/<c>xsd:int</c> intersected with
    /// <c>{2, 3}</c> over <c>xsd:short</c>/<c>xsd:int</c> shares the value <c>3</c>
    /// across the derived integer types.
    /// </summary>
    [TestMethod]
    public void EnumerationsAcrossDerivedIntegerTypesShareAValue()
    {
        OwlDataRange first = OneOf(Lit("3", Vocabulary.Xsd.Integer), Lit("4", Vocabulary.Xsd.Int));
        OwlDataRange second = OneOf(Lit("2", Vocabulary.Xsd.Short), Lit("3", Vocabulary.Xsd.Int));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([first, second], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Contradicting datatype Restrictions</c>: <c>{3, 4} ∩ {2, 3}</c>
    /// is <c>{3}</c>, but the restriction <c>xsd:integer[&gt;= 4]</c> excludes
    /// <c>3</c>, leaving no value.
    /// </summary>
    [TestMethod]
    public void EnumerationIntersectionOutsideAMinimumBoundIsUnsatisfiable()
    {
        OwlDataRange first = OneOf(Lit("3", Vocabulary.Xsd.Integer), Lit("4", Vocabulary.Xsd.Integer));
        OwlDataRange second = OneOf(Lit("2", Vocabulary.Xsd.Integer), Lit("3", Vocabulary.Xsd.Integer));
        OwlDataRange atLeastFour = Restriction(Vocabulary.Xsd.Integer, (Vocabulary.XsdFacets.MinInclusive, Lit("4", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([first, second, atLeastFour], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Inconsistent Byte Filler</c>: the enumerated integer
    /// <c>6542145</c> is outside the <c>xsd:byte</c> range <c>[-128, 127]</c>, so
    /// the conjunction with <c>xsd:byte</c> is empty.
    /// </summary>
    [TestMethod]
    public void IntegerEnumerationOutsideByteRangeIsUnsatisfiable()
    {
        OwlDataRange enumeration = OneOf(Lit("6542145", Vocabulary.Xsd.Integer));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.ByteValue), enumeration], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// An enumeration whose only literal is ill-typed — its value lies outside its
    /// own datatype's value space (<c>256</c> is not an <c>xsd:unsignedByte</c>) —
    /// denotes no value, so the enumeration is empty.
    /// </summary>
    [TestMethod]
    public void EnumerationOfAnOutOfRangeDerivedIntegerLiteralIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(OneOf(Lit("256", Vocabulary.Xsd.UnsignedByte)), DatatypeRegistry.Empty));
    }

    /// <summary>A negative literal typed <c>xsd:nonNegativeInteger</c> is ill-typed and denotes no value, so its enumeration is empty.</summary>
    [TestMethod]
    public void EnumerationOfASignDomainViolatingLiteralIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(OneOf(Lit("-1", Vocabulary.Xsd.NonNegativeInteger)), DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Inconsistent Data Complement with the Restrictions</c>:
    /// <c>{3, 4} ∩ {2, 3} ∩ ¬{3}</c> removes the only shared value <c>3</c>.
    /// </summary>
    [TestMethod]
    public void EnumerationIntersectionMinusItsOnlySharedValueIsUnsatisfiable()
    {
        OwlDataRange first = OneOf(Lit("3", Vocabulary.Xsd.Integer), Lit("4", Vocabulary.Xsd.Integer));
        OwlDataRange second = OneOf(Lit("2", Vocabulary.Xsd.Integer), Lit("3", Vocabulary.Xsd.Integer));
        OwlDataRange notThree = Complement(OneOf(Lit("3", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([first, second, notThree], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Different types in Datatype Restrictions and Complement</c>: the
    /// same emptiness as the previous case, with the shared value carried across
    /// <c>xsd:short</c>/<c>xsd:int</c>/<c>xsd:integer</c> spellings.
    /// </summary>
    [TestMethod]
    public void EnumerationIntersectionAcrossTypesMinusSharedValueIsUnsatisfiable()
    {
        OwlDataRange first = OneOf(Lit("3", Vocabulary.Xsd.Integer), Lit("4", Vocabulary.Xsd.Int));
        OwlDataRange second = OneOf(Lit("2", Vocabulary.Xsd.Short), Lit("3", Vocabulary.Xsd.Integer));
        OwlDataRange notThree = Complement(OneOf(Lit("3", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([first, second, notThree], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Datatype-DataComplementOf-001</c>: <c>-1</c> typed
    /// <c>xsd:negativeInteger</c> lies in the complement of <c>xsd:positiveInteger</c>.
    /// </summary>
    [TestMethod]
    public void NegativeIntegerLiesInComplementOfPositiveInteger()
    {
        OwlDataRange enumeration = OneOf(Lit("-1", Vocabulary.Xsd.NegativeInteger));
        OwlDataRange notPositive = Complement(Datatype(Vocabulary.Xsd.PositiveInteger));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([enumeration, notPositive], DatatypeRegistry.Empty));
    }

    /// <summary>The bare complement of a datatype is a non-empty data range (it holds values of every other datatype).</summary>
    [TestMethod]
    public void ComplementOfADatatypeIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(Complement(Datatype(Vocabulary.Xsd.Integer)), DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Plus and Minus Zero Integer</c>: <c>"0"^^xsd:integer</c> and
    /// <c>"-0"^^xsd:integer</c> denote the same integer value, so their enumerations
    /// intersect.
    /// </summary>
    [TestMethod]
    public void PlusZeroAndMinusZeroIntegerEnumerationsAreSatisfiable()
    {
        OwlDataRange plusZero = OneOf(Lit("0", Vocabulary.Xsd.Integer));
        OwlDataRange minusZero = OneOf(Lit("-0", Vocabulary.Xsd.Integer));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([plusZero, minusZero], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>New-Feature-Rational-001</c>: <c>owl:rational</c> is a non-empty
    /// continuum, and a rational lexical resolves to its exact value within it.
    /// </summary>
    [TestMethod]
    public void RationalRangeIsSatisfiableAndContainsItsLexicalValue()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(Datatype(OwlVocabulary.Rational), DatatypeRegistry.Empty));

        OwlDataRange half = OneOf(Lit("1/2", OwlVocabulary.Rational));
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(OwlVocabulary.Rational), half], DatatypeRegistry.Empty));
    }

    /// <summary>A rational and the decimal denoting the same value are one value, so excluding the decimal empties the rational enumeration.</summary>
    [TestMethod]
    public void RationalEqualsDecimalSoComplementOfTheDecimalExcludesIt()
    {
        OwlDataRange half = OneOf(Lit("1/2", OwlVocabulary.Rational));
        OwlDataRange notPointFive = Complement(OneOf(Lit("0.5", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([half, notPointFive], DatatypeRegistry.Empty));
    }

    /// <summary>The exact-real line and the disjoint <c>xsd:double</c> space share no value, so requiring both is empty.</summary>
    [TestMethod]
    public void OwlRealIntersectDoubleIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(OwlVocabulary.Real), Datatype(Vocabulary.Xsd.Double)], DatatypeRegistry.Empty));
    }

    /// <summary>A datatype and its own complement are a direct contradiction.</summary>
    [TestMethod]
    public void DatatypeIntersectItsComplementIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Integer), Complement(Datatype(Vocabulary.Xsd.Integer))], DatatypeRegistry.Empty));
    }

    /// <summary>Two datatypes of disjoint families (numeric and string) share no value.</summary>
    [TestMethod]
    public void IntegerIntersectStringIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Integer), Datatype(Vocabulary.Xsd.String)], DatatypeRegistry.Empty));
    }

    /// <summary>An inclusive lower bound equal to an inclusive upper bound is the single-point interval, which is non-empty.</summary>
    [TestMethod]
    public void DecimalRestrictionToASinglePointIsSatisfiable()
    {
        OwlDataRange point = Restriction(Vocabulary.Xsd.Decimal,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1.5", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("1.5", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(point, DatatypeRegistry.Empty));
    }

    /// <summary>An exclusive bound pair around a single value leaves no value (the open interval is empty).</summary>
    [TestMethod]
    public void DecimalRestrictionWithCollapsingExclusiveBoundsIsUnsatisfiable()
    {
        OwlDataRange empty = Restriction(Vocabulary.Xsd.Decimal,
            (Vocabulary.XsdFacets.MinExclusive, Lit("1", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("1", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(empty, DatatypeRegistry.Empty));
    }

    /// <summary>An exclusive integer range that admits no integer between its bounds is empty.</summary>
    [TestMethod]
    public void IntegerRestrictionWithNoIntegerBetweenExclusiveBoundsIsUnsatisfiable()
    {
        OwlDataRange empty = Restriction(Vocabulary.Xsd.Integer,
            (Vocabulary.XsdFacets.MinExclusive, Lit("3", Vocabulary.Xsd.Integer)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("4", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(empty, DatatypeRegistry.Empty));
    }

    /// <summary>A non-negative integer with all integers excluded (the complement of <c>xsd:integer</c>) is empty.</summary>
    [TestMethod]
    public void NonNegativeIntegerMinusAllIntegersIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.NonNegativeInteger), Complement(Datatype(Vocabulary.Xsd.Integer))], DatatypeRegistry.Empty));
    }

    /// <summary>The integers outside the non-negative range (the negatives) survive removing <c>xsd:nonNegativeInteger</c> from <c>xsd:integer</c>.</summary>
    [TestMethod]
    public void IntegerMinusNonNegativeIntegerKeepsTheNegativesAndIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Integer), Complement(Datatype(Vocabulary.Xsd.NonNegativeInteger))], DatatypeRegistry.Empty));
    }

    /// <summary>A continuum decimal interval minus the integers it spans (the complement of <c>xsd:integer</c>) keeps its non-integer values.</summary>
    [TestMethod]
    public void DecimalIntervalMinusIntegersKeepsNonIntegerValues()
    {
        OwlDataRange between = Restriction(Vocabulary.Xsd.Decimal,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([between, Complement(Datatype(Vocabulary.Xsd.Integer))], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// The integer tower is contained in <c>xsd:decimal</c>, so excluding
    /// <c>xsd:decimal</c> from <c>xsd:integer</c> removes every value.
    /// </summary>
    [TestMethod]
    public void IntegerMinusDecimalIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Integer), Complement(Datatype(Vocabulary.Xsd.Decimal))], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// <c>owl:real</c> is broader than <c>xsd:decimal</c>, so excluding the decimals
    /// leaves the non-decimal reals: the checker must not falsely report emptiness
    /// (the irrational witness is unrepresentable, so it soundly abstains).
    /// </summary>
    [TestMethod]
    public void OwlRealMinusDecimalIsAbstainedNotUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(OwlVocabulary.Real), Complement(Datatype(Vocabulary.Xsd.Decimal))], DatatypeRegistry.Empty));
    }

    /// <summary>A bounded decimal interval minus the same bounded decimal interval is empty.</summary>
    [TestMethod]
    public void DecimalIntervalMinusItselfIsUnsatisfiable()
    {
        OwlDataRange interval = Restriction(Vocabulary.Xsd.Decimal,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2", Vocabulary.Xsd.Decimal)));
        OwlDataRange sameInterval = Restriction(Vocabulary.Xsd.Decimal,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([interval, Complement(sameInterval)], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// A bounded <c>owl:real</c> interval minus the same-bounded <c>xsd:decimal</c>
    /// interval keeps its non-decimal reals, so the checker abstains rather than
    /// falsely report emptiness.
    /// </summary>
    [TestMethod]
    public void OwlRealIntervalMinusDecimalIntervalIsAbstainedNotUnsatisfiable()
    {
        OwlDataRange realInterval = Restriction(OwlVocabulary.Real,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2", Vocabulary.Xsd.Decimal)));
        OwlDataRange decimalInterval = Restriction(Vocabulary.Xsd.Decimal,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1", Vocabulary.Xsd.Decimal)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideConjunction([realInterval, Complement(decimalInterval)], DatatypeRegistry.Empty));
    }

    /// <summary>A union is satisfiable when any branch is, even if another branch is an empty interval.</summary>
    [TestMethod]
    public void UnionOfAnEmptyIntervalAndANonEmptyRangeIsSatisfiable()
    {
        OwlDataRange emptyBranch = Restriction(Vocabulary.Xsd.Integer,
            (Vocabulary.XsdFacets.MinInclusive, Lit("4", Vocabulary.Xsd.Integer)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("3", Vocabulary.Xsd.Integer)));
        OwlDataRange union = new OwlDataUnionOf([emptyBranch, Datatype(Vocabulary.Xsd.Integer)]);

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(union, DatatypeRegistry.Empty));
    }

    /// <summary>An enumeration with no members is the empty value space.</summary>
    [TestMethod]
    public void EmptyEnumerationIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(new OwlDataOneOf([]), DatatypeRegistry.Empty));
    }

    /// <summary>The complement of <c>rdfs:Literal</c> excludes the whole data domain, leaving nothing.</summary>
    [TestMethod]
    public void ComplementOfRdfsLiteralIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(Complement(Datatype(RdfVocabulary.Rdfs.LiteralClass)), DatatypeRegistry.Empty));
    }

    /// <summary><c>rdfs:Literal</c> is the whole, non-empty data domain.</summary>
    [TestMethod]
    public void RdfsLiteralIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(Datatype(RdfVocabulary.Rdfs.LiteralClass), DatatypeRegistry.Empty));
    }

    /// <summary>The boolean value space survives excluding one of its two values.</summary>
    [TestMethod]
    public void BooleanMinusOneValueIsSatisfiable()
    {
        OwlDataRange notTrue = Complement(OneOf(Lit("true", Vocabulary.Xsd.Boolean)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Boolean), notTrue], DatatypeRegistry.Empty));
    }

    /// <summary>Excluding both boolean values empties the boolean space.</summary>
    [TestMethod]
    public void BooleanMinusBothValuesIsUnsatisfiable()
    {
        OwlDataRange notTrue = Complement(OneOf(Lit("true", Vocabulary.Xsd.Boolean)));
        OwlDataRange notFalse = Complement(OneOf(Lit("false", Vocabulary.Xsd.Boolean)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Boolean), notTrue, notFalse], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Datatype-Float-Discrete-001</c>: the open interval between
    /// <c>0.0</c> and the smallest positive subnormal spans two adjacent ranks of
    /// the <c>xsd:float</c> value space, so it holds no value at all — the
    /// discreteness the dense interval algebra cannot express.
    /// </summary>
    [TestMethod]
    public void FloatAdjacentOpenIntervalIsUnsatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("0.0", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("1.401298464324817e-45", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>The same open interval widened by one rank contains the smallest positive subnormal, so it is decisively non-empty.</summary>
    [TestMethod]
    public void FloatOpenIntervalWithInteriorValueIsSatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("0.0", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("2.802596928649634e-45", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>Adjacency holds on the negative side too: nothing lies strictly between the greatest negative subnormal and <c>-0.0</c>, which shares its rank with <c>+0.0</c>.</summary>
    [TestMethod]
    public void FloatNegativeAdjacentOpenIntervalIsUnsatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("-1.401298464324817e-45", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("-0.0", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>The greatest finite <c>xsd:float</c> and <c>+INF</c> are adjacent, so the open interval between them is empty.</summary>
    [TestMethod]
    public void FloatAboveMaxValueOpenIntervalIsUnsatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("3.4028234663852886e38", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("INF", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>Including the upper endpoint admits <c>+INF</c>, which is itself a value of the <c>xsd:float</c> space.</summary>
    [TestMethod]
    public void FloatMaxValueToInfinityInclusiveIsSatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("3.4028234663852886e38", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("INF", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary><c>NaN</c> has no place in the value-space order, so a bound carrying it is outside the decided shape and abstains.</summary>
    [TestMethod]
    public void FloatNanBoundKeepsAbstaining()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float, (Vocabulary.XsdFacets.MinInclusive, Lit("NaN", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>The <c>xsd:double</c> space carries the same adjacency: the open interval between <c>0.0</c> and the smallest positive subnormal double is empty.</summary>
    [TestMethod]
    public void DoubleAdjacentOpenIntervalIsUnsatisfiable()
    {
        OwlDataRange doubleRange = Restriction(Vocabulary.Xsd.Double,
            (Vocabulary.XsdFacets.MinExclusive, Lit("0.0", Vocabulary.Xsd.Double)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("4.9406564584124654e-324", Vocabulary.Xsd.Double)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(doubleRange, DatatypeRegistry.Empty));
    }

    /// <summary>A bound typed in a different value space than the base is not a bound on that base's order, so the conjunction abstains.</summary>
    [TestMethod]
    public void FloatCrossSpaceBoundKeepsAbstaining()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("0.0", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("1.401298464324817e-45", Vocabulary.Xsd.Double)));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>The two zeros are order-equal — one shared rank — so an open interval spanning them holds them and is non-empty.</summary>
    [TestMethod]
    public void FloatZeroSpanningOpenIntervalIsSatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("-1.401298464324817e-45", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("1.401298464324817e-45", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>An enumeration beside a faceted float base leaves the ordered-facet shape, so the rank algebra never runs and the conjunction abstains.</summary>
    [TestMethod]
    public void FloatEnumerationConjunctionKeepsAbstaining()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("0.0", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("1.401298464324817e-45", Vocabulary.Xsd.Float)));
        OwlDataRange enumeration = OneOf(Lit("1.0", Vocabulary.Xsd.Float));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideConjunction([floatRange, enumeration], DatatypeRegistry.Empty));
    }

    /// <summary>An exclusive bound anchored at <c>+INF</c> steps one rank past the space's edge, which the rank type absorbs: the demand for a float above every float is empty.</summary>
    [TestMethod]
    public void FloatIntervalAboveInfinityIsUnsatisfiable()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float, (Vocabulary.XsdFacets.MinExclusive, Lit("INF", Vocabulary.Xsd.Float)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>The double space's extreme rank is the widest the algebra forms, and the step past it stays in range: the demand for a double above every double is empty.</summary>
    [TestMethod]
    public void DoubleIntervalAboveInfinityIsUnsatisfiable()
    {
        OwlDataRange doubleRange = Restriction(Vocabulary.Xsd.Double, (Vocabulary.XsdFacets.MinExclusive, Lit("INF", Vocabulary.Xsd.Double)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideRange(doubleRange, DatatypeRegistry.Empty));
    }

    /// <summary>A facet that is not one of the four ordered bounds lies outside the decided shape, so a pattern on a float base abstains.</summary>
    [TestMethod]
    public void FloatPatternFacetKeepsAbstaining()
    {
        OwlDataRange floatRange = Restriction(Vocabulary.Xsd.Float,
            (Vocabulary.XsdFacets.MinExclusive, Lit("0.0", Vocabulary.Xsd.Float)),
            (Vocabulary.XsdFacets.Pattern, Lit("[0-9.]+", Vocabulary.Xsd.String)));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideRange(floatRange, DatatypeRegistry.Empty));
    }

    /// <summary>
    /// The <c>WebOnt-miscellaneous-202</c> discriminators at the comparator: two
    /// XML literals differing by attribute order, empty-element form, and in-tag
    /// whitespace canonicalize alike, so they denote one value.
    /// </summary>
    [TestMethod]
    public void XmlLiteralAttributeOrderVariantsCompareSame()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare(
            "<br /><img src=\"vn.png\" alt=\"Venn diagram\" longdesc=\"vn.html\" title=\"Venn\"></img>"u8,
            "<br ></br><img  src=\"vn.png\" title=\"Venn\" alt=\"Venn diagram\" longdesc=\"vn.html\" />"u8));
    }

    /// <summary>The <c>WebOnt-miscellaneous-203</c> discriminator: text-node whitespace is significant, so a leading newline denotes a different value.</summary>
    [TestMethod]
    public void XmlLiteralLeadingWhitespaceComparesDistinct()
    {
        Assert.AreEqual(DatatypeValueIdentity.Distinct, XmlLiteralValues.Compare(
            "\n<br /><b>text</b>"u8,
            "<br /><b>text</b>"u8));
    }

    /// <summary>The <c>WebOnt-miscellaneous-204</c> discriminator: differing character data denotes differing values.</summary>
    [TestMethod]
    public void XmlLiteralDifferentTextComparesDistinct()
    {
        Assert.AreEqual(DatatypeValueIdentity.Distinct, XmlLiteralValues.Compare(
            "<span xml:lang='en'><b>Good!</b></span>"u8,
            "<span xml:lang='en'><b>Bad!</b></span>"u8));
    }

    /// <summary>A numeric character reference resolves to the character it names, so the two forms denote one value.</summary>
    [TestMethod]
    public void XmlLiteralCharacterReferenceComparesSame()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare("<b>&#65;</b>"u8, "<b>A</b>"u8));
    }

    /// <summary>A CDATA section is content, not markup: it canonicalizes to the same escaped text its escaped spelling does.</summary>
    [TestMethod]
    public void XmlLiteralCdataSectionComparesSame()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare("<b><![CDATA[a<b&c]]></b>"u8, "<b>a&lt;b&amp;c</b>"u8));
    }

    /// <summary>A namespace declaration nothing utilizes never reaches the exclusive canonical form, so it cannot distinguish two values.</summary>
    [TestMethod]
    public void XmlLiteralUnusedNamespaceDeclarationComparesSame()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare(
            "<b xmlns:unused=\"http://example.org/unused\">text</b>"u8,
            "<b>text</b>"u8));
    }

    /// <summary>
    /// The byte scanner surfaces no comment events, so a comment-blind comparison
    /// could report a false sameness under the with-comments mapping: any
    /// comment-bearing input abstains instead.
    /// </summary>
    [TestMethod]
    public void XmlLiteralCommentBearingComparesIndeterminate()
    {
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, XmlLiteralValues.Compare("<b><!-- note -->text</b>"u8, "<b>text</b>"u8));
    }

    /// <summary>Content that is not well-balanced represents no value, so distinctness is never claimed from it.</summary>
    [TestMethod]
    public void XmlLiteralMalformedComparesIndeterminate()
    {
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, XmlLiteralValues.Compare("<b>text</i>"u8, "<b>text</b>"u8));
    }

    /// <summary>Two singleton enumerations over XML literals that denote distinct values share no value, so their conjunction is empty.</summary>
    [TestMethod]
    public void XmlLiteralDistinctPointsConjunctionIsUnsatisfiable()
    {
        OwlDataRange first = OneOf(Lit("<b>Good!</b>", Vocabulary.Rdf.XmlLiteral));
        OwlDataRange second = OneOf(Lit("<b>Bad!</b>", Vocabulary.Rdf.XmlLiteral));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([first, second], DatatypeRegistry.Empty));
    }

    /// <summary>The named <c>rdf:XMLLiteral</c> range itself is not sized or bounded by the checker, so a bare conjunction over it abstains.</summary>
    [TestMethod]
    public void XmlLiteralBareRangeConjunctionKeepsAbstaining()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideRange(Datatype(Vocabulary.Rdf.XmlLiteral), DatatypeRegistry.Empty));
    }

    /// <summary>
    /// A well-formed XML literal is a member of its own datatype's value space, so
    /// a singleton enumeration over it exhibits an admitted candidate and the
    /// conjunction is decisively non-empty — the route a consistent functional
    /// pool of two equal XML literals rides.
    /// </summary>
    [TestMethod]
    public void XmlLiteralSingletonEnumerationIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(OneOf(Lit("<b>Good!</b>", Vocabulary.Rdf.XmlLiteral)), DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Attributes sort by resolved namespace IRI, not by prefix or local name: two
    /// literals whose prefixed attributes appear in opposite document order, under
    /// prefixes whose lexical order contradicts their IRIs' order, denote one value.
    /// </summary>
    [TestMethod]
    public void XmlLiteralPrefixedAttributeOrderComparesSame()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare(
            "<e xmlns:z=\"http://example.org/aaa\" xmlns:a=\"http://example.org/zzz\" a:v=\"1\" z:v=\"2\"/>"u8,
            "<e xmlns:z=\"http://example.org/aaa\" xmlns:a=\"http://example.org/zzz\" z:v=\"2\" a:v=\"1\"/>"u8));
    }

    /// <summary>An attribute value's literal whitespace normalizes to single spaces (XML 1.0 section 3.3.3), so an embedded newline and a space denote one value.</summary>
    [TestMethod]
    public void XmlLiteralAttributeValueWhitespaceComparesSame()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare("<b t=\"a\nb\">x</b>"u8, "<b t=\"a b\">x</b>"u8));
    }

    /// <summary>The XML value space is a proper part of the literal domain, so removing it alone still leaves a witness.</summary>
    [TestMethod]
    public void XmlLiteralNegatedAtomAloneDecidesSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(Complement(Datatype(Vocabulary.Rdf.XmlLiteral)), DatatypeRegistry.Empty));
    }

    /// <summary>The XML value space is disjoint from the temporal line, so a negated XML literal removes nothing and the temporal disjunct decides on its bounds alone.</summary>
    [TestMethod]
    public void XmlLiteralNegatedInTemporalDisjunctDecidesOnBounds()
    {
        OwlDataRange window = Restriction(Vocabulary.Xsd.DateTime,
            (Vocabulary.XsdFacets.MinInclusive, Lit("2008-01-01T00:00:00Z", Vocabulary.Xsd.DateTime)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2009-01-01T00:00:00Z", Vocabulary.Xsd.DateTime)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([window, Complement(Datatype(Vocabulary.Rdf.XmlLiteral))], DatatypeRegistry.Empty));
    }

    /// <summary>The XML value space is disjoint from the exact-real line too, so a negated XML literal removes nothing from a numeric interval and the disjunct decides on its numeric content alone.</summary>
    [TestMethod]
    public void XmlLiteralNegatedInExactRealDisjunctDecidesOnNumericContent()
    {
        OwlDataRange window = Restriction(Vocabulary.Xsd.Integer,
            (Vocabulary.XsdFacets.MinInclusive, Lit("5", Vocabulary.Xsd.Integer)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("10", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([window, Complement(Datatype(Vocabulary.Rdf.XmlLiteral))], DatatypeRegistry.Empty));
    }

    /// <summary>One value cannot inhabit two disjoint value spaces, so an XML literal conjoined with a datatype of another modelled family is empty.</summary>
    [TestMethod]
    public void XmlLiteralWithDisjointFamilyPositiveIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Rdf.XmlLiteral), Datatype(Vocabulary.Xsd.String)], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Consistent-dateTime</c>: a fully-timezoned <c>xsd:dateTime</c>
    /// lower bound and an equal upper bound share their endpoint, so the interval
    /// is the single instant — non-empty. Fully-timezoned values are totally ordered.
    /// </summary>
    [TestMethod]
    public void DateTimeIntervalSharingItsEndpointIsSatisfiable()
    {
        OwlDataRange lowerBound = Restriction(Vocabulary.Xsd.DateTime, (Vocabulary.XsdFacets.MinInclusive, Lit("2008-10-08T20:44:11.656+01:00", Vocabulary.Xsd.DateTime)));
        OwlDataRange upperBound = Restriction(Vocabulary.Xsd.DateTime, (Vocabulary.XsdFacets.MaxInclusive, Lit("2008-10-08T20:44:11.656+01:00", Vocabulary.Xsd.DateTime)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([lowerBound, upperBound], DatatypeRegistry.Empty));
    }

    /// <summary>An <c>xsd:dateTime</c> lower bound above its upper bound is an empty interval.</summary>
    [TestMethod]
    public void DateTimeIntervalWithCrossedBoundsIsUnsatisfiable()
    {
        OwlDataRange lowerBound = Restriction(Vocabulary.Xsd.DateTime, (Vocabulary.XsdFacets.MinInclusive, Lit("2009-01-01T00:00:00Z", Vocabulary.Xsd.DateTime)));
        OwlDataRange upperBound = Restriction(Vocabulary.Xsd.DateTime, (Vocabulary.XsdFacets.MaxInclusive, Lit("2008-01-01T00:00:00Z", Vocabulary.Xsd.DateTime)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([lowerBound, upperBound], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>Contradicting-dateTime-restrictions</c>: the enumerated value
    /// <c>2007-…</c> lies below the restriction's lower bound <c>2008-07-…</c>, so
    /// the conjunction is empty.
    /// </summary>
    [TestMethod]
    public void DateTimeValueBelowARestrictionLowerBoundIsUnsatisfiable()
    {
        OwlDataRange value = OneOf(Lit("2007-10-08T20:44:11.656+01:00", Vocabulary.Xsd.DateTime));
        OwlDataRange window = Restriction(Vocabulary.Xsd.DateTime,
            (Vocabulary.XsdFacets.MinInclusive, Lit("2008-07-08T20:44:11.656+01:00", Vocabulary.Xsd.DateTime)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("2008-10-08T20:44:11.656+01:00", Vocabulary.Xsd.DateTime)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([value, window], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// An untimezoned <c>xsd:dateTime</c> value compared against a timezoned bound
    /// within the ±14-hour window is order-indeterminate (XSD 1.1 Part 2 §3.2.7.4),
    /// so the checker abstains rather than assume a timezone.
    /// </summary>
    [TestMethod]
    public void DateTimeNaiveValueAgainstTimezonedBoundIsAbstained()
    {
        OwlDataRange value = OneOf(Lit("2008-10-08T20:44:11.656", Vocabulary.Xsd.DateTime));
        OwlDataRange lowerBound = Restriction(Vocabulary.Xsd.DateTime, (Vocabulary.XsdFacets.MinInclusive, Lit("2008-10-08T20:44:11.656+01:00", Vocabulary.Xsd.DateTime)));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideConjunction([value, lowerBound], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// <c>xsd:date</c> is a discrete day grid, not a dense line: a both-exclusive
    /// interval over two consecutive days holds no date, but deciding that
    /// precisely needs day-snapping under the timezone band, so the checker
    /// abstains rather than wrongly report the dense satisfiable verdict.
    /// </summary>
    [TestMethod]
    public void OpenDateIntervalOverConsecutiveDaysIsAbstained()
    {
        OwlDataRange range = Restriction(Vocabulary.Xsd.Date,
            (Vocabulary.XsdFacets.MinExclusive, Lit("2020-01-01", Vocabulary.Xsd.Date)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("2020-01-02", Vocabulary.Xsd.Date)));

        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideRange(range, DatatypeRegistry.Empty));
    }

    /// <summary>An <c>xsd:date</c> interval with an inclusive endpoint has that date as a witness, so it stays decisively satisfiable.</summary>
    [TestMethod]
    public void DateIntervalWithAnInclusiveEndpointIsSatisfiable()
    {
        OwlDataRange range = Restriction(Vocabulary.Xsd.Date,
            (Vocabulary.XsdFacets.MinInclusive, Lit("2020-01-01", Vocabulary.Xsd.Date)),
            (Vocabulary.XsdFacets.MaxExclusive, Lit("2020-01-02", Vocabulary.Xsd.Date)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(range, DatatypeRegistry.Empty));
    }

    /// <summary>
    /// Mirrors <c>New-Feature-Rational-001</c>: <c>owl:rational</c> is a dense,
    /// infinite value space, so at least two distinct values provably exist.
    /// </summary>
    [TestMethod]
    public void MinCardinalityTwoOverRationalIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([Datatype(OwlVocabulary.Rational)], 2, DatatypeRegistry.Empty));
    }

    /// <summary>A demand for two distinct values over a single-point integer interval cannot be met.</summary>
    [TestMethod]
    public void MinCardinalityTwoOverASinglePointIsUnsatisfiable()
    {
        OwlDataRange singlePoint = Restriction(Vocabulary.Xsd.Integer,
            (Vocabulary.XsdFacets.MinInclusive, Lit("5", Vocabulary.Xsd.Integer)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("5", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([singlePoint], 2, DatatypeRegistry.Empty));
    }

    /// <summary>A three-element enumeration supplies the two distinct values a min-cardinality of two demands.</summary>
    [TestMethod]
    public void MinCardinalityTwoOverAThreeValueEnumerationIsSatisfiable()
    {
        OwlDataRange enumeration = OneOf(Lit("1", Vocabulary.Xsd.Integer), Lit("2", Vocabulary.Xsd.Integer), Lit("3", Vocabulary.Xsd.Integer));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([enumeration], 2, DatatypeRegistry.Empty));
    }

    /// <summary>N1: the unconstrained data domain contains the countably infinite <c>xsd:string</c> value space, so a range-less counting demand of two is met — the counting floor itself.</summary>
    [TestMethod]
    public void RangeLessMinCardinalityTwoDecidesSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([Datatype(RdfVocabulary.Rdfs.LiteralClass)], 2, DatatypeRegistry.Empty), "The literal top admits two distinct values.");
    }

    /// <summary>N2: the floor is threshold-independent — an unbounded domain meets a counting demand of a thousand exactly as it meets one of two.</summary>
    [TestMethod]
    public void RangeLessMinCardinalityLargeThresholdDecidesSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([Datatype(RdfVocabulary.Rdfs.LiteralClass)], 1000, DatatypeRegistry.Empty), "The literal top admits a thousand distinct values just as it admits two.");
    }

    /// <summary>N3: a negated atom prices a removal the counting path does not size, so the literal top minus an enumerated point keeps the count undecided rather than riding the unconstrained-domain floor.</summary>
    [TestMethod]
    public void ComplementNegativeKeepsCountUnknown()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideMinCardinality(
            [Datatype(RdfVocabulary.Rdfs.LiteralClass), Complement(OneOf(Lit("alpha", Vocabulary.Xsd.String)))], 2, DatatypeRegistry.Empty), "A disjunct carrying a negated atom is not the unconstrained domain, so its count stays unsized.");
    }

    /// <summary>N4: a surviving positive atom keeps the family abstention — the <c>xsd:string</c> value space is not sized, so a counting demand of two over it stays undecided at the floor's edge.</summary>
    [TestMethod]
    public void BareTextDatatypeKeepsCountUnknown()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideMinCardinality([Datatype(Vocabulary.Xsd.String)], 2, DatatypeRegistry.Empty), "A text-family positive survives the literal-top drop, so the count is the unsized family's, not the domain's.");
    }

    /// <summary>A min-cardinality of one is satisfiability: an empty value space supplies fewer than one value.</summary>
    [TestMethod]
    public void MinCardinalityOneOverAnEmptySpaceIsUnsatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([Datatype(Vocabulary.Xsd.ByteValue), OneOf(Lit("6542145", Vocabulary.Xsd.Integer))], 1, DatatypeRegistry.Empty));
    }

    /// <summary>A string <c>pattern</c> facet on an <c>xsd:string</c> base is decided by the built-in automaton route: the language of <c>[a-z]+</c> is non-empty, so the restriction is satisfiable.</summary>
    [TestMethod]
    public void StringPatternRestrictionDecidedByAutomatonRoute()
    {
        OwlDataRange pattern = Restriction(Vocabulary.Xsd.String, (Vocabulary.XsdFacets.Pattern, Lit("[a-z]+", Vocabulary.Xsd.String)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideRange(pattern, DatatypeRegistry.Empty));
    }

    /// <summary>A datatype outside the modelled map is a sound abstention.</summary>
    [TestMethod]
    public void UnknownDatatypeIsAbstained()
    {
        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideRange(Datatype(Utf8Strings.From("http://example.org/myDatatype")), DatatypeRegistry.Empty));
    }

    /// <summary>An empty conjunction imposes no constraint and is the unconstrained, non-empty domain.</summary>
    [TestMethod]
    public void EmptyConjunctionIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([], DatatypeRegistry.Empty));
    }

    /// <summary>
    /// <c>rdf:XMLLiteral</c> is a built-in the family classifier decides, and
    /// built-ins are not registry-overridable, so a registration for it is
    /// rejected by the one acceptance rule that covers every built-in IRI.
    /// </summary>
    [TestMethod]
    public void XmlLiteralRegistrationIsRejected()
    {
        DatatypeRegistryBuilder builder = new();
        RegistrationOutcome outcome = builder.Add(new EnumeratedDatatype(Vocabulary.Rdf.XmlLiteral, [Lit("<b>Good!</b>", Vocabulary.Rdf.XmlLiteral)]));

        Assert.AreEqual(RegistrationOutcomeKind.RejectedBuiltInIri, outcome.Kind);
    }

    /// <summary>
    /// The value reduction a ground data-property-assertion refutation reaches on
    /// the integer-subtype facet shape: <c>xsd:nonNegativeInteger</c> and
    /// <c>xsd:nonPositiveInteger</c> intersect at the single point zero, and the
    /// refutation's excluded enumeration removes it, so the conjunction is empty.
    /// </summary>
    [TestMethod]
    public void IntegerSubtypeFacetIntersectionExcludingZeroIsUnsatisfiable()
    {
        OwlDataRange excluded = Complement(OneOf(Lit("0", Vocabulary.Xsd.Int)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [Datatype(Vocabulary.Xsd.NonNegativeInteger), Datatype(Vocabulary.Xsd.NonPositiveInteger), excluded], DatatypeRegistry.Empty));
    }

    /// <summary>The same integer-subtype intersection WITHOUT the exclusion holds the zero witness, so the emptiness above is the exclusion's doing and not a vacuous intersection.</summary>
    [TestMethod]
    public void IntegerSubtypeFacetIntersectionIsSatisfiable()
    {
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [Datatype(Vocabulary.Xsd.NonNegativeInteger), Datatype(Vocabulary.Xsd.NonPositiveInteger)], DatatypeRegistry.Empty));
    }

    /// <summary>The value reduction the enumeration shape reaches: two integer enumerations share only the value four, and the refutation's exclusion of four empties the conjunction — the intersection falls out of per-candidate screening, never a set operation.</summary>
    [TestMethod]
    public void EnumerationIntersectionExcludingSharedValueIsUnsatisfiable()
    {
        OwlDataRange first = OneOf(Lit("1", Vocabulary.Xsd.Integer), Lit("2", Vocabulary.Xsd.Integer), Lit("3", Vocabulary.Xsd.Integer), Lit("4", Vocabulary.Xsd.Integer));
        OwlDataRange second = OneOf(Lit("4", Vocabulary.Xsd.Integer), Lit("5", Vocabulary.Xsd.Integer), Lit("6", Vocabulary.Xsd.Integer));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [first, second, Complement(OneOf(Lit("4", Vocabulary.Xsd.Integer)))], DatatypeRegistry.Empty));
    }

    /// <summary>The same two enumerations WITHOUT the exclusion keep their shared four, so the emptiness above is the exclusion's doing.</summary>
    [TestMethod]
    public void EnumerationIntersectionIsSatisfiable()
    {
        OwlDataRange first = OneOf(Lit("1", Vocabulary.Xsd.Integer), Lit("2", Vocabulary.Xsd.Integer), Lit("3", Vocabulary.Xsd.Integer), Lit("4", Vocabulary.Xsd.Integer));
        OwlDataRange second = OneOf(Lit("4", Vocabulary.Xsd.Integer), Lit("5", Vocabulary.Xsd.Integer), Lit("6", Vocabulary.Xsd.Integer));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([first, second], DatatypeRegistry.Empty));
    }

    /// <summary>The value reduction the boolean qualified-cardinality shape reaches: excluding one of the two boolean values leaves a single value, which cannot meet a counting demand of two.</summary>
    [TestMethod]
    public void BooleanSpaceExcludingOneValueBelowTwoIsUnsatisfiable()
    {
        OwlDataRange excluded = Complement(OneOf(Lit("true", Vocabulary.Xsd.Boolean)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality(
            [Datatype(Vocabulary.Xsd.Boolean), excluded], 2, DatatypeRegistry.Empty));
    }

    /// <summary>The value reduction the faceted-integer qualified-cardinality shape reaches: a three-value integer footprint minus one excluded point holds two values, one short of a counting demand of three.</summary>
    [TestMethod]
    public void RestrictedIntegerFootprintExcludingOneValueBelowThreeIsUnsatisfiable()
    {
        OwlDataRange window = Restriction(Vocabulary.Xsd.Integer,
            (Vocabulary.XsdFacets.MinInclusive, Lit("1", Vocabulary.Xsd.Integer)),
            (Vocabulary.XsdFacets.MaxInclusive, Lit("3", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality(
            [window, Complement(OneOf(Lit("1", Vocabulary.Xsd.Integer)))], 3, DatatypeRegistry.Empty));
    }

    /// <summary>Identity across the integer subtypes is VALUE-level: the same zero excluded as <c>"-0"^^xsd:integer</c> rather than <c>"0"^^xsd:int</c> empties the intersection just the same, so neither the datatype IRI nor the lexical form carries the decision.</summary>
    [TestMethod]
    public void CrossTypedZeroExclusionIsUnsatisfiable()
    {
        OwlDataRange excluded = Complement(OneOf(Lit("-0", Vocabulary.Xsd.Integer)));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [Datatype(Vocabulary.Xsd.NonNegativeInteger), Datatype(Vocabulary.Xsd.NonPositiveInteger), excluded], DatatypeRegistry.Empty));
    }

    /// <summary>RN1: sixteen digits of a third is not a third, so the decimal and the rational denote distinct values — the reduction <c>New-Feature-Rational-003</c> rests on.</summary>
    [TestMethod]
    public void Rn1SixteenDigitDecimalIsDistinctFromOneThird()
    {
        Assert.AreEqual(DatatypeValueIdentity.Distinct, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("0.3333333333333333", Vocabulary.Xsd.Decimal), Lit("1/3", OwlVocabulary.Rational), DatatypeRegistry.Empty));
    }

    /// <summary>RN2: a non-terminating rational is the same value as itself — the exact fraction settles the self-pair the terminating-decimal conversion leaves unparsed.</summary>
    [TestMethod]
    public void Rn2OneThirdIsTheSameValueAsItself()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("1/3", OwlVocabulary.Rational), Lit("1/3", OwlVocabulary.Rational), DatatypeRegistry.Empty));
    }

    /// <summary>RN3: value identity is over the reduced fraction, so an unreduced rational and its reduced form are one value.</summary>
    [TestMethod]
    public void Rn3UnreducedRationalEqualsItsReducedForm()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("2/6", OwlVocabulary.Rational), Lit("1/3", OwlVocabulary.Rational), DatatypeRegistry.Empty));
    }

    /// <summary>RN4: the sign rides on the numerator, so a negative rational and the decimal denoting it are one value.</summary>
    [TestMethod]
    public void Rn4NegativeRationalEqualsItsDecimal()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("-1/2", OwlVocabulary.Rational), Lit("-0.5", Vocabulary.Xsd.Decimal), DatatypeRegistry.Empty));
    }

    /// <summary>
    /// RN5: the <c>New-Feature-Rational-002</c> seam. The two enumerated
    /// literals denote one value, so the enumeration holds one value and a
    /// counting demand of two cannot be met — the upper bound counts
    /// value-identity groups, not candidates.
    /// </summary>
    [TestMethod]
    public void Rn5MinCardinalityTwoOverOneValueInTwoLexicalsIsUnsatisfiable()
    {
        OwlDataRange enumeration = OneOf(Lit("0.5", Vocabulary.Xsd.Decimal), Lit("1/2", OwlVocabulary.Rational));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([enumeration], 2, DatatypeRegistry.Empty));
    }

    /// <summary>RN6: the <c>New-Feature-Rational-003</c> seam. The two enumerated literals denote distinct values, so the enumeration meets a counting demand of two.</summary>
    [TestMethod]
    public void Rn6MinCardinalityTwoOverTwoDistinctExactValuesIsSatisfiable()
    {
        OwlDataRange enumeration = OneOf(Lit("0.3333333333333333", Vocabulary.Xsd.Decimal), Lit("1/3", OwlVocabulary.Rational));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([enumeration], 2, DatatypeRegistry.Empty));
    }

    /// <summary>
    /// RN7: only a PROVEN identity merges. Two of the three candidates are one
    /// value and the third — an unparsed rational — is indeterminate against
    /// both, so the enumeration holds at most two values: a demand of three is
    /// refused, and a demand of two stays undecided rather than collapsing the
    /// indeterminate candidate into a group.
    /// </summary>
    [TestMethod]
    public void Rn7IndeterminateCandidateNeverMergesIntoAGroup()
    {
        OwlDataRange enumeration = OneOf(Lit("0.5", Vocabulary.Xsd.Decimal), Lit("1/2", OwlVocabulary.Rational), Lit("1/0", OwlVocabulary.Rational));

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideMinCardinality([enumeration], 3, DatatypeRegistry.Empty), "The two proven-same candidates share a group, so at most two values exist.");
        Assert.AreEqual(DatatypeSatisfiability.Unknown, DatatypeSatisfiabilityChecker.DecideMinCardinality([enumeration], 2, DatatypeRegistry.Empty), "The indeterminate candidate keeps a group of its own, so the upper bound is two and the demand is not refused.");
    }

    /// <summary>
    /// RN8: fraction membership is per value space, not the shared continuum
    /// the interval algebra approximates with. A third is a rational but no
    /// decimal (its expansion does not terminate) and no integer; a half is a
    /// decimal; an integer-form rational is an integer within the tower's
    /// bounds and outside them when the bounds exclude it.
    /// </summary>
    [TestMethod]
    public void Rn8FractionMembershipFollowsTheExactValueSpaces()
    {
        OwlDataRange oneThird = OneOf(Lit("1/3", OwlVocabulary.Rational));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(OwlVocabulary.Rational), oneThird], DatatypeRegistry.Empty), "A third is a rational.");
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Decimal), oneThird], DatatypeRegistry.Empty), "A third has no terminating decimal expansion, so it is no xsd:decimal.");
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([Datatype(Vocabulary.Xsd.Integer), oneThird], DatatypeRegistry.Empty), "A third is not whole, so it is no xsd:integer.");
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [Datatype(Vocabulary.Xsd.Decimal), OneOf(Lit("1/2", OwlVocabulary.Rational))], DatatypeRegistry.Empty), "A half terminates, so it is an xsd:decimal.");
        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [Datatype(Vocabulary.Xsd.Integer), OneOf(Lit("7/1", OwlVocabulary.Rational))], DatatypeRegistry.Empty), "An integer-form rational is an integer.");
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction(
            [Datatype(Vocabulary.Xsd.ByteValue), OneOf(Lit("300/1", OwlVocabulary.Rational))], DatatypeRegistry.Empty), "The integer tower's bounds still apply to a whole fraction.");
    }

    /// <summary>
    /// RN9: the admission freeze. A zero denominator, a negative denominator,
    /// and a malformed lexical yield no fraction, so identity over them stays
    /// undetermined — including against the decimal a negative-denominator form
    /// would denote if it were read as a fraction.
    /// </summary>
    [TestMethod]
    public void Rn9UnparsedRationalLexicalsStayUndetermined()
    {
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("1/0", OwlVocabulary.Rational), Lit("1/0", OwlVocabulary.Rational), DatatypeRegistry.Empty), "A zero denominator names no value.");
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("5/-2", OwlVocabulary.Rational), Lit("5/-2", OwlVocabulary.Rational), DatatypeRegistry.Empty), "A negative denominator is outside the admitted forms.");
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("5/-2", OwlVocabulary.Rational), Lit("-2.5", Vocabulary.Xsd.Decimal), DatatypeRegistry.Empty), "An unadmitted form denotes nothing to compare with.");
        Assert.AreEqual(DatatypeValueIdentity.Indeterminate, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("1/x", OwlVocabulary.Rational), Lit("1/x", OwlVocabulary.Rational), DatatypeRegistry.Empty), "A malformed lexical names no value.");
    }

    /// <summary>RN10: cross-multiplication carries arbitrary precision, so a thirty-two-digit fraction reduces onto a third exactly while the same numerator over the next power of ten stays distinct from it.</summary>
    [TestMethod]
    public void Rn10LargeDenominatorFractionsCompareExactly()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("33333333333333333333333333333333/99999999999999999999999999999999", OwlVocabulary.Rational), Lit("1/3", OwlVocabulary.Rational), DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeValueIdentity.Distinct, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("33333333333333333333333333333333/100000000000000000000000000000000", OwlVocabulary.Rational), Lit("1/3", OwlVocabulary.Rational), DatatypeRegistry.Empty));
    }

    /// <summary>RN11: the decimal precision corner. A scale-twenty-eight decimal converts through its own bits without rounding, so it is the same value as the rational one over ten to the twenty-eighth.</summary>
    [TestMethod]
    public void Rn11ScaleTwentyEightDecimalEqualsItsFraction()
    {
        Assert.AreEqual(DatatypeValueIdentity.Same, DatatypeSatisfiabilityChecker.CompareValues(
            Lit("0.0000000000000000000000000001", Vocabulary.Xsd.Decimal), Lit("1/10000000000000000000000000000", OwlVocabulary.Rational), DatatypeRegistry.Empty));
    }

    /// <summary>The facet row: an ordered bound over the exact-real line is compared as a fraction too, so a third clears an inclusive minimum of three tenths and violates the same value as an inclusive maximum.</summary>
    [TestMethod]
    public void RnFacetBoundsOverExactFractionsDecide()
    {
        OwlDataRange oneThird = OneOf(Lit("1/3", OwlVocabulary.Rational));
        OwlDataRange atLeastThreeTenths = Restriction(OwlVocabulary.Rational, (Vocabulary.XsdFacets.MinInclusive, Lit("0.3", Vocabulary.Xsd.Decimal)));
        OwlDataRange atMostThreeTenths = Restriction(OwlVocabulary.Rational, (Vocabulary.XsdFacets.MaxInclusive, Lit("0.3", Vocabulary.Xsd.Decimal)));

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([atLeastThreeTenths, oneThird], DatatypeRegistry.Empty));
        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, DatatypeSatisfiabilityChecker.DecideConjunction([atMostThreeTenths, oneThird], DatatypeRegistry.Empty));
    }

    /// <summary>Builds a named-datatype data range.</summary>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeReference Datatype(Utf8String datatypeIri)
    {
        return new OwlDatatypeReference(new NamedNode(datatypeIri));
    }

    /// <summary>Builds a typed literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal Lit(string lexical, Utf8String datatypeIri)
    {
        return new Literal(Utf8Strings.From(lexical), new NamedNode(datatypeIri));
    }

    /// <summary>Builds an enumeration data range over the given literals.</summary>
    /// <param name="literals">The enumerated literals.</param>
    /// <returns>The data range.</returns>
    private static OwlDataOneOf OneOf(params Literal[] literals)
    {
        return new OwlDataOneOf(literals);
    }

    /// <summary>Builds a datatype restriction data range with the given facet bounds.</summary>
    /// <param name="datatypeIri">The base datatype IRI.</param>
    /// <param name="facets">The facet–value pairs.</param>
    /// <returns>The data range.</returns>
    private static OwlDatatypeRestriction Restriction(Utf8String datatypeIri, params (Utf8String Facet, Literal Value)[] facets)
    {
        List<OwlFacetRestriction> restrictions = [];
        foreach((Utf8String facet, Literal value) in facets)
        {
            restrictions.Add(new OwlFacetRestriction(new NamedNode(facet), value));
        }

        return new OwlDatatypeRestriction(new NamedNode(datatypeIri), restrictions);
    }

    /// <summary>Builds the complement of a data range.</summary>
    /// <param name="range">The complemented range.</param>
    /// <returns>The data range.</returns>
    private static OwlDataComplementOf Complement(OwlDataRange range)
    {
        return new OwlDataComplementOf(range);
    }
}
