using System;
using System.Buffers;
using System.Linq;
using System.Text.Json;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Verisync.Core;
using Lumoin.Verisync.Json;

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
/// JSON state serialization is exercised here: the test project is exempt from
/// the <c>System.Text.Json</c> ban, so <c>Verisync.Json.CrdtStateJson</c>
/// round-trips an <see cref="OrSetState{T}"/> with a test-supplied element
/// (de)serializer carrying the Veritas triple payload. The matching CBOR state
/// serializers are not yet in <c>Verisync.Cbor</c> — they are the ones that will
/// come from <c>Lumoin.Veritas.Cbor</c> — so the CBOR round-trip is still pending.
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

    /// <summary>An OR-Set of triples survives a JSON state round-trip through <c>Verisync.Json</c>, the element payload (de)serialized by the test-supplied triple codec — the application-injected serialization seam.</summary>
    [TestMethod]
    public void OrSetStateRoundTripsThroughJson()
    {
        ReplicaId a = Replica(0x0A);
        OrSet<EncodedTriple> original = OrSet<EncodedTriple>.Empty
            .Add(EncodedTriple.FromEncoded(1, 100, 2), a)
            .Add(EncodedTriple.FromEncoded(2, 100, 3), a);

        SerializeMessageDelegate<OrSetState<EncodedTriple>> serialize = CrdtStateJson.CreateOrSetStateSerializer<EncodedTriple>(WriteTriple);
        DeserializeMessageDelegate<OrSetState<EncodedTriple>> deserialize = CrdtStateJson.CreateOrSetStateDeserializer<EncodedTriple>(ReadTriple);

        ArrayBufferWriter<byte> buffer = new();
        serialize(original.ToState(), buffer);
        OrSet<EncodedTriple> roundTripped = OrSet<EncodedTriple>.FromState(deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory)));

        Assert.AreEqual(original, roundTripped);
    }

    /// <summary>Writes a triple payload as a JSON object of its three encoded ids — the element serializer the OR-Set state serializer calls per element.</summary>
    /// <param name="writer">The JSON writer positioned at a value.</param>
    /// <param name="triple">The triple to write.</param>
    private static void WriteTriple(Utf8JsonWriter writer, EncodedTriple triple)
    {
        writer.WriteStartObject();
        writer.WriteNumber("s", triple.Subject.Encoded);
        writer.WriteNumber("p", triple.Predicate.Encoded);
        writer.WriteNumber("o", triple.Object.Encoded);
        writer.WriteEndObject();
    }

    /// <summary>Reads a triple payload back from its three encoded ids — the element deserializer the OR-Set state deserializer calls per element.</summary>
    /// <param name="element">The JSON element holding one triple object.</param>
    /// <returns>The triple.</returns>
    private static EncodedTriple ReadTriple(JsonElement element)
    {
        return EncodedTriple.FromEncoded(
            element.GetProperty("s").GetUInt32(),
            element.GetProperty("p").GetUInt32(),
            element.GetProperty("o").GetUInt32());
    }
}
