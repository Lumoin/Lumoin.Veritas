using System.Collections.Generic;
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
/// Tests for the pure array and object built-in functions: <c>$count</c>, <c>$reverse</c>, <c>$distinct</c>,
/// <c>$append</c>, <c>$keys</c>, <c>$lookup</c>, <c>$merge</c>, <c>$type</c>, and <c>$spread</c> — each with
/// its canonical case, its undefined/empty behaviour, the deep-equality and array-union edge cases, and the
/// explicit-stack depth bound that the array-descending functions enforce in place of recursion.
/// </summary>
[TestClass]
internal sealed class JsonataPureCollectionFunctionTests
{

    /// <summary><c>$count</c> counts an array's elements, an empty array as 0, undefined as 0, and a lone value as 1.</summary>
    [TestMethod]
    public void CountCountsElements()
    {
        Assert.AreEqual(3d, Evaluate("$count([1, 2, 3])", "{}").AsNumber);
        Assert.AreEqual(0d, Evaluate("$count([])", "{}").AsNumber);
        Assert.AreEqual(0d, Evaluate("$count(missing)", "{}").AsNumber);
        Assert.AreEqual(1d, Evaluate("$count(\"x\")", "{}").AsNumber);
    }

    /// <summary><c>$reverse</c> reverses an array; an empty array round-trips; the <c>&lt;a&gt;</c> signature wraps a lone value in a one-element array.</summary>
    [TestMethod]
    public void ReverseReversesArray()
    {
        Assert.AreEqual("[3,2,1]", Serialize("$reverse([1, 2, 3])", "{}"));
        Assert.AreEqual("[]", Serialize("$reverse([])", "{}"));
        Assert.AreEqual("[5]", Serialize("$reverse(5)", "{}"));
    }

