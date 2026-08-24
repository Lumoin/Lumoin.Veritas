using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Verisync.Core;
using CommittedMetadataRecord = Lumoin.Verisync.Core.VersionedValue<Lumoin.Veritas.Replication.VeritasMetadataRecord>;
using InboundFrame = Lumoin.Veritas.Replication.MetadataChannelFraming.InboundFrame;
using OutboundFrame = Lumoin.Veritas.Replication.MetadataChannelFraming.OutboundFrame;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Reaches ONE cluster member's metadata endpoint over a duplex connection, carrying the four exchanges the
/// consensus plane needs: <see cref="RecordAsync"/> is the member's recorder endpoint, <see cref="ReadCommittedAsync"/>
/// is its catch-up read, <see cref="PushRecordAsync"/> offers it a decided record, and
/// <see cref="ObserveVersionAsync"/> asks it which version it holds. Each of the four IS the delegate the
/// consensus surface expects, so a plane wires this client in per member without an
/// adapter: <see cref="RecordAsync"/> as the recorder endpoint a member resolves to,
/// <see cref="ReadCommittedAsync"/> as that member's committed-record reader, <see cref="PushRecordAsync"/>
/// as the per-member leg the plane's dissemination fans out over, and <see cref="ObserveVersionAsync"/> as the
/// per-member leg a readiness report is assembled from.
/// </summary>
/// <remarks>
/// <para>
/// ONE CONNECTION, CALLS SERIALIZED. The connection is opened on the first call and reused, and a gate admits
/// one call at a time, because two calls interleaving their frames on one pipe would be answered in an order
/// neither can predict. The correlation id is the second half of that discipline rather than a replacement for
/// it: the answering side echoes it, so a mis-paired stream is caught by value instead of being folded into
/// the wrong call. Concurrency across MEMBERS — which is where a consensus attempt's parallelism actually
/// lives — is unaffected, since each member has its own client and its own connection.
/// </para>
/// <para>
/// A FAULTED OR CANCELLED CALL TEARS THE CONNECTION DOWN. Either leaves the frame stream out of step with the
/// calls on it — a half-written request, or an answer nobody will read — so the connection is disposed and the
/// next call dials afresh. A peer FAULT FRAME is not that case: it is a well-formed answer that leaves the
/// stream in step, so it is raised to the caller while the connection stays open.
/// </para>
/// <para>
/// FAULTS ARE THE PROTOCOL'S OWN LANGUAGE HERE, not a broken contract. A recorder that refuses an instance
/// says so by faulting, and a register reads the fault as an unreachable recorder it retries within its
/// attempt budget; a catch-up read skips a faulting host; a dissemination that fails is an operability event
/// the decided write does not depend on. So this client raises a peer fault, an ended connection, and a
/// protocol violation as exceptions rather than inventing values the consensus surface has no room for.
/// </para>
/// <para>
/// A length-prefixed frame is buffered whole before it is read, so a duplex whose pause-writer threshold is
/// below <c>maxFrameLength</c> can deadlock on a frame larger than the threshold. Metadata frames are
/// control-plane sized, but a host that raises the frame length raises the transport's threshold with it. The
/// bound itself is enforced in both directions and is never trusted from the wire: a call whose own frame
/// exceeds it, and a peer frame that declares a payload past it or ends part-way through one, each end the call
/// with the frame layer's own refusal rather than with an allocation the length prefix asked for.
/// </para>
/// </remarks>
public sealed class MetadataChannelClient: IAsyncDisposable
{
    /// <summary>The seam that opens the connection this client reuses.</summary>
    private OpenPeerMetadataConnectionDelegate OpenConnection { get; }

    /// <summary>The codec that writes one consensus record request.</summary>
    private SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> SerializeRecordRequest { get; }

    /// <summary>The codec that reads the member's record reply back.</summary>
    private DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> DeserializeRecordReply { get; }

