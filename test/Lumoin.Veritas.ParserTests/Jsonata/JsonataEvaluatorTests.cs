using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
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
/// End-to-end evaluator tests for the JSONata engine: field navigation, the dot/map over arrays,
/// predicate filters and positional indexing, arithmetic / concatenation / comparison / membership /
/// boolean / conditional operators, grouping, the wildcard <c>*</c>, the descendant <c>**</c>, the range
/// <c>..</c>, the array constructor <c>[ ... ]</c> (skip-undefined, keep-nested-whole, flatten-else, kept
/// singleton, missing-closer recovery), the object constructor <c>{ ... }</c> (single-object build, dynamic
/// keys, per-item via map, group-by over a sequence, the led path-step group-by <c>path{ ... }</c> grouping
/// the source's result with computed and quoted-selector keys, the T1003 / D1009 key errors, undefined-value
/// omission, the empty object over nothing, and the depth-bounded cursor), the variable bind <c>:=</c> and the block
/// <c>( ... )</c> (the bound value, multi-bind and re-bind in a block frame, right-associative chains,
/// block-scope isolation so an inner bind does not leak, the last-statement value, the empty and
/// trailing-semicolon blocks, the shared block focus, and the unbound-variable read), the user-defined
/// function (lambda) and function application (the immediately-invoked and bound-by-name forms, positional
/// argument binding, recursion through the captured frame, closure over an outer binding, too-few-arguments
/// binding undefined and surplus-arguments-ignored, the T1006 non-function-call error, the higher-order
/// call, and the definition-time focus capture), the function-application / chain operator <c>~&gt;</c> (the
/// apply, call-prepend, and compose cases, left-to-right chaining, first-then-second composition order, and
/// the T2006 non-function right — including the function-left case), the source-verified edge cases (undefined propagation per
/// operator family, recursive truthiness, the inner-array index promotion, array-transparent descent, the
/// range guard order), and the <see cref="JsonataValueAdapter"/> round-trip.
/// </summary>
[TestClass]
internal sealed class JsonataEvaluatorTests
{
    /// <summary>A bare name reads a field of the input object.</summary>
    [TestMethod]
    public void FieldNavigationReadsObjectField()
    {
        JsonataValue result = Evaluate("price", "{ \"price\": 42 }");

        Assert.AreEqual(JsonataValueKind.Number, result.Kind);
        Assert.AreEqual(42d, result.AsNumber);
    }

    /// <summary>A missing field yields the undefined value (serialized as no output).</summary>
    [TestMethod]
    public void MissingFieldYieldsUndefined()
    {
        JsonataValue result = Evaluate("missing", "{ \"price\": 42 }");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
        Assert.AreEqual(string.Empty, JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A nested field path navigates object to object.</summary>
    [TestMethod]
    public void DotPathNavigatesNestedObjects()
    {
        JsonataValue result = Evaluate("a.b", "{ \"a\": { \"b\": \"deep\" } }");

        Assert.AreEqual(JsonataValueKind.String, result.Kind);
        Assert.AreEqual("deep", result.AsString);
    }

    /// <summary>The dot/map over an array of objects collects the field from each element.</summary>
    [TestMethod]
    public void DotMapOverArrayCollectsFields()
    {
        JsonataValue result = Evaluate("items.name", "{ \"items\": [ { \"name\": \"a\" }, { \"name\": \"b\" }, { \"name\": \"c\" } ] }");

        Assert.AreEqual("[\"a\",\"b\",\"c\"]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A field reference over an array maps the lookup, dropping elements that lack the field.</summary>
    [TestMethod]
    public void FieldOverArrayDropsMissing()
    {
        JsonataValue result = Evaluate("name", "[ { \"name\": \"a\" }, { \"other\": 1 }, { \"name\": \"b\" } ]");

        Assert.AreEqual("[\"a\",\"b\"]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A boolean predicate keeps items whose per-item result is truthy.</summary>
    [TestMethod]
    public void PredicateBooleanFilterKeepsMatches()
    {
        JsonataValue result = Evaluate("items[value > 1].value", "{ \"items\": [ { \"value\": 1 }, { \"value\": 2 }, { \"value\": 3 } ] }");

        Assert.AreEqual("[2,3]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A numeric predicate selects positionally by a floored index.</summary>
    [TestMethod]
    public void PredicateNumericIndexSelectsPosition()
    {
        JsonataValue result = Evaluate("items[1]", "{ \"items\": [ \"a\", \"b\", \"c\" ] }");

        Assert.AreEqual(JsonataValueKind.String, result.Kind);
        Assert.AreEqual("b", result.AsString);
    }

    /// <summary>A negative numeric predicate indexes from the end of the sequence.</summary>
    [TestMethod]
    public void PredicateNegativeIndexCountsFromEnd()
    {
        JsonataValue result = Evaluate("items[-1]", "{ \"items\": [ \"a\", \"b\", \"c\" ] }");

        Assert.AreEqual("c", result.AsString);
    }

    /// <summary>A non-integer numeric predicate floors toward negative infinity, not toward zero.</summary>
    [TestMethod]
    public void PredicateNonIntegerIndexFloors()
    {
        JsonataValue result = Evaluate("items[1.9]", "{ \"items\": [ \"a\", \"b\", \"c\" ] }");

        Assert.AreEqual("b", result.AsString);
    }

    /// <summary>When an integer index selects an item that is itself an array, that inner array becomes the whole result.</summary>
    [TestMethod]
    public void PredicateIndexPromotesInnerArray()
    {
        JsonataValue result = Evaluate("a[0]", "{ \"a\": [ [ 1, 2 ], 3 ] }");

        Assert.AreEqual("[1,2]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>Arithmetic over numbers computes the expected result.</summary>
    [TestMethod]
    public void ArithmeticComputesOverNumbers()
    {
        Assert.AreEqual(5d, Evaluate("a + b", "{ \"a\": 2, \"b\": 3 }").AsNumber);
        Assert.AreEqual(6d, Evaluate("a * b", "{ \"a\": 2, \"b\": 3 }").AsNumber);
        Assert.AreEqual(2d, Evaluate("a % b", "{ \"a\": 8, \"b\": 3 }").AsNumber);
    }

    /// <summary>A defined non-numeric arithmetic left operand throws T2001.</summary>
    [TestMethod]
    public void ArithmeticNonNumericLeftThrowsT2001()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("a + 1", "{ \"a\": \"x\" }"));

        Assert.AreEqual("T2001", error.Code.ToString());
    }

    /// <summary>Concatenation coerces each operand, joining the textual forms.</summary>
    [TestMethod]
    public void ConcatCoercesOperands()
    {
        JsonataValue result = Evaluate("a & b", "{ \"a\": \"x\", \"b\": 1 }");

        Assert.AreEqual("x1", result.AsString);
    }

    /// <summary>Concatenation treats an undefined operand as the empty string, so the right operand survives.</summary>
    [TestMethod]
    public void ConcatTreatsUndefinedAsEmptyString()
    {
        JsonataValue result = Evaluate("missing & b", "{ \"b\": \"kept\" }");

        Assert.AreEqual("kept", result.AsString);
    }

    /// <summary>Comparison of two numbers yields a boolean.</summary>
    [TestMethod]
    public void ComparisonOrdersNumbers()
    {
        Assert.IsTrue(Evaluate("a < b", "{ \"a\": 1, \"b\": 2 }").AsBoolean);
        Assert.IsFalse(Evaluate("a >= b", "{ \"a\": 1, \"b\": 2 }").AsBoolean);
    }

    /// <summary>Comparing a number with a string throws the type-mismatch error T2009.</summary>
    [TestMethod]
    public void ComparisonTypeMismatchThrowsT2009()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("1 < a", "{ \"a\": \"x\" }"));

        Assert.AreEqual("T2009", error.Code.ToString());
    }

    /// <summary>Comparing with a non-string/number operand (an array) throws the non-comparable error T2010.</summary>
    [TestMethod]
    public void ComparisonNonComparableThrowsT2010()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("a < 3", "{ \"a\": [ 1, 2 ] }"));

        Assert.AreEqual("T2010", error.Code.ToString());
    }

    /// <summary>Equality is deep: null equals null is true.</summary>
    [TestMethod]
    public void EqualityNullEqualsNull()
    {
        Assert.IsTrue(Evaluate("a = b", "{ \"a\": null, \"b\": null }").AsBoolean);
    }

    /// <summary>Equality with a missing field is false, not undefined.</summary>
    [TestMethod]
    public void EqualityWithMissingFieldIsFalse()
    {
        JsonataValue result = Evaluate("missing = 5", "{ \"present\": 5 }");

        Assert.AreEqual(JsonataValueKind.Boolean, result.Kind);
        Assert.IsFalse(result.AsBoolean);
    }

    /// <summary>Deep equality holds over structurally-equal arrays.</summary>
    [TestMethod]
    public void EqualityIsStructuralOverArrays()
    {
        Assert.IsTrue(Evaluate("a = b", "{ \"a\": [ 1, 2 ], \"b\": [ 1, 2 ] }").AsBoolean);
        Assert.IsFalse(Evaluate("a = b", "{ \"a\": [ 1, 2 ], \"b\": [ 2, 1 ] }").AsBoolean);
    }

    /// <summary>Membership tests whether a value deep-equals some element of the right side.</summary>
    [TestMethod]
    public void MembershipFindsElement()
    {
        Assert.IsTrue(Evaluate("a in b", "{ \"a\": 2, \"b\": [ 1, 2, 3 ] }").AsBoolean);
        Assert.IsFalse(Evaluate("a in b", "{ \"a\": 9, \"b\": [ 1, 2, 3 ] }").AsBoolean);
    }

    /// <summary>The boolean operators booleanize both operands, so undefined behaves as false.</summary>
    [TestMethod]
    public void BooleanOperatorsBooleanizeOperands()
    {
        Assert.IsTrue(Evaluate("a or b", "{ \"a\": false, \"b\": true }").AsBoolean);
        Assert.IsFalse(Evaluate("a and missing", "{ \"a\": true }").AsBoolean);
    }

    /// <summary>A truthy conditional with no else branch yields its value.</summary>
    [TestMethod]
    public void ConditionalTruthyYieldsValue()
    {
        JsonataValue result = Evaluate("a ? \"y\"", "{ \"a\": 5 }");

        Assert.AreEqual("y", result.AsString);
    }

    /// <summary>A falsy conditional with no else branch yields no output (the undefined value).</summary>
    [TestMethod]
    public void ConditionalFalsyNoElseYieldsUndefined()
    {
        JsonataValue result = Evaluate("a ? \"y\"", "{ \"a\": 0 }");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>A conditional with an else branch selects the else value when the condition is falsy.</summary>
    [TestMethod]
    public void ConditionalFalsySelectsElse()
    {
        JsonataValue result = Evaluate("a ? \"y\" : \"n\"", "{ \"a\": false }");

        Assert.AreEqual("n", result.AsString);
    }

    /// <summary>A singleton array of a falsy element is itself falsy (recursive truthiness).</summary>
    [TestMethod]
    public void SingletonArrayTruthinessRecurses()
    {
        JsonataValue falseResult = Evaluate("a ? \"y\" : \"n\"", "{ \"a\": [ false ] }");
        JsonataValue trueResult = Evaluate("a ? \"y\" : \"n\"", "{ \"a\": [ true ] }");

        Assert.AreEqual("n", falseResult.AsString);
        Assert.AreEqual("y", trueResult.AsString);
    }

    /// <summary>An array of length greater than one is truthy when any element is truthy.</summary>
    [TestMethod]
    public void MultiElementArrayTruthinessIsAnyTruthy()
    {
        JsonataValue result = Evaluate("a ? \"y\" : \"n\"", "{ \"a\": [ false, 0, 1 ] }");

        Assert.AreEqual("y", result.AsString);
    }

    /// <summary>Grouping overrides precedence so the addition happens before the multiplication.</summary>
    [TestMethod]
    public void GroupingOverridesPrecedence()
    {
        JsonataValue result = Evaluate("(a + b) * c", "{ \"a\": 1, \"b\": 2, \"c\": 3 }");

        Assert.AreEqual(9d, result.AsNumber);
    }

    /// <summary>Unary negation negates a number and propagates undefined.</summary>
    [TestMethod]
    public void UnaryNegateNegatesAndPropagatesUndefined()
    {
        Assert.AreEqual(-5d, Evaluate("-a", "{ \"a\": 5 }").AsNumber);
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("-missing", "{ \"a\": 5 }").Kind);
    }

    /// <summary>Arithmetic and comparison return undefined when an operand is undefined; equality, membership return false.</summary>
    [TestMethod]
    public void UndefinedPropagatesPerOperatorFamily()
    {
        //Arithmetic: undefined passes through.
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("missing + 1", "{ \"a\": 1 }").Kind);

        //Comparison: undefined passes through.
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("missing < 1", "{ \"a\": 1 }").Kind);

        //Equality: undefined yields false.
        Assert.IsFalse(Evaluate("missing = 1", "{ \"a\": 1 }").AsBoolean);

        //Membership: undefined yields false.
        Assert.IsFalse(Evaluate("missing in b", "{ \"b\": [ 1 ] }").AsBoolean);

        //Concat: undefined becomes the empty string.
        Assert.AreEqual("1", Evaluate("missing & b", "{ \"b\": 1 }").AsString);
    }

    /// <summary>A value bridged to a JsonataValue and back to a JsonNode preserves its JSON-representable shape.</summary>
    [TestMethod]
    public void AdapterRoundTripPreservesValue()
    {
        const string json = "{ \"s\": \"x\", \"n\": 1.5, \"b\": true, \"z\": null, \"arr\": [ 1, 2 ], \"obj\": { \"k\": \"v\" } }";
        JsonNode source = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(json)));

        JsonataValue value = JsonataValueAdapter.FromJsonNode(source);
        JsonNode roundTripped = JsonataValueAdapter.ToJsonNode(value);
        JsonataValue reread = JsonataValueAdapter.FromJsonNode(roundTripped);

        Assert.IsTrue(JsonataValue.DeepEquals(value, reread));
        Assert.AreEqual("{\"s\":\"x\",\"n\":1.5,\"b\":true,\"z\":null,\"arr\":[1,2],\"obj\":{\"k\":\"v\"}}", JsonataEngine.SerializeToJson(value).ToString());
    }

