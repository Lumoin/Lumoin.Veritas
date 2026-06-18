using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Jsonata.Values;

namespace Lumoin.Veritas.Jsonata.Formatting;

/// <summary>
/// Formats a number into a decimal picture string for the <c>$formatNumber</c> built-in, implementing the
/// XPath/XQuery Functions and Operators <c>fn:format-number</c> decimal-format DSL (F&amp;O 3.1 §4.7). This is
/// the picture-string member of the reusable <c>Formatting</c> unit the numeric built-ins consume; the caller
/// supplies the value, the picture, and an optional set of symbol overrides.
/// </summary>
/// <remarks>
/// <para>
/// The picture string is split into at most two sub-pictures (positive;negative) by the pattern-separator,
/// each split into a prefix, an active part, and a suffix, validated against the F&amp;O 4.7.3 rules, and
/// analysed into the F&amp;O 4.7.4 grouping, minimum-size, and exponent attributes. The number is scaled for a
/// percent or per-mille picture, normalised into a mantissa and exponent for a scientific picture, rounded
/// half to even to the picture's maximum fractional size, rendered, padded, grouped, and wrapped in the
/// prefix and suffix.
/// </para>
/// <para>
/// Every scan is an explicit loop and the grouping-regularity test uses an iterative Euclid loop, so the
/// formatter never recurses. Symbol and picture characters are compared as UTF-16 code units, matching the
/// reference engine's character model (the well-known symbols — per-mille, circled digits, custom letter
/// families — are all in the Basic Multilingual Plane). The XPath decimal-format defines the decimal-separator,
/// grouping-separator, percent, and per-mille symbols as single characters; a multi-character override of one
/// of those is matched on its first code unit.
/// </para>
/// </remarks>
internal static class NumberPictureFormatter
{
    /// <summary>The default decimal-separator symbol.</summary>
    private const string DefaultDecimalSeparator = ".";

    /// <summary>The default grouping-separator symbol.</summary>
    private const string DefaultGroupingSeparator = ",";

    /// <summary>The default exponent-separator symbol.</summary>
    private const string DefaultExponentSeparator = "e";

    /// <summary>The default minus-sign symbol.</summary>
    private const string DefaultMinusSign = "-";

    /// <summary>The default percent symbol.</summary>
    private const string DefaultPercent = "%";

    /// <summary>The default per-mille symbol.</summary>
    private const string DefaultPerMille = "‰";

    /// <summary>The default zero-digit symbol.</summary>
    private const string DefaultZeroDigit = "0";

    /// <summary>The default optional-digit symbol.</summary>
    private const string DefaultDigit = "#";

    /// <summary>The default pattern-separator symbol.</summary>
    private const string DefaultPatternSeparator = ";";

    /// <summary>The number of digits in a decimal digit family.</summary>
    private const int DigitFamilySize = 10;

    /// <summary>
    /// Formats a value into the picture string, applying any symbol overrides, and throws a
    /// <see cref="JsonataErrorException"/> with a <c>D308x</c> code for an invalid picture.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <param name="picture">The decimal picture string.</param>
    /// <param name="options">The symbol overrides, keyed by the F&amp;O property name; <see langword="null"/> or empty applies the defaults.</param>
    /// <returns>The formatted string.</returns>
    /// <exception cref="JsonataErrorException">The picture string is invalid (a <c>D308x</c> code).</exception>
    public static string Format(double value, string picture, IReadOnlyList<KeyValuePair<string, JsonataValue>>? options)
    {
        PictureProperties properties = PictureProperties.FromOptions(options);

        string[] decimalDigitFamily = BuildDigitFamily(properties.ZeroDigit);
        HashSet<char> activeChars = BuildActiveChars(decimalDigitFamily, properties);

        string[] subPictures = SplitSubPictures(picture, properties.PatternSeparator);
        if(subPictures.Length > 2)
        {
            throw new JsonataErrorException(WellKnownJsonataErrors.FormatNumberTooManySubPictures, null, "The picture string has more than two sub-pictures.");
        }

        PictureParts[] parts = new PictureParts[subPictures.Length];
        for(int i = 0; i < subPictures.Length; i++)
        {
            parts[i] = SplitParts(subPictures[i], activeChars, properties);
        }

        for(int i = 0; i < parts.Length; i++)
        {
            Validate(parts[i], decimalDigitFamily, activeChars, properties);
        }

        PictureVariables[] variables = new PictureVariables[parts.Length];
        for(int i = 0; i < parts.Length; i++)
        {
            variables[i] = Analyse(parts[i], decimalDigitFamily, properties);
        }

        PictureVariables positive = variables[0];
        PictureVariables negative = variables.Length == 2 ? variables[1] : positive.WithPrefix(properties.MinusSign + positive.Prefix);

        PictureVariables pic = value >= 0 ? positive : negative;

        double adjustedNumber = AdjustNumber(value, pic, properties);

        double mantissa = adjustedNumber;
        int exponent = 0;
        bool exponentRendered = pic.MinimumExponentSize != 0;
        if(exponentRendered)
        {
            double maxMantissa = Math.Pow(10, pic.ScalingFactor);
            double minMantissa = Math.Pow(10, pic.ScalingFactor - 1);
            if(mantissa != 0)
            {
                while(Math.Abs(mantissa) < minMantissa)
                {
                    mantissa *= 10;
                    exponent -= 1;
                }

                while(Math.Abs(mantissa) > maxMantissa)
                {
                    mantissa /= 10;
                    exponent += 1;
                }
            }
        }

        double roundedNumber = DecimalRounding.RoundHalfToEven(mantissa, pic.MaximumFractionalPartSize);
        string stringValue = MakeString(roundedNumber, pic.MaximumFractionalPartSize, decimalDigitFamily, properties.ZeroDigit);

        stringValue = ApplySeparatorAndTrim(stringValue, properties);
        stringValue = Pad(stringValue, pic, properties);
        stringValue = ApplyIntegerGrouping(stringValue, pic, properties);
        stringValue = ApplyFractionalGrouping(stringValue, pic, properties);
        stringValue = StripSyntheticSeparator(stringValue, pic, properties);

        if(exponentRendered)
        {
            stringValue = AppendExponent(stringValue, exponent, pic, decimalDigitFamily, properties);
        }

        return pic.Prefix + stringValue + pic.Suffix;
    }

