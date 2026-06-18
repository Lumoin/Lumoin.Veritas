using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Execution;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for the self-contained encoding / error built-ins: <c>$zip</c>, <c>$base64encode</c> /
/// <c>$base64decode</c>, the URI pair <c>$encodeUrl</c> / <c>$decodeUrl</c> and <c>$encodeUrlComponent</c> /
/// <c>$decodeUrlComponent</c> (including the D3140 malformed-URI errors), and the error-raisers
/// <c>$error</c> (D3137) and <c>$assert</c> (D3141, with T0410 for a non-boolean condition).
/// </summary>
[TestClass]
internal sealed class JsonataEncodingAndErrorFunctionTests
{
    /// <summary><c>$zip</c> convolves two arrays position-wise.</summary>
    [TestMethod]
    public void ZipTwoArrays()
    {
        Assert.AreEqual("[[1,4],[2,5],[3,6]]", EvaluateToJson("$zip([1,2,3],[4,5,6])"));
    }

    /// <summary><c>$zip</c> truncates to the shortest argument.</summary>
    [TestMethod]
    public void ZipTruncatesToShortest()
    {
        Assert.AreEqual("[[1,4,7],[2,5,8]]", EvaluateToJson("$zip([1,2,3],[4,5],[7,8,9])"));
    }

    /// <summary><c>$zip</c> over a single array wraps each element in a one-element tuple.</summary>
    [TestMethod]
    public void ZipSingleArray()
    {
        Assert.AreEqual("[[1],[2],[3]]", EvaluateToJson("$zip([1,2,3])"));
    }

    /// <summary><c>$zip</c> treats a scalar argument as a one-element array, so all-scalar arguments yield one tuple.</summary>
    [TestMethod]
    public void ZipScalarsYieldOneTuple()
    {
        Assert.AreEqual("[[1,2,3]]", EvaluateToJson("$zip(1,2,3)"));
    }

    /// <summary><c>$zip</c> with an undefined argument (length zero) collapses to the empty array.</summary>
    [TestMethod]
    public void ZipWithUndefinedIsEmpty()
    {
        Assert.AreEqual("[]", EvaluateToJson("$zip([1,2,3], [4,5,6], nothing)"));
    }

    /// <summary><c>$base64encode</c> encodes the Latin-1 byte sequence to Base64.</summary>
    [TestMethod]
    public void Base64EncodeRoundTrips()
    {
        Assert.AreEqual("aGVsbG86d29ybGQ=", Evaluate("$base64encode(\"hello:world\")").AsString);
        Assert.AreEqual("hello:world", Evaluate("$base64decode(\"aGVsbG86d29ybGQ=\")").AsString);
    }

    /// <summary><c>$base64encode</c> over no argument and an undefined focus is undefined (the context-substituted focus is the undefined value).</summary>
    [TestMethod]
    public void Base64EncodeNoArgumentIsUndefined()
    {
        JsonataValue result = JsonataEvaluator.Evaluate(JsonataEngine.Parse(Encoding.UTF8.GetBytes("$base64encode()")).Tree, JsonataValue.Undefined);

        Assert.IsTrue(result.IsUndefined);
    }

    /// <summary><c>$encodeUrl</c> leaves the reserved URI characters and percent-encodes the rest as UTF-8.</summary>
    [TestMethod]
    public void EncodeUrlKeepsReservedCharacters()
    {
        Assert.AreEqual("https://mozilla.org/?x=%D1%88%D0%B5%D0%BB%D0%BB%D1%8B", Evaluate("$encodeUrl(\"https://mozilla.org/?x=шеллы\")").AsString);
    }

    /// <summary><c>$encodeUrlComponent</c> percent-encodes the component-reserved characters.</summary>
    [TestMethod]
    public void EncodeUrlComponentEscapesReserved()
    {
        Assert.AreEqual("%3Fx%3Dtest", Evaluate("$encodeUrlComponent(\"?x=test\")").AsString);
    }

    /// <summary><c>$decodeUrl</c> decodes the percent-escapes back to the original string.</summary>
    [TestMethod]
    public void DecodeUrlRoundTrips()
    {
        Assert.AreEqual("https://mozilla.org/?x=шеллы", Evaluate("$decodeUrl(\"https://mozilla.org/?x=%D1%88%D0%B5%D0%BB%D0%BB%D1%8B\")").AsString);
    }

    /// <summary><c>$decodeUrlComponent</c> decodes the component percent-escapes.</summary>
    [TestMethod]
    public void DecodeUrlComponentDecodes()
    {
        Assert.AreEqual("?x=test", Evaluate("$decodeUrlComponent(\"%3Fx%3Dtest\")").AsString);
    }

    /// <summary>
    /// Encoding a lone surrogate cannot be UTF-8 encoded, so it raises D3140. The surrogate is supplied through
    /// the input value rather than the expression source, since a lone surrogate cannot survive UTF-8 encoding
    /// of the expression text.
    /// </summary>
    [TestMethod]
    public void EncodeUrlLoneSurrogateThrowsD3140()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("$encodeUrl($)")).Tree;
        JsonataValue loneSurrogate = JsonataValue.String("\uD800");

        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => JsonataEvaluator.Evaluate(tree, loneSurrogate));

        Assert.AreEqual("D3140", error.Code.ToString());
    }

    /// <summary>A malformed percent-escape raises D3140.</summary>
    [TestMethod]
    public void DecodeUrlMalformedThrowsD3140()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$decodeUrl('%E0%A4%A')"));

        Assert.AreEqual("D3140", error.Code.ToString());
    }

    /// <summary><c>$error</c> raises D3137 with the supplied message.</summary>
    [TestMethod]
    public void ErrorRaisesD3137()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$error('Too Expensive')"));

        Assert.AreEqual("D3137", error.Code.ToString());
    }

    /// <summary><c>$error</c> over an undefined message still raises D3137 (with its default message).</summary>
    [TestMethod]
    public void ErrorOnUndefinedMessageRaisesD3137()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$error(foo)"));

        Assert.AreEqual("D3137", error.Code.ToString());
    }

    /// <summary><c>$error</c> over a non-string message is the T0410 argument error.</summary>
    [TestMethod]
    public void ErrorOnNonStringThrowsT0410()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$error(5)"));

        Assert.AreEqual("T0410", error.Code.ToString());
    }

    /// <summary><c>$assert</c> over a false condition raises D3141.</summary>
    [TestMethod]
    public void AssertFalseRaisesD3141()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$assert(false, 'nope')"));

        Assert.AreEqual("D3141", error.Code.ToString());
    }

    /// <summary><c>$assert</c> over a true condition is undefined.</summary>
    [TestMethod]
    public void AssertTrueIsUndefined()
    {
        Assert.IsTrue(Evaluate("$assert(true)").IsUndefined);
    }

    /// <summary><c>$assert</c> over a non-boolean condition is the T0410 argument error.</summary>
    [TestMethod]
    public void AssertNonBooleanThrowsT0410()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$assert(5)"));

        Assert.AreEqual("T0410", error.Code.ToString());
    }

    /// <summary>Evaluates an expression against an empty object input and serializes the result to compact JSON.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <returns>The result serialized as compact JSON.</returns>
    private static string EvaluateToJson(string expression)
    {
        return JsonataEngine.SerializeToJson(Evaluate(expression)).ToString();
    }

    /// <summary>Evaluates an expression against an empty object input and returns the result value.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <returns>The result value.</returns>
    private static JsonataValue Evaluate(string expression)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes("{}")));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);
    }
}
