using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AlgebraPath = Lumoin.Veritas.Sparql.Algebra.Path;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlTranslator"/>: the AST-to-algebra translation of SPARQL 1.2 §18.2 — group
/// graph patterns with filter lifting, OPTIONAL/UNION/GRAPH/MINUS/BIND, the empty group, the trailing
/// VALUES join, and the query-level solution modifiers — plus the deferral of aggregation and sub-SELECT.
/// </summary>
[TestClass]
internal sealed class SparqlTranslatorTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A single basic graph pattern translates to a projection over a BGP holding its triples.</summary>
    [TestMethod]
    public void SingleBasicGraphPatternBecomesProjectOverBgp()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o . ?o :q ?z }", pool);

        Project project = Cast<Project>(algebra);
        Bgp bgp = Cast<Bgp>(project.Input);

        //The two consecutive triples form one BGP; SELECT * projects every variable in source order.
        Assert.HasCount(2, bgp.Patterns);
        AssertVariablesInOrder(project.Variables, "s", "o", "z");
    }

    /// <summary>A FILTER member is lifted to constrain the whole group, wrapping the group's algebra.</summary>
    [TestMethod]
    public void FilterMemberWrapsTheGroup()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o FILTER(?o > 5) }", pool);

        Project project = Cast<Project>(algebra);
        Filter filter = Cast<Filter>(project.Input);

        Cast<Bgp>(filter.Input);
        Cast<ComparisonExpression>(filter.Condition);
    }

    /// <summary>Two FILTERs in one group are conjoined into a single Filter over the group.</summary>
    [TestMethod]
    public void MultipleFiltersAreConjoined()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o FILTER(?o > 1) FILTER(?o < 9) }", pool);

        Project project = Cast<Project>(algebra);
        Filter filter = Cast<Filter>(project.Input);

        Cast<AndExpression>(filter.Condition);
    }

    /// <summary>An OPTIONAL member translates to a LeftJoin with no condition.</summary>
    [TestMethod]
    public void OptionalBecomesLeftJoin()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o OPTIONAL { ?s :q ?v } }", pool);

        Project project = Cast<Project>(algebra);
        LeftJoin leftJoin = Cast<LeftJoin>(project.Input);

        Cast<Bgp>(leftJoin.Left);
        Cast<Bgp>(leftJoin.Right);
        Assert.IsNull(leftJoin.Condition);
    }

    /// <summary>A FILTER at the top of an OPTIONAL's pattern is lifted to the LeftJoin's condition (§18.2.2.6).</summary>
    [TestMethod]
    public void FilterInsideOptionalIsLiftedToTheJoinCondition()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o OPTIONAL { ?s :q ?v FILTER(?v > 5) } }", pool);

        Project project = Cast<Project>(algebra);
        LeftJoin leftJoin = Cast<LeftJoin>(project.Input);

        //The inner filter became the join condition; the optional's right operand is the bare inner BGP.
        Cast<Bgp>(leftJoin.Right);
        Assert.IsNotNull(leftJoin.Condition);
        Cast<ComparisonExpression>(leftJoin.Condition);
    }

    /// <summary>A UNION member translates to a Union of its two alternatives.</summary>
    [TestMethod]
    public void UnionBecomesUnionOperator()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { { ?s :p ?o } UNION { ?s :q ?v } }", pool);

        Project project = Cast<Project>(algebra);
        Union union = Cast<Union>(project.Input);

        Cast<Bgp>(union.Left);
        Cast<Bgp>(union.Right);
    }

    /// <summary>A MINUS member translates to a Minus, and SELECT * projects only the left operand's variables.</summary>
    [TestMethod]
    public void MinusBecomesMinusAndProjectsOnlyLeftVariables()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o MINUS { ?s :q ?v } }", pool);

        Project project = Cast<Project>(algebra);
        Minus minus = Cast<Minus>(project.Input);

        Cast<Bgp>(minus.Left);
        Cast<Bgp>(minus.Right);

        //?v lives only on the right of the MINUS and is therefore out of scope for SELECT *.
        AssertVariablesInOrder(project.Variables, "s", "o");
    }

    /// <summary>A BIND member translates to an Extend, and the bound variable is visible to SELECT *.</summary>
    [TestMethod]
    public void BindBecomesExtend()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o BIND(?o AS ?x) }", pool);

        Project project = Cast<Project>(algebra);
        Extend extend = Cast<Extend>(project.Input);

        Cast<Bgp>(extend.Input);
        Assert.AreEqual("x", extend.Variable.Name.ToString());
        AssertVariablesInOrder(project.Variables, "s", "o", "x");
    }

    /// <summary>A GRAPH member translates to a Graph, and a variable designator is visible to SELECT *.</summary>
    [TestMethod]
    public void GraphBecomesGraphOperator()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { GRAPH ?g { ?s :p ?o } }", pool);

        Project project = Cast<Project>(algebra);
        Graph graph = Cast<Graph>(project.Input);

        Cast<GraphVariableTerm>(graph.Designator);
        Cast<Bgp>(graph.Input);
        AssertVariablesInOrder(project.Variables, "s", "o", "g");
    }

    /// <summary>A triple with a complex property-path predicate becomes a Path operator, not a BGP triple.</summary>
    [TestMethod]
    public void ComplexPathBecomesPathOperator()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p+ ?o }", pool);

        Project project = Cast<Project>(algebra);
        AlgebraPath path = Cast<AlgebraPath>(project.Input);

        Cast<PathOneOrMore>(path.PathExpression);
        AssertVariablesInOrder(project.Variables, "s", "o");
    }

    /// <summary>A sequence path is carried whole into the Path operator; its decomposition is a later slice.</summary>
    [TestMethod]
    public void SequencePathIsCarriedWholeIntoPath()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p/:q ?o }", pool);

        Project project = Cast<Project>(algebra);
        AlgebraPath path = Cast<AlgebraPath>(project.Input);

        Cast<PathSequence>(path.PathExpression);
    }

    /// <summary>Plain triples and a path triple in one block join as BGP runs and a Path, in source order.</summary>
    [TestMethod]
    public void MixedPlainAndPathTriplesJoinInOrder()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o . ?s :q+ ?x }", pool);

        Project project = Cast<Project>(algebra);
        Join join = Cast<Join>(project.Input);

        //The leading plain triple forms the BGP; the path triple is the Path, joined after it.
        Cast<Bgp>(join.Left);
        Cast<AlgebraPath>(join.Right);
        AssertVariablesInOrder(project.Variables, "s", "o", "x");
    }

    /// <summary>An empty group <c>{}</c> translates to the unit table (the join identity).</summary>
    [TestMethod]
    public void EmptyGroupBecomesUnitTable()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("ASK {}", pool);

        Cast<UnitTable>(algebra);
    }

    /// <summary>The solution modifiers nest as Slice(Distinct(Project(OrderBy(...)))) in §18.2.5 order.</summary>
    [TestMethod]
    public void SolutionModifiersNestInSpecOrder()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT DISTINCT ?s WHERE { ?s :p ?o } ORDER BY ?s LIMIT 10 OFFSET 5", pool);

        Slice slice = Cast<Slice>(algebra);
        Assert.AreEqual(5, slice.Offset);
        Assert.AreEqual(10, slice.Limit);

        Distinct distinct = Cast<Distinct>(slice.Input);
        Project project = Cast<Project>(distinct.Input);
        AssertVariablesInOrder(project.Variables, "s");

        OrderBy orderBy = Cast<OrderBy>(project.Input);
        Cast<Bgp>(orderBy.Input);
    }

    /// <summary>A SELECT-expression column becomes an Extend below the projection, which keeps the alias.</summary>
    [TestMethod]
    public void SelectExpressionBecomesExtendUnderProjection()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s (?o AS ?x) WHERE { ?s :p ?o }", pool);

        Project project = Cast<Project>(algebra);
        AssertVariablesInOrder(project.Variables, "s", "x");

        Extend extend = Cast<Extend>(project.Input);
        Assert.AreEqual("x", extend.Variable.Name.ToString());
    }

    /// <summary>ASK has no projection: its algebra is the bare pattern.</summary>
    [TestMethod]
    public void AskHasNoProjection()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> ASK { ?s :p ?o }", pool);

        Cast<Bgp>(algebra);
    }

    /// <summary>A trailing VALUES block joins the pattern result.</summary>
    [TestMethod]
    public void TrailingValuesJoinsThePattern()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p ?o } VALUES ?x { 1 2 }", pool);

        Project project = Cast<Project>(algebra);
        Join join = Cast<Join>(project.Input);

        //One operand is the pattern BGP, the other the inline VALUES table.
        bool hasBgp = join.Left is Bgp || join.Right is Bgp;
        bool hasTable = join.Left is Table || join.Right is Table;
        Assert.IsTrue(hasBgp, "Expected a BGP operand in the VALUES join.");
        Assert.IsTrue(hasTable, "Expected a VALUES Table operand in the VALUES join.");
        AssertVariablesInOrder(project.Variables, "s", "o", "x");
    }

    /// <summary>CONSTRUCT has no projection; ORDER BY / LIMIT still shape the solution sequence.</summary>
    [TestMethod]
    public void ConstructAppliesSliceWithoutProjection()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> CONSTRUCT { ?s :p ?o } WHERE { ?s :p ?o } LIMIT 5", pool);

        Slice slice = Cast<Slice>(algebra);
        Assert.AreEqual(0, slice.Offset);
        Assert.AreEqual(5, slice.Limit);
        Cast<Bgp>(slice.Input);
    }

    /// <summary>Deeply nested groups parse, normalize, and translate iteratively — far past the depth a recursive pipeline would overflow.</summary>
    [TestMethod]
    public void DeeplyNestedGroupsTranslateWithoutOverflow()
    {
        using Utf8StringPool pool = new();
        const int depth = 20_000;
        string query = "PREFIX : <http://example.org/> SELECT * WHERE " + new string('{', depth) + " ?s :p ?o " + new string('}', depth);

        AlgebraOperator algebra = Translate(query, pool);

        //The nested single-member groups collapse through the join identity to a projection over the inner BGP.
        Project project = Cast<Project>(algebra);
        Cast<Bgp>(project.Input);
    }

    /// <summary>A sub-SELECT becomes a ToMultiSet over the inner query's projection.</summary>
    [TestMethod]
    public void SubSelectBecomesToMultiSetOverProjection()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { SELECT ?s WHERE { ?s :p ?o } }", pool);

        Project outer = Cast<Project>(algebra);
        AssertVariablesInOrder(outer.Variables, "s");

        ToMultiSet toMultiSet = Cast<ToMultiSet>(outer.Input);
        Project inner = Cast<Project>(toMultiSet.Input);
        AssertVariablesInOrder(inner.Variables, "s");
        Cast<Bgp>(inner.Input);
    }

    /// <summary>A sub-SELECT joins into the enclosing group, and its non-projected variables stay out of scope.</summary>
    [TestMethod]
    public void SubSelectJoinsWithOuterPatternAndCapsScope()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?a :q ?b . { SELECT ?s WHERE { ?s :p ?o } } }", pool);

        Project project = Cast<Project>(algebra);
        Join join = Cast<Join>(project.Input);
        Cast<Bgp>(join.Left);
        Cast<ToMultiSet>(join.Right);

        //?o and ?p live only inside the sub-SELECT (not projected), so SELECT * sees only ?a, ?b, and ?s.
        AssertVariablesInOrder(project.Variables, "a", "b", "s");
    }

    /// <summary>Deeply nested sub-SELECTs translate iteratively — the head/modifier application does not recurse per level.</summary>
    [TestMethod]
    public void DeeplyNestedSubSelectsTranslateWithoutOverflow()
    {
        using Utf8StringPool pool = new();
        const int depth = 2_000;
        StringBuilder builder = new("PREFIX : <http://example.org/> SELECT * WHERE ");
        for(int i = 0; i < depth; i++)
        {
            builder.Append("{ SELECT * WHERE ");
        }

        builder.Append("{ ?s :p ?o }").Append('}', depth);

        AlgebraOperator algebra = Translate(builder.ToString(), pool);

        //Each level wraps the previous in Project(ToMultiSet(...)); the outermost is a Project.
        Cast<Project>(algebra);
    }

    /// <summary>Aggregation (GROUP BY) is a deferred construct and is refused.</summary>
    [TestMethod]
    public void GroupByIsNotSupportedYet()
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = ParseAndNormalize("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o } GROUP BY ?s", pool);

        _ = Assert.Throws<NotSupportedException>(() => SparqlTranslator.Translate(query));
    }

    /// <summary>Parses, normalizes, and translates a query to its algebra.</summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping parsed and lowered handles alive for the test's duration.</param>
    /// <returns>The query's algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        return SparqlTranslator.Translate(ParseAndNormalize(text, pool));
    }

    /// <summary>Parses and normalizes a query, returning the normalized AST ready for translation.</summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <param name="pool">The pool keeping parsed and lowered handles alive for the test's duration.</param>
    /// <returns>The normalized query.</returns>
    private static SparqlQuery ParseAndNormalize(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);

        return (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());
    }

    /// <summary>Asserts a value is of the expected type and returns it cast to that type.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    private static T Cast<T>(object value)
    {
        Assert.IsInstanceOfType<T>(value);

        return (T)value;
    }

    /// <summary>Asserts a variable list equals the expected names, in order.</summary>
    /// <param name="actual">The actual variables.</param>
    /// <param name="expected">The expected variable names, in order.</param>
    private static void AssertVariablesInOrder(IReadOnlyList<SparqlVariable> actual, params string[] expected)
    {
        List<string> names = new(actual.Count);
        foreach(SparqlVariable variable in actual)
        {
            names.Add(variable.Name.ToString());
        }

        CollectionAssert.AreEqual(expected, names, $"Expected variables [{string.Join(", ", expected)}] but got [{string.Join(", ", names)}].");
    }
}
