using System.Collections.Immutable;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The arm-coverage ledger for the W3C OWL 2 conformance corpus: the row that
/// turns an unclaimed entailment/consistency case into a named failure rather
/// than a silent gap. It asserts the partition law — every case carrying at
/// least one of the four entailment or consistency kinds is claimed by exactly
/// one of the three E/C remit predicates (<see cref="Owl2TestRemit.RlMarked"/>,
/// <see cref="Owl2TestRemit.DirectSemanticsDl"/>,
/// <see cref="Owl2TestRemit.RdfBasedBeyondRl"/>) — and that the
/// profile-identification arm's <see cref="Owl2TestRemit.All"/> remit covers the
/// whole corpus. A future corpus or remit change that opens a hole — a case no
/// E/C arm claims, or one two arms both claim — fails here, named by identifier
/// and metadata.
/// </summary>
[TestClass]
internal sealed class W3cOwl2CoverageTests
{
    /// <summary>Asserts the coverage ledger over the approved suite folder.</summary>
    [TestMethod]
    public void ApprovedSuiteIsFullyPartitioned()
    {
        AssertCoverage("approved");
    }

    /// <summary>Asserts the coverage ledger over the proposed suite folder.</summary>
    [TestMethod]
    public void ProposedSuiteIsFullyPartitioned()
    {
        AssertCoverage("proposed");
    }

    /// <summary>
    /// Loads one suite manifest and asserts, per case: the profile-identification
    /// arm's <see cref="Owl2TestRemit.All"/> remit covers it, and — when the case
    /// carries at least one of the four entailment or consistency kinds — exactly
    /// one of the three E/C remit predicates claims it.
    /// </summary>
    /// <param name="suiteFolder">The status arm under <c>Material/Owl2</c> (<c>approved</c> or <c>proposed</c>).</param>
    private static void AssertCoverage(string suiteFolder)
    {
        ImmutableArray<Owl2TestCase> tests = Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", suiteFolder, "all.rdf"));

        Owl2RemitPredicate all = Owl2Remit.For(Owl2TestRemit.All);
        Owl2RemitPredicate rlMarked = Owl2Remit.For(Owl2TestRemit.RlMarked);
        Owl2RemitPredicate directSemanticsDl = Owl2Remit.For(Owl2TestRemit.DirectSemanticsDl);
        Owl2RemitPredicate rdfBasedBeyondRl = Owl2Remit.For(Owl2TestRemit.RdfBasedBeyondRl);

        foreach(Owl2TestCase testCase in tests)
        {
            //Every case materialises for the profile-identification arm, whose
            //remit reads the whole corpus.
            Assert.IsTrue(all(testCase), $"{suiteFolder}/{testCase.Identifier}: the profile-identification arm's All remit must cover every case.");

            bool isEntailmentOrConsistency =
                testCase.Kinds.Contains("PositiveEntailmentTest")
                || testCase.Kinds.Contains("NegativeEntailmentTest")
                || testCase.Kinds.Contains("InconsistencyTest")
                || testCase.Kinds.Contains("ConsistencyTest");

            if(!isEntailmentOrConsistency)
            {
                continue;
            }

            bool byRl = rlMarked(testCase);
            bool byDirect = directSemanticsDl(testCase);
            bool byRdfBased = rdfBasedBeyondRl(testCase);
            int claimants = (byRl ? 1 : 0) + (byDirect ? 1 : 0) + (byRdfBased ? 1 : 0);

            Assert.AreEqual(
                1,
                claimants,
                $"{suiteFolder}/{testCase.Identifier}: an entailment/consistency case must be claimed by exactly one E/C remit "
                + $"(RlMarked={byRl}, DirectSemanticsDl={byDirect}, RdfBasedBeyondRl={byRdfBased}; "
                + $"profiles=[{string.Join(",", testCase.Profiles)}], species=[{string.Join(",", testCase.Species)}], "
                + $"semantics=[{string.Join(",", testCase.Semantics)}], kinds=[{string.Join(",", testCase.Kinds)}]).");
        }
    }
}
