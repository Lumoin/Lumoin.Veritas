using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Json;
using Lumoin.Veritas.Json.Stj;
using Lumoin.Veritas.Jsonata;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Lexer;
using Lumoin.Veritas.Jsonata.Parser;
using Lumoin.Veritas.Jsonata.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JsonataEngine = Lumoin.Veritas.Jsonata.Jsonata;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Parser tests for <see cref="JsonataParser"/>: literals, names, variables, the map and predicate path
/// operators, arithmetic / concatenation / comparison / boolean operators, unary negation, the
/// conditional (with and without an else branch), grouping, the wildcard <c>*</c>, the descendant
/// <c>**</c>, the range <c>..</c>, the array constructor <c>[ ... ]</c>, the object constructor
/// <c>{ ... }</c> (including its missing-colon and missing-closer recovery), the lambda
/// <c>function(...){...}</c> / <c>λ(...){...}</c> and function application <c>f(...)</c> (parameter and
/// argument node shapes, the zero-parameter and no-argument forms, the immediately-invoked lambda, and the
/// non-variable-parameter diagnostic), the function-application / chain operator <c>~&gt;</c> (its node
/// shape, left-associativity, and the call-prepend right-operand shape), precedence and associativity, the
/// recovery of the still-deferred parent <c>%</c>, the led path-step group-by <c>path{ ... }</c> (the
/// <c>{</c> after an operand binding the preceding path as the grouping source), the resumable token-feed
/// path, and the <see cref="JsonataExpressionWalker"/> traversal and rewrite over the variadic array and
/// object constructors.
/// </summary>
[TestClass]
internal sealed class JsonataParserTests
{
    /// <summary>Gets or sets the ambient test context supplied by the framework.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A bare integer parses to a number literal carrying its lexeme.</summary>
    [TestMethod]
    public void ParsesNumberLiteral()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("42", pool);

