using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AlgebraPath = Lumoin.Veritas.Sparql.Algebra.Path;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlTranslator"/>: the AST-to-algebra translation of SPARQL 1.2 §18.2 — group
/// graph patterns with filter lifting, OPTIONAL/UNION/GRAPH/MINUS/BIND, the empty group, the trailing
/// VALUES join, the query-level solution modifiers, sub-SELECT, and the aggregation translation
/// (Group/AggregateJoin, HAVING, and aggregate-to-result-variable rewriting).
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

    /// <summary>A sequence path decomposes to a Join of one-triple BGPs chained through a fresh internal join variable (§18.2.2.5); that fresh variable is not visible to SELECT *.</summary>
    [TestMethod]
    public void SequencePathDecomposesToJoinThroughFreshVariable()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p/:q ?o }", pool);

        Project project = Cast<Project>(algebra);
        Join join = Cast<Join>(project.Input);

        //?s :p V and V :q ?o, joined; the midpoint V is the same fresh variable in both triples.
        TriplePattern first = SingleTriple(Cast<Bgp>(join.Left));
        TriplePattern second = SingleTriple(Cast<Bgp>(join.Right));
        Assert.AreEqual("s", VariableName(first.Subject));
        string midpoint = VariableName(first.Object);
        Assert.AreEqual(midpoint, VariableName(second.Subject));
        Assert.AreEqual("o", VariableName(second.Object));
        Assert.StartsWith(".", midpoint, "The sequence join variable should use the collision-free internal-name prefix.");

        //The fresh midpoint is internal and stays out of SELECT *.
        AssertVariablesInOrder(project.Variables, "s", "o");
    }

    /// <summary>An inverse path swaps the endpoints: <c>?s ^:p ?o</c> becomes the BGP triple <c>?o :p ?s</c>.</summary>
    [TestMethod]
    public void InversePathSwapsEndpoints()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s ^:p ?o }", pool);

        Project project = Cast<Project>(algebra);
        TriplePattern triple = SingleTriple(Cast<Bgp>(project.Input));
        Assert.AreEqual("o", VariableName(triple.Subject));
        Assert.AreEqual("s", VariableName(triple.Object));
    }

    /// <summary>An alternative path decomposes to a Union of its branches over the same endpoints (§18.2.2.5).</summary>
    [TestMethod]
    public void AlternativePathDecomposesToUnion()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s :p|:q ?o }", pool);

        Project project = Cast<Project>(algebra);
        Union union = Cast<Union>(project.Input);

        //Each branch is the same-endpoints one-triple BGP for one alternative.
        TriplePattern left = SingleTriple(Cast<Bgp>(union.Left));
        TriplePattern right = SingleTriple(Cast<Bgp>(union.Right));
        Assert.AreEqual("s", VariableName(left.Subject));
        Assert.AreEqual("o", VariableName(left.Object));
        Assert.AreEqual("s", VariableName(right.Subject));
        Assert.AreEqual("o", VariableName(right.Object));
        AssertVariablesInOrder(project.Variables, "s", "o");
    }

    /// <summary>A negated property set is non-relational and stays an opaque Path operator (closure evaluation is an executor concern).</summary>
    [TestMethod]
    public void NegatedPropertySetStaysPath()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s !:p ?o }", pool);

        Project project = Cast<Project>(algebra);
        AlgebraPath path = Cast<AlgebraPath>(project.Input);

        Cast<PathNegatedSet>(path.PathExpression);
    }

    /// <summary>A nested sequence/inverse/alternative path decomposes into the matching Join/swap/Union tree, with closures left as Path leaves.</summary>
    [TestMethod]
    public void NestedPathDecomposesRelationalFormsAndKeepsClosureLeaf()
    {
        using Utf8StringPool pool = new();
        //(:a / ^:b) on the sequence, then | :c+ as an alternative: Union( Join(Bgp, Bgp-swapped), Path(:c+) ).
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT * WHERE { ?s (:a/^:b)|:c+ ?o }", pool);

        Project project = Cast<Project>(algebra);
        Union union = Cast<Union>(project.Input);

        //Left alternative: the :a/^:b sequence is a join of a forward and a swapped triple.
        Join sequence = Cast<Join>(union.Left);
        Cast<Bgp>(sequence.Left);
        Cast<Bgp>(sequence.Right);

        //Right alternative: :c+ is a closure that stays an opaque Path leaf.
        AlgebraPath closure = Cast<AlgebraPath>(union.Right);
        Cast<PathOneOrMore>(closure.PathExpression);
    }

    /// <summary>A long sequence path decomposes iteratively — far past the depth a recursive lowering would overflow — folding into a left-deep Join chain.</summary>
    [TestMethod]
    public void LongSequencePathDecomposesWithoutOverflow()
    {
        using Utf8StringPool pool = new();
        const int steps = 20_000;
        string path = string.Join("/", Enumerable.Range(0, steps).Select(static i => $":p{i}"));
        AlgebraOperator algebra = Translate($"PREFIX : <http://example.org/> SELECT * WHERE {{ ?s {path} ?o }}", pool);

        //The steps fold left-associatively, so the outermost operator is a Join over the last step's BGP.
        Project project = Cast<Project>(algebra);
        Join join = Cast<Join>(project.Input);
        Cast<Bgp>(join.Right);

        //Only the user endpoints survive; the 19,999 fresh midpoints are internal.
        AssertVariablesInOrder(project.Variables, "s", "o");
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

    /// <summary><c>COUNT(*)</c> with no GROUP BY triggers implicit grouping: an AggregateJoin over a Group with no keys, the count bound to a fresh result variable the projection references.</summary>
    [TestMethod]
    public void CountStarWithoutGroupByBecomesImplicitGroup()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT (COUNT(*) AS ?c) WHERE { ?s :p ?o }", pool);

        Project project = Cast<Project>(algebra);
        AssertVariablesInOrder(project.Variables, "c");

        //The (COUNT(*) AS ?c) column became an Extend binding ?c to the aggregate's fresh result variable.
        Extend extend = Cast<Extend>(project.Input);
        Assert.AreEqual("c", extend.Variable.Name.ToString());
        VariableExpression bound = Cast<VariableExpression>(extend.Expression);
        Assert.AreEqual(".agg0", bound.Variable.Name.ToString());

        AggregateJoin aggregateJoin = Cast<AggregateJoin>(extend.Input);
        Group group = Cast<Group>(aggregateJoin.Input);
        Assert.IsEmpty(group.Keys);
        Cast<Bgp>(group.Input);

        AggregateBinding binding = Assert.ContainsSingle(aggregateJoin.Aggregations);
        Assert.AreEqual(".agg0", binding.Variable.Name.ToString());
        BuiltInAggregateExpression aggregate = Cast<BuiltInAggregateExpression>(binding.Aggregate);
        Assert.AreEqual(AggregateFunction.Count, aggregate.Function);
        Assert.IsTrue(aggregate.IsCountStar);
    }

    /// <summary>An explicit GROUP BY groups by its keys; the aggregate result and the grouping key are both visible.</summary>
    [TestMethod]
    public void GroupByGroupsByItsKeys()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s (COUNT(?o) AS ?c) WHERE { ?s :p ?o } GROUP BY ?s", pool);

        Project project = Cast<Project>(algebra);
        AssertVariablesInOrder(project.Variables, "s", "c");

        Extend extend = Cast<Extend>(project.Input);
        AggregateJoin aggregateJoin = Cast<AggregateJoin>(extend.Input);
        Group group = Cast<Group>(aggregateJoin.Input);

        GroupCondition key = Assert.ContainsSingle(group.Keys);
        GroupVariable groupVariable = Cast<GroupVariable>(key);
        Assert.AreEqual("s", groupVariable.Variable.Name.ToString());

        AggregateBinding binding = Assert.ContainsSingle(aggregateJoin.Aggregations);
        BuiltInAggregateExpression aggregate = Cast<BuiltInAggregateExpression>(binding.Aggregate);
        Assert.AreEqual(AggregateFunction.Count, aggregate.Function);
        Assert.IsFalse(aggregate.IsCountStar);
        Cast<VariableExpression>(aggregate.Argument!);
    }

    /// <summary>An aggregate in HAVING becomes a Filter over the AggregateJoin, its aggregate rewritten to the result variable.</summary>
    [TestMethod]
    public void AggregateInHavingBecomesFilterOverAggregateJoin()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s WHERE { ?s :p ?o } GROUP BY ?s HAVING(COUNT(?o) > 1)", pool);

        Project project = Cast<Project>(algebra);
        AssertVariablesInOrder(project.Variables, "s");

        //HAVING constrains the grouped solutions, so the Filter sits above the AggregateJoin.
        Filter filter = Cast<Filter>(project.Input);
        ComparisonExpression comparison = Cast<ComparisonExpression>(filter.Condition);
        VariableExpression left = Cast<VariableExpression>(comparison.Left);
        Assert.AreEqual(".agg0", left.Variable.Name.ToString());

        AggregateJoin aggregateJoin = Cast<AggregateJoin>(filter.Input);
        Cast<Group>(aggregateJoin.Input);
        Assert.ContainsSingle(aggregateJoin.Aggregations);
    }

    /// <summary>An aggregate nested inside a SELECT-expression arithmetic is replaced in place by its result variable; one aggregate is computed.</summary>
    [TestMethod]
    public void AggregateInsideArithmeticExpressionIsReplacedInPlace()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT (COUNT(?o) + 1 AS ?c) WHERE { ?s :p ?o }", pool);

        Project project = Cast<Project>(algebra);
        Extend extend = Cast<Extend>(project.Input);

        //The COUNT(?o) leaf inside the addition became a reference to its result variable; the addition stands.
        ArithmeticExpression arithmetic = Cast<ArithmeticExpression>(extend.Expression);
        Assert.AreEqual(ArithmeticOp.Add, arithmetic.Op);
        VariableExpression left = Cast<VariableExpression>(arithmetic.Left);
        Assert.AreEqual(".agg0", left.Variable.Name.ToString());

        AggregateJoin aggregateJoin = Cast<AggregateJoin>(extend.Input);
        Assert.ContainsSingle(aggregateJoin.Aggregations);
    }

    /// <summary>A <c>GROUP BY (expr AS ?v)</c> key carries the named binding into the Group.</summary>
    [TestMethod]
    public void GroupByExpressionAsKeyCarriesTheName()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?d (COUNT(?o) AS ?c) WHERE { ?s :p ?o } GROUP BY (?s AS ?d)", pool);

        Project project = Cast<Project>(algebra);
        AssertVariablesInOrder(project.Variables, "d", "c");

        Extend extend = Cast<Extend>(project.Input);
        AggregateJoin aggregateJoin = Cast<AggregateJoin>(extend.Input);
        Group group = Cast<Group>(aggregateJoin.Input);

        GroupExpressionAs key = Cast<GroupExpressionAs>(Assert.ContainsSingle(group.Keys));
        Assert.AreEqual("d", key.AsVariable.Name.ToString());
    }

    /// <summary>The repeated aggregate <c>COUNT(?o)</c> is deduplicated by value: one binding feeds both the projection and HAVING.</summary>
    [TestMethod]
    public void RepeatedAggregateIsComputedOnce()
    {
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("PREFIX : <http://example.org/> SELECT ?s (COUNT(?o) AS ?c) WHERE { ?s :p ?o } GROUP BY ?s HAVING(COUNT(?o) > 1)", pool);

        //The SELECT-expression Extend is applied after the HAVING filter (§18.2.5), so Extend wraps the Filter.
        Project project = Cast<Project>(algebra);
        Extend extend = Cast<Extend>(project.Input);
        Filter filter = Cast<Filter>(extend.Input);
        AggregateJoin aggregateJoin = Cast<AggregateJoin>(filter.Input);

        //COUNT(?o) appears in both the projection and HAVING but is one aggregate, so one binding, one variable.
        AggregateBinding binding = Assert.ContainsSingle(aggregateJoin.Aggregations);
        VariableExpression projected = Cast<VariableExpression>(extend.Expression);
        VariableExpression having = Cast<VariableExpression>(Cast<ComparisonExpression>(filter.Condition).Left);
        Assert.AreEqual(binding.Variable.Name.ToString(), projected.Variable.Name.ToString());
        Assert.AreEqual(binding.Variable.Name.ToString(), having.Variable.Name.ToString());
    }

    /// <summary>A deeply nested SELECT-expression holding an aggregate rewrites iteratively — far past the depth a recursive expression transform would overflow.</summary>
    [TestMethod]
    public void DeeplyNestedAggregateExpressionRewritesWithoutOverflow()
    {
        using Utf8StringPool pool = new();
        const int depth = 50_000;
        string query = "PREFIX : <http://example.org/> SELECT (COUNT(?o)" + string.Concat(Enumerable.Repeat(" + 1", depth)) + " AS ?c) WHERE { ?s :p ?o }";

        AlgebraOperator algebra = Translate(query, pool);

        //The aggregate at the bottom of the left-deep addition still becomes the single computed binding.
        Project project = Cast<Project>(algebra);
        Extend extend = Cast<Extend>(project.Input);
        AggregateJoin aggregateJoin = Cast<AggregateJoin>(extend.Input);
        Assert.ContainsSingle(aggregateJoin.Aggregations);
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

    /// <summary>Asserts a BGP holds exactly one triple and returns it.</summary>
    /// <param name="bgp">The BGP to inspect.</param>
    /// <returns>The BGP's single triple pattern.</returns>
    private static TriplePattern SingleTriple(Bgp bgp)
    {
        Assert.HasCount(1, bgp.Patterns);

        return bgp.Patterns[0];
    }

    /// <summary>Returns the name of a variable term, asserting the term is a variable.</summary>
    /// <param name="term">The term, expected to be a <see cref="VariableTerm"/>.</param>
    /// <returns>The variable's name.</returns>
    private static string VariableName(TriplePatternTerm term)
    {
        return Cast<VariableTerm>(term).Variable.Name.ToString();
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

        Assert.AreSequenceEqual(expected, names, $"Expected variables [{string.Join(", ", expected)}] but got [{string.Join(", ", names)}].");
    }
}