    /// <summary>The whole-pipeline bytes-to-bytes overload parses, evaluates, and serializes through the host JSON parser.</summary>
    [TestMethod]
    public void BytesToBytesPipelineSerializesResult()
    {
        System.ReadOnlyMemory<byte> output = JsonataEngine.Evaluate(
            Encoding.UTF8.GetBytes("a + b"),
            Encoding.UTF8.GetBytes("{ \"a\": 2, \"b\": 5 }"),
            StjJsonAdapter.Parse);

        Assert.AreEqual("7", Encoding.UTF8.GetString(output.Span));
    }

    /// <summary>An astral (non-BMP) character serializes to its correct 4-byte UTF-8 and round-trips through the host parser, not two replacement characters.</summary>
    [TestMethod]
    public void SerializeKeepsAstralCharacterPaired()
    {
        //U+1D54F MATHEMATICAL DOUBLE-STRUCK CAPITAL X encodes to the four UTF-8 bytes F0 9D 95 8F.
        const string astral = "\U0001D54F";
        JsonataValue value = JsonataValue.String(astral);

        Utf8String serialized = JsonataEngine.SerializeToJson(value);
        byte[] expected = [(byte)'"', 0xF0, 0x9D, 0x95, 0x8F, (byte)'"'];
        Assert.IsTrue(serialized.Span.SequenceEqual(expected), "The astral character must serialize to its 4-byte UTF-8, not two replacement characters.");

        JsonNode reparsed = StjJsonAdapter.Parse(serialized);
        JsonataValue reread = JsonataValueAdapter.FromJsonNode(reparsed);
        Assert.AreEqual(JsonataValueKind.String, reread.Kind);
        Assert.AreEqual(astral, reread.AsString);
    }

    /// <summary>A divide-by-zero is not range-checked at the operation itself: it yields the IEEE-754 infinity unchanged, matching the reference (which surfaces the infinity later, not at the division).</summary>
    [TestMethod]
    public void DivideByZeroYieldsNonFiniteNumber()
    {
        JsonataValue result = Evaluate("a / b", "{ \"a\": 1, \"b\": 0 }");

        Assert.AreEqual(JsonataValueKind.Number, result.Kind);
        Assert.IsTrue(double.IsPositiveInfinity(result.AsNumber));
    }

