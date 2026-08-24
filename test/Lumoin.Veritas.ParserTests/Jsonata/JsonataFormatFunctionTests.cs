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
/// Tests for the F&amp;O picture-string formatting built-ins consuming the <c>Formatting</c> unit. Covers
/// <c>$formatBase</c> (base-N conversion with the lower-case alphabet, half-to-even rounding of both arguments,
/// and the D3100 out-of-range radix error); <c>$formatNumber</c> (the <c>fn:format-number</c> decimal-format
/// DSL: grouping, padding, percent and per-mille scaling, scientific notation, sub-pictures, prefix and suffix,
/// custom symbol families, and the D3080–D3093 picture-validation errors); and the <c>fn:format-integer</c> pair
/// <c>$formatInteger</c> / <c>$parseInteger</c> (the decimal-digit pattern with regular and irregular grouping
/// and Unicode digit families, the ordinal modifier, Roman numerals, number-to-words cardinal and ordinal,
/// spreadsheet-column letters, and the D3130 unsupported-sequence and D3131 mixed-digit-group errors).
/// </summary>
[TestClass]
internal sealed class JsonataFormatFunctionTests
{
    /// <summary><c>$formatBase</c> defaults to base 10.</summary>
    [TestMethod]
    public void FormatBaseDefaultRadixIsTen()
    {
        Assert.AreEqual("100", Evaluate("$formatBase(100)").AsString);
    }

    /// <summary><c>$formatBase</c> over an undefined number is undefined.</summary>
    [TestMethod]
    public void FormatBaseUndefinedIsUndefined()
    {
        Assert.IsTrue(Evaluate("$formatBase(nothing)").IsUndefined);
    }

    /// <summary><c>$formatBase</c> converts to binary.</summary>
    [TestMethod]
    public void FormatBaseBinary()
    {
        Assert.AreEqual("1100100", Evaluate("$formatBase(100, 2)").AsString);
    }

    /// <summary><c>$formatBase</c> prefixes a negative value with a minus sign.</summary>
    [TestMethod]
    public void FormatBaseNegativeBinary()
    {
        Assert.AreEqual("-1100100", Evaluate("$formatBase(-100, 2)").AsString);
    }

    /// <summary><c>$formatBase</c> uses the lower-case alphabet for base 36.</summary>
    [TestMethod]
    public void FormatBaseBase36()
    {
        Assert.AreEqual("2s", Evaluate("$formatBase(100, 36)").AsString);
    }

    /// <summary><c>$formatBase</c> rounds both the value and the radix half to even before converting.</summary>
    [TestMethod]
    public void FormatBaseRoundsArguments()
    {
        Assert.AreEqual("1100100", Evaluate("$formatBase(99.5, 2.5)").AsString);
    }

    /// <summary><c>$formatBase</c> with a radix below 2 raises D3100.</summary>
    [TestMethod]
    public void FormatBaseRadixTooLowThrowsD3100()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$formatBase(100, 1)"));