    /// <summary>The codec that writes one decided record — the dissemination push.</summary>
    private SerializeMessageDelegate<CommittedMetadataRecord> SerializeRecord { get; }

    /// <summary>The codec that reads a decided record back — the catch-up answer.</summary>
    private DeserializeMessageDelegate<CommittedMetadataRecord> DeserializeRecord { get; }

    /// <summary>The pool inbound frame payloads are copied into.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>The largest frame accepted or produced, in bytes; must match the member's.</summary>
    private int MaxFrameLength { get; }

    /// <summary>The gate that admits one call at a time to the single connection.</summary>
    private SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>The open connection and its frame streams, or <see langword="null"/> when none is open; touched only under <see cref="Gate"/>.</summary>
    private CallSession? Session { get; set; }

    //A naked field: correlation ids advance with Interlocked, which needs a by-ref target.
    private long correlation;

    //A naked field: disposal is flagged with an atomic exchange, which needs a by-ref target.
    private int disposed;

    /// <summary>Creates a client over the seam that reaches one member's metadata endpoint.</summary>
    /// <param name="openConnection">The seam that opens the connection this client reuses.</param>
    /// <param name="serializeRecordRequest">The codec that writes one consensus record request.</param>
    /// <param name="deserializeRecordReply">The codec that reads the member's record reply back.</param>
    /// <param name="serializeRecord">The codec that writes one decided record for the dissemination push.</param>
    /// <param name="deserializeRecord">The codec that reads a decided record back from the catch-up answer.</param>
    /// <param name="pool">The pool inbound frame payloads are copied into; the engine's governed pool.</param>
    /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes; must match the member's.</param>
    /// <exception cref="ArgumentNullException">A required seam or codec is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrameLength"/> is less than one.</exception>
    public MetadataChannelClient(
        OpenPeerMetadataConnectionDelegate openConnection,
        SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serializeRecordRequest,
        DeserializeMessageDelegate<VersionedRecordReply<CommittedMetadataRecord>> deserializeRecordReply,
        SerializeMessageDelegate<CommittedMetadataRecord> serializeRecord,
        DeserializeMessageDelegate<CommittedMetadataRecord> deserializeRecord,
        MemoryPool<byte> pool,
        int maxFrameLength = MessageChannel.DefaultMaxFrameLength)
    {
        ArgumentNullException.ThrowIfNull(openConnection);
        ArgumentNullException.ThrowIfNull(serializeRecordRequest);
        ArgumentNullException.ThrowIfNull(deserializeRecordReply);
        ArgumentNullException.ThrowIfNull(serializeRecord);
        ArgumentNullException.ThrowIfNull(deserializeRecord);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrameLength, 1);

