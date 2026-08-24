using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Planning;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The three-rotation order set's contract: every bound set is
/// answerable, rotation-compatible joins agree with the six-order
/// index exactly, the planner finds compatible global orders where
/// they exist and reports the cyclic shapes where they cannot, and
/// the rendezvous falls back to the system of record for those —
/// correct answers either way.
/// </summary>
[TestClass]
internal sealed class ColumnarThreeRotationTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Drains an evaluator into order-insensitive solution fingerprints.</summary>
    /// <param name="evaluator">The evaluator.</param>
    /// <returns>The sorted fingerprints.</returns>
    private async Task<List<string>> DrainAsync(ColumnarBasicGraphPatternEvaluator evaluator)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in evaluator.EvaluateAsync(TestContext.CancellationToken).ConfigureAwait(false))
        {
            fingerprints.Add(FingerprintOf(solution));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    /// <summary>Builds an order-insensitive fingerprint of one solution.</summary>
    /// <param name="solution">The solution.</param>
    /// <returns>The fingerprint.</returns>
    private static string FingerprintOf(Solution solution)
    {
        return string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}"));
    }

    /// <summary>A chain-of-two join: both patterns bind the predicate, so each is forced onto the POS rotation — compatible, with the global order (z, y, x).</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern ChainJoin(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(100)), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(TermId.FromEncoded(101)), PatternPosition.OfVariable(z)),
            ],
            registry);
    }

    /// <summary>The triangle: three predicate-bound patterns whose induced orders form a cycle — rotation-incompatible by construction.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern Triangle(VariableRegistry registry)
    {
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(x), PatternPosition.Bound(TermId.FromEncoded(100)), PatternPosition.OfVariable(y)),
                new TriplePattern(PatternPosition.OfVariable(y), PatternPosition.Bound(TermId.FromEncoded(100)), PatternPosition.OfVariable(z)),
                new TriplePattern(PatternPosition.OfVariable(z), PatternPosition.Bound(TermId.FromEncoded(100)), PatternPosition.OfVariable(x)),
            ],
            registry);
    }

    /// <summary>A small fixture with chain joins, a triangle, and a subject-object-bound pattern's data.</summary>
    /// <returns>The triples.</returns>
    private static List<EncodedTriple> Fixture()
    {
        List<EncodedTriple> triples = [];

        //Chain data: x --100--> y --101--> z.
        for(uint i = 0; i < 40; i++)
        {
            triples.Add(EncodedTriple.FromEncoded(1_000 + i, 100, 2_000 + (i % 8)));
            triples.Add(EncodedTriple.FromEncoded(2_000 + (i % 8), 101, 3_000 + (i % 4)));
        }

        //Triangle data over predicate 100.
        triples.Add(EncodedTriple.FromEncoded(10, 100, 11));
        triples.Add(EncodedTriple.FromEncoded(11, 100, 12));
        triples.Add(EncodedTriple.FromEncoded(12, 100, 10));

        return triples;
    }

    [TestMethod]
    public void PlannerFindsACompatibleOrderForTheChainJoin()
    {
        VariableRegistry registry = new();
        BasicGraphPattern query = ChainJoin(registry);

        IReadOnlyList<Variable>? order = ColumnarRotationPlanner.TryPlanGlobalOrder(ColumnarOrderSetMode.ThreeRotations, query);

        Assert.IsNotNull(order);
        Assert.HasCount(3, order);

        //Both patterns force POS (object before subject): z before
        //y, y before x.
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");
        List<Variable> ordered = [.. order];
        Assert.IsLessThan(ordered.IndexOf(y), ordered.IndexOf(z));
        Assert.IsLessThan(ordered.IndexOf(x), ordered.IndexOf(y));
    }

    [TestMethod]
    public void PlannerReportsTheTriangleAsRotationIncompatible()
    {
        VariableRegistry registry = new();

        Assert.IsNull(ColumnarRotationPlanner.TryPlanGlobalOrder(ColumnarOrderSetMode.ThreeRotations, Triangle(registry)));
    }

    [TestMethod]
    public void ThreeRotationIndexMaterialisesExactlyTheRotations()
    {
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(Fixture(), ColumnarOrderSetMode.ThreeRotations);

        //SPO, POS, OSP are permutation indices 0, 3, and 4.
        Assert.IsTrue(index.IsPermutationAvailable(0));
        Assert.IsFalse(index.IsPermutationAvailable(1));
        Assert.IsFalse(index.IsPermutationAvailable(2));
        Assert.IsTrue(index.IsPermutationAvailable(3));
        Assert.IsTrue(index.IsPermutationAvailable(4));
        Assert.IsFalse(index.IsPermutationAvailable(5));
    }

    [TestMethod]
    public async Task ThreeRotationsAgreeWithSixOrdersOnACompatibleJoin()
    {
        List<EncodedTriple> triples = Fixture();
        ColumnarTripleIndex six = ColumnarTripleIndex.Build(triples, ColumnarOrderSetMode.AllSixOrders);
        ColumnarTripleIndex three = ColumnarTripleIndex.Build(triples, ColumnarOrderSetMode.ThreeRotations);

        VariableRegistry sixRegistry = new();
        VariableRegistry threeRegistry = new();
        BasicGraphPattern sixQuery = ChainJoin(sixRegistry);
        BasicGraphPattern threeQuery = ChainJoin(threeRegistry);

        List<string> sixSolutions = await DrainAsync(
            new ColumnarBasicGraphPatternEvaluator(six, sixQuery, Planners.FirstOccurrence(sixQuery), TimeProvider.System)).ConfigureAwait(false);
        List<string> threeSolutions = await DrainAsync(
            new ColumnarBasicGraphPatternEvaluator(three, threeQuery, Planners.FirstOccurrence(threeQuery), TimeProvider.System)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, sixSolutions.Count);
        Assert.AreSequenceEqual(sixSolutions, threeSolutions);
    }

    [TestMethod]
    public void RotationIncompatibleQueriesAreRejectedAtTheEvaluator()
    {
        ColumnarTripleIndex three = ColumnarTripleIndex.Build(Fixture(), ColumnarOrderSetMode.ThreeRotations);
        VariableRegistry registry = new();
        BasicGraphPattern triangle = Triangle(registry);

        Assert.IsFalse(ColumnarBasicGraphPatternEvaluator.CanEvaluate(three, triangle));
        Assert.ThrowsExactly<ArgumentException>(() =>
            _ = new ColumnarBasicGraphPatternEvaluator(three, triangle, Planners.FirstOccurrence(triangle), TimeProvider.System));
    }

    [TestMethod]
    public async Task RendezvousFallsBackToTheSystemOfRecordForTheTriangle()
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(Fixture(), VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        QueryEnginePolicy policy = QueryEnginePolicy.Default with { OrderSetMode = ColumnarOrderSetMode.ThreeRotations };
        QueryEngineRendezvous rendezvous = new(store, policy);

        VariableRegistry registry = new();
        List<string> viaRendezvous = [];
        await foreach(Solution solution in rendezvous.QueryAsync(Triangle(registry), TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            viaRendezvous.Add(FingerprintOf(solution));
        }

        viaRendezvous.Sort(StringComparer.Ordinal);

        VariableRegistry directRegistry = new();
        List<string> direct = [];
        await foreach(Solution solution in store.QueryAsync(Triangle(directRegistry), TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            direct.Add(FingerprintOf(solution));
        }

        direct.Sort(StringComparer.Ordinal);

        //The triangle fixture has exactly the three cyclic matches
        //(one per starting corner); the fallback answers them
        //identically to the system of record.
        Assert.IsGreaterThan(0, direct.Count);
        Assert.AreSequenceEqual(direct, viaRendezvous);
    }
}
