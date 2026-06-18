using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Jsonata;

/// <summary>
/// The well-known JSONata runtime error codes, each a UTF-8 <see cref="Utf8String"/> sourced from a <c>u8</c>
/// literal, with an <c>Is…</c> span-comparison helper. This is the single home for the codes the engine
/// raises through <see cref="JsonataErrorException"/> — and, for the non-termination code, through
/// <see cref="JsonataEvaluationLimitException.Code"/> — mirroring the parser's
/// <see cref="Core.Diagnostics.WellKnownDiagnostics"/>: a throw site names the code by its well-known member
/// rather than a scattered string literal, and a consumer classifies a raised code
/// with the matching <c>Is…</c> helper (usable directly in a pattern-match guard).
/// </summary>
/// <remarks>See <see href="https://docs.jsonata.org/processing">the JSONata processing reference</see> and the reference engine's error code table.</remarks>
public static class WellKnownJsonataErrors
{
    /// <summary>An object-constructor key did not evaluate to a string (<c>T1003</c>).</summary>
    public static Utf8String ObjectKeyNotString { get; } = new("T1003"u8.ToArray());

    /// <summary>A non-function value was invoked where a same-named built-in exists (<c>T1005</c>); the message names the suggested <c>$name</c>.</summary>
    public static Utf8String NonFunctionCallSuggestion { get; } = new("T1005"u8.ToArray());

    /// <summary>A non-function value was invoked (<c>T1006</c>).</summary>
    public static Utf8String NonFunctionCall { get; } = new("T1006"u8.ToArray());

    /// <summary>A non-function value was partially applied where a same-named built-in exists (<c>T1007</c>); the message names the suggested <c>$name</c>.</summary>
    public static Utf8String PartialNonFunctionSuggestion { get; } = new("T1007"u8.ToArray());

    /// <summary>A non-function value was partially applied (<c>T1008</c>).</summary>
    public static Utf8String PartialNonFunction { get; } = new("T1008"u8.ToArray());

    /// <summary>A value used where a matcher (a regular expression) was required was neither a regular expression nor a matcher-shaped function (<c>T1010</c>).</summary>
    public static Utf8String NotAMatcher { get; } = new("T1010"u8.ToArray());

    /// <summary>The left operand of an arithmetic operator was not a number (<c>T2001</c>).</summary>
    public static Utf8String ArithmeticLeftNotNumeric { get; } = new("T2001"u8.ToArray());

    /// <summary>The right operand of an arithmetic operator was not a number (<c>T2002</c>).</summary>
    public static Utf8String ArithmeticRightNotNumeric { get; } = new("T2002"u8.ToArray());

    /// <summary>The left bound of the range operator was not an integer (<c>T2003</c>).</summary>
    public static Utf8String RangeLeftNotInteger { get; } = new("T2003"u8.ToArray());

    /// <summary>The right bound of the range operator was not an integer (<c>T2004</c>).</summary>
    public static Utf8String RangeRightNotInteger { get; } = new("T2004"u8.ToArray());

    /// <summary>The right operand of the chain operator <c>~&gt;</c> was not a function (<c>T2006</c>).</summary>
    public static Utf8String ChainRightNotFunction { get; } = new("T2006"u8.ToArray());

    /// <summary>Two order-by keys were of different comparable types (<c>T2007</c>).</summary>
    public static Utf8String OrderByTypeMismatch { get; } = new("T2007"u8.ToArray());

    /// <summary>An order-by key was not a number or a string (<c>T2008</c>).</summary>
    public static Utf8String OrderByKeyNotComparable { get; } = new("T2008"u8.ToArray());

    /// <summary>A comparison was made between two different comparable types (<c>T2009</c>).</summary>
    public static Utf8String ComparisonTypeMismatch { get; } = new("T2009"u8.ToArray());

    /// <summary>A comparison operand was neither a string nor a number (<c>T2010</c>).</summary>
    public static Utf8String ComparisonNotComparable { get; } = new("T2010"u8.ToArray());

    /// <summary>A transform update clause did not evaluate to an object (<c>T2011</c>).</summary>
    public static Utf8String TransformUpdateNotObject { get; } = new("T2011"u8.ToArray());

