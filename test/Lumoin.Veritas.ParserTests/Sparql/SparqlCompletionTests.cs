using System.Text;
using Lumoin.Veritas.Sparql.Completion;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests <see cref="SparqlCompletion.Describe"/>: the store-free completion context at a caret. Each case
/// places the caret at the end of the query text (or at an explicit offset) and checks the expected next
/// tokens and the enclosing-production chain (outermost to innermost). The expected tokens come from the
/// innermost open production plus, whenever that production may legitimately end at the caret, the
/// continuations of the enclosing productions it would close into.
/// </summary>
[TestClass]
internal sealed class SparqlCompletionTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Describes the completion context at the end of the given query text.</summary>
    /// <param name="query">The query text up to the caret.</param>
    /// <returns>The completion context at the caret.</returns>
    private static CompletionContext DescribeAtEnd(string query)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(query);

        return SparqlCompletion.Describe(bytes, bytes.Length);
    }

    /// <summary>Describes the completion context at an explicit byte offset in the given query text.</summary>
    /// <param name="query">The whole query text.</param>
    /// <param name="caretByteOffset">The caret position as a byte offset.</param>
    /// <returns>The completion context at the caret.</returns>
    private static CompletionContext DescribeAt(string query, int caretByteOffset)
    {
        return SparqlCompletion.Describe(Encoding.UTF8.GetBytes(query), caretByteOffset);
    }

    /// <summary>
    /// Asserts the context's expected tokens are exactly <paramref name="expected"/>, in that order — the
    /// whole answer, so neither a missing continuation nor a widened one can pass unnoticed.
    /// </summary>
    /// <param name="context">The completion context under test.</param>
    /// <param name="expected">The expected token kinds, in suggestion order.</param>
    private static void AssertExpectedTokens(CompletionContext context, params SparqlTokenKind[] expected)
    {
        Assert.AreEqual(string.Join(',', expected), string.Join(',', context.ExpectedTokens));
    }

    /// <summary>An empty buffer expects a prologue declaration or a query form, inside the request production.</summary>
    [TestMethod]
    public void EmptyBufferExpectsPrologueOrForm()
    {
        CompletionContext context = DescribeAtEnd("");

        Assert.Contains(SparqlTokenKind.SelectKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.PrefixKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.AskKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.InsertKeyword, context.ExpectedTokens);
        Assert.HasCount(1, context.EnclosingProductions);
        Assert.AreEqual(ParseFrameKind.Request, context.EnclosingProductions[0]);
    }

    /// <summary>After <c>SELECT</c> the projection list expects a variable or an <c>(expr AS ?var)</c> projection.</summary>
    [TestMethod]
    public void AfterSelectExpectsProjection()
    {
        CompletionContext context = DescribeAtEnd("SELECT ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.OpenParen, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.SelectClause, context.EnclosingProductions[^1]);
    }

    /// <summary>At a group-graph-pattern member position the caret expects any member start or the closing brace.</summary>
    [TestMethod]
    public void GroupMemberPositionExpectsMembersOrClose()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.OptionalKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.FilterKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.OpenBrace, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.GroupGraphPattern, context.EnclosingProductions[^1]);
    }

    /// <summary>Directly after a string literal's <c>^^</c> the caret sits at the datatype position, which admits exactly an IRI or a prefixed name.</summary>
    [TestMethod]
    public void AfterDatatypeMarkerExpectsAnIriOrPrefixedName()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p \"x\"^^");

        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.PrefixedName, context.ExpectedTokens);
        Assert.DoesNotContain(SparqlTokenKind.StringLiteral, context.ExpectedTokens);
    }

    /// <summary>After a triple subject the caret expects a verb (predicate or property path).</summary>
    [TestMethod]
    public void AfterSubjectExpectsVerb()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ");

        Assert.Contains(SparqlTokenKind.A, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Caret, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Request, context.EnclosingProductions[0]);
        Assert.AreEqual(ParseFrameKind.Triple, context.EnclosingProductions[^1]);
    }

    /// <summary>After a complete triple the caret expects a separator, terminator, annotation, or the group closer.</summary>
    [TestMethod]
    public void AfterObjectExpectsContinuation()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o");

        Assert.Contains(SparqlTokenKind.Period, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Semicolon, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Comma, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
    }

    /// <summary>After the WHERE group the caret expects a solution modifier, inside only the request production.</summary>
    [TestMethod]
    public void AfterWhereGroupExpectsSolutionModifiers()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o }");

        Assert.Contains(SparqlTokenKind.OrderKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.LimitKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.GroupKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.ValuesKeyword, context.ExpectedTokens);
        Assert.HasCount(1, context.EnclosingProductions);
        Assert.AreEqual(ParseFrameKind.Request, context.EnclosingProductions[0]);
    }

    /// <summary>Inside a FILTER's parentheses the caret expects an expression primary, inside the expression production.</summary>
    [TestMethod]
    public void InsideFilterExpectsExpression()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o . FILTER(");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.BuiltInFunctionName, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Bang, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.OpenParen, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Request, context.EnclosingProductions[0]);
        Assert.AreEqual(ParseFrameKind.Expression, context.EnclosingProductions[^1]);
    }

    /// <summary>The caret offset is echoed back, clamped to the buffer.</summary>
    [TestMethod]
    public void CaretOffsetIsEchoed()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o }");

        Assert.AreEqual("SELECT * WHERE { ?s ?p ?o }".Length, context.CaretByteOffset);
    }

    /// <summary>Whether a variable of the given name is in scope in the completion context.</summary>
    /// <param name="context">The completion context.</param>
    /// <param name="variableName">The variable name without its leading sigil.</param>
    /// <returns><see langword="true"/> when a variable of that name is in scope.</returns>
    private static bool InScope(CompletionContext context, string variableName)
        => context.InScopeVariables.Any(scope => scope.Variable.Name.ToString() == variableName);

    /// <summary>Inside a FILTER the caret sees the variables bound by the preceding triples of its group.</summary>
    [TestMethod]
    public void FilterSeesPrecedingTripleVariables()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o . FILTER(");

        Assert.IsTrue(InScope(context, "s"));
        Assert.IsTrue(InScope(context, "p"));
        Assert.IsTrue(InScope(context, "o"));
    }

    /// <summary>Inside a BIND the caret sees the variables bound earlier in the query.</summary>
    [TestMethod]
    public void BindSeesPrecedingBindings()
    {
        CompletionContext context = DescribeAtEnd("PREFIX e: <http://e/> SELECT * WHERE { ?person e:name ?name . BIND(");

        Assert.IsTrue(InScope(context, "person"));
        Assert.IsTrue(InScope(context, "name"));
    }

    /// <summary>An empty buffer has no variables in scope.</summary>
    [TestMethod]
    public void EmptyBufferHasNoVariablesInScope()
    {
        CompletionContext context = DescribeAtEnd("");

        Assert.IsEmpty(context.InScopeVariables);
    }

    /// <summary>Store-free, every in-scope variable's datatype is unresolved.</summary>
    [TestMethod]
    public void InScopeDatatypesAreUnknown()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o . FILTER(");

        foreach(ScopeVariable scope in context.InScopeVariables)
        {
            Assert.AreEqual(DatatypeSource.Unknown, scope.DatatypeSource);
            Assert.IsNull(scope.Datatype);
        }
    }

    /// <summary>
    /// A caret in the middle of the buffer sees only the bindings before it: a BIND at the caret sees the
    /// preceding triple variables but not a following triple's variables, nor its own (not-yet-bound) target.
    /// </summary>
    [TestMethod]
    public void CaretInMiddleSeesOnlyPrecedingBindings()
    {
        string source = "SELECT * WHERE { ?a ?b ?c . BIND(?x AS ?y) . ?later ?p2 ?o2 }";
        int caret = source.IndexOf("BIND(", StringComparison.Ordinal) + "BIND(".Length;
        CompletionContext context = SparqlCompletion.Describe(Encoding.UTF8.GetBytes(source), caret);

        Assert.IsTrue(InScope(context, "a"));
        Assert.IsTrue(InScope(context, "c"));
        Assert.IsFalse(InScope(context, "later"));
        Assert.IsFalse(InScope(context, "y"));
    }

    /// <summary>After <c>ORDER BY</c> the caret expects an ordering condition: ASC/DESC, a variable, or an expression.</summary>
    [TestMethod]
    public void AfterOrderByExpectsOrderingCondition()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o } ORDER BY ");

        Assert.Contains(SparqlTokenKind.AscKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.DescKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.OrderBy, context.EnclosingProductions[^1]);
    }

    /// <summary>After <c>GROUP BY</c> the caret expects a grouping condition: a variable or an expression.</summary>
    [TestMethod]
    public void AfterGroupByExpectsGroupingCondition()
    {
        CompletionContext context = DescribeAtEnd("SELECT (COUNT(?x) AS ?c) WHERE { ?x ?p ?o } GROUP BY ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.BuiltInFunctionName, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.GroupBy, context.EnclosingProductions[^1]);
    }

    /// <summary>After <c>HAVING</c> the caret expects a constraint: a parenthesised or bare expression.</summary>
    [TestMethod]
    public void AfterHavingExpectsConstraint()
    {
        CompletionContext context = DescribeAtEnd("SELECT (COUNT(?x) AS ?c) WHERE { ?x ?p ?o } GROUP BY ?p HAVING ");

        Assert.Contains(SparqlTokenKind.OpenParen, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.BuiltInFunctionName, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Having, context.EnclosingProductions[^1]);
    }

    /// <summary>After a path predicate the caret expects the object or a path-continuation operator.</summary>
    [TestMethod]
    public void AfterPathPredicateExpectsObjectOrPathOperator()
    {
        CompletionContext context = DescribeAtEnd("PREFIX e: <http://e/> SELECT * WHERE { ?s e:p ");

        Assert.Contains(SparqlTokenKind.Slash, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Pipe, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.PathSequence, context.EnclosingProductions[^1]);
    }

    /// <summary>Whether the context pairs a variable with a predicate IRI at a triple position.</summary>
    /// <param name="context">The completion context.</param>
    /// <param name="variableName">The variable name without its leading sigil.</param>
    /// <param name="predicateIri">The predicate IRI.</param>
    /// <param name="position">The triple position the variable occupies.</param>
    /// <returns><see langword="true"/> when that pairing is present.</returns>
    private static bool HasPredicate(CompletionContext context, string variableName, string predicateIri, TermPosition position)
        => context.VariablePredicates.Any(pair =>
            pair.Variable.Name.ToString() == variableName
            && pair.Predicate.ToString() == predicateIri
            && pair.Position == position);

    /// <summary>A triple's subject and object variables are paired with the constant predicate IRI and their position.</summary>
    [TestMethod]
    public void VariablesArePairedWithTheirConstantPredicate()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s <http://e/p> ?o ");

        Assert.IsTrue(HasPredicate(context, "s", "http://e/p", TermPosition.Subject));
        Assert.IsTrue(HasPredicate(context, "o", "http://e/p", TermPosition.Object));
    }

    /// <summary>A variable predicate names no single property, so it yields no variable→predicate pair.</summary>
    [TestMethod]
    public void VariablePredicateYieldsNoPairing()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o ");

        Assert.IsEmpty(context.VariablePredicates);
    }

    /// <summary>A property-path predicate names no single property, so it yields no variable→predicate pair.</summary>
    [TestMethod]
    public void PathPredicateYieldsNoPairing()
    {
        CompletionContext context = DescribeAtEnd("PREFIX e: <http://e/> SELECT * WHERE { ?s e:p/e:q ?o ");

        Assert.IsEmpty(context.VariablePredicates);
    }

    /// <summary>Inside a <c>VALUES</c> data block the caret expects a data value or the closing brace.</summary>
    [TestMethod]
    public void InsideValuesBlockExpectsDataValueOrClose()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { VALUES ?v { ");

        Assert.Contains(SparqlTokenKind.UndefKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.StringLiteral, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Values, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a collection the caret expects a term that begins an item or the closing parenthesis.</summary>
    [TestMethod]
    public void InsideCollectionExpectsItemOrClose()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ( ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseParen, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Collection, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a blank-node property list the caret expects a verb (predicate or path) or the closing bracket.</summary>
    [TestMethod]
    public void InsideBlankNodePropertyListExpectsVerbOrClose()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p [ ");

        Assert.Contains(SparqlTokenKind.A, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBracket, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.BlankNodePropertyList, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a <c>CONSTRUCT</c> template the caret expects a triple subject or the closing brace.</summary>
    [TestMethod]
    public void InsideConstructTemplateExpectsTripleOrClose()
    {
        CompletionContext context = DescribeAtEnd("CONSTRUCT { ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.ConstructTemplate, context.EnclosingProductions[^1]);
    }

    /// <summary>After <c>DESCRIBE</c> the caret expects a target: the <c>*</c> wildcard, a variable, or an IRI.</summary>
    [TestMethod]
    public void AfterDescribeExpectsTarget()
    {
        CompletionContext context = DescribeAtEnd("DESCRIBE ");

        Assert.Contains(SparqlTokenKind.Star, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Request, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside an <c>INSERT DATA</c> quad block the caret expects a triple, a <c>GRAPH</c> block, or the closing brace.</summary>
    [TestMethod]
    public void InsideInsertDataExpectsQuadOrClose()
    {
        CompletionContext context = DescribeAtEnd("INSERT DATA { ");

        Assert.Contains(SparqlTokenKind.Iri, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.GraphKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Quads, context.EnclosingProductions[^1]);
    }

    /// <summary>Inside a <c>DELETE WHERE</c> quad pattern the caret expects a triple, a <c>GRAPH</c> block, or the closing brace.</summary>
    [TestMethod]
    public void InsideDeleteWhereExpectsQuadOrClose()
    {
        CompletionContext context = DescribeAtEnd("DELETE WHERE { ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.GraphKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Quads, context.EnclosingProductions[^1]);
    }

    /// <summary>
    /// After a projected variable the projection repetition may either continue or close, so the caret
    /// expects the next projection together with what follows a closed <c>SelectClause</c>: a <c>FROM</c>
    /// dataset clause, the <c>WHERE</c> keyword, or the elided-WHERE opening brace.
    /// </summary>
    [TestMethod]
    public void AfterProjectionListExpectsNextProjectionOrDatasetOrWhere()
    {
        CompletionContext context = DescribeAtEnd("SELECT ?s ?p ?o ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.FromKeyword,
            SparqlTokenKind.WhereKeyword,
            SparqlTokenKind.OpenBrace);
        Assert.AreEqual(ParseFrameKind.Request, context.EnclosingProductions[0]);
        Assert.AreEqual(ParseFrameKind.SelectClause, context.EnclosingProductions[^1]);
    }

    /// <summary>
    /// The caret answer depends only on the text before it, so the same position inside an otherwise valid
    /// query answers exactly as the truncated buffer does.
    /// </summary>
    [TestMethod]
    public void ProjectionListFollowIsTheSameInsideAValidQuery()
    {
        CompletionContext context = DescribeAt("SELECT ?s ?p ?o WHERE { ?s ?p ?o }", 16);

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.FromKeyword,
            SparqlTokenKind.WhereKeyword,
            SparqlTokenKind.OpenBrace);
    }

    /// <summary>
    /// The <c>*</c> projection form cannot continue, so the caret after it expects exactly what follows a
    /// closed <c>SelectClause</c>.
    /// </summary>
    [TestMethod]
    public void AfterSelectStarExpectsDatasetOrWhere()
    {
        CompletionContext context = DescribeAtEnd("SELECT * ");

        AssertExpectedTokens(context, SparqlTokenKind.FromKeyword, SparqlTokenKind.WhereKeyword, SparqlTokenKind.OpenBrace);
    }

    /// <summary>
    /// The projection list needs at least one projection, so a caret before the first one expects only a
    /// projection — the clause cannot close there and contributes no continuation.
    /// </summary>
    [TestMethod]
    public void BeforeTheFirstProjectionExpectsOnlyAProjection()
    {
        CompletionContext context = DescribeAtEnd("SELECT ");

        AssertExpectedTokens(context, SparqlTokenKind.Variable, SparqlTokenKind.OpenParen);
    }

    /// <summary>An <c>ASK</c> form head admits a dataset clause or the <c>WHERE</c> opener, and nothing else.</summary>
    [TestMethod]
    public void AfterAskExpectsExactlyDatasetOrWhere()
    {
        CompletionContext context = DescribeAtEnd("ASK ");

        AssertExpectedTokens(context, SparqlTokenKind.FromKeyword, SparqlTokenKind.WhereKeyword, SparqlTokenKind.OpenBrace);
    }

    /// <summary>After a dataset clause the caret admits a further dataset clause or the <c>WHERE</c> opener, and nothing else.</summary>
    [TestMethod]
    public void AfterDatasetClauseExpectsExactlyDatasetOrWhere()
    {
        CompletionContext context = DescribeAtEnd("SELECT ?s FROM <http://g/> ");

        AssertExpectedTokens(context, SparqlTokenKind.FromKeyword, SparqlTokenKind.WhereKeyword, SparqlTokenKind.OpenBrace);
    }

    /// <summary>After a prologue declaration the caret admits exactly the whole start-of-request set: another declaration, a query form, or an update operation.</summary>
    [TestMethod]
    public void AfterPrologueDeclarationExpectsExactlyTheRequestStart()
    {
        CompletionContext context = DescribeAtEnd("PREFIX ex: <http://e/> ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.BaseKeyword,
            SparqlTokenKind.PrefixKeyword,
            SparqlTokenKind.VersionKeyword,
            SparqlTokenKind.SelectKeyword,
            SparqlTokenKind.ConstructKeyword,
            SparqlTokenKind.AskKeyword,
            SparqlTokenKind.DescribeKeyword,
            SparqlTokenKind.InsertKeyword,
            SparqlTokenKind.DeleteKeyword,
            SparqlTokenKind.WithKeyword,
            SparqlTokenKind.LoadKeyword,
            SparqlTokenKind.ClearKeyword,
            SparqlTokenKind.DropKeyword,
            SparqlTokenKind.CreateKeyword,
            SparqlTokenKind.AddKeyword,
            SparqlTokenKind.MoveKeyword,
            SparqlTokenKind.CopyKeyword);
    }

    /// <summary>
    /// A group graph pattern closes only on its <c>}</c>, so a member position admits exactly the member
    /// starts and that closer — the enclosing request contributes nothing.
    /// </summary>
    [TestMethod]
    public void GroupMemberPositionExpectsExactlyMembersOrClose()
    {
        CompletionContext context = DescribeAtEnd("SELECT ?s WHERE { ?s ?p ?o . ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.BlankNodeLabel,
            SparqlTokenKind.AnonymousBlankNode,
            SparqlTokenKind.StringLiteral,
            SparqlTokenKind.LongStringLiteral,
            SparqlTokenKind.IntegerLiteral,
            SparqlTokenKind.DecimalLiteral,
            SparqlTokenKind.DoubleLiteral,
            SparqlTokenKind.BooleanLiteral,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.OpenBracket,
            SparqlTokenKind.OpenTripleTerm,
            SparqlTokenKind.OpenReifiedTriple,
            SparqlTokenKind.OpenBrace,
            SparqlTokenKind.OptionalKeyword,
            SparqlTokenKind.MinusKeyword,
            SparqlTokenKind.GraphKeyword,
            SparqlTokenKind.ServiceKeyword,
            SparqlTokenKind.FilterKeyword,
            SparqlTokenKind.BindKeyword,
            SparqlTokenKind.ValuesKeyword,
            SparqlTokenKind.CloseBrace);
    }

    /// <summary>A top-level request that may end at the caret has no enclosing production, so it adds nothing to the solution modifiers.</summary>
    [TestMethod]
    public void AfterWhereGroupExpectsExactlyTheSolutionModifiers()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o }");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.GroupKeyword,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword);
    }

    /// <summary>
    /// A sub-<c>SELECT</c> may end once its <c>WHERE</c> pattern is parsed, so the caret also admits the
    /// closing brace of the group graph pattern that wraps it.
    /// </summary>
    [TestMethod]
    public void SubSelectMayCloseIntoItsEnclosingGroup()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { { SELECT ?x WHERE { ?x ?p ?o } ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.GroupKeyword,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword,
            SparqlTokenKind.CloseBrace);
    }

    /// <summary>
    /// A trailing <c>VALUES</c> block is the last symbol of a sub-<c>SELECT</c> and its option is spent, so
    /// the sub-<c>SELECT</c> itself admits nothing further; the caret's whole answer is the closing brace of
    /// the group graph pattern that wraps it, reached by the outward walk.
    /// </summary>
    [TestMethod]
    public void AfterASubSelectTrailingValuesBlockOnlyTheGroupCloserRemains()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { { SELECT ?x WHERE { ?x ?p ?o } VALUES ?v { 1 } ");

        AssertExpectedTokens(context, SparqlTokenKind.CloseBrace);
    }

    /// <summary>
    /// The continuation walks out through every production that may end at the caret: a satisfied
    /// <c>GROUP BY</c> closes into the sub-<c>SELECT</c>'s remaining modifiers, which close into the
    /// enclosing group's brace.
    /// </summary>
    [TestMethod]
    public void ContinuationWalksOutThroughEveryClosableProduction()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { { SELECT ?x WHERE { ?x ?p ?o } GROUP BY ?x ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.BuiltInFunctionName,
            SparqlTokenKind.AggregateFunctionName,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword,
            SparqlTokenKind.CloseBrace);
    }

    /// <summary>After an ordering condition the list may continue or close, so the caret also admits the slice clauses and the trailing data block.</summary>
    [TestMethod]
    public void AfterAnOrderConditionExpectsMoreConditionsOrTheSliceClauses()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o } ORDER BY ?s ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.AscKeyword,
            SparqlTokenKind.DescKeyword,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.BuiltInFunctionName,
            SparqlTokenKind.AggregateFunctionName,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword);
    }

    /// <summary>The ordering list needs at least one condition, so a caret before the first one expects only a condition.</summary>
    [TestMethod]
    public void BeforeTheFirstOrderConditionExpectsOnlyACondition()
    {
        CompletionContext context = DescribeAtEnd("SELECT * WHERE { ?s ?p ?o } ORDER BY ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.AscKeyword,
            SparqlTokenKind.DescKeyword,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.BuiltInFunctionName,
            SparqlTokenKind.AggregateFunctionName,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName);
    }

    /// <summary>After a grouping condition the list may continue or close, so the caret also admits the modifiers from <c>HAVING</c> onward.</summary>
    [TestMethod]
    public void AfterAGroupConditionExpectsMoreConditionsOrTheLaterModifiers()
    {
        CompletionContext context = DescribeAtEnd("SELECT ?p WHERE { ?s ?p ?o } GROUP BY ?p ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.BuiltInFunctionName,
            SparqlTokenKind.AggregateFunctionName,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword);
    }

    /// <summary>
    /// An open <c>CONSTRUCT</c> template is the caret's own production, and it closes only on its <c>}</c>,
    /// so a member position admits exactly the triple starts and that closer.
    /// </summary>
    [TestMethod]
    public void InsideAConstructTemplateExpectsExactlyTripleStartsOrClose()
    {
        CompletionContext context = DescribeAtEnd("CONSTRUCT { ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.BlankNodeLabel,
            SparqlTokenKind.AnonymousBlankNode,
            SparqlTokenKind.StringLiteral,
            SparqlTokenKind.LongStringLiteral,
            SparqlTokenKind.IntegerLiteral,
            SparqlTokenKind.DecimalLiteral,
            SparqlTokenKind.DoubleLiteral,
            SparqlTokenKind.BooleanLiteral,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.OpenBracket,
            SparqlTokenKind.OpenTripleTerm,
            SparqlTokenKind.OpenReifiedTriple,
            SparqlTokenKind.CloseBrace);
    }

    /// <summary>
    /// A closed <c>CONSTRUCT</c> template is followed by the dataset clauses and a <c>WHERE</c> clause that
    /// this form requires, so the caret admits exactly a <c>FROM</c> clause or the <c>WHERE</c> opener.
    /// </summary>
    [TestMethod]
    public void AfterAClosedConstructTemplateExpectsDatasetOrWhere()
    {
        CompletionContext context = DescribeAtEnd("CONSTRUCT { ?s ?p ?o } ");

        AssertExpectedTokens(context, SparqlTokenKind.FromKeyword, SparqlTokenKind.WhereKeyword, SparqlTokenKind.OpenBrace);
    }

    /// <summary>An open quad block is the caret's own production: a triple, a <c>GRAPH</c> group, or the closer.</summary>
    [TestMethod]
    public void InsideAnInsertDataBlockExpectsExactlyQuadStartsOrClose()
    {
        CompletionContext context = DescribeAtEnd("INSERT DATA { ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.BlankNodeLabel,
            SparqlTokenKind.AnonymousBlankNode,
            SparqlTokenKind.StringLiteral,
            SparqlTokenKind.LongStringLiteral,
            SparqlTokenKind.IntegerLiteral,
            SparqlTokenKind.DecimalLiteral,
            SparqlTokenKind.DoubleLiteral,
            SparqlTokenKind.BooleanLiteral,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.OpenBracket,
            SparqlTokenKind.OpenTripleTerm,
            SparqlTokenKind.OpenReifiedTriple,
            SparqlTokenKind.GraphKeyword,
            SparqlTokenKind.CloseBrace);
    }

    /// <summary>
    /// A closed <c>INSERT DATA</c> block ends the operation, and an update unit separates operations with
    /// <c>;</c>, so the caret admits exactly that separator.
    /// </summary>
    [TestMethod]
    public void AfterAClosedInsertDataBlockExpectsTheOperationSeparator()
    {
        CompletionContext context = DescribeAtEnd("INSERT DATA { <http://e/s> <http://e/p> <http://e/o> } ");

        AssertExpectedTokens(context, SparqlTokenKind.Semicolon);
    }

    /// <summary>A modify operation's <c>DELETE</c> template is a quad block too, so its member position reads from the same open production.</summary>
    [TestMethod]
    public void InsideAModifyDeleteTemplateExpectsQuadStartsOrClose()
    {
        CompletionContext context = DescribeAtEnd("DELETE { ");

        Assert.Contains(SparqlTokenKind.Variable, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.GraphKeyword, context.ExpectedTokens);
        Assert.Contains(SparqlTokenKind.CloseBrace, context.ExpectedTokens);
        Assert.AreEqual(ParseFrameKind.Quads, context.EnclosingProductions[^1]);
    }

    /// <summary>
    /// The target list needs at least one target, so a caret before the first one expects only a target —
    /// the <c>*</c> alternative among them, since neither branch of <c>( VarOrIri+ | '*' )</c> is chosen yet.
    /// </summary>
    [TestMethod]
    public void BeforeTheFirstDescribeTargetExpectsOnlyATarget()
    {
        CompletionContext context = DescribeAtEnd("DESCRIBE ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Star,
            SparqlTokenKind.Variable,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName);
    }

    /// <summary>
    /// After a target the list may continue or close. <c>DESCRIBE</c> alone makes its <c>WHERE</c> clause
    /// optional, so closing the list exposes not just the dataset clauses and the <c>WHERE</c> opener but
    /// every solution modifier and the trailing <c>VALUES</c> block. The <c>*</c> alternative is spent.
    /// </summary>
    [TestMethod]
    public void AfterADescribeTargetExpectsMoreTargetsOrTheOptionalTail()
    {
        CompletionContext context = DescribeAtEnd("DESCRIBE ?s ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName,
            SparqlTokenKind.FromKeyword,
            SparqlTokenKind.WhereKeyword,
            SparqlTokenKind.OpenBrace,
            SparqlTokenKind.GroupKeyword,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword);
    }

    /// <summary>
    /// The <c>*</c> alternative cannot continue, so the caret after it expects exactly the tail that follows
    /// a complete <c>DESCRIBE</c> target list.
    /// </summary>
    [TestMethod]
    public void AfterDescribeStarExpectsTheOptionalTail()
    {
        CompletionContext context = DescribeAtEnd("DESCRIBE * ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.FromKeyword,
            SparqlTokenKind.WhereKeyword,
            SparqlTokenKind.OpenBrace,
            SparqlTokenKind.GroupKeyword,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword);
    }

    /// <summary>
    /// A <c>DESCRIBE</c> dataset position carries the same optional tail: a further dataset clause, the
    /// <c>WHERE</c> opener, or — the clause being skippable — any solution modifier and the trailing data
    /// block.
    /// </summary>
    [TestMethod]
    public void AfterADescribeDatasetClauseExpectsTheOptionalTail()
    {
        CompletionContext context = DescribeAtEnd("DESCRIBE ?s FROM <http://g/> ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.FromKeyword,
            SparqlTokenKind.WhereKeyword,
            SparqlTokenKind.OpenBrace,
            SparqlTokenKind.GroupKeyword,
            SparqlTokenKind.HavingKeyword,
            SparqlTokenKind.OrderKeyword,
            SparqlTokenKind.LimitKeyword,
            SparqlTokenKind.OffsetKeyword,
            SparqlTokenKind.ValuesKeyword);
    }

    /// <summary>The grouping list needs at least one condition, so a caret before the first one expects only a condition.</summary>
    [TestMethod]
    public void BeforeTheFirstGroupConditionExpectsOnlyACondition()
    {
        CompletionContext context = DescribeAtEnd("SELECT ?p WHERE { ?s ?p ?o } GROUP BY ");

        AssertExpectedTokens(
            context,
            SparqlTokenKind.Variable,
            SparqlTokenKind.OpenParen,
            SparqlTokenKind.BuiltInFunctionName,
            SparqlTokenKind.AggregateFunctionName,
            SparqlTokenKind.Iri,
            SparqlTokenKind.PrefixedName);
    }
}
