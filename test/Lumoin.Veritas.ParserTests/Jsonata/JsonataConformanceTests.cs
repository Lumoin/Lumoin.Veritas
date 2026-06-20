using Lumoin.Veritas.ParserTests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Runs the vendored JSONata test suite against the engine.
/// </summary>
/// <remarks>
/// This is a measurement baseline, not a green gate: a case where the engine's value or error differs from
/// the suite's expectation is a real <see cref="W3cOutcomeStatus.Failed"/>, so the failing count is the
/// honest distance to full conformance. A case that genuinely cannot run yet — an input the host JSON
/// adapter cannot materialise, external variable bindings the harness does not thread, or a deliberate
/// reference divergence — passes as a documented capability boundary rather than reporting inconclusive, and
/// the count of each kind is pinned by <see cref="SkipCensus"/> so the boundary stays visible. The divergence
/// reasons live in <see cref="JsonataReferenceDivergences"/>.
/// </remarks>
[TestClass]
internal sealed class JsonataConformanceTests
{
    /// <summary>Runs one vendored JSONata suite case.</summary>
    /// <param name="testCase">The case supplied by the suite loader.</param>
    [TestMethod]
    [JsonataConformanceData]
    public void RunJsonataTest(JsonataConformanceCase testCase)
    {
        W3cOutcome outcome = JsonataConformanceRunner.Run(testCase);
        if(outcome.Status == W3cOutcomeStatus.Skipped)
        {
            //A documented harness limit or deliberate reference divergence,
            //counted by SkipCensus: a capability boundary, not an inconclusive
            //verdict. A wrong value or error is still a failure (the runner
            //returns Failed for those), so the ratchet's honest distance holds.
            return;
        }

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>
    /// Asserts the count of each kind of deliberately-skipped JSONata case
    /// matches the pinned census — inputs the host JSON adapter cannot
    /// materialise, cases supplying external variable bindings the harness does
    /// not thread, and documented reference divergences — so a case entering or
    /// leaving a category (the harness gaining a capability, the corpus
    /// changing) is a visible test change. The classification mirrors
    /// <see cref="JsonataConformanceRunner"/>'s precedence (load error, then
    /// bindings, then divergence) and needs no evaluation.
    /// </summary>
    [TestMethod]
    public void SkipCensus()
    {
        int hostAdapter = 0;
        int externalBindings = 0;
        int divergence = 0;

        foreach(JsonataConformanceCase testCase in JsonataConformanceLoader.LoadRequired())
        {
            if(testCase.LoadError is not null)
            {
                hostAdapter++;
            }
            else if(testCase.HasNonEmptyBindings)
            {
                externalBindings++;
            }
            else if(JsonataReferenceDivergences.TryGetReason(testCase.GroupName, testCase.CaseFile, out _))
            {
                divergence++;
            }
        }

        Assert.AreEqual(2, hostAdapter, "Host-JSON-adapter-unreadable JSONata cases changed; update the census.");
        Assert.AreEqual(7, externalBindings, "External-variable-binding JSONata cases changed; update the census.");
        Assert.AreEqual(2, divergence, "Documented JSONata reference divergences changed; update the census and JsonataReferenceDivergences.");
    }
}
