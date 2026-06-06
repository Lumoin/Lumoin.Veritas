using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Dl;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Tests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Owl;

/// <summary>
/// Runs the W3C OWL 2 conformance corpus as species-identification tests:
/// every document of a test is checked against the OWL 2 DL restrictions,
/// and the verdict is compared to the manifest's <c>test:species</c>
/// markers. Every RDF graph is OWL 2 Full, so <c>FULL</c> is universal and
/// the manifest claim under test is <c>DL</c> membership — present means
/// every document is OWL 2 DL, absent means at least one is not.
/// </summary>
[TestClass]
internal sealed class W3cOwl2SpeciesTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Corpus judgments the checker does not reproduce, recorded as
    /// self-correcting known gaps: a run fails if a listed test starts
    /// passing. <c>New-Feature-Rational-002/-003</c> carry an RDF/XML
    /// serialisation defect — the literal list's <c>rdf:rest</c> points at
    /// the bare <c>rdf:</c> namespace IRI instead of <c>rdf:nil</c> — while
    /// their functional-syntax variants are well-formed; the markers follow
    /// the intent, not the serialisation. <c>WebOnt-I5.5-005</c>'s
    /// headerless conclusion is judged DL although headerless documents are
    /// judged OWL Full everywhere else (<c>WebOnt-Ontology-003</c>, and the
    /// proposed-corpus revision comments document the header requirement).
    /// <c>WebOnt-InverseFunctionalProperty-001</c> and
    /// <c>WebOnt-SymmetricProperty-003</c> judge a property typed only with
    /// a characteristic class to be OWL Full, while
    /// <c>WebOnt-SymmetricProperty-002</c> judges the identical pattern DL;
    /// the checker follows the Mapping-to-RDF reading (a characteristic
    /// types its subject as an object property), siding with -002.
    /// </summary>
    private static HashSet<string> KnownGaps { get; } =
    [
        "New-Feature-Rational-002",
        "New-Feature-Rational-003",
        "WebOnt-I5.5-005",
        "WebOnt-InverseFunctionalProperty-001",
        "WebOnt-SymmetricProperty-003",
    ];

    /// <summary>Runs one approved-status OWL 2 test case as a species-identification test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf")]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    /// <summary>Runs one proposed-status OWL 2 test case as a species-identification test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf")]
    public void RunProposed(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    private static void RunAndAssert(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        //The species markers are curated claims only on the
        //profile-identification tests; elsewhere they are incidental
        //annotations (every graph is FULL) and assert nothing.
        if(!testCase.Kinds.Contains("ProfileIdentificationTest"))
        {
            Assert.Inconclusive("Not a profile-identification test; the species markers are not curated claims.");
        }

        //Check every document of the test; the species verdict for the
        //test is the conjunction over its documents, like the profile
        //verdict. A conclusion inherits the premise's declarations.
        List<OwlDlReport> reports = [];
        OwlOntologyDocument? premiseDocument = null;

        if(LoadDocument(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise, declarationContext: null) is (OwlOntologyDocument premise, List<Quad> premiseQuads))
        {
            premiseDocument = premise;
            reports.Add(OwlDlChecker.Check(premiseQuads, premise));
        }

        if(LoadDocument(testCase, testCase.RdfXmlConclusion, testCase.FunctionalConclusion, premiseDocument) is (OwlOntologyDocument conclusion, List<Quad> conclusionQuads))
        {
            reports.Add(OwlDlChecker.Check(conclusionQuads, conclusion));
        }

        if(LoadDocument(testCase, testCase.RdfXmlNonConclusion, testCase.FunctionalNonConclusion, premiseDocument) is (OwlOntologyDocument nonConclusion, List<Quad> nonConclusionQuads))
        {
            reports.Add(OwlDlChecker.Check(nonConclusionQuads, nonConclusion));
        }

        if(LoadDocument(testCase, testCase.RdfXmlInput, functional: null, declarationContext: null) is (OwlOntologyDocument input, List<Quad> inputQuads))
        {
            reports.Add(OwlDlChecker.Check(inputQuads, input));
        }

        if(reports.Count == 0)
        {
            Assert.Inconclusive("The test declares no document in a syntax the harness reads.");
        }

        bool expected = testCase.Species.Contains("DL");
        bool allIn = true;
        string? firstViolation = null;
        foreach(OwlDlReport report in reports)
        {
            if(!report.IsInDl)
            {
                allIn = false;
                firstViolation ??= report.Violations.Count > 0 ? report.Violations[0].Construct : null;
            }
        }

        if(KnownGaps.Contains(testCase.Identifier))
        {
            if(expected == allIn)
            {
                Assert.Fail($"{testCase.Identifier} is a recorded known gap but now matches the species claim; remove it from KnownGaps.");
            }

            Assert.Inconclusive("Recorded corpus contradiction; see KnownGaps.");
        }

        if(expected && !allIn)
        {
            Assert.Fail($"{testCase.Identifier}: expected OWL 2 DL, but the checker excluded it: {firstViolation ?? "(no recorded violation)"}");
        }

        if(!expected && allIn)
        {
            Assert.Fail($"{testCase.Identifier}: expected the documents NOT to be (all) OWL 2 DL, but the checker found no violation.");
        }
    }

    private static (OwlOntologyDocument Document, List<Quad> Quads)? LoadDocument(
        Owl2TestCase testCase,
        string? rdfXml,
        string? functional,
        OwlOntologyDocument? declarationContext)
    {
        if(rdfXml is string xml)
        {
            DiagnosticBag parseDiagnostics = new();
            List<Quad> quads = [.. RdfXmlReader.Read(Encoding.UTF8.GetBytes(xml), parseDiagnostics, baseIri: testCase.Uri.AbsoluteUri)];
            if(parseDiagnostics.HasErrors)
            {
                Assert.Inconclusive("A test document did not parse as RDF/XML; the test cannot be set up.");
            }

            quads = Owl2ImportResolver.Expand(testCase, quads);

            return (OwlRdfMapper.Map(quads, declarationContext), quads);
        }

        if(functional is string text)
        {
            OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(text);
            if(document.Diagnostics.HasErrors)
            {
                Assert.Inconclusive("A test document did not parse as functional syntax; the test cannot be set up.");
            }

            return (document, OwlStructuralToRdf.ToQuads(document));
        }

        return null;
    }
}