        LiteralExpression literal = AssertNode<LiteralExpression>(tree);
        Assert.AreEqual(JsonataLiteralKind.Number, literal.Kind);
        Assert.AreEqual("42", literal.Value.ToString());
    }

    /// <summary>A double-quoted string parses to a string literal with its decoded contents.</summary>
    [TestMethod]
    public void ParsesStringLiteral()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("\"hi\"", pool);

        LiteralExpression literal = AssertNode<LiteralExpression>(tree);
        Assert.AreEqual(JsonataLiteralKind.String, literal.Kind);
        Assert.AreEqual("hi", literal.Value.ToString());
    }

    /// <summary>The reserved words <c>true</c>, <c>false</c>, and <c>null</c> parse to literals, not field references.</summary>
    [TestMethod]
    public void ParsesBooleanAndNullLiterals()
    {
        using Utf8StringPool pool = new();

        Assert.AreEqual(JsonataLiteralKind.Boolean, AssertNode<LiteralExpression>(Parse("true", pool)).Kind);
        Assert.AreEqual(JsonataLiteralKind.Boolean, AssertNode<LiteralExpression>(Parse("false", pool)).Kind);
        Assert.AreEqual(JsonataLiteralKind.Null, AssertNode<LiteralExpression>(Parse("null", pool)).Kind);
    }

    /// <summary>A bare identifier parses to a field reference.</summary>
    [TestMethod]
    public void ParsesBareName()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("price", pool);

        Assert.AreEqual("price", AssertNode<NameExpression>(tree).Name.ToString());
    }

    /// <summary>A backtick-quoted identifier parses to a field reference with its backticks stripped.</summary>
    [TestMethod]
    public void ParsesBacktickName()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("`Product Name`", pool);

        Assert.AreEqual("Product Name", AssertNode<NameExpression>(tree).Name.ToString());
    }

    /// <summary>The bare context focus <c>$</c>, the root <c>$$</c>, and a named <c>$x</c> parse to the three variable forms.</summary>
    [TestMethod]
    public void ParsesVariableForms()
    {
        using Utf8StringPool pool = new();

        Assert.AreEqual(VariableForm.ContextFocus, AssertNode<VariableExpression>(Parse("$", pool)).Form);
        Assert.AreEqual(VariableForm.Root, AssertNode<VariableExpression>(Parse("$$", pool)).Form);

        VariableExpression named = AssertNode<VariableExpression>(Parse("$x", pool));
        Assert.AreEqual(VariableForm.Named, named.Form);
        Assert.AreEqual("x", named.Name.ToString());
    }

    /// <summary>
    /// The filter binds tighter than the map and the map is left-associative: <c>a.b[c=1].d</c> groups as
    /// <c>(a.(b[c=1])).d</c>.
    /// </summary>
    [TestMethod]
    public void FilterBindsTighterThanMap()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a.b[c=1].d", pool);

        //(a . (b[c=1])) . d: the top map's step is the field 'd'; its source is the inner map.
        MapExpression outer = AssertNode<MapExpression>(tree);
        Assert.AreEqual("d", AssertNode<NameExpression>(outer.Step).Name.ToString());

        MapExpression inner = AssertNode<MapExpression>(outer.Source);

        //The inner map's source is 'a'; its step is the predicate b[c=1].
        Assert.AreEqual("a", AssertNode<NameExpression>(inner.Source).Name.ToString());

        PredicateExpression predicate = AssertNode<PredicateExpression>(inner.Step);
        Assert.AreEqual("b", AssertNode<NameExpression>(predicate.Source).Name.ToString());
        Assert.AreEqual(BinaryOperator.Equal, AssertNode<BinaryExpression>(predicate.Filter).Operator);
    }

    /// <summary>The map operator is left-associative: <c>a.b.c</c> groups as <c>(a.b).c</c>.</summary>
    [TestMethod]
    public void MapIsLeftAssociative()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a.b.c", pool);

        //(a . b) . c: the top map's step is 'c'; its source is the inner map (a . b).
        MapExpression outer = AssertNode<MapExpression>(tree);
        Assert.AreEqual("c", AssertNode<NameExpression>(outer.Step).Name.ToString());

        MapExpression inner = AssertNode<MapExpression>(outer.Source);
        Assert.AreEqual("a", AssertNode<NameExpression>(inner.Source).Name.ToString());
        Assert.AreEqual("b", AssertNode<NameExpression>(inner.Step).Name.ToString());
    }

    /// <summary>Comparison binds tighter than the boolean operators: <c>x = 1 and y &gt; 2</c> groups as <c>(x=1) and (y&gt;2)</c>.</summary>
    [TestMethod]
    public void ComparisonBindsTighterThanBoolean()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("x = 1 and y > 2", pool);

        BinaryExpression and = AssertNode<BinaryExpression>(tree);
        Assert.AreEqual(BinaryOperator.And, and.Operator);
        Assert.AreEqual(BinaryOperator.Equal, AssertNode<BinaryExpression>(and.Left).Operator);
        Assert.AreEqual(BinaryOperator.Greater, AssertNode<BinaryExpression>(and.Right).Operator);
    }

    /// <summary>A block overrides precedence: <c>(a + b) * c</c> multiplies the one-statement block sum.</summary>
    [TestMethod]
    public void GroupingOverridesPrecedence()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("(a + b) * c", pool);

        BinaryExpression multiply = AssertNode<BinaryExpression>(tree);
        Assert.AreEqual(BinaryOperator.Multiply, multiply.Operator);

        BlockExpression block = AssertNode<BlockExpression>(multiply.Left);
        Assert.HasCount(1, block.Statements);
        Assert.AreEqual(BinaryOperator.Add, AssertNode<BinaryExpression>(block.Statements[0]).Operator);
        Assert.AreEqual("c", AssertNode<NameExpression>(multiply.Right).Name.ToString());
    }

    /// <summary>Binary minus and unary negate compose: <c>a - -b</c> subtracts a negated operand.</summary>
    [TestMethod]
    public void BinaryMinusThenUnaryNegate()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a - -b", pool);

        BinaryExpression subtract = AssertNode<BinaryExpression>(tree);
        Assert.AreEqual(BinaryOperator.Subtract, subtract.Operator);
        Assert.AreEqual("a", AssertNode<NameExpression>(subtract.Left).Name.ToString());

        UnaryExpression negate = AssertNode<UnaryExpression>(subtract.Right);
        Assert.AreEqual(UnaryOperator.Negate, negate.Operator);
        Assert.AreEqual("b", AssertNode<NameExpression>(negate.Operand).Name.ToString());
    }

    /// <summary>Subtraction is left-associative: <c>1 - 2 - 3</c> groups as <c>(1 - 2) - 3</c>.</summary>
    [TestMethod]
    public void SubtractionIsLeftAssociative()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("1 - 2 - 3", pool);

        BinaryExpression outer = AssertNode<BinaryExpression>(tree);
        Assert.AreEqual(BinaryOperator.Subtract, outer.Operator);

        //The left side is the nested (1 - 2); the right side is the bare 3.
        BinaryExpression inner = AssertNode<BinaryExpression>(outer.Left);
        Assert.AreEqual(BinaryOperator.Subtract, inner.Operator);
        Assert.AreEqual("1", AssertNode<LiteralExpression>(inner.Left).Value.ToString());
        Assert.AreEqual("2", AssertNode<LiteralExpression>(inner.Right).Value.ToString());
        Assert.AreEqual("3", AssertNode<LiteralExpression>(outer.Right).Value.ToString());
    }

    /// <summary>The no-else conditional <c>1 ? 2</c> parses with an absent false branch.</summary>
    [TestMethod]
    public void ParsesConditionalWithoutElse()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("1 ? 2", pool);

        ConditionalExpression conditional = AssertNode<ConditionalExpression>(tree);
        Assert.AreEqual("1", AssertNode<LiteralExpression>(conditional.Condition).Value.ToString());
        Assert.AreEqual("2", AssertNode<LiteralExpression>(conditional.WhenTrue).Value.ToString());
        Assert.IsNull(conditional.WhenFalse);
    }

    /// <summary>The full conditional <c>1 ? 2 : 3</c> parses all three branches.</summary>
    [TestMethod]
    public void ParsesConditionalWithElse()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("1 ? 2 : 3", pool);

        ConditionalExpression conditional = AssertNode<ConditionalExpression>(tree);
        Assert.AreEqual("1", AssertNode<LiteralExpression>(conditional.Condition).Value.ToString());
        Assert.AreEqual("2", AssertNode<LiteralExpression>(conditional.WhenTrue).Value.ToString());
        Assert.IsNotNull(conditional.WhenFalse);
        Assert.AreEqual("3", AssertNode<LiteralExpression>(conditional.WhenFalse!).Value.ToString());
    }

    /// <summary>A path, predicate, variable, and literal mix parses into a single nested tree.</summary>
    [TestMethod]
    public void ParsesPathPredicateVariableLiteralMix()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("items[price > $threshold].name", pool);

        //(items[price > $threshold]) . name.
        MapExpression map = AssertNode<MapExpression>(tree);
        Assert.AreEqual("name", AssertNode<NameExpression>(map.Step).Name.ToString());

        PredicateExpression predicate = AssertNode<PredicateExpression>(map.Source);
        Assert.AreEqual("items", AssertNode<NameExpression>(predicate.Source).Name.ToString());

        BinaryExpression comparison = AssertNode<BinaryExpression>(predicate.Filter);
        Assert.AreEqual(BinaryOperator.Greater, comparison.Operator);
        Assert.AreEqual("price", AssertNode<NameExpression>(comparison.Left).Name.ToString());

        VariableExpression variable = AssertNode<VariableExpression>(comparison.Right);
        Assert.AreEqual(VariableForm.Named, variable.Form);
        Assert.AreEqual("threshold", variable.Name.ToString());
    }

    /// <summary>String concatenation parses to a <see cref="BinaryOperator.Concat"/> node.</summary>
    [TestMethod]
    public void ParsesStringConcatenation()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("first & last", pool);

        Assert.AreEqual(BinaryOperator.Concat, AssertNode<BinaryExpression>(tree).Operator);
    }

    /// <summary>An object constructor <c>{"a":1,"b":2}</c> parses to an <see cref="ObjectConstructorExpression"/> carrying its two key/value member pairs in source order.</summary>
    [TestMethod]
    public void ObjectConstructorParsesToConstructorNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("{\"a\":1,\"b\":2}", pool);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(tree);
        Assert.HasCount(2, obj.Members);
        Assert.AreEqual("a", AssertNode<LiteralExpression>(obj.Members[0].Key).Value.ToString());
        Assert.AreEqual("1", AssertNode<LiteralExpression>(obj.Members[0].Value).Value.ToString());
        Assert.AreEqual("b", AssertNode<LiteralExpression>(obj.Members[1].Key).Value.ToString());
        Assert.AreEqual("2", AssertNode<LiteralExpression>(obj.Members[1].Value).Value.ToString());
    }

    /// <summary>The empty object constructor <c>{}</c> parses to an <see cref="ObjectConstructorExpression"/> with no members.</summary>
    [TestMethod]
    public void EmptyObjectConstructorParsesToZeroMemberNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("{}", pool);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(tree);
        Assert.IsEmpty(obj.Members);
    }

    /// <summary>An object key whose value is a path expression parses with that path as the member value.</summary>
    [TestMethod]
    public void ObjectConstructorParsesPathValuedMember()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("{\"n\": $.name}", pool);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(tree);
        Assert.HasCount(1, obj.Members);
        Assert.AreEqual("n", AssertNode<LiteralExpression>(obj.Members[0].Key).Value.ToString());
        AssertNode<MapExpression>(obj.Members[0].Value);
    }

    /// <summary>
    /// An object constructor missing the <c>:</c> between a key and its value <c>{"a" 1}</c> records a
    /// JS0001 diagnostic yet still yields an <see cref="ObjectConstructorExpression"/> whose single member
    /// retains the pending key paired with an <see cref="ErrorExpression"/> placeholder value (the partial
    /// member is kept, not discarded), with the unparsed run resynced so no cascading diagnostic follows.
    /// </summary>
    [TestMethod]
    public void ObjectConstructorMissingColonReportsAndKeepsNode()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("{\"a\" 1}"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.ExpectedExpression));

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(result.Tree);
        Assert.HasCount(1, obj.Members);
        Assert.AreEqual("a", AssertNode<LiteralExpression>(obj.Members[0].Key).Value.ToString());
        AssertNode<ErrorExpression>(obj.Members[0].Value);
    }

    /// <summary>
    /// An unterminated object constructor <c>{"a":1</c> records a JS0004 missing-closer diagnostic yet still
    /// yields an <see cref="ObjectConstructorExpression"/> carrying the members parsed so far (the partial
    /// node is kept, not discarded into an error node).
    /// </summary>
    [TestMethod]
    public void UnterminatedObjectConstructorReportsMissingCloserAndKeepsNode()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("{\"a\":1"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.MissingCloser));

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(result.Tree);
        Assert.HasCount(1, obj.Members);
    }

    /// <summary>
    /// The led path-step group-by <c>a{"k":v}</c> (a <c>{</c> following an operand) parses to an
    /// <see cref="ObjectConstructorExpression"/> whose <see cref="ObjectConstructorExpression.Source"/> is
    /// the preceding operand <c>a</c> and whose members are parsed by the same machinery as the prefix form;
    /// no diagnostic is reported.
    /// </summary>
    [TestMethod]
    public void LedGroupBySugarBindsSourceAndMembers()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("a{\"k\":v}"), pool);

        Assert.IsFalse(result.HasErrors);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(result.Tree);
        Assert.IsNotNull(obj.Source);
        Assert.AreEqual("a", AssertNode<NameExpression>(obj.Source!).Name.ToString());
        Assert.HasCount(1, obj.Members);
        Assert.AreEqual("k", AssertNode<LiteralExpression>(obj.Members[0].Key).Value.ToString());
        AssertNode<NameExpression>(obj.Members[0].Value);
    }

    /// <summary>A predicate following a grouping step is invalid (the reference's S0209): <c>[1,2,3]{"n":$}[true]</c> reports the grouping-step diagnostic.</summary>
    [TestMethod]
    public void PredicateAfterGroupingStepIsAnError()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("[1,2,3]{\"n\":$}[true]"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.InvalidGroupingStep));
    }

    /// <summary>A second grouping in one step is invalid (the reference's S0210): <c>[1,2,3]{"n":$}{"n":$}</c> reports the grouping-step diagnostic.</summary>
    [TestMethod]
    public void SecondGroupingInStepIsAnError()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("[1,2,3]{\"n\":$}{\"n\":$}"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.InvalidGroupingStep));
    }

    /// <summary>
    /// A led path-step group-by binds to the whole preceding path: <c>a.b{"k":v}</c> parses to an
    /// <see cref="ObjectConstructorExpression"/> whose source is the map <c>a.b</c> (the <c>{</c> at binding
    /// power 70 binds looser than the map <c>.</c>), confirming the led form reuses the shared member parsing
    /// over a multi-step source.
    /// </summary>
    [TestMethod]
    public void LedGroupByBindsWholePrecedingPath()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a.b{\"k\":v}", pool);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(tree);
        Assert.IsNotNull(obj.Source);
        AssertNode<MapExpression>(obj.Source!);
        Assert.HasCount(1, obj.Members);
    }

    /// <summary>An array constructor <c>[1,2,3]</c> parses to an <see cref="ArrayConstructorExpression"/> carrying its three element expressions in source order.</summary>
    [TestMethod]
    public void ArrayConstructorParsesToConstructorNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("[1,2,3]", pool);

        ArrayConstructorExpression array = AssertNode<ArrayConstructorExpression>(tree);
        Assert.HasCount(3, array.Elements);
        Assert.AreEqual("1", AssertNode<LiteralExpression>(array.Elements[0]).Value.ToString());
        Assert.AreEqual("2", AssertNode<LiteralExpression>(array.Elements[1]).Value.ToString());
        Assert.AreEqual("3", AssertNode<LiteralExpression>(array.Elements[2]).Value.ToString());
    }

    /// <summary>The empty array constructor <c>[]</c> parses to an <see cref="ArrayConstructorExpression"/> with no elements.</summary>
    [TestMethod]
    public void EmptyArrayConstructorParsesToZeroElementNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("[]", pool);

        ArrayConstructorExpression array = AssertNode<ArrayConstructorExpression>(tree);
        Assert.IsEmpty(array.Elements);
    }

    /// <summary>
    /// A field name on the left of <c>:=</c> is not a valid bind target: it reports the JS0006 bind-left
    /// diagnostic and keeps the right operand as the recovered node (no error node) so the parse continues.
    /// </summary>
    [TestMethod]
    public void BindFieldNameLeftReportsDiagnostic()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("a := 1"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.BindLeftNotVariable));
        Assert.IsFalse(ContainsError(result.Tree));
        Assert.AreEqual(JsonataLiteralKind.Number, AssertNode<LiteralExpression>(result.Tree).Kind);
    }

    /// <summary>
    /// The function-application / chain operator <c>a ~&gt; b</c> parses to an <see cref="ApplyExpression"/>
    /// over its two operands, with no diagnostics.
    /// </summary>
    [TestMethod]
    public void ChainParsesToApplyNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a ~> b", pool);

        ApplyExpression apply = AssertNode<ApplyExpression>(tree);
        Assert.AreEqual("a", AssertNode<NameExpression>(apply.Left).Name.ToString());
        Assert.AreEqual("b", AssertNode<NameExpression>(apply.Right).Name.ToString());
    }

    /// <summary>
    /// The chain operator is left-associative: <c>a ~&gt; b ~&gt; c</c> groups as <c>(a ~&gt; b) ~&gt; c</c>,
    /// so the top node's right is the bare <c>c</c> and its left is the inner chain over <c>a</c> and
    /// <c>b</c>.
    /// </summary>
    [TestMethod]
    public void ChainIsLeftAssociative()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a ~> b ~> c", pool);

        ApplyExpression outer = AssertNode<ApplyExpression>(tree);
        Assert.AreEqual("c", AssertNode<NameExpression>(outer.Right).Name.ToString());

        ApplyExpression inner = AssertNode<ApplyExpression>(outer.Left);
        Assert.AreEqual("a", AssertNode<NameExpression>(inner.Left).Name.ToString());
        Assert.AreEqual("b", AssertNode<NameExpression>(inner.Right).Name.ToString());
    }

    /// <summary>
    /// The call operator <c>(</c> binds tighter than the chain <c>~&gt;</c>, so <c>x ~&gt; $f(a)</c> parses
    /// to an <see cref="ApplyExpression"/> whose right operand is a <see cref="CallExpression"/> — the shape
    /// the evaluator detects as the call-prepend case.
    /// </summary>
    [TestMethod]
    public void ChainRightCallParsesToApplyOverCall()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("x ~> $f(a)", pool);

        ApplyExpression apply = AssertNode<ApplyExpression>(tree);
        Assert.AreEqual("x", AssertNode<NameExpression>(apply.Left).Name.ToString());

        CallExpression call = AssertNode<CallExpression>(apply.Right);
        Assert.AreEqual("f", AssertNode<VariableExpression>(call.Procedure).Name.ToString());
        Assert.HasCount(1, call.Arguments);
        Assert.AreEqual("a", AssertNode<NameExpression>(call.Arguments[0]).Name.ToString());
    }

    /// <summary>The wildcard <c>*</c> in nud position parses to a <see cref="WildcardExpression"/> with no errors.</summary>
    [TestMethod]
    public void WildcardParsesToWildcardNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("*", pool);

        AssertNode<WildcardExpression>(tree);
    }

    /// <summary>The descendant <c>**</c> in nud position parses to a <see cref="DescendantExpression"/> with no errors.</summary>
    [TestMethod]
    public void DescendantParsesToDescendantNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("**", pool);

        AssertNode<DescendantExpression>(tree);
    }

    /// <summary>The range <c>1..5</c> parses to a <see cref="RangeExpression"/> over its two numeric bounds.</summary>
    [TestMethod]
    public void RangeParsesToRangeNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("1..5", pool);

        RangeExpression range = AssertNode<RangeExpression>(tree);
        Assert.AreEqual("1", AssertNode<LiteralExpression>(range.Low).Value.ToString());
        Assert.AreEqual("5", AssertNode<LiteralExpression>(range.High).Value.ToString());
    }

    /// <summary>
    /// The parent <c>%</c> in nud position now parses to a <see cref="ParentExpression"/>, but a bare <c>%</c>
    /// has no enclosing path to resolve its ancestor against, so the post-parse ancestry pass records a
    /// <see cref="WellKnownDiagnostics.Jsonata.CannotDeriveAncestor"/> (the reference's S0217) diagnostic — the
    /// tree node is the parent operator, not an <see cref="ErrorExpression"/>.
    /// </summary>
    [TestMethod]
    public void ParentYieldsUnresolvedAncestorError()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("%"), pool);

        Assert.IsTrue(result.HasErrors, "%");
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.CannotDeriveAncestor), "%");
        AssertNode<ParentExpression>(result.Tree);
    }

    /// <summary>
    /// A wide array literal <c>[0,1,...,63]</c> (64 elements) parses without any diagnostic AND evaluates to
    /// that same 64-element array. The post-parse path-processing pass runs on every successful parse, so its
    /// nesting-depth guard must bound genuine nesting only — never a node's sibling breadth — or a valid wide
    /// expression like this would wrongly throw a parse error though it parses and evaluates today.
    /// </summary>
    [TestMethod]
    public void WideArrayLiteralParsesAndEvaluatesToTheArray()
    {
        StringBuilder source = new("[");
        StringBuilder expected = new("[");
        for(int i = 0; i < 64; i++)
        {
            if(i > 0)
            {
                source.Append(',');
                expected.Append(',');
            }

            source.Append(i);
            expected.Append(i);
        }

        source.Append(']');
        expected.Append(']');

        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> parsed = JsonataEngine.Parse(Encoding.UTF8.GetBytes(source.ToString()), pool);
        Assert.IsFalse(parsed.HasErrors, "The 64-element array literal must parse without a diagnostic.");

        string actual = EvaluateToJson(source.ToString(), "null");
        Assert.AreEqual(expected.ToString(), actual, "The 64-element array literal must evaluate to that array.");
    }

    /// <summary>
    /// A moderately deep nesting — a 32-deep parenthesised-block chain <c>(((...(1)...)))</c> — parses without
    /// any diagnostic: the path-processing pass's nesting-depth guard tolerates genuine nesting well within the
    /// bound and does not false-positive at this depth.
    /// </summary>
    [TestMethod]
    public void ModeratelyDeepNestingParsesWithoutError()
    {
        const int depth = 32;
        string source = new string('(', depth) + "1" + new string(')', depth);

        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> parsed = JsonataEngine.Parse(Encoding.UTF8.GetBytes(source), pool);

        Assert.IsFalse(parsed.HasErrors, "A 32-deep block nesting must parse without a diagnostic.");
    }

    /// <summary>
    /// The trailing block parent-capture <c>Account.Order.Product.( $parent := %; %.OrderID )</c> resolves its
    /// <c>%</c> against the <c>Product</c> step during the ancestry pass: no unresolved-ancestor (the reference's
    /// S0217 / <see cref="WellKnownDiagnostics.Jsonata.CannotDeriveAncestor"/>) diagnostic is recorded, even
    /// though the tuple-stream evaluation of the path is the SUB-1 stub. The block step's two bubbled <c>%</c>
    /// slots must be carried onto the path's last step so the backward seek can resolve them, rather than being
    /// dropped and wrongly swept as unresolved.
    /// </summary>
    [TestMethod]
    public void TrailingBlockParentCaptureResolvesAncestry()
    {
        const string source = "Account.Order.Product.( $parent := %; %.OrderID )";

        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> parsed = JsonataEngine.Parse(Encoding.UTF8.GetBytes(source), pool);

        Assert.IsFalse(
            HasDiagnosticCode(parsed.Diagnostics, WellKnownDiagnostics.Jsonata.CannotDeriveAncestor),
            "The % inside the trailing block must resolve against Product, not record an unresolved-ancestor diagnostic.");
    }

    /// <summary>
    /// An unterminated array constructor <c>[1,2</c> records a JS0004 missing-closer diagnostic yet still
    /// yields an <see cref="ArrayConstructorExpression"/> carrying the elements parsed so far (the partial
    /// node is kept, not discarded into an error node).
    /// </summary>
    [TestMethod]
    public void UnterminatedArrayConstructorReportsMissingCloserAndKeepsNode()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("[1,2"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.MissingCloser));

        ArrayConstructorExpression array = AssertNode<ArrayConstructorExpression>(result.Tree);
        Assert.HasCount(2, array.Elements);
    }

    /// <summary>A parenthesised block <c>(a; b)</c> parses to a two-statement <see cref="BlockExpression"/> in source order.</summary>
    [TestMethod]
    public void ParenthesisedBlockParsesToBlock()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("(a; b)", pool);

        BlockExpression block = AssertNode<BlockExpression>(tree);
        Assert.HasCount(2, block.Statements);
        Assert.AreEqual("a", AssertNode<NameExpression>(block.Statements[0]).Name.ToString());
        Assert.AreEqual("b", AssertNode<NameExpression>(block.Statements[1]).Name.ToString());
    }

    /// <summary>The empty block <c>()</c> parses to a zero-statement <see cref="BlockExpression"/>.</summary>
    [TestMethod]
    public void EmptyBlockParsesToEmptyBlock()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("()", pool);

        BlockExpression block = AssertNode<BlockExpression>(tree);
        Assert.IsEmpty(block.Statements);
    }

    /// <summary>A trailing semicolon <c>(a;)</c> adds no empty statement: it parses to the one-statement block <c>(a)</c>.</summary>
    [TestMethod]
    public void TrailingSemicolonAddsNoStatement()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("(a;)", pool);

        BlockExpression block = AssertNode<BlockExpression>(tree);
        Assert.HasCount(1, block.Statements);
        Assert.AreEqual("a", AssertNode<NameExpression>(block.Statements[0]).Name.ToString());
    }

    /// <summary>The bind operator <c>$x := 5</c> parses to a <see cref="BindExpression"/> naming the bare variable.</summary>
    [TestMethod]
    public void BindParsesToBindExpression()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("$x := 5", pool);

        BindExpression bind = AssertNode<BindExpression>(tree);
        Assert.AreEqual("x", bind.VariableName.ToString());
        Assert.AreEqual(JsonataLiteralKind.Number, AssertNode<LiteralExpression>(bind.Value).Kind);
    }

    /// <summary>The bind operator is right-associative: <c>$a := $b := 5</c> groups as <c>$a := ($b := 5)</c>.</summary>
    [TestMethod]
    public void BindIsRightAssociative()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("$a := $b := 5", pool);

        BindExpression outer = AssertNode<BindExpression>(tree);
        Assert.AreEqual("a", outer.VariableName.ToString());

        BindExpression inner = AssertNode<BindExpression>(outer.Value);
        Assert.AreEqual("b", inner.VariableName.ToString());
        Assert.AreEqual(JsonataLiteralKind.Number, AssertNode<LiteralExpression>(inner.Value).Kind);
    }

    /// <summary>A non-variable left side of <c>:=</c> reports the JS0006 bind-left diagnostic.</summary>
    [TestMethod]
    public void BindLeftNotVariableReportsDiagnostic()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("5 := 3"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.BindLeftNotVariable));
    }

    /// <summary>The Elvis operator <c>a ?: b</c> parses to a <see cref="DefaultExpression"/> with the Elvis operator.</summary>
    [TestMethod]
    public void ElvisParsesToDefaultExpression()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a ?: b", pool);

        DefaultExpression def = AssertNode<DefaultExpression>(tree);
        Assert.AreEqual(DefaultOperator.Elvis, def.Operator);
        Assert.AreEqual("a", AssertNode<NameExpression>(def.Left).Name.ToString());
        Assert.AreEqual("b", AssertNode<NameExpression>(def.Right).Name.ToString());
    }

    /// <summary>The coalesce operator <c>a ?? b</c> parses to a <see cref="DefaultExpression"/> with the coalesce operator.</summary>
    [TestMethod]
    public void CoalesceParsesToDefaultExpression()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a ?? b", pool);

        DefaultExpression def = AssertNode<DefaultExpression>(tree);
        Assert.AreEqual(DefaultOperator.Coalesce, def.Operator);
    }

    /// <summary>The default operators are left-associative: <c>a ?: b ?: c</c> groups as <c>(a ?: b) ?: c</c>.</summary>
    [TestMethod]
    public void DefaultOperatorIsLeftAssociative()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a ?: b ?: c", pool);

        DefaultExpression outer = AssertNode<DefaultExpression>(tree);
        Assert.AreEqual("c", AssertNode<NameExpression>(outer.Right).Name.ToString());

        DefaultExpression inner = AssertNode<DefaultExpression>(outer.Left);
        Assert.AreEqual("a", AssertNode<NameExpression>(inner.Left).Name.ToString());
        Assert.AreEqual("b", AssertNode<NameExpression>(inner.Right).Name.ToString());
    }

    /// <summary>The ternary <c>a ? b : c</c> still parses to a <see cref="ConditionalExpression"/>, distinct from the default operators.</summary>
    [TestMethod]
    public void TernaryStillParsesDistinctFromDefault()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("a ? b : c", pool);

        ConditionalExpression conditional = AssertNode<ConditionalExpression>(tree);
        Assert.IsNotNull(conditional.WhenFalse);
    }

    /// <summary>
    /// A complete expression followed by an unlexable byte records exactly one diagnostic for that span:
    /// the parser's trailing-token recovery stays silent because the lexer already bridged its error.
    /// </summary>
    [TestMethod]
    public void TrailingUnlexableByteRecordsSingleDiagnostic()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("1 \\"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.HasCount(1, result.Diagnostics);
    }

    /// <summary>Feeding the same source's tokens one at a time yields a structurally-equal tree to the whole-stream parse.</summary>
    [TestMethod]
    public void IncrementalFeedMatchesWholeStreamParse()
    {
        const string source = "a.b[c=1].d ? x + 1 : -y & \"z\"";
        using Utf8StringPool pool = new();

        JsonataExpression wholeStream = Parse(source, pool);
        JsonataExpression incremental = ParseIncrementally(source, pool);

        Assert.IsTrue(
            JsonataExpressionWalker.StructurallyEqual(wholeStream, incremental),
            "The incremental feed produced a different tree than the whole-stream parse.");
    }

    /// <summary>
    /// Feeding the tokens of an array constructor one at a time yields a structurally-equal tree to the
    /// whole-stream parse — so the walker's variadic <c>Children</c> for an array constructor is exercised
    /// end to end by the structural comparison.
    /// </summary>
    [TestMethod]
    public void IncrementalFeedMatchesWholeStreamParseForArrayConstructor()
    {
        const string source = "[a, 1 + 2]";
        using Utf8StringPool pool = new();

        JsonataExpression wholeStream = Parse(source, pool);
        JsonataExpression incremental = ParseIncrementally(source, pool);

        Assert.IsTrue(
            JsonataExpressionWalker.StructurallyEqual(wholeStream, incremental),
            "The incremental feed produced a different array constructor than the whole-stream parse.");
    }

    /// <summary>
    /// An identity rewrite over an array constructor returns the same instance: the variadic <c>Rebuild</c>
    /// path must not reallocate the node when no child changed.
    /// </summary>
    [TestMethod]
    public void WalkerIdentityRewriteOverArrayConstructorReturnsSameInstance()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("[a, b, c]", pool);

        JsonataExpressionRewriter identity = static node => node;
        JsonataExpression rewritten = JsonataExpressionWalker.Transform(tree, identity);

        Assert.AreSame(tree, rewritten);
    }

    /// <summary>
    /// A transforming rewrite that renames every field reference over <c>[a, b, c]</c> returns a
    /// three-element <see cref="ArrayConstructorExpression"/> whose elements are the rewritten names in
    /// source order — the regression guard that the variadic <c>Rebuild</c> drops no element and reorders
    /// none.
    /// </summary>
    [TestMethod]
    public void WalkerTransformingRewriteOverArrayConstructorPreservesElementOrder()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("[a, b, c]", pool);

        //Rename each field reference to its name with a trailing marker, so the rewrite is observable and
        //the per-element order is verifiable.
        JsonataExpressionRewriter rename = static node => node is NameExpression name
            ? new NameExpression(name.Span, new Utf8String(Encoding.UTF8.GetBytes(name.Name.ToString() + "_")))
            : node;

        JsonataExpression rewritten = JsonataExpressionWalker.Transform(tree, rename);

        ArrayConstructorExpression array = AssertNode<ArrayConstructorExpression>(rewritten);
        Assert.HasCount(3, array.Elements);
        Assert.AreEqual("a_", AssertNode<NameExpression>(array.Elements[0]).Name.ToString());
        Assert.AreEqual("b_", AssertNode<NameExpression>(array.Elements[1]).Name.ToString());
        Assert.AreEqual("c_", AssertNode<NameExpression>(array.Elements[2]).Name.ToString());
    }

    /// <summary>
    /// An identity rewrite over an object constructor returns the same instance: the variadic interleaved
    /// <c>Children</c> / <c>Rebuild</c> path must not reallocate the node when no child changed.
    /// </summary>
    [TestMethod]
    public void WalkerIdentityRewriteOverObjectConstructorReturnsSameInstance()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("{\"a\": x, \"b\": y}", pool);

        JsonataExpressionRewriter identity = static node => node;
        JsonataExpression rewritten = JsonataExpressionWalker.Transform(tree, identity);

        Assert.AreSame(tree, rewritten);
    }

    /// <summary>
    /// A transforming rewrite that renames every field reference over <c>{"a": x, "b": y}</c> returns a
    /// two-member <see cref="ObjectConstructorExpression"/> whose member values are the rewritten names in
    /// source order with their keys unchanged — the regression guard that the variadic interleaved
    /// <c>Rebuild</c> drops no member, reorders none, and re-pairs keys with values correctly.
    /// </summary>
    [TestMethod]
    public void WalkerTransformingRewriteOverObjectConstructorPreservesMemberOrder()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("{\"a\": x, \"b\": y}", pool);

        //Rename each field reference to its name with a trailing marker, so the rewrite is observable and
        //the per-member key/value pairing is verifiable.
        JsonataExpressionRewriter rename = static node => node is NameExpression name
            ? new NameExpression(name.Span, new Utf8String(Encoding.UTF8.GetBytes(name.Name.ToString() + "_")))
            : node;

        JsonataExpression rewritten = JsonataExpressionWalker.Transform(tree, rename);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(rewritten);
        Assert.HasCount(2, obj.Members);
        Assert.AreEqual("a", AssertNode<LiteralExpression>(obj.Members[0].Key).Value.ToString());
        Assert.AreEqual("x_", AssertNode<NameExpression>(obj.Members[0].Value).Name.ToString());
        Assert.AreEqual("b", AssertNode<LiteralExpression>(obj.Members[1].Key).Value.ToString());
        Assert.AreEqual("y_", AssertNode<NameExpression>(obj.Members[1].Value).Name.ToString());
    }

    /// <summary>
    /// A transforming rewrite over the led path-step group-by <c>src{"a": x}</c> rewrites both the grouping
    /// source and the member values, and the rebuilt node keeps its source bound and its members re-paired —
    /// the guard that the source-prefixed <c>[source, k0, v0, ...]</c> child list walks and rebuilds
    /// correctly.
    /// </summary>
    [TestMethod]
    public void WalkerRewriteOverLedObjectGroupRewritesSourceAndMembers()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("src{\"a\": x}", pool);

        JsonataExpressionRewriter rename = static node => node is NameExpression name
            ? new NameExpression(name.Span, new Utf8String(Encoding.UTF8.GetBytes(name.Name.ToString() + "_")))
            : node;

        JsonataExpression rewritten = JsonataExpressionWalker.Transform(tree, rename);

        ObjectConstructorExpression obj = AssertNode<ObjectConstructorExpression>(rewritten);
        Assert.IsNotNull(obj.Source);
        Assert.AreEqual("src_", AssertNode<NameExpression>(obj.Source!).Name.ToString());
        Assert.HasCount(1, obj.Members);
        Assert.AreEqual("a", AssertNode<LiteralExpression>(obj.Members[0].Key).Value.ToString());
        Assert.AreEqual("x_", AssertNode<NameExpression>(obj.Members[0].Value).Name.ToString());
    }

    /// <summary>
    /// The led path-step group-by and the prefix object constructor with identical members are NOT
    /// structurally equal: the source's presence is a distinguishing scalar field, so <c>src{"a": x}</c> and
    /// <c>{"a": x}</c> compare unequal under <see cref="JsonataExpressionWalker.StructurallyEqual"/>.
    /// </summary>
    [TestMethod]
    public void WalkerLedAndPrefixObjectConstructorsAreNotStructurallyEqual()
    {
        using Utf8StringPool pool = new();
        JsonataExpression led = Parse("src{\"a\": x}", pool);
        JsonataExpression prefix = Parse("{\"a\": x}", pool);

        Assert.IsFalse(JsonataExpressionWalker.StructurallyEqual(led, prefix));
    }

    /// <summary>
    /// A lambda <c>function($x, $y){ $x + $y }</c> parses to a <see cref="LambdaExpression"/> carrying its
    /// parameter names in declaration order and a body that is the parsed binary expression.
    /// </summary>
    [TestMethod]
    public void LambdaParsesToLambdaNodeWithParametersAndBody()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("function($x, $y){ $x + $y }", pool);

        LambdaExpression lambda = AssertNode<LambdaExpression>(tree);
        Assert.HasCount(2, lambda.Parameters);
        Assert.AreEqual("x", lambda.Parameters[0].ToString());
        Assert.AreEqual("y", lambda.Parameters[1].ToString());

        BinaryExpression body = AssertNode<BinaryExpression>(lambda.Body);
        Assert.AreEqual(BinaryOperator.Add, body.Operator);
    }

    /// <summary>A zero-parameter lambda <c>function(){42}</c> parses to a <see cref="LambdaExpression"/> with no parameters.</summary>
    [TestMethod]
    public void ZeroParameterLambdaParsesToZeroParameterNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("function(){42}", pool);

        LambdaExpression lambda = AssertNode<LambdaExpression>(tree);
        Assert.IsEmpty(lambda.Parameters);
        Assert.AreEqual(JsonataLiteralKind.Number, AssertNode<LiteralExpression>(lambda.Body).Kind);
    }

    /// <summary>The Greek <c>λ</c> is an alias for <c>function</c>: <c>λ($x){$x}</c> parses to the same <see cref="LambdaExpression"/> shape.</summary>
    [TestMethod]
    public void LambdaShorthandParsesToLambdaNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("λ($x){$x}", pool);

        LambdaExpression lambda = AssertNode<LambdaExpression>(tree);
        Assert.HasCount(1, lambda.Parameters);
        Assert.AreEqual("x", lambda.Parameters[0].ToString());
        Assert.AreEqual(VariableForm.Named, AssertNode<VariableExpression>(lambda.Body).Form);
    }

    /// <summary>
    /// A function call <c>$f(1, 2)</c> parses to a <see cref="CallExpression"/> whose procedure is the
    /// variable reference and whose arguments are the two parsed argument expressions in source order.
    /// </summary>
    [TestMethod]
    public void CallParsesToCallNodeWithProcedureAndArguments()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("$f(1, 2)", pool);

        CallExpression call = AssertNode<CallExpression>(tree);
        Assert.AreEqual(VariableForm.Named, AssertNode<VariableExpression>(call.Procedure).Form);
        Assert.HasCount(2, call.Arguments);
        Assert.AreEqual("1", AssertNode<LiteralExpression>(call.Arguments[0]).Value.ToString());
        Assert.AreEqual("2", AssertNode<LiteralExpression>(call.Arguments[1]).Value.ToString());
    }

    /// <summary>A no-argument call <c>$f()</c> parses to a <see cref="CallExpression"/> with no arguments.</summary>
    [TestMethod]
    public void NoArgumentCallParsesToZeroArgumentNode()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("$f()", pool);

        CallExpression call = AssertNode<CallExpression>(tree);
        Assert.IsEmpty(call.Arguments);
    }

    /// <summary>
    /// An immediately-invoked lambda <c>function($x){$x}(5)</c> parses to a <see cref="CallExpression"/>
    /// whose procedure is the <see cref="LambdaExpression"/> and whose single argument is the literal.
    /// </summary>
    [TestMethod]
    public void ImmediatelyInvokedLambdaParsesToCallOverLambda()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("function($x){$x}(5)", pool);

        CallExpression call = AssertNode<CallExpression>(tree);
        AssertNode<LambdaExpression>(call.Procedure);
        Assert.HasCount(1, call.Arguments);
        Assert.AreEqual("5", AssertNode<LiteralExpression>(call.Arguments[0]).Value.ToString());
    }

    /// <summary>A non-variable lambda parameter <c>function(5){5}</c> records the JS0007 diagnostic.</summary>
    [TestMethod]
    public void NonVariableLambdaParameterReportsDiagnostic()
    {
        using Utf8StringPool pool = new();
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes("function(5){5}"), pool);

        Assert.IsTrue(result.HasErrors);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, WellKnownDiagnostics.Jsonata.LambdaParameterNotVariable));
    }

    /// <summary>The bare context-focus <c>$</c> is accepted as a parameter name (any variable token is a valid parameter), matching upstream: <c>function($){1}</c> parses without error.</summary>
    [TestMethod]
    public void ContextFocusParameterIsAccepted()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("function($){1}", pool);

        LambdaExpression lambda = AssertNode<LambdaExpression>(tree);
        Assert.HasCount(1, lambda.Parameters);
        Assert.IsTrue(lambda.Parameters[0].IsEmpty);
    }

    /// <summary>
    /// Feeding the tokens of a lambda and its call one at a time yields a structurally-equal tree to the
    /// whole-stream parse — so the new LambdaDefinition and ArgumentList frames survive NeedMore suspension
    /// across their parameter, body, and argument stages.
    /// </summary>
    [TestMethod]
    public void IncrementalFeedMatchesWholeStreamParseForLambdaCall()
    {
        const string source = "function($x,$y){$x+$y}(5,6)";
        using Utf8StringPool pool = new();

        JsonataExpression wholeStream = Parse(source, pool);
        JsonataExpression incremental = ParseIncrementally(source, pool);

        Assert.IsTrue(
            JsonataExpressionWalker.StructurallyEqual(wholeStream, incremental),
            "The incremental feed produced a different lambda/call than the whole-stream parse.");
    }

    /// <summary>
    /// A transforming rewrite that renames every field reference over <c>$f(a, b)</c> returns a
    /// <see cref="CallExpression"/> whose procedure is preserved and whose arguments are the rewritten names
    /// in source order — the regression guard that the variadic <c>[procedure, ...arguments]</c> rebuild
    /// keeps the procedure slot and drops no argument.
    /// </summary>
    [TestMethod]
    public void WalkerTransformingRewriteOverCallPreservesProcedureAndArgumentOrder()
    {
        using Utf8StringPool pool = new();
        JsonataExpression tree = Parse("$f(a, b)", pool);

        JsonataExpressionRewriter rename = static node => node is NameExpression name
            ? new NameExpression(name.Span, new Utf8String(Encoding.UTF8.GetBytes(name.Name.ToString() + "_")))
            : node;

        JsonataExpression rewritten = JsonataExpressionWalker.Transform(tree, rename);

        CallExpression call = AssertNode<CallExpression>(rewritten);
        Assert.AreEqual("f", AssertNode<VariableExpression>(call.Procedure).Name.ToString());
        Assert.HasCount(2, call.Arguments);
        Assert.AreEqual("a_", AssertNode<NameExpression>(call.Arguments[0]).Name.ToString());
        Assert.AreEqual("b_", AssertNode<NameExpression>(call.Arguments[1]).Name.ToString());
    }

    /// <summary>
    /// Feeding the tokens of an object constructor one at a time yields a structurally-equal tree to the
    /// whole-stream parse — so the walker's variadic interleaved <c>Children</c> for an object constructor
    /// is exercised end to end by the structural comparison.
    /// </summary>
    [TestMethod]
    public void IncrementalFeedMatchesWholeStreamParseForObjectConstructor()
    {
        const string source = "{\"a\": x, \"b\": 1 + 2}";
        using Utf8StringPool pool = new();

        JsonataExpression wholeStream = Parse(source, pool);
        JsonataExpression incremental = ParseIncrementally(source, pool);

        Assert.IsTrue(
            JsonataExpressionWalker.StructurallyEqual(wholeStream, incremental),
            "The incremental feed produced a different object constructor than the whole-stream parse.");
    }

    /// <summary>Parses a source string into its tree via the public facade, asserting no diagnostics were raised.</summary>
    /// <param name="source">The JSONata source.</param>
    /// <param name="pool">The interning pool kept alive for the result's values.</param>
    /// <returns>The parsed expression tree.</returns>
    private static JsonataExpression Parse(string source, Utf8StringPool pool)
    {
        ParseResult<JsonataExpression> result = JsonataEngine.Parse(Encoding.UTF8.GetBytes(source), pool);
        Assert.IsFalse(result.HasErrors, source);

        return result.Tree;
    }

    /// <summary>Evaluates a JSONata expression against a JSON input document and returns the result serialized to JSON text.</summary>
    /// <param name="expression">The JSONata source.</param>
    /// <param name="inputJson">The input document as JSON text, parsed through the host JSON adapter.</param>
    /// <returns>The result serialized to JSON text; the empty string for the undefined value.</returns>
    private static string EvaluateToJson(string expression, string inputJson)
    {
        using Utf8StringPool pool = new();
        JsonNode input = StjJsonAdapter.Parse(new Utf8String(Encoding.UTF8.GetBytes(inputJson)));
        JsonataValue result = JsonataEngine.Evaluate(Encoding.UTF8.GetBytes(expression), input, pool);

        return JsonataEngine.SerializeToJson(result).ToString();
    }

    /// <summary>Determines whether a tree contains an <see cref="ErrorExpression"/> at any position.</summary>
    /// <param name="tree">The expression tree to scan.</param>
    /// <returns><see langword="true"/> when an error node is present.</returns>
    private static bool ContainsError(JsonataExpression tree)
    {
        foreach(JsonataExpression node in JsonataExpressionWalker.Traverse(tree))
        {
            if(node is ErrorExpression)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether the diagnostics include the given well-known diagnostic code.</summary>
    /// <param name="diagnostics">The diagnostics gathered while parsing.</param>
    /// <param name="code">The well-known diagnostic code to look for (a <c>WellKnownDiagnostics.Jsonata</c> constant).</param>
    /// <returns><see langword="true"/> when a diagnostic with that code is present.</returns>
    private static bool HasDiagnosticCode(IReadOnlyList<Diagnostic> diagnostics, Utf8String code)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Code.Span.SequenceEqual(code.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Parses a source by feeding its lexed tokens one at a time through the resumable path.</summary>
    /// <param name="source">The JSONata source.</param>
    /// <param name="pool">The interning pool kept alive for the result's values.</param>
    /// <returns>The parsed expression tree.</returns>
    private static JsonataExpression ParseIncrementally(string source, Utf8StringPool pool)
    {
        List<JsonataToken> tokens = LexAll(source, pool);
        JsonataParser parser = new(pool);
        JsonataExpression? expression = null;

        foreach(JsonataToken token in tokens)
        {
            parser.FeedToken(token);
            if(parser.TryParseExpression(out expression) == ParseStatus.Produced)
            {
                break;
            }
        }

        Assert.IsNotNull(expression, source);

        return expression;
    }

    /// <summary>Lexes a source string into its full token list, including the terminating end-of-input.</summary>
    /// <param name="source">The JSONata source.</param>
    /// <param name="pool">The interning pool the tokens' payloads live in.</param>
    /// <returns>The lexed tokens.</returns>
    private static List<JsonataToken> LexAll(string source, Utf8StringPool pool)
    {
        JsonataLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        List<JsonataToken> tokens = [];
        foreach(JsonataToken token in lexer.Tokenize())
        {
            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>Asserts a node is of the expected type and returns it typed.</summary>
    /// <typeparam name="T">The expected node type.</typeparam>
    /// <param name="node">The node to check.</param>
    /// <returns>The node typed as <typeparamref name="T"/>.</returns>
    private static T AssertNode<T>(JsonataExpression node)
        where T : JsonataExpression
    {
        Assert.IsInstanceOfType<T>(node);

        return (T)node;
    }
}
