using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;
using InboundFrame = Lumoin.Veritas.Replication.MetadataChannelFraming.InboundFrame;
using OutboundFrame = Lumoin.Veritas.Replication.MetadataChannelFraming.OutboundFrame;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Serves one peer's metadata connection: it reads correlated call frames and dispatches each to the host's
/// own seams — a record exchange to the host's recorder, a committed read to the host's catch-up reader, a
/// record push to the host's durable learn, a version probe to that same catch-up reader answered under the
/// serving host's own identity — answering every call on the same connection under the correlation
/// id it arrived with. One connection carries MANY calls, and the peer tearing it down is the normal end of
/// serve. An active-active replica accepts these beside its sketch, shard, and dotted serves; the accept loop
/// that dispatches connections isolates per-connection faults.
/// </summary>
/// <remarks>
/// <para>
/// A FAILED CALL IS AN OPAQUE FAULT FRAME, NOT A TORN CONNECTION. A call the host accepted and could not
/// complete, and a call whose payload does not read as the message its exchange carries, are both answered
/// with a fault frame naming the failure's class and nothing else — the host's exception text never crosses
/// the wire — and the connection keeps serving. That is what the consensus surface needs: a recorder that
/// refuses an instance is an unreachable recorder to the caller's register, which retries within its own
/// attempt budget, and killing the connection would take every other exchange down with it.
/// </para>
/// <para>
/// A MALFORMED FRAME IS THE OTHER CASE AND ENDS THE SERVE. The frame layer refuses a truncated frame, an
/// unknown kind, and an unreadable body before any call is dispatched, and it refuses a stream that ends
/// part-way through a frame or declares a payload past the configured maximum — a length prefix is never
/// trusted beyond that bound. A stream that cannot be framed can no longer be read in step with the calls on
/// it, so that refusal propagates, the response side is completed on the way out so the peer's reader always
/// observes the end, and the accept loop isolates it.
/// </para>
/// <para>
/// CALLS ARE SERVED IN ORDER, one at a time. The calling client admits one call at a time to its connection,
/// so serving them in order costs nothing and keeps the answering frames in a defined order; concurrency lives
/// across connections, which is where a cluster's parallelism actually is.
/// </para>
/// </remarks>
public sealed class MetadataChannelServer
{
    /// <summary>The seam supplying the host's local seams for one serve.</summary>
    private ProvideMetadataServeBindingDelegate ProvideBinding { get; }

    /// <summary>The codec that reads one consensus record request.</summary>
    private DeserializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> DeserializeRecordRequest { get; }

    /// <summary>The codec that writes the host's record reply.</summary>
    private SerializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> SerializeRecordReply { get; }

    /// <summary>The codec that writes one decided record — the catch-up answer.</summary>
    private SerializeMessageDelegate<CommittedMetadataRecord> SerializeRecord { get; }

    /// <summary>The codec that reads a decided record back — the dissemination push.</summary>
    private DeserializeMessageDelegate<CommittedMetadataRecord> DeserializeRecord { get; }

    /// <summary>The pool inbound frame payloads are copied into.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>The largest frame accepted or produced, in bytes; must match the peer's.</summary>
    private int MaxFrameLength { get; }

