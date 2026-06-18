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
/// Tests for JSONata partial function application — the <c>?</c> placeholder in an argument position
/// (<c>$add(?, 2)</c>). Covers the leading-placeholder and trailing-placeholder forms over a lambda, the
/// nested-partial form over a built-in (<c>$substring(?, 0, ?)</c> then <c>$firstn(?, 5)</c>), a
/// <c>~&gt;</c>-chained pair of partials, the T1007 bare-name suggestion and the T1008 plain non-function
/// error, and the parser regression that a ternary inside a call argument still parses (a <c>?</c> after an
/// expression is the conditional, not a placeholder).
/// </summary>
[TestClass]
internal sealed class JsonataPartialApplicationTests
{
    /// <summary>The JSONata error code for partially applying a non-function where a same-named built-in exists.</summary>
    private const string CodePartialNonFunctionSuggestion = "T1007";

    /// <summary>The JSONata error code for partially applying a non-function.</summary>
    private const string CodePartialNonFunction = "T1008";

    /// <summary>A leading placeholder over a lambda binds the remaining argument and the partial accepts the placeholder argument: <c>$add(?, 2)</c> then <c>(3)</c> is 5.</summary>
    [TestMethod]
    public void LeadingPlaceholderOverLambdaBindsTrailingArgument()
    {
        Assert.AreEqual(5d, Evaluate("($add := function($x, $y){$x+$y}; $add2 := $add(?, 2); $add2(3))", "{}").AsNumber);
    }

    /// <summary>A trailing placeholder over a lambda binds the leading argument: <c>$add(2, ?)</c> then <c>(4)</c> is 6.</summary>
    [TestMethod]
    public void TrailingPlaceholderOverLambdaBindsLeadingArgument()
    {
        Assert.AreEqual(6d, Evaluate("($add := function($x, $y){$x+$y}; $add2 := $add(2, ?); $add2(4))", "{}").AsNumber);
    }

    /// <summary>A nested partial over a built-in fills its two placeholders across two applications: <c>$substring(?, 0, ?)</c> then <c>$firstn(?, 5)</c> then <c>("Hello World")</c> is "Hello".</summary>
    [TestMethod]
    public void NestedPartialOverBuiltinFillsBothPlaceholders()
    {
        Assert.AreEqual("Hello", Evaluate("($firstn := $substring(?, 0, ?); $first5 := $firstn(?, 5); $first5(\"Hello World\"))", "{}").AsString);
    }

    /// <summary>Two partials chained through <c>~&gt;</c> compose: <c>$substringAfter(?,\"@\") ~&gt; $substringBefore(?,\".\")</c> over an email yields the domain label.</summary>
    [TestMethod]
    public void ChainedPartialsComposeThroughApplyOperator()
    {
        Assert.AreEqual("example", Evaluate("($domain := $substringAfter(?,\"@\") ~> $substringBefore(?,\".\"); $domain(\"john@example.com\"))", "{}").AsString);
    }

    /// <summary>A bare-name procedure that is not a function but matches a built-in raises T1007 with the suggested name.</summary>
    [TestMethod]
    public void PartialOverBareBuiltinNameThrowsT1007()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("substring(?, 0, ?)", "{}"));

        Assert.AreEqual(CodePartialNonFunctionSuggestion, error.Code.ToString());
        Assert.AreEqual("substring", error.Token);
    }

    /// <summary>A partial over a name that is neither a function nor a built-in raises the plain T1008.</summary>
    [TestMethod]
    public void PartialOverUnknownNameThrowsT1008()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("unknown(?)", "{}"));

        Assert.AreEqual(CodePartialNonFunction, error.Code.ToString());
    }

    /// <summary>A partial over a defined non-function value (a bound number) raises the plain T1008, not the built-in suggestion.</summary>
    [TestMethod]
    public void PartialOverNonFunctionValueThrowsT1008()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("($n := 5; $n(?))", "{}"));

        Assert.AreEqual(CodePartialNonFunction, error.Code.ToString());
    }

    /// <summary>A ternary inside a call argument still parses and evaluates as the conditional (a <c>?</c> after an expression is not a placeholder).</summary>
    [TestMethod]
    public void TernaryInsideCallArgumentParsesAsConditional()
    {
        Assert.AreEqual("[1]", Serialize("$append([], true ? 1 : 2)", "{}"));
    }

    /// <summary>A partial built and applied in a single step (no intervening binding) still fills its placeholder.</summary>
    [TestMethod]
    public void PartialAppliedWithoutBindingFillsPlaceholder()
    {
        Assert.AreEqual(7d, Evaluate("($add := function($x, $y){$x+$y}; $add(?, 4)(3))", "{}").AsNumber);
    }

    /// <summary>A normal call with no placeholder is entirely unchanged — the procedure is invoked immediately.</summary>
    [TestMethod]
    public void NormalCallWithoutPlaceholderIsUnchanged()
    {
        Assert.AreEqual("ell", Evaluate("$substring(\"Hello\", 1, 3)", "{}").AsString);
    }

    /// <summary>
    /// A partial used as a higher-order callback consumes only its placeholder count: the index and array
    /// <c>$map</c> passes are ignored, so they do not leak into the inner function's later parameters.
    /// </summary>
    [TestMethod]
    public void PartialAsHigherOrderCallbackIgnoresSurplusArguments()
    {
        Assert.AreEqual("[\"a\",\"c\"]", Serialize("$map([\"ab\", \"cd\"], $substring(?, 0, 1))", "{}"));
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
