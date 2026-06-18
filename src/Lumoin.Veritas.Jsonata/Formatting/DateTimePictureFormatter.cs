using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IntegerPicture = Lumoin.Veritas.Jsonata.Formatting.IntegerPictureFormatter.IntegerPicture;

namespace Lumoin.Veritas.Jsonata.Formatting;

/// <summary>
/// Formats and parses a date/time against an XPath <c>fn:format-dateTime</c> / <c>fn:parse-dateTime</c> picture
/// string for the <c>$fromMillis</c>, <c>$now</c>, and <c>$toMillis</c> built-ins. This is the date/time-picture
/// member of the reusable <c>Formatting</c> unit; it analyses a picture once into a part list and then renders an
/// epoch instant to a string or matches a string back to an epoch instant. Numeric components delegate to
/// <see cref="IntegerPictureFormatter"/> through its spec-based seam.
/// </summary>
/// <remarks>
/// <para>
/// A picture is a sequence of literal runs and bracketed component markers (<c>[Y]</c>, <c>[M01]</c>,
/// <c>[MNn]</c>, …); the marker grammar is the XPath one (component letter, presentation modifier, optional
/// <c>,min-max</c> width). Every instant is interpreted in UTC and offset by the supplied numeric timezone, so
/// the formatting is wall-clock-free and deterministic.
/// </para>
/// <para>
/// The analysis, the component-fragment computation, and the parse are all iterative. The year-month walk used
/// by the ISO-week fragments is a <see cref="YearMonth"/> value with explicit step methods rather than a closure;
/// the date arithmetic mirrors the reference engine's <c>Date.UTC</c> overflow normalisation through
/// <see cref="DateTime"/> month/day addition.
/// </para>
/// <para>See <see href="https://docs.jsonata.org/date-time-functions">the JSONata date/time-functions reference</see>.</para>
/// </remarks>
internal static class DateTimePictureFormatter
{
    /// <summary>The weekday names indexed Monday=1 through Sunday=7; index 0 is unused, matching the reference's leading blank.</summary>
    private static readonly string[] DayNames =
    [
        "", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    ];

    /// <summary>The month names indexed January=0 through December=11.</summary>
    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    /// <summary>The number of milliseconds in one day.</summary>
    private const long MillisInADay = 1000L * 60 * 60 * 24;

    /// <summary>The default ISO-8601 picture, the form produced when no picture is supplied.</summary>
    private const string Iso8601Picture = "[Y0001]-[M01]-[D01]T[H01]:[m01]:[s01].[f001][Z01:01t]";