    /// <summary>Builds the ten-character decimal digit family beginning at the zero-digit's first UTF-16 code unit.</summary>
    /// <param name="zeroDigit">The zero-digit symbol.</param>
    /// <returns>The ten consecutive digit characters.</returns>
    private static string[] BuildDigitFamily(string zeroDigit)
    {
        int zeroCharCode = zeroDigit[0];
        string[] family = new string[DigitFamilySize];
        for(int i = 0; i < DigitFamilySize; i++)
        {
            family[i] = ((char)(zeroCharCode + i)).ToString();
        }

        return family;
    }

    /// <summary>Builds the set of active characters: the digit family plus the decimal, exponent, grouping, optional-digit, and pattern symbols.</summary>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The set of active characters (each symbol's first UTF-16 code unit).</returns>
    private static HashSet<char> BuildActiveChars(string[] decimalDigitFamily, PictureProperties properties)
    {
        HashSet<char> chars = [];
        for(int i = 0; i < decimalDigitFamily.Length; i++)
        {
            chars.Add(decimalDigitFamily[i][0]);
        }

        chars.Add(properties.DecimalSeparator[0]);
        chars.Add(properties.ExponentSeparator[0]);
        chars.Add(properties.GroupingSeparator[0]);
        chars.Add(properties.Digit[0]);
        chars.Add(properties.PatternSeparator[0]);

        return chars;
    }

    /// <summary>Splits the picture into its sub-pictures on the single-character pattern-separator.</summary>
    /// <param name="picture">The picture string.</param>
    /// <param name="patternSeparator">The pattern-separator symbol.</param>
    /// <returns>The sub-pictures in order.</returns>
    private static string[] SplitSubPictures(string picture, string patternSeparator)
    {
        return picture.Split(patternSeparator[0]);
    }

    /// <summary>
    /// Splits a sub-picture into its prefix, suffix, active part, mantissa part, optional exponent part, and
    /// integer and fractional parts, per F&amp;O 4.7.2.
    /// </summary>
    /// <param name="subPicture">The sub-picture.</param>
    /// <param name="activeChars">The active-character set.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The decomposed picture parts.</returns>
    private static PictureParts SplitParts(string subPicture, HashSet<char> activeChars, PictureProperties properties)
    {
        char exponentChar = properties.ExponentSeparator[0];

        string prefix = "";
        for(int i = 0; i < subPicture.Length; i++)
        {
            char ch = subPicture[i];
            if(activeChars.Contains(ch) && ch != exponentChar)
            {
                prefix = subPicture[..i];
                break;
            }
        }

        string suffix = "";
        for(int i = subPicture.Length - 1; i >= 0; i--)
        {
            char ch = subPicture[i];
            if(activeChars.Contains(ch) && ch != exponentChar)
            {
                suffix = subPicture[(i + 1)..];
                break;
            }
        }

        string activePart = subPicture[prefix.Length..(subPicture.Length - suffix.Length)];

        string mantissaPart;
        bool hasExponentPart;
        string exponentPart;
        int exponentPosition = IndexOfChar(subPicture, exponentChar, prefix.Length);
        if(exponentPosition == -1 || exponentPosition > subPicture.Length - suffix.Length)
        {
            mantissaPart = activePart;
            hasExponentPart = false;
            exponentPart = "";
        }
        else
        {
            //The exponent position is measured against the whole sub-picture; the active part begins at the
            //prefix, so the marker sits at the same offset within the active part.
            int markerInActive = exponentPosition - prefix.Length;
            mantissaPart = activePart[..markerInActive];
            exponentPart = activePart[(markerInActive + 1)..];
            hasExponentPart = true;
        }

        string integerPart;
        string fractionalPart;
        int decimalPosition = IndexOfChar(mantissaPart, properties.DecimalSeparator[0]);
        if(decimalPosition == -1)
        {
            integerPart = mantissaPart;
            fractionalPart = suffix;
        }
        else
        {
            integerPart = mantissaPart[..decimalPosition];
            fractionalPart = mantissaPart[(decimalPosition + 1)..];
        }

        return new PictureParts(prefix, suffix, activePart, mantissaPart, hasExponentPart, exponentPart, integerPart, fractionalPart, subPicture);
    }

