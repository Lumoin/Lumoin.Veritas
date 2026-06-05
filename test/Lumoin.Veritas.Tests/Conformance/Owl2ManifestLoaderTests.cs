using System.Collections.Immutable;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Validates <see cref="Owl2ManifestLoader"/> against the vendored corpus
/// census recorded in <c>Material/Owl2/ATTRIBUTION.md</c> — the loader must
/// surface every test case, kind marker, profile marker, and inline document
/// the snapshot carries.
/// </summary>
[TestClass]
internal sealed class Owl2ManifestLoaderTests
{
    /// <summary>The approved arm carries the census the attribution records.</summary>
    [TestMethod]
    public void ApprovedArmMatchesCensus()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        Assert.HasCount(355, tests);

        int profileIdentification = 0;
        int consistency = 0;
        int inconsistency = 0;
        int positiveEntailment = 0;
        int negativeEntailment = 0;
        int el = 0;
        int ql = 0;
        int rl = 0;
        int rdfXmlPremise = 0;
        int functionalPremise = 0;

        foreach(Owl2TestCase test in tests)
        {
            profileIdentification += test.Kinds.Contains("ProfileIdentificationTest") ? 1 : 0;
            consistency += test.Kinds.Contains("ConsistencyTest") ? 1 : 0;
            inconsistency += test.Kinds.Contains("InconsistencyTest") ? 1 : 0;
            positiveEntailment += test.Kinds.Contains("PositiveEntailmentTest") ? 1 : 0;
            negativeEntailment += test.Kinds.Contains("NegativeEntailmentTest") ? 1 : 0;
            el += test.Profiles.Contains("EL") ? 1 : 0;
            ql += test.Profiles.Contains("QL") ? 1 : 0;
            rl += test.Profiles.Contains("RL") ? 1 : 0;
            rdfXmlPremise += test.RdfXmlPremise is not null ? 1 : 0;
            functionalPremise += test.FunctionalPremise is not null ? 1 : 0;
        }

        Assert.AreEqual(355, profileIdentification, "Every approved test doubles as a profile-identification test.");
        Assert.AreEqual(237, consistency);
        Assert.AreEqual(118, inconsistency);
        Assert.AreEqual(143, positiveEntailment);
        Assert.AreEqual(9, negativeEntailment);
        Assert.AreEqual(67, el);
        Assert.AreEqual(45, ql);
        Assert.AreEqual(70, rl);
        Assert.AreEqual(335, rdfXmlPremise);
        Assert.AreEqual(60, functionalPremise);
    }

    /// <summary>The proposed arm loads and carries its census count.</summary>
    [TestMethod]
    public void ProposedArmLoads()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "proposed", "all.rdf"));

        Assert.HasCount(86, tests);
    }

    /// <summary>The inline ontology documents are unescaped, parseable XML text, not entity-encoded residue. A document may still contain escaped text of its own — the marker of correct unescaping is the literal root element.</summary>
    [TestMethod]
    public void InlineDocumentsAreUnescaped()
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", "approved", "all.rdf"));

        int withDocument = 0;
        foreach(Owl2TestCase test in tests)
        {
            if(test.RdfXmlPremise is string premise)
            {
                withDocument++;

                Assert.Contains("<rdf:RDF", premise, $"{test.Identifier}: the premise document is RDF/XML text.");
            }
        }

        Assert.AreEqual(335, withDocument);
    }
}
