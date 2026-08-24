using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Parsed XSD duration value, decomposed into a year-month component
/// (months only — years are folded into months at parse time) and a
/// day-time component (seconds only — days, hours, minutes folded
/// into seconds).
/// </summary>
/// <remarks>
/// <para>
/// XSD §3.2.6 defines three duration types:
/// </para>
/// <list type="bullet">
///   <item><description><c>xsd:duration</c> — both components may be non-zero; <em>partial</em> order.</description></item>
///   <item><description><c>xsd:yearMonthDuration</c> — only year-month component; <em>total</em> order.</description></item>
///   <item><description><c>xsd:dayTimeDuration</c> — only day-time component; <em>total</em> order.</description></item>
/// </list>
/// <para>
/// Cross-subtype comparison (yearMonth vs dayTime) is always
/// <see cref="ComparisonResult.Incomparable"/> because the values
/// occupy different value spaces.
/// </para>
/// <para>
/// <b>Partial order for the general type.</b> XSD §3.2.6.2 defines
/// duration ordering by adding each duration to four specific
/// reference instants and comparing the resulting dates. The four
/// reference instants are chosen so that month lengths cover all
/// 28/29/30/31 cases. If the same ordering holds for all four, the
/// comparison is decided; otherwise it is indeterminate.
/// </para>
/// </remarks>
internal readonly partial struct XsdDuration
{
    /// <summary>Year-month component, expressed as total months. Negative for negative durations.</summary>
    public int Months { get; }

    /// <summary>Day-time component, expressed as total seconds (with fractional part). Negative for negative durations.</summary>
    public decimal Seconds { get; }

    private XsdDuration(int months, decimal seconds)
    {
        Months = months;
        Seconds = seconds;
    }

    /// <summary>
    /// Parses a duration lexical form against the given XSD subtype.
    /// Subtype constraints are enforced: <c>yearMonthDuration</c>
    /// rejects forms with day/time components,
    /// <c>dayTimeDuration</c> rejects forms with year/month
    /// components.
    /// </summary>
    public static bool TryParse(string lexicalForm, ValueSpace subtype, out XsdDuration value)
    {
        value = default;
        if(string.IsNullOrEmpty(lexicalForm))
        {
            return false;
        }

        Match match = DurationPattern().Match(lexicalForm);
        if(!match.Success)
        {
            return false;
        }

        int sign = match.Groups["sign"].Value == "-" ? -1 : 1;
        int years = ParseGroupOrZero(match, "years");
        int months = ParseGroupOrZero(match, "months");
        int days = ParseGroupOrZero(match, "days");
        int hours = ParseGroupOrZero(match, "hours");
        int minutes = ParseGroupOrZero(match, "minutes");
        decimal seconds = ParseSecondsOrZero(match);

        bool hasYearMonth = years != 0 || months != 0;
        bool hasDayTime = days != 0 || hours != 0 || minutes != 0 || seconds != 0m;

        //An XSD duration must have at least one component present in
        //the lexical form; the regex permits "P" with nothing after,
        //which is invalid.
        if(!match.Groups["years"].Success
            && !match.Groups["months"].Success
            && !match.Groups["days"].Success
            && !match.Groups["hours"].Success
            && !match.Groups["minutes"].Success
            && !match.Groups["seconds"].Success)
        {
            return false;
        }

        //Subtype gating.
        if(subtype == ValueSpace.YearMonthDuration && hasDayTime)
        {
            return false;
        }
        if(subtype == ValueSpace.DayTimeDuration && hasYearMonth)
        {
            return false;
        }

        int totalMonths = sign * (years * 12 + months);
        decimal totalSeconds = sign * (((decimal)days * 86400m)
            + ((decimal)hours * 3600m)
            + ((decimal)minutes * 60m)
            + seconds);

        value = new XsdDuration(totalMonths, totalSeconds);

        return true;
    }

    /// <summary>
    /// Compares two durations. The caller's <paramref name="leftSpace"/>
    /// and <paramref name="rightSpace"/> are the
    /// <see cref="ValueSpace"/> classifications of the operands; they
    /// determine whether the comparison is total (both same restricted
    /// subtype), partial via the four-test-points algorithm (general
    /// duration), or always incomparable (mixed subtypes).
    /// </summary>
    public static ComparisonResult Compare(
        XsdDuration left, ValueSpace leftSpace,
        XsdDuration right, ValueSpace rightSpace)
    {
        //Mixed restricted subtypes are never comparable.
        if(leftSpace == ValueSpace.YearMonthDuration && rightSpace == ValueSpace.DayTimeDuration)
        {
            return ComparisonResult.Incomparable;
        }
        if(leftSpace == ValueSpace.DayTimeDuration && rightSpace == ValueSpace.YearMonthDuration)
        {
            return ComparisonResult.Incomparable;
        }

        //Same restricted subtype, or both general. yearMonthDuration
        //vs yearMonthDuration: compare months only. dayTimeDuration
        //vs dayTimeDuration: compare seconds only. Both general or
        //one general + one restricted: partial order via four
        //reference instants.
        bool bothYearMonth = leftSpace == ValueSpace.YearMonthDuration && rightSpace == ValueSpace.YearMonthDuration;
        bool bothDayTime = leftSpace == ValueSpace.DayTimeDuration && rightSpace == ValueSpace.DayTimeDuration;
        if(bothYearMonth)
        {
            return SignToResult(left.Months.CompareTo(right.Months));
        }
        if(bothDayTime)
        {
            return SignToResult(left.Seconds.CompareTo(right.Seconds));
        }

        //General-duration comparison (or general vs restricted).
        return ComparePartialOrder(left, right);
    }

    //XSD §3.2.6.2 partial-order test: compute (left + ref) vs
    //(right + ref) for each of four reference instants. If the same
    //ordering holds at all four, that's the result; otherwise
    //indeterminate.
    private static ComparisonResult ComparePartialOrder(XsdDuration left, XsdDuration right)
    {
        ComparisonResult? agreed = null;
        foreach(DateTime reference in ReferenceInstants)
        {
            DateTime leftInstant = AddDuration(reference, left);
            DateTime rightInstant = AddDuration(reference, right);
            ComparisonResult thisRound = SignToResult(leftInstant.CompareTo(rightInstant));

            if(agreed is null)
            {
                agreed = thisRound;

                continue;
            }

            if(agreed != thisRound)
            {
                return ComparisonResult.Incomparable;
            }
        }

        return agreed ?? ComparisonResult.Equal;
    }

    //Adds a duration's months and seconds components to a reference
    //DateTime. Months are added via DateTime.AddMonths to respect
    //month-length variation; seconds are added as a TimeSpan. The
    //decimal-to-double conversion may lose precision below the
    //microsecond level, but ordering at that resolution is rarely
    //meaningful for durations and is well outside the spec's
    //intended use.
    private static DateTime AddDuration(DateTime reference, XsdDuration duration)
    {
        DateTime afterMonths = reference.AddMonths(duration.Months);
        TimeSpan delta = TimeSpan.FromSeconds((double)duration.Seconds);

        return afterMonths + delta;
    }

    private static int ParseGroupOrZero(Match match, string groupName)
    {
        Group group = match.Groups[groupName];
        if(!group.Success || group.Length == 0)
        {
            return 0;
        }

        return int.Parse(group.Value, CultureInfo.InvariantCulture);
    }

    private static decimal ParseSecondsOrZero(Match match)
    {
        Group group = match.Groups["seconds"];
        if(!group.Success || group.Length == 0)
        {
            return 0m;
        }

        return decimal.Parse(group.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    private static ComparisonResult SignToResult(int sign)
        => sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal,
        };

    //The four reference instants from XSD §3.2.6.2. Chosen so the
    //month-length variation across them covers all the cases that
    //can flip a duration ordering.
    private static DateTime[] ReferenceInstants { get; } =
    [
        new DateTime(1696, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(1697, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(1903, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(1903, 7, 1, 0, 0, 0, DateTimeKind.Utc),
    ];

    //Lexical pattern. Captures: optional minus sign, optional Y/M
    //(year-month components), optional T-led D/H/M/S (day-time
    //components). Each numeric group is independently optional, but
    //the parser checks at least one group is present.
    [GeneratedRegex(
        @"^(?<sign>-)?P(?:(?<years>\d+)Y)?(?:(?<months>\d+)M)?(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+(?:\.\d+)?)S)?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