        Assert.AreEqual("D3100", error.Code.ToString());
    }

    /// <summary><c>$formatBase</c> with a radix above 36 raises D3100.</summary>
    [TestMethod]
    public void FormatBaseRadixTooHighThrowsD3100()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$formatBase(100, 37)"));

        Assert.AreEqual("D3100", error.Code.ToString());
    }

    /// <summary><c>$formatBase</c> converts a large integer (within the exact double range) without scientific notation.</summary>
    [TestMethod]
    public void FormatBaseLargeInteger()
    {
        Assert.AreEqual("5890840712243076", Evaluate("$formatBase(5890840712243076)").AsString);
    }

    /// <summary><c>$formatBase</c> of zero is the single digit zero.</summary>
    [TestMethod]
    public void FormatBaseZero()
    {
        Assert.AreEqual("0", Evaluate("$formatBase(0, 2)").AsString);
    }

    /// <summary><c>$formatNumber</c> groups the integer part and pads the fractional part to its minimum size.</summary>
    [TestMethod]
    public void FormatNumberGroupingAndFractionalPad()
    {
        Assert.AreEqual("12,345.60", Evaluate("$formatNumber(12345.6, \"#,###.00\")").AsString);
    }

    /// <summary><c>$formatNumber</c> regroups regardless of the picture's literal grouping placement.</summary>
    [TestMethod]
    public void FormatNumberRegularGroupingFromMixedPicture()
    {
        Assert.AreEqual("12,345,678.90", Evaluate("$formatNumber(12345678.9, \"9,999.99\")").AsString);
    }

    /// <summary><c>$formatNumber</c> with irregular grouping positions inserts separators at the explicit positions.</summary>
    [TestMethod]
    public void FormatNumberIrregularGrouping()
    {
        Assert.AreEqual("123412345,6,78.90", Evaluate("$formatNumber(123412345678.9, \"9,9,99.99\")").AsString);
    }

    /// <summary><c>$formatNumber</c> groups the fractional part at its explicit position.</summary>
    [TestMethod]
    public void FormatNumberFractionalGrouping()
    {
        Assert.AreEqual("1,234.567,890", Evaluate("$formatNumber(1234.56789, \"9,999.999,999\")").AsString);
    }

    /// <summary><c>$formatNumber</c> left-pads the integer part with zeroes to the minimum size.</summary>
    [TestMethod]
    public void FormatNumberIntegerPad()
    {
        Assert.AreEqual("0124", Evaluate("$formatNumber(123.9, \"9999\")").AsString);
    }

    /// <summary><c>$formatNumber</c> scales by a hundred for a percent picture.</summary>
    [TestMethod]
    public void FormatNumberPercent()
    {
        Assert.AreEqual("14%", Evaluate("$formatNumber(0.14, \"01%\")").AsString);
    }

    /// <summary><c>$formatNumber</c> scales by a thousand for a per-mille picture.</summary>
    [TestMethod]
    public void FormatNumberPerMille()
    {
        Assert.AreEqual("485.7‰", Evaluate("$formatNumber(0.4857,\"###.###‰\")").AsString);
    }

    /// <summary><c>$formatNumber</c> uses a custom per-mille symbol supplied through the options object.</summary>
    [TestMethod]
    public void FormatNumberCustomPerMilleOption()
    {
        Assert.AreEqual("140pm", Evaluate("$formatNumber(0.14, \"###pm\", {\"per-mille\": \"pm\"})").AsString);
    }

    /// <summary><c>$formatNumber</c> prefixes a negative value with the minus-sign on the synthesised negative sub-picture.</summary>
    [TestMethod]
    public void FormatNumberNegativeMinusSign()
    {
        Assert.AreEqual("-006", Evaluate("$formatNumber(-6, \"000\")").AsString);
    }

    /// <summary><c>$formatNumber</c> renders scientific notation with a single-digit exponent.</summary>
    [TestMethod]
    public void FormatNumberScientificSingleExponent()
    {
        Assert.AreEqual("12.346e2", Evaluate("$formatNumber(1234.5678, \"00.000e0\")").AsString);
    }

    /// <summary><c>$formatNumber</c> pads the exponent to its minimum size.</summary>
    [TestMethod]
    public void FormatNumberScientificPaddedExponent()
    {
        Assert.AreEqual("12.346e002", Evaluate("$formatNumber(1234.5678, \"00.000e000\")").AsString);
    }

    /// <summary><c>$formatNumber</c> renders scientific notation in a custom digit family.</summary>
    [TestMethod]
    public void FormatNumberScientificCustomDigitFamily()
    {
        Assert.AreEqual("①②.③④⑥e②", Evaluate("$formatNumber(1234.5678, \"①①.①①①e①\", {\"zero-digit\": \"⑟\"})").AsString);
    }

    /// <summary><c>$formatNumber</c> renders a negative exponent.</summary>
    [TestMethod]
    public void FormatNumberScientificNegativeExponent()
    {
        Assert.AreEqual("2.3e-1", Evaluate("$formatNumber(0.234, \"0.0e0\")").AsString);
    }

    /// <summary><c>$formatNumber</c> renders a zero exponent with an optional integer digit.</summary>
    [TestMethod]
    public void FormatNumberScientificOptionalIntegerDigit()
    {
        Assert.AreEqual("0.23e0", Evaluate("$formatNumber(0.234, \"#.00e0\")").AsString);
    }

    /// <summary><c>$formatNumber</c> renders an optional-only fractional picture with an exponent.</summary>
    [TestMethod]
    public void FormatNumberScientificOptionalFractionalDigit()
    {
        Assert.AreEqual("0.1e0", Evaluate("$formatNumber(0.123, \"#.e9\")").AsString);
    }

    /// <summary><c>$formatNumber</c> omits the integer part when the picture has none.</summary>
    [TestMethod]
    public void FormatNumberScientificNoIntegerPart()
    {
        Assert.AreEqual(".23e0", Evaluate("$formatNumber(0.234, \".00e0\")").AsString);
    }

    /// <summary><c>$formatNumber</c> selects the negative sub-picture for a negative value.</summary>
    [TestMethod]
    public void FormatNumberNegativeSubPicture()
    {
        Assert.AreEqual("87,504.4812", Evaluate("$formatNumber(2392.14*(-36.58), \"000,000.000###;###,###.000###\")").AsString);
    }

    /// <summary><c>$formatNumber</c> keeps a literal prefix and suffix around the formatted number.</summary>
    [TestMethod]
    public void FormatNumberPrefixAndSuffix()
    {
        Assert.AreEqual("PREFIX185.2812SUFFIX", Evaluate("$formatNumber(2.14*86.58,\"PREFIX##00.000###SUFFIX\")").AsString);
    }

    /// <summary><c>$formatNumber</c> groups a very large integer at the regular interval.</summary>
    [TestMethod]
    public void FormatNumberLargeIntegerGrouping()
    {
        Assert.AreEqual("100,000000,000000,000000", Evaluate("$formatNumber(1E20,\"#,######\")").AsString);
    }

    /// <summary><c>$formatNumber</c> formats a value with a mandatory integer and fractional picture.</summary>
    [TestMethod]
    public void FormatNumberMandatoryParts()
    {
        Assert.AreEqual("002.000", Evaluate("$formatNumber(2, '000.000')").AsString);
    }

    /// <summary><c>$formatNumber</c> uses a custom letter family as the digit family.</summary>
    [TestMethod]
    public void FormatNumberCustomLetterFamily()
    {
        Assert.AreEqual("AAC.AAA", Evaluate("$formatNumber(2, 'AAA.AAA', {'zero-digit': 'A'})").AsString);
    }

    /// <summary><c>$formatNumber</c> renders scientific notation in a custom letter family with a negative exponent.</summary>
    [TestMethod]
    public void FormatNumberCustomLetterFamilyScientific()
    {
        Assert.AreEqual("Be-AAB", Evaluate("$formatNumber(0.1, 'AeAAA', {'zero-digit': 'A'})").AsString);
    }

    /// <summary><c>$formatNumber</c> over an undefined number is undefined.</summary>
    [TestMethod]
    public void FormatNumberUndefinedIsUndefined()
    {
        Assert.IsTrue(Evaluate("$formatNumber(foo, '#0.00')").IsUndefined);
    }

    /// <summary><c>$formatNumber</c> of zero with a scientific picture renders a zero mantissa and exponent.</summary>
    [TestMethod]
    public void FormatNumberZeroScientific()
    {
        Assert.AreEqual("0e0", Evaluate("$formatNumber(0, '0e0')").AsString);
    }

    /// <summary><c>$formatNumber</c> of a negative value with a scientific picture.</summary>
    [TestMethod]
    public void FormatNumberNegativeScientific()
    {
        Assert.AreEqual("-4e1", Evaluate("$formatNumber(-42, '0e0')").AsString);
    }

    /// <summary><c>$formatNumber</c> of a positive value with a scientific picture.</summary>
    [TestMethod]
    public void FormatNumberPositiveScientific()
    {
        Assert.AreEqual("4e1", Evaluate("$formatNumber(42, '0e0')").AsString);
    }

    /// <summary><c>$formatNumber</c> of zero with a fractional scientific picture pads the mantissa fraction.</summary>
    [TestMethod]
    public void FormatNumberZeroFractionalScientific()
    {
        Assert.AreEqual("0.00e0", Evaluate("$formatNumber(0, '0.00e0')").AsString);
    }

    /// <summary><c>$formatNumber</c> with more than two sub-pictures raises D3080.</summary>
    [TestMethod]
    public void FormatNumberTooManySubPicturesThrowsD3080()
    {
        Assert.AreEqual("D3080", EvaluateError("$formatNumber(20,\"#;#;#\")"));
    }

    /// <summary><c>$formatNumber</c> with two decimal separators raises D3081.</summary>
    [TestMethod]
    public void FormatNumberMultipleDecimalSeparatorsThrowsD3081()
    {
        Assert.AreEqual("D3081", EvaluateError("$formatNumber(20,\"#.0.0\")"));
    }

    /// <summary><c>$formatNumber</c> with two percent characters raises D3082.</summary>
    [TestMethod]
    public void FormatNumberMultiplePercentThrowsD3082()
    {
        Assert.AreEqual("D3082", EvaluateError("$formatNumber(20,\"#0%%\")"));
    }

    /// <summary><c>$formatNumber</c> with two per-mille characters raises D3083.</summary>
    [TestMethod]
    public void FormatNumberMultiplePerMilleThrowsD3083()
    {
        Assert.AreEqual("D3083", EvaluateError("$formatNumber(20,\"#0‰‰\")"));
    }

    /// <summary><c>$formatNumber</c> with both a percent and a per-mille character raises D3084.</summary>
    [TestMethod]
    public void FormatNumberPercentAndPerMilleThrowsD3084()
    {
        Assert.AreEqual("D3084", EvaluateError("$formatNumber(20,\"#0%‰\")"));
    }

    /// <summary><c>$formatNumber</c> with no digit in the mantissa raises D3085.</summary>
    [TestMethod]
    public void FormatNumberNoDigitThrowsD3085()
    {
        Assert.AreEqual("D3085", EvaluateError("$formatNumber(20,\".e0\")"));
    }

    /// <summary><c>$formatNumber</c> with a passive character in the active part raises D3086.</summary>
    [TestMethod]
    public void FormatNumberPassiveCharacterThrowsD3086()
    {
        Assert.AreEqual("D3086", EvaluateError("$formatNumber(20,\"0+.e0\")"));
    }

    /// <summary><c>$formatNumber</c> with a grouping-separator adjacent to the decimal-separator raises D3087.</summary>
    [TestMethod]
    public void FormatNumberGroupingAdjacentToDecimalThrowsD3087()
    {
        Assert.AreEqual("D3087", EvaluateError("$formatNumber(20,\"0,.e0\")"));
    }

    /// <summary><c>$formatNumber</c> with a trailing integer grouping-separator raises D3088.</summary>
    [TestMethod]
    public void FormatNumberGroupingAtEndThrowsD3088()
    {
        Assert.AreEqual("D3088", EvaluateError("$formatNumber(20,\"0,\")"));
    }

    /// <summary><c>$formatNumber</c> with two consecutive grouping-separators raises D3089.</summary>
    [TestMethod]
    public void FormatNumberConsecutiveGroupingThrowsD3089()
    {
        Assert.AreEqual("D3089", EvaluateError("$formatNumber(20,\"0,,0\")"));
    }

    /// <summary><c>$formatNumber</c> with a mandatory digit before an optional one in the integer part raises D3090.</summary>
    [TestMethod]
    public void FormatNumberMandatoryDigitBeforeOptionalThrowsD3090()
    {
        Assert.AreEqual("D3090", EvaluateError("$formatNumber(20,\"0#.e0\")"));
    }

    /// <summary><c>$formatNumber</c> with a mandatory digit after an optional one in the fractional part raises D3091.</summary>
    [TestMethod]
    public void FormatNumberMandatoryDigitAfterOptionalThrowsD3091()
    {
        Assert.AreEqual("D3091", EvaluateError("$formatNumber(20,\"#0.#0e0\")"));
    }

    /// <summary><c>$formatNumber</c> with an exponent and a percent character raises D3092.</summary>
    [TestMethod]
    public void FormatNumberExponentWithPercentThrowsD3092()
    {
        Assert.AreEqual("D3092", EvaluateError("$formatNumber(20,\"#0.0e0%\")"));
    }

    /// <summary><c>$formatNumber</c> with a non-digit exponent part raises D3093.</summary>
    [TestMethod]
    public void FormatNumberInvalidExponentThrowsD3093()
    {
        Assert.AreEqual("D3093", EvaluateError("$formatNumber(20,\"#0.0e0,0\")"));
    }

    /// <summary><c>$formatNumber</c> with a digit-less duplicate-percent picture raises D3086 (the last-wins passive-character rule).</summary>
    [TestMethod]
    public void FormatNumberDoublePercentNoDigitThrowsD3086()
    {
        Assert.AreEqual("D3086", EvaluateError("$formatNumber(42, '%%')"));
    }

    /// <summary><c>$formatNumber</c> with a digit-less duplicate-per-mille picture raises D3086 (the last-wins passive-character rule).</summary>
    [TestMethod]
    public void FormatNumberDoublePerMilleNoDigitThrowsD3086()
    {
        Assert.AreEqual("D3086", EvaluateError("$formatNumber(42, '‰‰')"));
    }

    /// <summary><c>$formatNumber</c> with a digit-less percent-and-per-mille picture raises D3086 (the last-wins passive-character rule).</summary>
    [TestMethod]
    public void FormatNumberPercentPerMilleNoDigitThrowsD3086()
    {
        Assert.AreEqual("D3086", EvaluateError("$formatNumber(42, '%‰')"));
    }

    /// <summary><c>$formatNumber</c> with a digit-less all-passive picture raises D3086.</summary>
    [TestMethod]
    public void FormatNumberAllPassiveThrowsD3086()
    {
        Assert.AreEqual("D3086", EvaluateError("$formatNumber(42, '---')"));
    }

    /// <summary><c>$formatInteger</c> over an undefined number is undefined.</summary>
    [TestMethod]
    public void FormatIntegerUndefinedIsUndefined()
    {
        Assert.IsTrue(Evaluate("$formatInteger(nothing, '0')").IsUndefined);
    }

    /// <summary><c>$formatInteger</c> renders a decimal-digit cardinal.</summary>
    [TestMethod]
    public void FormatIntegerDecimalCardinal()
    {
        Assert.AreEqual("123", Evaluate("$formatInteger(123, '000')").AsString);
    }

    /// <summary><c>$formatInteger</c> left-pads with zeros to the mandatory-digit count.</summary>
    [TestMethod]
    public void FormatIntegerZeroPad()
    {
        Assert.AreEqual("0123", Evaluate("$formatInteger(123, '0000')").AsString);
    }

    /// <summary><c>$formatInteger</c> prefixes a padded negative value with a minus sign.</summary>
    [TestMethod]
    public void FormatIntegerNegativeZeroPad()
    {
        Assert.AreEqual("-0003", Evaluate("$formatInteger(-3, '0000')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders only the mandatory digits when optional digits are unused.</summary>
    [TestMethod]
    public void FormatIntegerOptionalDigits()
    {
        Assert.AreEqual("12", Evaluate("$formatInteger(12, '###0')").AsString);
    }

    /// <summary><c>$formatInteger</c> floors a float toward negative infinity before rendering.</summary>
    [TestMethod]
    public void FormatIntegerFloorsFloat()
    {
        Assert.AreEqual("12", Evaluate("$formatInteger(12.6, '###0')").AsString);
    }

    /// <summary><c>$formatInteger</c> appends the ordinal suffix <c>rd</c> for a value ending in three.</summary>
    [TestMethod]
    public void FormatIntegerOrdinalRd()
    {
        Assert.AreEqual("123rd", Evaluate("$formatInteger(123, '000;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> appends the ordinal suffix <c>st</c> for one.</summary>
    [TestMethod]
    public void FormatIntegerOrdinalSt()
    {
        Assert.AreEqual("1st", Evaluate("$formatInteger(1, '0;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> appends the ordinal suffix <c>th</c> for twenty-eight.</summary>
    [TestMethod]
    public void FormatIntegerOrdinalTh()
    {
        Assert.AreEqual("28th", Evaluate("$formatInteger(28, '#0;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders an Arabic-Indic digit family from its picture base digit.</summary>
    [TestMethod]
    public void FormatIntegerArabicIndicFamily()
    {
        Assert.AreEqual("١٢٣٤٠", Evaluate("$formatInteger(12340, '###١')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders a fullwidth digit family from its picture base digit.</summary>
    [TestMethod]
    public void FormatIntegerFullwidthFamily()
    {
        Assert.AreEqual("１２３４０", Evaluate("$formatInteger(12340, '###０')").AsString);
    }

    /// <summary><c>$formatInteger</c> with two distinct digit families raises D3131.</summary>
    [TestMethod]
    public void FormatIntegerMixedFamiliesThrowsD3131()
    {
        Assert.AreEqual("D3131", EvaluateError("$formatInteger(12340, '##0０')"));
    }

    /// <summary><c>$formatInteger</c> inserts regular grouping separators.</summary>
    [TestMethod]
    public void FormatIntegerRegularGrouping()
    {
        Assert.AreEqual("12,345,678", Evaluate("$formatInteger(12345678, '#,##0')").AsString);
    }

    /// <summary><c>$formatInteger</c> inserts irregular grouping at the explicit positions and characters.</summary>
    [TestMethod]
    public void FormatIntegerIrregularGroupingChars()
    {
        Assert.AreEqual("1234:567,890", Evaluate("$formatInteger(1234567890, '#:###,##0')").AsString);
    }

    /// <summary><c>$formatInteger</c> inserts irregular grouping at the explicit positions.</summary>
    [TestMethod]
    public void FormatIntegerIrregularGroupingPositions()
    {
        Assert.AreEqual("12345,67,890", Evaluate("$formatInteger(1234567890, '##,##,##0')").AsString);
    }

    /// <summary><c>$formatInteger</c> of zero in Roman numerals is the empty string.</summary>
    [TestMethod]
    public void FormatIntegerRomanZero()
    {
        Assert.AreEqual("", Evaluate("$formatInteger(0, 'I')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders an upper-case Roman numeral.</summary>
    [TestMethod]
    public void FormatIntegerRomanUpper()
    {
        Assert.AreEqual("MCMLXXXIV", Evaluate("$formatInteger(1984, 'I')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders a lower-case Roman numeral.</summary>
    [TestMethod]
    public void FormatIntegerRomanLower()
    {
        Assert.AreEqual("xcix", Evaluate("$formatInteger(99, 'i')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells a cardinal teen in lower case.</summary>
    [TestMethod]
    public void FormatIntegerWordsTeen()
    {
        Assert.AreEqual("twelve", Evaluate("$formatInteger(12, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells a hyphenated cardinal.</summary>
    [TestMethod]
    public void FormatIntegerWordsHyphen()
    {
        Assert.AreEqual("thirty-four", Evaluate("$formatInteger(34, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an upper-case cardinal.</summary>
    [TestMethod]
    public void FormatIntegerWordsUpper()
    {
        Assert.AreEqual("NINETY-NINE", Evaluate("$formatInteger(99, 'W')").AsString);
    }

    /// <summary><c>$formatInteger</c> joins hundreds and a remainder with <c>and</c>.</summary>
    [TestMethod]
    public void FormatIntegerWordsHundredAnd()
    {
        Assert.AreEqual("nine hundred and nineteen", Evaluate("$formatInteger(919, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> title-cases words while keeping the joining <c>and</c> lower case.</summary>
    [TestMethod]
    public void FormatIntegerWordsTitle()
    {
        Assert.AreEqual("Five Hundred and Fifty-Five", Evaluate("$formatInteger(555, 'Ww')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells one thousand.</summary>
    [TestMethod]
    public void FormatIntegerWordsThousand()
    {
        Assert.AreEqual("one thousand", Evaluate("$formatInteger(1000, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> joins a thousands segment to the lower segment with a comma.</summary>
    [TestMethod]
    public void FormatIntegerWordsThousandsComma()
    {
        Assert.AreEqual("three thousand, seven hundred and thirty", Evaluate("$formatInteger(3730, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells a value just above a trillion with a trailing <c>and one</c>.</summary>
    [TestMethod]
    public void FormatIntegerWordsTrillionAndOne()
    {
        Assert.AreEqual("one trillion and one", Evaluate("$formatInteger(1000000000001, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells a full thirteen-digit value across every magnitude.</summary>
    [TestMethod]
    public void FormatIntegerWordsThirteenDigits()
    {
        Assert.AreEqual(
            "one trillion, two hundred and thirty-four billion, five hundred and sixty-seven million, eight hundred and ninety thousand, one hundred and twenty-three",
            Evaluate("$formatInteger(1234567890123, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> stacks the capped trillion magnitude for ten raised to the fifteen.</summary>
    [TestMethod]
    public void FormatIntegerWordsThousandTrillion()
    {
        Assert.AreEqual("one thousand trillion", Evaluate("$formatInteger(1000000000000000, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> stacks the capped trillion magnitude for ten raised to the forty-six.</summary>
    [TestMethod]
    public void FormatIntegerWordsTenToTheFortySix()
    {
        Assert.AreEqual("ten billion trillion trillion trillion", Evaluate("$formatInteger(1e46, 'w')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal teen.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalTeen()
    {
        Assert.AreEqual("twelfth", Evaluate("$formatInteger(12, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal decade with the <c>ieth</c> transform.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalDecade()
    {
        Assert.AreEqual("twentieth", Evaluate("$formatInteger(20, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal hyphenated value.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalHyphen()
    {
        Assert.AreEqual("ninety-ninth", Evaluate("$formatInteger(99, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal exact hundred with the <c>th</c> transform.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalHundredth()
    {
        Assert.AreEqual("one hundredth", Evaluate("$formatInteger(100, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal exact thousand with the <c>th</c> transform.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalThousandth()
    {
        Assert.AreEqual("one thousandth", Evaluate("$formatInteger(1000, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal value ending in one as <c>first</c>.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalFirst()
    {
        Assert.AreEqual("three thousand, seven hundred and thirty-first", Evaluate("$formatInteger(3731, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells an ordinal teen remainder as <c>thirteenth</c>.</summary>
    [TestMethod]
    public void FormatIntegerWordsOrdinalThirteenth()
    {
        Assert.AreEqual("three hundred and twenty-seven thousand, seven hundred and thirteenth", Evaluate("$formatInteger(327713, 'w;o')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders one as the first spreadsheet column letter.</summary>
    [TestMethod]
    public void FormatIntegerLettersOne()
    {
        Assert.AreEqual("A", Evaluate("$formatInteger(1, 'A')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders twelve as a lower-case letter.</summary>
    [TestMethod]
    public void FormatIntegerLettersTwelve()
    {
        Assert.AreEqual("l", Evaluate("$formatInteger(12, 'a')").AsString);
    }

    /// <summary><c>$formatInteger</c> wraps to a two-letter label at twenty-seven.</summary>
    [TestMethod]
    public void FormatIntegerLettersTwentySeven()
    {
        Assert.AreEqual("aa", Evaluate("$formatInteger(27, 'a')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders a multi-letter upper-case label.</summary>
    [TestMethod]
    public void FormatIntegerLettersThreeHundred()
    {
        Assert.AreEqual("KN", Evaluate("$formatInteger(300, 'A')").AsString);
    }

    /// <summary><c>$formatInteger</c> renders a four-letter upper-case label.</summary>
    [TestMethod]
    public void FormatIntegerLettersLarge()
    {
        Assert.AreEqual("FZPH", Evaluate("$formatInteger(123456, 'A')").AsString);
    }

    /// <summary><c>$formatInteger</c> with a non-digit, non-family sequence raises D3130.</summary>
    [TestMethod]
    public void FormatIntegerSequenceThrowsD3130()
    {
        Assert.AreEqual("D3130", EvaluateError("$formatInteger(123456, 'α')"));
    }

    /// <summary><c>$parseInteger</c> over an undefined value is undefined.</summary>
    [TestMethod]
    public void ParseIntegerUndefinedIsUndefined()
    {
        Assert.IsTrue(Evaluate("$parseInteger(nothing, '0')").IsUndefined);
    }

    /// <summary><c>$parseInteger</c> parses a zero-padded decimal.</summary>
    [TestMethod]
    public void ParseIntegerZeroPadded()
    {
        Assert.AreEqual(123d, Evaluate("$parseInteger('0123', '0000')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> strips the ordinal suffix before parsing.</summary>
    [TestMethod]
    public void ParseIntegerOrdinal()
    {
        Assert.AreEqual(123d, Evaluate("$parseInteger('123rd', '000;o')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> maps an Arabic-Indic digit family back to its value.</summary>
    [TestMethod]
    public void ParseIntegerArabicIndicFamily()
    {
        Assert.AreEqual(12340d, Evaluate("$parseInteger('١٢٣٤٠', '###١')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> strips regular grouping separators.</summary>
    [TestMethod]
    public void ParseIntegerRegularGrouping()
    {
        Assert.AreEqual(12345678d, Evaluate("$parseInteger('12,345,678', '#,##0')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> strips irregular grouping separators of distinct characters.</summary>
    [TestMethod]
    public void ParseIntegerIrregularGrouping()
    {
        Assert.AreEqual(1234567890d, Evaluate("$parseInteger('1234:567,890', '#:###,##0')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> parses the empty Roman numeral as zero.</summary>
    [TestMethod]
    public void ParseIntegerRomanZero()
    {
        Assert.AreEqual(0d, Evaluate("$parseInteger('', 'I')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> parses an upper-case Roman numeral.</summary>
    [TestMethod]
    public void ParseIntegerRomanUpper()
    {
        Assert.AreEqual(1984d, Evaluate("$parseInteger('MCMLXXXIV', 'I')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> upper-cases a lower-case Roman numeral before decoding.</summary>
    [TestMethod]
    public void ParseIntegerRomanLower()
    {
        Assert.AreEqual(99d, Evaluate("$parseInteger('xcix', 'i')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes upper-case cardinal words.</summary>
    [TestMethod]
    public void ParseIntegerWordsUpper()
    {
        Assert.AreEqual(555d, Evaluate("$parseInteger('FIVE HUNDRED AND FIFTY-FIVE', 'W')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes a value just above a trillion.</summary>
    [TestMethod]
    public void ParseIntegerWordsTrillionAndOne()
    {
        Assert.AreEqual(1000000000001d, Evaluate("$parseInteger('one trillion and one', 'w')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes the capped-trillion stack for ten raised to the forty-six.</summary>
    [TestMethod]
    public void ParseIntegerWordsTenToTheFortySix()
    {
        Assert.AreEqual(1e46, Evaluate("$parseInteger('ten billion trillion trillion trillion', 'w')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes an ordinal decade.</summary>
    [TestMethod]
    public void ParseIntegerWordsOrdinalDecade()
    {
        Assert.AreEqual(20d, Evaluate("$parseInteger('twentieth', 'w;o')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes title-cased ordinal words.</summary>
    [TestMethod]
    public void ParseIntegerWordsOrdinalTitle()
    {
        Assert.AreEqual(733d, Evaluate("$parseInteger('Seven Hundred and Thirty-Third', 'Ww;o')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes an ordinal value ending in <c>first</c>.</summary>
    [TestMethod]
    public void ParseIntegerWordsOrdinalFirst()
    {
        Assert.AreEqual(3731d, Evaluate("$parseInteger('three thousand, seven hundred and thirty-first', 'w;o')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes a lower-case spreadsheet column letter.</summary>
    [TestMethod]
    public void ParseIntegerLettersTwelve()
    {
        Assert.AreEqual(12d, Evaluate("$parseInteger('l', 'a')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes a wrapped two-letter label.</summary>
    [TestMethod]
    public void ParseIntegerLettersTwentySeven()
    {
        Assert.AreEqual(27d, Evaluate("$parseInteger('aa', 'a')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> decodes a four-letter upper-case label.</summary>
    [TestMethod]
    public void ParseIntegerLettersLarge()
    {
        Assert.AreEqual(123456d, Evaluate("$parseInteger('FZPH', 'A')").AsNumber);
    }

    /// <summary><c>$parseInteger</c> with an unsupported picture raises D3130.</summary>
    [TestMethod]
    public void ParseIntegerSequenceThrowsD3130()
    {
        Assert.AreEqual("D3130", EvaluateError("$parseInteger('50', '#')"));
    }

    /// <summary><c>$formatInteger</c> treats a non-ordinal format modifier (a semicolon part not starting with <c>o</c>) as cardinal.</summary>
    [TestMethod]
    public void FormatIntegerCardinalModifier()
    {
        Assert.AreEqual("1234", Evaluate("$formatInteger(1234, '0;c')").AsString);
    }

    /// <summary><c>$formatInteger</c> spells a sixteen-digit value as cardinal words, naming each magnitude group down to the trillions.</summary>
    [TestMethod]
    public void FormatIntegerWordsSixteenDigits()
    {
        Assert.AreEqual(
            "one thousand, two hundred and thirty-four trillion, five hundred and sixty-seven billion, eight hundred and ninety million, one hundred and twenty-three thousand, four hundred and fifty-six",
            Evaluate("$formatInteger(1234567890123456, 'w')").AsString);
    }

    /// <summary>Evaluates an expression against an empty object input and returns the result value.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <returns>The result value.</returns>
    private static JsonataValue Evaluate(string expression)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes("{}")));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);
    }

    /// <summary>Evaluates an expression expected to raise a <see cref="JsonataErrorException"/> and returns its error code.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <returns>The raised error code as a string.</returns>
    private static string EvaluateError(string expression)
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate(expression));

        return error.Code.ToString();
    }
}
