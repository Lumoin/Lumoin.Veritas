using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lumoin.Veritas.Jsonata.Formatting;

/// <summary>
/// Formats and parses an integer against an XPath <c>fn:format-integer</c> picture string for the
/// <c>$formatInteger</c> and <c>$parseInteger</c> built-ins. This is the integer-picture member of the reusable
/// <c>Formatting</c> unit the numeric built-ins consume; it analyses a picture once into an
/// <see cref="IntegerPicture"/> spec and then renders a value to a string or transforms a string back to a value.
/// </summary>
/// <remarks>
/// <para>
/// A picture selects one of five primary families: decimal digits (with optional grouping separators and a
/// custom Unicode digit family), bijective base-26 spreadsheet-column letters (<c>A</c>/<c>a</c>), Roman
/// numerals (<c>I</c>/<c>i</c>), English number words (<c>w</c>/<c>W</c>/<c>Ww</c>), or an unsupported sequence
/// (<c>D3130</c>). A trailing <c>;o</c> modifier selects ordinal rendering. The decimal family analysis runs the
/// digits least-significant-first and tests grouping regularity through an iterative Euclid loop, so the
/// analysis never recurses; the numeral families delegate to <see cref="IntegerNumerals"/>.
/// </para>
/// <para>
/// All magnitude arithmetic is carried in <see cref="double"/>, matching the reference engine's number model.
/// Picture characters are read by Unicode rune, faithful to surrogate handling, although every supported digit
/// family lives in the Basic Multilingual Plane.
/// </para>
/// </remarks>
internal static class IntegerPictureFormatter
{
    /// <summary>The base codepoints of the supported decimal digit families, each spanning ten consecutive codepoints.</summary>
    private static readonly int[] DecimalGroups =
    [
        0x30, 0x0660, 0x06F0, 0x07C0, 0x0966, 0x09E6, 0x0A66, 0x0AE6, 0x0B66, 0x0BE6, 0x0C66, 0x0CE6,
        0x0D66, 0x0DE6, 0x0E50, 0x0ED0, 0x0F20, 0x1040, 0x1090, 0x17E0, 0x1810, 0x1946, 0x19D0, 0x1A80,
        0x1A90, 0x1B50, 0x1BB0, 0x1C40, 0x1C50, 0xA620, 0xA8D0, 0xA900, 0xA9D0, 0xA9F0, 0xAA50, 0xABF0, 0xFF10
    ];

    /// <summary>The ASCII zero codepoint, the base of the default decimal digit family.</summary>
    private const int AsciiZero = 0x30;

    /// <summary>The optional-digit codepoint (<c>#</c>).</summary>
    private const int OptionalDigit = 0x23;

    /// <summary>Formats a value against a picture, flooring the value toward negative infinity before rendering.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="picture">The integer picture string.</param>
    /// <returns>The formatted string.</returns>
    /// <exception cref="JsonataErrorException">The picture is an unsupported sequence (D3130) or mixes digit families (D3131).</exception>
    public static string Format(double value, string picture)
    {
        IntegerPicture format = Analyse(picture);

        return FormatInteger(Math.Floor(value), format);
    }

    /// <summary>Parses a string against a picture, transforming the input directly for the integer path.</summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="picture">The integer picture string.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="JsonataErrorException">The picture is an unsupported sequence (D3130) or mixes digit families (D3131).</exception>
    public static double Parse(string value, string picture)
    {
        IntegerPicture format = Analyse(picture);

        return ParseFromSpec(value, format);
    }

