using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Verisync.Core;
using Lumoin.Verisync.Json;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The deployment's wire encoding of the coordinated metadata record: the JSON body one record is written as,
/// and the eight message codecs a metadata endpoint is composed from — the versioned request and reply pair a
/// consensus record exchange carries, the bare decided-record pair a catch-up answer and a dissemination push
/// carry, and the node-state pair a host's durable store reads and writes.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS A WIRE FORMAT AND NOT AN IMPLEMENTATION DETAIL. Hosts that disagree on it cannot exchange a record
/// at all, and the plane has no format negotiation: a change to the body below is a break every host of a
/// deployment takes at once. That is why the body is pinned by a round-trip battery of its own rather than
/// only by the transports that happen to carry it.
/// </para>
/// <para>
/// EVERY IDENTIFIER SIXTY-FOUR BITS WIDE CROSSES AS A DECIMAL STRING rather than as a bare JSON number, so a
/// value above two to the fifty-third survives a reader that would parse a JSON number as a double. The
/// register versions the consensus envelopes carry are the library's own business and are written by its
/// codecs; the identifiers here are the ones this record owns.
/// </para>
/// <para>
/// THE BASELINE AND ITS CONFIRMATION ARE TRI-STATE AND ARE WRITTEN AS EXPLICIT NULLS. An unconfirmed intent
/// and an absent baseline are different facts — one says a minting host recorded a lineage and may not have
/// committed it, the other says no lineage is recorded at all — so absence is a null the payload carries and
/// never a field it omits.
/// </para>
/// <para>
/// NOTHING HERE CAPTURES AN ENCLOSING SCOPE. The record body is a pair of static methods, and the bare
/// decided-record pair is bound through <see cref="DecidedMetadataRecordCodec"/>, an explicit frame that holds
/// the two value seams as properties and exposes its faces as method groups over that instance.
/// </para>
/// </remarks>
[SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "The serialization quarantine keeps the document stack's System.Text.Json dependency inside the binding projects that own it. This type is not part of that stack: it encodes ONE consensus control-plane record, and the consensus library's envelope factories take their value seams as Utf8JsonWriter and JsonElement delegates, so those two types are the seam's shape rather than a choice made here. The reader and the writer are the in-box UTF-8 ones — no serializer, no converter, no reflection — and this is the single file of the replication library that names them.")]
public static class VeritasMetadataWireCodec
{
    /// <summary>Creates the codec that writes one consensus record request onto the wire.</summary>
    /// <returns>The serializer.</returns>
    public static SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> CreateRecordRequestSerializer()
    {
        return QuePaxaMessageJson.CreateVersionedRequestSerializer(CreateDecidedValueWriter());
    }

    /// <summary>Creates the codec that reads one consensus record request back.</summary>
    /// <returns>The deserializer.</returns>
    public static DeserializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> CreateRecordRequestDeserializer()
    {
        return QuePaxaMessageJson.CreateVersionedRequestDeserializer(CreateDecidedValueReader());
    }

    /// <summary>Creates the codec that writes one host's record reply onto the wire.</summary>
    /// <returns>The serializer.</returns>
    public static SerializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> CreateRecordReplySerializer()
    {
        return QuePaxaMessageJson.CreateVersionedReplySerializer(CreateDecidedValueWriter());
    }

    /// <summary>Creates the codec that reads one member's record reply back.</summary>
    /// <returns>The deserializer.</returns>
    public static DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> CreateRecordReplyDeserializer()
    {
        return QuePaxaMessageJson.CreateVersionedReplyDeserializer(CreateDecidedValueReader());
    }

    /// <summary>Creates the codec that writes one decided record — the body of a catch-up answer and of a dissemination push.</summary>
    /// <returns>The serializer.</returns>
    public static SerializeMessageDelegate<CommittedMetadataRecord> CreateDecidedRecordSerializer()
    {
        return new DecidedMetadataRecordCodec(WriteRecord, ReadRecord).Serialize;
    }

