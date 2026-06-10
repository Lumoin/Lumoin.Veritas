using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The join-strategy selector's contract: the fan-out estimator reads exact
/// per-key group statistics off the index; the manual policy passes the flags
/// through verbatim; the adaptive policy engages the factorising star and
/// chain routes only where the estimated compression clears the time
/// thresholds — high fan-out engages, the property-table shape (fan-out one)
/// stays on the streamed join — and the engaged routes answer identically to
/// the default routing.
/// </summary>
[TestClass]
internal sealed class JoinStrategySelectorTests
{
    /// <summary>The first star arm's predicate.</summary>
    private const uint P1 = 10;

    /// <summary>The second star arm's predicate.</summary>
    private const uint P2 = 20;

    /// <summary>The third star arm's predicate.</summary>
    private const uint P3 = 30;

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A three-arm star fixture: each subject carries <paramref name="fan"/> objects on each predicate.</summary>
    /// <param name="subjects">The subject count.</param>
    /// <param name="fan">The per-arm object count per subject.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> StarFixture(int subjects, int fan)
    {
        List<EncodedTriple> triples = [];
        for(uint s = 0; s < subjects; s++)
        {
            uint subject = 1_000 + s;
            for(uint j = 0; j < fan; j++)
            {
                triples.Add(EncodedTriple.FromEncoded(subject, P1, 10_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P2, 20_000 + (s * 100) + j));
                triples.Add(EncodedTriple.FromEncoded(subject, P3, 30_000 + (s * 100) + j));
            }
        }

        return triples;
    }

