using System;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// Wires the Lumoin.Verisync observed-remove set against Veritas triple
/// payloads: two replicas add overlapping triple sets and converge to their
/// union on merge — commutatively, associatively, and idempotently — the
/// conflict-free building block under an eventual active-active graph. The
/// Verisync packages are consumed here, in a test project (exempt from the
/// central package-scope rules), ahead of any production replication library.
/// </summary>
/// <remarks>
/// State serialization is not exercised here: the JSON CRDT-state serializers
/// (<c>Verisync.Json.CrdtStateJson</c>) reach <c>System.Text.Json</c>, which the
/// test project's banned-API list forbids, and the matching CBOR serializers are
/// not yet in <c>Verisync.Cbor</c> (they are the ones that will come from
/// <c>Lumoin.Veritas.Cbor</c>). The payload round-trip belongs where those
/// serializers are available — a later step.
/// </remarks>
[TestClass]
internal sealed class VerisyncOrSetWiringTests
{
    /// <summary>A deterministic replica id whose bytes are all <paramref name="seed"/> — no entropy source, so the test is reproducible.</summary>
    /// <param name="seed">The byte every position of the id carries.</param>
    /// <returns>The replica id.</returns>
    private static ReplicaId Replica(byte seed)
    {
        Span<byte> bytes = stackalloc byte[ReplicaId.Size];
        bytes.Fill(seed);

        return ReplicaId.FromSpan(bytes);
    }

    /// <summary>Two replicas that added overlapping triple sets converge to the union, and the merge is commutative and idempotent.</summary>
    [TestMethod]
    public void TwoReplicasConvergeToTheUnionOnMerge()
    {
        ReplicaId a = Replica(0x0A);
        ReplicaId b = Replica(0x0B);

        EncodedTriple shared = EncodedTriple.FromEncoded(2, 100, 3);
        EncodedTriple onlyA = EncodedTriple.FromEncoded(1, 100, 2);
        EncodedTriple onlyB = EncodedTriple.FromEncoded(3, 100, 4);

        OrSet<EncodedTriple> replicaA = OrSet<EncodedTriple>.Empty.Add(onlyA, a).Add(shared, a);
        OrSet<EncodedTriple> replicaB = OrSet<EncodedTriple>.Empty.Add(shared, b).Add(onlyB, b);

        OrSet<EncodedTriple> merged = replicaA.Merge(replicaB);

        CollectionAssert.AreEquivalent(new[] { onlyA, shared, onlyB }, merged.Elements.ToList());

        //Join-semilattice merge: order-independent and absorbing.
        Assert.IsTrue(replicaB.Merge(replicaA).Equals(merged), "Merge must be commutative.");
        Assert.IsTrue(merged.Merge(merged).Equals(merged), "Merge must be idempotent.");
    }

    /// <summary>An add concurrent with a remove of the same triple survives, because the remove only affects the additions it has observed.</summary>
    [TestMethod]
    public void ConcurrentAddSurvivesARemoveItWasNotObservedBy()
    {
        ReplicaId a = Replica(0x0A);
        ReplicaId b = Replica(0x0B);

        EncodedTriple triple = EncodedTriple.FromEncoded(1, 100, 2);

        OrSet<EncodedTriple> removedByA = OrSet<EncodedTriple>.Empty.Add(triple, a).Remove(triple);
        OrSet<EncodedTriple> addedByB = OrSet<EncodedTriple>.Empty.Add(triple, b);

        OrSet<EncodedTriple> merged = removedByA.Merge(addedByB);

        Assert.IsTrue(merged.Contains(triple), "The concurrent, unobserved add must survive the remove.");
    }
}
