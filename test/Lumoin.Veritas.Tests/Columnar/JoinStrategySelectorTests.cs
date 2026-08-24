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
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The join-strategy selector's contract: the fan-out estimator
/// <see cref="ColumnarKeyStatistics"/> exposes reads exact per-key group
/// statistics off the index; the flags-verbatim rule states no engagement of
/// its own; the calibrated rule engages the factorising star and
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

        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, pattern, registry.GetOrAdd("s"), out double fanOut));
        Assert.AreEqual(7.0, fanOut, 0.0001);

        //A predicate matching nothing estimates a zero fan-out, not a failure.
        TriplePattern empty = EdgePattern(registry, 999, "s", "o");
        Assert.IsTrue(ColumnarKeyStatistics.TryEstimateKeyFanOut(index, empty, registry.GetOrAdd("s"), out double none));
        Assert.AreEqual(0.0, none, 0.0001);
    }

    [TestMethod]
    public void ManualPolicyPassesTheFlagsThroughVerbatim()
    {
        //The flags-verbatim rule states no engagement of its own, so the policy flags stand exactly as the
        //caller wrote them — on the very fan-out where the calibrated rule does state one.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 10, fan: 20));
        BasicGraphPattern query = StarQuery(new VariableRegistry());
        JoinSelectionContext context = ContextFor(index, query);

        JoinSelectionDecision manual = JoinStrategySelectors.Manual(in context, TestContext.CancellationToken);

        Assert.AreEqual(FactorizationEngagement.Unspecified, manual.Factorization);
        Assert.AreEqual(JoinStrategySelectorKind.Manual, manual.SelectorKind);

        JoinSelectionDecision calibrated = JoinStrategySelectors.Calibrated(in context, TestContext.CancellationToken);

        Assert.AreEqual(FactorizationEngagement.Star, calibrated.Factorization);
    }

    [TestMethod]
    public void TheCalibratedRuleEngagesTheStarOnFanOutAndDeclinesThePropertyTable()
    {
        //Fan-out 20 per arm: estimated compression 20³/60 ≈ 133 — engaged.
        ColumnarTripleIndex fanned = ColumnarTripleIndex.Build(StarFixture(subjects: 10, fan: 20));

        Assert.AreEqual(FactorizationEngagement.Star, CalibratedDecision(fanned, StarQuery(new VariableRegistry())).Factorization);

        //Fan-out 1 per arm (the property-table shape): compression 1/3 — declined.
        ColumnarTripleIndex flat = ColumnarTripleIndex.Build(StarFixture(subjects: 200, fan: 1));

        Assert.AreEqual(FactorizationEngagement.Unspecified, CalibratedDecision(flat, StarQuery(new VariableRegistry())).Factorization);
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
    public void TheCalibratedRuleEngagesTheChainOnTwoSidedFanOutOnly()
    {
        //Two-sided fan-out: fanA=30, S=15·6=90 → compression 2700/120 = 22.5 — engaged.
        ColumnarTripleIndex fanned = ColumnarTripleIndex.Build(ChainFixture(hubs: 20, fanA: 30, fanB: 15, fanC: 6));

        Assert.AreEqual(FactorizationEngagement.Chain, CalibratedDecision(fanned, ChainQuery(new VariableRegistry())).Factorization);

        //One-sided fan-out: fanA=30, S=1 → compression 30/31 < 1 — declined.
        ColumnarTripleIndex oneSided = ColumnarTripleIndex.Build(ChainFixture(hubs: 50, fanA: 30, fanB: 1, fanC: 1));

        Assert.AreEqual(FactorizationEngagement.Unspecified, CalibratedDecision(oneSided, ChainQuery(new VariableRegistry())).Factorization);
    }

    /// <summary>A consultation context over the real query and view, hinting nothing, with the features the engine would measure.</summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The query.</param>
    /// <returns>The context.</returns>
    private static JoinSelectionContext ContextFor(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        return new JoinSelectionContext(query, index, JoinShapeAnalysis.Describe(index, query, QueryEnginePolicy.Default), default);
    }

    /// <summary>The calibrated rule's decision for the query on the view.</summary>
    /// <param name="index">The columnar view.</param>
    /// <param name="query">The query.</param>
    /// <returns>The decision.</returns>
    private JoinSelectionDecision CalibratedDecision(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        JoinSelectionContext context = ContextFor(index, query);

        return JoinStrategySelectors.Calibrated(in context, TestContext.CancellationToken);
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
        await foreach(Solution solution in rendezvous.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(System.StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Drains the streamed plan and counts its rows — the oracle for <see cref="ColumnarBatchPipeline.TryCount"/>.</summary>
    /// <param name="index">The index.</param>
    /// <param name="query">The query.</param>
    /// <returns>The drained row count.</returns>
    private static long DrainedCount(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, query)!;
        long rows = 0;
        foreach(SolutionBatch batch in ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared))
        {
            rows += batch.Count;
        }

        return rows;
    }

    [TestMethod]
    public void CountWithoutFlattenAgreesWithTheDrainedCount()
    {
        //The star, both regimes: the count must equal the drained row count
        //exactly — the flatten is skipped, not approximated.
        ColumnarTripleIndex fanned = ColumnarTripleIndex.Build(StarFixture(subjects: 10, fan: 12));
        BasicGraphPattern star = StarQuery(new VariableRegistry());

        long? counted = ColumnarBatchPipeline.TryCount(fanned, star, VeritasMemoryPool<uint>.Shared);
        Assert.IsNotNull(counted);
        Assert.AreEqual(DrainedCount(fanned, star), counted.Value);
        Assert.AreEqual(10L * 12 * 12 * 12, counted.Value);

        ColumnarTripleIndex flat = ColumnarTripleIndex.Build(StarFixture(subjects: 50, fan: 1));
        long? flatCounted = ColumnarBatchPipeline.TryCount(flat, StarQuery(new VariableRegistry()), VeritasMemoryPool<uint>.Shared);
        Assert.IsNotNull(flatCounted);
        Assert.AreEqual(50L, flatCounted.Value);

        //The chain factorises depth-2 and counts through the nested form.
        ColumnarTripleIndex chained = ColumnarTripleIndex.Build(ChainFixture(hubs: 6, fanA: 4, fanB: 3, fanC: 2));
        BasicGraphPattern chain = ChainQuery(new VariableRegistry());

        long? chainCounted = ColumnarBatchPipeline.TryCount(chained, chain, VeritasMemoryPool<uint>.Shared);
        Assert.IsNotNull(chainCounted);
        Assert.AreEqual(DrainedCount(chained, chain), chainCounted.Value);
        Assert.AreEqual(6L * 4 * 3 * 2, chainCounted.Value);
    }

    [TestMethod]
    public void DistinctKeysWithoutFlattenAgreesWithTheDrainedDistinct()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(StarFixture(subjects: 9, fan: 5));
        VariableRegistry registry = new();
        BasicGraphPattern star = new(
            [
                EdgePattern(registry, P1, "s", "o1"),
                EdgePattern(registry, P2, "s", "o2"),
                EdgePattern(registry, P3, "s", "o3"),
            ],
            registry);

        //One row per centre subject, no flatten: 9 distinct keys.
        List<SolutionBatch>? batches = ColumnarBatchPipeline.TryDistinctKeys(index, star, [registry.GetOrAdd("s")], VeritasMemoryPool<uint>.Shared);
        Assert.IsNotNull(batches);

        HashSet<uint> keys = [];
        foreach(SolutionBatch batch in batches)
        {
            Assert.HasCount(1, batch.Schema);
            for(int row = 0; row < batch.Count; row++)
            {
                Assert.IsTrue(keys.Add(batch.ColumnOf(0)[row]), "A distinct key repeated.");
            }
        }

        Assert.HasCount(9, keys);

        //The drained oracle: distinct key values of the flat result.
        ColumnarBatchPlan plan = ColumnarBatchPipeline.TryPlan(index, star)!;
        int keyColumn = -1;
        for(int column = 0; column < plan.Schema.Count; column++)
        {
            if(plan.Schema[column] == registry.GetOrAdd("s"))
            {
                keyColumn = column;
            }
        }

        HashSet<uint> drained = [];
        foreach(SolutionBatch batch in ColumnarBatchPipeline.Run(index, plan, VeritasMemoryPool<uint>.Shared))
        {
            for(int row = 0; row < batch.Count; row++)
            {
                drained.Add(batch.ColumnOf(keyColumn)[row]);
            }
        }

        Assert.IsTrue(keys.SetEquals(drained));

        //A branch variable is outside the key — declined.
        Assert.IsNull(ColumnarBatchPipeline.TryDistinctKeys(index, star, [registry.GetOrAdd("o1")], VeritasMemoryPool<uint>.Shared));
    }

    [TestMethod]
    public void CountWithoutFlattenDeclinesANonFactorisableShape()
    {
        //A two-pattern join has no star or chain factorisation; the caller
        //counts by draining instead.
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(ChainFixture(hubs: 5, fanA: 2, fanB: 2, fanC: 2));
        VariableRegistry registry = new();
        BasicGraphPattern twoPattern = new(
            [
                EdgePattern(registry, P1, "a", "x"),
                EdgePattern(registry, P2, "x", "b"),
            ],
            registry);

        Assert.IsNull(ColumnarBatchPipeline.TryCount(index, twoPattern, VeritasMemoryPool<uint>.Shared));
    }

    [TestMethod]
    public async Task CalibratedRoutingAnswersIdenticallyToTheDefault()
    {
        //Both regimes: the fan-out star (where the calibrated rule engages the
        //factorising route) and the property table (where it declines) must
        //answer exactly as the default routing does.
        foreach(List<EncodedTriple> fixture in (List<EncodedTriple>[])[StarFixture(subjects: 8, fan: 12), StarFixture(subjects: 100, fan: 1)])
        {
            HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(fixture, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
            BasicGraphPattern query = StarQuery(new VariableRegistry());

            List<string> viaDefault = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default, query).ConfigureAwait(false);
            List<string> viaCalibrated = await RendezvousFingerprintsAsync(store, QueryEnginePolicy.Default with { JoinRouteSelector = JoinStrategySelectors.Calibrated }, query).ConfigureAwait(false);

            Assert.IsGreaterThan(0, viaDefault.Count);
            Assert.AreSequenceEqual(viaDefault, viaCalibrated);
        }
    }
}
