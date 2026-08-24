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
/// End-to-end tests for inline lambda type signatures <c>function(...)&lt;sig&gt;{...}</c> /
/// <c>λ(...)&lt;sig&gt;{...}</c>: the parser reassembles the bracketed signature from the token stream after
/// the parameter list and the evaluator runs it through the same signature validator the built-ins use, so a
/// signed lambda's arguments are context-substituted, coerced, singleton-wrapped, and type-checked
/// (T0410/T0411/T0412) before binding. The cases mirror the vendored <c>function-signatures</c> conformance
/// group: a boolean signature, context substitution applied per map item and with full arguments, array
/// singleton-wrapping, a union parameter, an optional parameter, a variadic parameter, function-typed
/// parameters, the array-element subtype error, the argument-mismatch error, and a trailing context
/// substitution.
/// </summary>
[TestClass]
internal sealed class JsonataLambdaSignatureTests
{
    /// <summary>The JSONata error code for arguments that do not match the function signature.</summary>
    private const string CodeArgumentMismatch = "T0410";

    /// <summary>The JSONata error code for an array argument whose elements are not the required subtype.</summary>
    private const string CodeWrongElementType = "T0412";

    /// <summary>A boolean-typed signature <c>&lt;b:b&gt;</c> passes a boolean argument through to the body: <c>λ($arg)&lt;b:b&gt;{$not($arg)}(true)</c> is false.</summary>
    [TestMethod]
    public void BooleanSignatureValidatesArgument()
    {
        Assert.AreEqual(JsonataValueKind.Boolean, Evaluate("λ($arg)<b:b>{$not($arg)}(true)", "{}").Kind);
        Assert.IsFalse(Evaluate("λ($arg)<b:b>{$not($arg)}(true)", "{}").AsBoolean);
    }

    /// <summary>Context substitution applied per map item: <c>[1..5].function($x,$y)&lt;n-n:n&gt;{$x+$y}(6)</c> fills <c>$x</c> from each item and <c>$y</c> from the supplied 6.</summary>
    [TestMethod]
    public void ContextSubstitutionFillsContextParameterPerMapItem()
    {
        Assert.AreEqual("[7,8,9,10,11]", EvaluateToJson("[1..5].function($x,$y)<n-n:n>{$x+$y}(6)", "{}"));
    }

    /// <summary>Both arguments supplied: <c>[1..5].function($x,$y)&lt;n-n:n&gt;{$x+$y}(2, 6)</c> consumes 2 into the context parameter and ignores each map item, so every result is 8.</summary>
    [TestMethod]
    public void ContextSubstitutionIgnoredWhenBothArgumentsSupplied()
    {
        Assert.AreEqual("[8,8,8,8,8]", EvaluateToJson("[1..5].function($x,$y)<n-n:n>{$x+$y}(2, 6)", "{}"));
    }

    /// <summary>A context-substituted string parameter <c>&lt;s-&gt;</c> still takes a supplied argument when one is given: <c>λ($str)&lt;s-&gt;{$uppercase($str)}("hello")</c> uppercases the supplied string.</summary>
    [TestMethod]
    public void ContextSubstitutedParameterTakesSuppliedArgument()
    {
        Assert.AreEqual("\"HELLO\"", EvaluateToJson("λ($str)<s->{$uppercase($str)}(\"hello\")", "{}"));
    }

    /// <summary>The array-element subtype signature <c>&lt;a&lt;s&gt;s?:s&gt;</c> singleton-wraps a single string argument into a one-element array before <c>$join</c>: <c>λ($arr,$sep)&lt;a&lt;s&gt;s?:s&gt;{$join($arr,$sep)}("a")</c> is "a".</summary>
    [TestMethod]
    public void SingletonWrapWrapsScalarStringForArrayParameter()
    {
        Assert.AreEqual("\"a\"", EvaluateToJson("λ($arr, $sep)<a<s>s?:s>{$join($arr, $sep)}(\"a\")", "{}"));
    }

    /// <summary>The same array signature joins a two-element array with a separator: <c>λ($arr,$sep)&lt;a&lt;s&gt;s?:s&gt;{$join($arr,$sep)}(["a","b"], "-")</c> is "a-b".</summary>
    [TestMethod]
    public void SingletonWrapJoinsTwoElementArrayWithSeparator()
    {
        Assert.AreEqual("\"a-b\"", EvaluateToJson("λ($arr, $sep)<a<s>s?:s>{$join($arr, $sep)}([\"a\", \"b\"], \"-\")", "{}"));
    }

    /// <summary>A union parameter <c>&lt;(ns)-:n&gt;</c> accepts a string and coerces it through the body: <c>λ($num)&lt;(ns)-:n&gt;{$number($num)}("5")</c> is 5.</summary>
    [TestMethod]
    public void UnionParameterAcceptsStringArgument()
    {
        Assert.AreEqual(5d, Evaluate("λ($num)<(ns)-:n>{$number($num)}(\"5\")", "{}").AsNumber);
    }

    /// <summary>A union parameter <c>&lt;(ns)-:n&gt;</c> also accepts a number: <c>λ($num)&lt;(ns)-:n&gt;{$number($num)}(5)</c> is 5.</summary>
    [TestMethod]
    public void UnionParameterAcceptsNumberArgument()
    {
        Assert.AreEqual(5d, Evaluate("λ($num)<(ns)-:n>{$number($num)}(5)", "{}").AsNumber);
    }

