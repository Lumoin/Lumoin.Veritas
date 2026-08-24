using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Rdf.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Rdf;

/// <summary>
/// Tests for <see cref="RdfValueComparer"/> covering every SPARQL
/// value space: numeric tower, string, boolean, date/time family,
/// and duration family.
/// </summary>
[TestClass]
internal sealed class RdfValueComparerTests
{
    public TestContext TestContext { get; set; } = null!;

    //Numeric value space — integer, decimal, float, double, plus the derived integer types.

    [TestMethod]
    public void IntegerLessThanInteger()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Int("3"), Int("5")));
    }

    [TestMethod]
    public void IntegerEqualToInteger()
    {
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Int("42"), Int("42")));
    }

    [TestMethod]
    public void IntegerGreaterThanInteger()
    {
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Int("100"), Int("99")));
    }

    [TestMethod]
    public void NegativeIntegerLessThanPositive()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Int("-5"), Int("0")));
    }

    [TestMethod]
    public void IntegerBeyondLongRangeStillCompares()
    {
        //2^100 is much larger than long.MaxValue. BigInteger handles
        //this; the comparator does too.
        Literal huge = Int("1267650600228229401496703205376");
        Literal small = Int("1");

        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(huge, small));
    }

    [TestMethod]
    public void IntegerPromotesToDecimalForCrossComparison()
    {
        //xsd:integer "5" vs xsd:decimal "5.0" promote to decimal,
        //compare equal.
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Int("5"), Decimal("5.0")));
    }

    [TestMethod]
    public void DecimalPreservesPrecisionAcrossLargeIntegerCompare()
    {
        //Integer 1000000000000000 vs decimal 1000000000000000.5 — the
        //decimal is strictly greater. Pure-double promotion would
        //lose this distinction; the per-spec lattice keeps the
        //precision via decimal.
        Assert.AreEqual(
            ComparisonResult.Less,
            RdfValueComparer.Compare(Int("1000000000000000"), Decimal("1000000000000000.5")));
    }

    [TestMethod]
    public void FloatPromotionLosesPrecisionCorrectly()
    {
        //float 1.1 and double 1.1 do NOT compare equal because float's
        //binary representation rounds differently. SPARQL says promote
        //to the larger; we promote to double; the float-as-double is
        //slightly different from the literal double 1.1. The point
        //of the test is they're not Equal — the direction depends on
        //which way the float rounded.
        ComparisonResult result = RdfValueComparer.Compare(Float("1.1"), Double("1.1"));

        Assert.AreNotEqual(ComparisonResult.Equal, result);
        Assert.AreNotEqual(ComparisonResult.Incomparable, result);
    }

    [TestMethod]
    public void NaNFloatAgainstAnythingIsIncomparable()
    {
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(Float("NaN"), Float("1.0")));
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(Float("1.0"), Float("NaN")));
    }

    [TestMethod]
    public void NaNDoubleAgainstAnythingIsIncomparable()
    {
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(Double("NaN"), Int("1")));
    }

    [TestMethod]
    public void NaNAgainstNaNIsAlsoIncomparable()
    {
        //IEEE 754: NaN != NaN. Per SPARQL the comparison is undefined.
        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(Double("NaN"), Double("NaN")));
    }

    [TestMethod]
    public void PositiveInfinityIsGreaterThanFinite()
    {
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Double("INF"), Double("1e308")));
    }

    [TestMethod]
    public void NegativeInfinityIsLessThanFinite()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Double("-INF"), Double("-1e308")));
    }

    [TestMethod]
    public void InfinitiesCompareToEachOther()
    {
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Double("INF"), Double("-INF")));
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Double("-INF"), Double("INF")));
    }

    [TestMethod]
    public void DerivedIntegerTypeComparesAsInteger()
    {
        //xsd:byte "5" vs xsd:int "5" — both are integers, equal.
        //Vocabulary.Xsd.ByteValue is the project's name for xsd:byte
        //(avoids System.Byte collision).
        Literal byteFive = MakeLiteral("5", Vocabulary.Xsd.ByteValue);
        Literal intFive = MakeLiteral("5", Vocabulary.Xsd.Int);

        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(byteFive, intFive));
    }

    [TestMethod]
    public void IllFormedIntegerIsIncomparable()
    {
        Literal bad = MakeLiteral("abc", Vocabulary.Xsd.Integer);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, Int("5")));
    }

    [TestMethod]
    public void IllFormedDecimalIsIncomparable()
    {
        Literal bad = MakeLiteral("not-a-number", Vocabulary.Xsd.Decimal);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, Decimal("1.5")));
    }

    [TestMethod]
    public void DecimalLexicalFormDoesNotAcceptScientificNotation()
    {
        //xsd:decimal forbids exponent. "1e2" is invalid lexical form
        //for decimal even though the parser would understand it.
        Literal bad = MakeLiteral("1e2", Vocabulary.Xsd.Decimal);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, Decimal("100")));
    }

    [TestMethod]
    public void IntegerVsStringIsIncomparable()
    {
        //Different value spaces under SPARQL.
        Literal num = Int("5");
        Literal str = MakeLiteral("5", Vocabulary.Xsd.String);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(num, str));
    }

    [TestMethod]
    public void IriAgainstIntegerIsIncomparable()
    {
        NamedNode iri = new(Utf8Strings.From("http://example.org/foo"));

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(iri, Int("5")));
    }

    //String value space — ordinal codepoint comparison; language-tag matching rules.

    [TestMethod]
    public void StringLessThanString()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Str("apple"), Str("banana")));
    }

    [TestMethod]
    public void StringEqualToString()
    {
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Str("hello"), Str("hello")));
    }

    [TestMethod]
    public void StringGreaterThanString()
    {
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Str("z"), Str("a")));
    }

    [TestMethod]
    public void EmptyStringComparesAsLeastNonNegativeString()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Str(""), Str("a")));
    }

    [TestMethod]
    public void StringComparisonIsOrdinalNotCultureSensitive()
    {
        //"a" (U+0061) vs "B" (U+0042). Ordinal: B < a (uppercase
        //precedes lowercase in code-point order). Culture-sensitive
        //in some locales would invert. We must give the ordinal
        //answer.
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Str("a"), Str("B")));
    }

    [TestMethod]
    public void LanguageTaggedSameTagComparesByValue()
    {
        Literal en1 = Lang("apple", "en");
        Literal en2 = Lang("banana", "en");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(en1, en2));
    }

    [TestMethod]
    public void LanguageTaggedDifferentTagIsIncomparable()
    {
        Literal en = Lang("hello", "en");
        Literal fr = Lang("bonjour", "fr");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(en, fr));
    }

    [TestMethod]
    public void LanguageTagMatchIsCaseInsensitive()
    {
        Literal lower = Lang("hello", "en");
        Literal upper = Lang("hellp", "EN");

        //Tags match case-insensitively, so the comparison proceeds
        //on the lexical form. "hello" < "hellp" by ordinal.
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(lower, upper));
    }

    [TestMethod]
    public void PlainStringVsLanguageTaggedIsIncomparable()
    {
        Literal plain = Str("hello");
        Literal tagged = Lang("hello", "en");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(plain, tagged));
    }

    //Boolean value space — false < true; lexical aliases "1"/"0".

    [TestMethod]
    public void FalseLessThanTrue()
    {
        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(Bool("false"), Bool("true")));
    }

    [TestMethod]
    public void TrueGreaterThanFalse()
    {
        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(Bool("true"), Bool("false")));
    }

    [TestMethod]
    public void TrueEqualToTrue()
    {
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Bool("true"), Bool("true")));
    }

    [TestMethod]
    public void FalseEqualToFalse()
    {
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Bool("false"), Bool("false")));
    }

    [TestMethod]
    public void OneIsEquivalentToTrue()
    {
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Bool("1"), Bool("true")));
    }

    [TestMethod]
    public void ZeroIsEquivalentToFalse()
    {
        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(Bool("0"), Bool("false")));
    }

    [TestMethod]
    public void IllFormedBooleanIsIncomparable()
    {
        Literal bad = Bool("yes");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, Bool("true")));
    }

    //Date/time value spaces — dateTime, date, time, plus the XSD §3.2.7.4 indeterminate cases.

    [TestMethod]
    public void TwoAwareDateTimesCompareByInstant()
    {
        Literal earlier = DateTime("2024-01-15T10:00:00Z");
        Literal later = DateTime("2024-01-15T11:00:00Z");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(earlier, later));
    }

    [TestMethod]
    public void EquivalentInstantsInDifferentZonesCompareEqual()
    {
        //10:00 UTC = 12:00 +02:00 = 05:00 -05:00.
        Literal utc = DateTime("2024-01-15T10:00:00Z");
        Literal plusTwo = DateTime("2024-01-15T12:00:00+02:00");

        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(utc, plusTwo));
    }

    [TestMethod]
    public void TwoNaiveDateTimesCompareByWallClock()
    {
        Literal earlier = DateTime("2024-01-15T10:00:00");
        Literal later = DateTime("2024-01-15T11:00:00");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(earlier, later));
    }

    [TestMethod]
    public void NaiveAndAwareWithLargeSeparationAreOrdered()
    {
        //Naive 2024-01-01T12:00:00 (could be ±14h around that wall
        //clock) vs aware 2024-06-01T00:00:00Z. Six months apart;
        //±14h envelope cannot bridge that gap.
        Literal naive = DateTime("2024-01-01T12:00:00");
        Literal aware = DateTime("2024-06-01T00:00:00Z");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(naive, aware));
    }

    [TestMethod]
    public void NaiveAndAwareWithinFourteenHoursIsIncomparable()
    {
        //Naive 2024-01-15T12:00:00. Aware 2024-01-15T18:00:00Z. The
        //naive wall-clock could correspond to any UTC instant in
        //[2024-01-14T22:00:00Z, 2024-01-16T02:00:00Z]. The aware
        //value 18:00Z falls inside that range.
        Literal naive = DateTime("2024-01-15T12:00:00");
        Literal aware = DateTime("2024-01-15T18:00:00Z");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(naive, aware));
    }

    [TestMethod]
    public void NaiveAndAwareIncomparabilityIsSymmetric()
    {
        //Same scenario as above, with operands swapped.
        Literal aware = DateTime("2024-01-15T18:00:00Z");
        Literal naive = DateTime("2024-01-15T12:00:00");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(aware, naive));
    }

    [TestMethod]
    public void AwareGreaterThanNaiveSymmetric()
    {
        //Wide-separation scenario, operands swapped — aware on left,
        //naive on right; expect Greater.
        Literal aware = DateTime("2024-06-01T00:00:00Z");
        Literal naive = DateTime("2024-01-01T12:00:00");

        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(aware, naive));
    }

    [TestMethod]
    public void DateLessThanDate()
    {
        Literal earlier = Date("2024-01-15");
        Literal later = Date("2024-01-16");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(earlier, later));
    }

    [TestMethod]
    public void TimeLessThanTime()
    {
        Literal earlier = Time("09:00:00");
        Literal later = Time("17:30:00");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(earlier, later));
    }

    [TestMethod]
    public void DateTimeVsDateIsIncomparable()
    {
        //Different value spaces under SPARQL — even when conceptually
        //the date matches.
        Literal dt = DateTime("2024-01-15T00:00:00Z");
        Literal d = Date("2024-01-15");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(dt, d));
    }

    [TestMethod]
    public void IllFormedDateTimeIsIncomparable()
    {
        Literal bad = DateTime("yesterday");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, DateTime("2024-01-15T00:00:00Z")));
    }

    [TestMethod]
    public void DateTimeStampRequiresTimezone()
    {
        //Same lexical form as a valid xsd:dateTime, but typed as
        //xsd:dateTimeStamp the parser rejects it because the
        //timezone is required.
        Literal naive = MakeLiteral("2024-01-15T10:00:00", Vocabulary.Xsd.DateTimeStamp);
        Literal aware = MakeLiteral("2024-01-15T10:00:00Z", Vocabulary.Xsd.DateTimeStamp);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(naive, aware));
    }

    [TestMethod]
    public void DateTimeStampValidValuesCompare()
    {
        Literal earlier = MakeLiteral("2024-01-15T10:00:00Z", Vocabulary.Xsd.DateTimeStamp);
        Literal later = MakeLiteral("2024-01-15T11:00:00Z", Vocabulary.Xsd.DateTimeStamp);

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(earlier, later));
    }

    //Duration value space — yearMonthDuration, dayTimeDuration, plus the partially-ordered general xsd:duration.

    [TestMethod]
    public void YearMonthDurationLessThan()
    {
        Literal twoMonths = YearMonth("P2M");
        Literal threeMonths = YearMonth("P3M");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(twoMonths, threeMonths));
    }

    [TestMethod]
    public void YearMonthDurationOneYearEqualsTwelveMonths()
    {
        Literal oneYear = YearMonth("P1Y");
        Literal twelveMonths = YearMonth("P12M");

        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(oneYear, twelveMonths));
    }

    [TestMethod]
    public void DayTimeDurationLessThan()
    {
        Literal oneHour = DayTime("PT1H");
        Literal oneDay = DayTime("P1D");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(oneHour, oneDay));
    }

    [TestMethod]
    public void DayTimeDurationOneDayEqualsTwentyFourHours()
    {
        Literal oneDay = DayTime("P1D");
        Literal twentyFourHours = DayTime("PT24H");

        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(oneDay, twentyFourHours));
    }

    [TestMethod]
    public void YearMonthDurationVsDayTimeDurationIsIncomparable()
    {
        //Cross-subtype is always incomparable. P1M vs P30D may or may
        //not be equal depending on which month, and SPARQL forbids
        //the comparison outright at the subtype level.
        Literal month = YearMonth("P1M");
        Literal thirtyDays = DayTime("P30D");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(month, thirtyDays));
    }

    [TestMethod]
    public void GeneralDurationOneMonthVsThirtyDaysIsIndeterminate()
    {
        //The famous edge case. P1M is between 28 and 31 days; P30D
        //is exactly 30. Adding P1M to a reference in February gives
        //28 or 29 days; adding to July gives 31. The four reference
        //instants of XSD §3.2.6.2 disagree on the ordering, so the
        //result is indeterminate.
        Literal month = General("P1M");
        Literal thirtyDays = General("P30D");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(month, thirtyDays));
    }

    [TestMethod]
    public void GeneralDurationClearlyOrderedComparesNormally()
    {
        //P1Y is at least 365 days, far more than P30D, regardless of
        //which year. All four reference instants agree.
        Literal year = General("P1Y");
        Literal thirtyDays = General("P30D");

        Assert.AreEqual(ComparisonResult.Greater, RdfValueComparer.Compare(year, thirtyDays));
    }

    [TestMethod]
    public void NegativeDurationIsLessThanZero()
    {
        Literal neg = DayTime("-PT1H");
        Literal zero = DayTime("PT0S");

        Assert.AreEqual(ComparisonResult.Less, RdfValueComparer.Compare(neg, zero));
    }

    [TestMethod]
    public void EqualDayTimeDurationsReturnEqual()
    {
        Literal a = DayTime("PT3600S");
        Literal b = DayTime("PT1H");

        Assert.AreEqual(ComparisonResult.Equal, RdfValueComparer.Compare(a, b));
    }

    [TestMethod]
    public void IllFormedDurationIsIncomparable()
    {
        Literal bad = General("not-a-duration");

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, General("P1D")));
    }

    [TestMethod]
    public void YearMonthDurationRejectsDayTimeComponents()
    {
        //"P1MT5S" has both year-month (1 month) and day-time (5 sec)
        //components. yearMonthDuration disallows the day-time part —
        //ill-formed for the subtype, comparator returns Incomparable.
        Literal bad = MakeLiteral("P1MT5S", Vocabulary.Xsd.YearMonthDuration);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, YearMonth("P1M")));
    }

    [TestMethod]
    public void DayTimeDurationRejectsYearMonthComponents()
    {
        Literal bad = MakeLiteral("P1Y", Vocabulary.Xsd.DayTimeDuration);

        Assert.AreEqual(ComparisonResult.Incomparable, RdfValueComparer.Compare(bad, DayTime("P1D")));
    }

    //Helpers below.

    private static Literal Int(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Integer);

    private static Literal Decimal(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Decimal);

    private static Literal Float(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Float);

    private static Literal Double(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Double);

    private static Literal Str(string value)
        => MakeLiteral(value, Vocabulary.Xsd.String);

    private static Literal Lang(string value, string tag)
        => new(
            Utf8Strings.From(value),
            new NamedNode(Vocabulary.Rdf.LangString),
            Utf8Strings.From(tag));

    private static Literal Bool(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Boolean);

    private static Literal DateTime(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.DateTime);

    private static Literal Date(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Date);

    private static Literal Time(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Time);

    private static Literal General(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.Duration);

    private static Literal YearMonth(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.YearMonthDuration);

    private static Literal DayTime(string lexical)
        => MakeLiteral(lexical, Vocabulary.Xsd.DayTimeDuration);

    private static Literal MakeLiteral(string lexical, Utf8String datatypeIri)
        => new(Utf8Strings.From(lexical), new NamedNode(datatypeIri));
}
