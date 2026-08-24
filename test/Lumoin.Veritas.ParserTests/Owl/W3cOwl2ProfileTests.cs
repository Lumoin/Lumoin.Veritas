using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Runs the W3C OWL 2 conformance corpus as profile-identification tests:
/// every test case's documents are mapped to structural form and checked
/// against the EL, QL, and RL grammars, and the verdicts are compared to the
/// manifest's <c>test:profile</c> markers.
/// </summary>
/// <remarks>
/// <para>
/// A profile marker present means every document of the test is in that
/// profile; for tests whose documents are curated as OWL 2 DL
/// (<c>test:species DL</c>) an absent marker is a deliberate negative claim
/// and is asserted too — that direction is what catches an over-permissive
/// checker. The species-FULL-only tests (the rdfbased-sem series) carry no
/// profile claims, so only their positive direction (vacuously empty) binds.
/// </para>
/// <para>
/// Documents arrive as RDF/XML through the RDF mapping or as functional
/// syntax read directly into structural form; <c>owl:imports</c> references
/// resolve against the test's supplied ontologies, so the checked unit is
/// the axiom closure.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cOwl2ProfileTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Corpus contradictions recorded as known gaps, self-correcting: a run
    /// fails if a listed test starts passing. <c>WebOnt-I4.6-004</c>'s
    /// non-conclusion and <c>WebOnt-I4.6-003</c>'s premise
    /// (<c>owl:sameAs</c>/<c>SameIndividual</c> over two declared class
    /// names) are structurally identical to <c>WebOnt-sameAs-001</c>'s
    /// premise, yet the corpus marks those two RL-only and the latter
    /// EL+RL. The
    /// functional-syntax datatype family (<c>*-integer-filler</c>,
    /// <c>functionality-clash</c>) uses only profile-legal constructs
    /// (<c>DataHasValue</c>/<c>DataAllValuesFrom</c> over <c>xsd:integer</c>,
    /// <c>FunctionalDataProperty</c>) yet carries withheld markers — the
    /// curators appear to track datatype-reasoning expectations rather than
    /// the published grammars there.
    /// </summary>
    private static HashSet<string> KnownGaps { get; } =
    [
        "WebOnt-I4.6-003",
        "WebOnt-I4.6-004",
        "consistent-integer-filler",
        "inconsistent-integer-filler",
        "functionality-clash",
    ];

    /// <summary>Runs one approved-status OWL 2 test case as a profile-identification test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf")]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    /// <summary>Runs one proposed-status OWL 2 test case as a profile-identification test.</summary>
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

        //Check every document of the test; the per-profile verdict for the
        //TEST is the conjunction over its documents. RDF/XML documents map
        //through the RDF mapping (a conclusion inherits the premise's
        //declarations); a functional-syntax document is self-describing and
        //reads directly into structural form.
        List<OwlProfileReport> reports = [];
        OwlOntologyDocument? premiseDocument = null;

        if(testCase.RdfXmlPremise is { } premise)
        {
            premiseDocument = MapDocument(testCase, premise, declarationContext: null);
            reports.Add(OwlProfileChecker.Check(premiseDocument));
        }
        else if(testCase.FunctionalPremise is string functionalPremise)
        {
            premiseDocument = ReadFunctional(functionalPremise);
            reports.Add(OwlProfileChecker.Check(premiseDocument));
        }

        if(testCase.RdfXmlConclusion is { } conclusion)
        {
            reports.Add(OwlProfileChecker.Check(MapDocument(testCase, conclusion, premiseDocument)));
        }
        else if(testCase.FunctionalConclusion is string functionalConclusion)
        {
            reports.Add(OwlProfileChecker.Check(ReadFunctional(functionalConclusion)));
        }

        if(testCase.RdfXmlNonConclusion is { } nonConclusion)
        {
            reports.Add(OwlProfileChecker.Check(MapDocument(testCase, nonConclusion, premiseDocument)));
        }
        else if(testCase.FunctionalNonConclusion is string functionalNonConclusion)
        {
            reports.Add(OwlProfileChecker.Check(ReadFunctional(functionalNonConclusion)));
        }

        if(testCase.RdfXmlInput is { } input)
        {
            reports.Add(OwlProfileChecker.Check(MapDocument(testCase, input, declarationContext: null)));
        }

        if(reports.Count == 0)
        {
            Assert.Fail($"{testCase.Identifier}: the test declares no document in a syntax the harness reads.");
        }

        if(KnownGaps.Contains(testCase.Identifier))
        {
            if(MatchesAllProfileClaims(testCase, reports))
            {
                Assert.Fail($"{testCase.Identifier} is a recorded known gap but now matches all profile claims; remove it from KnownGaps.");
            }

            //A pinned corpus contradiction the checker does not reproduce: an
            //expected, passing capability boundary.
            return;
        }

        AssertProfile(testCase, reports, "EL", OwlProfiles.El);
        AssertProfile(testCase, reports, "QL", OwlProfiles.Ql);
        AssertProfile(testCase, reports, "RL", OwlProfiles.Rl);
    }

    private static bool MatchesAllProfileClaims(Owl2TestCase testCase, List<OwlProfileReport> reports)
    {
        foreach((string marker, OwlProfiles profile) in (ReadOnlySpan<(string, OwlProfiles)>)[("EL", OwlProfiles.El), ("QL", OwlProfiles.Ql), ("RL", OwlProfiles.Rl)])
        {
            bool expected = testCase.Profiles.Contains(marker);
            bool allIn = true;
            foreach(OwlProfileReport report in reports)
            {
                if(!report.IsIn(profile))
                {
                    allIn = false;

                    break;
                }
            }

            if(expected != allIn && (expected || testCase.Species.Contains("DL")))
            {
                return false;
            }
        }

        return true;
    }

    private static OwlOntologyDocument MapDocument(Owl2TestCase testCase, Utf8String document, OwlOntologyDocument? declarationContext)
    {
        DiagnosticBag parseDiagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(document.Memory, parseDiagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri))];
        Assert.IsFalse(parseDiagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as RDF/XML; the test cannot be set up.");

        //Profile membership is a property of the ontology's axiom closure
        //(Profiles §2), so the document's owl:imports resolve against the
        //test's supplied ontologies before mapping.
        return OwlRdfMapper.Map(Owl2ImportResolver.Expand(testCase, quads), declarationContext);
    }

    private static OwlOntologyDocument ReadFunctional(string document)
    {
        OwlOntologyDocument read = Lumoin.Veritas.Owl.Functional.OwlFunctionalSyntaxReader.Read(document);
        Assert.IsFalse(read.Diagnostics.HasErrors, "A test document did not parse as functional syntax; the test cannot be set up.");

        return read;
    }

    private static void AssertProfile(Owl2TestCase testCase, List<OwlProfileReport> reports, string marker, OwlProfiles profile)
    {
        bool expected = testCase.Profiles.Contains(marker);
        bool allIn = true;
        foreach(OwlProfileReport report in reports)
        {
            if(!report.IsIn(profile))
            {
                allIn = false;

                break;
            }
        }

        if(expected && !allIn)
        {
            Assert.Fail($"{testCase.Identifier}: expected {marker} membership, but the checker excluded it: {DescribeViolations(reports, profile)}");
        }

        //The negative direction is asserted only for tests whose documents
        //the curators marked as OWL 2 DL: there the profile annotation is a
        //deliberate syntactic claim. The species-FULL-only tests (the
        //rdfbased-sem series) carry no profile claims at all — their absence
        //means "not applicable", not "out of profile".
        if(!expected && allIn && testCase.Species.Contains("DL"))
        {
            Assert.Fail($"{testCase.Identifier}: expected the documents NOT to be (all) in {marker}, but the checker found no violation.");
        }
    }

    private static string DescribeViolations(List<OwlProfileReport> reports, OwlProfiles profile)
    {
        List<string> details = [];
        foreach(OwlProfileReport report in reports)
        {
            foreach(OwlProfileViolation violation in report.Violations)
            {
                if(violation.Profile == profile)
                {
                    details.Add(violation.Construct);
                    if(details.Count == 3)
                    {
                        return string.Join(" | ", details);
                    }
                }
            }
        }

        return details.Count > 0 ? string.Join(" | ", details) : "(no recorded violation)";
    }
}