    /// <summary>A transform delete clause did not evaluate to a string or array of strings (<c>T2012</c>).</summary>
    public static Utf8String TransformDeleteNotStrings { get; } = new("T2012"u8.ToArray());

    /// <summary>A built-in's arguments did not match its signature (<c>T0410</c>).</summary>
    public static Utf8String ArgumentMismatch { get; } = new("T0410"u8.ToArray());

    /// <summary>A context value was incompatible with a context-substituted parameter (<c>T0411</c>).</summary>
    public static Utf8String ContextIncompatible { get; } = new("T0411"u8.ToArray());

    /// <summary>An array argument's elements were not the required subtype (<c>T0412</c>).</summary>
    public static Utf8String WrongElementType { get; } = new("T0412"u8.ToArray());

    /// <summary>An arithmetic result was not a finite number (<c>D1001</c>).</summary>
    public static Utf8String NumberOutOfRange { get; } = new("D1001"u8.ToArray());

    /// <summary>A unary negation was applied to a non-numeric operand (<c>D1002</c>).</summary>
    public static Utf8String NegateNonNumeric { get; } = new("D1002"u8.ToArray());

    /// <summary>A regular-expression match returned a zero-length string and the scan would not progress (<c>D1004</c>).</summary>
    public static Utf8String RegexZeroLengthMatch { get; } = new("D1004"u8.ToArray());

    /// <summary>Two object-constructor member pairs evaluated to the same key (<c>D1009</c>).</summary>
    public static Utf8String DuplicateGroupKey { get; } = new("D1009"u8.ToArray());

    /// <summary>The range operator produced more elements than the maximum (<c>D2014</c>).</summary>
    public static Utf8String RangeTooLarge { get; } = new("D2014"u8.ToArray());

    /// <summary>A non-finite number was coerced to a string (<c>D3001</c>).</summary>
    public static Utf8String NonFiniteString { get; } = new("D3001"u8.ToArray());

    /// <summary>The <c>$replace</c> pattern was the empty string (<c>D3010</c>).</summary>
    public static Utf8String ReplaceEmptyPattern { get; } = new("D3010"u8.ToArray());

    /// <summary>The <c>$replace</c> limit was negative (<c>D3011</c>).</summary>
    public static Utf8String ReplaceNegativeLimit { get; } = new("D3011"u8.ToArray());

    /// <summary>A <c>$replace</c> function replacement returned a value that was not a string (<c>D3012</c>).</summary>
    public static Utf8String ReplaceFunctionNotString { get; } = new("D3012"u8.ToArray());

    /// <summary>The <c>$split</c> limit was negative (<c>D3020</c>).</summary>
    public static Utf8String SplitNegativeLimit { get; } = new("D3020"u8.ToArray());

    /// <summary>A value could not be cast to a number by <c>$number</c> (<c>D3030</c>).</summary>
    public static Utf8String NumberNotCastable { get; } = new("D3030"u8.ToArray());

    /// <summary>The <c>$match</c> limit was negative (<c>D3040</c>).</summary>
    public static Utf8String MatchNegativeLimit { get; } = new("D3040"u8.ToArray());

    /// <summary>A <c>$reduce</c> reducer accepted fewer than two arguments (<c>D3050</c>).</summary>
    public static Utf8String ReduceArity { get; } = new("D3050"u8.ToArray());

    /// <summary><c>$sqrt</c> was applied to a negative number (<c>D3060</c>).</summary>
    public static Utf8String SqrtNegative { get; } = new("D3060"u8.ToArray());

    /// <summary>A <c>$power</c> result was not a finite number (<c>D3061</c>).</summary>
    public static Utf8String PowerNotFinite { get; } = new("D3061"u8.ToArray());

