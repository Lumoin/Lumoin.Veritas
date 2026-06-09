using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Lumoin.Veritas.Cbor;
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
/// The OR-Set state round-trips through both serializers, parametrized: JSON via
/// <c>Verisync.Json.CrdtStateJson</c> (the test project is exempt from the
/// <c>System.Text.Json</c> ban), and CBOR via a <c>Veritas.Cbor</c> codec written
/// here. The CBOR codec stands in for the CRDT-state serializers that will move
/// into <c>Verisync.Cbor</c> once <c>Lumoin.Veritas.Cbor</c> is published and
/// Verisync can consume it; until then Veritas supplies them.
/// </remarks>
[TestClass]
internal sealed class VerisyncOrSetWiringTests
{
    /// <summary>The CBOR conformance options for the state codec — permissive, since this is round-trip wiring rather than canonical-form conformance.</summary>
    private static CborSerializerOptions CborOptions { get; } = CborSerializerOptions.Default(CborConformanceMode.Lax);

    /// <summary>A deterministic replica id whose bytes are all <paramref name="seed"/> — no entropy source, so the test is reproducible.</summary>
    /// <param name="seed">The byte every position of the id carries.</param>
    /// <returns>The replica id.</returns>
    private static ReplicaId Replica(byte seed)
    {
        Span<byte> bytes = stackalloc byte[ReplicaId.Size];
        bytes.Fill(seed);

        return ReplicaId.FromSpan(bytes);
    }

