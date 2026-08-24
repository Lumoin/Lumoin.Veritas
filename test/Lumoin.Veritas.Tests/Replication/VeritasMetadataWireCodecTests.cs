using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Replication;
using Lumoin.Verisync.Core;
using Lumoin.Verisync.Json;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Tests.Replication;

/// <summary>
/// The deployment's production wire encoding of the coordinated metadata record: every arm the record has —
/// identity claims, a baseline intent, a confirmed baseline, an amended policy, a coordinator lease, and each of
/// the absent forms — survives all four codecs the plane's endpoints and its durable store are composed from,
/// by VALUE across the codec rather than by reference; every identifier sixty-four bits wide crosses as text, so
/// no reader that reparses the payload through an IEEE double collapses two lineages into one; a payload that
/// does not read as the message its exchange carries is refused as the one deserialization failure a channel
/// consumer catches rather than surfacing an encoding-specific one; and the production codec agrees with the
/// socket battery's own copy of the body BYTE FOR BYTE in both directions.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS BATTERY EXISTS AT ALL. The metadata plane has no format negotiation: hosts that disagree on this
/// encoding mint records they decline from each other rather than records they merge, so a change to the body is
/// a wire break every host of a deployment takes at once. Pinning the format only through the transports that
/// carry it would leave a change visible solely as a cross-process failure; pinning it here makes the format
/// itself the thing a row fails on.
/// </para>
/// <para>
/// THE PIN ROW IS WHAT LICENSES THE DUPLICATION. The socket battery keeps its own copy of the record body on
/// purpose, so that one file's wire format cannot be moved by an edit made for another file's reasons. That is a
/// check only while the two copies are known to agree, and one row here asserts exactly that: the same decided
/// record encodes to identical bytes under both, and each side reads what the other wrote.
/// </para>
/// <para>
/// NO ROW HERE BINDS A PORT, TOUCHES A FILE, OR READS A MUTABLE STATIC. Every row builds its own deployment,
/// its own records and its own buffers, so the battery runs beside the rest of the suite under method-level
/// parallelism. Nothing here waits on anything: a codec is a pure function of its input.
/// </para>
/// </remarks>
[TestClass]
internal sealed class VeritasMetadataWireCodecTests
{
    /// <summary>The causality digest the baseline arms carry; above two to the fifty-third, so a codec that routed it through a double would lose it.</summary>
    private const ulong CausalityDigestValue = 0x9E3779B97F4A7C15UL;

    /// <summary>The dataset StateId a confirmed baseline carries; likewise above two to the fifty-third.</summary>
    private const ulong StateIdValue = 0xFEEDFACECAFEBEEFUL;

    /// <summary>The term-dictionary epoch a confirmed baseline carries.</summary>
    private const long DictionaryEpochValue = 42L;

    /// <summary>Every record arm survives the decided-record codec — the body a catch-up answer and a dissemination push carry.</summary>
    [TestMethod]
    public void EveryRecordArmSurvivesTheDecidedRecordCodec()
    {
        SerializeMessageDelegate<CommittedMetadataRecord> serialize = VeritasMetadataWireCodec.CreateDecidedRecordSerializer();
        DeserializeMessageDelegate<CommittedMetadataRecord> deserialize = VeritasMetadataWireCodec.CreateDecidedRecordDeserializer();

        foreach(MetadataRecordArm arm in Arms())
        {
            CommittedMetadataRecord committed = CommittedFor(arm.Record);
            ArrayBufferWriter<byte> buffer = new();
            serialize(committed, buffer);

            CommittedMetadataRecord decoded = deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory));

