using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Parsed representation of an <c>xsd:dateTime</c>, <c>xsd:date</c>,
/// or <c>xsd:time</c> literal, carrying either an explicit timezone
/// offset or a "naive" flag indicating no timezone was present.
/// </summary>
/// <remarks>
/// <para>
/// XSD §3.2.7.4 specifies that comparison of a timezone-aware value
/// with a timezone-naive value is <em>indeterminate</em> when the
/// two could occupy the same instant under any timezone within the
/// ±14h envelope. The <see cref="Compare"/> method returns
/// <see cref="ComparisonResult.Incomparable"/> in that case.
/// </para>
/// <para>
/// The lexical-form parser is liberal in the sense of accepting any
/// XSD-valid form including negative years, leading zeroes, and
/// fractional seconds of arbitrary precision. It is strict in
/// rejecting trailing whitespace, missing required components, and
/// non-canonical sign forms.
/// </para>
/// <para>
/// <c>xsd:dateTimeStamp</c> is <c>xsd:dateTime</c> with a required
/// timezone; the parser, given the <c>dateTimeStamp</c> datatype
/// IRI, fails on naive input.
/// </para>
/// </remarks>
internal readonly partial struct DateTimeValue
{
    /// <summary>The instant as a <see cref="DateTime"/> in UTC if aware, or in unspecified-kind local time if naive.</summary>
    public DateTime Instant { get; }

    /// <summary>The original timezone offset; <c>null</c> when the value was naive.</summary>
    public TimeSpan? Offset { get; }

    /// <summary>Whether the value was parsed with a timezone offset.</summary>
    public bool IsAware => Offset.HasValue;

    private DateTimeValue(DateTime instant, TimeSpan? offset)
    {
        Instant = instant;
        Offset = offset;
    }

    /// <summary>
    /// Parses an <c>xsd:dateTime</c> lexical form. Accepts both naive
    /// (no timezone) and aware (Z or ±HH:mm) forms. When
    /// <paramref name="requireTimezone"/> is <c>true</c> (the
    /// <c>xsd:dateTimeStamp</c> case), naive forms fail.
    /// </summary>
    public static bool TryParseDateTime(string lexicalForm, bool requireTimezone, out DateTimeValue value)
    {
        value = default;
        if(string.IsNullOrEmpty(lexicalForm))
        {
            return false;
        }

        Match match = DateTimePattern().Match(lexicalForm);
        if(!match.Success)
        {
            return false;
        }

        string timezone = match.Groups["tz"].Value;
        bool hasTimezone = !string.IsNullOrEmpty(timezone);
        if(requireTimezone && !hasTimezone)
        {
            return false;
        }

        DateTimeStyles styles = hasTimezone
            ? DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
            : DateTimeStyles.AssumeLocal;

        if(!DateTimeOffset.TryParse(lexicalForm, CultureInfo.InvariantCulture, styles, out DateTimeOffset parsed))
        {
            return false;
        }

        if(hasTimezone)
        {
            //AdjustToUniversal converted Instant to UTC.
            value = new DateTimeValue(parsed.UtcDateTime, parsed.Offset);

            return true;
        }

        //Naive: keep the wall-clock value. DateTimeOffset.TryParse
        //with AssumeLocal applied a local offset; strip it and treat
        //the wall-clock as the value.
        DateTime wallClock = DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
        value = new DateTimeValue(wallClock, offset: null);

        return true;
    }

    /// <summary>
    /// Parses an <c>xsd:date</c> lexical form. Same naive/aware
    /// distinction as <see cref="TryParseDateTime"/>; the time
    /// component is fixed at <c>00:00:00</c> for ordering purposes.
    /// </summary>
    public static bool TryParseDate(string lexicalForm, out DateTimeValue value)
    {
        value = default;
        if(string.IsNullOrEmpty(lexicalForm))
        {
            return false;
        }

        Match match = DatePattern().Match(lexicalForm);
        if(!match.Success)
        {
            return false;
        }

        string timezone = match.Groups["tz"].Value;
        bool hasTimezone = !string.IsNullOrEmpty(timezone);

        //Re-form as a dateTime by appending T00:00:00 plus the
        //original timezone (if any), then parse via DateTimeOffset.
        string dateBody = match.Groups["body"].Value;
        string asDateTime = $"{dateBody}T00:00:00{timezone}";

        DateTimeStyles styles = hasTimezone
            ? DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
            : DateTimeStyles.AssumeLocal;

        if(!DateTimeOffset.TryParse(asDateTime, CultureInfo.InvariantCulture, styles, out DateTimeOffset parsed))
        {
            return false;
        }

        if(hasTimezone)
        {
            value = new DateTimeValue(parsed.UtcDateTime, parsed.Offset);

            return true;
        }

        DateTime wallClock = DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
        value = new DateTimeValue(wallClock, offset: null);

        return true;
    }

    /// <summary>
    /// Parses an <c>xsd:time</c> lexical form. Time-of-day with
    /// optional timezone; comparison is on the time-of-day itself.
    /// </summary>
    public static bool TryParseTime(string lexicalForm, out DateTimeValue value)
    {
        value = default;
        if(string.IsNullOrEmpty(lexicalForm))
        {
            return false;
        }

        Match match = TimePattern().Match(lexicalForm);
        if(!match.Success)
        {
            return false;
        }

        string timezone = match.Groups["tz"].Value;
        bool hasTimezone = !string.IsNullOrEmpty(timezone);

        //Re-form as a dateTime on a reference date; comparison can
        //still be done on the resulting DateTime values because both
        //operands of any comparison get the same reference date.
        string asDateTime = $"1972-12-31T{match.Groups["body"].Value}{timezone}";

        DateTimeStyles styles = hasTimezone
            ? DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
            : DateTimeStyles.AssumeLocal;

        if(!DateTimeOffset.TryParse(asDateTime, CultureInfo.InvariantCulture, styles, out DateTimeOffset parsed))
        {
            return false;
        }

        if(hasTimezone)
        {
            value = new DateTimeValue(parsed.UtcDateTime, parsed.Offset);

            return true;
        }

        DateTime wallClock = DateTime.SpecifyKind(parsed.DateTime, DateTimeKind.Unspecified);
        value = new DateTimeValue(wallClock, offset: null);

        return true;
    }

    /// <summary>
    /// Compares two parsed values. Returns
    /// <see cref="ComparisonResult.Incomparable"/> when one operand
    /// is timezone-naive and the other is timezone-aware and the
    /// indeterminate window (per XSD §3.2.7.4) covers the comparison.
    /// </summary>
    public static ComparisonResult Compare(DateTimeValue left, DateTimeValue right)
    {
        if(left.IsAware && right.IsAware)
        {
            //Both values were normalised to UTC at parse time;
            //direct comparison.
            return SignToResult(left.Instant.CompareTo(right.Instant));
        }

        if(!left.IsAware && !right.IsAware)
        {
            //Both naive: direct wall-clock comparison.
            return SignToResult(left.Instant.CompareTo(right.Instant));
        }

        //One naive, one aware: apply the XSD §3.2.7.4 indeterminate-
        //comparison algorithm. The naive value's range of possible
        //instants is [Instant - 14h, Instant + 14h]. If that range
        //is strictly less than or strictly greater than the aware
        //value, the comparison is determined; if it overlaps, the
        //comparison is indeterminate.
        DateTimeValue naive = left.IsAware ? right : left;
        DateTimeValue aware = left.IsAware ? left : right;

        DateTime naiveLow = naive.Instant - MaxTimezoneOffset;
        DateTime naiveHigh = naive.Instant + MaxTimezoneOffset;

        ComparisonResult naiveOnLeft = CompareNaiveAware(naiveLow, naiveHigh, aware.Instant);

        //If naive was actually the right operand, flip the verdict.
        if(left.IsAware)
        {
            return Flip(naiveOnLeft);
        }

        return naiveOnLeft;
    }

    private static ComparisonResult CompareNaiveAware(DateTime naiveLow, DateTime naiveHigh, DateTime awareInstant)
    {
        //Naive could be at any instant in [naiveLow, naiveHigh].
        //If the entire range is below awareInstant, naive < aware.
        //If the entire range is above awareInstant, naive > aware.
        //Otherwise indeterminate.
        if(naiveHigh < awareInstant)
        {
            return ComparisonResult.Less;
        }

        if(naiveLow > awareInstant)
        {
            return ComparisonResult.Greater;
        }

        return ComparisonResult.Incomparable;
    }

    private static ComparisonResult Flip(ComparisonResult original)
        => original switch
        {
            ComparisonResult.Less => ComparisonResult.Greater,
            ComparisonResult.Greater => ComparisonResult.Less,
            _ => original,
        };

    private static ComparisonResult SignToResult(int sign)
        => sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal,
        };

    //Maximum timezone offset is 14 hours per XSD §3.2.7.3.
    private static TimeSpan MaxTimezoneOffset { get; } = TimeSpan.FromHours(14);

    //Lexical patterns. Each captures the body (date/time portion) and
    //the optional timezone separately so we can decide aware vs naive
    //and reconstruct a parseable string for DateTimeOffset.TryParse.
    [GeneratedRegex(@"^(?<body>-?\d{4,}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?)(?<tz>Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DateTimePattern();

    [GeneratedRegex(@"^(?<body>-?\d{4,}-\d{2}-\d{2})(?<tz>Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"^(?<body>\d{2}:\d{2}:\d{2}(\.\d+)?)(?<tz>Z|[+-]\d{2}:\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();
}