    /// <summary>A non-finite number flowing in as an arithmetic operand is rejected with D1001 (the reference's <c>isNumeric</c> guard), so an overflow used in a further operation surfaces the range error.</summary>
    [TestMethod]
    public void NonFiniteArithmeticOperandThrowsD1001()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("1/(10e300 * 10e100)", "{}"));

        Assert.AreEqual("D1001", error.Code.ToString());
    }

    /// <summary>A literal numeric index that selects an empty inner array yields undefined (the empty sequence collapses, not the inner array).</summary>
    [TestMethod]
    public void PredicateLiteralIndexSelectingEmptyArrayYieldsUndefined()
    {
        JsonataValue result = Evaluate("a[0]", "{ \"a\": [ [] ] }");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>A boolean per-item filter that keeps exactly one array-valued item keeps that item (no inner-array promotion outside the literal-index branch).</summary>
    [TestMethod]
    public void PredicateBooleanFilterKeepsSingleArrayItem()
    {
        //Each item is a two-element array; the filter keeps only the one whose first element equals 1.
        JsonataValue result = Evaluate("a[$[0] = 1]", "{ \"a\": [ [ 1, 2 ], [ 3, 4 ] ] }");

        Assert.AreEqual("[1,2]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A huge literal index is floored in double space and never aliases onto a valid position; the result is undefined.</summary>
    [TestMethod]
    public void PredicateHugeLiteralIndexYieldsUndefined()
    {
        JsonataValue result = Evaluate("items[1e20]", "{ \"items\": [ \"a\", \"b\", \"c\" ] }");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>A per-item filter yielding an array of numbers keeps each item whose position is any of those indices.</summary>
    [TestMethod]
    public void PredicateArrayOfNumbersFilterSelectsByPosition()
    {
        //$indices is an array of numbers identical for every item, so positions 0 and 2 are kept.
        JsonataValue result = Evaluate("items[indices]", "{ \"items\": [ { \"indices\": [ 0, 2 ] }, { \"indices\": [ 0, 2 ] }, { \"indices\": [ 0, 2 ] } ] }");

        Assert.AreEqual("[{\"indices\":[0,2]},{\"indices\":[0,2]}]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A bare-name field lookup over a deeply-nested input array throws a catchable depth-limit error, not a stack overflow.</summary>
    [TestMethod]
    public void DeeplyNestedArrayFieldLookupThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("x")).Tree;
        JsonataValue input = BuildNestedArrays(JsonataValue.Object([new KeyValuePair<string, JsonataValue>("x", JsonataValue.Number(1))]), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
    }

    /// <summary>A conditional whose condition is a deeply-nested singleton array throws a catchable depth-limit error, not a stack overflow.</summary>
    [TestMethod]
    public void DeeplyNestedSingletonConditionThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("$ ? \"y\" : \"n\"")).Tree;
        JsonataValue input = BuildNestedSingletons(JsonataValue.Boolean(true), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
    }

    /// <summary>The wildcard <c>*</c> over an object selects its field values in key order, flattening an array-valued field one level.</summary>
    [TestMethod]
    public void WildcardSelectsObjectValuesFlatteningArrays()
    {
        JsonataValue result = Evaluate("*", "{ \"a\": 1, \"b\": [ 2, 3 ], \"c\": \"x\" }");

        Assert.AreEqual("[1,2,3,\"x\"]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The wildcard <c>*</c> deep-flattens an array-valued field, spreading arbitrarily nested arrays into the result.</summary>
    [TestMethod]
    public void WildcardDeepFlattensNestedArrayField()
    {
        JsonataValue result = Evaluate("*", "{ \"b\": [ [ 2, 3 ], 4 ] }");

        Assert.AreEqual("[2,3,4]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The wildcard <c>*</c> over a scalar focus contributes nothing (the empty/undefined value).</summary>
    [TestMethod]
    public void WildcardOverScalarYieldsUndefined()
    {
        JsonataValue result = Evaluate("*", "42");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>The wildcard <c>*</c> over an array focus selects its elements (the array is scanned as an object whose keys are its indices).</summary>
    [TestMethod]
    public void WildcardOverArraySelectsElements()
    {
        JsonataValue result = Evaluate("*", "[ 1, 2, 3 ]");

        Assert.AreEqual("[1,2,3]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The descendant <c>**</c> over a nested object yields the focus object and every nested value in pre-order.</summary>
    [TestMethod]
    public void DescendantYieldsFocusAndNestedValuesInPreOrder()
    {
        JsonataValue result = Evaluate("**", "{ \"a\": 1, \"b\": { \"c\": 2 } }");

        //Pre-order: the whole object, then 1, then {c:2}, then 2.
        Assert.AreEqual("[{\"a\":1,\"b\":{\"c\":2}},1,{\"c\":2},2]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The descendant <c>**</c> treats arrays as transparent containers: the array itself is never collected, only its members, while the focus object is.</summary>
    [TestMethod]
    public void DescendantTreatsArraysAsTransparent()
    {
        JsonataValue result = Evaluate("**", "{ \"x\": [ 10, 20 ] }");

        //The focus object is included; the array wrapper is not; its members are visited in order.
        Assert.AreEqual("[{\"x\":[10,20]},10,20]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The range <c>1..5</c> produces the inclusive ascending integer array.</summary>
    [TestMethod]
    public void RangeProducesInclusiveIntegerArray()
    {
        JsonataValue result = Evaluate("1..5", "{}");

        Assert.AreEqual("[1,2,3,4,5]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A one-element range <c>3..3</c> normalizes as a sequence and auto-unwraps to the bare number.</summary>
    [TestMethod]
    public void RangeSingletonUnwrapsToBareValue()
    {
        JsonataValue result = Evaluate("3..3", "{}");

        Assert.AreEqual("3", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A range whose low bound exceeds its high bound yields no output (the undefined value), not a reversed range or an error.</summary>
    [TestMethod]
    public void RangeLowAboveHighYieldsUndefined()
    {
        JsonataValue result = Evaluate("5..1", "{}");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>A range with an undefined bound yields undefined (the empty sequence), with no error.</summary>
    [TestMethod]
    public void RangeWithUndefinedBoundYieldsUndefined()
    {
        JsonataValue result = Evaluate("missing..5", "{ \"present\": 1 }");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>A range with a non-integer bound throws the left-bound integer error T2003.</summary>
    [TestMethod]
    public void RangeNonIntegerBoundThrowsT2003()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("1.5..5", "{}"));

        Assert.AreEqual("T2003", error.Code.ToString());
    }

    /// <summary>A range whose element count exceeds the maximum throws the oversize-range error D2014.</summary>
    [TestMethod]
    public void RangeOversizeThrowsD2014()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("1..100000000", "{}"));

        Assert.AreEqual("D2014", error.Code.ToString());
    }

    /// <summary>A range whose span exceeds the long range (the count would overflow a <c>long</c> cast) still throws D2014, computing the cap in double space rather than looping forever.</summary>
    [TestMethod]
    public void RangeSpanAboveLongRangeThrowsD2014()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("1..1e19", "{}"));

        Assert.AreEqual("D2014", error.Code.ToString());
    }

    /// <summary>The descendant <c>**</c> over a deeply-nested input throws a catchable depth-limit error, not a stack overflow.</summary>
    [TestMethod]
    public void DeeplyNestedDescendantThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("**")).Tree;
        JsonataValue input = BuildNestedArrays(JsonataValue.Number(1), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);

        //A deeply-nested-data guard is an engine-internal safety bound, not a non-terminating recursion, so it
        //carries no JSONata spec code (distinct from the U1001 recursion limits).
        Assert.IsTrue(error.Code.IsEmpty);
    }

    /// <summary>The array constructor <c>[1,2,3]</c> over literal elements builds the three-element array.</summary>
    [TestMethod]
    public void ArrayConstructorBuildsLiteralArray()
    {
        JsonataValue result = Evaluate("[1,2,3]", "{}");

        Assert.AreEqual("[1,2,3]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The empty array constructor <c>[]</c> builds the empty array (not undefined).</summary>
    [TestMethod]
    public void ArrayConstructorEmptyBuildsEmptyArray()
    {
        JsonataValue result = Evaluate("[]", "{}");

        Assert.AreEqual(JsonataValueKind.Array, result.Kind);
        Assert.AreEqual("[]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A singleton array constructor <c>[5]</c> is kept as a one-element array — it is not auto-unwrapped to the bare value.</summary>
    [TestMethod]
    public void ArrayConstructorSingletonStaysArray()
    {
        JsonataValue result = Evaluate("[5]", "{}");

        Assert.AreEqual(JsonataValueKind.Array, result.Kind);
        Assert.AreEqual("[5]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An element that yields a multi-value sequence (a field over an array of objects) spreads its items one level into the result.</summary>
    [TestMethod]
    public void ArrayConstructorFieldSpreadFlattensSequence()
    {
        JsonataValue result = Evaluate("[a.b]", "{ \"a\": [ { \"b\": 1 }, { \"b\": 2 }, { \"b\": 3 } ] }");

        //a.b yields the sequence [1,2,3]; the element is not a nested constructor, so it spreads.
        Assert.AreEqual("[1,2,3]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>Nested array constructors <c>[[1,2],[3]]</c> are kept whole: each inner constructor stays one element, never spread.</summary>
    [TestMethod]
    public void ArrayConstructorKeepsNestedConstructorsWhole()
    {
        JsonataValue result = Evaluate("[[1,2],[3]]", "{}");

        Assert.AreEqual("[[1,2],[3]]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A range element <c>[1..3]</c> is not a nested constructor, so its array value spreads into the result.</summary>
    [TestMethod]
    public void ArrayConstructorSpreadsRangeElement()
    {
        JsonataValue result = Evaluate("[1..3]", "{}");

        Assert.AreEqual("[1,2,3]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An element that evaluates to undefined (a missing field) is skipped, so <c>[x, 1]</c> over a missing <c>x</c> yields <c>[1]</c>.</summary>
    [TestMethod]
    public void ArrayConstructorSkipsUndefinedElement()
    {
        JsonataValue result = Evaluate("[x, 1]", "{ \"present\": 9 }");

        Assert.AreEqual("[1]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// An unterminated array constructor <c>[1,2</c> records a JS0004 missing-closer diagnostic yet its
    /// recovered tree still evaluates to an array of the elements parsed before the missing closer,
    /// exercising the parser's keep-the-partial-node recovery end to end.
    /// </summary>
    [TestMethod]
    public void ArrayConstructorMissingCloserStillYieldsArray()
    {
        //The throwing facade rejects the diagnostic, so the recovered tree is evaluated directly.
        ParseResult<JsonataExpression> parsed = JsonataEngine.Parse(Encoding.UTF8.GetBytes("[1,2"));
        Assert.IsTrue(parsed.HasErrors);

        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes("{}")));
        JsonataValue result = JsonataEvaluator.Evaluate(parsed.Tree, JsonataValueAdapter.FromJsonNode(input));

        Assert.AreEqual("[1,2]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// An array constructor used as a path step <c>Account.Order.[Product.Price]</c> keeps each step result
    /// whole (the JSONata cons marker): the per-order <c>[Product.Price]</c> array is pushed whole into the
    /// outer map rather than flattened, so the prices group per order.
    /// </summary>
    [TestMethod]
    public void ArrayConstructorAsPathStepKeepsEachStepWhole()
    {
        JsonataValue result = Evaluate(
            "Account.Order.[Product.Price]",
            "{ \"Account\": { \"Order\": [ { \"Product\": [ { \"Price\": 34.45 }, { \"Price\": 21.67 } ] }, { \"Product\": [ { \"Price\": 34.45 }, { \"Price\": 107.99 } ] } ] } }");

        Assert.AreEqual("[[34.45,21.67],[34.45,107.99]]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An array constructor as the trailing step <c>nest0.nest1.nest2.[nest3]</c> wraps each leaf in a one-element array.</summary>
    [TestMethod]
    public void ArrayConstructorAsTrailingStepWrapsEachLeaf()
    {
        JsonataValue result = Evaluate(
            "nest0.nest1.nest2.[nest3]",
            "{ \"nest0\": [ { \"nest1\": [ { \"nest2\": [ { \"nest3\": 1 }, { \"nest3\": 2 } ] } ] } ] }");

        Assert.AreEqual("[[1],[2]]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A leading array-constructor step <c>nest0.[nest1.nest2.nest3]</c> groups the navigated leaves under each top item.</summary>
    [TestMethod]
    public void ArrayConstructorAsLeadingStepGroupsNavigatedLeaves()
    {
        JsonataValue result = Evaluate(
            "nest0.[nest1.nest2.nest3]",
            "{ \"nest0\": [ { \"nest1\": [ { \"nest2\": [ { \"nest3\": 1 }, { \"nest3\": 2 } ] } ] }, { \"nest1\": [ { \"nest2\": [ { \"nest3\": 3 }, { \"nest3\": 4 } ] } ] } ] }");

        Assert.AreEqual("[[1,2],[3,4]]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A middle array-constructor step <c>nest0.nest1.[nest2.nest3]</c> keeps each nest1 group's leaves whole.</summary>
    [TestMethod]
    public void ArrayConstructorAsMiddleStepKeepsGroupsWhole()
    {
        JsonataValue result = Evaluate(
            "nest0.nest1.[nest2.nest3]",
            "{ \"nest0\": [ { \"nest1\": [ { \"nest2\": [ { \"nest3\": 1 }, { \"nest3\": 2 } ] }, { \"nest2\": [ { \"nest3\": 3 }, { \"nest3\": 4 } ] } ] } ] }");

        Assert.AreEqual("[[1,2],[3,4]]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// Deeply-nested array-constructor steps <c>nest0.[nest1.[nest2.[nest3]]]</c> compose: the cons marker
    /// rides on each level's value so every enclosing map step keeps its result whole, building the full
    /// four-deep nesting (the acceptance bar for the composition).
    /// </summary>
    [TestMethod]
    public void NestedArrayConstructorStepsCompose()
    {
        JsonataValue result = Evaluate(
            "nest0.[nest1.[nest2.[nest3]]]",
            "{ \"nest0\": [ { \"nest1\": [ { \"nest2\": [ { \"nest3\": [1] }, { \"nest3\": [2] } ] }, { \"nest2\": [ { \"nest3\": [3] }, { \"nest3\": [4] } ] } ] }, { \"nest1\": [ { \"nest2\": [ { \"nest3\": [5] }, { \"nest3\": [6] } ] }, { \"nest2\": [ { \"nest3\": [7] }, { \"nest3\": [8] } ] } ] } ] }");

        Assert.AreEqual("[[[[1],[2]],[[3],[4]]],[[[5],[6]],[[7],[8]]]]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The empty-bracket keep-array marker <c>phone[0][]</c> keeps a single selected value an array (a singleton stays an array).</summary>
    [TestMethod]
    public void KeepArrayMarkerKeepsSingleSelectionAsArray()
    {
        JsonataValue result = Evaluate(
            "phone[0][]",
            "[ { \"phone\": [ { \"number\": 0 } ] }, { \"phone\": [ { \"number\": 1 } ] } ]");

        Assert.AreEqual("[{\"number\":0}]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The keep-array marker propagates through a following step <c>phone[0][].number</c> so the whole path's singleton result stays an array.</summary>
    [TestMethod]
    public void KeepArrayMarkerPropagatesThroughFollowingStep()
    {
        JsonataValue result = Evaluate(
            "phone[0][].number",
            "[ { \"phone\": [ { \"number\": 7 } ] }, { \"phone\": [ { \"number\": 8 } ] } ]");

        Assert.AreEqual("[7]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A keep-array marker before a filter <c>items[][value=1]</c> keeps the single filtered match an array.</summary>
    [TestMethod]
    public void KeepArrayMarkerBeforeFilterKeepsMatchAsArray()
    {
        JsonataValue result = Evaluate(
            "items[][value=1]",
            "{ \"items\": [ { \"value\": 1 }, { \"value\": 2 } ] }");

        Assert.AreEqual("[{\"value\":1}]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An array constructor that is NOT a path step <c>[a.b]</c> still flattens its element one level (the cons marker is only set on a path-step constructor), so normal construction is unchanged.</summary>
    [TestMethod]
    public void NonPathStepArrayConstructorStillFlattensElement()
    {
        JsonataValue result = Evaluate("[a.b]", "{ \"a\": [ { \"b\": 1 }, { \"b\": 2 }, { \"b\": 3 } ] }");

        Assert.AreEqual("[1,2,3]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>Normal array navigation <c>items.name</c> over an array of objects still flattens one level (unchanged by the cons / keep-array work).</summary>
    [TestMethod]
    public void NormalArrayNavigationStillFlattens()
    {
        JsonataValue result = Evaluate("items.name", "{ \"items\": [ { \"name\": \"a\" }, { \"name\": \"b\" }, { \"name\": \"c\" } ] }");

        Assert.AreEqual("[\"a\",\"b\",\"c\"]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The object constructor <c>{"x": 1, "y": 2}</c> over a single object builds the two-member object.</summary>
    [TestMethod]
    public void ObjectConstructorBuildsLiteralObject()
    {
        JsonataValue result = Evaluate("{\"x\": 1, \"y\": 2}", "{}");

        Assert.AreEqual(JsonataValueKind.Object, result.Kind);
        Assert.AreEqual("{\"x\":1,\"y\":2}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A member value that reads a field of the focus is evaluated against that focus: <c>{"n": $.name}</c> over <c>{name:"a"}</c> yields <c>{n:"a"}</c>.</summary>
    [TestMethod]
    public void ObjectConstructorValueReadsFocusField()
    {
        JsonataValue result = Evaluate("{\"n\": $.name}", "{ \"name\": \"a\" }");

        Assert.AreEqual("{\"n\":\"a\"}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An object constructor as a map step is evaluated once per source item, building one object per item: <c>data.{"id": id}</c> over an array of objects yields an array of per-item objects.</summary>
    [TestMethod]
    public void ObjectConstructorPerItemViaMap()
    {
        JsonataValue result = Evaluate("data.{\"id\": id}", "{ \"data\": [ { \"id\": 1 }, { \"id\": 2 } ] }");

        Assert.AreEqual("[{\"id\":1},{\"id\":2}]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// A standalone object constructor evaluates its keys against the WHOLE focus, not per array element, so a
    /// field-reference key over an array focus is the array of that field's values (a non-string) and raises
    /// the key-type error T1003. Per-element grouping is the path-step form <c>path{ ... }</c>.
    /// </summary>
    [TestMethod]
    public void StandaloneObjectConstructorKeyOverArrayThrowsT1003()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("{t: v}", "[ { \"t\": \"a\", \"v\": 1 }, { \"t\": \"a\", \"v\": 2 }, { \"t\": \"b\", \"v\": 3 } ]"));

        Assert.AreEqual("T1003", error.Code.ToString());
    }

    /// <summary>A key that evaluates to a non-string throws the key-type error T1003.</summary>
    [TestMethod]
    public void ObjectConstructorNonStringKeyThrowsT1003()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("{v: t}", "{ \"v\": 1, \"t\": \"a\" }"));

        Assert.AreEqual("T1003", error.Code.ToString());
    }

    /// <summary>Two member pairs whose keys collide on the same string throw the duplicate-key error D1009.</summary>
    [TestMethod]
    public void ObjectConstructorDuplicateKeyFromDifferentPairsThrowsD1009()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("{\"x\": 1, \"x\": 2}", "{}"));

        Assert.AreEqual("D1009", error.Code.ToString());
    }

    /// <summary>A member whose value evaluates to undefined is omitted: <c>{"x": 1, "y": missing}</c> yields just <c>{x:1}</c>.</summary>
    [TestMethod]
    public void ObjectConstructorOmitsUndefinedValueMember()
    {
        JsonataValue result = Evaluate("{\"x\": 1, \"y\": missing}", "{}");

        Assert.AreEqual("{\"x\":1}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A member whose key evaluates to undefined is skipped for that item, producing no member.</summary>
    [TestMethod]
    public void ObjectConstructorSkipsUndefinedKeyMember()
    {
        JsonataValue result = Evaluate("{missing: 1, \"y\": 2}", "{}");

        Assert.AreEqual("{\"y\":2}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The empty object constructor <c>{}</c> over an object focus builds the empty object (not undefined).</summary>
    [TestMethod]
    public void ObjectConstructorEmptyBuildsEmptyObject()
    {
        JsonataValue result = Evaluate("{}", "{}");

        Assert.AreEqual(JsonataValueKind.Object, result.Kind);
        Assert.AreEqual("{}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A literal object still builds over an empty focus (the empty array): <c>{"x": 1}</c> over <c>[]</c> yields <c>{x:1}</c>, exercising the single-undefined-item seed.</summary>
    [TestMethod]
    public void ObjectConstructorBuildsLiteralOverEmptyFocus()
    {
        JsonataValue result = Evaluate("{\"x\": 1}", "[]");

        Assert.AreEqual("{\"x\":1}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// The led path-step group-by <c>path{key: value}</c> groups the source path's result rather than the
    /// focus: <c>data{t: v}</c> over an object whose <c>data</c> is an array buckets the two <c>"a"</c> items
    /// (whose <c>v</c> become the sequence <c>[1,2]</c>) and the single <c>"b"</c> item (whose <c>v</c> is
    /// <c>3</c>), in first-seen key order — the same group-by the prefix form runs, fed the path result.
    /// </summary>
    [TestMethod]
    public void LedObjectGroupGroupsPathResultByKey()
    {
        JsonataValue result = Evaluate("data{t: v}", "{ \"data\": [ { \"t\": \"a\", \"v\": 1 }, { \"t\": \"a\", \"v\": 2 }, { \"t\": \"b\", \"v\": 3 } ] }");

        Assert.AreEqual("{\"a\":[1,2],\"b\":3}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A led path-step group-by over a single-item path result builds one object (no grouping): <c>data{t: v}</c> over a one-element <c>data</c> yields a single member.</summary>
    [TestMethod]
    public void LedObjectGroupOverSingleItemBuildsOneMember()
    {
        JsonataValue result = Evaluate("data{t: v}", "{ \"data\": [ { \"t\": \"a\", \"v\": 1 } ] }");

        Assert.AreEqual("{\"a\":1}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A led path-step group-by binds to the whole preceding path: <c>a.b{t: v}</c> groups the result of the map <c>a.b</c>, confirming the source is the multi-step path, not just the last step.</summary>
    [TestMethod]
    public void LedObjectGroupBindsWholePrecedingPath()
    {
        JsonataValue result = Evaluate("a.b{t: v}", "{ \"a\": { \"b\": [ { \"t\": \"x\", \"v\": 10 }, { \"t\": \"x\", \"v\": 20 } ] } }");

        Assert.AreEqual("{\"x\":[10,20]}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A led path-step group-by with a computed <c>$string(...)</c> key coerces the numeric field to its string form as the group key: <c>data{$string(id): v}</c>.</summary>
    [TestMethod]
    public void LedObjectGroupComputedStringKeyGroupsByCoercedKey()
    {
        JsonataValue result = Evaluate("data{$string(id): v}", "{ \"data\": [ { \"id\": 7, \"v\": 1 }, { \"id\": 7, \"v\": 2 }, { \"id\": 8, \"v\": 3 } ] }");

        Assert.AreEqual("{\"7\":[1,2],\"8\":3}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A led path-step group-by with a quoted-selector key reads the named field of each item as the group key: <c>data{$."t name": v}</c>, equivalently to a backtick-quoted name.</summary>
    [TestMethod]
    public void LedObjectGroupQuotedSelectorKeyGroupsByNamedField()
    {
        JsonataValue result = Evaluate("data{$.\"t name\": v}", "{ \"data\": [ { \"t name\": \"a\", \"v\": 1 }, { \"t name\": \"b\", \"v\": 2 } ] }");

        Assert.AreEqual("{\"a\":1,\"b\":2}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A led path-step group-by whose key evaluates to a non-string throws the key-type error T1003, reusing the prefix form's key check.</summary>
    [TestMethod]
    public void LedObjectGroupNonStringKeyThrowsT1003()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("data{id: v}", "{ \"data\": [ { \"id\": 7, \"v\": 1 } ] }"));

        Assert.AreEqual("T1003", error.Code.ToString());
    }

    /// <summary>A led path-step group-by whose two member pairs collide on the same key from different pairs throws the duplicate-key error D1009, reusing the prefix form's collision check.</summary>
    [TestMethod]
    public void LedObjectGroupDuplicateKeyFromDifferentPairsThrowsD1009()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("data{t: v, t: v}", "{ \"data\": [ { \"t\": \"a\", \"v\": 1 } ] }"));

        Assert.AreEqual("D1009", error.Code.ToString());
    }

    /// <summary>A standalone object constructor over a single object builds one object, evaluating each value against the whole focus: <c>{t: v}</c> over <c>{t:"a", v:1}</c> yields <c>{"a":1}</c>.</summary>
    [TestMethod]
    public void StandaloneObjectConstructorBuildsOverSingleObject()
    {
        JsonataValue result = Evaluate("{t: v}", "{ \"t\": \"a\", \"v\": 1 }");

        Assert.AreEqual("{\"a\":1}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An object constructor whose key reads a field over a deeply-nested input array throws a catchable depth-limit error, not a stack overflow — the group-by cursor stays depth-bounded.</summary>
    [TestMethod]
    public void DeeplyNestedObjectConstructorFocusThrowsDepthLimit()
    {
        JsonataExpression tree = JsonataEngine.Parse(Encoding.UTF8.GetBytes("{x: $}")).Tree;
        JsonataValue input = BuildNestedArrays(JsonataValue.Object([new KeyValuePair<string, JsonataValue>("x", JsonataValue.String("k"))]), JsonataLimits.MaxEvaluationDepth + 64);

        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(() => JsonataEvaluator.Evaluate(tree, input));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
    }

    /// <summary>A bind in a block is its bound value: <c>($x := 5)</c> evaluates to <c>5</c>.</summary>
    [TestMethod]
    public void BindIsItsBoundValue()
    {
        JsonataValue result = Evaluate("($x := 5)", "{}");

        Assert.AreEqual(5d, result.AsNumber);
    }

    /// <summary>A single top-level bind (no parentheses) evaluates to its bound value: <c>$x := 5</c> yields <c>5</c>.</summary>
    [TestMethod]
    public void TopLevelBindYieldsValue()
    {
        JsonataValue result = Evaluate("$x := 5", "{}");

        Assert.AreEqual(5d, result.AsNumber);
    }

    /// <summary>A later block statement reads an earlier bind of the same block: <c>($x := 5; $x)</c> yields <c>5</c>.</summary>
    [TestMethod]
    public void BlockReadsEarlierBind()
    {
        JsonataValue result = Evaluate("($x := 5; $x)", "{}");

        Assert.AreEqual(5d, result.AsNumber);
    }

    /// <summary>Two binds in a block are both visible to a later statement: <c>($x := 5; $y := 6; $x + $y)</c> yields <c>11</c>.</summary>
    [TestMethod]
    public void BlockReadsMultipleBinds()
    {
        JsonataValue result = Evaluate("($x := 5; $y := 6; $x + $y)", "{}");

        Assert.AreEqual(11d, result.AsNumber);
    }

    /// <summary>A re-bind in the same frame reads the previous value: <c>($x := 1; $x := $x + 1; $x)</c> yields <c>2</c>.</summary>
    [TestMethod]
    public void BlockRebindReadsPreviousValue()
    {
        JsonataValue result = Evaluate("($x := 1; $x := $x + 1; $x)", "{}");

        Assert.AreEqual(2d, result.AsNumber);
    }

    /// <summary>The bind operator is right-associative at runtime: <c>($a := $b := 5; [$a, $b])</c> binds both to <c>5</c>.</summary>
    [TestMethod]
    public void BindChainBindsBothRightAssociatively()
    {
        JsonataValue result = Evaluate("($a := $b := 5; [$a, $b])", "{}");

        Assert.AreEqual("[5,5]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// A bind inside a nested block does not leak to the enclosing scope: in
    /// <c>($x := 1; ($x := 2); $x)</c> the inner block's re-bind is local to its own frame, so the outer
    /// <c>$x</c> remains <c>1</c>. This is the load-bearing scope-isolation guarantee.
    /// </summary>
    [TestMethod]
    public void InnerBlockBindDoesNotLeakToOuterScope()
    {
        JsonataValue result = Evaluate("($x := 1; ($x := 2); $x)", "{}");

        Assert.AreEqual(1d, result.AsNumber);
    }

    /// <summary>An inner block sees its own re-bind for its own duration: <c>($x := 1; ($x := 2; $x))</c> yields <c>2</c> (the inner block's value).</summary>
    [TestMethod]
    public void InnerBlockSeesItsOwnRebind()
    {
        JsonataValue result = Evaluate("($x := 1; ($x := 2; $x))", "{}");

        Assert.AreEqual(2d, result.AsNumber);
    }

    /// <summary>A nested block reads a binding from the enclosing block through the frame chain: <c>($x := 10; ($x + 1))</c> yields <c>11</c>.</summary>
    [TestMethod]
    public void InnerBlockReadsOuterBinding()
    {
        JsonataValue result = Evaluate("($x := 10; ($x + 1))", "{}");

        Assert.AreEqual(11d, result.AsNumber);
    }

    /// <summary>A block evaluates to its last statement's value: <c>(1; 2; 3)</c> yields <c>3</c>.</summary>
    [TestMethod]
    public void BlockYieldsLastStatement()
    {
        JsonataValue result = Evaluate("(1; 2; 3)", "{}");

        Assert.AreEqual(3d, result.AsNumber);
    }

    /// <summary>The empty block <c>()</c> yields the undefined value (serialized as no output).</summary>
    [TestMethod]
    public void EmptyBlockYieldsUndefined()
    {
        JsonataValue result = Evaluate("()", "{}");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
        Assert.AreEqual(string.Empty, JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A trailing-semicolon block <c>(1;)</c> is the one-statement block <c>(1)</c> and yields <c>1</c>.</summary>
    [TestMethod]
    public void TrailingSemicolonBlockYieldsStatement()
    {
        JsonataValue result = Evaluate("(1;)", "{}");

        Assert.AreEqual(1d, result.AsNumber);
    }

    /// <summary>
    /// Every statement of a block is evaluated against the block's own focus, not a rebound one: mapping a
    /// block over a field, <c>a.($x := b; $y := c; $x * $y)</c> over <c>{a:{b:2,c:3}}</c>, reads <c>b</c> and
    /// <c>c</c> from the same focus and yields <c>6</c>.
    /// </summary>
    [TestMethod]
    public void BlockStatementsShareTheBlockFocus()
    {
        JsonataValue result = Evaluate("a.($x := b; $y := c; $x * $y)", "{ \"a\": { \"b\": 2, \"c\": 3 } }");

        Assert.AreEqual(6d, result.AsNumber);
    }

    /// <summary>Reading an unbound variable yields the undefined value: <c>$nope</c> over any input is undefined.</summary>
    [TestMethod]
    public void UnboundVariableYieldsUndefined()
    {
        JsonataValue result = Evaluate("$nope", "{}");

        Assert.AreEqual(JsonataValueKind.Undefined, result.Kind);
    }

    /// <summary>The Elvis operator keeps a truthy left operand: <c>5 ?: 10</c> yields <c>5</c>.</summary>
    [TestMethod]
    public void ElvisKeepsTruthyLeft()
    {
        JsonataValue result = Evaluate("5 ?: 10", "{}");

        Assert.AreEqual(5d, result.AsNumber);
    }

    /// <summary>The Elvis operator falls back on a falsy left operand: <c>0 ?: 10</c> yields <c>10</c> (zero is falsy).</summary>
    [TestMethod]
    public void ElvisFallsBackOnFalsyZero()
    {
        JsonataValue result = Evaluate("0 ?: 10", "{}");

        Assert.AreEqual(10d, result.AsNumber);
    }

    /// <summary>The Elvis operator falls back on an undefined (missing-field) left operand: <c>missing ?: 10</c> yields <c>10</c>.</summary>
    [TestMethod]
    public void ElvisFallsBackOnUndefined()
    {
        JsonataValue result = Evaluate("missing ?: 10", "{ \"present\": 1 }");

        Assert.AreEqual(10d, result.AsNumber);
    }

    /// <summary>The Elvis operator falls back on an empty string: <c>'' ?: 'x'</c> yields <c>'x'</c>.</summary>
    [TestMethod]
    public void ElvisFallsBackOnEmptyString()
    {
        JsonataValue result = Evaluate("'' ?: 'x'", "{}");

        Assert.AreEqual("x", result.AsString);
    }

    /// <summary>The coalesce operator keeps a defined-but-falsy left operand: <c>0 ?? 1</c> yields <c>0</c> (only undefined falls back).</summary>
    [TestMethod]
    public void CoalesceKeepsDefinedZero()
    {
        JsonataValue result = Evaluate("0 ?? 1", "{}");

        Assert.AreEqual(0d, result.AsNumber);
    }

    /// <summary>The coalesce operator keeps a defined <c>false</c>: <c>false ?? true</c> yields <c>false</c>.</summary>
    [TestMethod]
    public void CoalesceKeepsDefinedFalse()
    {
        JsonataValue result = Evaluate("false ?? true", "{}");

        Assert.AreEqual(JsonataValueKind.Boolean, result.Kind);
        Assert.IsFalse(result.AsBoolean);
    }

    /// <summary>The coalesce operator falls back only on undefined: <c>missing ?? 42</c> yields <c>42</c>.</summary>
    [TestMethod]
    public void CoalesceFallsBackOnUndefined()
    {
        JsonataValue result = Evaluate("missing ?? 42", "{ \"present\": 1 }");

        Assert.AreEqual(42d, result.AsNumber);
    }

    /// <summary>The default operators short-circuit: a qualifying left operand never evaluates the right, so <c>5 ?: ('a' * 1)</c> yields <c>5</c> without raising the arithmetic-type error the right side would.</summary>
    [TestMethod]
    public void ElvisShortCircuitsRightOperand()
    {
        JsonataValue result = Evaluate("5 ?: ('a' * 1)", "{}");

        Assert.AreEqual(5d, result.AsNumber);
    }

    /// <summary>On fallback the right operand is evaluated: <c>0 ?: ('a' * 1)</c> evaluates the right side and raises its arithmetic-type error T2001.</summary>
    [TestMethod]
    public void ElvisEvaluatesRightOperandOnFallback()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("0 ?: ('a' * 1)", "{}"));

        Assert.AreEqual("T2001", error.Code.ToString());
    }

    /// <summary>The default operators are left-associative: <c>0 ?: 5 ?: 9</c> groups as <c>(0 ?: 5) ?: 9</c> and yields <c>5</c>.</summary>
    [TestMethod]
    public void DefaultOperatorChainsLeftToRight()
    {
        JsonataValue result = Evaluate("0 ?: 5 ?: 9", "{}");

        Assert.AreEqual(5d, result.AsNumber);
    }

    /// <summary>An immediately-invoked lambda applies its body to the argument: <c>function($x){$x+1}(5)</c> yields <c>6</c>.</summary>
    [TestMethod]
    public void ImmediatelyInvokedLambdaAppliesBody()
    {
        Assert.AreEqual(6d, Evaluate("function($x){$x+1}(5)", "{}").AsNumber);
    }

    /// <summary>The Greek <c>λ</c> alias behaves identically: <c>λ($x){$x}(5)</c> yields <c>5</c>.</summary>
    [TestMethod]
    public void LambdaShorthandApplies()
    {
        Assert.AreEqual(5d, Evaluate("λ($x){$x}(5)", "{}").AsNumber);
    }

    /// <summary>A lambda bound in a block is callable by its name: <c>($f := function($x){$x*2}; $f(5))</c> yields <c>10</c>.</summary>
    [TestMethod]
    public void BoundLambdaIsCallableByName()
    {
        Assert.AreEqual(10d, Evaluate("($f := function($x){$x*2}; $f(5))", "{}").AsNumber);
    }

    /// <summary>A two-parameter lambda binds each argument by position: <c>($add := function($a,$b){$a+$b}; $add(3,4))</c> yields <c>7</c>.</summary>
    [TestMethod]
    public void TwoParameterLambdaBindsByPosition()
    {
        Assert.AreEqual(7d, Evaluate("($add := function($a,$b){$a+$b}; $add(3,4))", "{}").AsNumber);
    }

    /// <summary>
    /// A recursive lambda resolves its own name through the captured frame it is bound into:
    /// <c>($fact := function($n){$n &lt;= 1 ? 1 : $n * $fact($n-1)}; $fact(5))</c> yields <c>120</c>.
    /// </summary>
    [TestMethod]
    public void RecursiveLambdaResolvesItsOwnName()
    {
        Assert.AreEqual(120d, Evaluate("($fact := function($n){$n <= 1 ? 1 : $n * $fact($n-1)}; $fact(5))", "{}").AsNumber);
    }

    /// <summary>
    /// A lambda closes over an outer binding through its captured frame:
    /// <c>($k := 10; $f := function($x){$x + $k}; $f(5))</c> yields <c>15</c>.
    /// </summary>
    [TestMethod]
    public void LambdaClosesOverOuterBinding()
    {
        Assert.AreEqual(15d, Evaluate("($k := 10; $f := function($x){$x + $k}; $f(5))", "{}").AsNumber);
    }

    /// <summary>
    /// Too few arguments bind the missing trailing parameters to undefined (no error):
    /// <c>($f := function($a,$b){$b}; $f(7))</c> yields undefined.
    /// </summary>
    [TestMethod]
    public void TooFewArgumentsBindMissingParameterToUndefined()
    {
        Assert.AreEqual(JsonataValueKind.Undefined, Evaluate("($f := function($a,$b){$b}; $f(7))", "{}").Kind);
    }

    /// <summary>Surplus arguments are silently ignored (no error): <c>($f := function($a){$a}; $f(1,2,3))</c> yields <c>1</c>.</summary>
    [TestMethod]
    public void SurplusArgumentsAreIgnored()
    {
        Assert.AreEqual(1d, Evaluate("($f := function($a){$a}; $f(1,2,3))", "{}").AsNumber);
    }

    /// <summary>Invoking a non-function value throws the T1006 error: <c>($x := 5; $x(3))</c>.</summary>
    [TestMethod]
    public void CallingNonFunctionThrowsT1006()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("($x := 5; $x(3))", "{}"));

        Assert.AreEqual("T1006", error.Code.ToString());
    }

    /// <summary>
    /// A higher-order call applies a lambda argument inside another lambda's body:
    /// <c>(function($f,$v){$f($v)})(function($x){$x+1}, 4)</c> yields <c>5</c>.
    /// </summary>
    [TestMethod]
    public void HigherOrderCallAppliesLambdaArgument()
    {
        Assert.AreEqual(5d, Evaluate("(function($f,$v){$f($v)})(function($x){$x+1}, 4)", "{}").AsNumber);
    }

    /// <summary>
    /// A bare <c>$</c> in a lambda body is the captured definition-time focus, not the call-site argument:
    /// over input <c>{"a":1}</c>, <c>($f := function($x){$}; $f(99))</c> yields the input object.
    /// </summary>
    [TestMethod]
    public void LambdaBodyCapturesDefinitionTimeFocus()
    {
        JsonataValue result = Evaluate("($f := function($x){$}; $f(99))", "{ \"a\": 1 }");

        Assert.AreEqual("{\"a\":1}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// The captured focus is the DEFINITION-time focus, not the call-site focus: a lambda defined inside a
    /// map step over <c>a</c> captures the value of <c>a</c>, so calling it at the (root) call site still
    /// makes its body's <c>$</c> the captured value. Over <c>{"a":{"x":7}}</c>,
    /// <c>($f := a.(function(){$}); $f())</c> yields <c>{"x":7}</c> (the definition focus), not the root.
    /// </summary>
    [TestMethod]
    public void LambdaCapturesDefinitionFocusNotCallSiteFocus()
    {
        JsonataValue result = Evaluate("($f := a.(function(){$}); $f())", "{ \"a\": { \"x\": 7 } }");

        Assert.AreEqual("{\"x\":7}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>
    /// The chain operator's apply case pipes the left value into an inline lambda as its single argument:
    /// <c>(5 ~&gt; function($x){$x+1})</c> yields <c>6</c>.
    /// </summary>
    [TestMethod]
    public void ChainAppliesInlineLambdaToLeftValue()
    {
        Assert.AreEqual(6d, Evaluate("(5 ~> function($x){$x+1})", "{}").AsNumber);
    }

    /// <summary>
    /// The chain operator's apply case pipes the left value into a bound function:
    /// <c>($double := function($x){$x*2}; 5 ~&gt; $double)</c> yields <c>10</c>.
    /// </summary>
    [TestMethod]
    public void ChainAppliesBoundFunctionToLeftValue()
    {
        Assert.AreEqual(10d, Evaluate("($double := function($x){$x*2}; 5 ~> $double)", "{}").AsNumber);
    }

    /// <summary>
    /// The chain operator's call-prepend case makes the left value the FIRST argument of the right-hand call:
    /// <c>(3 ~&gt; function($x,$y){$x - $y}(1))</c> yields <c>2</c> (the call is <c>$f(3, 1)</c>, so
    /// <c>3 - 1</c>).
    /// </summary>
    [TestMethod]
    public void ChainPrependsLeftAsFirstCallArgument()
    {
        Assert.AreEqual(2d, Evaluate("(3 ~> function($x,$y){$x - $y}(1))", "{}").AsNumber);
    }

    /// <summary>
    /// The chain operator's compose case builds a function applied left-then-right and invoked by name:
    /// <c>($f := function($x){$x+1}; $g := function($x){$x*2}; $h := $f ~&gt; $g; $h(3))</c> yields <c>8</c>
    /// (<c>$g($f(3))</c> = <c>$g(4)</c> = <c>8</c>).
    /// </summary>
    [TestMethod]
    public void ChainComposesFunctionsAppliedLeftThenRight()
    {
        Assert.AreEqual(8d, Evaluate("($f := function($x){$x+1}; $g := function($x){$x*2}; $h := $f ~> $g; $h(3))", "{}").AsNumber);
    }

    /// <summary>
    /// The composed function value is callable in place: an inline composition immediately invoked,
    /// <c>(($f := function($x){$x+1}; $g := function($x){$x*2}; $f ~&gt; $g))(3)</c>, yields <c>8</c>.
    /// </summary>
    [TestMethod]
    public void InlineComposedFunctionIsCallableInPlace()
    {
        Assert.AreEqual(8d, Evaluate("(($f := function($x){$x+1}; $g := function($x){$x*2}; $f ~> $g))(3)", "{}").AsNumber);
    }

    /// <summary>
    /// The chain operator pipes left-to-right across a multi-step chain:
    /// <c>(10 ~&gt; function($x){$x+1} ~&gt; function($x){$x*2})</c> yields <c>22</c> (<c>(10+1)*2</c>),
    /// confirming the apply case threads the running value through each step in order.
    /// </summary>
    [TestMethod]
    public void ChainPipesLeftToRightAcrossSteps()
    {
        Assert.AreEqual(22d, Evaluate("(10 ~> function($x){$x+1} ~> function($x){$x*2})", "{}").AsNumber);
    }

    /// <summary>
    /// Composition order is first-then-second, not the reverse: <c>$f</c> subtracts and <c>$g</c> doubles, so
    /// <c>($f := function($x){$x-1}; $g := function($x){$x*2}; ($f ~&gt; $g)(5))</c> yields <c>8</c>
    /// (<c>$g($f(5))</c> = <c>$g(4)</c> = <c>8</c>), whereas the reverse <c>$f($g(5))</c> would be <c>9</c>.
    /// </summary>
    [TestMethod]
    public void ChainComposesInFirstThenSecondOrder()
    {
        Assert.AreEqual(8d, Evaluate("($f := function($x){$x-1}; $g := function($x){$x*2}; ($f ~> $g)(5))", "{}").AsNumber);
    }

    /// <summary>
    /// The right side of the chain operator must be a function: <c>(5 ~&gt; 3)</c> throws the dedicated T2006
    /// error (distinct from the call operator's T1006).
    /// </summary>
    [TestMethod]
    public void ChainToNonFunctionRightThrowsT2006()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("(5 ~> 3)", "{}"));

        Assert.AreEqual("T2006", error.Code.ToString());
    }

    /// <summary>
    /// The non-function-right check fires before the apply-vs-compose decision, so even a FUNCTION left with a
    /// non-function right throws T2006 (it does not silently build a composition):
    /// <c>($f := function($x){$x+1}; $f ~&gt; 3)</c>.
    /// </summary>
    [TestMethod]
    public void ChainComposeWithNonFunctionRightThrowsT2006()
    {
        JsonataErrorException error = Assert.ThrowsExactly<JsonataErrorException>(() => Evaluate("($f := function($x){$x+1}; $f ~> 3)", "{}"));

        Assert.AreEqual("T2006", error.Code.ToString());
    }

    /// <summary>
    /// The canonical zero-argument call-prepend form passes the left value as the function's sole argument:
    /// <c>($double := function($x){$x*2}; 5 ~&gt; $double())</c> yields <c>10</c>.
    /// </summary>
    [TestMethod]
    public void ChainZeroArgCallPrependsLeftAsSoleArgument()
    {
        Assert.AreEqual(10d, Evaluate("($double := function($x){$x*2}; 5 ~> $double())", "{}").AsNumber);
    }

    /// <summary>
    /// A deeply tail-recursive lambda runs to completion: the tail call leaves no pending work frame, so the
    /// recursion runs in constant work-stack depth (as iteration) and is bounded only by the step budget, far
    /// past the work-stack depth limit.
    /// </summary>
    [TestMethod]
    public void DeeplyTailRecursiveLambdaCompletes()
    {
        JsonataValue result = Evaluate("($sum := function($n, $acc){$n = 0 ? $acc : $sum($n - 1, $acc + $n)}; $sum(1000, 0))", "{}");

        Assert.AreEqual(500500d, result.AsNumber);
    }

    /// <summary>
    /// A deeply non-tail-recursive lambda is bounded: the pending operator after each call keeps a work frame
    /// resident, so the work stack grows with depth and the recursion throws a catchable
    /// <see cref="JsonataEvaluationLimitException"/> at the work-stack depth limit rather than overflowing the
    /// process stack, carrying the JSONata non-terminating-recursion code (<c>U1001</c>).
    /// </summary>
    [TestMethod]
    public void DeeplyNonTailRecursiveLambdaThrowsDepthLimit()
    {
        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(
            () => Evaluate("($sum := function($n){$n = 0 ? 0 : $n + $sum($n - 1)}; $sum(100000))", "{}"));

        Assert.AreEqual(JsonataLimit.EvaluationDepth, error.Limit);
        Assert.IsTrue(WellKnownJsonataErrors.IsNonTerminatingRecursion(error.Code));
    }

    /// <summary>
    /// A runaway tail recursion terminates on the step budget: with the work stack flat the depth limit never
    /// applies, so a tail-recursive expression that would take more than the maximum number of steps throws a
    /// catchable <see cref="JsonataEvaluationLimitException"/> for the step limit, not the depth limit, and
    /// carries the JSONata non-terminating-recursion code (<c>U1001</c>).
    /// </summary>
    [TestMethod]
    public void RunawayTailRecursionThrowsStepLimit()
    {
        JsonataEvaluationLimitException error = Assert.ThrowsExactly<JsonataEvaluationLimitException>(
            () => Evaluate("($count := function($n){$n = 0 ? 0 : $count($n - 1)}; $count(100000))", "{}"));

        Assert.AreEqual(JsonataLimit.EvaluationSteps, error.Limit);
        Assert.IsTrue(WellKnownJsonataErrors.IsNonTerminatingRecursion(error.Code));
    }

    /// <summary>A double-quoted string as a map step names a field (a quoted selector), navigating like a backtick name.</summary>
    [TestMethod]
    public void DoubleQuotedStringIsAFieldSelector()
    {
        Assert.AreEqual(5, Evaluate("$.\"a b\"", "{\"a b\": 5}").AsNumber);
    }

    /// <summary>A single-quoted string as a map step also names a field.</summary>
    [TestMethod]
    public void SingleQuotedStringIsAFieldSelector()
    {
        Assert.AreEqual("ok", Evaluate("$.'single'", "{\"single\": \"ok\"}").AsString);
    }

    /// <summary>A quoted field name may contain a literal dot, which is part of the name rather than a further step.</summary>
    [TestMethod]
    public void QuotedFieldNameWithDotIsLiteral()
    {
        Assert.AreEqual("here", Evaluate("foo.\"blah.baz\"", "{\"foo\": {\"blah.baz\": \"here\"}}").AsString);
    }

    /// <summary>A leading quoted string (the left operand of a path) names a field on the input.</summary>
    [TestMethod]
    public void LeadingQuotedStringNavigatesField()
    {
        Assert.AreEqual(9, Evaluate("\"a\".\"b\"", "{\"a\": {\"b\": 9}}").AsNumber);
    }

    /// <summary>The keyword <c>and</c> in operand position names a field, while in infix position it stays the and operator.</summary>
    [TestMethod]
    public void KeywordAndIsAFieldNameInOperandPosition()
    {
        Assert.IsTrue(Evaluate("and=1 and or=2", "{\"and\": 1, \"or\": 2}").AsBoolean);
    }

    /// <summary>The keyword <c>in</c> in operand position names a field, with the following <c>in</c> the inclusion operator.</summary>
    [TestMethod]
    public void KeywordInIsAFieldNameInOperandPosition()
    {
        Assert.IsTrue(Evaluate("in in [\"x\", \"y\"]", "{\"in\": \"x\"}").AsBoolean);
    }

    /// <summary>A positional index bind <c>#$pos</c> binds the step-time position, so a later predicate over <c>$pos</c> keeps the leading items by their original ordinal.</summary>
    [TestMethod]
    public void PositionalIndexBindFiltersByBoundPosition()
    {
        JsonataValue result = Evaluate("$#$pos[$pos<3]", "[3,1,4,1,5,9]");

        Assert.AreEqual("[3,1,4]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A numeric-literal predicate over a tuple stream selects the single tuple at that position, projected back to its focus.</summary>
    [TestMethod]
    public void PositionalIndexBindNumericLiteralSelectsPosition()
    {
        JsonataValue result = Evaluate("$#$pos[$pos<3][1]", "[3,1,4,1,5,9]");

        Assert.AreEqual(JsonataValueKind.Number, result.Kind);
        Assert.AreEqual(1d, result.AsNumber);
    }

    /// <summary>A trailing <c>[]</c> keep-array marker on a positional bind keeps a single selected value an array.</summary>
    [TestMethod]
    public void PositionalIndexBindKeepArrayKeepsSingletonArray()
    {
        JsonataValue result = Evaluate("$#$pos[$pos<3][1][]", "[3,1,4,1,5,9]");

        Assert.AreEqual("[1]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An absolute path whose first step is <c>$</c> wraps the whole array as one item before the tuple bind, so the positional bind numbers the single element once and the predicate keeps the whole array.</summary>
    [TestMethod]
    public void AbsolutePathFirstVariableStepWrapsInputAsOneItem()
    {
        JsonataValue result = Evaluate("$.$#$pos[$pos<3]", "[3,1,4,1,5,9]");

        Assert.AreEqual("[3,1,4,1,5,9]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>Two <c>@</c> focus binds build the cartesian product of the two child collections; the cross-binding predicate keeps the matching pairs, and a trailing per-tuple object constructor projects each surviving pair.</summary>
    [TestMethod]
    public void TwoFocusBindsJoinWithCrossBindingPredicate()
    {
        string input = "{ \"loans\": [ { \"k\": 1, \"who\": \"a\" }, { \"k\": 2, \"who\": \"b\" } ], \"books\": [ { \"k\": 1, \"t\": \"x\" }, { \"k\": 2, \"t\": \"y\" } ] }";
        JsonataValue result = Evaluate("loans@$l.books@$b[$l.k=$b.k].{ 'title': $b.t, 'who': $l.who }", input);

        Assert.AreEqual("[{\"title\":\"x\",\"who\":\"a\"},{\"title\":\"y\",\"who\":\"b\"}]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>An index bind <c>#$o</c> remains in scope through a later tuple step and is readable in the trailing per-tuple object constructor.</summary>
    [TestMethod]
    public void IndexBindStaysVisibleThroughLaterStep()
    {
        string input = "{ \"orders\": [ { \"items\": [ \"p\", \"q\" ] }, { \"items\": [ \"r\" ] } ] }";
        JsonataValue result = Evaluate("orders#$o.items.{ 'item': $, 'order': $o }", input);

        Assert.AreEqual("[{\"item\":\"p\",\"order\":0},{\"item\":\"q\",\"order\":0},{\"item\":\"r\",\"order\":1}]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The parent operator <c>%</c> inside a predicate reads the captured ancestor focus, so a child step filters by a parent field.</summary>
    [TestMethod]
    public void ParentOperatorInPredicateReadsAncestorFocus()
    {
        string input = "{ \"orders\": [ { \"id\": \"o1\", \"items\": [ { \"sku\": \"a\" } ] }, { \"id\": \"o2\", \"items\": [ { \"sku\": \"b\" }, { \"sku\": \"c\" } ] } ] }";
        JsonataValue result = Evaluate("orders.items[%.id='o2'].sku", input);

        Assert.AreEqual("[\"b\",\"c\"]", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>The parent operator <c>%</c> inside a per-tuple object constructor reads the captured ancestor focus.</summary>
    [TestMethod]
    public void ParentOperatorInConstructorReadsAncestorField()
    {
        string input = "{ \"orders\": [ { \"id\": \"o1\", \"items\": [ { \"name\": \"hat\" } ] } ] }";
        JsonataValue result = Evaluate("orders.items.{ 'name': name, 'order': %.id }", input);

        //A single-item path result unwraps to the bare value (the core JSONata singleton-sequence rule):
        //one order with one item yields one constructed object, not a one-element array.
        Assert.AreEqual("{\"name\":\"hat\",\"order\":\"o1\"}", JsonataEngine.SerializeToJson(result).ToString());
    }

    /// <summary>A grandparent <c>%.%</c> walks two structural levels up to read the ancestor's ancestor field.</summary>
    [TestMethod]
    public void GrandparentOperatorReadsTwoLevelsUp()
    {
        string input = "{ \"account\": \"acc\", \"orders\": [ { \"id\": \"o1\", \"items\": [ { \"sku\": \"a\" } ] } ] }";
        JsonataValue result = Evaluate("orders.items[%.%.account='acc'].sku", input);

        Assert.AreEqual("a", result.AsString);
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

    /// <summary>Wraps a value in the given number of nested singleton arrays (the truthiness singleton spine).</summary>
    /// <param name="leaf">The innermost value.</param>
    /// <param name="depth">The number of singleton levels to wrap.</param>
    /// <returns>The nested-singleton value.</returns>
    private static JsonataValue BuildNestedSingletons(JsonataValue leaf, int depth)
    {
        return BuildNestedArrays(leaf, depth);
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
}