    /// <summary>Creates the codec that reads one decided record back.</summary>
    /// <returns>The deserializer.</returns>
    public static DeserializeMessageDelegate<CommittedMetadataRecord> CreateDecidedRecordDeserializer()
    {
        return new DecidedMetadataRecordCodec(WriteRecord, ReadRecord).Deserialize;
    }

    /// <summary>Creates the codec a host's durable store writes its consensus node state with.</summary>
    /// <returns>The serializer.</returns>
    public static SerializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> CreateNodeStateSerializer()
    {
        return QuePaxaMessageJson.CreateVersionedNodeStateSerializer<VeritasMetadataRecord>(WriteRecord);
    }

    /// <summary>Creates the codec a host's durable store reads its consensus node state back with.</summary>
    /// <returns>The deserializer.</returns>
    /// <remarks>
    /// A state this reads is decoded and not cross-checked: whether the restored leader, version and membership
    /// agree with the record beside them is answered once by the consensus host's own restore factory, whose
    /// torn-snapshot refusal is the safety net.
    /// </remarks>
    public static DeserializeMessageDelegate<QuePaxaVersionedNodeState<VeritasMetadataRecord>> CreateNodeStateDeserializer()
    {
        return QuePaxaMessageJson.CreateVersionedNodeStateDeserializer<VeritasMetadataRecord>(ReadRecord);
    }

    /// <summary>
    /// Writes one coordinated metadata record as the application value inside a consensus payload. Every
    /// identifier that is 64 bits wide is written as a decimal STRING rather than as a bare number, so a value
    /// above two to the fifty-third survives a reader that would parse a JSON number as a double.
    /// </summary>
    /// <param name="writer">The writer the value is written into.</param>
    /// <param name="record">The record to write.</param>
    /// <exception cref="ArgumentNullException">Thrown if the writer or the record is <see langword="null"/>.</exception>
    public static void WriteRecord(Utf8JsonWriter writer, VeritasMetadataRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);

