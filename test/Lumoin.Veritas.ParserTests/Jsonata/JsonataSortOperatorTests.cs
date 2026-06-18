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
/// Tests for the order-by operator <c>^( ... )</c>: ascending / descending single and multi-key sorts, an
/// undefined key sorting last, a single value returned unchanged, and the T2008 (non-comparable key) and
/// T2007 (type mismatch) errors.
/// </summary>
[TestClass]
internal sealed class JsonataSortOperatorTests
{
    /// <summary>The default ascending order sorts numbers ascending.</summary>
    [TestMethod]
    public void AscendingByDefault()
    {
        Assert.AreEqual("[1,2,3]", EvaluateToJson("$^($)", "[3,1,2]"));
    }

    /// <summary>A leading <c>&lt;</c> sorts ascending.</summary>
    [TestMethod]
    public void AscendingExplicit()
    {
        Assert.AreEqual("[1,2,3]", EvaluateToJson("$^(<$)", "[3,1,2]"));
    }

    /// <summary>A leading <c>&gt;</c> sorts descending.</summary>
    [TestMethod]
    public void Descending()
    {
        Assert.AreEqual("[3,2,1]", EvaluateToJson("$^(>$)", "[3,1,2]"));
    }

    /// <summary>A key expression sorts objects by the keyed field.</summary>
    [TestMethod]
    public void SortByKeyField()
    {
        Assert.AreEqual("[{\"x\":1},{\"x\":2},{\"x\":3}]", EvaluateToJson("$^(x)", "[{\"x\":3},{\"x\":1},{\"x\":2}]"));
    }

    /// <summary>Two terms sort by the first key, ties broken by the second.</summary>
    [TestMethod]
    public void MultiKeyTieBreak()
    {
        Assert.AreEqual("[{\"a\":0,\"b\":9},{\"a\":1,\"b\":1},{\"a\":1,\"b\":2}]", EvaluateToJson("$^(a, b)", "[{\"a\":1,\"b\":2},{\"a\":1,\"b\":1},{\"a\":0,\"b\":9}]"));
    }

    /// <summary>A second term may sort in the opposite direction to the first.</summary>
    [TestMethod]
    public void MultiKeyMixedDirection()
    {
        Assert.AreEqual("[{\"a\":0,\"b\":9},{\"a\":1,\"b\":2},{\"a\":1,\"b\":1}]", EvaluateToJson("$^(a, >b)", "[{\"a\":1,\"b\":2},{\"a\":1,\"b\":1},{\"a\":0,\"b\":9}]"));
    }

    /// <summary>An element whose key is undefined sorts after the defined keys.</summary>
    [TestMethod]
    public void UndefinedKeySortsLast()
    {
        Assert.AreEqual("[{\"x\":1},{\"x\":2},{\"y\":1}]", EvaluateToJson("$^(x)", "[{\"x\":2},{\"y\":1},{\"x\":1}]"));
    }

    /// <summary>A source of a single value is returned unchanged (no comparison).</summary>
    [TestMethod]
    public void SingleValueUnchanged()
    {
        Assert.AreEqual("{\"x\":5}", EvaluateToJson("$[0]^(x)", "[{\"x\":5}]"));
    }

    /// <summary>A non-comparable key (an array) raises T2008.</summary>
    [TestMethod]
    public void NonComparableKeyThrowsT2008()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$^(x)", "[{\"x\":[1]},{\"x\":[2]}]"));

        Assert.AreEqual("T2008", error.Code.ToString());
    }

    /// <summary>Two keys of different comparable types raise T2007.</summary>
    [TestMethod]
    public void TypeMismatchThrowsT2007()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$^(x)", "[{\"x\":1},{\"x\":\"a\"}]"));

        Assert.AreEqual("T2007", error.Code.ToString());
    }

    /// <summary>Evaluates an expression against the given JSON input and serializes the result to compact JSON.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="inputJson">The input document as JSON text.</param>
    /// <returns>The result serialized as compact JSON.</returns>
    private static string EvaluateToJson(string expression, string inputJson)
    {
        return JsonataEngine.SerializeToJson(Evaluate(expression, inputJson)).ToString();
    }

    /// <summary>Evaluates an expression against the given JSON input and returns the result value.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="inputJson">The input document as JSON text.</param>
    /// <returns>The result value.</returns>
    private static JsonataValue Evaluate(string expression, string inputJson)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(inputJson)));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);
    }
}
