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
/// Tests for the JSONata higher-order array functions <c>$map</c>, <c>$filter</c>, <c>$single</c>, and
/// <c>$reduce</c>: their per-element application of a supplied lambda or built-in through the resident cursor,
/// the index and array arguments, the truthiness-driven keep / match rules, the left-fold seeding, and the
/// documented domain errors. Also covers the registry value-type change: a user binding still shadows a
/// higher-order name and the higher-order functions are first-class values.
/// </summary>
[TestClass]
internal sealed class JsonataHigherOrderFunctionTests
{
    /// <summary>The JSONata error code for a <c>$reduce</c> reducer that accepts fewer than two arguments.</summary>
    private const string CodeReduceArity = "D3050";

    /// <summary>The JSONata error code for a <c>$single</c> over which more than one element matched the predicate.</summary>
    private const string CodeSingleMultipleMatches = "D3138";

    /// <summary>The JSONata error code for a <c>$single</c> over which no element matched the predicate.</summary>
    private const string CodeSingleNoMatch = "D3139";

    /// <summary>The JSONata error code for a default-comparator <c>$sort</c> over a non-numeric/non-string array.</summary>
    private const string CodeSortDefaultComparatorType = "D3070";

    /// <summary><c>$map</c> applies a unary lambda to each element and collects the results.</summary>
    [TestMethod]
    public void MapAppliesLambdaPerElement()
    {
        Assert.AreEqual("[2,4,6]", Serialize("$map([1, 2, 3], function($v){$v * 2})", "{}"));
    }

    /// <summary><c>$map</c> passes the element index as the second argument.</summary>
    [TestMethod]
    public void MapPassesIndexArgument()
    {
        Assert.AreEqual("[0,1,2]", Serialize("$map([1, 2, 3], function($v, $i){$i})", "{}"));
    }

    /// <summary><c>$map</c> passes the whole source array as the third argument.</summary>
    [TestMethod]
    public void MapPassesArrayArgument()
    {
        Assert.AreEqual("[10,10,10]", Serialize("$map([1, 2, 3], function($v, $i, $a){$a[0] * 10})", "{}"));
    }

    /// <summary><c>$map</c> drops undefined results, so the output can be shorter than the input.</summary>
    [TestMethod]
    public void MapDropsUndefinedResults()
    {
        Assert.AreEqual("[1,3]", Serialize("$map([1, 2, 3], function($v){$v != 2 ? $v})", "{}"));
    }

    /// <summary><c>$map</c> accepts a built-in as the applied function, exercising the cursor's synchronous-apply path.</summary>
    [TestMethod]
    public void MapAppliesBuiltinFunction()
    {
        Assert.AreEqual("[\"A\",\"B\"]", Serialize("$map([\"a\", \"b\"], $uppercase)", "{}"));
    }