    /// <summary>
    /// Transforms a string back to its value against an already-analysed picture, the spec-based parse the
    /// date parser delegates to per numeric component. The numeral families parse exactly as
    /// <see cref="Parse(string, string)"/> does; the decimal family delegates to <see cref="ParseDecimal"/>.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="JsonataErrorException">The picture is an unsupported sequence (D3130).</exception>
    internal static double ParseFromSpec(string value, IntegerPicture format)
    {
        bool isUpper = format.Case == IntegerCase.Upper;

        return format.Primary switch
        {
            IntegerFormat.Letters => IntegerNumerals.LettersToDecimal(value, isUpper ? 'A' : 'a'),
            IntegerFormat.Roman => IntegerNumerals.RomanToDecimal(isUpper ? value : value.ToUpperInvariant()),
            IntegerFormat.Words => IntegerNumerals.WordsToNumber(IntegerNumerals.ToLower(value)),
            IntegerFormat.Decimal => ParseDecimal(value, format),
            _ => throw UnsupportedSequence(format.Token)
        };
    }

    /// <summary>
    /// Builds the per-component regular-expression fragment that matches a value formatted against the picture,
    /// for the date parser's combined runtime pattern. The decimal family is a digit run — fixed-width
    /// <c>[0-9]{n}</c> when <see cref="IntegerPicture.ParseWidth"/> is set (for an adjacent numeric field),
    /// <c>[0-9]+</c> otherwise — with an ordinal suffix appended when the picture is ordinal; the numeral and
    /// word families match a run of letters and word punctuation.
    /// </summary>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The regular-expression fragment.</returns>
    internal static string IntegerRegex(IntegerPicture format)
    {
        return format.Primary switch
        {
            IntegerFormat.Words => "[a-zA-Z]+(?:[\\s,\\-]+[a-zA-Z]+)*?",
            IntegerFormat.Letters or IntegerFormat.Roman => "[a-zA-Z]+",
            IntegerFormat.Decimal => DecimalRegex(format),
            _ => "[0-9]+"
        };
    }

    /// <summary>Builds the decimal-family regular-expression fragment: a fixed- or variable-width digit run with an optional ordinal suffix.</summary>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The decimal regular-expression fragment.</returns>
    private static string DecimalRegex(IntegerPicture format)
    {
        string digits = format.ParseWidth > 0 ? string.Create(CultureInfo.InvariantCulture, $"[0-9]{{{format.ParseWidth}}}") : "[0-9]+";

        return format.Ordinal ? digits + "(?:th|st|nd|rd)" : digits;
    }

    /// <summary>
    /// Analyses a picture string into its primary family, case, ordinal flag, and (for the decimal family) its
    /// digit family, mandatory-digit count, and grouping separators. The decimal analysis walks the picture
    /// least-significant-first, accumulating digit, optional-digit, and separator positions.
    /// </summary>
    /// <param name="picture">The integer picture string.</param>
    /// <returns>The analysed picture spec.</returns>
    /// <exception cref="JsonataErrorException">The picture mixes digit families (D3131).</exception>
    internal static IntegerPicture Analyse(string picture)
    {
        int semicolon = picture.LastIndexOf(';');
        string primaryFormat;
        bool ordinal = false;
        if(semicolon == -1)
        {
            primaryFormat = picture;
        }
        else
        {
            primaryFormat = picture[..semicolon];
            string modifier = picture[(semicolon + 1)..];
            ordinal = modifier.Length > 0 && modifier[0] == 'o';
        }

        return primaryFormat switch
        {
            "A" => IntegerPicture.Simple(IntegerFormat.Letters, IntegerCase.Upper, ordinal),
            "a" => IntegerPicture.Simple(IntegerFormat.Letters, IntegerCase.Lower, ordinal),
            "I" => IntegerPicture.Simple(IntegerFormat.Roman, IntegerCase.Upper, ordinal),
            "i" => IntegerPicture.Simple(IntegerFormat.Roman, IntegerCase.Lower, ordinal),
            "W" => IntegerPicture.Simple(IntegerFormat.Words, IntegerCase.Upper, ordinal),
            "Ww" => IntegerPicture.Simple(IntegerFormat.Words, IntegerCase.Title, ordinal),
            "w" => IntegerPicture.Simple(IntegerFormat.Words, IntegerCase.Lower, ordinal),
            _ => AnalyseDecimal(primaryFormat, ordinal)
        };
    }

