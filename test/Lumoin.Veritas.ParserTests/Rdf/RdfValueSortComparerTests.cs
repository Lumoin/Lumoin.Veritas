using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Comparator-level pins for temporal conformance, keyed to the certified
/// ground-truth table: the implicit-timezone-totalized comparison entry point,
/// the temporal-governed filter dispatch, the wide proleptic parse axis, and the <see cref="RdfValueComparer.CompareForSort"/>
/// total order with its class-rank partition, including both transitivity triples and the instant-equal tiebreak.
/// </summary>
[TestClass]
internal sealed class RdfValueSortComparerTests
{
    /// <summary>The implicit timezone every certified cell assumes: UTC.</summary>
    private static TimeSpan Utc => TimeSpan.Zero;

    /// <summary>F1/F2: a timezone-naive operand normalizes with the implicit UTC timezone and totalizes what the legacy XSD partial order leaves indeterminate.</summary>
    [TestMethod]
    public void NormalizedEntryTotalizesTheNaiveAwareCell()
    {
        Literal naive = DateTime("2020-01-01T00:30:00");
        Literal aware = DateTime("2020-01-01T01:00:00+00:00");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(naive, aware, Utc));
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(aware, naive, Utc));

        //The legacy two-argument entry keeps the XSD ±14h indeterminate window for SHACL and OWL consumers.
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(naive, aware));
    }

    /// <summary>F3: an aware/aware pair is spec-determinate on both entry points.</summary>
    [TestMethod]
    public void AwareAwarePairIsDeterminateOnBothEntryPoints()
    {
        Literal utcOne = DateTime("2020-01-01T01:00:00+00:00");
        Literal minusFive = DateTime("2020-01-01T00:00:00-05:00");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(utcOne, minusFive, Utc));
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(utcOne, minusFive));
    }

    /// <summary>F4: the naive operand's normalized instant lies after the aware one, so the less-than cell is Greater.</summary>
    [TestMethod]
    public void NaiveOperandAfterAwareOperandComparesGreater()
    {
        Assert.AreEqual(
            ComparisonResult.Greater,
            RdfValueComparer.Compare(DateTime("2020-01-01T06:00:00"), DateTime("2020-01-01T00:00:00-05:00"), Utc));
    }

    /// <summary>F5/F13: two lexical forms of one instant — a naive/aware pair under UTC and a trailing-zero fraction pair — are Equal on the totalized axis.</summary>
    [TestMethod]
    public void EqualInstantsCompareEqualOnTheTotalizedAxis()
    {
        Assert.AreEqual(
            ComparisonResult.Equal,
            RdfValueComparer.Compare(DateTime("2020-01-01T05:00:00"), DateTime("2020-01-01T00:00:00-05:00"), Utc));
        Assert.AreEqual(
            ComparisonResult.Equal,
            RdfValueComparer.Compare(DateTime("2020-01-01T00:00:00.5"), DateTime("2020-01-01T00:00:00.50"), Utc));
    }

    /// <summary>F6/F7: date ordering — tz-independent within same-timezone-presence pairs, and the naive date's starting instant under UTC precedes the -05:00 date's.</summary>
    [TestMethod]
    public void DateFamilyOrdersByStartingInstant()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Date("2020-03-01"), Date("2020-03-02"), Utc));
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Date("2020-03-01"), Date("2020-03-01-05:00"), Utc));
    }

    /// <summary>F8: time ordering on the shared reference day — naive 13:00 under UTC is after 12:00+00:00.</summary>
    [TestMethod]
    public void TimeFamilyOrdersOnTheReferenceDay()
    {
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Time("13:00:00"), Time("12:00:00+00:00"), Utc));
    }

    /// <summary>F9: a cross-family pair (dateTime vs date) is temporal-GOVERNED — the dispatch claims it — but incomparable, which the filter layer surfaces as a type error; a temporal/non-temporal pair is not claimed at all.</summary>
    [TestMethod]
    public void CrossFamilyPairsAreGovernedButIncomparable()
    {
        Assert.IsTrue(RdfValueComparer.TryCompareTemporal(
            DateTime("2020-01-01T00:00:00"), Date("2020-01-02"), Utc, out ComparisonResult crossFamily));
        Assert.AreEqual(ComparisonResult.Incomparable, crossFamily);

        Assert.IsFalse(RdfValueComparer.TryCompareTemporal(DateTime("2020-01-01T00:00:00"), Int("5"), Utc, out _));
    }

    /// <summary>F10/F11: a timezone-less dateTimeStamp is ill-typed (Incomparable), while a well-formed dateTimeStamp compares against a plain dateTime as one family.</summary>
    [TestMethod]
    public void DateTimeStampEnforcesTimezoneAndSharesTheDateTimeFamily()
    {
        Assert.AreEqual(
            ComparisonResult.Incomparable,
            RdfValueComparer.Compare(DateTimeStamp("2020-01-01T00:00:00"), DateTimeStamp("2020-01-01T01:00:00Z"), Utc));
        Assert.AreEqual(
            ComparisonResult.Less,
            RdfValueComparer.Compare(DateTimeStamp("2020-01-01T00:00:00Z"), DateTime("2020-01-01T01:00:00+00:00"), Utc));
    }

    /// <summary>F14: an unparseable lexical form under the declared datatype is Incomparable on both entry points.</summary>
    [TestMethod]
    public void UnparseableLexicalFormIsIncomparable()
    {
        Literal bad = DateTime("2020-13-45T99:99:99");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, DateTime("2020-01-01T00:00:00Z"), Utc));
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, DateTime("2020-01-01T00:00:00Z")));
    }

    /// <summary>F15: the proleptic axis — XSD 1.1 year 0000 is valid, year -0001 precedes it, and the totalized comparison decides what the legacy ±14h window leaves indeterminate; -0000 stays outside the lexical space.</summary>
    [TestMethod]
    public void ProlepticAxisOrdersNegativeAndZeroYears()
    {
        Literal beforeYearZero = DateTime("-0001-12-31T23:00:00");
        Literal yearZero = DateTime("0000-01-01T01:00:00Z");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(beforeYearZero, yearZero, Utc));
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(beforeYearZero, yearZero));

        Assert.IsTrue(DateTimeValue.TryParseDateTime("0000-01-01T00:00:00Z"u8, requireTimezone: false, out _));
        Assert.IsFalse(DateTimeValue.TryParseDateTime("-0000-01-01T00:00:00Z"u8, requireTimezone: false, out _));
    }

    /// <summary>The XSD 1.1 24:00:00 end-of-day form denotes the following day's 00:00:00 for dateTime and the same day's 00:00:00 for time.</summary>
    [TestMethod]
    public void EndOfDayFormNormalizes()
    {
        Assert.AreEqual(
            ComparisonResult.Equal,
            RdfValueComparer.Compare(DateTime("2020-01-01T24:00:00"), DateTime("2020-01-02T00:00:00"), Utc));
        Assert.AreEqual(
            ComparisonResult.Equal,
            RdfValueComparer.Compare(Time("24:00:00"), Time("00:00:00"), Utc));
        Assert.IsFalse(DateTimeValue.TryParseDateTime("2020-01-01T24:00:01"u8, requireTimezone: false, out _));
    }

    /// <summary>Years beyond four digits parse and order by value, and fractional seconds keep nanosecond (nine-digit) precision with further digits truncated.</summary>
    [TestMethod]
    public void WideYearsAndFractionPrecisionBounds()
    {
        Assert.AreEqual(
            ComparisonResult.Greater,
            RdfValueComparer.Compare(DateTime("12020-01-01T00:00:00Z"), DateTime("2020-01-01T00:00:00Z"), Utc));

        //Nine fractional digits are significant; the tenth digit is beyond the axis resolution.
        Assert.AreEqual(
            ComparisonResult.Less,
            RdfValueComparer.Compare(DateTime("2020-01-01T00:00:00.123456788Z"), DateTime("2020-01-01T00:00:00.123456789Z"), Utc));
        Assert.AreEqual(
            ComparisonResult.Equal,
            RdfValueComparer.Compare(DateTime("2020-01-01T00:00:00.1234567891Z"), DateTime("2020-01-01T00:00:00.1234567892Z"), Utc));
    }

    /// <summary>The timezone field honours the XSD ±14:00 bound: 14:00 exactly is valid, anything beyond is outside the lexical space.</summary>
    [TestMethod]
    public void TimezoneOffsetBoundIsFourteenHours()
    {
        Assert.IsTrue(DateTimeValue.TryParseDateTime("2020-01-01T00:00:00+14:00"u8, requireTimezone: false, out _));
        Assert.IsFalse(DateTimeValue.TryParseDateTime("2020-01-01T00:00:00+14:30"u8, requireTimezone: false, out _));
        Assert.IsFalse(DateTimeValue.TryParseDateTime("2020-01-01T00:00:00+15:00"u8, requireTimezone: false, out _));
    }

    /// <summary>O4/R1: the class-rank partition orders the mixed double/integer/duration set transitively — every permutation sorts to the same certified sequence, and the pairwise signs contain no cycle.</summary>
    [TestMethod]
    public void MixedDatatypeSortIsTransitive()
    {
        Literal integerFive = Int("5");
        Literal doubleHundred = Double("100.0");
        Literal durationYear = Duration("P1Y");

        //The §15.1-mandated numeric pair plus the two class-rank pairs, and their transitive closure.
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(integerFive, doubleHundred, Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(doubleHundred, durationYear, Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(integerFive, durationYear, Utc));

        AssertEverySortPermutationYields([integerFive, doubleHundred, durationYear], [integerFive, doubleHundred, durationYear]);
    }

    /// <summary>O1/C2: the temporal transitivity triple — naive, +00:00, and -05:00 forms sort to the certified sequence from every permutation with cycle-free pairwise signs.</summary>
    [TestMethod]
    public void TemporalSortIsTransitiveAcrossTimezoneForms()
    {
        Literal naive = DateTime("2020-01-01T00:30:00");
        Literal utcAware = DateTime("2020-01-01T01:00:00+00:00");
        Literal minusFive = DateTime("2020-01-01T00:00:00-05:00");

        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(naive, utcAware, Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(utcAware, minusFive, Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(naive, minusFive, Utc));

        AssertEverySortPermutationYields([naive, utcAware, minusFive], [naive, utcAware, minusFive]);
    }

    /// <summary>O2/R2: two lexical forms of one instant are field-unequal yet comparator-Equal, and the sort tiebreak (datatype IRI, then lexical bytes) orders them deterministically regardless of input order.</summary>
    [TestMethod]
    public void InstantEqualPairTiebreaksDeterministically()
    {
        Literal zulu = DateTime("2020-01-01T01:00:00Z");
        Literal plusOne = DateTime("2020-01-01T02:00:00+01:00");

        Assert.IsTrue(DateTimeValue.TryParseDateTime("2020-01-01T01:00:00Z"u8, requireTimezone: false, out DateTimeValue zuluValue));
        Assert.IsTrue(DateTimeValue.TryParseDateTime("2020-01-01T02:00:00+01:00"u8, requireTimezone: false, out DateTimeValue plusOneValue));
        Assert.IsFalse(zuluValue.Equals(plusOneValue));
        Assert.AreEqual(ComparisonResult.Equal, DateTimeValue.Compare(zuluValue, plusOneValue, Utc));

        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(zulu, plusOne, Utc));
        Assert.IsGreaterThan(0, RdfValueComparer.CompareForSort(plusOne, zulu, Utc));
    }

    /// <summary>NaN sorts before every other numeric value; two NaNs of different datatypes fall to the datatype tiebreak; an ill-formed numeric member sorts after every well-formed one.</summary>
    [TestMethod]
    public void NumericClassHandlesNaNAndIllFormedMembersDeterministically()
    {
        Literal notANumber = Double("NaN");
        Literal one = Double("1.0");
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(notANumber, one, Utc));
        Assert.IsGreaterThan(0, RdfValueComparer.CompareForSort(one, notANumber, Utc));

        //Two NaNs: the datatype IRI tiebreak (xsd:double < xsd:float) decides.
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Double("NaN"), Float("NaN"), Utc));

        Literal illFormed = Int("aldi");
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Int("5"), illFormed, Utc));
        Assert.IsGreaterThan(0, RdfValueComparer.CompareForSort(illFormed, Int("5"), Utc));
    }

    /// <summary>O5/DR-5: duration ordering is deferred — within the duration class the lexical fallback puts P1Y before P2M even though the XSD value order is the reverse, a recorded, total divergence.</summary>
    [TestMethod]
    public void DurationSortKeepsTheRecordedLexicalFallback()
    {
        Literal oneYear = Duration("P1Y");
        Literal twoMonths = Duration("P2M");

        //The XSD value axis (the legacy entry) knows P2M < P1Y; the deferred sort fallback is lexical.
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(oneYear, twoMonths));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(oneYear, twoMonths, Utc));
    }

    /// <summary>The static class ranks: each class is keyed by its least member datatype IRI, so boolean precedes the numerics (keyed xsd:byte), the numerics precede the temporal and duration classes, date precedes dateTime, and xsd:string follows the numerics.</summary>
    [TestMethod]
    public void ClassRanksFollowTheLeastMemberDatatypeIri()
    {
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Bool("true"), Int("5"), Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Int("5"), DateTime("2020-01-01T00:00:00Z"), Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Int("5"), Duration("P1Y"), Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Date("2020-01-01"), DateTime("2020-01-01T00:00:00Z"), Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Int("5"), Str("apple"), Utc));

        //A dateTimeStamp literal sits in the dateTime family class, not a class of its own.
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Date("2021-01-01"), DateTimeStamp("2020-01-01T00:00:00Z"), Utc));
    }

    /// <summary>The boolean class orders false before true, and the two lexical forms of one value tie-break by lexical bytes.</summary>
    [TestMethod]
    public void BooleanClassOrdersValuesThenLexicalForms()
    {
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Bool("false"), Bool("true"), Utc));
        Assert.IsLessThan(0, RdfValueComparer.CompareForSort(Bool("1"), Bool("true"), Utc));
        Assert.IsGreaterThan(0, RdfValueComparer.CompareForSort(Bool("true"), Bool("1"), Utc));
    }

    /// <summary>Sorts every permutation of <paramref name="items"/> with the sort comparator and asserts each yields <paramref name="expected"/>.</summary>
    /// <param name="items">The three literals to permute.</param>
    /// <param name="expected">The expected sorted sequence.</param>
    private static void AssertEverySortPermutationYields(Literal[] items, Literal[] expected)
    {
        int[][] permutations =
        [
            [0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0],
        ];
        foreach(int[] permutation in permutations)
        {
            Literal[] candidate = [items[permutation[0]], items[permutation[1]], items[permutation[2]]];
            Array.Sort(candidate, new SortAxisComparer(TimeSpan.Zero));
            Assert.AreSequenceEqual(expected, candidate, $"Permutation {permutation[0]}{permutation[1]}{permutation[2]} sorted out of the certified order.");
        }
    }

    /// <summary>An <see cref="IComparer{T}"/> over <see cref="RdfValueComparer.CompareForSort"/>, carrying the implicit timezone as explicit state.</summary>
    /// <param name="implicitTimezone">The implicit timezone the comparisons normalize naive operands with.</param>
    private sealed class SortAxisComparer(TimeSpan implicitTimezone) : IComparer<Literal>
    {
        /// <summary>The implicit timezone the comparisons normalize naive operands with.</summary>
        private TimeSpan ImplicitTimezone { get; } = implicitTimezone;

        /// <summary>Compares two literals on the sort axis.</summary>
        /// <param name="x">The left literal.</param>
        /// <param name="y">The right literal.</param>
        /// <returns>The sort order sign.</returns>
        public int Compare(Literal? x, Literal? y)
        {
            return RdfValueComparer.CompareForSort(x!, y!, ImplicitTimezone);
        }
    }

    /// <summary>Builds an <c>xsd:integer</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Int(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Integer);

    /// <summary>Builds an <c>xsd:double</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Double(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Double);

    /// <summary>Builds an <c>xsd:float</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Float(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Float);

    /// <summary>Builds an <c>xsd:string</c> literal.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The literal.</returns>
    private static Literal Str(string value)
        => MakeLiteral(value, Vocabulary.Xsd.String);

    /// <summary>Builds an <c>xsd:boolean</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Bool(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Boolean);

    /// <summary>Builds an <c>xsd:dateTime</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTime(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.DateTime);

    /// <summary>Builds an <c>xsd:dateTimeStamp</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal DateTimeStamp(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.DateTimeStamp);

    /// <summary>Builds an <c>xsd:date</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Date(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Date);

    /// <summary>Builds an <c>xsd:time</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Time(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Time);

    /// <summary>Builds an <c>xsd:duration</c> literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <returns>The literal.</returns>
    private static Literal Duration(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Duration);

    /// <summary>Builds a typed literal.</summary>
    /// <param name="lexical">The lexical form.</param>
    /// <param name="datatypeIri">The datatype IRI.</param>
    /// <returns>The literal.</returns>
    private static Literal MakeLiteral(string lexical, Utf8String datatypeIri)
        => new(Utf8Strings.From(lexical), new NamedNode(datatypeIri));
}