    /// <summary><c>$map</c> over an undefined array yields undefined (no output).</summary>
    [TestMethod]
    public void MapOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$map(missing, function($v){$v})", "{}").Kind);
    }

    /// <summary><c>$map</c> over a non-array, defined value treats it as a one-element array.</summary>
    [TestMethod]
    public void MapWrapsSingleValue()
    {
        Assert.AreEqual(10d, Evaluate("$map(5, function($v){$v * 2})", "{}").AsNumber);
    }

    /// <summary><c>$filter</c> keeps the elements (not the predicate values) whose predicate result is truthy.</summary>
    [TestMethod]
    public void FilterKeepsMatchingElements()
    {
        Assert.AreEqual("[3,4]", Serialize("$filter([1, 2, 3, 4], function($v){$v > 2})", "{}"));
    }

    /// <summary><c>$filter</c> uses JSONata truthiness, so a non-empty string is a truthy predicate result.</summary>
    [TestMethod]
    public void FilterCoercesTruthiness()
    {
        Assert.AreEqual("[\"a\",\"c\"]", Serialize("$filter([\"a\", \"\", \"c\"], function($v){$v})", "{}"));
    }

    /// <summary><c>$filter</c> over an undefined array yields undefined.</summary>
    [TestMethod]
    public void FilterOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$filter(missing, function($v){true})", "{}").Kind);
    }

    /// <summary><c>$filter</c> passes the element index, so an index predicate selects positions.</summary>
    [TestMethod]
    public void FilterPassesIndexArgument()
    {
        Assert.AreEqual("[10,30]", Serialize("$filter([10, 20, 30], function($v, $i){$i != 1})", "{}"));
    }

    /// <summary><c>$single</c> returns the one element whose predicate matches.</summary>
    [TestMethod]
    public void SingleReturnsTheLoneMatch()
    {
        Assert.AreEqual(4d, Evaluate("$single([2, 4, 6], function($v){$v = 4})", "{}").AsNumber);
    }

    /// <summary><c>$single</c> with no predicate over a one-element array returns that element.</summary>
    [TestMethod]
    public void SingleNoPredicateOverOneElement()
    {
        Assert.AreEqual(7d, Evaluate("$single([7])", "{}").AsNumber);
    }

    /// <summary><c>$single</c> with no predicate over a multi-element array throws D3138 (every element matches, so the second is a duplicate).</summary>
    [TestMethod]
    public void SingleNoPredicateOverMultipleThrowsD3138()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$single([1, 2])", "{}"));

        Assert.AreEqual(CodeSingleMultipleMatches, error.Code.ToString());
    }

    /// <summary><c>$single</c> with no predicate over an empty array throws D3139 (no element to match).</summary>
    [TestMethod]
    public void SingleNoPredicateOverEmptyThrowsD3139()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$single([])", "{}"));

        Assert.AreEqual(CodeSingleNoMatch, error.Code.ToString());
    }

    /// <summary><c>$single</c> over a no-match scan throws D3139.</summary>
    [TestMethod]
    public void SingleNoMatchThrowsD3139()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$single([1, 2, 3], function($v){$v > 5})", "{}"));

        Assert.AreEqual(CodeSingleNoMatch, error.Code.ToString());
    }

    /// <summary><c>$single</c> over a second match throws D3138.</summary>
    [TestMethod]
    public void SingleMultipleMatchesThrowsD3138()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$single([1, 2, 3], function($v){$v > 1})", "{}"));

        Assert.AreEqual(CodeSingleMultipleMatches, error.Code.ToString());
    }

    /// <summary><c>$single</c> over an undefined array yields undefined (no throw).</summary>
    [TestMethod]
    public void SingleOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$single(missing, function($v){true})", "{}").Kind);
    }

    /// <summary><c>$reduce</c> left-folds with an explicit initial value.</summary>
    [TestMethod]
    public void ReduceWithInitialValue()
    {
        Assert.AreEqual(20d, Evaluate("$reduce([1, 2, 3, 4], function($a, $b){$a + $b}, 10)", "{}").AsNumber);
    }

    /// <summary><c>$reduce</c> without an initial value seeds from the first element.</summary>
    [TestMethod]
    public void ReduceWithoutInitialValueSeedsFromFirst()
    {
        Assert.AreEqual(10d, Evaluate("$reduce([1, 2, 3, 4], function($a, $b){$a + $b})", "{}").AsNumber);
    }

    /// <summary><c>$reduce</c> passes the element index as the third argument.</summary>
    [TestMethod]
    public void ReducePassesIndexArgument()
    {
        Assert.AreEqual(3d, Evaluate("$reduce([10, 20, 30, 40], function($a, $b, $i){$i}, 0)", "{}").AsNumber);
    }

    /// <summary>
    /// <c>$reduce</c> without an initial value folds the remaining elements starting at index 1 (the first
    /// element seeds the accumulator and is not re-folded), so the indices seen by the reducer are 1, 2, ….
    /// </summary>
    [TestMethod]
    public void ReduceWithoutInitialValueFoldsFromIndexOne()
    {
        //Seed = 5 (items[0]); then fold index 1 -> 5*10+1 = 51; fold index 2 -> 51*10+2 = 512. A start at
        //index 0 would re-fold the seed element and yield 5012, so 512 pins the index-1 start.
        Assert.AreEqual(512d, Evaluate("$reduce([5, 5, 5], function($a, $v, $i){$a * 10 + $i})", "{}").AsNumber);
    }

    /// <summary><c>$reduce</c> with a one-parameter reducer throws D3050.</summary>
    [TestMethod]
    public void ReduceOneParameterReducerThrowsD3050()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$reduce([1, 2, 3], function($a){$a}, 0)", "{}"));

        Assert.AreEqual(CodeReduceArity, error.Code.ToString());
    }

    /// <summary><c>$reduce</c> over an undefined array yields undefined.</summary>
    [TestMethod]
    public void ReduceOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$reduce(missing, function($a, $b){$a + $b}, 0)", "{}").Kind);
    }

    /// <summary><c>$reduce</c> drives a recursive lambda through the cursor without C# recursion.</summary>
    [TestMethod]
    public void ReduceDrivesRecursiveLambda()
    {
        const string expression = "($fac := function($n){$n <= 1 ? 1 : $n * $fac($n - 1)}; $reduce([1, 2, 3, 4], function($a, $b){$a + $fac($b)}, 0))";

        Assert.AreEqual(33d, Evaluate(expression, "{}").AsNumber);
    }

    /// <summary>A user binding of a higher-order name shadows the higher-order function, yielding the bound value.</summary>
    [TestMethod]
    public void UserBindingShadowsHigherOrder()
    {
        Assert.AreEqual(5d, Evaluate("($map := 5; $map)", "{}").AsNumber);
    }

    /// <summary>A higher-order function applied through the chain operator <c>~&gt;</c> still applies its supplied function.</summary>
    [TestMethod]
    public void HigherOrderIsFirstClassInChain()
    {
        Assert.AreEqual("[2,4]", Serialize("[1, 2] ~> $map(function($v){$v * 2})", "{}"));
    }

    /// <summary>The registry value-type change did not regress a synchronous built-in: <c>$uppercase</c> still resolves and dispatches.</summary>
    [TestMethod]
    public void SynchronousBuiltinStillResolves()
    {
        Assert.AreEqual("HELLO", Evaluate("$uppercase(\"hello\")", "{}").AsString);
    }

    /// <summary><c>$each</c> applies a two-parameter lambda to <c>(value, key)</c> per entry and collects the results in insertion order.</summary>
    [TestMethod]
    public void EachAppliesFunctionPerEntryWithKey()
    {
        Assert.AreEqual("[\"a=1\",\"b=2\"]", Serialize("$each({\"a\": 1, \"b\": 2}, function($v, $k){$k & \"=\" & $v})", "{}"));
    }

    /// <summary><c>$each</c> passes the whole source object as the third argument.</summary>
    [TestMethod]
    public void EachPassesObjectArgument()
    {
        Assert.AreEqual("[1,1]", Serialize("$each({\"a\": 1, \"b\": 2}, function($v, $k, $o){$o.a})", "{}"));
    }

    /// <summary><c>$each</c> drops undefined results, so the output can be shorter than the entry count.</summary>
    [TestMethod]
    public void EachDropsUndefinedResults()
    {
        Assert.AreEqual("[1,3]", Serialize("$each({\"a\": 1, \"b\": 2, \"c\": 3}, function($v){$v != 2 ? $v})", "{}"));
    }

    /// <summary><c>$each</c> over a non-object value yields undefined (no output).</summary>
    [TestMethod]
    public void EachOverNonObjectIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$each([1, 2], function($v){$v})", "{}").Kind);
    }

    /// <summary><c>$each</c> over an undefined value yields undefined.</summary>
    [TestMethod]
    public void EachOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$each(missing, function($v){$v})", "{}").Kind);
    }

    /// <summary><c>$each</c> over an empty object yields undefined (the zero-entry cursor produces nothing).</summary>
    [TestMethod]
    public void EachOverEmptyObjectIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$each({}, function($v){$v})", "{}").Kind);
    }

    /// <summary><c>$sift</c> keeps the original (key, value) pairs whose predicate result is truthy, in insertion order.</summary>
    [TestMethod]
    public void SiftKeepsMatchingPairs()
    {
        Assert.AreEqual("{\"b\":2,\"c\":3}", Serialize("$sift({\"a\": 1, \"b\": 2, \"c\": 3}, function($v){$v > 1})", "{}"));
    }

    /// <summary><c>$sift(predicate)</c> with only the predicate takes the object from the evaluation focus (its context-injectable object parameter), keeping the matching pairs.</summary>
    [TestMethod]
    public void SiftFromContextFocusKeepsMatchingPairs()
    {
        Assert.AreEqual("{\"b\":2,\"c\":3}", Serialize("$sift(function($v){$v > 1})", "{\"a\": 1, \"b\": 2, \"c\": 3}"));
    }

    /// <summary><c>$sift</c> passes the key as the second predicate argument, so a key predicate selects entries.</summary>
    [TestMethod]
    public void SiftPassesKeyArgument()
    {
        Assert.AreEqual("{\"b\":2}", Serialize("$sift({\"ax\": 1, \"b\": 2}, function($v, $k){$k = \"b\"})", "{}"));
    }

    /// <summary><c>$sift</c> that keeps no entry yields undefined (the "nothing" value), not an empty object.</summary>
    [TestMethod]
    public void SiftWithNoMatchIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$sift({\"a\": 1, \"b\": 2}, function($v){$v > 5})", "{}").Kind);
    }

    /// <summary><c>$sift</c> over an empty object yields undefined (no entry to keep).</summary>
    [TestMethod]
    public void SiftOverEmptyObjectIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$sift({}, function($v){true})", "{}").Kind);
    }

    /// <summary><c>$sift</c> over a non-object value yields undefined.</summary>
    [TestMethod]
    public void SiftOverNonObjectIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$sift([1, 2], function($v){true})", "{}").Kind);
    }

    /// <summary><c>$sift</c> applied through the chain operator <c>~&gt;</c> takes the left object as its source.</summary>
    [TestMethod]
    public void SiftIsFirstClassInChain()
    {
        Assert.AreEqual("{\"b\":2}", Serialize("{\"a\": 1, \"b\": 2} ~> $sift(function($v){$v > 1})", "{}"));
    }

    /// <summary><c>$sort</c> with no comparator orders a number array ascending.</summary>
    [TestMethod]
    public void SortDefaultOrdersNumbersAscending()
    {
        Assert.AreEqual("[1,2,3]", Serialize("$sort([3, 1, 2])", "{}"));
    }

    /// <summary><c>$sort</c> with no comparator orders a string array by ordinal ascending.</summary>
    [TestMethod]
    public void SortDefaultOrdersStringsAscending()
    {
        Assert.AreEqual("[\"a\",\"b\",\"c\"]", Serialize("$sort([\"c\", \"a\", \"b\"])", "{}"));
    }

    /// <summary><c>$sort</c> with no comparator over a non-numeric/non-string array throws D3070.</summary>
    [TestMethod]
    public void SortDefaultOverNonComparableThrowsD3070()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("$sort([true, false])", "{}"));

        Assert.AreEqual(CodeSortDefaultComparatorType, error.Code.ToString());
    }

    /// <summary><c>$sort</c> over a single-element array returns it unchanged (no comparator, no type validation).</summary>
    [TestMethod]
    public void SortSingleElementIsUnchanged()
    {
        Assert.AreEqual("[5]", Serialize("$sort([5])", "{}"));
    }

    /// <summary><c>$sort</c> over an empty array returns the empty array.</summary>
    [TestMethod]
    public void SortEmptyArrayIsEmpty()
    {
        Assert.AreEqual("[]", Serialize("$sort([])", "{}"));
    }

    /// <summary><c>$sort</c> over an undefined array yields undefined (no output).</summary>
    [TestMethod]
    public void SortOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$sort(missing)", "{}").Kind);
    }

    /// <summary><c>$sort</c> with a comparator orders by the comparator (true means the left argument sorts after the right), here descending.</summary>
    [TestMethod]
    public void SortComparatorOrdersDescending()
    {
        Assert.AreEqual("[3,2,1]", Serialize("$sort([3, 1, 2], function($a, $b){$a < $b})", "{}"));
    }

    /// <summary>
    /// <c>$sort</c> with a comparator is stable: elements that the comparator treats as tied keep their input
    /// order. Sorting by the <c>k</c> key (only) puts the single <c>k = 0</c> element first and keeps the two
    /// tied <c>k = 1</c> elements in input order (x before y).
    /// </summary>
    [TestMethod]
    public void SortComparatorIsStableOverTies()
    {
        const string expression = "$sort([{\"k\": 1, \"t\": \"x\"}, {\"k\": 1, \"t\": \"y\"}, {\"k\": 0, \"t\": \"z\"}], function($a, $b){$a.k > $b.k})";

        Assert.AreEqual("[{\"k\":0,\"t\":\"z\"},{\"k\":1,\"t\":\"x\"},{\"k\":1,\"t\":\"y\"}]", Serialize(expression, "{}"));
    }

    /// <summary><c>$sort</c> with a comparator over an undefined array yields undefined.</summary>
    [TestMethod]
    public void SortComparatorOverUndefinedIsUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("$sort(missing, function($a, $b){$a > $b})", "{}").Kind);
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