    /// <summary>
    /// Analyses the decimal-family branch of a picture: the digit family, the mandatory- and optional-digit
    /// counts, and the grouping separators, run least-significant-first. A picture with no mandatory digit is an
    /// unsupported sequence; mixed digit families raise D3131.
    /// </summary>
    /// <param name="primaryFormat">The primary picture text (with any modifier removed).</param>
    /// <param name="ordinal">Whether the ordinal modifier was present.</param>
    /// <returns>The analysed decimal or sequence spec.</returns>
    /// <exception cref="JsonataErrorException">The picture mixes digit families (D3131).</exception>
    private static IntegerPicture AnalyseDecimal(string primaryFormat, bool ordinal)
    {
        int zeroCode = -1;
        int mandatoryDigits = 0;
        int optionalDigits = 0;
        List<GroupingSeparator> groupingSeparators = [];
        int separatorPosition = 0;

        //Walk the runes least-significant-first, matching the reference's reversed codepoint scan.
        int[] codepoints = ToCodepoints(primaryFormat);
        for(int i = codepoints.Length - 1; i >= 0; i--)
        {
            int codePoint = codepoints[i];
            int group = DigitGroupOf(codePoint);
            if(group != -1)
            {
                mandatoryDigits++;
                separatorPosition++;
                if(zeroCode == -1)
                {
                    zeroCode = group;
                }
                else if(group != zeroCode)
                {
                    throw new JsonataErrorException(WellKnownJsonataErrors.MixedDigitGroups, null, "The $formatInteger picture string mixes decimal digit groups.");
                }
            }
            else if(codePoint == OptionalDigit)
            {
                separatorPosition++;
                optionalDigits++;
            }
            else
            {
                groupingSeparators.Add(new GroupingSeparator(separatorPosition, char.ConvertFromUtf32(codePoint)));
            }
        }

        if(mandatoryDigits == 0)
        {
            return IntegerPicture.Sequence(primaryFormat);
        }

        int regular = RegularRepeat(groupingSeparators);
        if(regular > 0)
        {
            return IntegerPicture.Decimal(ordinal, zeroCode, mandatoryDigits, optionalDigits, regular: true, [new GroupingSeparator(regular, groupingSeparators[0].Character)]);
        }

        return IntegerPicture.Decimal(ordinal, zeroCode, mandatoryDigits, optionalDigits, regular: false, [.. groupingSeparators]);
    }

    /// <summary>
    /// Renders a non-negative-or-negative magnitude against an analysed picture, delegating the numeral families
    /// to <see cref="IntegerNumerals"/> and rendering the decimal family with zero-padding, digit-family
    /// mapping, grouping, and an optional ordinal suffix. A negative value is prefixed with a minus sign.
    /// </summary>
    /// <param name="value">The value to render (the caller floors it for the integer built-in; the date formatter passes whole components).</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The formatted string.</returns>
    /// <exception cref="JsonataErrorException">The picture is an unsupported sequence (D3130).</exception>
    internal static string FormatInteger(double value, IntegerPicture format)
    {
        bool negative = value < 0;
        double magnitude = Math.Abs(value);

        string formatted = format.Primary switch
        {
            IntegerFormat.Letters => IntegerNumerals.DecimalToLetters(magnitude, format.Case == IntegerCase.Upper ? 'A' : 'a'),
            IntegerFormat.Roman => RenderRoman(magnitude, format.Case),
            IntegerFormat.Words => RenderWords(magnitude, format),
            IntegerFormat.Decimal => RenderDecimal(magnitude, format),
            _ => throw UnsupportedSequence(format.Token)
        };

        return negative ? "-" + formatted : formatted;
    }

    /// <summary>Renders a magnitude as a Roman numeral, upper-casing it for the upper-case picture.</summary>
    /// <param name="magnitude">The magnitude to render.</param>
    /// <param name="textCase">The picture's case.</param>
    /// <returns>The Roman numeral.</returns>
    private static string RenderRoman(double magnitude, IntegerCase textCase)
    {
        string roman = IntegerNumerals.DecimalToRoman(magnitude);

        return textCase == IntegerCase.Upper ? roman.ToUpperInvariant() : roman;
    }

