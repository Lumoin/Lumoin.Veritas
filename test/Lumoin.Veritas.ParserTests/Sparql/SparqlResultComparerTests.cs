using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Tests for <see cref="SparqlResultComparer"/>: <c>SELECT</c> multiset (bag) equivalence under blank-node
/// isomorphism, ordered comparison for <c>ORDER BY</c> results, and <c>ASK</c> boolean comparison.
/// </summary>
[TestClass]
internal sealed class SparqlResultComparerTests
{
    /// <summary>Builds an IRI term.</summary>
    /// <param name="iri">The IRI.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string iri) => new(Utf8Strings.From(iri));

    /// <summary>Builds a blank node.</summary>
    /// <param name="label">The blank-node label.</param>
    /// <returns>The blank node.</returns>
    private static BlankNode Blank(string label) => new(Utf8Strings.From(label));

    /// <summary>Builds a solution from variable/term pairs.</summary>
    /// <param name="bindings">The bindings.</param>
    /// <returns>The solution.</returns>
    private static SparqlSolution Sol(params (string Variable, RdfTerm Term)[] bindings)
    {
        List<SparqlBinding> list = [];
        foreach((string variable, RdfTerm term) in bindings)
        {
            list.Add(new SparqlBinding(new SparqlVariable(Utf8Strings.From(variable)), term));
        }

        return new SparqlSolution(list);
    }

    /// <summary>Builds a <c>SELECT</c> result set with no declared head from its solutions.</summary>
    /// <param name="solutions">The solutions.</param>
    /// <returns>The result set.</returns>
    private static SparqlResultSet Select(params SparqlSolution[] solutions) => SparqlResultSet.ForSelect([], solutions);

    /// <summary>Two identical ground result sets are equivalent regardless of row order.</summary>
    [TestMethod]
    public void GroundBagEquivalentIgnoringOrder()
    {
        SparqlResultSet actual = Select(Sol(("x", Iri("urn:a"))), Sol(("x", Iri("urn:b"))));
        SparqlResultSet expected = Select(Sol(("x", Iri("urn:b"))), Sol(("x", Iri("urn:a"))));

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(actual, expected, ordered: false));
    }

    /// <summary>Reordered rows are equivalent unordered but not equivalent when order is significant.</summary>
    [TestMethod]
    public void OrderedComparisonRespectsRowOrder()
    {
        SparqlResultSet actual = Select(Sol(("x", Iri("urn:a"))), Sol(("x", Iri("urn:b"))));
        SparqlResultSet reordered = Select(Sol(("x", Iri("urn:b"))), Sol(("x", Iri("urn:a"))));

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(actual, reordered, ordered: false));
        Assert.IsFalse(SparqlResultComparer.AreEquivalent(actual, reordered, ordered: true));
    }

    /// <summary>Row multiplicity is significant: a duplicated row does not match a single occurrence.</summary>
    [TestMethod]
    public void BagMultiplicityIsSignificant()
    {
        SparqlResultSet twice = Select(Sol(("x", Iri("urn:a"))), Sol(("x", Iri("urn:a"))));
        SparqlResultSet onceAndOther = Select(Sol(("x", Iri("urn:a"))), Sol(("x", Iri("urn:b"))));

        Assert.IsFalse(SparqlResultComparer.AreEquivalent(twice, onceAndOther, ordered: false));
    }

    /// <summary>A differing count is never equivalent.</summary>
    [TestMethod]
    public void DifferentCountNotEquivalent()
    {
        SparqlResultSet one = Select(Sol(("x", Iri("urn:a"))));
        SparqlResultSet two = Select(Sol(("x", Iri("urn:a"))), Sol(("x", Iri("urn:b"))));

        Assert.IsFalse(SparqlResultComparer.AreEquivalent(one, two, ordered: false));
    }

    /// <summary>Result sets that differ only by a consistent blank-node relabelling are equivalent.</summary>
    [TestMethod]
    public void BlankNodeIsomorphicEquivalent()
    {
        SparqlResultSet actual = Select(Sol(("x", Blank("a"))), Sol(("x", Blank("b"))));
        SparqlResultSet expected = Select(Sol(("x", Blank("p"))), Sol(("x", Blank("q"))));

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(actual, expected, ordered: false));
    }

    /// <summary>A blank node shared across rows cannot match two distinct expected blank nodes.</summary>
    [TestMethod]
    public void BlankNodeNonIsomorphicNotEquivalent()
    {
        SparqlResultSet shared = Select(Sol(("x", Blank("a"))), Sol(("x", Blank("a"))));
        SparqlResultSet distinct = Select(Sol(("x", Blank("p"))), Sol(("x", Blank("q"))));

        Assert.IsFalse(SparqlResultComparer.AreEquivalent(shared, distinct, ordered: false));
    }

    /// <summary>A blank node correlated across two variables within a row must map consistently.</summary>
    [TestMethod]
    public void CorrelatedBlankNodeAcrossVariables()
    {
        SparqlResultSet correlated = Select(Sol(("x", Blank("a")), ("y", Blank("a"))));
        SparqlResultSet alsoCorrelated = Select(Sol(("x", Blank("z")), ("y", Blank("z"))));
        SparqlResultSet uncorrelated = Select(Sol(("x", Blank("z")), ("y", Blank("w"))));

        Assert.IsTrue(SparqlResultComparer.AreEquivalent(correlated, alsoCorrelated, ordered: false));
        Assert.IsFalse(SparqlResultComparer.AreEquivalent(correlated, uncorrelated, ordered: false));
    }

    /// <summary><c>ASK</c> results compare by their boolean.</summary>
    [TestMethod]
    public void AskComparesByBoolean()
    {
        Assert.IsTrue(SparqlResultComparer.AreEquivalent(SparqlResultSet.ForAsk(true), SparqlResultSet.ForAsk(true), ordered: false));
        Assert.IsFalse(SparqlResultComparer.AreEquivalent(SparqlResultSet.ForAsk(true), SparqlResultSet.ForAsk(false), ordered: false));
    }
}
