using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// The row-level hash-join index's contract: every build solution carrying a probe's join-variable values is
/// reachable by chain walk exactly once, single- and two-variable keys both index correctly, and a probe with no
/// matching key yields no match — the allocation-lean structure behind the engine's shared-variable join.
/// </summary>
[TestClass]
internal sealed class SolutionHashJoinIndexTests
{
    /// <summary>The build rows expected for the single-variable probe (?s = :a), in ascending order.</summary>
    private static int[] SingleVariableMatches { get; } = [0, 2, 3];

    /// <summary>The lone build row expected for a probe matching exactly one row.</summary>
    private static int[] SingleRowMatch { get; } = [1];

    /// <summary>The build rows expected for the two-variable probe ((:a, :knows)), in ascending order.</summary>
    private static int[] TwoVariableMatches { get; } = [0, 2];

    /// <summary>Builds a solution over the given variable-to-IRI bindings.</summary>
    /// <param name="bindings">The (variable name, IRI) pairs.</param>
    /// <returns>The solution.</returns>
    private static SparqlSolution Solution(params (string Variable, string Iri)[] bindings)
    {
        List<SparqlBinding> list = [];
        foreach((string variable, string iri) in bindings)
        {
            list.Add(new SparqlBinding(new SparqlVariable(Utf8Strings.From(variable)), new NamedNode(Utf8Strings.From(iri))));
        }

        return new SparqlSolution(list);
    }

    /// <summary>Collects every build row matching a probe, by chain walk.</summary>
    /// <param name="index">The index.</param>
    /// <param name="probe">The probe solution.</param>
    /// <returns>The matching row ids in chain order.</returns>
    private static List<int> MatchesOf(SolutionHashJoinIndex index, SparqlSolution probe)
    {
        List<int> matches = [];
        for(int rowId = index.FirstMatch(probe); rowId >= 0; rowId = index.NextMatch(rowId))
        {
            matches.Add(rowId);
        }

        return matches;
    }

    /// <summary>A single-variable key reaches every build row sharing the probe's value, and only those rows.</summary>
    [TestMethod]
    public void SingleVariableKeyIndexesEveryMatchingRow()
    {
        SparqlVariable[] joinVariables = [new SparqlVariable(Utf8Strings.From("s"))];

        //Rows 0, 2, 3 share ?s = :a; row 1 has ?s = :b.
        SolutionHashJoinIndex index = SolutionHashJoinIndex.Build(
            [
                Solution(("s", "http://x/a"), ("o", "http://x/0")),
                Solution(("s", "http://x/b"), ("o", "http://x/1")),
                Solution(("s", "http://x/a"), ("o", "http://x/2")),
                Solution(("s", "http://x/a"), ("o", "http://x/3")),
            ],
            joinVariables);

        List<int> matches = MatchesOf(index, Solution(("s", "http://x/a"), ("z", "http://x/9")));
        matches.Sort();
        Assert.AreSequenceEqual(SingleVariableMatches, matches);

        Assert.AreSequenceEqual(SingleRowMatch, MatchesOf(index, Solution(("s", "http://x/b"))));
        Assert.IsEmpty(MatchesOf(index, Solution(("s", "http://x/missing"))));
    }

    /// <summary>A two-variable key matches only build rows agreeing on both join values.</summary>
    [TestMethod]
    public void TwoVariableKeyMatchesOnlyOnBothValues()
    {
        SparqlVariable[] joinVariables = [new SparqlVariable(Utf8Strings.From("s")), new SparqlVariable(Utf8Strings.From("p"))];

        SolutionHashJoinIndex index = SolutionHashJoinIndex.Build(
            [
                Solution(("s", "http://x/a"), ("p", "http://x/knows")),
                Solution(("s", "http://x/a"), ("p", "http://x/likes")),
                Solution(("s", "http://x/a"), ("p", "http://x/knows")),
            ],
            joinVariables);

        //Same ?s but different ?p must not match the (:a, :knows) rows.
        List<int> matches = MatchesOf(index, Solution(("s", "http://x/a"), ("p", "http://x/knows")));
        matches.Sort();
        Assert.AreSequenceEqual(TwoVariableMatches, matches);

        Assert.AreSequenceEqual(SingleRowMatch, MatchesOf(index, Solution(("s", "http://x/a"), ("p", "http://x/likes"))));
        Assert.IsEmpty(MatchesOf(index, Solution(("s", "http://x/a"), ("p", "http://x/hates"))));
    }
}