    /// <summary>Renders a magnitude as English words, applying the picture's word case (title case leaves the table spelling as-is).</summary>
    /// <param name="magnitude">The magnitude to render.</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The spelled words.</returns>
    private static string RenderWords(double magnitude, IntegerPicture format)
    {
        string words = IntegerNumerals.NumberToWords(magnitude, format.Ordinal);

        return format.Case switch
        {
            IntegerCase.Upper => words.ToUpperInvariant(),
            IntegerCase.Lower => IntegerNumerals.ToLower(words),
            _ => words
        };
    }

    /// <summary>
    /// Renders a magnitude in the decimal family: the digit string padded to the mandatory-digit count, mapped
    /// into the custom digit family, grouped at the regular interval or explicit positions, and given an
    /// ordinal suffix when requested.
    /// </summary>
    /// <param name="magnitude">The magnitude to render.</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The rendered decimal string.</returns>
    private static string RenderDecimal(double magnitude, IntegerPicture format)
    {
        //The magnitude is integer-valued, so a fixed-point render with no fractional digits is its exact digit
        //string and never lapses into exponent notation (which would feed an 'E' into the grouping logic).
        string digits = magnitude.ToString("F0", CultureInfo.InvariantCulture);
        int padLength = format.MandatoryDigits - digits.Length;
        if(padLength > 0)
        {
            digits = new string('0', padLength) + digits;
        }

        if(format.ZeroCode != AsciiZero)
        {
            digits = MapDigitFamily(digits, format.ZeroCode);
        }

        digits = format.Regular ? ApplyRegularGrouping(digits, format) : ApplyIrregularGrouping(digits, format);

        if(format.Ordinal)
        {
            digits += OrdinalSuffix(digits);
        }

        return digits;
    }

    /// <summary>Maps each ASCII digit of a rendered number into the custom digit family by the family's codepoint offset.</summary>
    /// <param name="digits">The ASCII digit string.</param>
    /// <param name="zeroCode">The custom digit family's base codepoint.</param>
    /// <returns>The digit string in the custom family.</returns>
    private static string MapDigitFamily(string digits, int zeroCode)
    {
        StringBuilder builder = new(digits.Length);
        foreach(Rune rune in digits.EnumerateRunes())
        {
            builder.Append(char.ConvertFromUtf32(rune.Value + zeroCode - AsciiZero));
        }

        return builder.ToString();
    }

    /// <summary>Inserts the grouping separator at the regular interval, counting positions from the least-significant digit.</summary>
    /// <param name="digits">The padded digit string.</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The grouped digit string.</returns>
    private static string ApplyRegularGrouping(string digits, IntegerPicture format)
    {
        GroupingSeparator separator = format.GroupingSeparators[0];
        string result = digits;
        int groups = (int)Math.Floor((result.Length - 1) / (double)separator.Position);
        for(int group = groups; group > 0; group--)
        {
            int position = result.Length - (group * separator.Position);
            result = result[..position] + separator.Character + result[position..];
        }

        return result;
    }

    /// <summary>Inserts the explicit grouping separators, applied from the most-significant position so each insertion's offset stays valid.</summary>
    /// <param name="digits">The padded digit string.</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The grouped digit string.</returns>
    private static string ApplyIrregularGrouping(string digits, IntegerPicture format)
    {
        string result = digits;

        //The separators were collected least-significant-first; applying them in reverse mirrors the reference's
        //reversed forEach, so each separator's position counts from the unchanged least-significant end.
        IReadOnlyList<GroupingSeparator> separators = format.GroupingSeparators;
        for(int i = separators.Count - 1; i >= 0; i--)
        {
            GroupingSeparator separator = separators[i];

            //A separator whose position reaches or passes the most-significant digit prepends (clamped to the
            //start), matching the reference's unconditional insertion.
            int position = Math.Max(0, result.Length - separator.Position);
            result = result[..position] + separator.Character + result[position..];
        }

        return result;
    }