    /// <summary>Creates a serving endpoint over the host's per-serve binding seam.</summary>
    /// <param name="provideBinding">The seam supplying the host's recorder, catch-up reader, inbound apply, and probe identity for one serve.</param>
    /// <param name="deserializeRecordRequest">The codec that reads one consensus record request.</param>
    /// <param name="serializeRecordReply">The codec that writes the host's record reply.</param>
    /// <param name="serializeRecord">The codec that writes one decided record for the catch-up answer.</param>
    /// <param name="deserializeRecord">The codec that reads a decided record back from the dissemination push.</param>
    /// <param name="pool">The pool inbound frame payloads are copied into; the engine's governed pool.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the peer's.</param>
    /// <exception cref="ArgumentNullException">A required seam or codec is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public MetadataChannelServer(
        ProvideMetadataServeBindingDelegate provideBinding,
        DeserializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> deserializeRecordRequest,
        SerializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> serializeRecordReply,
        SerializeMessageDelegate<CommittedMetadataRecord> serializeRecord,
        DeserializeMessageDelegate<CommittedMetadataRecord> deserializeRecord,
        MemoryPool<byte> pool,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(provideBinding);
        ArgumentNullException.ThrowIfNull(deserializeRecordRequest);
        ArgumentNullException.ThrowIfNull(serializeRecordReply);
        ArgumentNullException.ThrowIfNull(serializeRecord);
        ArgumentNullException.ThrowIfNull(deserializeRecord);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        ProvideBinding = provideBinding;
        DeserializeRecordRequest = deserializeRecordRequest;
        SerializeRecordReply = serializeRecordReply;
        SerializeRecord = serializeRecord;
        DeserializeRecord = deserializeRecord;
        Pool = pool;
        MaxFrameLength = maxFrameLength;
    }

    /// <summary>Serves one connection until the peer ends it. The response side is completed on every exit so the peer's reader always observes the channel ending; a serve fault still propagates after that completion, for the accept loop to isolate.</summary>
    /// <param name="requestReader">The pipe call frames are read from.</param>
    /// <param name="responseWriter">The pipe answering frames are written to.</param>
    /// <param name="cancellationToken">Cancels the serve.</param>
    /// <returns>A task that completes when the connection's calls end and the response side is completed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestReader"/> or <paramref name="responseWriter"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The binding seam answered with no binding, so the host has no seams to serve from; or the peer's frame stream ended part-way through a frame or declared a payload longer than the configured maximum, and an answer this serve produced exceeding that maximum raises here for the same reason. A refusal of the frame layer's own bounds is not a call this serve can fault-frame: it is the stream itself that can no longer be read in step.</exception>
    /// <exception cref="InvalidDataException">The peer's frame stream could not be framed and can no longer be read in step with the calls on it.</exception>
    public async Task ServeAsync(PipeReader requestReader, PipeWriter responseWriter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);

        OwnedMessageChannelReader<InboundFrame> reader = new(requestReader, MetadataChannelFraming.ReadOwnedFrame, Pool, MaxFrameLength);
        MessageChannelWriter<OutboundFrame> writer = new(responseWriter, MetadataChannelFraming.WriteFrame, MaxFrameLength);
        try
        {
            //The binding is taken once per connection: a host that starts its runner after its listener
            //answers from the runner it holds when the connection arrives, and every call of this connection
            //then reaches one host.
            MetadataServeBinding binding = ProvideBinding();
            if(binding is null)
            {
                throw new InvalidOperationException("A metadata serve binding seam answers with a binding; a serve has no seams to dispatch to without one.");
            }

            await foreach(InboundFrame call in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using(call)
                {
                    OutboundFrame answer = await ServeCallAsync(binding, call, cancellationToken).ConfigureAwait(false);
                    await writer.WriteAsync(answer, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Serves one call, converting every fault except cancellation into the opaque fault frame that answers it.</summary>
    /// <param name="binding">The host's seams this serve dispatches to.</param>
    /// <param name="call">The call frame.</param>
    /// <param name="cancellationToken">Cancels the serve.</param>
    /// <returns>The answering frame.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The serve's documented fault posture: a call the host could not complete is answered with an opaque fault frame naming the failure's class, so one failed call never takes the connection — and every other exchange on it — down, and the host's exception text never crosses the wire.")]
    private async ValueTask<OutboundFrame> ServeCallAsync(MetadataServeBinding binding, InboundFrame call, CancellationToken cancellationToken)
    {
        try
        {
            return await DispatchAsync(binding, call, cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
            throw;
        }
        catch(Exception exception)
        {
            //A payload that does not read as the message its exchange carries is the peer's frame to fix;
            //anything else is this host's own seam failing to complete a call it accepted.
            bool malformed = exception is InvalidDataException or MessageDeserializationException;

            return OutboundFrame.ForFault(call.CorrelationId, call.Kind, malformed ? MetadataChannelFraming.MalformedPayloadFault : MetadataChannelFraming.ServeFailedFault);
        }
    }

    /// <summary>Dispatches one call to the seam its exchange names and wraps the result as the answering frame.</summary>
    /// <param name="binding">The host's seams this serve dispatches to.</param>
    /// <param name="call">The call frame.</param>
    /// <param name="cancellationToken">Cancels the serve.</param>
    /// <returns>The answering frame.</returns>
    /// <exception cref="InvalidDataException">The call carried a fault body, a body its exchange does not take, or an exchange this endpoint does not serve.</exception>
    private async ValueTask<OutboundFrame> DispatchAsync(MetadataServeBinding binding, InboundFrame call, CancellationToken cancellationToken)
    {
        if(call.IsFault)
        {
            //Only an answer reports a failure to serve; a call carrying one is a peer writing answers onto
            //the call stream.
            throw new InvalidDataException("A metadata channel call never carries a fault body.");
        }

        switch(call.Kind)
        {
            case MetadataChannelFraming.RecordExchangeKind:
            {
                if(!call.HasPayload)
                {
                    throw new InvalidDataException("A metadata record exchange carries the record request as its payload.");
                }

                VersionedRecordRequest<CommittedMetadataRecord> request = DeserializeRecordRequest(call.Payload);
                VersionedRecordReply<CommittedMetadataRecord> reply = await binding.Record(request, cancellationToken).ConfigureAwait(false);
                RecordReplyPayload payload = new(SerializeRecordReply, reply);

                return OutboundFrame.ForPayload(call.CorrelationId, call.Kind, payload.Write);
            }

            case MetadataChannelFraming.CommittedReadKind:
            {
                if(call.HasPayload)
                {
                    throw new InvalidDataException("A metadata committed read carries no payload; it asks the host what it has learned.");
                }

                CommittedMetadataRecord? committed = await binding.ReadCommitted(cancellationToken).ConfigureAwait(false);
                if(committed is null)
                {
                    //An absent body is the host's answer that it has learned no record — a fact the caller
                    //must be able to tell apart from an unreachable host, which is why it is a frame at all.
                    return OutboundFrame.ForAbsent(call.CorrelationId, call.Kind);
                }

                RecordPayload record = new(SerializeRecord, committed);

                return OutboundFrame.ForPayload(call.CorrelationId, call.Kind, record.Write);
            }

            case MetadataChannelFraming.RecordPushKind:
            {
                if(!call.HasPayload)
                {
                    throw new InvalidDataException("A metadata record push carries the decided record as its payload.");
                }

                CommittedMetadataRecord pushed = DeserializeRecord(call.Payload);
                await binding.OfferRecord(pushed, cancellationToken).ConfigureAwait(false);

                //The acknowledgement is empty and is sent only after the host has learned the record durably,
                //so a peer that saw it knows the record survives this host's crash.
                return OutboundFrame.ForAbsent(call.CorrelationId, call.Kind);
            }

            case MetadataChannelFraming.VersionProbeKind:
            {
                if(call.HasPayload)
                {
                    throw new InvalidDataException("A metadata version probe carries no payload; it asks the host which version it holds.");
                }

                //The version is read through the same catch-up seam the read arm uses, so a probe answers with a
                //version the host's store holds rather than one a crash could take back.
                CommittedMetadataRecord? held = await binding.ReadCommitted(cancellationToken).ConfigureAwait(false);

                //The identity is the SERVING host's own, taken from the binding this serve dispatches through
                //and never echoed off the call, so the register's mis-wiring refusal compares against a genuine
                //claim: a probe route that landed on the wrong host is caught instead of counted.
                VersionReportPayload report = new(new MemberVersionReport(binding.Self, held is null ? RegisterVersion.Unwritten : held.Version));

                return OutboundFrame.ForPayload(call.CorrelationId, call.Kind, report.Write);
            }

            default:
            {
                throw new InvalidDataException("A metadata channel call named an exchange this endpoint does not serve.");
            }
        }
    }

    /// <summary>Binds one record reply to its codec as an explicit frame, so the payload writer captures nothing.</summary>
    /// <param name="serialize">The codec that writes the reply.</param>
    /// <param name="reply">The reply to write.</param>
    private sealed class RecordReplyPayload(SerializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> serialize, VersionedRecordReply<CommittedMetadataRecord> reply)
    {
        /// <summary>The codec that writes the reply.</summary>
        private SerializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> Serialize { get; } = serialize;

        /// <summary>The reply to write.</summary>
        private VersionedRecordReply<CommittedMetadataRecord> Reply { get; } = reply;

        /// <summary>Writes the reply's bytes straight into the frame's channel buffer.</summary>
        /// <param name="output">The channel buffer to write into.</param>
        public void Write(IBufferWriter<byte> output)
        {
            Serialize(Reply, output);
        }
    }

    /// <summary>Binds one version report to the frame layer's own body encoding as an explicit frame, so the payload writer captures nothing.</summary>
    /// <param name="report">The report to write: the version the serving host holds beside its own identity.</param>
    private sealed class VersionReportPayload(MemberVersionReport report)
    {
        /// <summary>The report to write.</summary>
        private MemberVersionReport Report { get; } = report;

        /// <summary>Writes the report's bytes straight into the frame's channel buffer.</summary>
        /// <param name="output">The channel buffer to write into.</param>
        public void Write(IBufferWriter<byte> output)
        {
            MetadataChannelFraming.WriteVersionReport(Report, output);
        }
    }

    /// <summary>Binds one decided record to its codec as an explicit frame, so the payload writer captures nothing.</summary>
    /// <param name="serialize">The codec that writes the record.</param>
    /// <param name="record">The record to write.</param>
    private sealed class RecordPayload(SerializeMessageDelegate<CommittedMetadataRecord> serialize, CommittedMetadataRecord record)
    {
        /// <summary>The codec that writes the record.</summary>
        private SerializeMessageDelegate<CommittedMetadataRecord> Serialize { get; } = serialize;

        /// <summary>The record to write.</summary>
        private CommittedMetadataRecord Record { get; } = record;

        /// <summary>Writes the record's bytes straight into the frame's channel buffer.</summary>
        /// <param name="output">The channel buffer to write into.</param>
        public void Write(IBufferWriter<byte> output)
        {
            Serialize(Record, output);
        }
    }
}
