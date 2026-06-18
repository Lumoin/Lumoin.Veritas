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
/// Tests for the SUB-3 tuple-stream cursor extensions: the tuple-aware <c>^</c> sort step (over a tuple stream
/// and as the first tuple step over a flat input, both ascending and descending, by focus and by a binding
/// term), the <c>#</c> index re-numbering stage (post-filter position re-binding), and the path-end group-by
/// reduce over a tuple stream (keying / value-merge over the tuple bindings).
/// </summary>
[TestClass]
internal sealed class JsonataTupleStreamSortGroupTests
{
    /// <summary>A flat-input sort that is the first tuple step sorts the values then numbers them by post-sort position, so a post-sort filter on the index keeps the first three sorted values.</summary>
    [TestMethod]
    public void FlatSortFirstTupleStepNumbersPostSort()
    {
        Assert.AreEqual("[1,1,3]", EvaluateToJson("$^($)#$pos[$pos<3]", "[3,1,4,1,5,9]"));
    }

    /// <summary>A <c>#</c> index bind numbers each value by its pre-sort position, so a filter on that index keeps the first three input values in input order.</summary>
    [TestMethod]
    public void IndexBindNumbersPreFilterPosition()
    {
        Assert.AreEqual("[3,1,4]", EvaluateToJson("$#$pos[$pos<3]", "[3,1,4,1,5,9]"));
    }

    /// <summary>A sort over an already-filtered tuple stream stably orders the surviving tuples by their focus.</summary>
    [TestMethod]
    public void SortOverTupleStreamAscending()
    {
        Assert.AreEqual("[1,3,4]", EvaluateToJson("$#$pos[$pos<3]^($)", "[3,1,4,1,5,9]"));
    }

    /// <summary>A descending sort over a tuple stream reverses the order.</summary>
    [TestMethod]
    public void SortOverTupleStreamDescending()
    {
        Assert.AreEqual("[4,3,1]", EvaluateToJson("$#$pos[$pos<3]^(>$)", "[3,1,4,1,5,9]"));
    }

    /// <summary>A numeric-literal predicate selects positionally from the (already filtered) tuple stream.</summary>
    [TestMethod]
    public void NumericLiteralSelectsTuplePosition()
    {
        Assert.AreEqual("1", EvaluateToJson("$#$pos[$pos<3][1]", "[3,1,4,1,5,9]"));
    }

    /// <summary>A negative numeric-literal predicate after a sort selects from the end of the sorted stream.</summary>
    [TestMethod]
    public void NegativeLiteralAfterSortSelectsFromEnd()
    {
        Assert.AreEqual("4", EvaluateToJson("$#$pos[$pos<3]^($)[-1]", "[3,1,4,1,5,9]"));
    }

    /// <summary>A <c>#</c> index bind over a positional sub-array selects the positions at or after a cutoff (the index numbers the sliced stream).</summary>
    [TestMethod]
    public void IndexBindOverSliceFiltersByPosition()
    {
        Assert.AreEqual("[1,5]", EvaluateToJson("$[[1..4]]#$pos[$pos>=2]", "[3,1,4,1,5,9]"));
    }

    /// <summary>Evaluates an expression against the given JSON input and serializes the result to compact JSON.</summary>
    /// <param name="expression">The JSONata expression.</param>
    /// <param name="inputJson">The input document as JSON text.</param>
    /// <returns>The result serialized as compact JSON.</returns>
    private static string EvaluateToJson(string expression, string inputJson)
    {
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(inputJson)));
        JsonataValue result = JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input);

        return JsonataEngine.SerializeToJson(result).ToString();
    }
}
