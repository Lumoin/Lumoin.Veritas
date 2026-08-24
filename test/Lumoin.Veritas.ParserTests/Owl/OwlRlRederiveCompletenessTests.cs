using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The rederivability-coverage sweep for the maintained OWL 2 RL closure: over
/// each battery pool and hand-built shape and every W3C RL-marked premise, it
/// traces the naive materialization and asserts that for every traced
/// derivation the head-bound matcher entry belonging to that very rule confirms
/// the conclusion against the full-closure state.
/// </summary>
/// <remarks>
/// <para>
/// The assertion is per rule, never any-witness: it is the traced rule's own
/// entry that must confirm the conclusion, not merely some entry finding some
/// witness. A symmetric partner (eq-sym behind a functional-property merge,
/// cax-dw behind the AllDisjointClasses materialization, prp-pdw behind
/// AllDisjointProperties) supplies a spurious full-closure witness that masks a
/// missing producer entry, so any-witness coverage is blind exactly where a
/// whole symmetric orbit is co-deleted; the per-rule form fails on the first
/// input exercising the missing producer.
/// </para>
/// <para>
/// An inconsistent input carries no coverage obligation — its trace is partial
/// — so the sweep instead asserts verdict agreement: the maintained engine
/// constructed over the same base must also report the inconsistency. Checks
/// are deduped on (rule, conclusion) per input; nothing is sampled or
/// truncated. A missing or mis-checked entry fails mechanically, naming the
/// input, the rule and the conclusion.
/// </para>
/// </remarks>
[TestClass]
internal sealed class OwlRlRederiveCompletenessTests
{
    /// <summary>The MSTest-supplied per-test context; its token aborts derivation between rounds.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Sweeps the schema-closure pool as a whole base for rederivability coverage.</summary>
    [TestMethod]
    public void SchemaPoolRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.SchemaPool());
    }

    /// <summary>Sweeps the property-characteristic pool as a whole base for rederivability coverage.</summary>
    [TestMethod]
    public void CharacteristicPoolRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.CharacteristicPool());
    }

    /// <summary>Sweeps the inverse/chain pool as a whole base for rederivability coverage.</summary>
    [TestMethod]
    public void InverseChainPoolRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.InverseChainPool());
    }

    /// <summary>Sweeps the equality-churn pool as a whole base for rederivability coverage.</summary>
    [TestMethod]
    public void EqualityPoolRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.EqualityPool());
    }

    /// <summary>Sweeps the CyclicOrphan op-0 base for rederivability coverage.</summary>
    [TestMethod]
    public void CyclicOrphanRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.CyclicOrphan());
    }

    /// <summary>Sweeps the AlternateDerivation op-0 base for rederivability coverage.</summary>
    [TestMethod]
    public void AlternateDerivationRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.AlternateDerivation());
    }

    /// <summary>Sweeps the SameAsUnMerge op-0 base for rederivability coverage.</summary>
    [TestMethod]
    public void SameAsUnMergeRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.SameAsUnMerge());
    }

    /// <summary>Sweeps the consistent FalsityRetract base for rederivability coverage.</summary>
    [TestMethod]
    public void FalsityRetractConsistentRederiveCoverage()
    {
        RunSweep(OwlRlCompletenessCorpus.FalsityRetractConsistent());
    }

    /// <summary>Sweeps one approved-status, RL-marked W3C premise for rederivability coverage.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.RlMarked)]
    public void ApprovedW3cRederiveCoverage(Owl2TestCase testCase)
    {
        SweepW3c(testCase);
    }

    /// <summary>Sweeps one proposed-status, RL-marked W3C premise for rederivability coverage.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.RlMarked)]
    public void ProposedW3cRederiveCoverage(Owl2TestCase testCase)
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
    /// Runs the rederivability-coverage sweep over one input: traces the naive
    /// closure, asserts verdict agreement instead when the base is
    /// inconsistent, then for every deduped (rule, conclusion) pair asserts
    /// the named rule's own matcher entry confirms the conclusion against the
    /// full-closure state.
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

        HashSet<(string Rule, EncodedTriple Conclusion)> seen = [];
        List<string> findings = [];
        int pairsProbed = 0;
        foreach(InferenceTraceEvent evt in events)
        {
            if(!seen.Add((evt.Rule, evt.Conclusion)))
            {
                continue;
            }

            TestContext.CancellationToken.ThrowIfCancellationRequested();
            pairsProbed++;
            if(!engine.CheckRederiveEntry(evt.Rule, evt.Conclusion))
            {
                findings.Add(
                    $"{input.Identifier}: rule {evt.Rule} — its own matcher entry does not rederive conclusion {OwlRlCompletenessCorpus.Describe(evt.Conclusion)} against the full closure.");
            }
        }

        TestContext.WriteLine(
            $"[rederive-coverage] {input.Identifier}: derivations={events.Count}, pairs-probed={pairsProbed}, gaps={findings.Count}.");

        Assert.IsEmpty(
            findings,
            $"{input.Identifier}: {findings.Count} rederivability-coverage gap(s):\n{string.Join("\n", findings)}");
    }
}
