using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl.Datatypes;
using Lumoin.Veritas.Owl.Structural;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// The datatype-registry stage-D consult points at CHECKER level: the whole-domain-branch split
/// (a negated declarative registered type is a modelled non-covering removal; a negated delegate-backed
/// type stays unmodelled and abstains), and the registered-membership consult that decides an enumeration
/// against a registered value space. Each row carries its certified battery id.
/// </summary>
[TestClass]
internal sealed class DatatypeRegistryCheckerTests
{
    /// <summary>REG-C12A: a demand over the complement of the registered declarative :Percent is a proper subset of the domain, so it is satisfiable — the negated declarative type is a modelled non-covering removal.</summary>
    [TestMethod]
    public void REGC12ANegatedDeclarativeRegisteredIsSatisfiable()
    {
        DatatypeRegistry registry = PercentRegistry();
        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction([new OwlDataComplementOf(PercentReference)], registry);

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, verdict);
    }

    /// <summary>REG-C12-DEL: a demand over the complement of the registered delegate-backed :Oracle abstains — a self-certified definition never checks domain coverage, so a domain-covering delegate would make the difference empty; the branch never claims Satisfiable.</summary>
    [TestMethod]
    public void REGC12DELNegatedDelegateBackedAbstains()
    {
        DatatypeRegistry registry = DelegateRegistry();
        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction([new OwlDataComplementOf(OracleReference)], registry);

        Assert.AreEqual(DatatypeSatisfiability.Unknown, verdict);
    }

    /// <summary>The registered-membership consult (point 1): an enumerated value in the registered :Percent value space keeps the conjunction satisfiable.</summary>
    [TestMethod]
    public void REGMEMBERSHIPValueInRegisteredRangeSatisfiable()
    {
        DatatypeRegistry registry = PercentRegistry();
        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction([OneOf(IntegerLiteral(50)), PercentReference], registry);

        Assert.AreEqual(DatatypeSatisfiability.Satisfiable, verdict);
    }

    /// <summary>The registered-membership consult (point 1): an enumerated value outside the registered :Percent value space empties the conjunction.</summary>
    [TestMethod]
    public void REGMEMBERSHIPValueOutOfRegisteredRangeUnsatisfiable()
    {
        DatatypeRegistry registry = PercentRegistry();
        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction([OneOf(IntegerLiteral(150)), PercentReference], registry);

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, verdict);
    }

    /// <summary>The registered positive base consult (point 3a): a demand over the delegate-backed :Oracle whose oracle empties every conjunction is decided unsatisfiable by the delegate.</summary>
    [TestMethod]
    public void REGPOSITIVEDelegateBackedBaseDecidesUnsatisfiable()
    {
        DatatypeRegistry registry = DelegateRegistry();
        DatatypeSatisfiability verdict = DatatypeSatisfiabilityChecker.DecideConjunction([OracleReference], registry);

        Assert.AreEqual(DatatypeSatisfiability.Unsatisfiable, verdict);
    }

    /// <summary>Builds a registry carrying the :Percent bounded datatype over xsd:integer[0,100].</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry PercentRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new BoundedDatatype(Iri("Percent"), Vocabulary.Xsd.Integer,
        [
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MinInclusive), IntegerLiteral(0)),
            new OwlFacetRestriction(new NamedNode(Vocabulary.XsdFacets.MaxInclusive), IntegerLiteral(100)),
        ]));

        return builder.Build();
    }

    /// <summary>Builds a registry carrying the :Oracle delegate-backed datatype whose oracle empties every conjunction.</summary>
    /// <returns>The frozen registry.</returns>
    private static DatatypeRegistry DelegateRegistry()
    {
        DatatypeRegistryBuilder builder = new();
        builder.Add(new DelegateBackedDatatype(Iri("Oracle"), new EmptyingOracle().Answer));

        return builder.Build();
    }

    /// <summary>The registered :Percent datatype reference.</summary>
    private static OwlDatatypeReference PercentReference { get; } = new(new NamedNode(Iri("Percent")));

    /// <summary>The registered :Oracle datatype reference.</summary>
    private static OwlDatatypeReference OracleReference { get; } = new(new NamedNode(Iri("Oracle")));

    /// <summary>An example-namespace datatype IRI.</summary>
    /// <param name="localName">The local name.</param>
    /// <returns>The IRI.</returns>
    private static Utf8String Iri(string localName)
    {
        return Utf8Strings.From("http://example.org/" + localName);
    }

    /// <summary>An <c>xsd:integer</c> typed literal.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The literal.</returns>
    private static Literal IntegerLiteral(int value)
    {
        return new Literal(Utf8Strings.From(value.ToString(System.Globalization.CultureInfo.InvariantCulture)), new NamedNode(Vocabulary.Xsd.Integer));
    }

    /// <summary>A data enumeration (<c>DataOneOf</c>).</summary>
    /// <param name="literals">The enumerated literals.</param>
    /// <returns>The data range.</returns>
    private static OwlDataOneOf OneOf(params Literal[] literals)
    {
        return new OwlDataOneOf(literals);
    }

    /// <summary>A frame binding a datatype oracle that empties every conjunction and admits no value, exposing a method group as the oracle without a lexical closure.</summary>
    private sealed class EmptyingOracle
    {
        /// <summary>The bound membership verdict the oracle returns for a value.</summary>
        private DatatypeMembership Membership { get; } = DatatypeMembership.Out;

        /// <summary>The bound satisfiability verdict the oracle returns for a conjunction.</summary>
        private DatatypeSatisfiability Satisfiability { get; } = DatatypeSatisfiability.Unsatisfiable;

        /// <summary>Answers the folded question from the bound frame state: every conjunction empty, every membership out.</summary>
        /// <param name="question">The folded question.</param>
        /// <returns>The folded answer.</returns>
        public DatatypeAnswer Answer(in DatatypeQuestion question)
        {
            return question.Operation switch
            {
                DatatypeOperation.Contains => DatatypeAnswer.ForContains(Membership, null),
                DatatypeOperation.DecideConjunction => DatatypeAnswer.ForConjunction(Satisfiability),
                _ => default
            };
        }
    }
}
