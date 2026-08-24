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
/// Tests for the JSONata built-in function registry seam and the first batch of pure functions: the
/// name-resolution fallback and user-binding shadowing, the zero-args context substitution, a built-in as a
/// first-class value in <c>~&gt;</c>, and each of the 27 string / numeric / aggregation / boolean functions
/// with its canonical case, edge cases, and documented domain errors.
/// </summary>
[TestClass]
internal sealed class JsonataBuiltinFunctionTests
{
    /// <summary>The JSONata error code for a string or boolean that <c>$number</c> cannot cast to a number.</summary>
    private const string CodeNumberNotCastable = "D3030";

    /// <summary>The JSONata error code for <c>$sqrt</c> of a negative number.</summary>
    private const string CodeSqrtNegative = "D3060";

    /// <summary>The JSONata error code for a <c>$power</c> result that is not a finite number.</summary>
    private const string CodePowerNotFinite = "D3061";

    /// <summary>The JSONata error code for an empty <c>$replace</c> pattern.</summary>
    private const string CodeReplaceEmptyPattern = "D3010";

    /// <summary>The JSONata error code for a negative <c>$replace</c> limit.</summary>
    private const string CodeReplaceNegativeLimit = "D3011";

    /// <summary>The JSONata error code for a negative <c>$split</c> limit.</summary>
    private const string CodeSplitNegativeLimit = "D3020";

    /// <summary>The JSONata error code for applying the call operator to a value that is not a function.</summary>
    private const string CodeNonFunctionCall = "T1006";

    /// <summary>A bare built-in name resolves through the registry and dispatches: <c>$uppercase("hello")</c> is <c>"HELLO"</c>.</summary>
    [TestMethod]
    public void BuiltinResolvesAndDispatches()
    {
        JsonataValue result = Evaluate("$uppercase(\"hello\")", "{}");

        Assert.AreEqual("HELLO", result.AsString);
    }

    /// <summary>A user binding of a built-in name shadows the built-in, yielding the bound value rather than the function.</summary>
    [TestMethod]
    public void UserBindingShadowsBuiltin()
    {
        JsonataValue result = Evaluate("($uppercase := \"x\"; $uppercase)", "{}");

        Assert.AreEqual("x", result.AsString);
    }

    /// <summary>A zero-argument context built-in over a navigated focus takes that focus as its argument.</summary>
    [TestMethod]
    public void ContextSubstitutionOverNavigatedFocus()
    {
        JsonataValue result = Evaluate("Name.$uppercase()", "{ \"Name\": \"bob\" }");

        Assert.AreEqual("BOB", result.AsString);
    }

    /// <summary>A zero-argument context built-in over a string root takes the root as its argument.</summary>
    [TestMethod]
    public void ContextSubstitutionOverStringRoot()
    {
        JsonataValue result = Evaluate("$uppercase()", "\"root\"");

        Assert.AreEqual("ROOT", result.AsString);
    }

    /// <summary>A built-in is a first-class value usable as the right operand of the chain operator <c>~&gt;</c>.</summary>
    [TestMethod]
    public void BuiltinIsFirstClassInChain()
    {
        JsonataValue result = Evaluate("\"abc\" ~> $uppercase", "{}");

        Assert.AreEqual("ABC", result.AsString);
    }