        OpenConnection = openConnection;
        SerializeRecordRequest = serializeRecordRequest;
        DeserializeRecordReply = deserializeRecordReply;
        SerializeRecord = serializeRecord;
        DeserializeRecord = deserializeRecord;
        Pool = pool;
        MaxFrameLength = maxFrameLength;
    }

    /// <summary>Sends one consensus record request to the member and returns its reply — a <see cref="VersionedRecorderEndpointDelegate{TValue}"/> over the decided record, so it binds where a register resolves a member's recorder endpoint.</summary>
    /// <param name="request">The versioned record request to send.</param>
    /// <param name="cancellationToken">Cancels the call; a cancelled call tears the connection down.</param>
    /// <returns>The member's reply.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="IOException">The member reported that it could not serve the call, or the connection ended before it answered — both of which a register reads as an unreachable recorder.</exception>
    /// <exception cref="InvalidDataException">The member's answer violated the channel protocol.</exception>
    /// <exception cref="InvalidOperationException">The frame stream could not be kept in step: this call's own frame is longer than the configured maximum, or the member's answering frame ended part-way through or declared a payload longer than that maximum. The connection is torn down and the next call dials afresh.</exception>
    public async ValueTask<VersionedRecordReply<CommittedMetadataRecord>> RecordAsync(VersionedRecordRequest<CommittedMetadataRecord> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        RecordRequestPayload payload = new(SerializeRecordRequest, request);
        using InboundFrame answer = await CallAsync(MetadataChannelFraming.RecordExchangeKind, payload.Write, cancellationToken).ConfigureAwait(false);
        ThrowIfFault(answer);
        if(!answer.HasPayload)
        {
            throw new InvalidDataException("A metadata record exchange is answered with a reply payload; the member answered an empty body.");
        }

        return DeserializeRecordReply(answer.Payload);
    }

    /// <summary>Asks the member for the committed record it has learned — a <see cref="ReadCommittedRecordDelegate{TValue}"/>, so it binds where a register resolves a member's catch-up reader.</summary>
    /// <param name="cancellationToken">Cancels the call; a cancelled call tears the connection down.</param>
    /// <returns>The member's committed record, or <see langword="null"/> when it has learned none.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="IOException">The member reported that it could not serve the call, or the connection ended before it answered — both of which a catch-up read skips.</exception>
    /// <exception cref="InvalidDataException">The member's answer violated the channel protocol.</exception>
    /// <exception cref="InvalidOperationException">The member's answering frame ended part-way through, or declared a payload longer than the configured maximum, so the frame stream can no longer be read in step. The connection is torn down and the next call dials afresh.</exception>
    public async ValueTask<CommittedMetadataRecord?> ReadCommittedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        using InboundFrame answer = await CallAsync(MetadataChannelFraming.CommittedReadKind, writePayload: null, cancellationToken).ConfigureAwait(false);
        ThrowIfFault(answer);

        //An absent body is the member's answer that it has learned no record — a fact, not a failure, and the
        //one the read's caller must not confuse with an unreachable member.
        return answer.HasPayload ? DeserializeRecord(answer.Payload) : null;
    }

    /// <summary>Offers one decided record to the member and completes when the member has learned it durably — an <see cref="OfferMetadataRecordDelegate"/>, so it binds as one leg of the plane's dissemination fan-out.</summary>
    /// <param name="committed">The decided record to offer.</param>
    /// <param name="cancellationToken">Cancels the call; a cancelled call tears the connection down.</param>
    /// <returns>A task that completes when the member has learned the record durably.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="committed"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="IOException">The member reported that it could not serve the call, or the connection ended before it answered — a slower cluster, never a failed decision.</exception>
    /// <exception cref="InvalidDataException">The member's answer violated the channel protocol.</exception>
    /// <exception cref="InvalidOperationException">The frame stream could not be kept in step: this push's own frame is longer than the configured maximum, or the member's acknowledging frame ended part-way through or declared a payload longer than that maximum. The connection is torn down and the next call dials afresh.</exception>
    public async ValueTask PushRecordAsync(CommittedMetadataRecord committed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(committed);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        RecordPayload payload = new(SerializeRecord, committed);
        using InboundFrame answer = await CallAsync(MetadataChannelFraming.RecordPushKind, payload.Write, cancellationToken).ConfigureAwait(false);
        ThrowIfFault(answer);
        if(answer.HasPayload)
        {
            throw new InvalidDataException("A metadata record push is acknowledged with an empty body; the member answered a payload.");
        }
    }

    /// <summary>Asks the member which committed version it holds and returns the member's OWN answer — an <see cref="ObserveMetadataVersionDelegate"/>, so it binds as the per-member leg a readiness report is assembled from.</summary>
    /// <param name="cancellationToken">Cancels the call; a cancelled call tears the connection down.</param>
    /// <returns>The member's report: the version it holds, or <see cref="RegisterVersion.Unwritten"/> when it has learned none, beside the identity the answering host asserts for itself.</returns>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="IOException">The member reported that it could not serve the call, or the connection ended before it answered — which a readiness report records as an unreachable member rather than as a member holding nothing.</exception>
    /// <exception cref="InvalidDataException">The member's answer violated the channel protocol.</exception>
    /// <exception cref="InvalidOperationException">The member's answering frame ended part-way through, or declared a payload longer than the configured maximum, so the frame stream can no longer be read in step. A readiness report records it as an unreachable member for the same reason it records a fault as one. The connection is torn down and the next call dials afresh.</exception>
    /// <remarks>
    /// The identity in the answer is read out of the member's reply and is never the member this client was
    /// composed for: the register refuses a report naming another member, and that refusal is what catches an
    /// endpoint map whose two routes land on one host — which a client labelling the answer itself would hide.
    /// </remarks>
    public async ValueTask<MemberVersionReport> ObserveVersionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        using InboundFrame answer = await CallAsync(MetadataChannelFraming.VersionProbeKind, writePayload: null, cancellationToken).ConfigureAwait(false);
        ThrowIfFault(answer);
        if(!answer.HasPayload)
        {
            throw new InvalidDataException("A metadata version probe is answered with the reporting host's identity and its version; the member answered an empty body.");
        }

        return MetadataChannelFraming.ReadVersionReport(answer.Payload);
    }

    /// <summary>Tears the connection down and releases the gate. Do not dispose while a call is in flight — that is the ordinary use-after-dispose misuse, not a race this guards against. Disposal is idempotent.</summary>
    /// <returns>A task that completes when the connection is torn down.</returns>
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await TearDownAsync().ConfigureAwait(false);
        Gate.Dispose();
    }

    /// <summary>Runs one correlated call: writes the request frame, reads exactly one answering frame, and verifies it pairs with the call. Ownership of the answer transfers to the caller.</summary>
    /// <param name="kind">The exchange the call belongs to.</param>
    /// <param name="writePayload">The seam that writes the call's payload, or <see langword="null"/> for a call with no argument.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The answering frame, which the caller disposes.</returns>
    /// <exception cref="IOException">The connection ended before the member answered.</exception>
    /// <exception cref="InvalidDataException">The answer named another call or another exchange.</exception>
    /// <exception cref="InvalidOperationException">The call's own frame is longer than the configured maximum, or the answering frame ended part-way through or declared a payload longer than it — the frame layer's own bounds on a length prefix it never trusts beyond them.</exception>
    private async ValueTask<InboundFrame> CallAsync(byte kind, MetadataChannelFraming.WriteFramePayloadDelegate? writePayload, CancellationToken cancellationToken)
    {
        ulong correlationId = unchecked((ulong)Interlocked.Increment(ref correlation));
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CallSession call = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

            //The registration ends the session's own reads when the caller cancels, which is what unblocks a
            //read parked on a member that will never answer. Disposing it waits out a callback already
            //running, so the teardown below can never race one.
            CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(CancelSession, call);
            try
            {
                OutboundFrame request = writePayload is null
                    ? OutboundFrame.ForAbsent(correlationId, kind)
                    : OutboundFrame.ForPayload(correlationId, kind, writePayload);
                await call.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

                if(!await call.Frames.MoveNextAsync().ConfigureAwait(false))
                {
                    throw new IOException("A metadata channel member ended the connection before answering the call in flight.");
                }

                InboundFrame answer = call.Frames.Current;
                try
                {
                    Verify(answer, correlationId, kind);
                }
                catch
                {
                    answer.Dispose();

                    throw;
                }

                return answer;
            }
            finally
            {
                await registration.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
        {
            await TearDownAsync().ConfigureAwait(false);

            //The read is cancelled through the session's own lifetime source, so the cancellation is re-raised
            //against the caller's token: a caller that filters on its own token sees its own cancellation.
            throw new OperationCanceledException(cancellationToken);
        }
        catch
        {
            //A faulted call leaves the frame stream out of step with the calls on it, so the connection is
            //torn down and the next call dials afresh. A member's fault FRAME is a well-formed answer and
            //never reaches here.
            await TearDownAsync().ConfigureAwait(false);

            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Returns the open session, opening the connection and its frame streams when none is open. Called only under the gate.</summary>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>The session every call of this connection runs over.</returns>
    private async ValueTask<CallSession> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if(Session is { } live)
        {
            return live;
        }

        PeerChannelConnection connection = await OpenConnection(cancellationToken).ConfigureAwait(false);
        CallSession opened = CallSession.Create(connection, Pool, MaxFrameLength);
        Session = opened;

        return opened;
    }

    /// <summary>Tears the open session down, if any, so the next call dials afresh. Called only under the gate, or from disposal.</summary>
    /// <returns>A task that completes when the session is torn down.</returns>
    private async ValueTask TearDownAsync()
    {
        if(Session is not { } live)
        {
            return;
        }

        Session = null;
        await live.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Ends the session's reads on the caller's cancellation; bound as a method group so the registration carries no closure.</summary>
    /// <param name="state">The session to cancel.</param>
    private static void CancelSession(object? state)
    {
        ((CallSession)state!).Cancel();
    }

    /// <summary>Refuses an answer that pairs with another call or another exchange — the mis-pairing the correlation id exists to catch.</summary>
    /// <param name="answer">The answering frame.</param>
    /// <param name="correlationId">The correlation id the call was sent under.</param>
    /// <param name="kind">The exchange the call belongs to.</param>
    /// <exception cref="InvalidDataException">The answer named another call or another exchange.</exception>
    private static void Verify(InboundFrame answer, ulong correlationId, byte kind)
    {
        if(answer.CorrelationId != correlationId)
        {
            throw new InvalidDataException("A metadata channel answer carried another call's correlation id; the frame stream is mis-paired.");
        }

        if(answer.Kind != kind)
        {
            throw new InvalidDataException("A metadata channel answer named another exchange than the call it answers.");
        }
    }

    /// <summary>Raises a member's fault frame as the I/O fault the consensus seams read as an unreachable member; the fault's class is named, its text is the member's and never crosses the wire.</summary>
    /// <param name="answer">The answering frame.</param>
    /// <exception cref="IOException">The frame reports that the member could not serve the call.</exception>
    private static void ThrowIfFault(InboundFrame answer)
    {
        if(!answer.IsFault)
        {
            return;
        }

        throw new IOException(FaultMessage(answer.FaultCode));
    }

    /// <summary>The fixed message naming a fault code's class.</summary>
    /// <param name="faultCode">The fault code the member answered with.</param>
    /// <returns>The message.</returns>
    private static string FaultMessage(byte faultCode)
    {
        return faultCode switch
        {
            MetadataChannelFraming.ServeFailedFault => "A metadata channel member accepted the call and could not complete it.",
            MetadataChannelFraming.MalformedPayloadFault => "A metadata channel member could not read the call's payload as the message the exchange carries.",
            _ => "A metadata channel member reported an unclassified failure to serve the call.",
        };
    }

    /// <summary>Binds one record request to its codec as an explicit frame, so the payload writer captures nothing.</summary>
    /// <param name="serialize">The codec that writes the request.</param>
    /// <param name="request">The request to write.</param>
    private sealed class RecordRequestPayload(SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> serialize, VersionedRecordRequest<CommittedMetadataRecord> request)
    {
        /// <summary>The codec that writes the request.</summary>
        private SerializeMessageDelegate<VersionedRecordRequest<CommittedMetadataRecord>> Serialize { get; } = serialize;

        /// <summary>The request to write.</summary>
        private VersionedRecordRequest<CommittedMetadataRecord> Request { get; } = request;

        /// <summary>Writes the request's bytes straight into the frame's channel buffer.</summary>
        /// <param name="output">The channel buffer to write into.</param>
        public void Write(IBufferWriter<byte> output)
        {
            Serialize(Request, output);
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

    /// <summary>
    /// One open connection and the frame streams over it: the writer calls leave on, the enumerator answers
    /// arrive through, and the lifetime source a cancelled call ends the reads with. The enumerator lives as
    /// long as the connection because the call gate admits one call at a time, so one answer is read per call
    /// and the stream stays in step.
    /// </summary>
    private sealed class CallSession: IAsyncDisposable
    {
        /// <summary>Creates a session over an opened connection and its frame streams.</summary>
        /// <param name="connection">The opened connection; this session disposes it.</param>
        /// <param name="lifetime">The source a cancelled call ends the session's reads with.</param>
        /// <param name="writer">The frame writer calls leave on.</param>
        /// <param name="frames">The frame enumerator answers arrive through.</param>
        private CallSession(PeerChannelConnection connection, CancellationTokenSource lifetime, MessageChannelWriter<OutboundFrame> writer, IAsyncEnumerator<InboundFrame> frames)
        {
            Connection = connection;
            Lifetime = lifetime;
            Writer = writer;
            Frames = frames;
        }

        /// <summary>The frame writer calls leave on.</summary>
        public MessageChannelWriter<OutboundFrame> Writer { get; }

        /// <summary>The frame enumerator answers arrive through; one answer is read per call.</summary>
        public IAsyncEnumerator<InboundFrame> Frames { get; }

        /// <summary>The opened connection this session owns.</summary>
        private PeerChannelConnection Connection { get; }

        /// <summary>The source a cancelled call ends the session's reads with.</summary>
        private CancellationTokenSource Lifetime { get; }

        /// <summary>Creates a session over an opened connection, building its frame streams.</summary>
        /// <param name="connection">The opened connection; the session disposes it.</param>
        /// <param name="pool">The pool inbound frame payloads are copied into.</param>
        /// <param name="maxFrameLength">The largest frame accepted or produced, in bytes.</param>
        /// <returns>The session.</returns>
        public static CallSession Create(PeerChannelConnection connection, MemoryPool<byte> pool, int maxFrameLength)
        {
            CancellationTokenSource lifetime = new();
            MessageChannelWriter<OutboundFrame> writer = new(connection.RequestWriter, MetadataChannelFraming.WriteFrame, maxFrameLength);
            OwnedMessageChannelReader<InboundFrame> reader = new(connection.ResponseReader, MetadataChannelFraming.ReadOwnedFrame, pool, maxFrameLength);

            //The stream runs under the session's own lifetime rather than any one call's token, because it
            //outlives every call it carries; a call cancels it through that source and the session is then
            //torn down.
            IAsyncEnumerator<InboundFrame> frames = reader.ReadAllAsync(lifetime.Token).GetAsyncEnumerator(CancellationToken.None);

            return new CallSession(connection, lifetime, writer, frames);
        }

        /// <summary>Ends the session's reads. Called only from a live cancellation registration, which is disposed before the session is, so the lifetime source is never already disposed here.</summary>
        public void Cancel()
        {
            Lifetime.Cancel();
        }

        /// <summary>Ends the frame stream and disposes the connection; the connection's teardown is what releases a member blocked on a send, so it runs on every path.</summary>
        /// <returns>A task that completes when the session is torn down.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Teardown is the channel's liveness mechanism and runs on the fault path: the stream is ended precisely because it has failed or been cancelled, so a fault raised while ending it reports the state teardown was called for, and letting it replace the call's own fault would hide the diagnosis.")]
        public async ValueTask DisposeAsync()
        {
            try
            {
                await Frames.DisposeAsync().ConfigureAwait(false);
            }
            catch(Exception)
            {
                //Ending a stream that has already failed reports the failure teardown was called for.
            }

            try
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
            catch(Exception)
            {
                //The connection is gone either way, which is what teardown asks for.
            }

            Lifetime.Dispose();
        }
    }
}
