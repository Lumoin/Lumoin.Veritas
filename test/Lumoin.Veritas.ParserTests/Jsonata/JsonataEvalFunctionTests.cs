using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Values;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Tests for the dynamic-evaluation built-in <c>$eval</c>: it parses a string as a JSONata expression and
/// evaluates it against the optional context (default the current focus), raising D3120 for a parse error and
/// wrapping a runtime error as D3121.
/// </summary>
[TestClass]
internal sealed class JsonataEvalFunctionTests
{
    /// <summary><c>$eval</c> parses and evaluates a literal array expression.</summary>
    [TestMethod]
    public void EvalLiteralArray()
    {
        Assert.AreEqual("[1,2,3]", EvaluateToJson("$eval('[1,2,3]')"));
    }

    /// <summary><c>$eval</c> over an undefined expression yields undefined.</summary>
    [TestMethod]
    public void EvalUndefinedExpressionIsUndefined()
    {
        Assert.IsTrue(Evaluate("$eval(nothing)").IsUndefined);
    }

    /// <summary><c>$eval</c> evaluates a nested built-in call within the expression string.</summary>
    [TestMethod]
    public void EvalNestedBuiltinCall()
    {
        Assert.AreEqual("[1,\"2\",3]", EvaluateToJson("$eval('[1,$string(2),3]')"));
    }

    /// <summary><c>$eval</c> evaluates the expression against an explicit context argument.</summary>
    [TestMethod]
    public void EvalAgainstExplicitContext()
    {
        Assert.AreEqual("[1,2,3]", EvaluateToJson("$eval('$', [1,2,3])"));
    }

    /// <summary><c>$eval</c> over a syntactically invalid expression raises D3120.</summary>
    [TestMethod]
    public void EvalSyntaxErrorThrowsD3120()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$eval('[1,#string(2),3]')"));

        Assert.AreEqual("D3120", error.Code.ToString());
    }

    /// <summary><c>$eval</c> wraps a runtime error in the evaluated expression as D3121.</summary>
    [TestMethod]
    public void EvalRuntimeErrorThrowsD3121()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$eval('[1,string(2),3]')"));

        Assert.AreEqual("D3121", error.Code.ToString());
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
