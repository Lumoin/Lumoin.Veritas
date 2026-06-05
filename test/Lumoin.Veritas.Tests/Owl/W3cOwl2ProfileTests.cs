using System;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Profiles;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Tests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.Tests.Owl;

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
/// Honest skips: functional-syntax-only tests (no OWL functional-syntax
/// reader yet) and imports-bearing tests (the harness supplies no imported
/// ontologies).
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cOwl2ProfileTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Corpus contradictions recorded as known gaps. <c>WebOnt-I4.6-004</c>'s
    /// non-conclusion (<c>SameIndividual</c> over two declared class names)
    /// is structurally identical to <c>WebOnt-sameAs-001</c>'s premise, yet
    /// the corpus marks the former RL-only and the latter EL+RL — no
    /// construct-level grammar can satisfy both. The gap is self-correcting:
    /// a run fails if a listed test starts passing.
    /// </summary>
    private static HashSet<string> KnownGaps { get; } = ["WebOnt-I4.6-004"];

    /// <summary>Runs one approved-status OWL 2 test case as a profile-identification test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf")]
    public void RunApproved(Owl2TestCase testCase)
    {
        RunAndAssert(testCase);
    }

    private static void RunAndAssert(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(!testCase.IsRdfXmlReadable)
        {
            Assert.Inconclusive("Functional-syntax-only test; the OWL functional-syntax reader is not built.");
        }

        if(testCase.HasImports)
        {
            Assert.Inconclusive("Imports-bearing test; the harness supplies no imported ontologies.");
        }

        //Check every document of the test; the per-profile verdict for the
        //TEST is the conjunction over its documents. A conclusion (or
        //non-conclusion) document is interpreted in the premise's context and
        //inherits its declarations.
        List<OwlProfileReport> reports = [];
        OwlOntologyDocument? premiseDocument = null;

        if(testCase.RdfXmlPremise is string premise)
        {
            premiseDocument = MapDocument(testCase, premise, declarationContext: null);
            reports.Add(OwlProfileChecker.Check(premiseDocument));
        }

        if(testCase.RdfXmlConclusion is string conclusion)
        {
            reports.Add(OwlProfileChecker.Check(MapDocument(testCase, conclusion, premiseDocument)));
        }

        if(testCase.RdfXmlNonConclusion is string nonConclusion)
        {
            reports.Add(OwlProfileChecker.Check(MapDocument(testCase, nonConclusion, premiseDocument)));
        }

        if(testCase.RdfXmlInput is string input)
        {
            reports.Add(OwlProfileChecker.Check(MapDocument(testCase, input, declarationContext: null)));
        }

        if(KnownGaps.Contains(testCase.Identifier))
        {
            if(MatchesAllProfileClaims(testCase, reports))
            {
                Assert.Fail($"{testCase.Identifier} is a recorded known gap but now matches all profile claims; remove it from KnownGaps.");
            }

            Assert.Inconclusive("Recorded corpus contradiction; see KnownGaps.");
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

    private static OwlOntologyDocument MapDocument(Owl2TestCase testCase, string document, OwlOntologyDocument? declarationContext)
    {
        DiagnosticBag parseDiagnostics = new();
        IReadOnlyList<Quad> quads = RdfXmlReader.Read(Encoding.UTF8.GetBytes(document), parseDiagnostics, baseIri: testCase.Uri.AbsoluteUri);
        if(parseDiagnostics.HasErrors)
        {
            Assert.Inconclusive("A test document did not parse as RDF/XML; the test cannot be set up.");
        }

        return OwlRdfMapper.Map(quads, declarationContext);
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