    /// <summary><c>$reverse</c> of undefined is undefined.</summary>
    [TestMethod]
    public void ReverseOfUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$reverse(missing)", "{}").Kind);
    }

    /// <summary><c>$distinct</c> removes duplicate primitives, preserving first-occurrence order.</summary>
    [TestMethod]
    public void DistinctDeduplicatesPrimitives()
    {
        Assert.AreEqual("[1,2,3]", Serialize("$distinct([1, 2, 1, 3, 2])", "{}"));
    }

    /// <summary><c>$distinct</c> collapses structurally equal objects via deep equality, not reference identity.</summary>
    [TestMethod]
    public void DistinctCollapsesDeepEqualObjects()
    {
        Assert.AreEqual("[{\"a\":1},{\"a\":2}]", Serialize("$distinct([{\"a\": 1}, {\"a\": 1}, {\"a\": 2}])", "{}"));
    }

    /// <summary><c>$distinct</c> passes a non-array scalar through unchanged and yields undefined for undefined.</summary>
    [TestMethod]
    public void DistinctPassesScalarThrough()
    {
        Assert.AreEqual(5d, Evaluate("$distinct(5)", "{}").AsNumber);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$distinct(missing)", "{}").Kind);
    }

    /// <summary><c>$append</c> concatenates one level, coercing a non-array operand to a one-element array.</summary>
    [TestMethod]
    public void AppendConcatenatesOneLevel()
    {
        Assert.AreEqual("[1,2,3]", Serialize("$append([1, 2], [3])", "{}"));
        Assert.AreEqual("[1,2]", Serialize("$append(1, 2)", "{}"));
    }

    /// <summary><c>$append</c> with one undefined operand yields the other operand; both undefined yields undefined.</summary>
    [TestMethod]
    public void AppendUndefinedOperandYieldsOther()
    {
        Assert.AreEqual("[1,2]", Serialize("$append([1, 2], missing)", "{}"));
        Assert.AreEqual("[1,2]", Serialize("$append(missing, [1, 2])", "{}"));
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$append(missing, missing)", "{}").Kind);
    }

    /// <summary><c>$keys</c> returns an object's keys in insertion order.</summary>
    [TestMethod]
    public void KeysReturnsObjectKeys()
    {
        Assert.AreEqual("[\"a\",\"b\"]", Serialize("$keys({\"a\": 1, \"b\": 2})", "{}"));
    }

    /// <summary><c>$keys</c> over an array is the deduplicated union of keys across the object leaves, in first-seen order.</summary>
    [TestMethod]
    public void KeysOverArrayUnionsKeys()
    {
        Assert.AreEqual("[\"a\",\"b\"]", Serialize("$keys([{\"a\": 1}, {\"b\": 2}, {\"a\": 3}])", "{}"));
    }

    /// <summary><c>$keys</c> of a scalar yields undefined; the context form reads the object root.</summary>
    [TestMethod]
    public void KeysScalarUndefinedAndContextForm()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$keys(5)", "{}").Kind);
        Assert.AreEqual("[\"a\",\"b\"]", Serialize("$keys()", "{ \"a\": 1, \"b\": 2 }"));
    }

    /// <summary><c>$lookup</c> reads a key on an object; a missing key yields undefined.</summary>
    [TestMethod]
    public void LookupReadsObjectKey()
    {
        Assert.AreEqual(2d, Evaluate("$lookup({\"a\": 1, \"b\": 2}, \"b\")", "{}").AsNumber);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$lookup({\"a\": 1}, \"z\")", "{}").Kind);
    }

    /// <summary><c>$lookup</c> over an array maps the lookup per element, flattening the defined results one level.</summary>
    [TestMethod]
    public void LookupOverArrayFlattens()
    {
        Assert.AreEqual("[1,2]", Serialize("$lookup([{\"a\": 1}, {\"a\": 2}], \"a\")", "{}"));
    }

    /// <summary><c>$lookup</c> of a present null-valued key yields null, distinct from an absent key's undefined.</summary>
    [TestMethod]
    public void LookupPresentNullYieldsNull()
    {
        Assert.AreEqual(JsonataValueKind.Null, Evaluate("$lookup({\"a\": null}, \"a\")", "{}").Kind);
    }

    /// <summary><c>$merge</c> merges objects last-wins, with keys in first-seen order; the empty array merges to the empty object.</summary>
    [TestMethod]
    public void MergeIsLastWinsFirstSeenOrder()
    {
        Assert.AreEqual("{\"a\":3,\"b\":2}", Serialize("$merge([{\"a\": 1}, {\"b\": 2}, {\"a\": 3}])", "{}"));
        Assert.AreEqual("{}", Serialize("$merge([])", "{}"));
    }

    /// <summary><c>$merge</c> of undefined is undefined.</summary>
    [TestMethod]
    public void MergeOfUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$merge(missing)", "{}").Kind);
    }

    /// <summary><c>$type</c> names each JSONata data type, and undefined yields the undefined value.</summary>
    [TestMethod]
    public void TypeNamesEachKind()
    {
        Assert.AreEqual("string", Evaluate("$type(\"x\")", "{}").AsString);
        Assert.AreEqual("number", Evaluate("$type(1)", "{}").AsString);
        Assert.AreEqual("boolean", Evaluate("$type(true)", "{}").AsString);
        Assert.AreEqual("null", Evaluate("$type(null)", "{}").AsString);
        Assert.AreEqual("array", Evaluate("$type([1])", "{}").AsString);
        Assert.AreEqual("object", Evaluate("$type({})", "{}").AsString);
        Assert.AreEqual("function", Evaluate("$type($uppercase)", "{}").AsString);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$type(missing)", "{}").Kind);
    }

    /// <summary><c>$spread</c> splits an object into an array of single-key objects in insertion order.</summary>
    [TestMethod]
    public void SpreadSplitsObject()
    {
        Assert.AreEqual("[{\"a\":1},{\"b\":2}]", Serialize("$spread({\"a\": 1, \"b\": 2})", "{}"));
    }

    /// <summary><c>$spread</c> over an array spreads each object element into one flat array of single-key objects.</summary>
    [TestMethod]
    public void SpreadOverArrayFlattens()
    {
        Assert.AreEqual("[{\"a\":1},{\"b\":2}]", Serialize("$spread([{\"a\": 1}, {\"b\": 2}])", "{}"));
    }

    /// <summary><c>$spread</c> passes a non-object, non-array scalar through unchanged.</summary>
    [TestMethod]
    public void SpreadPassesScalarThrough()
    {
        Assert.AreEqual(5d, Evaluate("$spread(5)", "{}").AsNumber);
    }

    /// <summary><c>$keys</c> over a deeply nested array input throws the catchable depth-limit error, not a stack overflow — the array-descent cursor stays depth-bounded.</summary>
    [TestMethod]
    public void KeysOverDeeplyNestedArrayThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("$keys($)")).Tree;
        JsonataValue input = BuildNestedArrays(ObjectWithKeyA(), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
    }

    /// <summary><c>$lookup</c> over a deeply nested array input throws the catchable depth-limit error, not a stack overflow — the array-descent cursor stays depth-bounded.</summary>
    [TestMethod]
    public void LookupOverDeeplyNestedArrayThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("$lookup($, \"a\")")).Tree;
        JsonataValue input = BuildNestedArrays(ObjectWithKeyA(), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
    }

    /// <summary><c>$spread</c> over a deeply nested array input throws the catchable depth-limit error, not a stack overflow — the array-descent cursor stays depth-bounded.</summary>
    [TestMethod]
    public void SpreadOverDeeplyNestedArrayThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("$spread($)")).Tree;
        JsonataValue input = BuildNestedArrays(ObjectWithKeyA(), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
    }

    /// <summary>Builds a one-key object <c>{ "a": 1 }</c> as the innermost leaf of the nested-array depth tests.</summary>
    /// <returns>The object value.</returns>
    private static JsonataValue ObjectWithKeyA()
    {
        return JsonataValue.Object([new KeyValuePair<string, JsonataValue>("a", JsonataValue.Number(1))]);
    }

    /// <summary>Wraps a value in the given number of nested single-element arrays.</summary>
    /// <param name="leaf">The innermost value.</param>
    /// <param name="depth">The number of array levels to wrap.</param>
    /// <returns>The nested-array value.</returns>
    private static JsonataValue BuildNestedArrays(JsonataValue leaf, int depth)
    {
        JsonataValue current = leaf;
        for(int i = 0; i < depth; i++)
        {
            current = JsonataValue.Array([current]);
        }

        return current;
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