    /// <summary>The three-arm star query <c>?s p1 ?o1 . ?s p2 ?o2 . ?s p3 ?o3</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern StarQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3"),
            ],
            registry);
    }

    /// <summary>The pattern <c>?subject {predicate} ?object</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <param name="predicate">The bound predicate.</param>
    /// <param name="subjectName">The subject variable name.</param>
    /// <param name="objectName">The object variable name.</param>
    /// <returns>The pattern.</returns>
    private static TriplePattern EdgePattern(VariableRegistry registry, uint predicate, string subjectName, string objectName)
    {
        return new TriplePattern(
            PatternPosition.OfVariable(registry.GetOrAdd(subjectName)),
            PatternPosition.Bound(TermId.FromEncoded(predicate)),
            PatternPosition.OfVariable(registry.GetOrAdd(objectName)));
    }

    [TestMethod]
    public void EstimatorReadsExactPerKeyFanOut()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 50, fan: 7));
        VariableRegistry registry = new();
        TriplePattern pattern = EdgePattern(registry, P1, "s", "o");

        Assert.IsTrue(JoinStrategySelector.TryEstimateKeyFanOut(index, pattern, registry.GetOrAdd("s"), out double fanOut));
        Assert.AreEqual(7.0, fanOut, 0.0001);

        //A predicate matching nothing estimates a zero fan-out, not a failure.
        TriplePattern empty = EdgePattern(registry, 999, "s", "o");
        Assert.IsTrue(JoinStrategySelector.TryEstimateKeyFanOut(index, empty, registry.GetOrAdd("s"), out double none));
        Assert.AreEqual(0.0, none, 0.0001);
    }

    [TestMethod]
    public void ManualPolicyPassesTheFlagsThroughVerbatim()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 10, fan: 20));
        BasicGraphPattern query = StarQuery(new VariableRegistry());

        QueryEnginePolicy manual = QueryEnginePolicy.Default with { PreferFactorizedStar = true };
        JoinStrategyChoice choice = JoinStrategySelector.Choose(index, query, manual);

        Assert.IsTrue(choice.FactorizedStar);
        Assert.IsFalse(choice.FactorizedChain);
        Assert.IsTrue(choice.SemijoinReduction);

        //Without the flag and without adaptivity, nothing engages — even on a
        //fan-out the adaptive policy would take.
        JoinStrategyChoice off = JoinStrategySelector.Choose(index, query, QueryEnginePolicy.Default);

        Assert.IsFalse(off.FactorizedStar);
        Assert.IsFalse(off.FactorizedChain);
    }

    [TestMethod]
    public void AdaptivePolicyEngagesTheStarOnFanOutAndDeclinesThePropertyTable()
    {
        QueryEnginePolicy adaptive = QueryEnginePolicy.Default with { AdaptiveJoinStrategies = true };

        //Fan-out 20 per arm: estimated compression 20³/60 ≈ 133 — engaged.
        ColumnarTripleIndex fanned = ColumnarTripleIndex.Build(StarFixture(subjects: 10, fan: 20));
        JoinStrategyChoice engaged = JoinStrategySelector.Choose(fanned, StarQuery(new VariableRegistry()), adaptive);

        Assert.IsTrue(engaged.FactorizedStar);

        //Fan-out 1 per arm (the property-table shape): compression 1/3 — declined.
        ColumnarTripleIndex flat = ColumnarTripleIndex.Build(StarFixture(subjects: 200, fan: 1));
        JoinStrategyChoice declined = JoinStrategySelector.Choose(flat, StarQuery(new VariableRegistry()), adaptive);

        Assert.IsFalse(declined.FactorizedStar);
        Assert.IsFalse(declined.FactorizedChain);
    }

    /// <summary>A chain fixture <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c> with the given fan-outs per hop.</summary>
    /// <param name="hubs">The distinct hub count.</param>
    /// <param name="fanA">The <c>?a</c> count per hub.</param>
    /// <param name="fanB">The <c>?b</c> count per hub.</param>
    /// <param name="fanC">The <c>?c</c> count per <c>?b</c>.</param>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> ChainFixture(int hubs, int fanA, int fanB, int fanC)
    {
        List<EncodedTriple> triples = [];
        for(uint h = 0; h < hubs; h++)
        {
            uint hub = 1_000 + h;
            for(uint a = 0; a < fanA; a++)
            {
                triples.Add(EncodedTriple.FromEncoded(50_000 + (h * 100) + a, P1, hub));
            }

            for(uint b = 0; b < fanB; b++)
            {
                uint branch = 60_000 + (h * 100) + b;
                triples.Add(EncodedTriple.FromEncoded(hub, P2, branch));
                for(uint c = 0; c < fanC; c++)
                {
                    triples.Add(EncodedTriple.FromEncoded(branch, P3, 70_000 + (((h * 100) + b) * 100) + c));
                }
            }
        }

        return triples;
    }

    /// <summary>The chain query <c>?a p1 ?x . ?x p2 ?b . ?b p3 ?c</c>.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The query.</returns>
    private static BasicGraphPattern ChainQuery(VariableRegistry registry)
    {
        return new BasicGraphPattern(
            [
                EdgePattern(registry, P1, "a", "x"),
                EdgePattern(registry, P2, "x", "b"),
                EdgePattern(registry, P3, "b", "c"),
            ],
            registry);
    }

    [TestMethod]
    public void AdaptivePolicyEngagesTheChainOnTwoSidedFanOutOnly()
    {
        QueryEnginePolicy adaptive = QueryEnginePolicy.Default with { AdaptiveJoinStrategies = true };

        //Two-sided fan-out: fanA=30, S=15·6=90 → compression 2700/120 = 22.5 — engaged.
        ColumnarTripleIndex fanned = ColumnarTripleIndex.Build(ChainFixture(hubs: 20, fanA: 30, fanB: 15, fanC: 6));
        JoinStrategyChoice engaged = JoinStrategySelector.Choose(fanned, ChainQuery(new VariableRegistry()), adaptive);

        Assert.IsTrue(engaged.FactorizedChain);
        Assert.IsFalse(engaged.FactorizedStar);

        //One-sided fan-out: fanA=30, S=1 → compression 30/31 < 1 — declined.
        ColumnarTripleIndex oneSided = ColumnarTripleIndex.Build(ChainFixture(hubs: 50, fanA: 30, fanB: 1, fanC: 1));
        JoinStrategyChoice declined = JoinStrategySelector.Choose(oneSided, ChainQuery(new VariableRegistry()), adaptive);

        Assert.IsFalse(declined.FactorizedChain);
    }

    /// <summary>Runs the query through a rendezvous with the policy and returns sorted answer fingerprints.</summary>
    /// <param name="store">The system of record.</param>
    /// <param name="policy">The engine policy.</param>
    /// <param name="query">The query.</param>
    /// <returns>The sorted fingerprints.</returns>
    private async Task<List<string>> RendezvousFingerprintsAsync(HypertrieGraphStore store, QueryEnginePolicy policy, BasicGraphPattern query)
    {
        QueryEngineRendezvous rendezvous = new(store, policy);

        List<string> fingerprints = [];
        await foreach(Solution solution in rendezvous.Query(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(System.StringComparer.Ordinal);

        return fingerprints;
    }

    [TestMethod]
    public async Task AdaptiveRoutingAnswersIdenticallyToTheDefault()
    {
        //Both regimes: the fan-out star (where adaptivity engages the
        //factorising route) and the property table (where it declines) must
        //answer exactly as the default manual routing does.
        foreach(List<EncodedTriple> fixture in (List<EncodedTriple>[])[StarFixture(subjects: 8, fan: 12), StarFixture(subjects: 100, fan: 1)])
        {
            HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
            BasicGraphPattern query = StarQuery(new VariableRegistry());

            List<string> viaDefault = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default, query).ConfigureAwait(false);
            List<string> viaAdaptive = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default with { AdaptiveJoinStrategies = true }, query).ConfigureAwait(false);

            Assert.IsGreaterThan(0, viaDefault.Count);
            CollectionAssert.AreEqual(viaDefault, viaAdaptive);
        }
    }
}