    /// <summary>
    /// Formats epoch-milliseconds against a picture (the default ISO-8601 picture when none is supplied), offset
    /// by the supplied numeric timezone. The timezone is a signed <c>±HHmm</c> integer string such as
    /// <c>+0100</c>; the offset shifts the wall-clock fields and the <c>Z</c>/<c>z</c> components render it.
    /// </summary>
    /// <param name="millis">The epoch-milliseconds to format.</param>
    /// <param name="picture">The picture string, or <see langword="null"/> for the default ISO-8601 picture.</param>
    /// <param name="timezone">The numeric timezone string (<c>±HHmm</c>), or <see langword="null"/> for UTC.</param>
    /// <returns>The formatted string.</returns>
    /// <exception cref="JsonataErrorException">The picture is malformed (D3132/D3133/D3134/D3135).</exception>
    public static string FormatDateTime(long millis, string? picture, string? timezone)
    {
        int offsetHours = 0;
        int offsetMinutes = 0;
        if(timezone is not null)
        {
            int offset = int.Parse(timezone, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

            //The hours are the floored hundreds and the minutes the signed remainder, mirroring the reference's
            //Math.floor(offset/100) and offset%100: a negative offset with non-zero minutes carries the hour
            //toward negative infinity (so -0530 decomposes to -6 hours and -30 minutes, as in the reference).
            offsetHours = (int)Math.Floor(offset / 100.0);
            offsetMinutes = offset % 100;
        }

        DateTimePictureSpec spec = Analyse(picture ?? Iso8601Picture);
        long offsetMillis = ((60L * offsetHours) + offsetMinutes) * 60 * 1000;
        DateTime instant = DateTimeOffset.FromUnixTimeMilliseconds(millis + offsetMillis).UtcDateTime;

        StringBuilder result = new();
        foreach(DateTimePart part in spec.Parts)
        {
            result.Append(part.IsLiteral ? part.Literal : FormatComponent(instant, part, offsetHours, offsetMinutes));
        }

        return result.ToString();
    }

    /// <summary>
    /// Parses a timestamp string against a picture to UTC epoch-milliseconds. A combined case-insensitive regular
    /// expression is built from the picture at runtime, each captured group is transformed back to its component
    /// value, the present components are reconciled against the supported component sets, and the unspecified
    /// fields are defaulted (the fields before the first specified one from <paramref name="nowMillis"/>, the
    /// fields after it to their zero/one). A non-matching timestamp yields no millis and the caller maps that to
    /// undefined.
    /// </summary>
    /// <remarks>
    /// The reconciled instant is materialised through <see cref="DateTime"/>, whose representable range is the
    /// years 0001-9999; a parsed year outside that range (reachable only from adversarial input far outside the
    /// corpus) is a representation limit narrower than the reference engine's and surfaces as the underlying range
    /// error rather than a formatted value.
    /// </remarks>
    /// <param name="timestamp">The timestamp string to parse.</param>
    /// <param name="picture">The picture string.</param>
    /// <param name="nowMillis">The evaluation's captured instant, defaulting the fields before the first specified one.</param>
    /// <returns>The UTC epoch-milliseconds, or <see langword="null"/> when the timestamp does not match the picture.</returns>
    /// <exception cref="JsonataErrorException">The picture is malformed (D3132/D3133/D3135) or the parsed components are inconsistent/unsupported (D3136).</exception>
    public static long? ParseDateTime(string timestamp, string picture, long nowMillis)
    {
        DateTimePictureSpec spec = Analyse(picture);
        List<DateMatchPart> matchParts = GenerateRegex(spec);

        StringBuilder pattern = new();
        pattern.Append('^');
        foreach(DateMatchPart matchPart in matchParts)
        {
            pattern.Append('(').Append(matchPart.Regex).Append(')');
        }

        pattern.Append('$');

        //The pattern is built from the user picture at runtime, so it cannot be a source-generated regex; this is
        //the sanctioned runtime-Regex case for a dynamic pattern, matched case-insensitively as the reference does.
        Match match = new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Match(timestamp);
        if(!match.Success)
        {
            return null;
        }

        DateComponents components = new();
        for(int i = 0; i < matchParts.Count; i++)
        {
            DateMatchPart matchPart = matchParts[i];
            if(matchPart.HasParse)
            {
                components.Set(matchPart.Component, matchPart.Parse(match.Groups[i + 1].Value));
            }
        }

        if(components.Count == 0)
        {
            return null;
        }

        return Reconcile(components, nowMillis);
    }

    /// <summary>
    /// Reconciles the parsed components: classifies the date and time component sets through the reference's
    /// bit-mask, defaults the unspecified fields, derives a day-of-year date, applies 12-hour resolution, and
    /// assembles the UTC instant less the timezone offset.
    /// </summary>
    /// <param name="components">The parsed components.</param>
    /// <param name="nowMillis">The evaluation's captured instant, defaulting the fields before the first specified one.</param>
    /// <returns>The UTC epoch-milliseconds.</returns>
    /// <exception cref="JsonataErrorException">The component set is inconsistent or unsupported (D3136).</exception>
    private static long Reconcile(DateComponents components, long nowMillis)
    {
        //The bit-mask classifies which named components are present; the type constants name the supported sets.
        const int DateMaskA = 161;
        const int DateMaskB = 130;
        const int DateMaskC = 84;
        const int DateMaskD = 72;
        const int TimeMaskA = 23;
        const int TimeMaskB = 47;

        int dateMask = BuildMask(components, "YXMxWwdD");
        bool dateA = IsType(dateMask, DateMaskA);
        bool dateB = !dateA && IsType(dateMask, DateMaskB);
        bool dateC = IsType(dateMask, DateMaskC);
        bool dateD = !dateC && IsType(dateMask, DateMaskD);

        int timeMask = BuildMask(components, "PHhmsf");
        bool timeA = IsType(timeMask, TimeMaskA);
        bool timeB = !timeA && IsType(timeMask, TimeMaskB);

        string dateComps = dateB ? "YD" : dateC ? "XxwF" : dateD ? "XWF" : "YMD";
        string timeComps = timeB ? "Phmsf" : "Hmsf";
        string comps = dateComps + timeComps;

        DateTime now = DateTimeOffset.FromUnixTimeMilliseconds(nowMillis).UtcDateTime;
        bool startSpecified = false;
        bool endSpecified = false;
        foreach(char part in comps)
        {
            if(!components.Has(part))
            {
                if(startSpecified)
                {
                    components.Set(part, "MDd".Contains(part, StringComparison.Ordinal) ? 1 : 0);
                    endSpecified = true;
                }
                else
                {
                    components.Set(part, GetDateTimeFragment(now, part));
                }
            }
            else
            {
                startSpecified = true;
                if(endSpecified)
                {
                    throw InconsistentComponents();
                }
            }
        }

        //The month component arrives one-based and is shifted to the zero-based month the assembly uses.
        components.Set('M', components.Get('M') > 0 ? components.Get('M') - 1 : 0);

        if(dateB)
        {
            //The day-of-year date is derived by adding the zero-based day offset to the first of January.
            DateTime firstJan = UtcDate((int)components.Get('Y'), 0, 1);
            DateTime derived = firstJan.AddDays(components.Get('d') - 1);
            components.Set('M', derived.Month - 1);
            components.Set('D', derived.Day);
        }

        if(dateC || dateD)
        {
            throw InconsistentComponents();
        }

        if(timeB)
        {
            components.Set('H', components.Get('h') == 12 ? 0 : components.Get('h'));
            if(components.Get('P') == 1)
            {
                components.Set('H', components.Get('H') + 12);
            }
        }

        DateTime assembled = UtcDateTime(
            (int)components.Get('Y'),
            (int)components.Get('M'),
            (int)components.Get('D'),
            (int)components.Get('H'),
            (int)components.Get('m'),
            (int)components.Get('s'),
            (int)components.Get('f'));

        long millis = new DateTimeOffset(assembled, TimeSpan.Zero).ToUnixTimeMilliseconds();

        double zone = components.Has('Z') ? components.Get('Z') : components.Has('z') ? components.Get('z') : 0;

        return millis - (long)(zone * 60 * 1000);
    }

    /// <summary>
    /// Builds the eight-bit mask of which named components are present with a non-zero value,
    /// most-significant-first over the given component letters, mirroring the reference's <c>shift</c> fold over
    /// the parsed values (where a zero value is falsy and contributes a clear bit).
    /// </summary>
    /// <param name="components">The parsed components.</param>
    /// <param name="parts">The component letters, in mask order.</param>
    /// <returns>The presence mask.</returns>
    private static int BuildMask(DateComponents components, string parts)
    {
        int mask = 0;
        foreach(char part in parts)
        {
            mask <<= 1;
            mask += components.Has(part) && components.Get(part) != 0 ? 1 : 0;
        }

        return mask;
    }

    /// <summary>Determines whether a mask has exactly the bits of a type set among that type's positions, the reference's <c>isType</c>.</summary>
    /// <param name="mask">The presence mask.</param>
    /// <param name="type">The type constant.</param>
    /// <returns><see langword="true"/> when the mask matches the type.</returns>
    private static bool IsType(int mask, int type)
    {
        return (~type & mask) == 0 && (type & mask) != 0;
    }

    /// <summary>
    /// Renders one component of the instant: a numeric component through the integer formatter (with the year
    /// truncation), a named component (month/weekday/am-pm) through the name tables, and a timezone component
    /// through the offset rendering.
    /// </summary>
    /// <param name="instant">The offset-adjusted UTC instant whose fields are rendered.</param>
    /// <param name="part">The component part.</param>
    /// <param name="offsetHours">The timezone offset hours.</param>
    /// <param name="offsetMinutes">The timezone offset minutes.</param>
    /// <returns>The rendered component.</returns>
    /// <exception cref="JsonataErrorException">A name was requested for a non-name component (D3133) or the timezone digit count is out of range (D3134).</exception>
    private static string FormatComponent(DateTime instant, DateTimePart part, int offsetHours, int offsetMinutes)
    {
        char component = part.Component;
        if(component is 'Z' or 'z')
        {
            return FormatTimezone(part, offsetHours, offsetMinutes);
        }

        if(component == 'P')
        {
            string meridiem = GetDateTimeFragment(instant, 'P') >= 0.5 ? "pm" : "am";

            return part.Names == NameCase.Upper ? meridiem.ToUpperInvariant() : meridiem;
        }

        double value = GetDateTimeFragment(instant, component);
        if("YMDdFWwXxHhms".Contains(component, StringComparison.Ordinal))
        {
            if(component == 'Y' && part.YearDigits != -1)
            {
                value %= Math.Pow(10, part.YearDigits);
            }

            if(part.Names != NameCase.None)
            {
                return FormatName(component, value, part);
            }

            return IntegerPictureFormatter.FormatInteger(value, part.IntegerFormat);
        }

        if(component == 'f')
        {
            return IntegerPictureFormatter.FormatInteger(value, part.IntegerFormat);
        }

        if(component is 'C' or 'E')
        {
            return "ISO";
        }

        return "";
    }

    /// <summary>Renders a named component (month/weekday) against its table, applying the requested case and width truncation.</summary>
    /// <param name="component">The component letter.</param>
    /// <param name="value">The numeric component value.</param>
    /// <param name="part">The component part carrying the case and width.</param>
    /// <returns>The rendered name.</returns>
    /// <exception cref="JsonataErrorException">The component does not support names (D3133).</exception>
    private static string FormatName(char component, double value, DateTimePart part)
    {
        string name = component switch
        {
            'M' or 'x' => MonthNames[(int)value - 1],
            'F' => DayNames[(int)value],
            _ => throw NameOnNonNameComponent(component)
        };

        name = part.Names switch
        {
            NameCase.Upper => name.ToUpperInvariant(),
            NameCase.Lower => IntegerNumerals.ToLower(name),
            _ => name
        };

        if(part.MaxWidth > 0 && name.Length > part.MaxWidth)
        {
            name = name[..part.MaxWidth];
        }

        return name;
    }

    /// <summary>
    /// Renders the timezone component from the offset: a regular-grouped integer format renders the whole offset,
    /// a one- or two-digit format renders the hours (with an appended <c>:mm</c> when minutes are present), a
    /// three- or four-digit format renders the whole offset, the sign is prefixed for a non-negative offset, the
    /// <c>z</c> component prefixes <c>GMT</c>, and a zero offset with the <c>t</c> modifier renders <c>Z</c>.
    /// </summary>
    /// <param name="part">The timezone component part.</param>
    /// <param name="offsetHours">The timezone offset hours.</param>
    /// <param name="offsetMinutes">The timezone offset minutes.</param>
    /// <returns>The rendered timezone.</returns>
    /// <exception cref="JsonataErrorException">The mandatory-digit count is not in the range 1 to 4 (D3134).</exception>
    private static string FormatTimezone(DateTimePart part, int offsetHours, int offsetMinutes)
    {
        int offset = (offsetHours * 100) + offsetMinutes;
        IntegerPicture format = part.IntegerFormat;
        string componentValue;
        if(format.Regular)
        {
            componentValue = IntegerPictureFormatter.FormatInteger(offset, format);
        }
        else
        {
            int numDigits = format.MandatoryDigits;
            if(numDigits is 1 or 2)
            {
                componentValue = IntegerPictureFormatter.FormatInteger(offsetHours, format);
                if(offsetMinutes != 0)
                {
                    componentValue += ":" + IntegerPictureFormatter.Format(offsetMinutes, "00");
                }
            }
            else if(numDigits is 3 or 4)
            {
                componentValue = IntegerPictureFormatter.FormatInteger(offset, format);
            }
            else
            {
                throw new JsonataErrorException(WellKnownJsonataErrors.TimezoneDigitCount, numDigits.ToString(CultureInfo.InvariantCulture), "The $fromMillis timezone format has an unsupported mandatory-digit count.");
            }
        }

        if(offset >= 0)
        {
            componentValue = "+" + componentValue;
        }

        if(part.Component == 'z')
        {
            componentValue = "GMT" + componentValue;
        }

        if(offset == 0 && part.Presentation2 == 't')
        {
            componentValue = "Z";
        }

        return componentValue;
    }

    /// <summary>
    /// Computes one date/time fragment of a UTC instant: the calendar fields directly, the day-of-year, the
    /// ISO weekday (Monday=1 through Sunday=7), the week-of-year, the week-of-month, the ISO week-year, the
    /// ISO month-of-week-year, the 24-hour and 12-hour hours, the am/pm flag (1 for pm), the minutes, the
    /// seconds, and the milliseconds.
    /// </summary>
    /// <param name="date">The UTC instant whose fragment is computed.</param>
    /// <param name="component">The component letter.</param>
    /// <returns>The fragment value (am/pm is returned as 0 or 1).</returns>
    private static double GetDateTimeFragment(DateTime date, char component)
    {
        return component switch
        {
            'Y' => date.Year,
            'M' => date.Month,
            'D' => date.Day,
            'd' => DayOfYear(date),
            'F' => IsoWeekday(date),
            'W' => WeekOfYear(date),
            'w' => WeekOfMonth(date),
            'X' => IsoWeekYear(date),
            'x' => IsoMonth(date),
            'H' => date.Hour,
            'h' => Hour12(date),
            'P' => date.Hour >= 12 ? 1 : 0,
            'm' => date.Minute,
            's' => date.Second,
            'f' => date.Millisecond,
            _ => 0
        };
    }

    /// <summary>Computes the one-based day of the year of a UTC instant.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The day of the year.</returns>
    private static double DayOfYear(DateTime date)
    {
        long today = UtcMillis(date.Year, date.Month - 1, date.Day);
        long firstJan = UtcMillis(date.Year, 0, 1);

        return ((today - firstJan) / (double)MillisInADay) + 1;
    }

    /// <summary>Computes the ISO weekday of a UTC instant, Monday=1 through Sunday=7.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The ISO weekday.</returns>
    private static double IsoWeekday(DateTime date)
    {
        int day = (int)date.DayOfWeek;

        return day == 0 ? 7 : day;
    }

    /// <summary>Computes the twelve-hour hour of a UTC instant, with midnight and midday rendered as 12.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The twelve-hour hour.</returns>
    private static double Hour12(DateTime date)
    {
        int hour = date.Hour % 12;

        return hour == 0 ? 12 : hour;
    }

    /// <summary>Computes the ISO week-of-year of a UTC instant, accounting for the first/last partial week rolling into the neighbouring year.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The ISO week-of-year.</returns>
    private static double WeekOfYear(DateTime date)
    {
        YearMonth thisYear = new(date.Year, 0);
        long startOfFirstWeek = StartOfFirstWeek(thisYear);
        long today = UtcMillis(thisYear.Year, date.Month - 1, date.Day);
        double week = DeltaWeeks(startOfFirstWeek, today);
        if(week > 52)
        {
            long startNext = StartOfFirstWeek(thisYear.NextYear());
            if(today >= startNext)
            {
                week = 1;
            }
        }
        else if(week < 1)
        {
            long startPrev = StartOfFirstWeek(thisYear.PreviousYear());
            week = DeltaWeeks(startPrev, today);
        }

        return Math.Floor(week);
    }

    /// <summary>Computes the week-of-month of a UTC instant, accounting for the first/last partial week rolling into the neighbouring month.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The week-of-month.</returns>
    private static double WeekOfMonth(DateTime date)
    {
        YearMonth thisMonth = new(date.Year, date.Month - 1);
        long startOfFirstWeek = StartOfFirstWeek(thisMonth);
        long today = UtcMillis(thisMonth.Year, thisMonth.Month, date.Day);
        double week = DeltaWeeks(startOfFirstWeek, today);
        if(week > 4)
        {
            long startNext = StartOfFirstWeek(thisMonth.NextMonth());
            if(today >= startNext)
            {
                week = 1;
            }
        }
        else if(week < 1)
        {
            long startPrev = StartOfFirstWeek(thisMonth.PreviousMonth());
            week = DeltaWeeks(startPrev, today);
        }

        return Math.Floor(week);
    }

    /// <summary>Computes the ISO week-year of a UTC instant: the neighbouring year when the instant falls in the first/last partial week.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The ISO week-year.</returns>
    private static double IsoWeekYear(DateTime date)
    {
        YearMonth thisYear = new(date.Year, 0);
        long startIso = StartOfFirstWeek(thisYear);
        long endIso = StartOfFirstWeek(thisYear.NextYear());
        long now = UtcMillis(date.Year, date.Month - 1, date.Day, date.Hour, date.Minute, date.Second, date.Millisecond);

        return now < startIso ? thisYear.Year - 1 : now >= endIso ? thisYear.Year + 1 : thisYear.Year;
    }

    /// <summary>Computes the ISO month-of-week-year of a UTC instant (one-based): the neighbouring month when the instant falls in the first/last partial week.</summary>
    /// <param name="date">The UTC instant.</param>
    /// <returns>The one-based ISO month.</returns>
    private static double IsoMonth(DateTime date)
    {
        YearMonth thisMonth = new(date.Year, date.Month - 1);
        long startIso = StartOfFirstWeek(thisMonth);
        YearMonth nextMonth = thisMonth.NextMonth();
        long endIso = StartOfFirstWeek(nextMonth);
        long now = UtcMillis(date.Year, date.Month - 1, date.Day, date.Hour, date.Minute, date.Second, date.Millisecond);

        return now < startIso ? thisMonth.PreviousMonth().Month + 1 : now >= endIso ? nextMonth.Month + 1 : thisMonth.Month + 1;
    }

    /// <summary>
    /// Computes the epoch-millis of the start of the first ISO week of a year-month: the Monday of the week
    /// containing the first of the month, rolled to the following Monday when the first lands after Thursday.
    /// </summary>
    /// <param name="yearMonth">The year-month whose first ISO week start is computed.</param>
    /// <returns>The start-of-first-week epoch-millis.</returns>
    private static long StartOfFirstWeek(YearMonth yearMonth)
    {
        long firstOfMonth = UtcMillis(yearMonth.Year, yearMonth.Month, 1);
        int day = (int)DateTimeOffset.FromUnixTimeMilliseconds(firstOfMonth).UtcDateTime.DayOfWeek;
        if(day == 0)
        {
            day = 7;
        }

        return day > 4 ? firstOfMonth + ((8 - day) * MillisInADay) : firstOfMonth - ((day - 1) * MillisInADay);
    }

    /// <summary>Computes the one-based count of whole weeks between two epoch-millis instants.</summary>
    /// <param name="start">The start epoch-millis.</param>
    /// <param name="end">The end epoch-millis.</param>
    /// <returns>The one-based week delta.</returns>
    private static double DeltaWeeks(long start, long end)
    {
        return ((end - start) / (double)(MillisInADay * 7)) + 1;
    }

    /// <summary>Builds the per-component match parts of the parse regex from the analysed picture.</summary>
    /// <param name="spec">The analysed picture spec.</param>
    /// <returns>The match parts in picture order.</returns>
    /// <exception cref="JsonataErrorException">A name was requested for a non-name component (D3133).</exception>
    private static List<DateMatchPart> GenerateRegex(DateTimePictureSpec spec)
    {
        List<DateMatchPart> parts = [];
        foreach(DateTimePart part in spec.Parts)
        {
            parts.Add(part.IsLiteral ? DateMatchPart.Literal(part.Literal) : ComponentMatchPart(part));
        }

        return parts;
    }

    /// <summary>Builds the match part for a single component marker: the regex fragment and the parse delegate.</summary>
    /// <param name="part">The component part.</param>
    /// <returns>The match part.</returns>
    /// <exception cref="JsonataErrorException">A name was requested for a non-name component (D3133).</exception>
    private static DateMatchPart ComponentMatchPart(DateTimePart part)
    {
        char component = part.Component;
        if(component is 'Z' or 'z')
        {
            return DateMatchPart.Timezone(component, part.IntegerFormat);
        }

        if(component == 'f')
        {
            return DateMatchPart.Fraction(component);
        }

        if(part.HasIntegerFormat)
        {
            return DateMatchPart.Integer(component, part.IntegerFormat);
        }

        if(part.Names != NameCase.None)
        {
            return DateMatchPart.Name(component, part);
        }

        return DateMatchPart.Ignored(component);
    }

    /// <summary>
    /// Analyses a picture string into its literal and component parts. The walk is iterative; a <c>[[</c> or
    /// <c>]]</c> is the bracket literal, an unclosed <c>[</c> raises D3135, an unknown component raises D3132, and
    /// each numeric marker's integer format honours the width modifier and (for the year) the truncation width.
    /// </summary>
    /// <param name="picture">The picture string.</param>
    /// <returns>The analysed picture spec.</returns>
    /// <exception cref="JsonataErrorException">The picture has an unclosed marker (D3135) or an unknown component (D3132).</exception>
    private static DateTimePictureSpec Analyse(string picture)
    {
        List<DateTimePart> parts = [];
        int start = 0;
        int pos = 0;
        while(pos < picture.Length)
        {
            if(picture[pos] == '[')
            {
                if(pos + 1 < picture.Length && picture[pos + 1] == '[')
                {
                    AddLiteral(parts, picture, start, pos);
                    parts.Add(DateTimePart.LiteralPart("["));
                    pos += 2;
                    start = pos;
                    continue;
                }

                AddLiteral(parts, picture, start, pos);
                start = pos;
                pos = picture.IndexOf(']', start);
                if(pos == -1)
                {
                    throw new JsonataErrorException(WellKnownJsonataErrors.UnclosedDateMarker, null, "The $fromMillis picture string has no closing bracket for a marker.");
                }

                string marker = StripWhitespace(picture.Substring(start + 1, pos - (start + 1)));
                parts.Add(AnalyseMarker(marker, parts));
                start = pos + 1;
            }

            pos++;
        }

        AddLiteral(parts, picture, start, pos);

        return new DateTimePictureSpec(parts);
    }

    /// <summary>
    /// Analyses one marker body into a component part: the component letter, the optional width modifier, the
    /// presentation modifier (name case, ordinal, or integer format), and the year truncation width.
    /// </summary>
    /// <param name="marker">The whitespace-stripped marker body.</param>
    /// <param name="parts">The parts collected so far, so the preceding numeric part's parse width can be set.</param>
    /// <returns>The component part.</returns>
    /// <exception cref="JsonataErrorException">The component is unknown (D3132).</exception>
    private static DateTimePart AnalyseMarker(string marker, List<DateTimePart> parts)
    {
        char component = marker[0];
        int comma = marker.LastIndexOf(',');
        int minWidth = -1;
        int maxWidth = -1;
        string presMod;
        if(comma != -1)
        {
            string widthMod = marker[(comma + 1)..];
            int dash = widthMod.IndexOf('-', StringComparison.Ordinal);
            if(dash == -1)
            {
                minWidth = ParseWidth(widthMod);
            }
            else
            {
                minWidth = ParseWidth(widthMod[..dash]);
                maxWidth = ParseWidth(widthMod[(dash + 1)..]);
            }

            presMod = marker.Substring(1, comma - 1);
        }
        else
        {
            presMod = marker[1..];
        }

        NameCase names = NameCase.None;
        bool ordinal = false;
        string presentation1;
        char presentation2 = '\0';
        if(presMod.Length == 1)
        {
            presentation1 = presMod;
        }
        else if(presMod.Length > 1)
        {
            char lastChar = presMod[^1];
            if("atco".Contains(lastChar, StringComparison.Ordinal))
            {
                presentation2 = lastChar;
                if(lastChar == 'o')
                {
                    ordinal = true;
                }

                presentation1 = presMod[..^1];
            }
            else
            {
                presentation1 = presMod;
            }
        }
        else
        {
            presentation1 = DefaultPresentation(component);
        }

        if(presentation1.Length == 0)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.UnknownDateComponent, component.ToString(), "The $fromMillis picture string has an unknown component specifier.");
        }