    /// <summary>Calling a bound non-function value still throws T1006.</summary>
    [TestMethod]
    public void CallingNonFunctionThrowsT1006()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("($x := 5; $x(3))", "{}"));

        Assert.AreEqual(CodeNonFunctionCall, error.Code.ToString());
    }

    /// <summary>An unresolved built-in name stays undefined when read (no throw).</summary>
    [TestMethod]
    public void UnknownNameStaysUndefined()
    {
        JsonataValue result = Evaluate("$noSuchFunction", "{}");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary><c>$string</c> returns a string verbatim and formats each scalar kind.</summary>
    [TestMethod]
    public void StringFormatsScalars()
    {
        Assert.AreEqual("text", Evaluate("$string(\"text\")", "{}").AsString);
        Assert.AreEqual("42", Evaluate("$string(42)", "{}").AsString);
        Assert.AreEqual("3.5", Evaluate("$string(3.5)", "{}").AsString);
        Assert.AreEqual("true", Evaluate("$string(true)", "{}").AsString);
        Assert.AreEqual("null", Evaluate("$string(null)", "{}").AsString);
    }

    /// <summary><c>$string</c> of an array or object is its compact JSON.</summary>
    [TestMethod]
    public void StringSerializesContainers()
    {
        Assert.AreEqual("[1,2,3]", Evaluate("$string([1, 2, 3])", "{}").AsString);
        Assert.AreEqual("{\"a\":1}", Evaluate("$string({\"a\": 1})", "{}").AsString);
    }

    /// <summary><c>$string</c> of a function value (a lambda) is the empty string.</summary>
    [TestMethod]
    public void StringOfFunctionIsEmpty()
    {
        Assert.AreEqual(string.Empty, Evaluate("$string(function($x){$x})", "{}").AsString);
    }

    /// <summary><c>$string</c> of a built-in function value is the empty string.</summary>
    [TestMethod]
    public void StringOfBuiltinIsEmpty()
    {
        Assert.AreEqual(string.Empty, Evaluate("$string($uppercase)", "{}").AsString);
    }

    /// <summary><c>$string</c> of undefined is undefined.</summary>
    [TestMethod]
    public void StringOfUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$string(missing)", "{}").Kind);
    }

    /// <summary><c>$string</c> of a non-integer applies the reference's <c>toPrecision(15)</c> reduction, so <c>22/7</c> drops float noise to fifteen significant figures.</summary>
    [TestMethod]
    public void StringReducesNonIntegerToFifteenSignificantFigures()
    {
        Assert.AreEqual("3.14285714285714", Evaluate("$string(22/7)", "{}").AsString);
    }

    /// <summary><c>$string</c> renders a number in ECMAScript <c>Number::toString</c> form: fixed-point at the boundaries and exponential beyond them, with a signed exponent and no leading zeroes.</summary>
    [TestMethod]
    public void StringFormatsNumberPerEcmaScript()
    {
        Assert.AreEqual("0.000001", Evaluate("$string(1e-6)", "{}").AsString);
        Assert.AreEqual("1e-7", Evaluate("$string(1e-7)", "{}").AsString);
        Assert.AreEqual("100000000000000000000", Evaluate("$string(1e20)", "{}").AsString);
        Assert.AreEqual("1e+21", Evaluate("$string(1e21)", "{}").AsString);
        Assert.AreEqual("1e+100", Evaluate("$string(1e100)", "{}").AsString);
        Assert.AreEqual("1e-100", Evaluate("$string(1e-100)", "{}").AsString);
    }

    /// <summary><c>$string</c> renders a negative number and an integer-valued double without spurious precision or a decimal point.</summary>
    [TestMethod]
    public void StringFormatsNegativeAndIntegerNumbers()
    {
        Assert.AreEqual("-3.5", Evaluate("$string(-3.5)", "{}").AsString);
        Assert.AreEqual("0", Evaluate("$string(0)", "{}").AsString);
        Assert.AreEqual("100", Evaluate("$string(100)", "{}").AsString);
        Assert.AreEqual("-100000000000000000000", Evaluate("$string(-1e20)", "{}").AsString);
    }

    /// <summary><c>$string</c> of a structure renders any nested function or lambda as the empty string, recursively.</summary>
    [TestMethod]
    public void StringRendersNestedFunctionsAsEmpty()
    {
        string result = Evaluate("$string({\"f\": $sum, \"o\": {\"g\": function($x){$x}}})", "{}").AsString;

        Assert.AreEqual("{\"f\":\"\",\"o\":{\"g\":\"\"}}", result);
    }

    /// <summary><c>$string</c> with a true second argument two-space prettifies an object.</summary>
    [TestMethod]
    public void StringPrettifiesObject()
    {
        Assert.AreEqual("{\n  \"string\": \"hello\"\n}", Evaluate("$string({\"string\": \"hello\"}, true)", "{}").AsString);
    }

    /// <summary><c>$string</c> with a true second argument two-space prettifies an array.</summary>
    [TestMethod]
    public void StringPrettifiesArray()
    {
        Assert.AreEqual("[\n  \"string\",\n  5\n]", Evaluate("$string([\"string\", 5], true)", "{}").AsString);
    }

    /// <summary><c>$string</c> with a false second argument is compact.</summary>
    [TestMethod]
    public void StringCompactWhenPrettifyFalse()
    {
        Assert.AreEqual("{\"string\":\"hello\"}", Evaluate("$string({\"string\": \"hello\"}, false)", "{}").AsString);
    }

    /// <summary><c>$string</c> of a bare non-finite number (an infinity from a divide-by-zero) throws D3001.</summary>
    [TestMethod]
    public void StringOfBareNonFiniteThrowsD3001()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$string(1/0)", "{}"));

        Assert.AreEqual("D3001", error.Code.ToString());
    }

    /// <summary><c>$string</c> of a structure holding a non-finite number throws D1001 (the reference's nested <c>isNumeric</c> guard), distinct from the bare-number D3001.</summary>
    [TestMethod]
    public void StringOfStructureWithNonFiniteThrowsD1001()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$string({\"inf\": 1/0})", "{}"));

        Assert.AreEqual("D1001", error.Code.ToString());
    }

    /// <summary>The string-concatenation operator coerces a non-integer number through the same <c>toPrecision(15)</c> reduction as <c>$string</c>.</summary>
    [TestMethod]
    public void ConcatCoercesNumberLikeString()
    {
        Assert.AreEqual("x3.14285714285714", Evaluate("\"x\" & (22/7)", "{}").AsString);
    }

    /// <summary><c>$length</c> counts codepoints, so a non-BMP character counts as one.</summary>
    [TestMethod]
    public void LengthCountsCodepoints()
    {
        Assert.AreEqual(5d, Evaluate("$length(\"hello\")", "{}").AsNumber);
        Assert.AreEqual(0d, Evaluate("$length(\"\")", "{}").AsNumber);
        Assert.AreEqual(1d, Evaluate("$length(\"\\uD83D\\uDE00\")", "{}").AsNumber);
    }

    /// <summary><c>$substring</c> with a start and length slices the codepoint array.</summary>
    [TestMethod]
    public void SubstringStartAndLength()
    {
        Assert.AreEqual("ell", Evaluate("$substring(\"hello\", 1, 3)", "{}").AsString);
    }

    /// <summary><c>$substring</c> with a negative start counts from the end.</summary>
    [TestMethod]
    public void SubstringNegativeStartCountsFromEnd()
    {
        Assert.AreEqual("lo", Evaluate("$substring(\"hello\", -2)", "{}").AsString);
    }

    /// <summary><c>$substring</c> with a non-positive length is the empty string.</summary>
    [TestMethod]
    public void SubstringNonPositiveLengthIsEmpty()
    {
        Assert.AreEqual(string.Empty, Evaluate("$substring(\"hello\", 0, -2)", "{}").AsString);
    }

    /// <summary><c>$substringBefore</c> returns the prefix before a separator, or the whole string when absent.</summary>
    [TestMethod]
    public void SubstringBeforeFindsPrefix()
    {
        Assert.AreEqual("Hello", Evaluate("$substringBefore(\"Hello World\", \" \")", "{}").AsString);
        Assert.AreEqual("Hello World", Evaluate("$substringBefore(\"Hello World\", \"x\")", "{}").AsString);
    }

    /// <summary><c>$substringAfter</c> returns the suffix after a separator, or the whole string when absent.</summary>
    [TestMethod]
    public void SubstringAfterFindsSuffix()
    {
        Assert.AreEqual("World", Evaluate("$substringAfter(\"Hello World\", \" \")", "{}").AsString);
        Assert.AreEqual("Hello World", Evaluate("$substringAfter(\"Hello World\", \"x\")", "{}").AsString);
    }

    /// <summary><c>$uppercase</c> and <c>$lowercase</c> are culture-invariant.</summary>
    [TestMethod]
    public void CaseFunctionsAreInvariant()
    {
        Assert.AreEqual("ABC", Evaluate("$uppercase(\"abc\")", "{}").AsString);
        Assert.AreEqual("abc", Evaluate("$lowercase(\"ABC\")", "{}").AsString);
    }

    /// <summary><c>$trim</c> collapses interior whitespace runs and strips the ends.</summary>
    [TestMethod]
    public void TrimCollapsesAndStrips()
    {
        Assert.AreEqual("Hello World", Evaluate("$trim(\"  Hello \\n\\t World  \")", "{}").AsString);
        Assert.AreEqual(string.Empty, Evaluate("$trim(\"   \")", "{}").AsString);
    }

    /// <summary><c>$pad</c> with a positive width right-pads, a negative width left-pads.</summary>
    [TestMethod]
    public void PadLeftAndRight()
    {
        Assert.AreEqual("foo--", Evaluate("$pad(\"foo\", 5, \"-\")", "{}").AsString);
        Assert.AreEqual("--foo", Evaluate("$pad(\"foo\", -5, \"-\")", "{}").AsString);
        Assert.AreEqual("foo  ", Evaluate("$pad(\"foo\", 5)", "{}").AsString);
    }

    /// <summary><c>$pad</c> returns the string unchanged when the target width is not wider.</summary>
    [TestMethod]
    public void PadNoWideningReturnsInput()
    {
        Assert.AreEqual("foo", Evaluate("$pad(\"foo\", 2, \"-\")", "{}").AsString);
    }

    /// <summary><c>$contains</c> tests a string token, with the empty token always contained.</summary>
    [TestMethod]
    public void ContainsTestsToken()
    {
        Assert.IsTrue(Evaluate("$contains(\"Hello\", \"ell\")", "{}").AsBoolean);
        Assert.IsFalse(Evaluate("$contains(\"Hello\", \"xyz\")", "{}").AsBoolean);
        Assert.IsTrue(Evaluate("$contains(\"Hello\", \"\")", "{}").AsBoolean);
    }

    /// <summary><c>$split</c> on a separator yields the pieces.</summary>
    [TestMethod]
    public void SplitOnSeparator()
    {
        Assert.AreEqual("[\"a\",\"b\",\"c\"]", Serialize("$split(\"a,b,c\", \",\")", "{}"));
    }

    /// <summary><c>$split</c> truncates to the limit, dropping the tail rather than joining it.</summary>
    [TestMethod]
    public void SplitTruncatesToLimit()
    {
        Assert.AreEqual("[\"a\",\"b\"]", Serialize("$split(\"a,b,c,d\", \",\", 2)", "{}"));
    }

    /// <summary><c>$split</c> with an empty separator splits into UTF-16 code units.</summary>
    [TestMethod]
    public void SplitEmptySeparatorSplitsCodeUnits()
    {
        Assert.AreEqual("[\"a\",\"b\",\"c\"]", Serialize("$split(\"abc\", \"\")", "{}"));
    }

    /// <summary><c>$split</c> with a zero limit yields the empty array.</summary>
    [TestMethod]
    public void SplitZeroLimitIsEmptyArray()
    {
        Assert.AreEqual("[]", Serialize("$split(\"a,b\", \",\", 0)", "{}"));
    }

    /// <summary><c>$split</c> with a negative limit throws D3020.</summary>
    [TestMethod]
    public void SplitNegativeLimitThrowsD3020()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$split(\"a,b\", \",\", -1)", "{}"));

        Assert.AreEqual(CodeSplitNegativeLimit, error.Code.ToString());
    }

    /// <summary><c>$join</c> concatenates the array's strings with a separator.</summary>
    [TestMethod]
    public void JoinConcatenatesWithSeparator()
    {
        Assert.AreEqual("a-b-c", Evaluate("$join([\"a\", \"b\", \"c\"], \"-\")", "{}").AsString);
        Assert.AreEqual("abc", Evaluate("$join([\"a\", \"b\", \"c\"])", "{}").AsString);
        Assert.AreEqual(string.Empty, Evaluate("$join([])", "{}").AsString);
    }

    /// <summary><c>$replace</c> replaces all non-overlapping occurrences of a literal pattern.</summary>
    [TestMethod]
    public void ReplaceAllOccurrences()
    {
        Assert.AreEqual("jo*n smit*", Evaluate("$replace(\"john smith\", \"h\", \"*\")", "{}").AsString);
    }

    /// <summary><c>$replace</c> honours the occurrence limit.</summary>
    [TestMethod]
    public void ReplaceHonoursLimit()
    {
        Assert.AreEqual("**oo", Evaluate("$replace(\"ffoo\", \"f\", \"*\", 2)", "{}").AsString);
        Assert.AreEqual("foo", Evaluate("$replace(\"foo\", \"o\", \"*\", 0)", "{}").AsString);
    }

    /// <summary><c>$replace</c> with an empty pattern throws D3010.</summary>
    [TestMethod]
    public void ReplaceEmptyPatternThrowsD3010()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$replace(\"foo\", \"\", \"x\")", "{}"));

        Assert.AreEqual(CodeReplaceEmptyPattern, error.Code.ToString());
    }

    /// <summary><c>$replace</c> with a negative limit throws D3011.</summary>
    [TestMethod]
    public void ReplaceNegativeLimitThrowsD3011()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$replace(\"foo\", \"o\", \"x\", -1)", "{}"));

        Assert.AreEqual(CodeReplaceNegativeLimit, error.Code.ToString());
    }

    /// <summary><c>$number</c> parses a decimal string and passes a number through.</summary>
    [TestMethod]
    public void NumberParsesDecimal()
    {
        Assert.AreEqual(3.14d, Evaluate("$number(\"3.14\")", "{}").AsNumber);
        Assert.AreEqual(7d, Evaluate("$number(\"007\")", "{}").AsNumber);
        Assert.AreEqual(-12d, Evaluate("$number(\"-12\")", "{}").AsNumber);
        Assert.AreEqual(150d, Evaluate("$number(\"1.5e2\")", "{}").AsNumber);
    }

    /// <summary><c>$number</c> parses explicit-prefix hex, octal, and binary integers.</summary>
    [TestMethod]
    public void NumberParsesPrefixedIntegers()
    {
        Assert.AreEqual(18d, Evaluate("$number(\"0x12\")", "{}").AsNumber);
        Assert.AreEqual(15d, Evaluate("$number(\"0o17\")", "{}").AsNumber);
        Assert.AreEqual(5d, Evaluate("$number(\"0b101\")", "{}").AsNumber);
    }

    /// <summary><c>$number</c> casts a boolean to 1 or 0.</summary>
    [TestMethod]
    public void NumberCastsBoolean()
    {
        Assert.AreEqual(1d, Evaluate("$number(true)", "{}").AsNumber);
        Assert.AreEqual(0d, Evaluate("$number(false)", "{}").AsNumber);
    }

    /// <summary><c>$number</c> of an unparseable string throws D3030.</summary>
    [TestMethod]
    public void NumberRejectsUnparseableStringD3030()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$number(\"abc\")", "{}"));

        Assert.AreEqual(CodeNumberNotCastable, error.Code.ToString());
    }

    /// <summary><c>$number</c> rejects a malformed decimal (a missing digit beside the point) with D3030.</summary>
    [TestMethod]
    public void NumberRejectsMalformedDecimalD3030()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$number(\".5\")", "{}"));

        Assert.AreEqual(CodeNumberNotCastable, error.Code.ToString());
    }

    /// <summary><c>$abs</c> is the absolute value.</summary>
    [TestMethod]
    public void AbsTakesMagnitude()
    {
        Assert.AreEqual(5d, Evaluate("$abs(-5)", "{}").AsNumber);
        Assert.AreEqual(5d, Evaluate("$abs(5)", "{}").AsNumber);
    }

    /// <summary><c>$floor</c> rounds toward negative infinity, <c>$ceil</c> toward positive infinity, with negative zero normalized.</summary>
    [TestMethod]
    public void FloorAndCeilRoundDirectionally()
    {
        Assert.AreEqual(-2d, Evaluate("$floor(-1.5)", "{}").AsNumber);
        Assert.AreEqual(-1d, Evaluate("$ceil(-1.5)", "{}").AsNumber);

        JsonataValue zero = Evaluate("$ceil(-0.5)", "{}");
        Assert.AreEqual(0d, zero.AsNumber);
        Assert.IsFalse(double.IsNegative(zero.AsNumber));
    }

    /// <summary><c>$sqrt</c> is the non-negative square root; a negative argument throws D3060.</summary>
    [TestMethod]
    public void SqrtComputesAndGuardsNegative()
    {
        Assert.AreEqual(3d, Evaluate("$sqrt(9)", "{}").AsNumber);

        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$sqrt(-1)", "{}"));
        Assert.AreEqual(CodeSqrtNegative, error.Code.ToString());
    }

    /// <summary><c>$sqrt</c> of a negative-zero input normalizes the result to positive zero, like the other numeric functions.</summary>
    [TestMethod]
    public void SqrtNormalizesNegativeZero()
    {
        Assert.AreEqual("0", JsonataEngine.SerializeToJson(Evaluate("$sqrt(-1 * 0)", "{}")).ToString());
    }

    /// <summary><c>$round</c> rounds half to even (banker's rounding).</summary>
    [TestMethod]
    public void RoundHalfToEven()
    {
        Assert.AreEqual(0d, Evaluate("$round(0.5)", "{}").AsNumber);
        Assert.AreEqual(2d, Evaluate("$round(1.5)", "{}").AsNumber);
        Assert.AreEqual(2d, Evaluate("$round(2.5)", "{}").AsNumber);
        Assert.AreEqual(4d, Evaluate("$round(3.5)", "{}").AsNumber);
        Assert.AreEqual(12d, Evaluate("$round(12.5)", "{}").AsNumber);
    }

    /// <summary><c>$round</c> with a positive precision rounds to decimal places.</summary>
    [TestMethod]
    public void RoundWithPositivePrecision()
    {
        Assert.AreEqual(123.46d, Evaluate("$round(123.456, 2)", "{}").AsNumber);
        Assert.AreEqual(123d, Evaluate("$round(123.456)", "{}").AsNumber);
    }

    /// <summary><c>$round</c> with a negative precision rounds to tens.</summary>
    [TestMethod]
    public void RoundWithNegativePrecision()
    {
        Assert.AreEqual(120d, Evaluate("$round(125, -1)", "{}").AsNumber);
    }

    /// <summary>
    /// <c>$round</c> scales through the shortest round-trippable decimal form, so a value that prints as an
    /// exact decimal half (whose binary representation is marginally below it) still rounds half to even
    /// rather than down, matching the reference.
    /// </summary>
    [TestMethod]
    public void RoundHalfBoundaryUsesShortestDecimalForm()
    {
        Assert.AreEqual(8.58d, Evaluate("$round(8.575, 2)", "{}").AsNumber);
        Assert.AreEqual(1.26d, Evaluate("$round(1.255, 2)", "{}").AsNumber);
        Assert.AreEqual(35.86d, Evaluate("$round(35.855, 2)", "{}").AsNumber);
    }

    /// <summary><c>$power</c> raises the base to the exponent.</summary>
    [TestMethod]
    public void PowerRaises()
    {
        Assert.AreEqual(256d, Evaluate("$power(2, 8)", "{}").AsNumber);
        Assert.AreEqual(0.25d, Evaluate("$power(2, -2)", "{}").AsNumber);
    }

    /// <summary><c>$power</c> with a non-finite result (a negative base and fractional exponent) throws D3061.</summary>
    [TestMethod]
    public void PowerNonFiniteThrowsD3061()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$power(-2, 0.5)", "{}"));

        Assert.AreEqual(CodePowerNotFinite, error.Code.ToString());
    }

    /// <summary><c>$sum</c> totals an array and wraps a lone value.</summary>
    [TestMethod]
    public void SumTotalsAndWraps()
    {
        Assert.AreEqual(6d, Evaluate("$sum([1, 2, 3])", "{}").AsNumber);
        Assert.AreEqual(5d, Evaluate("$sum(5)", "{}").AsNumber);
    }

    /// <summary><c>$max</c>, <c>$min</c>, and <c>$average</c> aggregate an array.</summary>
    [TestMethod]
    public void MaxMinAverageAggregate()
    {
        Assert.AreEqual(3d, Evaluate("$max([1, 2, 3])", "{}").AsNumber);
        Assert.AreEqual(1d, Evaluate("$min([1, 2, 3])", "{}").AsNumber);
        Assert.AreEqual(2d, Evaluate("$average([1, 2, 3])", "{}").AsNumber);
    }

    /// <summary>The empty-array result is asymmetric: <c>$sum([])</c> is 0 but <c>$max</c>/<c>$min</c>/<c>$average</c> are undefined.</summary>
    [TestMethod]
    public void EmptyArrayAggregationAsymmetry()
    {
        Assert.AreEqual(0d, Evaluate("$sum([])", "{}").AsNumber);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$max([])", "{}").Kind);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$min([])", "{}").Kind);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$average([])", "{}").Kind);
    }

    /// <summary><c>$boolean</c> casts to JSONata truthiness; <c>$not</c> negates it.</summary>
    [TestMethod]
    public void BooleanAndNotCastTruthiness()
    {
        Assert.IsTrue(Evaluate("$boolean(\"x\")", "{}").AsBoolean);
        Assert.IsFalse(Evaluate("$boolean(\"\")", "{}").AsBoolean);
        Assert.IsFalse(Evaluate("$boolean(0)", "{}").AsBoolean);
        Assert.IsTrue(Evaluate("$boolean([0, 1])", "{}").AsBoolean);
        Assert.IsFalse(Evaluate("$not(\"x\")", "{}").AsBoolean);
        Assert.IsTrue(Evaluate("$not(0)", "{}").AsBoolean);
    }

    /// <summary><c>$boolean</c> of undefined is undefined.</summary>
    [TestMethod]
    public void BooleanOfUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$boolean(missing)", "{}").Kind);
    }

    /// <summary><c>$exists</c> distinguishes a present value (including null) from the undefined value.</summary>
    [TestMethod]
    public void ExistsDistinguishesNullFromUndefined()
    {
        Assert.IsTrue(Evaluate("$exists(null)", "{}").AsBoolean);
        Assert.IsTrue(Evaluate("$exists(0)", "{}").AsBoolean);
        Assert.IsFalse(Evaluate("$exists(missing)", "{}").AsBoolean);
    }

    /// <summary>Evaluates a JSONata expression against a JSON input document, parsing input through the host adapter.</summary>
    /// <param name="expression">The JSONata expression source.</param>
    /// <param name="inputJson">The input JSON document text.</param>
    /// <returns>The normalized result value.</returns>
    private static JsonataValue Evaluate(string expression, string inputJson)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(inputJson)));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);
    }

    /// <summary>Evaluates a JSONata expression and serializes the result to compact JSON text.</summary>
    /// <param name="expression">The JSONata expression source.</param>
    /// <param name="inputJson">The input JSON document text.</param>
    /// <returns>The result serialized as JSON text.</returns>
    private static string Serialize(string expression, string inputJson)
    {
        return JsonataEngine.SerializeToJson(Evaluate(expression, inputJson)).ToString();
    }
}
