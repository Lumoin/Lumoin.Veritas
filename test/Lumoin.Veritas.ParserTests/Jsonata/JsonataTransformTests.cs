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
/// Tests for the JSONata transform operator <c>| location | update [, delete] |</c>: it deep-clones the
/// input (leaving the original untouched), merges the update object into each node the location matches,
/// removes the keys the delete clause names, and reports the T2011 / T2012 clause-type errors.
/// </summary>
[TestClass]
internal sealed class JsonataTransformTests
{
    /// <summary>An update over the whole-document match adds a new member to the cloned object.</summary>
    [TestMethod]
    public void UpdateAddsMember()
    {
        Assert.AreEqual("{\"a\":1,\"b\":2}", EvaluateToJson("$ ~> |$|{\"b\": 2}|", "{\"a\": 1}"));
    }

    /// <summary>An update overwrites an existing member in place, keeping its position.</summary>
    [TestMethod]
    public void UpdateOverwritesMemberInPlace()
    {
        Assert.AreEqual("{\"a\":10,\"b\":2}", EvaluateToJson("$ ~> |$|{\"a\": 10}|", "{\"a\": 1, \"b\": 2}"));
    }

    /// <summary>A location pattern that matches every element of an array updates each matched object in place.</summary>
    [TestMethod]
    public void UpdateAppliesToEveryMatchedArrayElement()
    {
        Assert.AreEqual("{\"items\":[{\"v\":0},{\"v\":0}]}", EvaluateToJson("$ ~> |items|{\"v\": 0}|", "{\"items\": [{\"v\": 1}, {\"v\": 2}]}"));
    }

    /// <summary>A delete clause removes the named key from each matched object; an empty update object leaves the rest unchanged.</summary>
    [TestMethod]
    public void DeleteRemovesNamedKey()
    {
        Assert.AreEqual("{\"a\":1}", EvaluateToJson("$ ~> |$|{}, \"b\"|", "{\"a\": 1, \"b\": 2}"));
    }

    /// <summary>An update and a delete clause apply together to each match: the update merges, then the delete removes.</summary>
    [TestMethod]
    public void UpdateAndDeleteApplyTogether()
    {
        Assert.AreEqual("{\"a\":1,\"c\":3}", EvaluateToJson("$ ~> |$|{\"c\": 3}, \"b\"|", "{\"a\": 1, \"b\": 2}"));
    }

    /// <summary>The transform clones the input, so the original document is left untouched: the transformed copy and the original are both observable.</summary>
    [TestMethod]
    public void TransformLeavesOriginalUntouched()
    {
        Assert.AreEqual("[99,1]", EvaluateToJson("[($ ~> |$|{\"a\": 99}|).a, a]", "{\"a\": 1}"));
    }

    /// <summary>Transforms chain left-to-right through the chain operator, each applied to the previous result.</summary>
    [TestMethod]
    public void TransformsChainLeftToRight()
    {
        Assert.AreEqual("{\"a\":2,\"b\":3}", EvaluateToJson("$ ~> |$|{\"a\": 2}| ~> |$|{\"b\": 3}|", "{\"a\": 1}"));
    }

    /// <summary>An update clause that evaluates to a defined non-object value throws T2011.</summary>
    [TestMethod]
    public void UpdateNotObjectThrowsT2011()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$ ~> |$|5|", "{\"a\": 1}"));

        Assert.AreEqual("T2011", error.Code.ToString());
    }

    /// <summary>A delete clause that evaluates to a non-string value throws T2012.</summary>
    [TestMethod]
    public void DeleteNotStringsThrowsT2012()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$ ~> |$|{}, 5|", "{\"a\": 1}"));

        Assert.AreEqual("T2012", error.Code.ToString());
    }

    /// <summary>
    /// A location pattern that constructs a new object (a match outside the clone) merges harmlessly into that
    /// disconnected object, so the returned clone is unchanged — matching the reference, where a match that is
    /// not part of the result is updated in place but never surfaces.
    /// </summary>
    [TestMethod]
    public void NonCloneMatchMutationIsInvisible()
    {
        Assert.AreEqual("{\"a\":1}", EvaluateToJson("$ ~> |{\"x\": 1}|{\"y\": 2}|", "{\"a\": 1}"));
    }

    /// <summary>
    /// An input nested deeper than the evaluation-depth bound throws the catchable clone-depth limit rather
    /// than exhausting the stack. The input is built directly as a value (not through a JSON parser, whose own
    /// depth cap would reject it first), so the transform's deep-clone is the traversal that hits the bound.
    /// </summary>
    [TestMethod]
    public void DeeplyNestedInputThrowsCloneDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("$ ~> |$|{}|")).Tree;
        JsonataValue deepInput = NestedValue(200);

        Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, deepInput));
    }

    /// <summary>Builds a value nested <paramref name="depth"/> objects deep under the key <c>a</c> (the innermost value is the number <c>1</c>).</summary>
    /// <param name="depth">The nesting depth.</param>
    /// <returns>The nested-object value.</returns>
    private static JsonataValue NestedValue(int depth)
    {
        JsonataValue value = JsonataValue.Number(1);
        for(int i = 0; i < depth; i++)
        {
            value = JsonataValue.Object([new KeyValuePair<string, JsonataValue>("a", value)]);
        }

        return value;
    }

    /// <summary>Evaluates a transform expression and serializes the result to compact JSON for comparison.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="inputJson">The input document as JSON text.</param>
    /// <returns>The result serialized as compact JSON.</returns>
    private static string EvaluateToJson(string expression, string inputJson)
    {
        return JsonataEngine.SerializeToJson(Evaluate(expression, inputJson)).ToString();
    }

    /// <summary>Evaluates a transform expression against an input document and returns the result value.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="inputJson">The input document as JSON text.</param>
    /// <returns>The result value.</returns>
    private static JsonataValue Evaluate(string expression, string inputJson)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(inputJson)));

        return JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);
    }
}