    /// <summary>An optional parameter <c>&lt;a&lt;s&gt;s?:s&gt;</c> may be omitted: <c>λ($arr,$sep)&lt;a&lt;s&gt;s?:s&gt;{$join($arr,$sep)}(["a"])</c> joins the single element with no separator.</summary>
    [TestMethod]
    public void OptionalParameterMayBeOmitted()
    {
        Assert.AreEqual("\"a\"", EvaluateToJson("λ($arr, $sep)<a<s>s?:s>{$join($arr, $sep)}([\"a\"])", "{}"));
    }

    /// <summary>A variadic parameter <c>&lt;n+n:o&gt;</c> binds the supplied positional arguments to the declared parameters: <c>λ($arg1,$arg2)&lt;n+n:o&gt;{...}(1, 2, 3)</c> binds 1 and 2.</summary>
    [TestMethod]
    public void VariadicParameterBindsToDeclaredParameters()
    {
        Assert.AreEqual("{\"$arg1\":1,\"$arg2\":2}", EvaluateToJson("λ($arg1, $arg2)<n+n:o>{{\"$arg1\": $arg1, \"$arg2\": $arg2}}(1, 2, 3)", "{}"));
    }

    /// <summary>A function-typed signature <c>&lt;f:f&gt;</c> accepts a function argument and returns one: the higher-order twice composition <c>$twice($add2)</c> applied to 5 is 9.</summary>
    [TestMethod]
    public void FunctionTypedSignatureAcceptsFunctionArgument()
    {
        const string expression = "($twice := function($f)<f:f>{function($x)<n:n>{$f($f($x))}};$add2 := function($x)<n:n>{$x+2};$add4 := $twice($add2);$add4(5))";

        Assert.AreEqual(9d, Evaluate(expression, "{}").AsNumber);
    }

    /// <summary>A parameterised function-typed signature <c>&lt;f&lt;n:n&gt;:f&lt;n:n&gt;&gt;</c> is parsed and validated like the bare function type: the twice composition still yields 9.</summary>
    [TestMethod]
    public void ParameterisedFunctionTypedSignatureAcceptsFunctionArgument()
    {
        const string expression = "($twice := function($f)<f<n:n>:f<n:n>>{function($x)<n:n>{$f($f($x))}};$add2 := function($x)<n:n>{$x+2};$add4 := $twice($add2);$add4(5))";

        Assert.AreEqual(9d, Evaluate(expression, "{}").AsNumber);
    }

    /// <summary>An array-element subtype mismatch raises T0412: <c>λ($arr)&lt;a&lt;n&gt;&gt;{$arr}(["3"])</c> supplies a string element where a number is required.</summary>
    [TestMethod]
    public void ArrayElementSubtypeMismatchRaisesT0412()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("λ($arr)<a<n>>{$arr}([\"3\"])", "{}"));

        Assert.AreEqual(CodeWrongElementType, error.Code.ToString());
    }

    /// <summary>An argument-type mismatch raises T0410: <c>λ($arg1,$arg2)&lt;nn:a&gt;{...}(1, "2")</c> supplies a string where the second number parameter is required.</summary>
    [TestMethod]
    public void ArgumentTypeMismatchRaisesT0410()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("λ($arg1, $arg2)<nn:a>{[$arg1, $arg2]}(1, \"2\")", "{}"));

        Assert.AreEqual(CodeArgumentMismatch, error.Code.ToString());
    }

    /// <summary>A trailing context-substituted parameter fills from the input focus: <c>λ($arg1,$arg2,$arg3)&lt;n+s-:a&lt;n&gt;&gt;{[...]}(1, 2)</c> over input "b" binds $arg3 to the focus "b".</summary>
    [TestMethod]
    public void TrailingContextSubstitutionFillsFromInputFocus()
    {
        Assert.AreEqual("[1,2,\"b\"]", EvaluateToJson("λ($arg1, $arg2, $arg3)<n+s-:a<n>>{[$arg1, $arg2, $arg3]}(1, 2)", "\"b\""));
    }

    /// <summary>A trailing context-substituted parameter is overridden by a supplied argument: the same lambda with three explicit arguments over input "b" binds $arg3 to the supplied "a".</summary>
    [TestMethod]
    public void TrailingContextSubstitutionOverriddenBySuppliedArgument()
    {
        Assert.AreEqual("[1,2,\"a\"]", EvaluateToJson("λ($arg1, $arg2, $arg3)<n+s-:a<n>>{[$arg1, $arg2, $arg3]}(1, 2, \"a\")", "\"b\""));
    }

    /// <summary>A lambda with no signature still binds its arguments positionally: <c>function($x,$y){$x+$y}(2, 6)</c> is 8 with no validation.</summary>
    [TestMethod]
    public void UnsignedLambdaBindsArgumentsPositionally()
    {
        Assert.AreEqual(8d, Evaluate("function($x, $y){$x + $y}(2, 6)", "{}").AsNumber);
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

    /// <summary>Evaluates a JSONata expression and serializes the result to compact JSON for a structural comparison.</summary>
    /// <param name="expression">The JSONata expression source.</param>
    /// <param name="inputJson">The input JSON document text.</param>
    /// <returns>The result serialized to compact JSON.</returns>
    private static string EvaluateToJson(string expression, string inputJson)
    {
        return JsonataEngine.SerializeToJson(Evaluate(expression, inputJson)).ToString();
    }
}