    /// <summary>An OR-Set of triples for the serialization round-trips: two adds by one replica.</summary>
    /// <returns>The set.</returns>
    private static OrSet<EncodedTriple> SampleSet()
    {
        ReplicaId a = Replica(0x0A);

        return OrSet<EncodedTriple>.Empty
            .Add(EncodedTriple.FromEncoded(1, 100, 2), a)
            .Add(EncodedTriple.FromEncoded(2, 100, 3), a);
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
        Assert.AreEqual(merged, replicaB.Merge(replicaA), "Merge must be commutative.");
        Assert.AreEqual(merged, merged.Merge(merged), "Merge must be idempotent.");
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

    /// <summary>The OR-Set state round-trips through each serializer with a test-supplied triple element codec — the application-injected serialization seam.</summary>
    /// <param name="cbor">Whether to use the CBOR codec; otherwise JSON.</param>
    [TestMethod]
    [DataRow(false, DisplayName = "json")]
    [DataRow(true, DisplayName = "cbor")]
    public void OrSetStateRoundTrips(bool cbor)
    {
        OrSet<EncodedTriple> original = SampleSet();
        OrSetState<EncodedTriple> state = original.ToState();

        byte[] bytes = cbor ? SerializeCbor(state) : SerializeJson(state);
        OrSetState<EncodedTriple> restored = cbor ? DeserializeCbor(bytes) : DeserializeJson(bytes);

        Assert.AreEqual(original, OrSet<EncodedTriple>.FromState(restored));
    }

    /// <summary>The CBOR state encoding is more compact than the JSON one for the same OR-Set — the density the eventual Veritas.Cbor-backed serializers buy.</summary>
    [TestMethod]
    public void CborStateIsMoreCompactThanJson()
    {
        OrSetState<EncodedTriple> state = SampleSet().ToState();

        Assert.IsLessThan(SerializeJson(state).Length, SerializeCbor(state).Length);
    }

    /// <summary>Serializes the OR-Set state as JSON through Verisync.Json, the triple element written by <see cref="WriteTriple"/>.</summary>
    /// <param name="state">The state to serialize.</param>
    /// <returns>The JSON bytes.</returns>
    private static byte[] SerializeJson(OrSetState<EncodedTriple> state)
    {
        SerializeMessageDelegate<OrSetState<EncodedTriple>> serialize = CrdtStateJson.CreateOrSetStateSerializer<EncodedTriple>(WriteTriple);
        ArrayBufferWriter<byte> buffer = new();
        serialize(state, buffer);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Deserializes the OR-Set state from JSON through Verisync.Json, the triple element read by <see cref="ReadTriple"/>.</summary>
    /// <param name="bytes">The JSON bytes.</param>
    /// <returns>The state.</returns>
    private static OrSetState<EncodedTriple> DeserializeJson(byte[] bytes)
    {
        DeserializeMessageDelegate<OrSetState<EncodedTriple>> deserialize = CrdtStateJson.CreateOrSetStateDeserializer<EncodedTriple>(ReadTriple);

        return deserialize(new ReadOnlySequence<byte>(bytes));
    }

    /// <summary>Serializes the OR-Set state as CBOR through Veritas.Cbor: the dotted-version-vector set as a pair of arrays — the causal context and the dotted entries — with each triple inlined as its three encoded ids.</summary>
    /// <param name="state">The state to serialize.</param>
    /// <returns>The CBOR bytes.</returns>
    private static byte[] SerializeCbor(OrSetState<EncodedTriple> state)
    {
        ArrayBufferWriter<byte> buffer = new();
        CborWriter writer = new(buffer, CborOptions);
        DottedVersionVectorSetState<EncodedTriple> set = state.Set;

        writer.WriteStartArray(2);

        writer.WriteStartArray(set.Context.Entries.Length);
        foreach(ReplicaCounterEntry entry in set.Context.Entries)
        {
            writer.WriteStartArray(2);
            writer.WriteByteString(entry.Replica.AsSpan());
            writer.WriteInt32(entry.Count);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WriteStartArray(set.Entries.Length);
        foreach(DottedEntry<EncodedTriple> entry in set.Entries)
        {
            writer.WriteStartArray(5);
            writer.WriteByteString(entry.Replica.AsSpan());
            writer.WriteInt32(entry.Counter);
            writer.WriteUInt64(entry.Value.Subject.Encoded);
            writer.WriteUInt64(entry.Value.Predicate.Encoded);
            writer.WriteUInt64(entry.Value.Object.Encoded);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        writer.WriteEndArray();

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Deserializes the OR-Set state from the CBOR <see cref="SerializeCbor"/> writes.</summary>
    /// <param name="bytes">The CBOR bytes.</param>
    /// <returns>The state.</returns>
    private static OrSetState<EncodedTriple> DeserializeCbor(byte[] bytes)
    {
        CborReader reader = new(bytes, CborOptions);
        reader.ReadStartArray();

        int contextCount = reader.ReadStartArray() ?? 0;
        ImmutableArray<ReplicaCounterEntry>.Builder context = ImmutableArray.CreateBuilder<ReplicaCounterEntry>(contextCount);
        for(int i = 0; i < contextCount; i++)
        {
            reader.ReadStartArray();
            byte[] replica = reader.ReadByteString();
            int count = (int)reader.ReadInt64();
            reader.ReadEndArray();
            context.Add(new ReplicaCounterEntry([.. replica], count));
        }

        reader.ReadEndArray();

        int entryCount = reader.ReadStartArray() ?? 0;
        ImmutableArray<DottedEntry<EncodedTriple>>.Builder entries = ImmutableArray.CreateBuilder<DottedEntry<EncodedTriple>>(entryCount);
        for(int i = 0; i < entryCount; i++)
        {
            reader.ReadStartArray();
            byte[] replica = reader.ReadByteString();
            int counter = (int)reader.ReadInt64();
            uint subject = (uint)reader.ReadUInt64();
            uint predicate = (uint)reader.ReadUInt64();
            uint @object = (uint)reader.ReadUInt64();
            reader.ReadEndArray();
            entries.Add(new DottedEntry<EncodedTriple>([.. replica], counter, EncodedTriple.FromEncoded(subject, predicate, @object)));
        }

        reader.ReadEndArray();

        reader.ReadEndArray();

        return new OrSetState<EncodedTriple>(new DottedVersionVectorSetState<EncodedTriple>(new VectorClockState(context.ToImmutable()), entries.ToImmutable()));
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
