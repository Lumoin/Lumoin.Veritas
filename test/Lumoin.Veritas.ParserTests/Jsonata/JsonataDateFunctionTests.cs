using System;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for the JSONata date/time built-ins in default (no-picture) mode: <c>$fromMillis</c> /
/// <c>$toMillis</c> ISO-8601 formatting and parsing (including the D3110 reject for an unparseable or
/// non-calendar string), and the <c>$now</c> / <c>$millis</c> determinism over a caller-injected fixed
/// <see cref="TimeProvider"/> (the same instant for every read in one evaluation).
/// </summary>
[TestClass]
internal sealed class JsonataDateFunctionTests
{
    /// <summary>The JSONata error code for a <c>$toMillis</c> string that is not a valid ISO-8601 timestamp.</summary>
    private const string CodeNotIso8601 = "D3110";

    /// <summary>The fixed instant the injected clock is pinned to in the determinism tests: 2020-01-01T00:00:00.000Z.</summary>
    private const long PinnedMillis = 1577836800000L;

    /// <summary><c>$fromMillis</c> formats the Unix epoch as the default ISO-8601 UTC string.</summary>
    [TestMethod]
    public void FromMillisFormatsEpochZero()
    {
        Assert.AreEqual("1970-01-01T00:00:00.000Z", Evaluate("$fromMillis(0)").AsString);
    }

    /// <summary><c>$toMillis</c> inverts <c>$fromMillis</c>, so a known millis value round-trips through the ISO form.</summary>
    [TestMethod]
    public void ToMillisInvertsFromMillis()
    {
        Assert.AreEqual(1502700297574d, Evaluate("$toMillis($fromMillis(1502700297574))").AsNumber);
    }

