using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SolutionBlankNodeScope"/> — the per-solution blank-node identity scope SPARQL <c>BNODE</c>
/// (§17.4.2.3) builds against, and the substrate SPARQL Update's <c>INSERT … WHERE</c> reuses for per-solution
/// template blank nodes: keyless allocation is always fresh, a key correlates within one solution, and a derived
/// (extended) solution keeps its parent's correlation.
/// </summary>
[TestClass]
internal sealed class SolutionBlankNodeScopeTests
{
    /// <summary>Builds an (empty) solution to act as a correlation key.</summary>
    /// <returns>The solution.</returns>
    private static SparqlSolution NewSolution() => new(new List<SparqlBinding>());

    /// <summary>A correlation key from its text.</summary>
    /// <param name="text">The key text.</param>
    /// <returns>The key bytes.</returns>
    private static Utf8String Key(string text) => Utf8Strings.From(text);

    [TestMethod]
    public void FreshYieldsADistinctBlankNodePerCall()
    {
        using Utf8StringPool pool = new();
        SolutionBlankNodeScope scope = new(VeritasBlankNodes.System, pool);

        BlankNode first = scope.Fresh();
        BlankNode second = scope.Fresh();

        Assert.AreNotEqual(first.Label, second.Label, "Each BNODE() call must mint a distinct blank node.");
    }

    [TestMethod]
    public void CorrelatedReturnsTheSameBlankNodeForTheSameKeyWithinASolution()
    {
        using Utf8StringPool pool = new();
        SolutionBlankNodeScope scope = new(VeritasBlankNodes.System, pool);
        SparqlSolution solution = NewSolution();

        BlankNode first = scope.Correlated(solution, Key("a"));
        BlankNode second = scope.Correlated(solution, Key("a"));

        Assert.AreEqual(first.Label, second.Label, "The same BNODE key within one solution must correlate to one blank node.");
    }

    [TestMethod]
    public void CorrelatedReturnsDistinctBlankNodesForDistinctKeysWithinASolution()
    {
        using Utf8StringPool pool = new();
        SolutionBlankNodeScope scope = new(VeritasBlankNodes.System, pool);
        SparqlSolution solution = NewSolution();

        BlankNode a = scope.Correlated(solution, Key("a"));
        BlankNode b = scope.Correlated(solution, Key("b"));

        Assert.AreNotEqual(a.Label, b.Label, "Distinct BNODE keys must correlate to distinct blank nodes.");
    }

    [TestMethod]
    public void CorrelatedReturnsDistinctBlankNodesForTheSameKeyAcrossSolutions()
    {
        using Utf8StringPool pool = new();
        SolutionBlankNodeScope scope = new(VeritasBlankNodes.System, pool);
        SparqlSolution first = NewSolution();
        SparqlSolution second = NewSolution();

        BlankNode inFirst = scope.Correlated(first, Key("a"));
        BlankNode inSecond = scope.Correlated(second, Key("a"));

        Assert.AreNotEqual(inFirst.Label, inSecond.Label, "The same BNODE key in different solutions must yield different blank nodes.");
    }

    [TestMethod]
    public void LinkMakesADerivedSolutionShareItsParentsCorrelation()
    {
        using Utf8StringPool pool = new();
        SolutionBlankNodeScope scope = new(VeritasBlankNodes.System, pool);
        SparqlSolution parent = NewSolution();
        SparqlSolution child = NewSolution();

        BlankNode inParent = scope.Correlated(parent, Key("a"));
        scope.Link(parent, child);
        BlankNode inChild = scope.Correlated(child, Key("a"));

        Assert.AreEqual(inParent.Label, inChild.Label, "A solution extended (Link) from a parent must keep the parent's BNODE correlation across the chain.");
    }
}