    /// <summary>
    /// Validates a sub-picture's parts against the F&amp;O 4.7.3 rules, throwing the matching <c>D308x</c>
    /// code. The last matching condition wins, mirroring the reference's single throw after all checks.
    /// </summary>
    /// <param name="parts">The decomposed picture parts.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="activeChars">The active-character set.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <exception cref="JsonataErrorException">A validation rule failed (a <c>D308x</c> code).</exception>
    private static void Validate(PictureParts parts, string[] decimalDigitFamily, HashSet<char> activeChars, PictureProperties properties)
    {
        Utf8String error = default;
        string subPicture = parts.SubPicture;

        char decimalChar = properties.DecimalSeparator[0];
        int decimalPos = IndexOfChar(subPicture, decimalChar);
        if(decimalPos != LastIndexOfChar(subPicture, decimalChar))
        {
            error = WellKnownJsonataErrors.FormatNumberMultipleDecimalSeparators;
        }

        char percentChar = properties.Percent[0];
        if(IndexOfChar(subPicture, percentChar) != LastIndexOfChar(subPicture, percentChar))
        {
            error = WellKnownJsonataErrors.FormatNumberMultiplePercent;
        }

        char perMilleChar = properties.PerMille[0];
        if(IndexOfChar(subPicture, perMilleChar) != LastIndexOfChar(subPicture, perMilleChar))
        {
            error = WellKnownJsonataErrors.FormatNumberMultiplePerMille;
        }

        if(IndexOfChar(subPicture, percentChar) != -1 && IndexOfChar(subPicture, perMilleChar) != -1)
        {
            error = WellKnownJsonataErrors.FormatNumberPercentAndPerMille;
        }

        if(!MantissaHasDigit(parts.MantissaPart, decimalDigitFamily, properties.Digit[0]))
        {
            error = WellKnownJsonataErrors.FormatNumberNoDigit;
        }

        if(ActivePartHasPassiveChar(parts.ActivePart, activeChars))
        {
            error = WellKnownJsonataErrors.FormatNumberPassiveCharacter;
        }

        char groupingChar = properties.GroupingSeparator[0];
        if(decimalPos != -1)
        {
            if(CharAt(subPicture, decimalPos - 1) == groupingChar || CharAt(subPicture, decimalPos + 1) == groupingChar)
            {
                error = WellKnownJsonataErrors.FormatNumberGroupingAdjacentToDecimal;
            }
        }
        else if(parts.IntegerPart.Length > 0 && parts.IntegerPart[^1] == groupingChar)
        {
            error = WellKnownJsonataErrors.FormatNumberGroupingAtEnd;
        }

        if(subPicture.Contains(string.Concat(properties.GroupingSeparator, properties.GroupingSeparator), StringComparison.Ordinal))
        {
            error = WellKnownJsonataErrors.FormatNumberConsecutiveGrouping;
        }

        if(MandatoryDigitBeforeOptional(parts.IntegerPart, decimalDigitFamily, properties.Digit[0]))
        {
            error = WellKnownJsonataErrors.FormatNumberMandatoryDigitBeforeOptional;
        }

        if(MandatoryDigitAfterOptional(parts.FractionalPart, decimalDigitFamily, properties.Digit[0]))
        {
            error = WellKnownJsonataErrors.FormatNumberMandatoryDigitAfterOptional;
        }

        if(parts.HasExponentPart && parts.ExponentPart.Length > 0 &&
            (IndexOfChar(subPicture, percentChar) != -1 || IndexOfChar(subPicture, perMilleChar) != -1))
        {
            error = WellKnownJsonataErrors.FormatNumberExponentWithPercent;
        }

        if(parts.HasExponentPart && (parts.ExponentPart.Length == 0 || ExponentHasNonDigit(parts.ExponentPart, decimalDigitFamily)))
        {
            error = WellKnownJsonataErrors.FormatNumberInvalidExponent;
        }

        if(error.Span.Length != 0)
        {
            throw new JsonataErrorException(error, null, "The $formatNumber picture string is invalid.");
        }
    }

