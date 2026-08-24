using System.Collections.Generic;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AstTripleTerm = Lumoin.Veritas.Sparql.Ast.TripleTerm;
using NamedNode = Lumoin.Veritas.Core.NamedNode;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for the <see cref="AlgebraOperator"/> hierarchy: the base's computed <c>Children</c> (uniform
/// traversal surface) and <c>OutputVariables</c> (scope) members, their lazy caching, and the operators'
/// per-shape implementations.
/// </summary>
[TestClass]
internal sealed class SparqlAlgebraTests
{
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A BGP has no child operators and collects every variable across its patterns, recursing into triple terms.</summary>
    [TestMethod]
    public void BgpHasNoChildrenAndCollectsVariables()
    {
        Bgp bgp = new(
        [
            Triple(Var("s"), Var("p"), Var("o")),
            Triple(Var("s"), Iri("name"), new AstTripleTerm(default, Triple(Var("a"), Iri("b"), Var("c")))),
        ]);

        Assert.IsEmpty(bgp.Children);
        AssertVariables(bgp.OutputVariables, "s", "p", "o", "a", "c");
    }

    /// <summary>A join exposes both operands as children and outputs the union of their variables.</summary>
    [TestMethod]
    public void JoinExposesBothChildrenAndUnionsVariables()
    {
        Bgp left = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Bgp right = new([Triple(Var("o"), Iri("q"), Var("z"))]);
        Join join = new(left, right);

        Assert.AreSequenceEqual(new List<AlgebraOperator> { left, right }, new List<AlgebraOperator>(join.Children));
        AssertVariables(join.OutputVariables, "s", "o", "z");
    }

    /// <summary>An OPTIONAL exposes both operands as children.</summary>
    [TestMethod]
    public void LeftJoinExposesBothChildren()
    {
        Bgp left = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Bgp right = new([Triple(Var("s"), Iri("q"), Var("z"))]);
        LeftJoin leftJoin = new(left, right, Condition: null);

        Assert.HasCount(2, leftJoin.Children);
        AssertVariables(leftJoin.OutputVariables, "s", "o", "z");
    }

    /// <summary>MINUS outputs only the left operand's variables; the right's are used for compatibility, not binding.</summary>
    [TestMethod]
    public void MinusOutputsOnlyLeftVariables()
    {
        Bgp left = new([Triple(Var("s"), Iri("p"), Var("x"))]);
        Bgp right = new([Triple(Var("s"), Iri("q"), Var("y"))]);
        Minus minus = new(left, right);

        AssertVariables(minus.OutputVariables, "s", "x");
    }

    /// <summary>A GRAPH with a variable designator binds that variable; an IRI designator adds none.</summary>
    [TestMethod]
    public void GraphVariableDesignatorIsBound()
    {
        Bgp inner = new([Triple(Var("s"), Iri("p"), Var("o"))]);

        Graph variableGraph = new(new GraphVariableTerm(default, new SparqlVariable(Utf8Strings.From("g"))), inner);
        AssertVariables(variableGraph.OutputVariables, "s", "o", "g");

        Graph iriGraph = new(new GraphIriTerm(default, new IriRef(Utf8Strings.From(Ex + "g"), default)), inner);
        AssertVariables(iriGraph.OutputVariables, "s", "o");
    }

    /// <summary>A BIND (Extend) adds the bound variable to the input's output variables.</summary>
    [TestMethod]
    public void ExtendAddsBoundVariable()
    {
        Bgp inner = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Extend extend = new(inner, new SparqlVariable(Utf8Strings.From("label")), new VariableExpression(default, new SparqlVariable(Utf8Strings.From("o"))));

        AssertVariables(extend.OutputVariables, "s", "o", "label");
    }

    /// <summary>A projection restricts the output variables to the projected set.</summary>
    [TestMethod]
    public void ProjectRestrictsVariables()
    {
        Bgp inner = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Project project = new(inner, [new SparqlVariable(Utf8Strings.From("s"))]);

        AssertVariables(project.OutputVariables, "s");
    }

