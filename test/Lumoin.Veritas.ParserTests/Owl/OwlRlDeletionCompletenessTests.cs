using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The deletion-coverage sweep for the maintained OWL 2 RL closure: over each
/// battery pool and hand-built shape and every W3C RL-marked premise, it traces
/// the naive materialization and asserts that for every traced derivation and
/// every premise the derivation matched, deleting that premise overdeletes the
/// derived conclusion — the maintained engine's forward deletion marking must
/// cover every body position of every rule the input exercises.
/// </summary>
/// <remarks>
/// <para>
/// A conclusion that is a base fact or an axiomatic seed is exempt: the marking
/// sink refuses to mark such facts by construction (a derived fact re-asserted
/// as base survives deletion), so those pairs carry no coverage obligation. A
/// traced conclusion is never a base fact at trace time; the guard stands for
/// the seeded facts and for defence in depth.
/// </para>
/// <para>
/// An inconsistent input carries no coverage obligation — its trace is partial
/// and the engine is not on its incremental path — so the sweep instead asserts
/// verdict agreement: the maintained engine constructed over the same base must
/// also report the inconsistency. Every checked pair is deduped on (premise,
/// conclusion) per input; nothing is sampled or truncated. A gap the engine's
/// deletion table leaves fails mechanically, naming the input, the rule, the
/// premise and the conclusion.
/// </para>
/// </remarks>
[TestClass]
internal sealed class OwlRlDeletionCompletenessTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Sweeps the schema-closure pool as a whole base for deletion coverage.</summary>
    [TestMethod]
    public void SchemaPoolDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.SchemaPool());
    }

    /// <summary>Sweeps the property-characteristic pool as a whole base for deletion coverage.</summary>
    [TestMethod]
    public void CharacteristicPoolDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.CharacteristicPool());
    }

    /// <summary>Sweeps the inverse/chain pool as a whole base for deletion coverage.</summary>
    [TestMethod]
    public void InverseChainPoolDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.InverseChainPool());
    }

    /// <summary>Sweeps the equality-churn pool as a whole base for deletion coverage.</summary>
    [TestMethod]
    public void EqualityPoolDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.EqualityPool());
    }

    /// <summary>Sweeps the CyclicOrphan op-0 base for deletion coverage.</summary>
    [TestMethod]
    public void CyclicOrphanDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.CyclicOrphan());
    }

    /// <summary>Sweeps the AlternateDerivation op-0 base for deletion coverage.</summary>
    [TestMethod]
    public void AlternateDerivationDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.AlternateDerivation());
    }

    /// <summary>Sweeps the SameAsUnMerge op-0 base for deletion coverage.</summary>
    [TestMethod]
    public void SameAsUnMergeDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.SameAsUnMerge());
    }

    /// <summary>Sweeps the consistent FalsityRetract base for deletion coverage.</summary>
    [TestMethod]
    public void FalsityRetractConsistentDeletionCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.FalsityRetractConsistent());
    }

    /// <summary>Sweeps one approved-status, RL-marked W3C premise for deletion coverage.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.RlMarked)]
    public void ApprovedW3cDeletionCoverage(Owl2TestCase testCase)
    {
        SweepW3c(testCase);
    }

    /// <summary>Sweeps one proposed-status, RL-marked W3C premise for deletion coverage.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.RlMarked)]
    public void ProposedW3cDeletionCoverage(Owl2TestCase testCase)
    {
        SweepW3c(testCase);
    }

    /// <summary>Loads a W3C test case premise and sweeps it, failing when it declares no readable premise.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    private void SweepW3c(Owl2TestCase testCase)
    {
        System.ArgumentNullException.ThrowIfNull(testCase);

        if(OwlRlCompletenessCorpus.LoadW3c(testCase) is not OwlRlCompletenessCorpus.CorpusInput input)
        {
            Assert.Fail($"{testCase.Identifier}: the test declares no premise document in a syntax the sweep reads; the conformance harness reads the same rows.");

            return;
        }

        RunSweep(input);
    }

    /// <summary>
    /// Runs the deletion-coverage sweep over one input: traces the naive
    /// closure, asserts verdict agreement instead when the base is
    /// inconsistent, then for every deduped (premise, conclusion) pair asserts
    /// the conclusion joins the engine's overdelete marking of that premise,
    /// unless the conclusion is a base or seeded fact.
    /// </summary>
    /// <param name="input">The corpus input to sweep.</param>
    private void RunSweep(OwlRlCompletenessCorpus.CorpusInput input)
    {
        (bool consistent, List<InferenceTraceEvent> events) = OwlRlCompletenessCorpus.TraceClosure(input, TestContext.CancellationToken);
        if(!consistent)
        {
            OwlRlMaintainedClosure inconsistentEngine = new(input.Base, input.Terms, input.Oracle, TestContext.CancellationToken);
            Assert.IsFalse(inconsistentEngine.Current.IsConsistent, $"{input.Identifier}: the naive oracle reports an inconsistency the maintained engine's construction missed.");

            return;
        }

        OwlRlMaintainedClosure engine = new(input.Base, input.Terms, input.Oracle, TestContext.CancellationToken);
        HashSet<EncodedTriple> baseSet = [.. input.Base];
        HashSet<EncodedTriple> seeded = OwlRlCompletenessCorpus.SeededSet(input, TestContext.CancellationToken);

        //Group deduped conclusions by the premise whose deletion must reach
        //them, keeping one producing rule per pair for the assertion message,
        //so the forward marking is computed once per distinct premise.
        Dictionary<EncodedTriple, Dictionary<EncodedTriple, string>> byPremise = [];
        foreach(InferenceTraceEvent evt in events)
        {
            foreach(EncodedTriple premise in evt.Premises)
            {
                if(!byPremise.TryGetValue(premise, out Dictionary<EncodedTriple, string>? conclusions))
                {
                    conclusions = [];
                    byPremise[premise] = conclusions;
                }

                conclusions.TryAdd(evt.Conclusion, evt.Rule);
            }
        }

        List<string> findings = [];
        int pairsProbed = 0;
        int baseOrSeedSkipped = 0;
        foreach((EncodedTriple premise, Dictionary<EncodedTriple, string> conclusions) in byPremise)
        {
            TestContext.CancellationToken.ThrowIfCancellationRequested();

            HashSet<EncodedTriple>? marking = null;
            foreach((EncodedTriple conclusion, string rule) in conclusions)
            {
                if(baseSet.Contains(conclusion) || seeded.Contains(conclusion))
                {
                    baseOrSeedSkipped++;

                    continue;
                }

                pairsProbed++;
                marking ??= engine.ComputeOverdeleteMarking(premise);
                if(!marking.Contains(conclusion))
                {
                    findings.Add(
                        $"{input.Identifier}: rule {rule} — retracting premise {OwlRlCompletenessCorpus.Describe(premise)} did not overdelete conclusion {OwlRlCompletenessCorpus.Describe(conclusion)}.");
                }
            }
        }

        TestContext.WriteLine(
            $"[deletion-coverage] {input.Identifier}: derivations={events.Count}, premises={byPremise.Count}, pairs-probed={pairsProbed}, base-or-seed-skipped={baseOrSeedSkipped}, gaps={findings.Count}.");

        Assert.IsEmpty(
            findings,
            $"{input.Identifier}: {findings.Count} deletion-coverage gap(s):\n{string.Join("\n", findings)}");
    }
}