        if(presentation1[0] == 'n')
        {
            names = NameCase.Lower;
        }
        else if(presentation1[0] == 'N')
        {
            names = presentation1.Length > 1 && presentation1[1] == 'n' ? NameCase.Title : NameCase.Upper;
        }

        DateTimePart part = DateTimePart.Marker(component, names, ordinal, presentation2, maxWidth);

        if(names == NameCase.None && "YMDdFWwXxHhmsf".Contains(component, StringComparison.Ordinal))
        {
            part = ApplyIntegerFormat(part, presentation1, presentation2, ordinal, minWidth, maxWidth, parts);
        }
        else if(component is 'Z' or 'z')
        {
            part = part.WithIntegerFormat(IntegerPictureFormatter.Analyse(presentation1));
        }

        return part;
    }

    /// <summary>
    /// Analyses and attaches a numeric marker's integer format: the picture (with any ordinal modifier), the
    /// width-modifier minimum raised onto the mandatory digits, the year truncation width, and the parse width of
    /// the directly-preceding numeric part.
    /// </summary>
    /// <param name="part">The component part being built.</param>
    /// <param name="presentation1">The primary presentation (the integer picture).</param>
    /// <param name="presentation2">The secondary presentation (the ordinal modifier when ordinal).</param>
    /// <param name="ordinal">Whether the ordinal modifier was present.</param>
    /// <param name="minWidth">The width-modifier minimum, or -1 when absent.</param>
    /// <param name="maxWidth">The width-modifier maximum, or -1 when absent.</param>
    /// <param name="parts">The parts collected so far, so the preceding numeric part's parse width can be set.</param>
    /// <returns>The component part with its integer format attached.</returns>
    private static DateTimePart ApplyIntegerFormat(DateTimePart part, string presentation1, char presentation2, bool ordinal, int minWidth, int maxWidth, List<DateTimePart> parts)
    {
        string integerPattern = ordinal ? presentation1 + ";" + presentation2 : presentation1;
        IntegerPicture format = IntegerPictureFormatter.Analyse(integerPattern);
        if(minWidth != -1)
        {
            format = format.WithMandatoryDigitsAtLeast(minWidth);
        }

        int yearDigits = part.YearDigits;
        if(part.Component == 'Y')
        {
            yearDigits = -1;
            if(maxWidth != -1)
            {
                yearDigits = maxWidth;
                format = format.WithMandatoryDigits(maxWidth);
            }
            else
            {
                int width = format.MandatoryDigits + format.OptionalDigits;
                if(width >= 2)
                {
                    yearDigits = width;
                }
            }
        }

        //The directly-preceding numeric part is given a fixed parse width so an adjacent numeric field such as
        //[H01][m01] matches a fixed run rather than a greedy one.
        if(parts.Count > 0 && parts[^1].HasIntegerFormat)
        {
            DateTimePart previous = parts[^1];
            parts[^1] = previous.WithIntegerFormat(previous.IntegerFormat.WithParseWidth(previous.IntegerFormat.MandatoryDigits));
        }

        return part.WithIntegerFormat(format).WithYearDigits(yearDigits);
    }

    /// <summary>Returns the default presentation modifier for a component, or the empty string for an unknown component.</summary>
    /// <param name="component">The component letter.</param>
    /// <returns>The default presentation modifier.</returns>
    private static string DefaultPresentation(char component)
    {
        return component switch
        {
            'Y' or 'M' or 'D' or 'd' or 'W' or 'w' or 'X' or 'x' or 'H' or 'h' or 'f' => "1",
            'F' or 'P' or 'C' or 'E' => "n",
            'm' or 's' => "01",
            'Z' or 'z' => "01:01",
            _ => ""
        };
    }

    /// <summary>Appends a literal part for the picture range, collapsing the escaped <c>]]</c> to a single bracket.</summary>
    /// <param name="parts">The parts being collected.</param>
    /// <param name="picture">The picture string.</param>
    /// <param name="start">The inclusive start of the literal range.</param>
    /// <param name="end">The exclusive end of the literal range.</param>
    private static void AddLiteral(List<DateTimePart> parts, string picture, int start, int end)
    {
        if(end > start)
        {
            string literal = picture.Substring(start, end - start).Replace("]]", "]", StringComparison.Ordinal);
            parts.Add(DateTimePart.LiteralPart(literal));
        }
    }

    /// <summary>Removes every whitespace character from a marker body, the reference's whitespace-stripping split/join.</summary>
    /// <param name="marker">The marker body.</param>
    /// <returns>The whitespace-stripped marker body.</returns>
    private static string StripWhitespace(string marker)
    {
        StringBuilder builder = new(marker.Length);
        foreach(char c in marker)
        {
            if(!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Parses a width-modifier value, treating an empty value or the wildcard <c>*</c> as unset (-1).</summary>
    /// <param name="widthMod">The width-modifier value.</param>
    /// <returns>The parsed width, or -1 when unset.</returns>
    private static int ParseWidth(string widthMod)
    {
        return widthMod.Length == 0 || widthMod == "*" ? -1 : int.Parse(widthMod, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the epoch-millis of a UTC date, mirroring <c>Date.UTC(year, month, day)</c> with overflow normalisation.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The zero-based month (may overflow either side).</param>
    /// <param name="day">The one-based day (may overflow either side).</param>
    /// <returns>The epoch-millis.</returns>
    private static long UtcMillis(int year, int month, int day)
    {
        return new DateTimeOffset(UtcDate(year, month, day), TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>Builds the epoch-millis of a UTC instant, mirroring <c>Date.UTC(...)</c> with overflow normalisation.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The zero-based month (may overflow either side).</param>
    /// <param name="day">The one-based day (may overflow either side).</param>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The second.</param>
    /// <param name="millisecond">The millisecond.</param>
    /// <returns>The epoch-millis.</returns>
    private static long UtcMillis(int year, int month, int day, int hour, int minute, int second, int millisecond)
    {
        return new DateTimeOffset(UtcDateTime(year, month, day, hour, minute, second, millisecond), TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>Builds a UTC date from a zero-based month and a one-based day, normalising overflow as <c>Date.UTC</c> does.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The zero-based month (may overflow either side).</param>
    /// <param name="day">The one-based day (may overflow either side).</param>
    /// <returns>The normalised UTC date.</returns>
    private static DateTime UtcDate(int year, int month, int day)
    {
        return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(month).AddDays(day - 1);
    }

    /// <summary>Builds a UTC instant from a zero-based month and a one-based day, normalising overflow as <c>Date.UTC</c> does.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The zero-based month (may overflow either side).</param>
    /// <param name="day">The one-based day (may overflow either side).</param>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The second.</param>
    /// <param name="millisecond">The millisecond.</param>
    /// <returns>The normalised UTC instant.</returns>
    private static DateTime UtcDateTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
    {
        return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(month)
            .AddDays(day - 1)
            .AddHours(hour)
            .AddMinutes(minute)
            .AddSeconds(second)
            .AddMilliseconds(millisecond);
    }

    /// <summary>Builds the D3136 inconsistent-components error.</summary>
    /// <returns>The error to throw.</returns>
    private static JsonataErrorException InconsistentComponents()
    {
        return new JsonataErrorException(WellKnownJsonataErrors.InconsistentDateComponents, null, "The $toMillis picture string specified an inconsistent or unsupported set of components.");
    }

    /// <summary>Builds the D3133 name-on-non-name-component error.</summary>
    /// <param name="component">The offending component letter.</param>
    /// <returns>The error to throw.</returns>
    private static JsonataErrorException NameOnNonNameComponent(char component)
    {
        return new JsonataErrorException(WellKnownJsonataErrors.DateNameNotSupported, component.ToString(), "The $fromMillis picture string requested a name for a component that has no name form.");
    }

    /// <summary>The letter case (or none) a named component renders in.</summary>
    private enum NameCase
    {
        /// <summary>The component is not a named component.</summary>
        None,

        /// <summary>Lower case.</summary>
        Lower,

        /// <summary>Title case (the table spelling).</summary>
        Title,

        /// <summary>Upper case.</summary>
        Upper
    }

    /// <summary>
    /// A year-month pair with explicit step methods, the iterative replacement for the reference's closure-based
    /// <c>yearMonth</c>. The month is zero-based; the step methods wrap across the year boundary.
    /// </summary>
    /// <param name="Year">The year.</param>
    /// <param name="Month">The zero-based month.</param>
    private readonly record struct YearMonth(int Year, int Month)
    {
        /// <summary>Returns the following month, wrapping into the next year past December.</summary>
        /// <returns>The following year-month.</returns>
        public YearMonth NextMonth()
        {
            return Month == 11 ? new YearMonth(Year + 1, 0) : new YearMonth(Year, Month + 1);
        }

        /// <summary>Returns the preceding month, wrapping into the previous year before January.</summary>
        /// <returns>The preceding year-month.</returns>
        public YearMonth PreviousMonth()
        {
            return Month == 0 ? new YearMonth(Year - 1, 11) : new YearMonth(Year, Month - 1);
        }

        /// <summary>Returns the same month of the following year.</summary>
        /// <returns>The next year's year-month.</returns>
        public YearMonth NextYear()
        {
            return new YearMonth(Year + 1, Month);
        }

        /// <summary>Returns the same month of the preceding year.</summary>
        /// <returns>The previous year's year-month.</returns>
        public YearMonth PreviousYear()
        {
            return new YearMonth(Year - 1, Month);
        }
    }

    /// <summary>The accumulated parsed components, keyed by the single-letter component code, carried in a mutable map.</summary>
    private sealed class DateComponents
    {
        /// <summary>The component values by component letter.</summary>
        private readonly Dictionary<char, double> values = [];

        /// <summary>Gets the count of components present.</summary>
        public int Count => values.Count;

        /// <summary>Records a component value.</summary>
        /// <param name="component">The component letter.</param>
        /// <param name="value">The component value.</param>
        public void Set(char component, double value)
        {
            values[component] = value;
        }

        /// <summary>Determines whether a component is present.</summary>
        /// <param name="component">The component letter.</param>
        /// <returns><see langword="true"/> when the component is present.</returns>
        public bool Has(char component)
        {
            return values.ContainsKey(component);
        }

        /// <summary>Gets a component value, or zero when the component is absent.</summary>
        /// <param name="component">The component letter.</param>
        /// <returns>The component value, or zero.</returns>
        public double Get(char component)
        {
            return values.TryGetValue(component, out double value) ? value : 0;
        }
    }

    /// <summary>The analysed picture: the ordered literal and component parts.</summary>
    /// <param name="Parts">The ordered parts.</param>
    private readonly record struct DateTimePictureSpec(IReadOnlyList<DateTimePart> Parts);

    /// <summary>
    /// One part of an analysed picture: either a literal run or a component marker. A component marker carries the
    /// component letter, the name case, the ordinal flag, the secondary presentation, the maximum name width, the
    /// numeric integer format, and the year truncation width.
    /// </summary>
    /// <param name="IsLiteral">Whether this part is a literal run rather than a component marker.</param>
    /// <param name="Literal">The literal text (for a literal part).</param>
    /// <param name="Component">The component letter (for a component part).</param>
    /// <param name="Names">The name case, or <see cref="NameCase.None"/> for a numeric component.</param>
    /// <param name="Ordinal">Whether ordinal rendering was selected.</param>
    /// <param name="Presentation2">The secondary presentation character, or the null character when none.</param>
    /// <param name="MaxWidth">The maximum name width, or -1 when unset.</param>
    /// <param name="HasIntegerFormat">Whether a numeric integer format is attached.</param>
    /// <param name="IntegerFormat">The numeric integer format (valid only when <paramref name="HasIntegerFormat"/>).</param>
    /// <param name="YearDigits">The year truncation width, or -1 when no truncation applies.</param>
    private readonly record struct DateTimePart(
        bool IsLiteral,
        string Literal,
        char Component,
        NameCase Names,
        bool Ordinal,
        char Presentation2,
        int MaxWidth,
        bool HasIntegerFormat,
        IntegerPicture IntegerFormat,
        int YearDigits)
    {
        /// <summary>Builds a literal part.</summary>
        /// <param name="literal">The literal text.</param>
        /// <returns>The literal part.</returns>
        public static DateTimePart LiteralPart(string literal)
        {
            return new DateTimePart(IsLiteral: true, literal, '\0', NameCase.None, Ordinal: false, '\0', -1, HasIntegerFormat: false, default, -1);
        }

        /// <summary>Builds a component marker part with no integer format yet attached.</summary>
        /// <param name="component">The component letter.</param>
        /// <param name="names">The name case.</param>
        /// <param name="ordinal">Whether ordinal rendering was selected.</param>
        /// <param name="presentation2">The secondary presentation character.</param>
        /// <param name="maxWidth">The maximum name width, or -1 when unset.</param>
        /// <returns>The component marker part.</returns>
        public static DateTimePart Marker(char component, NameCase names, bool ordinal, char presentation2, int maxWidth)
        {
            return new DateTimePart(IsLiteral: false, "", component, names, ordinal, presentation2, maxWidth, HasIntegerFormat: false, default, -1);
        }

        /// <summary>Re-projects the part with an integer format attached.</summary>
        /// <param name="format">The integer format.</param>
        /// <returns>The re-projected part.</returns>
        public DateTimePart WithIntegerFormat(IntegerPicture format)
        {
            return this with { HasIntegerFormat = true, IntegerFormat = format };
        }

        /// <summary>Re-projects the part with the year truncation width set.</summary>
        /// <param name="yearDigits">The year truncation width.</param>
        /// <returns>The re-projected part.</returns>
        public DateTimePart WithYearDigits(int yearDigits)
        {
            return this with { YearDigits = yearDigits };
        }
    }

    /// <summary>
    /// One part of the parse regex: a regex fragment and, for a component, a delegate that transforms a captured
    /// group back to its component value. A literal part has no parse delegate.
    /// </summary>
    /// <param name="Regex">The regex fragment.</param>
    /// <param name="Component">The component letter (for a component part).</param>
    /// <param name="HasParse">Whether a parse delegate is attached.</param>
    /// <param name="Parse">The parse delegate (valid only when <paramref name="HasParse"/>).</param>
    private readonly record struct DateMatchPart(
        string Regex,
        char Component,
        bool HasParse,
        DateComponentParseDelegate Parse)
    {
        /// <summary>Builds a literal match part with no parse delegate.</summary>
        /// <param name="literal">The literal text to match.</param>
        /// <returns>The literal match part.</returns>
        public static DateMatchPart Literal(string literal)
        {
            return new DateMatchPart(System.Text.RegularExpressions.Regex.Escape(literal), '\0', HasParse: false, NoParse);
        }

        /// <summary>Builds a numeric-component match part from its integer format.</summary>
        /// <param name="component">The component letter.</param>
        /// <param name="format">The integer format.</param>
        /// <returns>The numeric-component match part.</returns>
        public static DateMatchPart Integer(char component, IntegerPicture format)
        {
            return new DateMatchPart(IntegerPictureFormatter.IntegerRegex(format), component, HasParse: true, new IntegerComponentParse(format).Parse);
        }

        /// <summary>Builds a fractional-seconds match part: a digit run truncated to three places and scaled to milliseconds.</summary>
        /// <param name="component">The component letter.</param>
        /// <returns>The fractional-seconds match part.</returns>
        public static DateMatchPart Fraction(char component)
        {
            return new DateMatchPart("[0-9]+", component, HasParse: true, ParseFraction);
        }

        /// <summary>Builds a timezone match part: a signed offset, optionally separated, parsed to total minutes.</summary>
        /// <param name="component">The component letter.</param>
        /// <param name="format">The integer format of the timezone presentation.</param>
        /// <returns>The timezone match part.</returns>
        public static DateMatchPart Timezone(char component, IntegerPicture format)
        {
            string separator = format.RegularSeparator;
            string prefix = component == 'z' ? "GMT" : "";
            string regex = prefix + "[-+][0-9]+" + (separator.Length > 0 ? System.Text.RegularExpressions.Regex.Escape(separator) + "[0-9]+" : "");

            return new DateMatchPart(regex, component, HasParse: true, new TimezoneComponentParse(component, separator).Parse);
        }

        /// <summary>Builds a named-component match part: a run of letters looked up in the month/weekday/am-pm tables.</summary>
        /// <param name="component">The component letter.</param>
        /// <param name="part">The component part carrying the maximum width.</param>
        /// <returns>The named-component match part.</returns>
        /// <exception cref="JsonataErrorException">The component does not support names (D3133).</exception>
        public static DateMatchPart Name(char component, DateTimePart part)
        {
            if(component is not ('M' or 'x' or 'F' or 'P'))
            {
                throw NameOnNonNameComponent(component);
            }

            return new DateMatchPart("[a-zA-Z]+", component, HasParse: true, new NameComponentParse(component, part.MaxWidth).Parse);
        }

        /// <summary>Builds an ignored-component match part: it matches but contributes no component value.</summary>
        /// <param name="component">The component letter.</param>
        /// <returns>The ignored-component match part.</returns>
        public static DateMatchPart Ignored(char component)
        {
            return new DateMatchPart("[0-9]+", component, HasParse: false, NoParse);
        }

        /// <summary>The no-op parse delegate for a literal or ignored part; it is never invoked.</summary>
        /// <param name="value">The captured value.</param>
        /// <returns>Zero.</returns>
        private static double NoParse(string value)
        {
            return 0;
        }

        /// <summary>Parses a fractional-seconds capture: the first three digits as the fraction of a second scaled to milliseconds.</summary>
        /// <param name="value">The captured digit run.</param>
        /// <returns>The milliseconds value.</returns>
        private static double ParseFraction(string value)
        {
            string head = value.Length > 3 ? value[..3] : value;

            return double.Parse("0." + head, NumberStyles.Float, CultureInfo.InvariantCulture) * 1000;
        }
    }

    /// <summary>The signature of a parse delegate that transforms a captured regex group back to its numeric component value.</summary>
    /// <param name="value">The captured group value.</param>
    /// <returns>The numeric component value.</returns>
    private delegate double DateComponentParseDelegate(string value);

    /// <summary>The numeric-component parse: it transforms a captured value through the integer formatter's spec-based parse.</summary>
    /// <param name="Format">The integer format the capture was rendered against.</param>
    private readonly record struct IntegerComponentParse(IntegerPicture Format)
    {
        /// <summary>Parses a captured numeric value through the integer formatter.</summary>
        /// <param name="value">The captured value.</param>
        /// <returns>The numeric component value.</returns>
        public double Parse(string value)
        {
            return IntegerPictureFormatter.ParseFromSpec(value, Format);
        }
    }

    /// <summary>The timezone-component parse: it transforms a captured signed offset to total minutes.</summary>
    /// <param name="Component">The timezone component letter.</param>
    /// <param name="Separator">The hour/minute separator, or the empty string when the offset is contiguous.</param>
    private readonly record struct TimezoneComponentParse(char Component, string Separator)
    {
        /// <summary>Parses a captured timezone offset to total minutes.</summary>
        /// <param name="value">The captured offset.</param>
        /// <returns>The total minutes of the offset.</returns>
        public double Parse(string value)
        {
            string text = value;
            if(Component == 'z' && text.StartsWith("GMT", StringComparison.OrdinalIgnoreCase))
            {
                text = text[3..];
            }

            int offsetHours;
            int offsetMinutes;
            if(Separator.Length > 0)
            {
                int separatorIndex = text.IndexOf(Separator, StringComparison.Ordinal);
                offsetHours = int.Parse(text[..separatorIndex], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                offsetMinutes = int.Parse(text[(separatorIndex + Separator.Length)..], NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            else
            {
                //The digit count excludes the leading sign; up to two digits are hours only, otherwise the
                //signed first three characters are the hours and the remainder the minutes (the reference's split).
                int digitCount = text.Length - 1;
                if(digitCount <= 2)
                {
                    offsetHours = int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    offsetMinutes = 0;
                }
                else
                {
                    offsetHours = int.Parse(text[..3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    offsetMinutes = int.Parse(text[3..], NumberStyles.Integer, CultureInfo.InvariantCulture);
                }
            }

            return (offsetHours * 60) + offsetMinutes;
        }
    }

    /// <summary>The named-component parse: it looks a captured name up in the month/weekday/am-pm tables to its numeric value.</summary>
    /// <param name="Component">The named component letter.</param>
    /// <param name="MaxWidth">The maximum name width the table entries were truncated to, or -1 when untruncated.</param>
    private readonly record struct NameComponentParse(char Component, int MaxWidth)
    {
        /// <summary>Parses a captured name to its numeric component value.</summary>
        /// <param name="value">The captured name.</param>
        /// <returns>The numeric component value.</returns>
        public double Parse(string value)
        {
            if(Component == 'P')
            {
                return string.Equals(value, "pm", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }

            if(Component == 'F')
            {
                for(int i = 1; i < DayNames.Length; i++)
                {
                    if(Matches(DayNames[i], value))
                    {
                        return i;
                    }
                }

                return 0;
            }

            for(int i = 0; i < MonthNames.Length; i++)
            {
                if(Matches(MonthNames[i], value))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        /// <summary>Determines whether a captured name matches a table entry case-insensitively, honouring the width truncation.</summary>
        /// <param name="entry">The table entry (full name).</param>
        /// <param name="value">The captured name.</param>
        /// <returns><see langword="true"/> when the captured name matches the entry.</returns>
        private bool Matches(string entry, string value)
        {
            string candidate = MaxWidth > 0 && entry.Length > MaxWidth ? entry[..MaxWidth] : entry;

            return string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