    /// <summary>The computed members are cached: repeated access returns the same instance.</summary>
    [TestMethod]
    public void ComputedMembersAreCached()
    {
        Bgp left = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Bgp right = new([Triple(Var("o"), Iri("q"), Var("z"))]);
        Join join = new(left, right);

        IReadOnlyList<AlgebraOperator> childrenFirst = join.Children;
        IReadOnlyList<AlgebraOperator> childrenSecond = join.Children;

        Assert.AreSame(childrenFirst, childrenSecond, "Children should be cached.");

        IReadOnlySet<SparqlVariable> outputVariablesFirst = join.OutputVariables;
        IReadOnlySet<SparqlVariable> outputVariablesSecond = join.OutputVariables;

        Assert.AreSame(outputVariablesFirst, outputVariablesSecond, "OutputVariables should be cached.");
    }

    /// <summary>
    /// Record value equality survives traversal: caching the computed members of one instance does not
    /// make it unequal to an identical untouched instance. This is the reason the cache is held off-instance
    /// (in a ConditionalWeakTable) rather than in record fields.
    /// </summary>
    [TestMethod]
    public void ValueEqualityHoldsAfterTraversal()
    {
        UnitTable traversed = new();
        UnitTable fresh = new();

        Assert.AreEqual(traversed, fresh);

        //Populate the off-instance cache on one of them.
        _ = traversed.Children;
        _ = traversed.OutputVariables;

        Assert.AreEqual(traversed, fresh);
    }

    /// <summary>A Group exposes its named grouping keys; a bare grouping expression names nothing.</summary>
    [TestMethod]
    public void GroupExposesKeyVariables()
    {
        Bgp inner = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Group group = new([new GroupVariable(default, new SparqlVariable(Utf8Strings.From("o")))], inner);

        Assert.HasCount(1, group.Children);
        AssertVariables(group.OutputVariables, "o");
    }

    /// <summary>An AggregateJoin exposes the grouping keys plus each aggregate's result variable.</summary>
    [TestMethod]
    public void AggregateJoinExposesKeysAndAggregateVariables()
    {
        Bgp inner = new([Triple(Var("s"), Iri("p"), Var("o"))]);
        Group group = new([new GroupVariable(default, new SparqlVariable(Utf8Strings.From("o")))], inner);
        BuiltInAggregateExpression countStar = new(default, AggregateFunction.Count, Argument: null, IsDistinct: false, IsCountStar: true, GroupConcatSeparator: null);
        AggregateJoin aggregateJoin = new(group, [new AggregateBinding(new SparqlVariable(Utf8Strings.From("c")), countStar)]);

        Assert.AreSequenceEqual(new List<AlgebraOperator> { group }, new List<AlgebraOperator>(aggregateJoin.Children));
        AssertVariables(aggregateJoin.OutputVariables, "o", "c");
    }

    /// <summary>OutputVariables on a tree far deeper than the call stack tolerates is computed bottom-up without overflowing.</summary>
    [TestMethod]
    public void OutputVariablesOnDeepTreeDoesNotOverflow()
    {
        //Every level binds the same two variables, so each node's union stays O(1) and the test is fast; the
        //point is depth, which a recursive ComputeOutputVariables (reading children's OutputVariables) would
        //overflow on.
        const int depth = 50_000;
        AlgebraOperator tree = new Bgp([Triple(Var("s"), Iri("p"), Var("o"))]);
        for(int i = 0; i < depth; i++)
        {
            tree = new Join(tree, new Bgp([Triple(Var("s"), Iri("p"), Var("o"))]));
        }

        AssertVariables(tree.OutputVariables, "s", "o");
    }

    private static VariableTerm Var(string name)
    {
        return new VariableTerm(default, new SparqlVariable(Utf8Strings.From(name)));
    }

    private static ConstantTerm Iri(string local)
    {
        return new ConstantTerm(default, new NamedNode(Utf8Strings.From(Ex + local)));
    }

    private static TriplePattern Triple(TriplePatternTerm subject, TriplePatternTerm predicate, TriplePatternTerm @object)
    {
        return new TriplePattern(default, subject, predicate, @object);
    }

    private static void AssertVariables(IReadOnlySet<SparqlVariable> actual, params string[] expected)
    {
        Assert.HasCount(expected.Length, actual);
        foreach(string name in expected)
        {
            Assert.Contains(new SparqlVariable(Utf8Strings.From(name)), actual, $"Expected variable ?{name} in the output set.");
        }
    }
}