    /// <summary><c>$toMillis</c> over a string that is not an ISO-8601 timestamp throws D3110.</summary>
    [TestMethod]
    public void ToMillisOnNonIsoStringThrowsD3110()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$toMillis(\"not-a-date\")"));

        Assert.AreEqual(CodeNotIso8601, error.Code.ToString());
    }

    /// <summary><c>$toMillis</c> over a string that matches the ISO shape but is not a real calendar instant still throws D3110, not a framework exception.</summary>
    [TestMethod]
    public void ToMillisOnInvalidCalendarStringThrowsD3110()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$toMillis(\"2018-22\")"));

        Assert.AreEqual(CodeNotIso8601, error.Code.ToString());
    }

    /// <summary><c>$millis</c> returns the injected clock's instant as integer epoch-milliseconds.</summary>
    [TestMethod]
    public void MillisReturnsInjectedInstant()
    {
        Assert.AreEqual((double)PinnedMillis, EvaluateAt("$millis()", PinnedMillis).AsNumber);
    }

    /// <summary><c>$now</c> formats the injected clock's instant as the default ISO-8601 string.</summary>
    [TestMethod]
    public void NowReturnsInjectedInstant()
    {
        Assert.AreEqual("2020-01-01T00:00:00.000Z", EvaluateAt("$now()", PinnedMillis).AsString);
    }

    /// <summary>Two <c>$now</c> reads in one evaluation see the same fixed instant, so they compare equal.</summary>
    [TestMethod]
    public void RepeatedNowReadsAreIdentical()
    {
        Assert.IsTrue(EvaluateAt("$now() = $now()", PinnedMillis).AsBoolean);
    }

    /// <summary>A picture with no markers formats to its literal text.</summary>
    [TestMethod]
    public void FromMillisFormatsLiteralPicture()
    {
        Assert.AreEqual("Hello", Evaluate("$fromMillis(1521801216617, 'Hello')").AsString);
    }

    /// <summary>The <c>[Y0001]</c> year marker, the <c>&lt;…&gt;</c> literal run, and the <c>[Y9,999,*]</c> grouped year all render.</summary>
    [TestMethod]
    public void FromMillisFormatsYearMarkers()
    {
        Assert.AreEqual("Year: 2018", Evaluate("$fromMillis(1521801216617, 'Year: [Y0001]')").AsString);
        Assert.AreEqual("Year: <2018>", Evaluate("$fromMillis(1521801216617, 'Year: <[Y0001]>')").AsString);
        Assert.AreEqual("Year: <2,018>", Evaluate("$fromMillis(1521801216617, 'Year: <[Y9,999,*]>')").AsString);
    }

    /// <summary>The <c>[[</c> and <c>]]</c> escapes render single brackets around a marker.</summary>
    [TestMethod]
    public void FromMillisHandlesBracketEscapes()
    {
        Assert.AreEqual("[Year]: [2018]", Evaluate("$fromMillis(1521801216617, '[[Year]]: [[[Y0001]]]')").AsString);
    }

    /// <summary>The European day/month/year picture renders with the variable-width <c>[D#1]</c>/<c>[M#1]</c> markers.</summary>
    [TestMethod]
    public void FromMillisFormatsEuropeanStyle()
    {
        Assert.AreEqual("23/3/2018", Evaluate("$fromMillis(1521801216617, '[D#1]/[M#1]/[Y0001]')").AsString);
    }

    /// <summary>The US date-and-time picture renders with zero-padded fields.</summary>
    [TestMethod]
    public void FromMillisFormatsUsStyle()
    {
        Assert.AreEqual("03/23/2018 at 10:33:36", Evaluate("$fromMillis(1521801216617, '[M01]/[D01]/[Y0001] at [H01]:[m01]:[s01]')").AsString);
    }

    /// <summary>The full ISO picture renders the year, month, day, time, fractional seconds, and a UTC <c>Z</c>.</summary>
    [TestMethod]
    public void FromMillisFormatsFullIsoPicture()
    {
        Assert.AreEqual("2018-03-23T10:33:36.617Z", Evaluate("$fromMillis(1521801216617, '[Y]-[M01]-[D01]T[H01]:[m]:[s].[f001][Z01:01t]')").AsString);
    }

    /// <summary>Whitespace inside a marker (including a newline) is ignored.</summary>
    [TestMethod]
    public void FromMillisIgnoresWhitespaceInMarkers()
    {
        Assert.AreEqual("2018-03-23T10:33:36.617Z", Evaluate("$fromMillis(1521801216617, '[Y]-[ M01]-[D 01]T[H01 ]:[ m   ]:[s].[f0  01][Z01:\n 01t]')").AsString);
    }

    /// <summary>The numeric weekday <c>[F0]</c> is Monday=1 through Sunday=7 and the named weekday <c>[FNn]</c> is title-cased.</summary>
    [TestMethod]
    public void FromMillisFormatsWeekday()
    {
        Assert.AreEqual("7 Sunday", Evaluate("$fromMillis(1522616700000, '[F0] [FNn]')").AsString);
        Assert.AreEqual("1 Monday", Evaluate("$fromMillis(1522703100000, '[F0] [FNn]')").AsString);
    }

    /// <summary>The named-month marker <c>[MNn]</c> renders the month name and <c>[MN]</c> upper-cases it.</summary>
    [TestMethod]
    public void FromMillisFormatsMonthName()
    {
        Assert.AreEqual("23rd March 2018", Evaluate("$fromMillis(1521801216617, '[D1o] [MNn] [Y]')").AsString);
        Assert.AreEqual("23rd MARCH 2018", Evaluate("$fromMillis(1521801216617, '[D1o] [MN] [Y]')").AsString);
    }

    /// <summary>The abbreviated weekday/month names are truncated to the maximum width.</summary>
    [TestMethod]
    public void FromMillisTruncatesAbbreviatedNames()
    {
        Assert.AreEqual("Fri, 23rd Mar 2018 ISO", Evaluate("$fromMillis(1521801216617, '[FNn,3-3], [D1o] [MNn,3-3] [Y] [C]')").AsString);
    }

    /// <summary>A <c>+0100</c> timezone offsets the wall-clock and renders <c>+0100</c>.</summary>
    [TestMethod]
    public void FromMillisOffsetsForPositiveTimezone()
    {
        Assert.AreEqual("2018-03-23T11:33:36.617+0100", Evaluate("$fromMillis(1521801216617, '[Y]-[M01]-[D01]T[H01]:[m]:[s].[f001][Z0101t]', '+0100')").AsString);
    }

    /// <summary>A zero offset renders <c>+00:00</c> for <c>[Z01:01]</c> and <c>Z</c> for <c>[Z01:01t]</c>.</summary>
    [TestMethod]
    public void FromMillisRendersUtcTimezone()
    {
        Assert.AreEqual("2018-07-11T12:00:00+00:00", Evaluate("$fromMillis(1531310400000, '[Y]-[M01]-[D01]T[H01]:[m]:[s][Z01:01]')").AsString);
        Assert.AreEqual("2018-07-11T12:00:00Z", Evaluate("$fromMillis(1531310400000, '[Y]-[M01]-[D01]T[H01]:[m]:[s][Z01:01t]')").AsString);
    }

    /// <summary>A negative <c>[Z]</c> timezone shifts the wall-clock back and renders <c>-05:00</c>; the <c>[z]</c> form prefixes <c>GMT</c>.</summary>
    [TestMethod]
    public void FromMillisRendersNegativeTimezone()
    {
        Assert.AreEqual("2018-07-11T07:00:00-05:00", Evaluate("$fromMillis(1531310400000, '[Y]-[M01]-[D01]T[H01]:[m]:[s][Z]', '-0500')").AsString);
        Assert.AreEqual("2018-07-11T07:00:00GMT-05:00", Evaluate("$fromMillis(1531310400000, '[Y]-[M01]-[D01]T[H01]:[m]:[s][z]', '-0500')").AsString);
    }

    /// <summary>A six-digit timezone format raises D3134.</summary>
    [TestMethod]
    public void FromMillisRejectsTooManyTimezoneDigits()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$fromMillis(1230757500000, '[Y]-[M01]-[D01]T[H01]:[m]:[s].[f001][Z010101t]', '+0530')"));

        Assert.AreEqual("D3134", error.Code.ToString());
    }

    /// <summary>The default presentation modifiers render the 12-hour clock and the lower-case am/pm meridiem.</summary>
    [TestMethod]
    public void FromMillis12HourClock()
    {
        Assert.AreEqual("friday, 23/3/2018 10:33:36 am", Evaluate("$fromMillis(1521801216617, '[F], [D]/[M]/[Y] [h]:[m]:[s] [P]')").AsString);
        Assert.AreEqual("saturday, 1/3/2008 9:05:00 pm", Evaluate("$fromMillis(1204405500000, '[F], [D]/[M]/[Y] [h]:[m]:[s] [P]')").AsString);
        Assert.AreEqual("monday, 7/1/2008 12:00:00 am", Evaluate("$fromMillis(1199664000000, '[F], [D]/[M]/[Y] [h]:[m]:[s] [P]')").AsString);
    }

    /// <summary>The <c>[PN]</c> meridiem is upper-cased while <c>[Pn]</c> stays lower-case.</summary>
    [TestMethod]
    public void FromMillisMeridiemCase()
    {
        Assert.AreEqual("friday, 23/3/2018 10:33:36 AM", Evaluate("$fromMillis(1521801216617, '[F], [D]/[M]/[Y] [h]:[m]:[s] [PN]')").AsString);
        Assert.AreEqual("friday, 23/3/2018 10:33:36 am", Evaluate("$fromMillis(1521801216617, '[F], [D]/[M]/[Y] [h]:[m]:[s] [Pn]')").AsString);
    }

    /// <summary>The day-of-year fragment counts from 1 on the first of January through the last day of the year.</summary>
    [TestMethod]
    public void FromMillisFormatsDayOfYear()
    {
        Assert.AreEqual("first day of the year", Evaluate("$fromMillis(1514808000000, '[dwo] day of the year')").AsString);
        Assert.AreEqual("365 days in 2018", Evaluate("$fromMillis(1546257600000, '[d] days in [Y0001]')").AsString);
        Assert.AreEqual("366 days in 2016", Evaluate("$fromMillis(1483185600000, '[d] days in [Y0001]')").AsString);
    }

    /// <summary>The ISO week-of-year fragment rolls the first/last partial weeks into the neighbouring year.</summary>
    [TestMethod]
    public void FromMillisFormatsWeekOfYear()
    {
        Assert.AreEqual("Week: 1", Evaluate("$fromMillis(1514808000000, 'Week: [W]')").AsString);
        Assert.AreEqual("Week: 1", Evaluate("$fromMillis(1419854400000, 'Week: [W]')").AsString);
        Assert.AreEqual("Week: 53", Evaluate("$fromMillis(1451304000000, 'Week: [W]')").AsString);
    }

    /// <summary>The ISO week-date picture renders the ISO week-year, the zero-padded week, and the ISO weekday at the year boundaries.</summary>
    [TestMethod]
    public void FromMillisFormatsIsoWeekDate()
    {
        Assert.AreEqual("2004-W53-6", Evaluate("($ts := $toMillis('2005-01-01', '[Y]-[M]-[D]'); $fromMillis($ts, '[X0001]-W[W01]-[F1]') )").AsString);
        Assert.AreEqual("2006-W01-1", Evaluate("($ts := $toMillis('2006-01-02', '[Y]-[M]-[D]'); $fromMillis($ts, '[X0001]-W[W01]-[F1]') )").AsString);
        Assert.AreEqual("2009-W01-3", Evaluate("($ts := $toMillis('2008-12-31', '[Y]-[M]-[D]'); $fromMillis($ts, '[X0001]-W[W01]-[F1]') )").AsString);
    }

    /// <summary>An undefined first argument to <c>$fromMillis</c> with a picture still yields undefined.</summary>
    [TestMethod]
    public void FromMillisUndefinedInputYieldsUndefined()
    {
        Assert.IsTrue(Evaluate("$fromMillis(undefined, 'undefined')").IsUndefined);
    }

    /// <summary>A year-only name picture raises D3133, and an unclosed marker raises D3135.</summary>
    [TestMethod]
    public void FromMillisPictureErrors()
    {
        JsonataErrorException nameError = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$fromMillis(1419940800000, '[YN]-[M]-[D]')"));
        Assert.AreEqual("D3133", nameError.Code.ToString());

        JsonataErrorException unclosed = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$fromMillis(1419940800000, '[YN]-[M')"));
        Assert.AreEqual("D3135", unclosed.Code.ToString());
    }

    /// <summary><c>$toMillis</c> parses a bare year, a year/month/day picture, and the ISO picture.</summary>
    [TestMethod]
    public void ToMillisParsesBasicPictures()
    {
        Assert.AreEqual(1514764800000d, Evaluate("$toMillis('2018', '[Y1]')").AsNumber);
        Assert.AreEqual(1522108800000d, Evaluate("$toMillis('2018-03-27', '[Y1]-[M01]-[D01]')").AsNumber);
        Assert.AreEqual(1522159380123d, Evaluate("$toMillis('2018-03-27T14:03:00.123Z', '[Y0001]-[M01]-[D01]T[H01]:[m01]:[s01].[f001]Z')").AsNumber);
    }

    /// <summary><c>$toMillis</c> parses adjacent fixed-width numeric fields with no separators.</summary>
    [TestMethod]
    public void ToMillisParsesAdjacentFixedWidthFields()
    {
        Assert.AreEqual(1517443200000d, Evaluate("$toMillis('201802', '[Y0001][M01]')").AsNumber);
        Assert.AreEqual(1517788800000d, Evaluate("$toMillis('20180205', '[Y0001][M01][D01]')").AsNumber);
    }

    /// <summary><c>$toMillis</c> parses an ordinal day, a named month, and a 12-hour am/pm time.</summary>
    [TestMethod]
    public void ToMillisParsesOrdinalNameAnd12Hour()
    {
        Assert.AreEqual(196732800000d, Evaluate("$toMillis('27th 3 1976', '[D1o] [M#1] [Y0001]')").AsNumber);
        Assert.AreEqual(1209254400000d, Evaluate("$toMillis('27th April 2008', '[D1o] [MNn] [Y0001]')").AsNumber);
        Assert.AreEqual(1522800360000d, Evaluate("$toMillis('4/4/2018 12:06 am', '[D1]/[M1]/[Y0001] [h]:[m] [P]')").AsNumber);
        Assert.AreEqual(1522843560000d, Evaluate("$toMillis('4/4/2018 12:06 pm', '[D1]/[M1]/[Y0001] [h]:[m] [P]')").AsNumber);
    }

    /// <summary><c>$toMillis</c> derives the date from a day-of-year (dateB) and roundtrips through <c>$fromMillis</c>.</summary>
    [TestMethod]
    public void ToMillisParsesDayOfYear()
    {
        Assert.AreEqual(1522800000000d, Evaluate("$toMillis('2018-094', '[Y0001]-[d001]')").AsNumber);
        Assert.AreEqual("2018-06-29T00:00:00.000Z", Evaluate("$toMillis('2018--180', '[Y]--[d]') ~> $fromMillis()").AsString);
    }

    /// <summary><c>$toMillis</c> parses a separated and a GMT-prefixed timezone offset, roundtripped to UTC.</summary>
    [TestMethod]
    public void ToMillisParsesTimezones()
    {
        Assert.AreEqual("2020-09-09T06:00:00.000Z", Evaluate("$toMillis('2020-09-09 08:00:00 +02:00', '[Y0001]-[M01]-[D01] [H01]:[m01]:[s01] [Z]') ~> $fromMillis()").AsString);
        Assert.AreEqual("2020-09-09T13:00:00.000Z", Evaluate("$toMillis('2020-09-09 08:00:00 GMT-05:00', '[Y0001]-[M01]-[D01] [H01]:[m01]:[s01] [z]') ~> $fromMillis()").AsString);
        Assert.AreEqual("2020-09-09T06:30:00.000Z", Evaluate("$toMillis('2020-09-09 12:00:00 +0530', '[Y0001]-[M01]-[D01] [H01]:[m01]:[s01] [Z0001]') ~> $fromMillis()").AsString);
    }

    /// <summary><c>$toMillis</c> truncates fractional seconds longer than three digits to milliseconds.</summary>
    [TestMethod]
    public void ToMillisTruncatesFractionalSeconds()
    {
        Assert.AreEqual("2026-04-08T19:05:04.019Z", Evaluate("$toMillis('2026-04-08T19:05:04.01987', '[Y0001]-[M01]-[D01]T[H01]:[m01]:[s01].[f1]') ~> $fromMillis()").AsString);
    }

    /// <summary>A date-only parse defaults the time to midnight.</summary>
    [TestMethod]
    public void ToMillisDateOnlyDefaultsTimeToMidnight()
    {
        Assert.AreEqual("2018-11-14T00:00:00.000Z", Evaluate("$toMillis('Wednesday, 14th November 2018', '[FNn], [D1o] [MNn] [Y]') ~> $fromMillis()").AsString);
    }

    /// <summary>A time-only parse defaults the seconds to zero and the date to the captured instant's date.</summary>
    [TestMethod]
    public void ToMillisTimeOnlyDefaultsFromNow()
    {
        Assert.AreEqual("2020-01-01T13:45:00.000Z", EvaluateAt("$toMillis('13:45', '[H]:[m]') ~> $fromMillis()", PinnedMillis).AsString);
    }

    /// <summary>An undefined first argument and a non-matching string both yield undefined.</summary>
    [TestMethod]
    public void ToMillisUndefinedAndNonMatching()
    {
        Assert.IsTrue(Evaluate("$toMillis(undefined, 'pic')").IsUndefined);
        Assert.IsTrue(Evaluate("$toMillis('Hello', 'Hello')").IsUndefined);
        Assert.IsTrue(Evaluate("$toMillis('irrelevent string', '[Y]-[M]-[D]')").IsUndefined);
    }

    /// <summary>An unknown component raises D3132, a named year raises D3133, and a gap in the date components raises D3136.</summary>
    [TestMethod]
    public void ToMillisPictureErrors()
    {
        JsonataErrorException unknown = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$toMillis('2018-05-22', '[Y]-[M]-[q]')"));
        Assert.AreEqual("D3132", unknown.Code.ToString());

        JsonataErrorException named = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$toMillis('2018-05-22', '[YN]-[M]-[D]')"));
        Assert.AreEqual("D3133", named.Code.ToString());

        JsonataErrorException gap = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$toMillis('2018-22', '[Y]-[D]')"));
        Assert.AreEqual("D3136", gap.Code.ToString());
    }

    /// <summary><c>$now</c> formats the captured instant against a supplied picture and timezone.</summary>
    [TestMethod]
    public void NowFormatsAgainstPicture()
    {
        Assert.AreEqual("2020-01-01", EvaluateAt("$now('[Y0001]-[M01]-[D01]')", PinnedMillis).AsString);
    }

    /// <summary><c>$toMillis</c> parses adjacent multi-word date/month/year components, partitioning the words correctly.</summary>
    [TestMethod]
    public void ToMillisParsesAdjacentWordComponents()
    {
        Assert.AreEqual(1503273600000d, Evaluate("$toMillis('twenty-first August two thousand and seventeen', '[Dw] [MNn] [Yw]')").AsNumber);
        Assert.AreEqual(1503360000000d, Evaluate("$toMillis('TWENTY-SECOND August two thousand and seventeen', '[DW] [MNn] [Yw]')").AsNumber);
    }

    /// <summary><c>$toMillis</c> parses a year spelled in words on its own and a day-of-year spelled in ordinal words.</summary>
    [TestMethod]
    public void ToMillisParsesWordYearAndDayOfYear()
    {
        Assert.AreEqual(441763200000d, Evaluate("$toMillis('nineteen hundred and eighty-four', '[Yw]')").AsNumber);
        Assert.AreEqual("2018-12-31T00:00:00.000Z", Evaluate("$toMillis('three hundred and sixty-fifth day of 2018', '[dwo] day of [Y]') ~> $fromMillis()").AsString);
    }

    /// <summary>Evaluates an expression with the system clock (for the pure, clock-independent functions).</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <returns>The normalized result value.</returns>
    private static JsonataValue Evaluate(string expression)
    {
        return EvaluateAt(expression, PinnedMillis);
    }

    /// <summary>Evaluates an expression with the evaluation clock pinned to the given instant.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="millis">The instant to pin the clock to, as integer epoch-milliseconds (UTC).</param>
    /// <returns>The normalized result value.</returns>
    private static JsonataValue EvaluateAt(string expression, long millis)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes("{}")));
        FixedTimeProvider clock = new(DateTimeOffset.FromUnixTimeMilliseconds(millis));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input, pool: null, timeProvider: clock);
    }

    /// <summary>A <see cref="TimeProvider"/> whose <see cref="GetUtcNow"/> always returns one fixed instant, so the date built-ins are deterministic under test.</summary>
    private sealed class FixedTimeProvider: TimeProvider
    {
        /// <summary>The fixed instant returned by every clock read.</summary>
        private readonly DateTimeOffset instant;

        /// <summary>Initializes the provider with the instant it always reports.</summary>
        /// <param name="instant">The fixed instant.</param>
        public FixedTimeProvider(DateTimeOffset instant)
        {
            this.instant = instant;
        }

        /// <summary>Returns the fixed instant.</summary>
        /// <returns>The fixed instant.</returns>
        public override DateTimeOffset GetUtcNow()
        {
            return instant;
        }
    }
}
