using System;

namespace Lumoin.Veritas.Rdf.Values;

/// <summary>
/// Parsed representation of an <c>xsd:dateTime</c>, <c>xsd:date</c>,
/// or <c>xsd:time</c> literal, carrying either an explicit timezone
/// offset or a "naive" flag indicating no timezone was present.
/// </summary>
/// <remarks>
/// <para>
/// The value is held as a wall-clock position on the proleptic
/// Gregorian axis — <see cref="AbsoluteDay"/> (days since 1970-01-01)
/// plus <see cref="NanosecondOfDay"/> — together with the optional
/// <see cref="Offset"/>. The wall-clock form represents the full
/// XSD 1.1 value space, including year 0000 and negative years,
/// which a <see cref="DateTime"/> cannot hold.
/// </para>
/// <para>
/// XSD §3.2.7.4 specifies that comparison of a timezone-aware value
/// with a timezone-naive value is <em>indeterminate</em> when the
/// two could occupy the same instant under any timezone within the
/// ±14h envelope. The <see cref="Compare(DateTimeValue, DateTimeValue)"/>
/// overload returns <see cref="ComparisonResult.Incomparable"/> in that
/// case. The <see cref="Compare(DateTimeValue, DateTimeValue, TimeSpan)"/>
/// overload instead totalizes the axis by normalizing naive operands
/// with the caller's implicit timezone, the SPARQL §17.3 /
/// XPath&#160;F&amp;O reading in which no indeterminate verdict exists.
/// </para>
/// <para>
/// The lexical-form parsers accept the XSD 1.1 lexical space:
/// negative years, year 0000 (with <c>-0000</c> rejected), years
/// beyond four digits (up to <see cref="MaxAbsoluteYear"/>, the
/// engine's representable window), the <c>24:00:00</c> end-of-day
/// form (normalized to <c>00:00:00</c> of the following day, or of
/// the same day for <c>xsd:time</c>), and fractional seconds, of
/// which the first nine digits (nanosecond precision) are
/// significant and further digits are truncated. They reject
/// whitespace, missing required components, out-of-range fields,
/// and non-canonical sign forms.
/// </para>
/// <para>
/// <c>xsd:dateTimeStamp</c> is <c>xsd:dateTime</c> with a required
/// timezone; the parser, given the <c>dateTimeStamp</c> datatype
/// IRI, fails on naive input.
/// </para>
/// </remarks>
public readonly struct DateTimeValue: IEquatable<DateTimeValue>
{
    /// <summary>The wall-clock day as proleptic Gregorian days since 1970-01-01 (day 0); for an <c>xsd:time</c> value this is the shared reference day 0.</summary>
    public long AbsoluteDay { get; }

    /// <summary>The wall-clock time of day in nanoseconds, in [0, 86 400 000 000 000).</summary>
    public long NanosecondOfDay { get; }

    /// <summary>The original timezone offset; <c>null</c> when the value was naive.</summary>
    public TimeSpan? Offset { get; }

    /// <summary>Whether the value was parsed with a timezone offset.</summary>
    public bool IsAware => Offset.HasValue;

    /// <summary>Constructs a value from its wall-clock fields.</summary>
    /// <param name="absoluteDay">The wall-clock day since 1970-01-01.</param>
    /// <param name="nanosecondOfDay">The wall-clock time of day in nanoseconds.</param>
    /// <param name="offset">The timezone offset, or <see langword="null"/> for a naive value.</param>
    private DateTimeValue(long absoluteDay, long nanosecondOfDay, TimeSpan? offset)
    {
        AbsoluteDay = absoluteDay;
        NanosecondOfDay = nanosecondOfDay;
        Offset = offset;
    }

    /// <summary>Whether this value equals another field for field — the same wall-clock position and the same timezone offset (or shared absence of one). Value-space ordering, which is what the datatype semantics compares by, is <see cref="Compare(DateTimeValue, DateTimeValue)"/>; two lexical forms of one instant with different offsets are field-unequal yet value-equal.</summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true"/> when the wall-clock fields and offset match.</returns>
    public bool Equals(DateTimeValue other)
    {
        return AbsoluteDay == other.AbsoluteDay && NanosecondOfDay == other.NanosecondOfDay && Offset == other.Offset;
    }

    /// <summary>Whether this value equals another boxed value.</summary>
    /// <param name="obj">The boxed candidate.</param>
    /// <returns><see langword="true"/> when the candidate is a field-equal <see cref="DateTimeValue"/>.</returns>
    public override bool Equals(object? obj)
    {
        return obj is DateTimeValue other && Equals(other);
    }

    /// <summary>The hash over the wall-clock fields and offset.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(AbsoluteDay, NanosecondOfDay, Offset);
    }

    /// <summary>Whether two values are equal field for field.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values match.</returns>
    public static bool operator ==(DateTimeValue left, DateTimeValue right)
    {
        return left.Equals(right);
    }

    /// <summary>Whether two values differ field for field.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(DateTimeValue left, DateTimeValue right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Parses an <c>xsd:dateTime</c> lexical form. Accepts both naive
    /// (no timezone) and aware (Z or ±HH:mm) forms. When
    /// <paramref name="requireTimezone"/> is <c>true</c> (the
    /// <c>xsd:dateTimeStamp</c> case), naive forms fail.
    /// </summary>
    /// <param name="lexicalForm">The UTF-8 lexical form.</param>
    /// <param name="requireTimezone">Whether a timezone is mandatory.</param>
    /// <param name="value">Receives the parsed value on success.</param>
    /// <returns><see langword="true"/> when the form is valid.</returns>
    public static bool TryParseDateTime(ReadOnlySpan<byte> lexicalForm, bool requireTimezone, out DateTimeValue value)
    {
        value = default;
        int position = 0;
        if(!TryParseYear(lexicalForm, ref position, out long year)
            || !TryExpect(lexicalForm, ref position, (byte)'-')
            || !TryParseTwoDigits(lexicalForm, ref position, out int month)
            || !TryExpect(lexicalForm, ref position, (byte)'-')
            || !TryParseTwoDigits(lexicalForm, ref position, out int day)
            || !TryExpect(lexicalForm, ref position, (byte)'T')
            || !TryParseTimeOfDay(lexicalForm, ref position, out long nanosecondOfDay, out int dayCarry)
            || !TryParseTimezone(lexicalForm, ref position, out TimeSpan? offset)
            || position != lexicalForm.Length
            || !IsValidDate(year, month, day)
            || (requireTimezone && offset is null))
        {
            return false;
        }

        value = new DateTimeValue(DaysFromCivil(year, month, day) + dayCarry, nanosecondOfDay, offset);

        return true;
    }

    /// <summary>
    /// Parses an <c>xsd:date</c> lexical form. Same naive/aware
    /// distinction as the <c>dateTime</c> parser; the time component
    /// is fixed at <c>00:00:00</c>, so the value orders by the day's
    /// starting instant per XPath <c>op:date-less-than</c>.
    /// </summary>
    /// <param name="lexicalForm">The UTF-8 lexical form.</param>
    /// <param name="value">Receives the parsed value on success.</param>
    /// <returns><see langword="true"/> when the form is valid.</returns>
    public static bool TryParseDate(ReadOnlySpan<byte> lexicalForm, out DateTimeValue value)
    {
        value = default;
        int position = 0;
        if(!TryParseYear(lexicalForm, ref position, out long year)
            || !TryExpect(lexicalForm, ref position, (byte)'-')
            || !TryParseTwoDigits(lexicalForm, ref position, out int month)
            || !TryExpect(lexicalForm, ref position, (byte)'-')
            || !TryParseTwoDigits(lexicalForm, ref position, out int day)
            || !TryParseTimezone(lexicalForm, ref position, out TimeSpan? offset)
            || position != lexicalForm.Length
            || !IsValidDate(year, month, day))
        {
            return false;
        }

        value = new DateTimeValue(DaysFromCivil(year, month, day), nanosecondOfDay: 0, offset);

        return true;
    }

    /// <summary>
    /// Parses an <c>xsd:time</c> lexical form: a time of day with an
    /// optional timezone, held on the shared reference day 0 so any
    /// two <c>xsd:time</c> values compare on the same axis. The
    /// <c>24:00:00</c> form denotes the same value as
    /// <c>00:00:00</c> per XSD 1.1.
    /// </summary>
    /// <param name="lexicalForm">The UTF-8 lexical form.</param>
    /// <param name="value">Receives the parsed value on success.</param>
    /// <returns><see langword="true"/> when the form is valid.</returns>
    public static bool TryParseTime(ReadOnlySpan<byte> lexicalForm, out DateTimeValue value)
    {
        value = default;
        int position = 0;
        if(!TryParseTimeOfDay(lexicalForm, ref position, out long nanosecondOfDay, out _)
            || !TryParseTimezone(lexicalForm, ref position, out TimeSpan? offset)
            || position != lexicalForm.Length)
        {
            return false;
        }

        //The day carry from 24:00:00 is discarded: for xsd:time the end-of-day form maps to 00:00:00 of the
        //same (reference) day, not the next one.
        value = new DateTimeValue(absoluteDay: 0, nanosecondOfDay, offset);

        return true;
    }

    /// <summary>
    /// Compares two parsed values under the XSD partial order.
    /// Returns <see cref="ComparisonResult.Incomparable"/> when one
    /// operand is timezone-naive and the other timezone-aware and the
    /// indeterminate window (per XSD §3.2.7.4) covers the comparison.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns>The comparison verdict.</returns>
    public static ComparisonResult Compare(DateTimeValue left, DateTimeValue right)
    {
        if(left.IsAware == right.IsAware)
        {
            //Both aware: compare the offset-normalized instants. Both naive: compare the wall clocks directly,
            //which is the same arithmetic with a zero adjustment on each side.
            (long leftDay, long leftNanos) = left.ToInstant(TimeSpan.Zero);
            (long rightDay, long rightNanos) = right.ToInstant(TimeSpan.Zero);

            return SignToResult(CompareInstants(leftDay, leftNanos, rightDay, rightNanos));
        }

        //One naive, one aware: apply the XSD §3.2.7.4 indeterminate-comparison algorithm. The naive value's
        //possible instants span [wall - 14h, wall + 14h]; only a strict separation from the aware instant
        //decides the comparison.
        DateTimeValue naive = left.IsAware ? right : left;
        DateTimeValue aware = left.IsAware ? left : right;
        (long awareDay, long awareNanos) = aware.ToInstant(TimeSpan.Zero);
        (long naiveLowDay, long naiveLowNanos) = naive.ToInstant(MaxTimezoneOffset);
        (long naiveHighDay, long naiveHighNanos) = naive.ToInstant(-MaxTimezoneOffset);

        ComparisonResult naiveOnLeft = CompareInstants(naiveHighDay, naiveHighNanos, awareDay, awareNanos) < 0
            ? ComparisonResult.Less
            : CompareInstants(naiveLowDay, naiveLowNanos, awareDay, awareNanos) > 0
                ? ComparisonResult.Greater
                : ComparisonResult.Incomparable;

        return left.IsAware ? Flip(naiveOnLeft) : naiveOnLeft;
    }

    /// <summary>
    /// Compares two parsed values on the totalized axis: a naive
    /// operand is normalized with <paramref name="implicitTimezone"/>
    /// (the SPARQL §17.3 / XPath F&amp;O implicit-timezone reading),
    /// so the comparison always yields an order verdict — never
    /// <see cref="ComparisonResult.Incomparable"/>. This is the one
    /// shared normalization routine every SPARQL ordering consumer
    /// uses, so the evaluator and any value index agree on the axis.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="implicitTimezone">The implicit timezone applied to naive operands.</param>
    /// <returns>The comparison verdict: less, equal, or greater.</returns>
    public static ComparisonResult Compare(DateTimeValue left, DateTimeValue right, TimeSpan implicitTimezone)
    {
        (long leftDay, long leftNanos) = left.ToInstant(implicitTimezone);
        (long rightDay, long rightNanos) = right.ToInstant(implicitTimezone);

        return SignToResult(CompareInstants(leftDay, leftNanos, rightDay, rightNanos));
    }

    /// <summary>The largest absolute year the parsers accept — the engine's representable window on the proleptic axis, chosen so day arithmetic stays inside <see cref="long"/> with headroom.</summary>
    public static long MaxAbsoluteYear => 999_999_999_999;

    /// <summary>Nanoseconds in a day.</summary>
    private const long NanosecondsPerDay = 86_400_000_000_000;

    /// <summary>Nanoseconds in a second.</summary>
    private const long NanosecondsPerSecond = 1_000_000_000;

    /// <summary>
    /// Converts the wall-clock fields to a normalized <see cref="TimelineInstant"/>, applying the explicit
    /// offset when present and <paramref name="implicitTimezone"/> otherwise. This is the ONE shared
    /// normalization routine: the SPARQL evaluator's totalized comparisons and every temporal value index
    /// derive their axis positions from it, so a probe and a scan can never disagree.
    /// </summary>
    /// <param name="implicitTimezone">The offset assumed for a naive value.</param>
    /// <returns>The normalized instant.</returns>
    public TimelineInstant ToInstant(TimeSpan implicitTimezone)
    {
        //An offset magnitude is at most 14 hours, so the borrow/carry below moves at most one day.
        long offsetNanos = (Offset ?? implicitTimezone).Ticks * 100;
        long nanos = NanosecondOfDay - offsetNanos;
        long day = AbsoluteDay;
        if(nanos < 0)
        {
            nanos += NanosecondsPerDay;
            day--;
        }
        else if(nanos >= NanosecondsPerDay)
        {
            nanos -= NanosecondsPerDay;
            day++;
        }

        return new TimelineInstant(day, nanos);
    }

    /// <summary>Compares two normalized instants: days first, then nanoseconds within the day.</summary>
    /// <param name="leftDay">The left instant's day.</param>
    /// <param name="leftNanos">The left instant's nanosecond of day.</param>
    /// <param name="rightDay">The right instant's day.</param>
    /// <param name="rightNanos">The right instant's nanosecond of day.</param>
    /// <returns>A negative, zero, or positive value as the left instant is earlier, simultaneous, or later.</returns>
    private static int CompareInstants(long leftDay, long leftNanos, long rightDay, long rightNanos)
    {
        int byDay = leftDay.CompareTo(rightDay);

        return byDay != 0 ? byDay : leftNanos.CompareTo(rightNanos);
    }

    /// <summary>Maps a flipped operand order back to the caller's orientation.</summary>
    /// <param name="original">The verdict computed with the operands swapped.</param>
    /// <returns>The verdict in the caller's orientation.</returns>
    private static ComparisonResult Flip(ComparisonResult original)
        => original switch
        {
            ComparisonResult.Less => ComparisonResult.Greater,
            ComparisonResult.Greater => ComparisonResult.Less,
            _ => original,
        };

    /// <summary>Maps a comparison sign to a <see cref="ComparisonResult"/>.</summary>
    /// <param name="sign">The comparison sign.</param>
    /// <returns>The corresponding verdict.</returns>
    private static ComparisonResult SignToResult(int sign)
        => sign switch
        {
            < 0 => ComparisonResult.Less,
            > 0 => ComparisonResult.Greater,
            _ => ComparisonResult.Equal,
        };

    /// <summary>Maximum timezone offset is 14 hours per XSD §3.2.7.3.</summary>
    private static TimeSpan MaxTimezoneOffset { get; } = TimeSpan.FromHours(14);

    /// <summary>
    /// Parses the XSD year field at <paramref name="position"/>: an
    /// optional leading minus, then four or more digits, with a
    /// leading zero prohibited beyond four digits, <c>-0000</c>
    /// rejected, and the magnitude capped at
    /// <see cref="MaxAbsoluteYear"/>.
    /// </summary>
    /// <param name="lexicalForm">The full lexical form.</param>
    /// <param name="position">The cursor, advanced past the year on success.</param>
    /// <param name="year">Receives the signed year.</param>
    /// <returns><see langword="true"/> when a valid year field is present.</returns>
    private static bool TryParseYear(ReadOnlySpan<byte> lexicalForm, ref int position, out long year)
    {
        year = 0;
        bool negative = position < lexicalForm.Length && lexicalForm[position] == (byte)'-';
        if(negative)
        {
            position++;
        }

        int digitStart = position;
        long magnitude = 0;
        while(position < lexicalForm.Length && IsDigit(lexicalForm[position]))
        {
            magnitude = magnitude * 10 + (lexicalForm[position] - (byte)'0');
            position++;
            if(magnitude > MaxAbsoluteYear)
            {
                return false;
            }
        }

        int digitCount = position - digitStart;
        if(digitCount < 4
            || (digitCount > 4 && lexicalForm[digitStart] == (byte)'0')
            || (negative && magnitude == 0))
        {
            return false;
        }

        year = negative ? -magnitude : magnitude;

        return true;
    }

    /// <summary>Parses the <c>hh:mm:ss[.fraction]</c> time-of-day fields, honouring the XSD 1.1 <c>24:00:00</c> end-of-day form via <paramref name="dayCarry"/>; fractional digits beyond nanosecond precision are truncated.</summary>
    /// <param name="lexicalForm">The full lexical form.</param>
    /// <param name="position">The cursor, advanced past the fields on success.</param>
    /// <param name="nanosecondOfDay">Receives the time of day in nanoseconds.</param>
    /// <param name="dayCarry">Receives 1 when the <c>24:00:00</c> form rolled the value to the next day, else 0.</param>
    /// <returns><see langword="true"/> when valid time-of-day fields are present.</returns>
    private static bool TryParseTimeOfDay(ReadOnlySpan<byte> lexicalForm, ref int position, out long nanosecondOfDay, out int dayCarry)
    {
        nanosecondOfDay = 0;
        dayCarry = 0;
        if(!TryParseTwoDigits(lexicalForm, ref position, out int hour)
            || !TryExpect(lexicalForm, ref position, (byte)':')
            || !TryParseTwoDigits(lexicalForm, ref position, out int minute)
            || !TryExpect(lexicalForm, ref position, (byte)':')
            || !TryParseTwoDigits(lexicalForm, ref position, out int second))
        {
            return false;
        }

        long fractionNanos = 0;
        if(position < lexicalForm.Length && lexicalForm[position] == (byte)'.')
        {
            position++;
            int fractionStart = position;
            long scale = NanosecondsPerSecond / 10;
            while(position < lexicalForm.Length && IsDigit(lexicalForm[position]))
            {
                fractionNanos += scale * (lexicalForm[position] - (byte)'0');
                scale /= 10;
                position++;
            }

            if(position == fractionStart)
            {
                return false;
            }
        }

        if(minute > 59 || second > 59)
        {
            return false;
        }

        if(hour == 24)
        {
            //XSD 1.1 end-of-day: exactly 24:00:00 with a zero fraction, denoting 00:00:00 of the next day.
            if(minute != 0 || second != 0 || fractionNanos != 0)
            {
                return false;
            }

            dayCarry = 1;

            return true;
        }

        if(hour > 23)
        {
            return false;
        }

        nanosecondOfDay = (hour * 3600L + minute * 60L + second) * NanosecondsPerSecond + fractionNanos;

        return true;
    }

    /// <summary>Parses the optional timezone field: <c>Z</c> or <c>±hh:mm</c> with the magnitude capped at 14:00 per XSD §3.2.7.3.</summary>
    /// <param name="lexicalForm">The full lexical form.</param>
    /// <param name="position">The cursor, advanced past the field when one is present.</param>
    /// <param name="offset">Receives the offset, or <see langword="null"/> when no timezone field is present.</param>
    /// <returns><see langword="true"/> when the field is absent or valid.</returns>
    private static bool TryParseTimezone(ReadOnlySpan<byte> lexicalForm, ref int position, out TimeSpan? offset)
    {
        offset = null;
        if(position >= lexicalForm.Length)
        {
            return true;
        }

        byte marker = lexicalForm[position];
        if(marker == (byte)'Z')
        {
            position++;
            offset = TimeSpan.Zero;

            return true;
        }

        if(marker is not ((byte)'+' or (byte)'-'))
        {
            //Not a timezone field; the caller's end-of-input check rejects any trailing garbage.
            return true;
        }

        position++;
        if(!TryParseTwoDigits(lexicalForm, ref position, out int hours)
            || !TryExpect(lexicalForm, ref position, (byte)':')
            || !TryParseTwoDigits(lexicalForm, ref position, out int minutes)
            || minutes > 59
            || hours > 14
            || (hours == 14 && minutes != 0))
        {
            return false;
        }

        int totalMinutes = hours * 60 + minutes;
        offset = TimeSpan.FromMinutes(marker == (byte)'-' ? -totalMinutes : totalMinutes);

        return true;
    }

    /// <summary>Parses exactly two ASCII digits at <paramref name="position"/>.</summary>
    /// <param name="lexicalForm">The full lexical form.</param>
    /// <param name="position">The cursor, advanced past the digits on success.</param>
    /// <param name="value">Receives the two-digit value.</param>
    /// <returns><see langword="true"/> when two digits are present.</returns>
    private static bool TryParseTwoDigits(ReadOnlySpan<byte> lexicalForm, ref int position, out int value)
    {
        value = 0;
        if(position + 1 >= lexicalForm.Length || !IsDigit(lexicalForm[position]) || !IsDigit(lexicalForm[position + 1]))
        {
            return false;
        }

        value = (lexicalForm[position] - (byte)'0') * 10 + (lexicalForm[position + 1] - (byte)'0');
        position += 2;

        return true;
    }

    /// <summary>Consumes one expected byte at <paramref name="position"/>.</summary>
    /// <param name="lexicalForm">The full lexical form.</param>
    /// <param name="position">The cursor, advanced past the byte on success.</param>
    /// <param name="expected">The required byte.</param>
    /// <returns><see langword="true"/> when the byte is present.</returns>
    private static bool TryExpect(ReadOnlySpan<byte> lexicalForm, ref int position, byte expected)
    {
        if(position >= lexicalForm.Length || lexicalForm[position] != expected)
        {
            return false;
        }

        position++;

        return true;
    }

    /// <summary>Whether the byte is an ASCII digit.</summary>
    /// <param name="candidate">The byte to test.</param>
    /// <returns><see langword="true"/> for <c>0</c>–<c>9</c>.</returns>
    private static bool IsDigit(byte candidate)
    {
        return candidate is >= (byte)'0' and <= (byte)'9';
    }

    /// <summary>Whether the year/month/day triple denotes a real proleptic Gregorian date.</summary>
    /// <param name="year">The signed year.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day of month.</param>
    /// <returns><see langword="true"/> when the date exists.</returns>
    private static bool IsValidDate(long year, int month, int day)
    {
        if(month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        int daysInMonth = month switch
        {
            1 or 3 or 5 or 7 or 8 or 10 or 12 => 31,
            4 or 6 or 9 or 11 => 30,
            _ => IsLeapYear(year) ? 29 : 28,
        };

        return day <= daysInMonth;
    }

    /// <summary>Whether the proleptic Gregorian year is a leap year; the remainder tests are sign-safe because they only compare against zero.</summary>
    /// <param name="year">The signed year.</param>
    /// <returns><see langword="true"/> for a leap year.</returns>
    private static bool IsLeapYear(long year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }

    /// <summary>Computes proleptic Gregorian days since 1970-01-01 from a civil date (the standard era/day-of-era decomposition, exact over the whole signed range).</summary>
    /// <param name="year">The signed year.</param>
    /// <param name="month">The month.</param>
    /// <param name="day">The day of month.</param>
    /// <returns>The day count since 1970-01-01.</returns>
    private static long DaysFromCivil(long year, int month, int day)
    {
        long shiftedYear = month <= 2 ? year - 1 : year;
        long era = (shiftedYear >= 0 ? shiftedYear : shiftedYear - 399) / 400;
        long yearOfEra = shiftedYear - era * 400;
        long dayOfYear = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
        long dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;

        return era * 146097 + dayOfEra - 719468;
    }
}