    /// <summary>Determines the ordinal suffix (<c>st</c>/<c>nd</c>/<c>rd</c>/<c>th</c>) for a rendered decimal, with the teens taking <c>th</c>.</summary>
    /// <param name="digits">The rendered decimal string.</param>
    /// <returns>The ordinal suffix.</returns>
    private static string OrdinalSuffix(string digits)
    {
        char lastDigit = digits[^1];
        string? suffix = lastDigit switch
        {
            '1' => "st",
            '2' => "nd",
            '3' => "rd",
            _ => null
        };

        if(suffix is null || (digits.Length > 1 && digits[^2] == '1'))
        {
            return "th";
        }

        return suffix;
    }

    /// <summary>
    /// Transforms a decimal-family string back to its value: strips the two-character ordinal suffix, removes
    /// the grouping separators (a hardcoded comma for a regular picture, the explicit characters otherwise),
    /// maps a custom digit family back to ASCII, and parses the result.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="format">The analysed picture spec.</param>
    /// <returns>The parsed value.</returns>
    private static double ParseDecimal(string value, IntegerPicture format)
    {
        string digits = value;
        if(format.Ordinal)
        {
            digits = digits[..^2];
        }

        if(format.Regular)
        {
            digits = digits.Replace(",", "", StringComparison.Ordinal);
        }
        else
        {
            foreach(GroupingSeparator separator in format.GroupingSeparators)
            {
                digits = digits.Replace(separator.Character, "", StringComparison.Ordinal);
            }
        }

        if(format.ZeroCode != AsciiZero)
        {
            digits = UnmapDigitFamily(digits, format.ZeroCode);
        }

        return double.Parse(digits, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Maps each custom-family digit of a string back to its ASCII digit by the family's codepoint offset.</summary>
    /// <param name="digits">The custom-family digit string.</param>
    /// <param name="zeroCode">The custom digit family's base codepoint.</param>
    /// <returns>The ASCII digit string.</returns>
    private static string UnmapDigitFamily(string digits, int zeroCode)
    {
        StringBuilder builder = new(digits.Length);
        foreach(Rune rune in digits.EnumerateRunes())
        {
            builder.Append(char.ConvertFromUtf32(rune.Value - zeroCode + AsciiZero));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether the grouping positions are regular (an equal interval that occurs at every multiple),
    /// returning that interval or zero when irregular. The common factor is found through an iterative Euclid
    /// loop, matching the reference engine.
    /// </summary>
    /// <param name="separators">The grouping separators, collected least-significant-first.</param>
    /// <returns>The regular grouping interval, or zero when irregular or empty.</returns>
    private static int RegularRepeat(List<GroupingSeparator> separators)
    {
        if(separators.Count == 0)
        {
            return 0;
        }

        string separatorChar = separators[0].Character;
        for(int i = 1; i < separators.Count; i++)
        {
            if(separators[i].Character != separatorChar)
            {
                return 0;
            }
        }

        int factor = separators[0].Position;
        for(int i = 1; i < separators.Count; i++)
        {
            factor = GreatestCommonDivisor(factor, separators[i].Position);
        }

        for(int index = 1; index <= separators.Count; index++)
        {
            if(!ContainsPosition(separators, index * factor))
            {
                return 0;
            }
        }

        return factor;
    }

    /// <summary>Determines whether any grouping separator has the given position.</summary>
    /// <param name="separators">The grouping separators.</param>
    /// <param name="position">The position to find.</param>
    /// <returns><see langword="true"/> when a separator has that position.</returns>
    private static bool ContainsPosition(List<GroupingSeparator> separators, int position)
    {
        foreach(GroupingSeparator separator in separators)
        {
            if(separator.Position == position)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Computes the greatest common divisor of two non-negative integers through an iterative Euclid loop.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The greatest common divisor.</returns>
    private static int GreatestCommonDivisor(int a, int b)
    {
        int left = a;
        int right = b;
        while(right != 0)
        {
            int remainder = left % right;
            left = right;
            right = remainder;
        }

        return left;
    }

    /// <summary>Decomposes a string into its Unicode codepoints, faithful to surrogate handling.</summary>
    /// <param name="text">The string to decompose.</param>
    /// <returns>The codepoints in order.</returns>
    private static int[] ToCodepoints(string text)
    {
        List<int> codepoints = [];
        foreach(Rune rune in text.EnumerateRunes())
        {
            codepoints.Add(rune.Value);
        }

        return [.. codepoints];
    }

    /// <summary>Returns the base codepoint of the digit family a codepoint belongs to, or -1 when it is not a supported digit.</summary>
    /// <param name="codePoint">The codepoint to classify.</param>
    /// <returns>The digit family's base codepoint, or -1.</returns>
    private static int DigitGroupOf(int codePoint)
    {
        for(int i = 0; i < DecimalGroups.Length; i++)
        {
            int group = DecimalGroups[i];
            if(codePoint >= group && codePoint <= group + 9)
            {
                return group;
            }
        }

        return -1;
    }

    /// <summary>Builds the D3130 unsupported-sequence error naming the offending token.</summary>
    /// <param name="token">The unsupported picture token.</param>
    /// <returns>The error to throw.</returns>
    private static JsonataErrorException UnsupportedSequence(string token)
    {
        return new JsonataErrorException(WellKnownJsonataErrors.UnsupportedSequence, token, "The $formatInteger picture string is an unsupported sequence.");
    }

    /// <summary>The primary format families an integer picture can select.</summary>
    internal enum IntegerFormat
    {
        /// <summary>Decimal digits, with optional grouping and a custom digit family.</summary>
        Decimal,

        /// <summary>Bijective base-26 spreadsheet-column letters.</summary>
        Letters,

        /// <summary>Roman numerals.</summary>
        Roman,

        /// <summary>English number words.</summary>
        Words,

        /// <summary>An unsupported sequence (raises D3130 on use).</summary>
        Sequence
    }

    /// <summary>The letter case an integer picture selects.</summary>
    internal enum IntegerCase
    {
        /// <summary>Lower case.</summary>
        Lower,

        /// <summary>Upper case.</summary>
        Upper,

        /// <summary>Title case (the table spelling, for the <c>Ww</c> word picture).</summary>
        Title
    }

    /// <summary>One grouping separator in a decimal picture: the digit position it follows and the separator character.</summary>
    /// <param name="Position">The digit position, counted from the least-significant digit.</param>
    /// <param name="Character">The separator character (a string to carry a surrogate-pair separator).</param>
    internal readonly record struct GroupingSeparator(int Position, string Character);

    /// <summary>
    /// The analysed integer picture: the primary family, the case and ordinal flag, and (for the decimal family)
    /// the digit family, the mandatory- and optional-digit counts, the grouping regularity, the grouping
    /// separators, the sequence token, and the parse width. The date formatter's analysis sets
    /// <see cref="MandatoryDigits"/> (width override and the year truncation) and the date parser's analysis
    /// sets <see cref="ParseWidth"/> on the preceding numeric part, so both are re-projected through the
    /// <c>With…</c> helpers rather than mutated in place.
    /// </summary>
    /// <param name="Primary">The primary format family.</param>
    /// <param name="Case">The letter case.</param>
    /// <param name="Ordinal">Whether ordinal rendering was selected.</param>
    /// <param name="ZeroCode">The decimal digit family's base codepoint.</param>
    /// <param name="MandatoryDigits">The count of mandatory decimal digits.</param>
    /// <param name="OptionalDigits">The count of optional decimal digits.</param>
    /// <param name="Regular">Whether the grouping is regular.</param>
    /// <param name="GroupingSeparators">The grouping separators (a single regular separator, or the explicit list).</param>
    /// <param name="Token">The original picture token, for the D3130 sequence error.</param>
    /// <param name="ParseWidth">The fixed decimal digit-run width the date parser matches, or zero for a variable run.</param>
    internal readonly record struct IntegerPicture(
        IntegerFormat Primary,
        IntegerCase Case,
        bool Ordinal,
        int ZeroCode,
        int MandatoryDigits,
        int OptionalDigits,
        bool Regular,
        IReadOnlyList<GroupingSeparator> GroupingSeparators,
        string Token,
        int ParseWidth)
    {
        /// <summary>Builds a non-decimal picture (letters, Roman, or words) with no decimal attributes.</summary>
        /// <param name="primary">The primary format family.</param>
        /// <param name="textCase">The letter case.</param>
        /// <param name="ordinal">Whether ordinal rendering was selected.</param>
        /// <returns>The picture spec.</returns>
        public static IntegerPicture Simple(IntegerFormat primary, IntegerCase textCase, bool ordinal)
        {
            return new IntegerPicture(primary, textCase, ordinal, AsciiZero, 0, 0, Regular: false, [], "", ParseWidth: 0);
        }

        /// <summary>Builds a decimal picture from its analysed digit family, counts, and grouping.</summary>
        /// <param name="ordinal">Whether ordinal rendering was selected.</param>
        /// <param name="zeroCode">The decimal digit family's base codepoint.</param>
        /// <param name="mandatoryDigits">The count of mandatory decimal digits.</param>
        /// <param name="optionalDigits">The count of optional decimal digits.</param>
        /// <param name="regular">Whether the grouping is regular.</param>
        /// <param name="groupingSeparators">The grouping separators.</param>
        /// <returns>The picture spec.</returns>
        public static IntegerPicture Decimal(bool ordinal, int zeroCode, int mandatoryDigits, int optionalDigits, bool regular, IReadOnlyList<GroupingSeparator> groupingSeparators)
        {
            return new IntegerPicture(IntegerFormat.Decimal, IntegerCase.Lower, ordinal, zeroCode, mandatoryDigits, optionalDigits, regular, groupingSeparators, "", ParseWidth: 0);
        }

        /// <summary>Builds an unsupported-sequence picture carrying the offending token.</summary>
        /// <param name="token">The unsupported picture token.</param>
        /// <returns>The picture spec.</returns>
        public static IntegerPicture Sequence(string token)
        {
            return new IntegerPicture(IntegerFormat.Sequence, IntegerCase.Lower, Ordinal: false, AsciiZero, 0, 0, Regular: false, [], token, ParseWidth: 0);
        }

        /// <summary>Re-projects the spec with the mandatory-digit count raised to the given minimum, leaving a larger count unchanged.</summary>
        /// <param name="minimum">The minimum mandatory-digit count.</param>
        /// <returns>The re-projected spec.</returns>
        public IntegerPicture WithMandatoryDigitsAtLeast(int minimum)
        {
            return MandatoryDigits < minimum ? this with { MandatoryDigits = minimum } : this;
        }

        /// <summary>Re-projects the spec with the mandatory-digit count set exactly to the given value (the year-truncation width).</summary>
        /// <param name="count">The mandatory-digit count.</param>
        /// <returns>The re-projected spec.</returns>
        public IntegerPicture WithMandatoryDigits(int count)
        {
            return this with { MandatoryDigits = count };
        }

        /// <summary>Re-projects the spec with the fixed parse width set, the count of digits a directly-preceding numeric field matches.</summary>
        /// <param name="width">The fixed parse width.</param>
        /// <returns>The re-projected spec.</returns>
        public IntegerPicture WithParseWidth(int width)
        {
            return this with { ParseWidth = width };
        }

        /// <summary>Gets the regular grouping separator character, or the empty string when the picture has no regular separator.</summary>
        public string RegularSeparator => Regular && GroupingSeparators.Count > 0 ? GroupingSeparators[0].Character : "";
    }
}
