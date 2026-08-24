using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// Wires the Lumoin.Verisync replication layer to the Veritas graph engine: two
/// replicas of a replicated triple set converge, and each — building a graph
/// store over the converged set — answers the same query identically. Verisync
/// verifies the replication protocol itself (its own suite, TLA+ included); the
/// only part that is ours, and the only part asserted here, is that the
/// protocol's output drives the Veritas engine consistently.
/// </summary>
[TestClass]
internal sealed class VerisyncEngineConvergenceTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The "s p o" fingerprints the converged replicated set is expected to answer through the engine.</summary>
    private static readonly string[] ExpectedConvergedTriples = ["1 100 2", "2 100 3", "3 100 4"];

    /// <summary>A deterministic replica id whose bytes are all <paramref name="seed"/> — no entropy source, so the test is reproducible.</summary>
    /// <param name="seed">The byte every position of the id carries.</param>
    /// <returns>The replica id.</returns>
    private static ReplicaId Replica(byte seed)
    {
        Span<byte> bytes = stackalloc byte[ReplicaId.Size];
        bytes.Fill(seed);

        return ReplicaId.FromSpan(bytes);
    }

    /// <summary>
    /// Two replicas of a replicated triple set merge, each builds a Veritas graph
    /// store over the converged set, and the all-variables query answers
    /// identically — convergence observed through the graph engine, not only the
    /// replicated set.
    /// </summary>
    [TestMethod]
    public async Task ReplicatedTriplesConvergeThroughTheQueryEngine()
    {
        ReplicaId a = Replica(0x0A);
        ReplicaId b = Replica(0x0B);

        EncodedTriple onlyA = EncodedTriple.FromEncoded(1, 100, 2);
        EncodedTriple shared = EncodedTriple.FromEncoded(2, 100, 3);
        EncodedTriple onlyB = EncodedTriple.FromEncoded(3, 100, 4);

        OrSet<EncodedTriple> replicaA = OrSet<EncodedTriple>.Empty.Add(onlyA, a).Add(shared, a);
        OrSet<EncodedTriple> replicaB = OrSet<EncodedTriple>.Empty.Add(shared, b).Add(onlyB, b);

        List<string> answersFromA = await QueryAllTriplesAsync(replicaA.Merge(replicaB).Elements).ConfigureAwait(false);
        List<string> answersFromB = await QueryAllTriplesAsync(replicaB.Merge(replicaA).Elements).ConfigureAwait(false);

        Assert.AreSequenceEqual(answersFromA, answersFromB);
        Assert.AreSequenceEqual(ExpectedConvergedTriples, answersFromA, SequenceOrder.InAnyOrder);
    }

    /// <summary>Builds a Veritas graph store over the triples and returns every triple the all-variables query answers, as sorted "s p o" fingerprints — the engine's view of the replicated set.</summary>
    /// <param name="triples">The converged triple set.</param>
    /// <returns>The sorted query-answer fingerprints.</returns>
    private async Task<List<string>> QueryAllTriplesAsync(IEnumerable<EncodedTriple> triples)
    {
        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync([.. triples], VeritasHashing.Default, TestContext.CancellationToken).ConfigureAwait(false);

        VariableRegistry registry = new();
        Variable subject = registry.GetOrAdd("s");
        Variable predicate = registry.GetOrAdd("p");
        Variable @object = registry.GetOrAdd("o");
        BasicGraphPattern query = new(
            [new TriplePattern(PatternPosition.OfVariable(subject), PatternPosition.OfVariable(predicate), PatternPosition.OfVariable(@object))],
            registry);

        List<string> fingerprints = [];
        await foreach(Solution solution in store.QueryAsync(query, TimeProvider.System, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            fingerprints.Add(string.Join(" ", solution.Bindings.OrderBy(binding => binding.Variable.Id).Select(binding => binding.Value.Encoded)));
        }

        fingerprints.Sort(StringComparer.Ordinal);

        return fingerprints;
    }
}
