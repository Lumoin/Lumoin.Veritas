using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Dl;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

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
    /// passing. <c>WebOnt-I5.5-005</c>'s
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
        "WebOnt-I5.5-005",
        "WebOnt-InverseFunctionalProperty-001",
        "WebOnt-SymmetricProperty-003",
    ];

    /// <summary>Runs one approved-status OWL 2 test case as a species-identification test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.ProfileIdentification)]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    /// <summary>Runs one proposed-status OWL 2 test case as a species-identification test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.ProfileIdentification)]
    public void RunProposed(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    private static void RunAndAssert(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        //The species markers are curated claims only on the
        //profile-identification tests; elsewhere they are incidental
        //annotations (every graph is FULL) and assert nothing. The
        //profile-identification remit is enforced at the data source
        //(Owl2TestRemit.ProfileIdentification), so every case reaching here
        //carries a curated species claim.

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
            Assert.Fail($"{testCase.Identifier}: the test declares no document in a syntax the harness reads.");
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

            //A pinned corpus contradiction the checker does not reproduce: an
            //expected, passing capability boundary.
            return;
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

    /// <summary>
    /// Loads one document role in the syntax the harness reads, RDF/XML first,
    /// and returns it both as structural form and as triples; <c>null</c> when
    /// the role declares no document. A mapped RDF/XML form carrying errors
    /// falls through to the role's functional variant where the case declares
    /// one: the corpus's RDF/XML serialisation of the two rational cases is
    /// defective — the <c>DataOneOf</c> list's <c>rdf:rest</c> points at the
    /// bare <c>rdf:</c> namespace IRI instead of <c>rdf:nil</c>, so the list
    /// walk reports a malformed list and drops the axiom — while their
    /// functional-syntax documents, which the manifest declares normative
    /// alongside the RDF/XML ones, are well-formed, and the species markers
    /// follow the intent rather than the serialisation. A role with no
    /// functional variant that fails to map stays judged not structurally well
    /// formed, exactly as a mapping failure is judged everywhere else.
    /// </summary>
    /// <param name="testCase">The test case the documents belong to.</param>
    /// <param name="rdfXml">The role's inline RDF/XML, or <c>null</c>.</param>
    /// <param name="functional">The role's inline functional syntax, or <c>null</c>.</param>
    /// <param name="declarationContext">The document whose declarations the role inherits, or <c>null</c>.</param>
    /// <returns>The document and its triples, or <c>null</c>.</returns>
    private static (OwlOntologyDocument Document, List<Quad> Quads)? LoadDocument(
        Owl2TestCase testCase,
        Utf8String? rdfXml,
        string? functional,
        OwlOntologyDocument? declarationContext)
    {
        if(rdfXml is { } xml)
        {
            DiagnosticBag parseDiagnostics = new();
            List<Quad> quads = [.. RdfXmlReader.Read(xml.Memory, parseDiagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri))];
            Assert.IsFalse(parseDiagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as RDF/XML; the test cannot be set up.");

            quads = Owl2ImportResolver.Expand(testCase, quads);
            OwlOntologyDocument mapped = OwlRdfMapper.Map(quads, declarationContext);
            if(!mapped.Diagnostics.HasErrors || functional is not string variant)
            {
                return (mapped, quads);
            }

            return LoadFunctionalDocument(testCase, variant);
        }

        if(functional is string text)
        {
            return LoadFunctionalDocument(testCase, text);
        }

        return null;
    }

    /// <summary>Reads a role's functional-syntax document into structural form and serialises it through the forward RDF mapping; a functional document carries its own declarations, so it needs no declaration context.</summary>
    /// <param name="testCase">The test case the document belongs to.</param>
    /// <param name="functional">The role's inline functional syntax.</param>
    /// <returns>The document and its triples.</returns>
    private static (OwlOntologyDocument Document, List<Quad> Quads) LoadFunctionalDocument(Owl2TestCase testCase, string functional)
    {
        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(functional);
        Assert.IsFalse(document.Diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as functional syntax; the test cannot be set up.");

        return (document, OwlStructuralToRdf.ToQuads(document));
    }
}
