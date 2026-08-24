using System;
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
using Lumoin.Veritas.Core.Hypertrie.Tracing;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The named-graph rendezvous's contract: a qualifying join builds
/// the shared graph set once and reuses it, answers agree with the
/// per-graph store, a generation mismatch (a session's working
/// snapshot, or a superseded dataset state) answers on the pinned
/// store, and advancing drops the set for a lazy rebuild.
/// </summary>
[TestClass]
internal sealed class GraphSetRendezvousTests
{
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The fixture's graph id.</summary>
    private static TermId Graph { get; } = TermId.FromEncoded(7_000);

    /// <summary>Builds the per-graph store and a rendezvous over a two-graph source.</summary>
    /// <param name="generation">The initial generation token.</param>
    /// <returns>The graph's store and the rendezvous.</returns>
    private async Task<(HypertrieGraphStore Store, GraphSetRendezvous Rendezvous)> CreateAsync(object generation)
    {
        List<EncodedTriple> triples =
        [
            EncodedTriple.FromEncoded(1, 10, 2),
            EncodedTriple.FromEncoded(1, 11, 3),
            EncodedTriple.FromEncoded(4, 10, 2),
            EncodedTriple.FromEncoded(4, 11, 5),
        ];
        List<EncodedTriple> otherGraph = [EncodedTriple.FromEncoded(9, 10, 9)];

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);
        GraphSetRendezvous rendezvous = new(
            generation,
            () => new Dictionary<TermId, IEnumerable<EncodedTriple>>
            {
                [Graph] = triples,
                [TermId.FromEncoded(7_001)] = otherGraph,
            },
            QueryEnginePolicy.Default);

        return (store, rendezvous);
    }

    /// <summary>A two-pattern join the policy routes to the columnar set.</summary>
    /// <param name="registry">The variable registry.</param>
    /// <returns>The pattern.</returns>
    private static BasicGraphPattern Join(VariableRegistry registry)
    {
        Variable s = registry.GetOrAdd("s");
        Variable o = registry.GetOrAdd("o");
        Variable o2 = registry.GetOrAdd("o2");

        return new BasicGraphPattern(
            [
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(10)), PatternPosition.OfVariable(o)),
                new TriplePattern(PatternPosition.OfVariable(s), PatternPosition.Bound(TermId.FromEncoded(11)), PatternPosition.OfVariable(o2)),
            ],
            registry);
    }

    /// <summary>Drains a solution stream into order-insensitive fingerprints.</summary>
    /// <param name="solutions">The stream.</param>
    /// <returns>The sorted fingerprints.</returns>
    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<Solution> solutions)
    {
        List<string> fingerprints = [];
        await foreach(Solution solution in solutions.ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(";", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => $"{binding.Variable.Id}={binding.Value.Encoded}")));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }

    [TestMethod]
    public async Task QualifyingJoinBuildsOnceReusesAfterAndAgreesWithTheStore()
    {
        object generation = new();
        (HypertrieGraphStore store, GraphSetRendezvous rendezvous) = await CreateAsync(generation).ConfigureAwait(false);

        List<EngineSelectionReason> reasons = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                reasons.Add(evt.SelectionReason);
            }
        }

        VariableRegistry registry = new();
        List<string> first = await DrainAsync(rendezvous.QueryAsync(
            generation, Graph, store, Join(registry), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> second = await DrainAsync(rendezvous.QueryAsync(
            generation, Graph, store, Join(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> direct = await DrainAsync(store.QueryAsync(
            Join(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.IsGreaterThan(0, direct.Count);
        Assert.AreSequenceEqual(direct, first);
        Assert.AreSequenceEqual(direct, second);
        Assert.HasCount(2, reasons);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, reasons[0]);
        Assert.AreEqual(EngineSelectionReason.ViewReused, reasons[1]);
    }

    [TestMethod]
    public async Task GenerationMismatchAnswersOnThePinnedStore()
    {
        object generation = new();
        (HypertrieGraphStore store, GraphSetRendezvous rendezvous) = await CreateAsync(generation).ConfigureAwait(false);

        List<EngineSelectionReason> reasons = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                reasons.Add(evt.SelectionReason);
            }
        }

        //A session-style never-matching token: correct answers from
        //the pinned store, no set build.
        List<string> viaMismatch = await DrainAsync(rendezvous.QueryAsync(
            new object(), Graph, store, Join(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        List<string> direct = await DrainAsync(store.QueryAsync(
            Join(new VariableRegistry()), TimeProvider.System, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.AreSequenceEqual(direct, viaMismatch);
        Assert.HasCount(1, reasons);
        Assert.AreEqual(EngineSelectionReason.SnapshotSuperseded, reasons[0]);
    }

    [TestMethod]
    public async Task AdvanceDropsTheSetAndTheNewGenerationRebuilds()
    {
        object firstGeneration = new();
        (HypertrieGraphStore store, GraphSetRendezvous rendezvous) = await CreateAsync(firstGeneration).ConfigureAwait(false);

        List<EngineSelectionReason> reasons = [];
        void Capture(in QueryTraceEvent evt)
        {
            if(evt.Kind == QueryTraceEventKind.EngineSelected)
            {
                reasons.Add(evt.SelectionReason);
            }
        }

        _ = await DrainAsync(rendezvous.QueryAsync(
            firstGeneration, Graph, store, Join(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        object secondGeneration = new();
        rendezvous.Advance(secondGeneration, () => new Dictionary<TermId, IEnumerable<EncodedTriple>>
        {
            [Graph] = [EncodedTriple.FromEncoded(1, 10, 2), EncodedTriple.FromEncoded(1, 11, 3)],
        });

        //The old generation's snapshot falls back; the new one
        //rebuilds lazily.
        _ = await DrainAsync(rendezvous.QueryAsync(
            firstGeneration, Graph, store, Join(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);
        _ = await DrainAsync(rendezvous.QueryAsync(
            secondGeneration, Graph, store, Join(new VariableRegistry()), TimeProvider.System, traceHandler: Capture, cancellationToken: TestContext.CancellationToken)).ConfigureAwait(false);

        Assert.HasCount(3, reasons);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, reasons[0]);
        Assert.AreEqual(EngineSelectionReason.SnapshotSuperseded, reasons[1]);
        Assert.AreEqual(EngineSelectionReason.ViewBuilt, reasons[2]);
    }
}