            Assert.AreEqual(committed.Value, decoded.Value, FormattableString.Invariant($"The {arm.Name} arm equals what was encoded, on the record's own element-wise equality."));
            Assert.AreNotSame(committed.Value, decoded.Value, FormattableString.Invariant($"The {arm.Name} arm came back through the codec, so the equality above is structural and not reference identity."));
            Assert.AreEqual(committed, decoded, FormattableString.Invariant($"The whole decided record around the {arm.Name} arm — version, writer, membership and value — equals what was encoded."));
        }
    }

    /// <summary>Every record arm survives the versioned record REQUEST codec, which is what a consensus record exchange carries outbound.</summary>
    [TestMethod]
    public void EveryRecordArmSurvivesTheRecordRequestCodec()
    {
        SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serialize = VeritasMetadataWireCodec.CreateRecordRequestSerializer();
        DeserializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> deserialize = VeritasMetadataWireCodec.CreateRecordRequestDeserializer();

        foreach(MetadataRecordArm arm in Arms())
        {
            CommittedMetadataRecord committed = CommittedFor(arm.Record);
            VersionedRecordRequest<CommittedMetadataRecord> request = RequestFor(committed);
            ArrayBufferWriter<byte> buffer = new();
            serialize(request, buffer);

            VersionedRecordRequest<CommittedMetadataRecord> decoded = deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory));

            Assert.AreEqual(request.Version, decoded.Version, FormattableString.Invariant($"The request naming the {arm.Name} arm addresses the instance it was written for, which is the guard a mis-routed request is refused by."));
            Assert.AreEqual(committed.Value, decoded.Request.Proposal.Value.Value, FormattableString.Invariant($"The {arm.Name} arm inside the proposal equals what was encoded."));
            Assert.AreEqual(request, decoded, FormattableString.Invariant($"The whole request carrying the {arm.Name} arm — step, key and proposal — equals what was encoded."));
        }
    }

    /// <summary>Every record arm survives the versioned record REPLY codec, which is what a recorder host answers a record exchange with.</summary>
    [TestMethod]
    public void EveryRecordArmSurvivesTheRecordReplyCodec()
    {
        SerializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> serialize = VeritasMetadataWireCodec.CreateRecordReplySerializer();
        DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> deserialize = VeritasMetadataWireCodec.CreateRecordReplyDeserializer();

        foreach(MetadataRecordArm arm in Arms())
        {
            CommittedMetadataRecord committed = CommittedFor(arm.Record);
            VersionedRecordReply<CommittedMetadataRecord> reply = ReplyFor(committed);
            ArrayBufferWriter<byte> buffer = new();
            serialize(reply, buffer);

            VersionedRecordReply<CommittedMetadataRecord> decoded = deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory));

            Assert.AreEqual(reply.Recorder, decoded.Recorder, FormattableString.Invariant($"The reply carrying the {arm.Name} arm names the host that produced it, which is what a probe route landing on the wrong host is caught by."));
            Assert.AreEqual(committed.Value, decoded.Reply.First.Value.Value, FormattableString.Invariant($"The {arm.Name} arm inside the recorded proposal equals what was encoded."));
            Assert.AreEqual(reply, decoded, FormattableString.Invariant($"The whole reply carrying the {arm.Name} arm — version, recorder, step and proposals — equals what was encoded."));
        }
    }

    /// <summary>Every record arm survives the NODE STATE codec, which is what a host's durable store writes and reads back.</summary>
    [TestMethod]
    public void EveryRecordArmSurvivesTheNodeStateCodec()
    {
        SerializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> serialize = VeritasMetadataWireCodec.CreateNodeStateSerializer();
        DeserializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> deserialize = VeritasMetadataWireCodec.CreateNodeStateDeserializer();

        foreach(MetadataRecordArm arm in Arms())
        {
            CommittedMetadataRecord committed = CommittedFor(arm.Record);
            QuePaxaVersionedNodeState<VeritasMetadataRecord> state = StateFor(committed);
            ArrayBufferWriter<byte> buffer = new();
            serialize(state, buffer);

            QuePaxaVersionedNodeState<VeritasMetadataRecord> decoded = deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory));

            Assert.AreEqual(state.RecorderVersion, decoded.RecorderVersion, FormattableString.Invariant($"The decoded snapshot around the {arm.Name} arm serves the version the encoded one served."));
            Assert.AreEqual(state.ActiveConfiguration, decoded.ActiveConfiguration, FormattableString.Invariant($"The decoded snapshot around the {arm.Name} arm carries the membership the encoded one carried."));
            Assert.IsNotNull(decoded.Committed, FormattableString.Invariant($"The decoded snapshot around the {arm.Name} arm holds the record the encoded one held."));
            Assert.AreEqual(committed.Value, decoded.Committed!.Value, FormattableString.Invariant($"The {arm.Name} arm inside the snapshot equals what was encoded."));
            Assert.AreNotSame(committed.Value, decoded.Committed.Value, FormattableString.Invariant($"The {arm.Name} arm came back through the codec, so the equality above is structural and not reference identity."));
        }
    }

    /// <summary>
    /// Every identifier sixty-four bits wide crosses as TEXT and never as a bare JSON number, so a consumer that
    /// reparses the payload through an IEEE double reads every one of them exactly.
    /// </summary>
    /// <remarks>
    /// The register versions the record carries are capped by the consensus library at two to the fifty-third
    /// and cross as numbers by its own rule; the three fields checked here are the ones this record owns, and
    /// each of them carries a value above that cap in the arm this row reads.
    /// </remarks>
    [TestMethod]
    public void SixtyFourBitIdentifiersCrossAsText()
    {
        SerializeMessageDelegate<CommittedMetadataRecord> serialize = VeritasMetadataWireCodec.CreateDecidedRecordSerializer();
        ArrayBufferWriter<byte> buffer = new();
        serialize(CommittedFor(FullRecord()), buffer);

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        JsonElement baseline = document.RootElement.GetProperty("value").GetProperty("baseline");

        Assert.AreEqual(JsonValueKind.String, baseline.GetProperty("causalityDigest").ValueKind, "A lineage digest spanning sixty-four bits crosses as text, so two lineages cannot collapse into one at a double-parsing reader.");
        Assert.AreEqual(JsonValueKind.String, baseline.GetProperty("confirmation").GetProperty("stateId").ValueKind, "A dataset StateId spanning sixty-four bits crosses as text for the same reason.");
        Assert.AreEqual(JsonValueKind.String, baseline.GetProperty("confirmation").GetProperty("dictionaryEpoch").ValueKind, "A term-dictionary epoch spanning sixty-four bits crosses as text for the same reason.");
    }

    /// <summary>
    /// The absent baseline, the unconfirmed baseline and the vacant coordinator lease are written as EXPLICIT
    /// nulls, so absence stays distinguishable from a field the payload never carried.
    /// </summary>
    [TestMethod]
    public void AbsentFormsAreWrittenAsExplicitNulls()
    {
        SerializeMessageDelegate<CommittedMetadataRecord> serialize = VeritasMetadataWireCodec.CreateDecidedRecordSerializer();

        ArrayBufferWriter<byte> initial = new();
        serialize(CommittedFor(VeritasMetadataRecord.Initial), initial);

        using(JsonDocument document = JsonDocument.Parse(initial.WrittenMemory))
        {
            JsonElement value = document.RootElement.GetProperty("value");
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("baseline").ValueKind, "A record carrying no lineage baseline says so with a null rather than by omitting the field.");
            Assert.AreEqual(JsonValueKind.Null, value.GetProperty("coordinator").ValueKind, "A record whose coordinator lease is vacant says so with a null rather than by omitting the field.");
        }

        ArrayBufferWriter<byte> intent = new();
        serialize(CommittedFor(BaselineIntentRecord()), intent);

        using(JsonDocument document = JsonDocument.Parse(intent.WrittenMemory))
        {
            JsonElement baseline = document.RootElement.GetProperty("value").GetProperty("baseline");
            Assert.AreEqual(JsonValueKind.Null, baseline.GetProperty("confirmation").ValueKind, "An unconfirmed intent is an explicit null, which is what keeps a baseline that may not have committed apart from one that never existed.");
        }
    }

    /// <summary>
    /// A payload that does not read as the message its exchange carries is refused as the ONE deserialization
    /// failure a channel consumer catches, for every codec alike — and a well-formed encoding of the wrong
    /// shape is refused on the same rule as a malformed one.
    /// </summary>
    /// <remarks>
    /// The uniform refusal is what lets a serve answer a malformed payload with the fault frame that names it
    /// rather than with the one that names its own seam failing, so a row that accepted an encoding-specific
    /// exception here would be pinning a weaker contract than the transport depends on.
    /// </remarks>
    [TestMethod]
    public void EveryCodecRefusesAMalformedPayload()
    {
        ReadOnlySequence<byte> truncated = PayloadOf("{\"version\":1,\"value\":{\"identityClaims\":[");
        ReadOnlySequence<byte> wellFormedWrongShape = PayloadOf("{\"version\":1,\"writer\":\"00\",\"value\":{}}");
        ReadOnlySequence<byte> notAnObject = PayloadOf("[]");

        Assert.IsTrue(IsRefused(VeritasMetadataWireCodec.CreateDecidedRecordDeserializer(), truncated), "A truncated decided-record payload is refused rather than read as the part that arrived.");
        Assert.IsTrue(IsRefused(VeritasMetadataWireCodec.CreateDecidedRecordDeserializer(), wellFormedWrongShape), "A decided-record payload whose value carries none of the record's fields is refused rather than defaulted.");
        Assert.IsTrue(IsRefused(VeritasMetadataWireCodec.CreateDecidedRecordDeserializer(), notAnObject), "A decided-record payload that is not an object at all is refused.");
        Assert.IsTrue(IsRefused(VeritasMetadataWireCodec.CreateRecordRequestDeserializer(), truncated), "A truncated record-request payload is refused.");
        Assert.IsTrue(IsRefused(VeritasMetadataWireCodec.CreateRecordReplyDeserializer(), truncated), "A truncated record-reply payload is refused.");
        Assert.IsTrue(IsRefused(VeritasMetadataWireCodec.CreateNodeStateDeserializer(), truncated), "A truncated node-state payload is refused, which is what keeps a torn store from being read as a fresh host.");
    }

    /// <summary>
    /// A decided record that the production codec wrote is BYTE-IDENTICAL to what the socket battery's own copy
    /// of the record body writes, and each side reads what the other wrote.
    /// </summary>
    /// <remarks>
    /// The socket battery keeps its copy so that its wire format cannot be moved by an edit made for another
    /// file's reasons. That is a check only while the copies agree, and this row is where they are held to it:
    /// a change to either body that the other did not take fails here, before it can reach a deployment as a
    /// break with no negotiation.
    /// </remarks>
    [TestMethod]
    public void TheProductionCodecAndTheSocketBatteryCodecAgreeByteForByte()
    {
        VeritasMetadataRecord record = FullRecord();
        CommittedMetadataRecord committed = CommittedFor(record);

        ArrayBufferWriter<byte> production = new();
        VeritasMetadataWireCodec.CreateDecidedRecordSerializer()(committed, production);

        WriteValueDelegate<Utf8JsonWriter, CommittedMetadataRecord> batteryWrite = QuePaxaMessageJson.CreateVersionedValueWriter<VeritasMetadataRecord>(MetadataChannelTransportTests.WriteMetadataRecord);
        ArrayBufferWriter<byte> battery = new();
        using(Utf8JsonWriter writer = new(battery))
        {
            batteryWrite(writer, committed);
        }

        //The decoded text is compared first so a divergence names the field it is in rather than an offset.
        Assert.AreEqual(
            Encoding.UTF8.GetString(battery.WrittenSpan),
            Encoding.UTF8.GetString(production.WrittenSpan),
            "The production codec writes the record the socket battery's own copy writes.");
        Assert.IsTrue(
            production.WrittenSpan.SequenceEqual(battery.WrittenSpan),
            "The two encodings agree byte for byte, which is the claim a cross-process wire riding one of them and a socket battery checking the other both rest on.");

        using JsonDocument fromProduction = JsonDocument.Parse(production.WrittenMemory);
        Assert.AreEqual(
            record,
            MetadataChannelTransportTests.ReadMetadataRecord(fromProduction.RootElement.GetProperty("value")),
            "The socket battery's reader reads what the production codec wrote, so the pin covers the decode direction and not the encoded bytes alone.");

        ArrayBufferWriter<byte> reencoded = new();
        using(Utf8JsonWriter writer = new(reencoded))
        {
            MetadataChannelTransportTests.WriteMetadataRecord(writer, record);
        }

        using JsonDocument fromBattery = JsonDocument.Parse(reencoded.WrittenMemory);
        Assert.AreEqual(
            record,
            VeritasMetadataWireCodec.ReadRecord(fromBattery.RootElement),
            "The production reader reads what the socket battery's copy wrote, which closes the other direction.");
    }

    /// <summary>The record arms this battery round-trips, each named so a failing assertion says which arm broke.</summary>
    /// <returns>The arms.</returns>
    private static IReadOnlyList<MetadataRecordArm> Arms()
    {
        ReplicaAxis claimant = MetadataPlaneHarness.AxisFor(0);
        ReplicaAxis holder = MetadataPlaneHarness.AxisFor(1);

        return
        [
            new MetadataRecordArm("initial", VeritasMetadataRecord.Initial),
            new MetadataRecordArm(
                "identity claims",
                VeritasMetadataRecord.Initial with
                {
                    IdentityClaims = [new ReplicaIdentityClaim(claimant, new RegisterVersion(1UL)), new ReplicaIdentityClaim(holder, new RegisterVersion(4UL))]
                }),
            new MetadataRecordArm("baseline intent", BaselineIntentRecord()),
            new MetadataRecordArm(
                "confirmed baseline",
                VeritasMetadataRecord.Initial with
                {
                    Baseline = new LineageBaseline(
                        claimant,
                        new NodeIdentifier(CausalityDigestValue),
                        new LineageConfirmation(new NodeIdentifier(StateIdValue), DictionaryEpochValue),
                        new RegisterVersion(6UL))
                }),
            new MetadataRecordArm("amended policy", VeritasMetadataRecord.Initial with { Policy = new CoordinationPolicy(HealCadenceClass: 2, SymbolBudgetTier: 3) }),
            new MetadataRecordArm("coordinator lease", VeritasMetadataRecord.Initial with { Coordinator = new CoordinatorLease(holder, new RegisterVersion(9UL)) }),
            new MetadataRecordArm("every arm at once", FullRecord())
        ];
    }

    /// <summary>The record with every arm populated, which is the representative the format pin and the width checks read.</summary>
    /// <returns>The record.</returns>
    private static VeritasMetadataRecord FullRecord()
    {
        ReplicaAxis claimant = MetadataPlaneHarness.AxisFor(0);
        ReplicaAxis holder = MetadataPlaneHarness.AxisFor(1);

        return new VeritasMetadataRecord(
            IdentityClaims: [new ReplicaIdentityClaim(claimant, new RegisterVersion(1UL)), new ReplicaIdentityClaim(holder, new RegisterVersion(4UL))],
            Baseline: new LineageBaseline(
                claimant,
                new NodeIdentifier(CausalityDigestValue),
                new LineageConfirmation(new NodeIdentifier(StateIdValue), DictionaryEpochValue),
                new RegisterVersion(6UL)),
            Policy: new CoordinationPolicy(HealCadenceClass: 2, SymbolBudgetTier: 3),
            Coordinator: new CoordinatorLease(holder, new RegisterVersion(9UL)));
    }

    /// <summary>The record whose baseline is an INTENT: recorded, and not yet confirmed by the minting host's local commit.</summary>
    /// <returns>The record.</returns>
    private static VeritasMetadataRecord BaselineIntentRecord()
    {
        return VeritasMetadataRecord.Initial with
        {
            Baseline = new LineageBaseline(
                MetadataPlaneHarness.AxisFor(0),
                new NodeIdentifier(CausalityDigestValue),
                Confirmation: null,
                new RegisterVersion(5UL))
        };
    }

    /// <summary>Wraps one record as the decided value a register produced, which is what every codec here carries.</summary>
    /// <param name="record">The record to wrap.</param>
    /// <returns>The decided record.</returns>
    private static CommittedMetadataRecord CommittedFor(VeritasMetadataRecord record)
    {
        MetadataPlaneDeployment deployment = Deployment();

        return new CommittedMetadataRecord(RegisterVersion.First, MetadataPlaneDeployment.ReplicaIdFor(deployment.Founders[0].Axis), deployment.Genesis, record);
    }

    /// <summary>Builds the record request one consensus exchange carries around a decided record.</summary>
    /// <param name="committed">The decided record the proposal carries.</param>
    /// <returns>The versioned request.</returns>
    private static VersionedRecordRequest<CommittedMetadataRecord> RequestFor(CommittedMetadataRecord committed)
    {
        return new VersionedRecordRequest<CommittedMetadataRecord>(committed.Version, new RecordRequest<CommittedMetadataRecord>(RecorderStep.RoundOnePhaseZero, ProposalFor(committed)));
    }

    /// <summary>Builds the record reply a recorder host answers one exchange with.</summary>
    /// <param name="committed">The decided record the recorded proposal carries.</param>
    /// <returns>The versioned reply.</returns>
    private static VersionedRecordReply<CommittedMetadataRecord> ReplyFor(CommittedMetadataRecord committed)
    {
        return new VersionedRecordReply<CommittedMetadataRecord>(
            committed.Version,
            Deployment().Genesis.Members[0],
            new RecordReply<CommittedMetadataRecord>(RecorderStep.RoundOnePhaseZero, ProposalFor(committed), PriorAggregate: null));
    }

    /// <summary>Builds the prioritized proposal both the request and the reply carry, keyed to the record's own writer.</summary>
    /// <param name="committed">The decided record the proposal carries.</param>
    /// <returns>The proposal.</returns>
    private static PrioritizedProposal<CommittedMetadataRecord> ProposalFor(CommittedMetadataRecord committed)
    {
        return new PrioritizedProposal<CommittedMetadataRecord>(
            new ProposalKey(new ProposalPriority(0x0123456789ABCDEFUL), ProposerLane.For(committed.Writer)),
            committed);
    }

    /// <summary>Builds the durable host snapshot one record produces, which is what the node-state codec carries.</summary>
    /// <param name="committed">The record the host has learned.</param>
    /// <returns>The snapshot.</returns>
    private static QuePaxaVersionedNodeState<VeritasMetadataRecord> StateFor(CommittedMetadataRecord committed)
    {
        MetadataPlaneDeployment deployment = Deployment();
        QuePaxaVersionedNode<VeritasMetadataRecord> node = new(deployment.Genesis, deployment.Founders[0].ToHostId());
        Assert.IsTrue(node.Learn(committed), "The host adopts a record it has not seen, which is what moves it to the next instance and gives the snapshot something to carry.");

        return node.ToState();
    }

    /// <summary>The two-founder chain every record here is decided on.</summary>
    /// <returns>The deployment.</returns>
    private static MetadataPlaneDeployment Deployment()
    {
        return MetadataPlaneDeployment.Create([MetadataPlaneHarness.FounderFor(0), MetadataPlaneHarness.FounderFor(1)]);
    }

    /// <summary>Renders one hand-written payload as the framed bytes a deserializer is handed.</summary>
    /// <param name="text">The payload text.</param>
    /// <returns>The payload.</returns>
    private static ReadOnlySequence<byte> PayloadOf(string text)
    {
        return new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Reads one payload through a deserializer and reports whether it was refused as unreadable.
    /// </summary>
    /// <typeparam name="TMessage">The message the deserializer reads.</typeparam>
    /// <param name="deserialize">The deserializer to drive.</param>
    /// <param name="payload">The payload to hand it.</param>
    /// <returns><see langword="true"/> when the payload was refused as the uniform deserialization failure.</returns>
    /// <remarks>
    /// The refusal is answered as a value rather than through an assertion callback, so the operands reach the
    /// deserializer as explicit arguments and nothing here captures an enclosing scope. Only the uniform
    /// failure counts as a refusal: an encoding-specific exception escaping this helper fails the row, which is
    /// the point of asking for that type by name.
    /// </remarks>
    private static bool IsRefused<TMessage>(DeserializeMessageDelegate<TMessage> deserialize, ReadOnlySequence<byte> payload)
    {
        try
        {
            _ = deserialize(payload);

            return false;
        }
        catch(MessageDeserializationException)
        {
            return true;
        }
    }

    /// <summary>One named record arm this battery round-trips.</summary>
    /// <param name="Name">The arm's name, which a failing assertion carries.</param>
    /// <param name="Record">The record.</param>
    private sealed record MetadataRecordArm(string Name, VeritasMetadataRecord Record);
}