    /// <summary>Determines whether a mantissa part contains at least one digit-family character or the optional-digit symbol.</summary>
    /// <param name="mantissaPart">The mantissa part.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="digit">The optional-digit character.</param>
    /// <returns><see langword="true"/> when a digit-bearing character is present.</returns>
    private static bool MantissaHasDigit(string mantissaPart, string[] decimalDigitFamily, char digit)
    {
        for(int i = 0; i < mantissaPart.Length; i++)
        {
            char ch = mantissaPart[i];
            if(IsDigitFamily(ch, decimalDigitFamily) || ch == digit)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether an active part contains a passive character (one outside the active-character set).</summary>
    /// <param name="activePart">The active part.</param>
    /// <param name="activeChars">The active-character set.</param>
    /// <returns><see langword="true"/> when a passive character is present.</returns>
    private static bool ActivePartHasPassiveChar(string activePart, HashSet<char> activeChars)
    {
        for(int i = 0; i < activePart.Length; i++)
        {
            if(!activeChars.Contains(activePart[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a digit-family character precedes the first optional-digit character in the integer part.</summary>
    /// <param name="integerPart">The integer part.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="digit">The optional-digit character.</param>
    /// <returns><see langword="true"/> when a mandatory digit appears before an optional one.</returns>
    private static bool MandatoryDigitBeforeOptional(string integerPart, string[] decimalDigitFamily, char digit)
    {
        int optionalDigitPos = IndexOfChar(integerPart, digit);
        if(optionalDigitPos == -1)
        {
            return false;
        }

        for(int i = 0; i < optionalDigitPos; i++)
        {
            if(IsDigitFamily(integerPart[i], decimalDigitFamily))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a digit-family character follows the last optional-digit character in the fractional part.</summary>
    /// <param name="fractionalPart">The fractional part.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="digit">The optional-digit character.</param>
    /// <returns><see langword="true"/> when a mandatory digit appears after an optional one.</returns>
    private static bool MandatoryDigitAfterOptional(string fractionalPart, string[] decimalDigitFamily, char digit)
    {
        int optionalDigitPos = LastIndexOfChar(fractionalPart, digit);
        if(optionalDigitPos == -1)
        {
            return false;
        }

        for(int i = optionalDigitPos; i < fractionalPart.Length; i++)
        {
            if(IsDigitFamily(fractionalPart[i], decimalDigitFamily))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether an exponent part contains any character outside the digit family.</summary>
    /// <param name="exponentPart">The exponent part.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <returns><see langword="true"/> when a non-digit-family character is present.</returns>
    private static bool ExponentHasNonDigit(string exponentPart, string[] decimalDigitFamily)
    {
        for(int i = 0; i < exponentPart.Length; i++)
        {
            if(!IsDigitFamily(exponentPart[i], decimalDigitFamily))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Analyses a sub-picture's parts into the F&amp;O 4.7.4 attributes: grouping positions and regularity,
    /// minimum and maximum part sizes, the scaling factor, and the minimum exponent size.
    /// </summary>
    /// <param name="parts">The decomposed picture parts.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The analysed picture variables.</returns>
    private static PictureVariables Analyse(PictureParts parts, string[] decimalDigitFamily, PictureProperties properties)
    {
        char groupingChar = properties.GroupingSeparator[0];
        char digit = properties.Digit[0];

        int[] integerPartGroupingPositions = GetGroupingPositions(parts.IntegerPart, false, parts.IntegerPart, decimalDigitFamily, groupingChar, digit);
        int regularGrouping = Regular(integerPartGroupingPositions);
        int[] fractionalPartGroupingPositions = GetGroupingPositions(parts.FractionalPart, true, parts.IntegerPart, decimalDigitFamily, groupingChar, digit);

        int minimumIntegerPartSize = CountDigitFamily(parts.IntegerPart, decimalDigitFamily);
        int scalingFactor = minimumIntegerPartSize;

        int minimumFractionalPartSize = CountDigitFamily(parts.FractionalPart, decimalDigitFamily);
        int maximumFractionalPartSize = CountDigitOrOptional(parts.FractionalPart, decimalDigitFamily, digit);

        bool exponentPresent = parts.HasExponentPart;
        if(minimumIntegerPartSize == 0 && maximumFractionalPartSize == 0)
        {
            if(exponentPresent)
            {
                minimumFractionalPartSize = 1;
                maximumFractionalPartSize = 1;
            }
            else
            {
                minimumIntegerPartSize = 1;
            }
        }

        if(exponentPresent && minimumIntegerPartSize == 0 && IndexOfChar(parts.IntegerPart, digit) != -1)
        {
            minimumIntegerPartSize = 1;
        }

        if(minimumIntegerPartSize == 0 && minimumFractionalPartSize == 0)
        {
            minimumFractionalPartSize = 1;
        }

        int minimumExponentSize = 0;
        if(exponentPresent)
        {
            minimumExponentSize = CountDigitFamily(parts.ExponentPart, decimalDigitFamily);
        }

        return new PictureVariables(
            integerPartGroupingPositions,
            regularGrouping,
            minimumIntegerPartSize,
            scalingFactor,
            parts.Prefix,
            fractionalPartGroupingPositions,
            minimumFractionalPartSize,
            maximumFractionalPartSize,
            minimumExponentSize,
            parts.Suffix,
            parts.SubPicture,
            parts.HasExponentPart);
    }

    /// <summary>
    /// Collects the grouping-separator positions in a part as digit counts: for the integer part, the count of
    /// digit-bearing characters to the right of each separator; for the fractional part (<paramref name="toLeft"/>),
    /// the count to the left.
    /// </summary>
    /// <param name="part">The integer or fractional part whose first separator seeds the scan and whose segments are counted.</param>
    /// <param name="toLeft">When <see langword="true"/>, count digit-bearing characters to the left of each separator (fractional part).</param>
    /// <param name="continuationPart">The part the continuation scan advances through (the integer part, matching the reference).</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="groupingChar">The grouping-separator character.</param>
    /// <param name="digit">The optional-digit character.</param>
    /// <returns>The grouping positions as digit counts.</returns>
    private static int[] GetGroupingPositions(string part, bool toLeft, string continuationPart, string[] decimalDigitFamily, char groupingChar, char digit)
    {
        List<int> positions = [];
        int groupingPosition = IndexOfChar(part, groupingChar);
        while(groupingPosition != -1)
        {
            //The segment is taken from `part`, but the next separator is sought in `continuationPart` — the
            //integer part in both calls, matching the reference's grouping-position scan.
            int clamped = Math.Min(groupingPosition, part.Length);
            string segment = toLeft ? part[..clamped] : part[clamped..];
            int charsToTheRight = CountDigitOrOptional(segment, decimalDigitFamily, digit);
            positions.Add(charsToTheRight);
            groupingPosition = IndexOfChar(continuationPart, groupingChar, groupingPosition + 1);
        }

        return [.. positions];
    }

    /// <summary>
    /// Determines whether grouping positions are regular (an equal interval between each), returning that
    /// interval or zero when they are not. The common factor is found through an iterative Euclid loop.
    /// </summary>
    /// <param name="indexes">The grouping positions.</param>
    /// <returns>The regular grouping interval, or zero when the positions are irregular or empty.</returns>
    private static int Regular(int[] indexes)
    {
        if(indexes.Length == 0)
        {
            return 0;
        }

        int factor = indexes[0];
        for(int i = 1; i < indexes.Length; i++)
        {
            factor = GreatestCommonDivisor(factor, indexes[i]);
        }

        for(int index = 1; index <= indexes.Length; index++)
        {
            if(Array.IndexOf(indexes, index * factor) == -1)
            {
                return 0;
            }
        }

        return factor;
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

    /// <summary>Counts the digit-family characters in a string.</summary>
    /// <param name="text">The string to scan.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <returns>The count of digit-family characters.</returns>
    private static int CountDigitFamily(string text, string[] decimalDigitFamily)
    {
        int count = 0;
        for(int i = 0; i < text.Length; i++)
        {
            if(IsDigitFamily(text[i], decimalDigitFamily))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Counts the digit-family characters and optional-digit characters in a string.</summary>
    /// <param name="text">The string to scan.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="digit">The optional-digit character.</param>
    /// <returns>The count of digit-family and optional-digit characters.</returns>
    private static int CountDigitOrOptional(string text, string[] decimalDigitFamily, char digit)
    {
        int count = 0;
        for(int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if(IsDigitFamily(ch, decimalDigitFamily) || ch == digit)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Determines whether a character is a member of the decimal digit family.</summary>
    /// <param name="ch">The character to test.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <returns><see langword="true"/> when the character is a digit-family member.</returns>
    private static bool IsDigitFamily(char ch, string[] decimalDigitFamily)
    {
        for(int i = 0; i < decimalDigitFamily.Length; i++)
        {
            if(decimalDigitFamily[i][0] == ch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Scales the value for a percent (×100) or per-mille (×1000) picture, otherwise leaving it unchanged.</summary>
    /// <param name="value">The value to scale.</param>
    /// <param name="pic">The picture variables.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The scaled value.</returns>
    private static double AdjustNumber(double value, PictureVariables pic, PictureProperties properties)
    {
        if(IndexOfChar(pic.SubPicture, properties.Percent[0]) != -1)
        {
            return value * 100;
        }

        if(IndexOfChar(pic.SubPicture, properties.PerMille[0]) != -1)
        {
            return value * 1000;
        }

        return value;
    }

    /// <summary>
    /// Renders the absolute value with exactly the given number of fractional digits, mapping the ASCII digits
    /// to the custom digit family when the zero-digit is not the ASCII zero.
    /// </summary>
    /// <param name="value">The value to render (already rounded to the fractional-digit grid).</param>
    /// <param name="fractionalDigits">The number of fractional digits to render.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="zeroDigit">The zero-digit symbol.</param>
    /// <returns>The rendered numeric string with an ASCII decimal point.</returns>
    private static string MakeString(double value, int fractionalDigits, string[] decimalDigitFamily, string zeroDigit)
    {
        string str = Math.Abs(value).ToString("F" + fractionalDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if(zeroDigit == DefaultZeroDigit)
        {
            return str;
        }

        StringBuilder builder = new(str.Length);
        for(int i = 0; i < str.Length; i++)
        {
            char ch = str[i];
            if(ch is >= '0' and <= '9')
            {
                builder.Append(decimalDigitFamily[ch - '0']);
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Replaces the rendered ASCII decimal point with the decimal-separator symbol (appending it when absent),
    /// then strips leading and trailing zero-digits.
    /// </summary>
    /// <param name="stringValue">The rendered numeric string.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The string carrying the decimal-separator symbol with surplus zero-digits removed.</returns>
    private static string ApplySeparatorAndTrim(string stringValue, PictureProperties properties)
    {
        string result = stringValue;
        int decimalPos = IndexOfChar(result, '.');
        result = decimalPos == -1 ? result + properties.DecimalSeparator : result.Replace(".", properties.DecimalSeparator, StringComparison.Ordinal);

        char zeroChar = properties.ZeroDigit[0];
        int start = 0;
        while(start < result.Length && result[start] == zeroChar)
        {
            start++;
        }

        int end = result.Length;
        while(end > start && result[end - 1] == zeroChar)
        {
            end--;
        }

        return result[start..end];
    }

    /// <summary>
    /// Left-pads with zero-digits to the minimum integer-part size and right-pads with zero-digits to the
    /// minimum fractional-part size, with the right-pad width computed against the pre-pad length.
    /// </summary>
    /// <param name="stringValue">The string carrying the decimal-separator symbol.</param>
    /// <param name="pic">The picture variables.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The padded string.</returns>
    private static string Pad(string stringValue, PictureVariables pic, PictureProperties properties)
    {
        int decimalPos = stringValue.IndexOf(properties.DecimalSeparator, StringComparison.Ordinal);
        int padLeft = pic.MinimumIntegerPartSize - decimalPos;

        //The decimal-separator occupies a single position here, matching the fractional-grouping and synthetic-strip sites.
        int padRight = pic.MinimumFractionalPartSize - (stringValue.Length - decimalPos - 1);

        string result = stringValue;
        if(padLeft > 0)
        {
            result = new string(properties.ZeroDigit[0], padLeft) + result;
        }

        if(padRight > 0)
        {
            result += new string(properties.ZeroDigit[0], padRight);
        }

        return result;
    }

    /// <summary>Inserts grouping-separators into the integer part, using a regular interval when present, otherwise the explicit positions.</summary>
    /// <param name="stringValue">The padded string.</param>
    /// <param name="pic">The picture variables.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The grouped string.</returns>
    private static string ApplyIntegerGrouping(string stringValue, PictureVariables pic, PictureProperties properties)
    {
        int decimalPos = stringValue.IndexOf(properties.DecimalSeparator, StringComparison.Ordinal);
        string result = stringValue;
        if(pic.RegularGrouping > 0)
        {
            int groupCount = (int)Math.Floor((decimalPos - 1) / (double)pic.RegularGrouping);
            for(int group = 1; group <= groupCount; group++)
            {
                int splitAt = decimalPos - (group * pic.RegularGrouping);
                result = string.Concat(result[..splitAt], properties.GroupingSeparator, result[splitAt..]);
            }

            return result;
        }

        for(int i = 0; i < pic.IntegerPartGroupingPositions.Length; i++)
        {
            int pos = pic.IntegerPartGroupingPositions[i];
            int splitAt = decimalPos - pos;
            result = string.Concat(result[..splitAt], properties.GroupingSeparator, result[splitAt..]);
            decimalPos++;
        }

        return result;
    }

    /// <summary>Inserts grouping-separators into the fractional part at the explicit positions.</summary>
    /// <param name="stringValue">The grouped integer-part string.</param>
    /// <param name="pic">The picture variables.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The fractionally grouped string.</returns>
    private static string ApplyFractionalGrouping(string stringValue, PictureVariables pic, PictureProperties properties)
    {
        int decimalPos = stringValue.IndexOf(properties.DecimalSeparator, StringComparison.Ordinal);
        string result = stringValue;
        for(int i = 0; i < pic.FractionalPartGroupingPositions.Length; i++)
        {
            int pos = pic.FractionalPartGroupingPositions[i];
            int splitAt = pos + decimalPos + 1;
            result = string.Concat(result[..splitAt], properties.GroupingSeparator, result[splitAt..]);
        }

        return result;
    }

    /// <summary>Strips the synthetic trailing decimal-separator when the picture had no decimal point or the separator landed at the end.</summary>
    /// <param name="stringValue">The grouped string.</param>
    /// <param name="pic">The picture variables.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The string with any synthetic trailing separator removed.</returns>
    private static string StripSyntheticSeparator(string stringValue, PictureVariables pic, PictureProperties properties)
    {
        int decimalPos = stringValue.IndexOf(properties.DecimalSeparator, StringComparison.Ordinal);
        if(IndexOfChar(pic.SubPicture, properties.DecimalSeparator[0]) == -1 || decimalPos == stringValue.Length - 1)
        {
            return stringValue[..^1];
        }

        return stringValue;
    }

    /// <summary>Appends the exponent-separator, an optional minus-sign, and the zero-padded exponent magnitude.</summary>
    /// <param name="stringValue">The formatted mantissa string.</param>
    /// <param name="exponent">The exponent value.</param>
    /// <param name="pic">The picture variables.</param>
    /// <param name="decimalDigitFamily">The decimal digit family.</param>
    /// <param name="properties">The resolved picture symbols.</param>
    /// <returns>The string with the exponent appended.</returns>
    private static string AppendExponent(string stringValue, int exponent, PictureVariables pic, string[] decimalDigitFamily, PictureProperties properties)
    {
        string stringExponent = MakeString(exponent, 0, decimalDigitFamily, properties.ZeroDigit);
        int padLeft = pic.MinimumExponentSize - stringExponent.Length;
        if(padLeft > 0)
        {
            stringExponent = new string(properties.ZeroDigit[0], padLeft) + stringExponent;
        }

        string sign = exponent < 0 ? properties.MinusSign : "";

        return stringValue + properties.ExponentSeparator + sign + stringExponent;
    }

    /// <summary>Returns the character at an index, or the null character when the index is out of range.</summary>
    /// <param name="text">The string.</param>
    /// <param name="index">The index.</param>
    /// <returns>The character at the index, or <c>'\0'</c> when out of range.</returns>
    private static char CharAt(string text, int index)
    {
        return index >= 0 && index < text.Length ? text[index] : '\0';
    }

    /// <summary>Finds the first index of a UTF-16 code unit in a string by ordinal comparison, or -1 when absent.</summary>
    /// <param name="text">The string to scan.</param>
    /// <param name="ch">The code unit to find.</param>
    /// <returns>The first matching index, or -1.</returns>
    private static int IndexOfChar(string text, char ch)
    {
        return text.AsSpan().IndexOf(ch);
    }

    /// <summary>Finds the first index of a UTF-16 code unit in a string at or after a start index by ordinal comparison, or -1 when absent.</summary>
    /// <param name="text">The string to scan.</param>
    /// <param name="ch">The code unit to find.</param>
    /// <param name="startIndex">The index to begin scanning from.</param>
    /// <returns>The first matching index at or after the start, or -1.</returns>
    private static int IndexOfChar(string text, char ch, int startIndex)
    {
        if(startIndex >= text.Length)
        {
            return -1;
        }

        int relative = text.AsSpan(startIndex).IndexOf(ch);

        return relative == -1 ? -1 : relative + startIndex;
    }

    /// <summary>Finds the last index of a UTF-16 code unit in a string by ordinal comparison, or -1 when absent.</summary>
    /// <param name="text">The string to scan.</param>
    /// <param name="ch">The code unit to find.</param>
    /// <returns>The last matching index, or -1.</returns>
    private static int LastIndexOfChar(string text, char ch)
    {
        return text.AsSpan().LastIndexOf(ch);
    }

    /// <summary>
    /// The resolved picture symbols, the defaults overlaid by any options. Each symbol is read by its first
    /// UTF-16 code unit, matching the reference engine's character model.
    /// </summary>
    /// <param name="DecimalSeparator">The decimal-separator symbol.</param>
    /// <param name="GroupingSeparator">The grouping-separator symbol.</param>
    /// <param name="ExponentSeparator">The exponent-separator symbol.</param>
    /// <param name="MinusSign">The minus-sign symbol.</param>
    /// <param name="Percent">The percent symbol.</param>
    /// <param name="PerMille">The per-mille symbol.</param>
    /// <param name="ZeroDigit">The zero-digit symbol.</param>
    /// <param name="Digit">The optional-digit symbol.</param>
    /// <param name="PatternSeparator">The pattern-separator symbol.</param>
    private readonly record struct PictureProperties(
        string DecimalSeparator,
        string GroupingSeparator,
        string ExponentSeparator,
        string MinusSign,
        string Percent,
        string PerMille,
        string ZeroDigit,
        string Digit,
        string PatternSeparator)
    {
        /// <summary>Builds the resolved symbols from the defaults, overlaid by any string-valued options (later keys win).</summary>
        /// <param name="options">The symbol overrides, keyed by the F&amp;O property name; <see langword="null"/> or empty applies the defaults.</param>
        /// <returns>The resolved picture symbols.</returns>
        public static PictureProperties FromOptions(IReadOnlyList<KeyValuePair<string, JsonataValue>>? options)
        {
            string decimalSeparator = DefaultDecimalSeparator;
            string groupingSeparator = DefaultGroupingSeparator;
            string exponentSeparator = DefaultExponentSeparator;
            string minusSign = DefaultMinusSign;
            string percent = DefaultPercent;
            string perMille = DefaultPerMille;
            string zeroDigit = DefaultZeroDigit;
            string digit = DefaultDigit;
            string patternSeparator = DefaultPatternSeparator;

            if(options is not null)
            {
                for(int i = 0; i < options.Count; i++)
                {
                    KeyValuePair<string, JsonataValue> pair = options[i];
                    if(pair.Value.Kind != JsonataValueKind.String)
                    {
                        continue;
                    }

                    string overrideValue = pair.Value.AsString;
                    switch(pair.Key)
                    {
                        case "decimal-separator":
                        {
                            decimalSeparator = overrideValue;
                            break;
                        }
                        case "grouping-separator":
                        {
                            groupingSeparator = overrideValue;
                            break;
                        }
                        case "exponent-separator":
                        {
                            exponentSeparator = overrideValue;
                            break;
                        }
                        case "minus-sign":
                        {
                            minusSign = overrideValue;
                            break;
                        }
                        case "percent":
                        {
                            percent = overrideValue;
                            break;
                        }
                        case "per-mille":
                        {
                            perMille = overrideValue;
                            break;
                        }
                        case "zero-digit":
                        {
                            zeroDigit = overrideValue;
                            break;
                        }
                        case "digit":
                        {
                            digit = overrideValue;
                            break;
                        }
                        case "pattern-separator":
                        {
                            patternSeparator = overrideValue;
                            break;
                        }
                        default:
                        {
                            break;
                        }
                    }
                }
            }

            return new PictureProperties(decimalSeparator, groupingSeparator, exponentSeparator, minusSign, percent, perMille, zeroDigit, digit, patternSeparator);
        }
    }

    /// <summary>
    /// The decomposition of one sub-picture per F&amp;O 4.7.2: the prefix and suffix, the active part, the
    /// mantissa and optional exponent parts, the integer and fractional parts, and the original sub-picture.
    /// </summary>
    /// <param name="Prefix">The passive prefix before the first active non-exponent character.</param>
    /// <param name="Suffix">The passive suffix after the last active non-exponent character.</param>
    /// <param name="ActivePart">The active part between the prefix and suffix.</param>
    /// <param name="MantissaPart">The mantissa portion of the active part.</param>
    /// <param name="HasExponentPart">Whether the sub-picture has an exponent part.</param>
    /// <param name="ExponentPart">The exponent portion of the active part, empty when absent.</param>
    /// <param name="IntegerPart">The integer portion of the mantissa.</param>
    /// <param name="FractionalPart">The fractional portion of the mantissa (the suffix when there is no decimal-separator).</param>
    /// <param name="SubPicture">The original sub-picture.</param>
    private readonly record struct PictureParts(
        string Prefix,
        string Suffix,
        string ActivePart,
        string MantissaPart,
        bool HasExponentPart,
        string ExponentPart,
        string IntegerPart,
        string FractionalPart,
        string SubPicture);

    /// <summary>
    /// The analysed attributes of one sub-picture per F&amp;O 4.7.4: grouping positions and regularity, minimum
    /// and maximum part sizes, the scaling factor, the minimum exponent size, the prefix and suffix, and the
    /// original sub-picture.
    /// </summary>
    /// <param name="IntegerPartGroupingPositions">The integer-part grouping positions as digit counts.</param>
    /// <param name="RegularGrouping">The regular grouping interval, or zero when irregular.</param>
    /// <param name="MinimumIntegerPartSize">The minimum number of integer-part digits.</param>
    /// <param name="ScalingFactor">The scaling factor for scientific normalisation.</param>
    /// <param name="Prefix">The prefix.</param>
    /// <param name="FractionalPartGroupingPositions">The fractional-part grouping positions as digit counts.</param>
    /// <param name="MinimumFractionalPartSize">The minimum number of fractional-part digits.</param>
    /// <param name="MaximumFractionalPartSize">The maximum number of fractional-part digits.</param>
    /// <param name="MinimumExponentSize">The minimum number of exponent digits.</param>
    /// <param name="Suffix">The suffix.</param>
    /// <param name="SubPicture">The original sub-picture.</param>
    /// <param name="HasExponentPart">Whether the sub-picture has an exponent part.</param>
    private readonly record struct PictureVariables(
        int[] IntegerPartGroupingPositions,
        int RegularGrouping,
        int MinimumIntegerPartSize,
        int ScalingFactor,
        string Prefix,
        int[] FractionalPartGroupingPositions,
        int MinimumFractionalPartSize,
        int MaximumFractionalPartSize,
        int MinimumExponentSize,
        string Suffix,
        string SubPicture,
        bool HasExponentPart)
    {
        /// <summary>Returns a copy of these variables with a replaced prefix, for synthesising the negative sub-picture.</summary>
        /// <param name="prefix">The replacement prefix.</param>
        /// <returns>The variables with the new prefix.</returns>
        public PictureVariables WithPrefix(string prefix)
        {
            return this with { Prefix = prefix };
        }
    }
}
