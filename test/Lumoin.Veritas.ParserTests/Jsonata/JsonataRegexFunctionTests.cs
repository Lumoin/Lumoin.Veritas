using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for JSONata regular-expression literals <c>/pattern/flags</c> and the regular-expression branches of
/// the string functions <c>$match</c>, <c>$split</c>, <c>$contains</c>, and <c>$replace</c>: the
/// regex-versus-divide lexer disambiguation, the regex AST node, the four functions including the <c>$N</c>
/// replacement substitution and the case-insensitive flag, the D1004 zero-length guard, the D3040 / D3012
/// errors, the T1010 non-matcher error, and undefined-input handling.
/// </summary>
[TestClass]
internal sealed class JsonataRegexFunctionTests
{
    /// <summary>The JSONata error code for a zero-length regular-expression match that would not progress.</summary>
    private const string CodeRegexZeroLengthMatch = "D1004";

    /// <summary>The JSONata error code for a <c>$replace</c> function replacement that returned a non-string.</summary>
    private const string CodeReplaceFunctionNotString = "D3012";

    /// <summary>The JSONata error code for a negative <c>$match</c> limit.</summary>
    private const string CodeMatchNegativeLimit = "D3040";

    /// <summary>The JSONata error code for a non-regex value used where a matcher is required.</summary>
    private const string CodeNotAMatcher = "T1010";

    /// <summary>A <c>/</c> after a value (a number) is the divide operator: <c>10 / 2</c> is <c>5</c>.</summary>
    [TestMethod]
    public void SlashAfterValueIsDivision()
    {
        Assert.AreEqual(5d, Evaluate("10 / 2", "{}").AsNumber);
    }

    /// <summary>A <c>/pattern/</c> in prefix position parses as a regex literal that is a function value.</summary>
    [TestMethod]
    public void RegexInPrefixPositionIsFunctionValue()
    {
        JsonataValue result = Evaluate("$string(/ab+/)", "{}");

        //A regex value casts to the empty string like any function value, confirming it is a function.
        Assert.AreEqual(string.Empty, result.AsString);
    }