        writer.WriteStartObject();
        writer.WriteStartArray("identityClaims");
        foreach(ReplicaIdentityClaim claim in record.IdentityClaims)
        {
            writer.WriteStartObject();
            writer.WriteString("axis", Convert.ToHexStringLower(claim.Axis.Bytes.Span));
            writer.WriteNumber("claimedAt", claim.ClaimedAt.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        if(record.Baseline is { } baseline)
        {
            writer.WritePropertyName("baseline");
            writer.WriteStartObject();
            writer.WriteString("claimantAxis", Convert.ToHexStringLower(baseline.ClaimantAxis.Bytes.Span));
            writer.WriteString("causalityDigest", baseline.CausalityDigest.Value.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("recordedAt", baseline.RecordedAt.Value);
            if(baseline.Confirmation is { } confirmation)
            {
                writer.WritePropertyName("confirmation");
                writer.WriteStartObject();
                writer.WriteString("stateId", confirmation.StateId.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteString("dictionaryEpoch", confirmation.DictionaryEpoch.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }
            else
            {
                //An unconfirmed intent is written as an explicit null, so absence stays distinguishable from a
                //field the payload never carried — the tri-state the baseline keeps.
                writer.WriteNull("confirmation");
            }

            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("baseline");
        }

        writer.WritePropertyName("policy");
        writer.WriteStartObject();
        writer.WriteNumber("healCadenceClass", record.Policy.HealCadenceClass);
        writer.WriteNumber("symbolBudgetTier", record.Policy.SymbolBudgetTier);
        writer.WriteEndObject();

        if(record.Coordinator is { } lease)
        {
            writer.WritePropertyName("coordinator");
            writer.WriteStartObject();
            writer.WriteString("holder", Convert.ToHexStringLower(lease.Holder.Bytes.Span));
            writer.WriteNumber("term", lease.Term.Value);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("coordinator");
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads one coordinated metadata record back. Nothing here refuses a payload on a rule of its own: a
    /// missing field, a malformed identifier or a value a domain constructor rejects each surfaces from this
    /// body and reaches the caller as the codec's own fail-closed refusal.
    /// </summary>
    /// <param name="element">The element the value was written into.</param>
    /// <returns>The record the payload carries.</returns>
    public static VeritasMetadataRecord ReadRecord(JsonElement element)
    {
        JsonElement claimsElement = element.GetProperty("identityClaims");
        ImmutableArray<ReplicaIdentityClaim>.Builder claims = ImmutableArray.CreateBuilder<ReplicaIdentityClaim>(claimsElement.GetArrayLength());
        foreach(JsonElement claim in claimsElement.EnumerateArray())
        {
            claims.Add(new ReplicaIdentityClaim(
                new ReplicaAxis(Convert.FromHexString(claim.GetProperty("axis").GetString()!)),
                new RegisterVersion(claim.GetProperty("claimedAt").GetUInt64())));
        }

        JsonElement baselineElement = element.GetProperty("baseline");
        JsonElement policyElement = element.GetProperty("policy");
        JsonElement coordinatorElement = element.GetProperty("coordinator");

        return new VeritasMetadataRecord(
            IdentityClaims: claims.MoveToImmutable(),
            Baseline: baselineElement.ValueKind == JsonValueKind.Null ? null : ReadBaseline(baselineElement),
            Policy: new CoordinationPolicy(policyElement.GetProperty("healCadenceClass").GetInt32(), policyElement.GetProperty("symbolBudgetTier").GetInt32()),
            Coordinator: coordinatorElement.ValueKind == JsonValueKind.Null ? null : ReadLease(coordinatorElement));
    }

    /// <summary>Builds the value seam that writes one decided record, which the request, reply and push codecs are all composed over.</summary>
    /// <returns>The value writer.</returns>
    private static WriteValueDelegate<Utf8JsonWriter, CommittedMetadataRecord> CreateDecidedValueWriter()
    {
        return QuePaxaMessageJson.CreateVersionedValueWriter<VeritasMetadataRecord>(WriteRecord);
    }

    /// <summary>Builds the value seam that reads one decided record back, the counterpart of <see cref="CreateDecidedValueWriter"/>.</summary>
    /// <returns>The value reader.</returns>
    private static ReadValueDelegate<JsonElement, CommittedMetadataRecord> CreateDecidedValueReader()
    {
        return QuePaxaMessageJson.CreateVersionedValueReader<VeritasMetadataRecord>(ReadRecord);
    }

    /// <summary>Reads the lineage baseline, whose confirmation is present as a whole or absent as a whole.</summary>
    /// <param name="element">The baseline element.</param>
    /// <returns>The baseline.</returns>
    private static LineageBaseline ReadBaseline(JsonElement element)
    {
        JsonElement confirmationElement = element.GetProperty("confirmation");
        LineageConfirmation? confirmation = confirmationElement.ValueKind == JsonValueKind.Null
            ? null
            : new LineageConfirmation(
                new NodeIdentifier(ulong.Parse(confirmationElement.GetProperty("stateId").GetString()!, CultureInfo.InvariantCulture)),
                long.Parse(confirmationElement.GetProperty("dictionaryEpoch").GetString()!, CultureInfo.InvariantCulture));

        return new LineageBaseline(
            ClaimantAxis: new ReplicaAxis(Convert.FromHexString(element.GetProperty("claimantAxis").GetString()!)),
            CausalityDigest: new NodeIdentifier(ulong.Parse(element.GetProperty("causalityDigest").GetString()!, CultureInfo.InvariantCulture)),
            Confirmation: confirmation,
            RecordedAt: new RegisterVersion(element.GetProperty("recordedAt").GetUInt64()));
    }

    /// <summary>Reads the coordinator lease.</summary>
    /// <param name="element">The lease element.</param>
    /// <returns>The lease.</returns>
    private static CoordinatorLease ReadLease(JsonElement element)
    {
        return new CoordinatorLease(
            new ReplicaAxis(Convert.FromHexString(element.GetProperty("holder").GetString()!)),
            new RegisterVersion(element.GetProperty("term").GetUInt64()));
    }

    /// <summary>
    /// Binds the record body to the message codecs the bare decided-record exchanges expect, as an explicit
    /// frame so neither face captures an enclosing scope.
    /// </summary>
    /// <param name="writeRecord">The body seam that writes one record.</param>
    /// <param name="readRecord">The body seam that reads one record back.</param>
    /// <remarks>
    /// The consensus library owns the envelope around the application value, so the frame wraps the two body
    /// seams in the library's own versioned-value pair once, at construction, and its two faces then encode and
    /// decode a whole decided record without composing anything further per call.
    /// </remarks>
    private sealed class DecidedMetadataRecordCodec(WriteMetadataRecordDelegate writeRecord, ReadMetadataRecordDelegate readRecord)
    {
        /// <summary>The value seam that writes one decided record, envelope and all.</summary>
        private WriteValueDelegate<Utf8JsonWriter, CommittedMetadataRecord> Write { get; } = QuePaxaMessageJson.CreateVersionedValueWriter<VeritasMetadataRecord>(writeRecord.Invoke);

        /// <summary>The value seam that reads one decided record back, envelope and all.</summary>
        private ReadValueDelegate<JsonElement, CommittedMetadataRecord> Read { get; } = QuePaxaMessageJson.CreateVersionedValueReader<VeritasMetadataRecord>(readRecord.Invoke);

        /// <summary>Writes one decided record into a frame's channel buffer — a <see cref="SerializeMessageDelegate{TMessage}"/>.</summary>
        /// <param name="record">The record to write.</param>
        /// <param name="output">The buffer to write into.</param>
        public void Serialize(CommittedMetadataRecord record, IBufferWriter<byte> output)
        {
            //The writer's disposal is what flushes the encoded bytes into the frame's buffer, so it ends inside
            //this call and not at some later point the caller would have to know about.
            using Utf8JsonWriter writer = new(output);
            Write(writer, record);
        }

        /// <summary>Reads one decided record back from a frame's payload — a <see cref="DeserializeMessageDelegate{TMessage}"/>.</summary>
        /// <param name="payload">The payload to read.</param>
        /// <returns>The decided record.</returns>
        /// <exception cref="MessageDeserializationException">Thrown when the payload does not read as one decided record.</exception>
        /// <remarks>
        /// The encoding-specific failure is folded into the one deserialization failure a channel consumer
        /// catches, and carried on as the inner exception, because that is the contract
        /// <see cref="DeserializeMessageDelegate{TMessage}"/> states and what lets a serve answer a malformed
        /// payload with the fault frame that names it rather than with the one that names its own seam failing.
        /// </remarks>
        public CommittedMetadataRecord Deserialize(ReadOnlySequence<byte> payload)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);

                return Read(document.RootElement);
            }
            catch(JsonException exception)
            {
                throw Refusal(exception);
            }
            catch(KeyNotFoundException exception)
            {
                throw Refusal(exception);
            }
            catch(InvalidOperationException exception)
            {
                throw Refusal(exception);
            }
            catch(FormatException exception)
            {
                throw Refusal(exception);
            }
            catch(OverflowException exception)
            {
                throw Refusal(exception);
            }
            catch(NotSupportedException exception)
            {
                throw Refusal(exception);
            }
            catch(ArgumentException exception)
            {
                throw Refusal(exception);
            }
        }

        /// <summary>Wraps an encoding-specific failure as the uniform deserialization refusal.</summary>
        /// <param name="cause">The encoding-specific failure.</param>
        /// <returns>The refusal to raise.</returns>
        private static MessageDeserializationException Refusal(Exception cause)
        {
            return new MessageDeserializationException("The payload does not read as one decided coordinated metadata record.", cause);
        }
    }
}