    /// <summary>A default-comparator <c>$sort</c> was over an array that was neither all numbers nor all strings (<c>D3070</c>).</summary>
    public static Utf8String SortDefaultComparatorType { get; } = new("D3070"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> picture string had more than two sub-pictures (<c>D3080</c>).</summary>
    public static Utf8String FormatNumberTooManySubPictures { get; } = new("D3080"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> sub-picture had more than one decimal-separator (<c>D3081</c>).</summary>
    public static Utf8String FormatNumberMultipleDecimalSeparators { get; } = new("D3081"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> sub-picture had more than one percent character (<c>D3082</c>).</summary>
    public static Utf8String FormatNumberMultiplePercent { get; } = new("D3082"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> sub-picture had more than one per-mille character (<c>D3083</c>).</summary>
    public static Utf8String FormatNumberMultiplePerMille { get; } = new("D3083"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> sub-picture had both a percent and a per-mille character (<c>D3084</c>).</summary>
    public static Utf8String FormatNumberPercentAndPerMille { get; } = new("D3084"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> mantissa had no digit-family character and no optional-digit character (<c>D3085</c>).</summary>
    public static Utf8String FormatNumberNoDigit { get; } = new("D3085"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> active part contained a passive character between active characters (<c>D3086</c>).</summary>
    public static Utf8String FormatNumberPassiveCharacter { get; } = new("D3086"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> grouping-separator was adjacent to the decimal-separator (<c>D3087</c>).</summary>
    public static Utf8String FormatNumberGroupingAdjacentToDecimal { get; } = new("D3087"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> integer part ended with a grouping-separator (<c>D3088</c>).</summary>
    public static Utf8String FormatNumberGroupingAtEnd { get; } = new("D3088"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> sub-picture had two consecutive grouping-separators (<c>D3089</c>).</summary>
    public static Utf8String FormatNumberConsecutiveGrouping { get; } = new("D3089"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> integer part had a mandatory digit before an optional-digit character (<c>D3090</c>).</summary>
    public static Utf8String FormatNumberMandatoryDigitBeforeOptional { get; } = new("D3090"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> fractional part had a mandatory digit after an optional-digit character (<c>D3091</c>).</summary>
    public static Utf8String FormatNumberMandatoryDigitAfterOptional { get; } = new("D3091"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> sub-picture had an exponent together with a percent or per-mille character (<c>D3092</c>).</summary>
    public static Utf8String FormatNumberExponentWithPercent { get; } = new("D3092"u8.ToArray());

    /// <summary>A <c>$formatNumber</c> exponent part was empty or contained a non-digit-family character (<c>D3093</c>).</summary>
    public static Utf8String FormatNumberInvalidExponent { get; } = new("D3093"u8.ToArray());

    /// <summary>A <c>$formatBase</c> radix was outside the supported range of 2 to 36 (<c>D3100</c>).</summary>
    public static Utf8String InvalidRadix { get; } = new("D3100"u8.ToArray());

    /// <summary>A <c>$toMillis</c> input was not a valid ISO-8601 timestamp (<c>D3110</c>).</summary>
    public static Utf8String NotIso8601 { get; } = new("D3110"u8.ToArray());

    /// <summary>The expression passed to <c>$eval</c> could not be parsed (<c>D3120</c>).</summary>
    public static Utf8String EvalSyntax { get; } = new("D3120"u8.ToArray());

    /// <summary>A runtime error was raised while evaluating the expression passed to <c>$eval</c> (<c>D3121</c>).</summary>
    public static Utf8String EvalRuntime { get; } = new("D3121"u8.ToArray());

    /// <summary>An integer picture string was an unsupported sequence (<c>D3130</c>).</summary>
    public static Utf8String UnsupportedSequence { get; } = new("D3130"u8.ToArray());

    /// <summary>An integer picture string mixed more than one decimal digit group (<c>D3131</c>).</summary>
    public static Utf8String MixedDigitGroups { get; } = new("D3131"u8.ToArray());

    /// <summary>A date/time picture string contained an unknown component specifier (<c>D3132</c>).</summary>
    public static Utf8String UnknownDateComponent { get; } = new("D3132"u8.ToArray());

    /// <summary>A date/time picture string requested a name for a component that has no name form (<c>D3133</c>).</summary>
    public static Utf8String DateNameNotSupported { get; } = new("D3133"u8.ToArray());

    /// <summary>A date/time timezone format had a mandatory-digit count outside the supported range of 1 to 4 (<c>D3134</c>).</summary>
    public static Utf8String TimezoneDigitCount { get; } = new("D3134"u8.ToArray());

    /// <summary>A date/time picture string had no closing bracket for a marker (<c>D3135</c>).</summary>
    public static Utf8String UnclosedDateMarker { get; } = new("D3135"u8.ToArray());

    /// <summary>A date/time parse produced an inconsistent or unsupported set of components (<c>D3136</c>).</summary>
    public static Utf8String InconsistentDateComponents { get; } = new("D3136"u8.ToArray());

    /// <summary>A user error raised by <c>$error</c> (<c>D3137</c>).</summary>
    public static Utf8String UserError { get; } = new("D3137"u8.ToArray());

    /// <summary>More than one element matched the <c>$single</c> predicate (<c>D3138</c>).</summary>
    public static Utf8String SingleMultipleMatches { get; } = new("D3138"u8.ToArray());

    /// <summary>No element matched the <c>$single</c> predicate (<c>D3139</c>).</summary>
    public static Utf8String SingleNoMatch { get; } = new("D3139"u8.ToArray());

    /// <summary>A malformed URI was passed to an encode/decode function (<c>D3140</c>).</summary>
    public static Utf8String MalformedUri { get; } = new("D3140"u8.ToArray());

    /// <summary>An <c>$assert</c> condition was false (<c>D3141</c>).</summary>
    public static Utf8String AssertionFailed { get; } = new("D3141"u8.ToArray());

    /// <summary>A signature subtype followed a non-array, non-function parameter (<c>S0401</c>).</summary>
    public static Utf8String SubtypeOnNonContainer { get; } = new("S0401"u8.ToArray());

    /// <summary>A signature union contained a parameterised type (<c>S0402</c>).</summary>
    public static Utf8String BracketInUnion { get; } = new("S0402"u8.ToArray());

    /// <summary>Evaluation did not terminate within the recursion-depth or runaway-step bound — a likely non-terminating or too-deeply recursive function (<c>U1001</c>). The engine surfaces this one code for both the depth and the step bound, having no separate time-limit code.</summary>
    public static Utf8String NonTerminatingRecursion { get; } = new("U1001"u8.ToArray());

    /// <summary>Determines whether a code is <see cref="ObjectKeyNotString"/> (<c>T1003</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsObjectKeyNotString(Utf8String code) => Matches(code, ObjectKeyNotString);

    /// <summary>Determines whether a code is <see cref="NonFunctionCallSuggestion"/> (<c>T1005</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNonFunctionCallSuggestion(Utf8String code) => Matches(code, NonFunctionCallSuggestion);

    /// <summary>Determines whether a code is <see cref="NonFunctionCall"/> (<c>T1006</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNonFunctionCall(Utf8String code) => Matches(code, NonFunctionCall);

    /// <summary>Determines whether a code is <see cref="PartialNonFunctionSuggestion"/> (<c>T1007</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsPartialNonFunctionSuggestion(Utf8String code) => Matches(code, PartialNonFunctionSuggestion);

    /// <summary>Determines whether a code is <see cref="PartialNonFunction"/> (<c>T1008</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsPartialNonFunction(Utf8String code) => Matches(code, PartialNonFunction);

    /// <summary>Determines whether a code is <see cref="NotAMatcher"/> (<c>T1010</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNotAMatcher(Utf8String code) => Matches(code, NotAMatcher);

    /// <summary>Determines whether a code is <see cref="ArithmeticLeftNotNumeric"/> (<c>T2001</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsArithmeticLeftNotNumeric(Utf8String code) => Matches(code, ArithmeticLeftNotNumeric);

    /// <summary>Determines whether a code is <see cref="ArithmeticRightNotNumeric"/> (<c>T2002</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsArithmeticRightNotNumeric(Utf8String code) => Matches(code, ArithmeticRightNotNumeric);

    /// <summary>Determines whether a code is <see cref="RangeLeftNotInteger"/> (<c>T2003</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsRangeLeftNotInteger(Utf8String code) => Matches(code, RangeLeftNotInteger);

    /// <summary>Determines whether a code is <see cref="RangeRightNotInteger"/> (<c>T2004</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsRangeRightNotInteger(Utf8String code) => Matches(code, RangeRightNotInteger);

    /// <summary>Determines whether a code is <see cref="ChainRightNotFunction"/> (<c>T2006</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsChainRightNotFunction(Utf8String code) => Matches(code, ChainRightNotFunction);

    /// <summary>Determines whether a code is <see cref="OrderByTypeMismatch"/> (<c>T2007</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsOrderByTypeMismatch(Utf8String code) => Matches(code, OrderByTypeMismatch);

    /// <summary>Determines whether a code is <see cref="OrderByKeyNotComparable"/> (<c>T2008</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsOrderByKeyNotComparable(Utf8String code) => Matches(code, OrderByKeyNotComparable);

    /// <summary>Determines whether a code is <see cref="ComparisonTypeMismatch"/> (<c>T2009</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsComparisonTypeMismatch(Utf8String code) => Matches(code, ComparisonTypeMismatch);

    /// <summary>Determines whether a code is <see cref="ComparisonNotComparable"/> (<c>T2010</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsComparisonNotComparable(Utf8String code) => Matches(code, ComparisonNotComparable);

    /// <summary>Determines whether a code is <see cref="TransformUpdateNotObject"/> (<c>T2011</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsTransformUpdateNotObject(Utf8String code) => Matches(code, TransformUpdateNotObject);

    /// <summary>Determines whether a code is <see cref="TransformDeleteNotStrings"/> (<c>T2012</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsTransformDeleteNotStrings(Utf8String code) => Matches(code, TransformDeleteNotStrings);

    /// <summary>Determines whether a code is <see cref="ArgumentMismatch"/> (<c>T0410</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsArgumentMismatch(Utf8String code) => Matches(code, ArgumentMismatch);

    /// <summary>Determines whether a code is <see cref="ContextIncompatible"/> (<c>T0411</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsContextIncompatible(Utf8String code) => Matches(code, ContextIncompatible);

    /// <summary>Determines whether a code is <see cref="WrongElementType"/> (<c>T0412</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsWrongElementType(Utf8String code) => Matches(code, WrongElementType);

    /// <summary>Determines whether a code is <see cref="NumberOutOfRange"/> (<c>D1001</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNumberOutOfRange(Utf8String code) => Matches(code, NumberOutOfRange);

    /// <summary>Determines whether a code is <see cref="NegateNonNumeric"/> (<c>D1002</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNegateNonNumeric(Utf8String code) => Matches(code, NegateNonNumeric);

    /// <summary>Determines whether a code is <see cref="RegexZeroLengthMatch"/> (<c>D1004</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsRegexZeroLengthMatch(Utf8String code) => Matches(code, RegexZeroLengthMatch);

    /// <summary>Determines whether a code is <see cref="DuplicateGroupKey"/> (<c>D1009</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsDuplicateGroupKey(Utf8String code) => Matches(code, DuplicateGroupKey);

    /// <summary>Determines whether a code is <see cref="RangeTooLarge"/> (<c>D2014</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsRangeTooLarge(Utf8String code) => Matches(code, RangeTooLarge);

    /// <summary>Determines whether a code is <see cref="NonFiniteString"/> (<c>D3001</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNonFiniteString(Utf8String code) => Matches(code, NonFiniteString);

    /// <summary>Determines whether a code is <see cref="ReplaceEmptyPattern"/> (<c>D3010</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsReplaceEmptyPattern(Utf8String code) => Matches(code, ReplaceEmptyPattern);

    /// <summary>Determines whether a code is <see cref="ReplaceNegativeLimit"/> (<c>D3011</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsReplaceNegativeLimit(Utf8String code) => Matches(code, ReplaceNegativeLimit);

    /// <summary>Determines whether a code is <see cref="ReplaceFunctionNotString"/> (<c>D3012</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsReplaceFunctionNotString(Utf8String code) => Matches(code, ReplaceFunctionNotString);

    /// <summary>Determines whether a code is <see cref="SplitNegativeLimit"/> (<c>D3020</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsSplitNegativeLimit(Utf8String code) => Matches(code, SplitNegativeLimit);

    /// <summary>Determines whether a code is <see cref="NumberNotCastable"/> (<c>D3030</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNumberNotCastable(Utf8String code) => Matches(code, NumberNotCastable);

    /// <summary>Determines whether a code is <see cref="MatchNegativeLimit"/> (<c>D3040</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsMatchNegativeLimit(Utf8String code) => Matches(code, MatchNegativeLimit);

    /// <summary>Determines whether a code is <see cref="ReduceArity"/> (<c>D3050</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsReduceArity(Utf8String code) => Matches(code, ReduceArity);

    /// <summary>Determines whether a code is <see cref="SqrtNegative"/> (<c>D3060</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsSqrtNegative(Utf8String code) => Matches(code, SqrtNegative);

    /// <summary>Determines whether a code is <see cref="PowerNotFinite"/> (<c>D3061</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsPowerNotFinite(Utf8String code) => Matches(code, PowerNotFinite);

    /// <summary>Determines whether a code is <see cref="SortDefaultComparatorType"/> (<c>D3070</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsSortDefaultComparatorType(Utf8String code) => Matches(code, SortDefaultComparatorType);

    /// <summary>Determines whether a code is <see cref="FormatNumberTooManySubPictures"/> (<c>D3080</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberTooManySubPictures(Utf8String code) => Matches(code, FormatNumberTooManySubPictures);

    /// <summary>Determines whether a code is <see cref="FormatNumberMultipleDecimalSeparators"/> (<c>D3081</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberMultipleDecimalSeparators(Utf8String code) => Matches(code, FormatNumberMultipleDecimalSeparators);

    /// <summary>Determines whether a code is <see cref="FormatNumberMultiplePercent"/> (<c>D3082</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberMultiplePercent(Utf8String code) => Matches(code, FormatNumberMultiplePercent);

    /// <summary>Determines whether a code is <see cref="FormatNumberMultiplePerMille"/> (<c>D3083</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberMultiplePerMille(Utf8String code) => Matches(code, FormatNumberMultiplePerMille);

    /// <summary>Determines whether a code is <see cref="FormatNumberPercentAndPerMille"/> (<c>D3084</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberPercentAndPerMille(Utf8String code) => Matches(code, FormatNumberPercentAndPerMille);

    /// <summary>Determines whether a code is <see cref="FormatNumberNoDigit"/> (<c>D3085</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberNoDigit(Utf8String code) => Matches(code, FormatNumberNoDigit);

    /// <summary>Determines whether a code is <see cref="FormatNumberPassiveCharacter"/> (<c>D3086</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberPassiveCharacter(Utf8String code) => Matches(code, FormatNumberPassiveCharacter);

    /// <summary>Determines whether a code is <see cref="FormatNumberGroupingAdjacentToDecimal"/> (<c>D3087</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberGroupingAdjacentToDecimal(Utf8String code) => Matches(code, FormatNumberGroupingAdjacentToDecimal);

    /// <summary>Determines whether a code is <see cref="FormatNumberGroupingAtEnd"/> (<c>D3088</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberGroupingAtEnd(Utf8String code) => Matches(code, FormatNumberGroupingAtEnd);

    /// <summary>Determines whether a code is <see cref="FormatNumberConsecutiveGrouping"/> (<c>D3089</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberConsecutiveGrouping(Utf8String code) => Matches(code, FormatNumberConsecutiveGrouping);

    /// <summary>Determines whether a code is <see cref="FormatNumberMandatoryDigitBeforeOptional"/> (<c>D3090</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberMandatoryDigitBeforeOptional(Utf8String code) => Matches(code, FormatNumberMandatoryDigitBeforeOptional);

    /// <summary>Determines whether a code is <see cref="FormatNumberMandatoryDigitAfterOptional"/> (<c>D3091</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberMandatoryDigitAfterOptional(Utf8String code) => Matches(code, FormatNumberMandatoryDigitAfterOptional);

    /// <summary>Determines whether a code is <see cref="FormatNumberExponentWithPercent"/> (<c>D3092</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberExponentWithPercent(Utf8String code) => Matches(code, FormatNumberExponentWithPercent);

    /// <summary>Determines whether a code is <see cref="FormatNumberInvalidExponent"/> (<c>D3093</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsFormatNumberInvalidExponent(Utf8String code) => Matches(code, FormatNumberInvalidExponent);

    /// <summary>Determines whether a code is <see cref="InvalidRadix"/> (<c>D3100</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsInvalidRadix(Utf8String code) => Matches(code, InvalidRadix);

    /// <summary>Determines whether a code is <see cref="NotIso8601"/> (<c>D3110</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNotIso8601(Utf8String code) => Matches(code, NotIso8601);

    /// <summary>Determines whether a code is <see cref="EvalSyntax"/> (<c>D3120</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsEvalSyntax(Utf8String code) => Matches(code, EvalSyntax);

    /// <summary>Determines whether a code is <see cref="EvalRuntime"/> (<c>D3121</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsEvalRuntime(Utf8String code) => Matches(code, EvalRuntime);

    /// <summary>Determines whether a code is <see cref="UnsupportedSequence"/> (<c>D3130</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsUnsupportedSequence(Utf8String code) => Matches(code, UnsupportedSequence);

    /// <summary>Determines whether a code is <see cref="MixedDigitGroups"/> (<c>D3131</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsMixedDigitGroups(Utf8String code) => Matches(code, MixedDigitGroups);

    /// <summary>Determines whether a code is <see cref="UnknownDateComponent"/> (<c>D3132</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsUnknownDateComponent(Utf8String code) => Matches(code, UnknownDateComponent);

    /// <summary>Determines whether a code is <see cref="DateNameNotSupported"/> (<c>D3133</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsDateNameNotSupported(Utf8String code) => Matches(code, DateNameNotSupported);

    /// <summary>Determines whether a code is <see cref="TimezoneDigitCount"/> (<c>D3134</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsTimezoneDigitCount(Utf8String code) => Matches(code, TimezoneDigitCount);

    /// <summary>Determines whether a code is <see cref="UnclosedDateMarker"/> (<c>D3135</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsUnclosedDateMarker(Utf8String code) => Matches(code, UnclosedDateMarker);

    /// <summary>Determines whether a code is <see cref="InconsistentDateComponents"/> (<c>D3136</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsInconsistentDateComponents(Utf8String code) => Matches(code, InconsistentDateComponents);

    /// <summary>Determines whether a code is <see cref="UserError"/> (<c>D3137</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsUserError(Utf8String code) => Matches(code, UserError);

    /// <summary>Determines whether a code is <see cref="SingleMultipleMatches"/> (<c>D3138</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsSingleMultipleMatches(Utf8String code) => Matches(code, SingleMultipleMatches);

    /// <summary>Determines whether a code is <see cref="SingleNoMatch"/> (<c>D3139</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsSingleNoMatch(Utf8String code) => Matches(code, SingleNoMatch);

    /// <summary>Determines whether a code is <see cref="MalformedUri"/> (<c>D3140</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsMalformedUri(Utf8String code) => Matches(code, MalformedUri);

    /// <summary>Determines whether a code is <see cref="AssertionFailed"/> (<c>D3141</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsAssertionFailed(Utf8String code) => Matches(code, AssertionFailed);

    /// <summary>Determines whether a code is <see cref="SubtypeOnNonContainer"/> (<c>S0401</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsSubtypeOnNonContainer(Utf8String code) => Matches(code, SubtypeOnNonContainer);

    /// <summary>Determines whether a code is <see cref="BracketInUnion"/> (<c>S0402</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsBracketInUnion(Utf8String code) => Matches(code, BracketInUnion);

    /// <summary>Determines whether a code is <see cref="NonTerminatingRecursion"/> (<c>U1001</c>).</summary>
    /// <param name="code">The code to test.</param>
    /// <returns><see langword="true"/> when the code matches.</returns>
    public static bool IsNonTerminatingRecursion(Utf8String code) => Matches(code, NonTerminatingRecursion);

    /// <summary>Compares a raised code against a well-known code by their UTF-8 bytes.</summary>
    /// <param name="code">The raised code.</param>
    /// <param name="wellKnown">The well-known code to match against.</param>
    /// <returns><see langword="true"/> when the bytes are equal.</returns>
    private static bool Matches(Utf8String code, Utf8String wellKnown)
    {
        return code.Span.SequenceEqual(wellKnown.Span);
    }
}