    /// <summary>A regex literal parses to a <see cref="RegexExpression"/> carrying its split pattern and flags.</summary>
    [TestMethod]
    public void RegexParsesToRegexExpression()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("/hat/i"), pool);

        Assert.IsFalse(result.HasErrors);

        RegexExpression regex = (RegexExpression)result.Tree;
        Assert.AreEqual("hat", regex.Pattern.ToString());
        Assert.AreEqual("i", regex.Flags.ToString());
    }

    /// <summary><c>$split</c> on a regex separator splits on each match (regex group conformance case000).</summary>
    [TestMethod]
    public void SplitOnRegexSeparator()
    {
        Assert.AreEqual("[\"a\",\"a\",\"xa\",\"cc\"]", Serialize("$split(\"ababbxabbcc\",/b+/)", "{}"));
    }

    /// <summary><c>$split</c> honours the limit (regex group conformance case001).</summary>
    [TestMethod]
    public void SplitOnRegexSeparatorWithLimit()
    {
        Assert.AreEqual("[\"a\",\"a\"]", Serialize("$split(\"ababbxabbcc\",/b+/, 2)", "{}"));
    }

    /// <summary><c>$split</c> on a non-matching regex yields the whole string (regex group conformance case002).</summary>
    [TestMethod]
    public void SplitOnNonMatchingRegex()
    {
        Assert.AreEqual("[\"ababbxabbcc\"]", Serialize("$split(\"ababbxabbcc\",/d+/)", "{}"));
    }

    /// <summary><c>$contains</c> with a matching regex is true (regex group conformance case003).</summary>
    [TestMethod]
    public void ContainsMatchingRegexIsTrue()
    {
        Assert.IsTrue(Evaluate("$contains(\"ababbxabbcc\",/ab+/)", "{}").AsBoolean);
    }

    /// <summary><c>$contains</c> with a non-matching regex is false (regex group conformance case004).</summary>
    [TestMethod]
    public void ContainsNonMatchingRegexIsFalse()
    {
        Assert.IsFalse(Evaluate("$contains(\"ababbxabbcc\",/ax+/)", "{}").AsBoolean);
    }

    /// <summary><c>$contains</c> with a case-insensitive regex matches across case.</summary>
    [TestMethod]
    public void ContainsCaseInsensitiveRegex()
    {
        Assert.IsTrue(Evaluate("$contains(\"Bowler Hat\", /hat/i)", "{}").AsBoolean);
        Assert.IsFalse(Evaluate("$contains(\"Bowler Hat\", /hat/)", "{}").AsBoolean);
    }

    /// <summary><c>$match</c> returns an array of match objects with the match, index, and groups.</summary>
    [TestMethod]
    public void MatchReturnsMatchObjects()
    {
        Assert.AreEqual(
            "[{\"match\":\"ab\",\"index\":0,\"groups\":[]},{\"match\":\"ab\",\"index\":4,\"groups\":[]}]",
            Serialize("$match(\"abxxabyy\", /ab/)", "{}"));
    }

    /// <summary><c>$match</c> exposes the captured groups, unwrapping a single match to the bare match object.</summary>
    [TestMethod]
    public void MatchExposesGroups()
    {
        Assert.AreEqual(
            "{\"match\":\"John Smith\",\"index\":0,\"groups\":[\"John\",\"Smith\"]}",
            Serialize("$match(\"John Smith\", /(\\w+)\\s(\\w+)/)", "{}"));
    }

    /// <summary><c>$match</c> honours a non-negative limit, keeping at most that many matches (a single match is the bare object).</summary>
    [TestMethod]
    public void MatchHonoursLimit()
    {
        Assert.AreEqual(
            "{\"match\":\"ab\",\"index\":0,\"groups\":[]}",
            Serialize("$match(\"abxxabyy\", /ab/, 1)", "{}"));
    }

    /// <summary><c>$match</c> with no matches is undefined (the reference returns an empty sequence).</summary>
    [TestMethod]
    public void MatchNoMatchIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$match(\"xyz\", /a/)", "{}").Kind);
    }

    /// <summary><c>$match</c> with a negative limit throws D3040.</summary>
    [TestMethod]
    public void MatchNegativeLimitThrowsD3040()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$match(\"abc\", /a/, -1)", "{}"));

        Assert.AreEqual(CodeMatchNegativeLimit, error.Code.ToString());
    }

    /// <summary><c>$replace</c> with a regex replaces every match with the literal replacement (regex group conformance case007).</summary>
    [TestMethod]
    public void ReplaceRegexLiteralReplacement()
    {
        Assert.AreEqual("ayyayyxayycc", Evaluate("$replace(\"ababbxabbcc\",/b+/, \"yy\")", "{}").AsString);
    }

    /// <summary><c>$replace</c> with a regex honours the limit (regex group conformance case008).</summary>
    [TestMethod]
    public void ReplaceRegexWithLimit()
    {
        Assert.AreEqual("ayyayyxabbcc", Evaluate("$replace(\"ababbxabbcc\",/b+/, \"yy\", 2)", "{}").AsString);
    }

    /// <summary><c>$replace</c> with a regex and a zero limit returns the string unchanged (regex group conformance case009).</summary>
    [TestMethod]
    public void ReplaceRegexZeroLimitUnchanged()
    {
        Assert.AreEqual("ababbxabbcc", Evaluate("$replace(\"ababbxabbcc\",/b+/, \"yy\", 0)", "{}").AsString);
    }

    /// <summary>The <c>$N</c> substitution inserts captured groups (regex group conformance case011).</summary>
    [TestMethod]
    public void ReplaceGroupSubstitution()
    {
        Assert.AreEqual("Smith, John", Evaluate("$replace(\"John Smith\", /(\\w+)\\s(\\w+)/, \"$2, $1\")", "{}").AsString);
    }

    /// <summary>The <c>$$</c> substitution inserts a literal dollar (regex group conformance case012).</summary>
    [TestMethod]
    public void ReplaceDollarEscape()
    {
        Assert.AreEqual("$265", Evaluate("$replace(\"265USD\", /([0-9]+)USD/, \"$$$1\")", "{}").AsString);
    }

    /// <summary>The <c>$0</c> substitution inserts the whole match (regex group conformance case014).</summary>
    [TestMethod]
    public void ReplaceWholeMatchSubstitution()
    {
        Assert.AreEqual("265USD -> $265", Evaluate("$replace(\"265USD\", /([0-9]+)USD/, \"$0 -> $$$1\")", "{}").AsString);
    }

    /// <summary>An unmatched alternative group substitutes to nothing (regex group conformance case016).</summary>
    [TestMethod]
    public void ReplaceUnmatchedGroupSubstitutesEmpty()
    {
        Assert.AreEqual("[1=ab][2=]cd", Evaluate("$replace(\"abcd\", /(ab)|(a)/, \"[1=$1][2=$2]\")", "{}").AsString);
    }

    /// <summary>The multi-digit group-index back-off reads as many digits as the group count allows (regex group conformance case028).</summary>
    [TestMethod]
    public void ReplaceMultiDigitGroupBackOff()
    {
        Assert.AreEqual("abcdefgh22823lmno", Evaluate("$replace(\"abcdefghijklmno\", /ijk/, \"$8$5$12$12$18$123\")", "{}").AsString);
    }

    /// <summary>A lazy whole-match zero-length continuation throws D1004 (regex group conformance case022).</summary>
    [TestMethod]
    public void ReplaceZeroLengthMatchThrowsD1004()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$replace(\"abracadabra\", /.*?/, \"$1\")", "{}"));

        Assert.AreEqual(CodeRegexZeroLengthMatch, error.Code.ToString());
    }

    /// <summary>A function replacement is applied per match and its string result spliced in.</summary>
    [TestMethod]
    public void ReplaceFunctionReplacement()
    {
        Assert.AreEqual("Bowler HAT", Evaluate("$replace(\"Bowler hat\", /(h)(at)/i, function($m) { $uppercase($m.match) })", "{}").AsString);
    }

    /// <summary>A function replacement that uses a captured group computes per match (regex group conformance case034).</summary>
    [TestMethod]
    public void ReplaceFunctionReplacementUsesGroups()
    {
        Assert.AreEqual(
            "temperature = 20C today",
            Evaluate("$replace(\"temperature = 68F today\", /(-?\\d+(?:\\.\\d*)?)F\\b/, function($m) { ($number($m.groups[0]) - 32) * 5/9 & \"C\" })", "{}").AsString);
    }

    /// <summary>A function replacement returning a non-string throws D3012 (regex group conformance case035/036).</summary>
    [TestMethod]
    public void ReplaceFunctionReturningNonStringThrowsD3012()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$replace(\"Bowler hat\", /hat/i, function($m) { 42 })", "{}"));

        Assert.AreEqual(CodeReplaceFunctionNotString, error.Code.ToString());
    }

    /// <summary>A non-regex function passed as a matcher throws T1010 (matchers group conformance case001).</summary>
    [TestMethod]
    public void NonRegexFunctionMatcherThrowsT1010()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$split(\"some text\", $uppercase)", "{}"));

        Assert.AreEqual(CodeNotAMatcher, error.Code.ToString());
    }

    /// <summary>A regex matcher function applied directly to a string returns the first match object.</summary>
    [TestMethod]
    public void RegexAppliedDirectlyReturnsFirstMatch()
    {
        Assert.AreEqual(
            "{\"match\":\"ab\",\"index\":0,\"groups\":[]}",
            Serialize("($re := /ab/; $re(\"abxxab\"))", "{}"));
    }

    /// <summary>Each regex-branch function returns undefined for an undefined primary string argument.</summary>
    [TestMethod]
    public void RegexFunctionsOfUndefinedAreUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$match(missing, /a/)", "{}").Kind);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$split(missing, /a/)", "{}").Kind);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$contains(missing, /a/)", "{}").Kind);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$replace(missing, /a/, \"x\")", "{}").Kind);
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
