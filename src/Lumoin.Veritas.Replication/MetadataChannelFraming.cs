using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The correlated frame layer of the metadata channel. Every frame carries the same fixed header — an
/// eight-byte big-endian correlation id, a one-byte kind naming which of the four exchanges the frame belongs
/// to (<see cref="RecordExchangeKind"/>, <see cref="CommittedReadKind"/>, <see cref="RecordPushKind"/>,
/// <see cref="VersionProbeKind"/>), and a one-byte body discriminator saying whether what follows is a payload,
/// nothing at all, or a fault — and the record-carrying payload bytes after it are OPAQUE here. Those metadata
/// codecs are applied by the client and the server, so one framing serves any encoding and this layer names
/// none of them.
/// </summary>
/// <remarks>
/// <para>
/// CORRELATION IS THE TRANSPORT'S OWN OBLIGATION. The consensus envelopes carry a version, which catches a
/// reply mis-routed from ANOTHER instance, and nothing in them tells two overlapping calls to one recorder
/// apart. The serving side echoes the id and the kind on its answering frame, so a caller that reads an answer
/// carrying another id has found a mis-paired stream and refuses it instead of folding another call's answer
/// into this one.
/// </para>
/// <para>
/// BOUNDED READS, in the sibling framings' discipline: a frame shorter than the header is refused; an unknown
/// kind or body byte is refused as structurally unreadable rather than guessed at; an absent body that carries
/// trailing bytes is refused; a fault body carries exactly its one code byte; and a payload body carries at
/// least one byte, because the absent body is how a frame says it carries nothing and a zero-length payload
/// would make the two indistinguishable. A payload is copied once into memory rented from the caller's pool,
/// which the read frame owns and its consumer disposes.
/// </para>
/// <para>
/// THE VERSION PROBE'S ANSWER IS THE ONE BODY THIS LAYER ENCODES ITSELF, and it is not an exception to the rule
/// above so much as a case the rule does not reach: the answer carries no application value, only the answering
/// host's replica identity and the register version it holds, both of a width the consensus library fixes, so
/// there is no deployment-chosen codec for it to be opaque to. It is written in this layer's own idiom — the
/// identity's thirty-two bytes as they stand, then the version as an eight-byte big-endian value, exactly as the
/// header writes its correlation id.
/// </para>
/// <para>
/// A FAULT FRAME IS OPAQUE BY DESIGN: it carries a closed code naming the class of failure and never the
/// serving side's exception text, so a serve that failed tells the caller that it failed without publishing the
/// host's internals across the wire.
/// </para>
/// </remarks>
internal static class MetadataChannelFraming
{
    /// <summary>The request/reply exchange that carries one consensus record request and its reply.</summary>
    internal const byte RecordExchangeKind = 1;

    /// <summary>The catch-up exchange that asks a host for the committed record it has learned.</summary>
    internal const byte CommittedReadKind = 2;

    /// <summary>The dissemination exchange that offers a decided record to a host.</summary>
    internal const byte RecordPushKind = 3;

    /// <summary>The readiness exchange that asks a host which version it holds and is answered with that host's own identity beside it.</summary>
    internal const byte VersionProbeKind = 4;

    /// <summary>The body discriminator for a frame that carries nothing after its header: a call with no argument, or an answer whose result is the absence of a record.</summary>
    internal const byte AbsentBody = 0;

    /// <summary>The body discriminator for a frame whose remaining bytes are the opaque codec payload.</summary>
    internal const byte PayloadBody = 1;

    /// <summary>The body discriminator for a frame that reports the serving side could not answer; the remaining byte is the fault code.</summary>
    internal const byte FaultBody = 2;

    /// <summary>The fault code for a failure the serving side did not classify.</summary>
    internal const byte UnspecifiedFault = 0;

    /// <summary>The fault code for a call the serving side accepted and could not complete — its local seam faulted.</summary>
    internal const byte ServeFailedFault = 1;

    /// <summary>The fault code for a call whose payload the serving side could not read as the message the exchange carries.</summary>
    internal const byte MalformedPayloadFault = 2;

    /// <summary>The version probe's answer body: the answering host's thirty-two identity bytes, then its committed version as an eight-byte big-endian value.</summary>
    internal const int VersionReportByteLength = ReplicaId.Size + StoreIncarnation.Size + sizeof(ulong);

    /// <summary>The fixed frame header: the eight-byte big-endian correlation id, the kind byte, then the body byte.</summary>
    private const int HeaderByteLength = sizeof(ulong) + sizeof(byte) + sizeof(byte);

    /// <summary>The kind byte's offset inside the header.</summary>
    private const int KindOffset = sizeof(ulong);

    /// <summary>The body byte's offset inside the header.</summary>
    private const int BodyOffset = sizeof(ulong) + sizeof(byte);

    /// <summary>Writes one frame's opaque payload bytes straight into the channel buffer, so a message reaches the wire without a staging copy of its own.</summary>
    /// <param name="output">The channel buffer to write the payload into.</param>
    internal delegate void WriteFramePayloadDelegate(IBufferWriter<byte> output);

    /// <summary>Writes one frame: the fixed header, then the body its discriminator names. A <see cref="Lumoin.Verisync.Core.SerializeMessageDelegate{TMessage}"/> the channel writer binds as a method group, so it carries no closure.</summary>
    /// <param name="frame">The frame to serialize.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="InvalidOperationException">The frame's body discriminator is not one this build writes.</exception>
    internal static void WriteFrame(OutboundFrame frame, IBufferWriter<byte> output)
    {
        Span<byte> header = output.GetSpan(HeaderByteLength);
        BinaryPrimitives.WriteUInt64BigEndian(header, frame.CorrelationId);
        header[KindOffset] = frame.Kind;
        header[BodyOffset] = frame.Body;
        output.Advance(HeaderByteLength);

        switch(frame.Body)
        {
            case AbsentBody:
            {
                break;
            }

            case PayloadBody:
            {
                //The payload writer is the frame's own invariant: the payload factory refuses a frame without
                //one, so reaching here without it is a construction path that bypassed the factory.
                frame.WritePayload!(output);

                break;
            }

            case FaultBody:
            {
                Span<byte> code = output.GetSpan(sizeof(byte));
                code[0] = frame.FaultCode;
                output.Advance(sizeof(byte));

                break;
            }

            default:
            {
                throw new InvalidOperationException("A metadata channel frame carries a body this build does not write.");
            }
        }
    }

    /// <summary>Reads one frame, copying an opaque payload once into pool-backed memory the returned frame owns. A <see cref="Lumoin.Verisync.Core.DeserializeOwnedMessageDelegate{TMessage}"/> the channel reader binds as a method group; ownership of the result transfers to the consumer.</summary>
    /// <param name="payload">The framed payload; valid only for the duration of the call.</param>
    /// <param name="pool">The pool an opaque payload is copied into.</param>
    /// <returns>The frame, owning its payload rental.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">The frame is shorter than its header, names an unknown kind or body, carries trailing bytes an absent body cannot hold, carries a fault body that is not exactly one code byte, carries an empty payload body, or declares more payload bytes than a buffer can hold.</exception>
    internal static InboundFrame ReadOwnedFrame(ReadOnlySequence<byte> payload, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if(payload.Length < HeaderByteLength)
        {
            throw new InvalidDataException("A metadata channel frame is shorter than its correlation, kind, and body header.");
        }

        Span<byte> header = stackalloc byte[HeaderByteLength];
        payload.Slice(0, HeaderByteLength).CopyTo(header);
        ulong correlationId = BinaryPrimitives.ReadUInt64BigEndian(header);
        byte kind = ReadKind(header[KindOffset]);
        byte body = header[BodyOffset];
        ReadOnlySequence<byte> rest = payload.Slice(HeaderByteLength);

        switch(body)
        {
            case AbsentBody:
            {
                if(!rest.IsEmpty)
                {
                    throw new InvalidDataException("A metadata channel frame with an absent body carries no bytes after its header.");
                }

                return new InboundFrame(correlationId, kind, AbsentBody, UnspecifiedFault, null, 0);
            }

            case FaultBody:
            {
                if(rest.Length != sizeof(byte))
                {
                    throw new InvalidDataException("A metadata channel fault frame carries exactly one fault code byte after its header.");
                }

                Span<byte> code = stackalloc byte[sizeof(byte)];
                rest.CopyTo(code);

                return new InboundFrame(correlationId, kind, FaultBody, code[0], null, 0);
            }

            case PayloadBody:
            {
                if(rest.IsEmpty)
                {
                    //The absent body is how a frame says it carries nothing, so an empty payload body would
                    //make the two indistinguishable and is refused at the wire instead.
                    throw new InvalidDataException("A metadata channel frame with a payload body carries at least one payload byte.");
                }

                if(rest.Length > int.MaxValue)
                {
                    throw new InvalidDataException("A metadata channel frame declared more payload bytes than a single buffer can hold.");
                }

                int length = (int)rest.Length;
                IMemoryOwner<byte> owner = pool.Rent(length);

                //The rental is returned before any throw so a rejected frame leaks nothing, per the owned-
                //deserializer contract.
                try
                {
                    rest.CopyTo(owner.Memory.Span[..length]);
                }
                catch
                {
                    owner.Dispose();

                    throw;
                }

                return new InboundFrame(correlationId, kind, PayloadBody, UnspecifiedFault, owner, length);
            }

            default:
            {
                throw new InvalidDataException("A metadata channel frame carries an unknown body byte.");
            }
        }
    }

    /// <summary>Writes one version probe answer: the answering host's replica, the store it holds, then the version it holds. A <see cref="WriteFramePayloadDelegate"/> the serving side binds through its own payload frame.</summary>
    /// <param name="report">The answering host's report: the version it holds beside the identity it asserts for itself.</param>
    /// <param name="output">The channel buffer to write into.</param>
    internal static void WriteVersionReport(MemberVersionReport report, IBufferWriter<byte> output)
    {
        Span<byte> body = output.GetSpan(VersionReportByteLength);
        report.Recorder.Replica.CopyTo(body);
        report.Recorder.Incarnation.CopyTo(body[ReplicaId.Size..]);
        BinaryPrimitives.WriteUInt64BigEndian(body[(ReplicaId.Size + StoreIncarnation.Size)..], report.Version.Value);
        output.Advance(VersionReportByteLength);
    }

    /// <summary>Reads one version probe answer back, refusing a body that is not exactly what the exchange carries.</summary>
    /// <param name="payload">The frame's payload bytes.</param>
    /// <returns>The answering host's report.</returns>
    /// <exception cref="InvalidDataException">The body is not the report's fixed width, or carries a version outside the register's range.</exception>
    /// <remarks>
    /// The identity read here is the answering host's own claim and never the member this side aimed at, which
    /// is what lets the register refuse a probe route that landed on the wrong host instead of counting its
    /// answer. It carries the store beside the replica, so a probe answered under the right replica by a store
    /// the membership never admitted is refused on the same comparison.
    /// </remarks>
    internal static MemberVersionReport ReadVersionReport(ReadOnlySequence<byte> payload)
    {
        if(payload.Length != VersionReportByteLength)
        {
            throw new InvalidDataException("A metadata version probe is answered with the reporting host's identity and its version, and this body is not that width.");
        }

        Span<byte> body = stackalloc byte[VersionReportByteLength];
        payload.CopyTo(body);

        ulong version = BinaryPrimitives.ReadUInt64BigEndian(body[(ReplicaId.Size + StoreIncarnation.Size)..]);
        if(version > RegisterVersion.MaxValue.Value)
        {
            //The register's own range check would raise an argument fault, which reads as this side's mistake;
            //a body no host could have produced is the peer's frame to fix and is refused as such.
            throw new InvalidDataException("A metadata version probe answer carries a version above the register's range, which no host can hold.");
        }

        HostId answered = new(
            ReplicaId.FromSpan(body[..ReplicaId.Size]),
            StoreIncarnation.FromSpan(body.Slice(ReplicaId.Size, StoreIncarnation.Size)));

        return new MemberVersionReport(answered, new RegisterVersion(version));
    }

    /// <summary>Maps a wire kind byte to the exchange it names, refusing an unknown value rather than guessing which exchange a frame belongs to.</summary>
    /// <param name="value">The kind byte read from a frame.</param>
    /// <returns>The kind the byte names.</returns>
    /// <exception cref="InvalidDataException">The byte is not a kind this build serves.</exception>
    private static byte ReadKind(byte value)
    {
        return value switch
        {
            RecordExchangeKind or CommittedReadKind or RecordPushKind or VersionProbeKind => value,
            _ => throw new InvalidDataException("A metadata channel frame carries an unknown exchange kind byte."),
        };
    }

    /// <summary>
    /// One frame on its way out: the header fields, and — for a payload body — the seam that writes the
    /// opaque payload bytes straight into the channel buffer. The factories are the only producers, so a frame
    /// always agrees with its own body discriminator.
    /// </summary>
    internal sealed class OutboundFrame
    {
        /// <summary>Creates a frame over its header fields and, for a payload body, its payload writer.</summary>
        /// <param name="correlationId">The call's correlation id.</param>
        /// <param name="kind">The exchange the frame belongs to.</param>
        /// <param name="body">The body discriminator.</param>
        /// <param name="faultCode">The fault code; read only for a fault body.</param>
        /// <param name="writePayload">The payload writer; present exactly for a payload body.</param>
        private OutboundFrame(ulong correlationId, byte kind, byte body, byte faultCode, WriteFramePayloadDelegate? writePayload)
        {
            CorrelationId = correlationId;
            Kind = kind;
            Body = body;
            FaultCode = faultCode;
            WritePayload = writePayload;
        }

        /// <summary>The call's correlation id, echoed by the answering side.</summary>
        internal ulong CorrelationId { get; }

        /// <summary>The exchange the frame belongs to.</summary>
        internal byte Kind { get; }

        /// <summary>The body discriminator naming what follows the header.</summary>
        internal byte Body { get; }

        /// <summary>The fault code; meaningful only for a fault body.</summary>
        internal byte FaultCode { get; }

        /// <summary>The seam that writes the opaque payload bytes, or <see langword="null"/> when the body carries none.</summary>
        internal WriteFramePayloadDelegate? WritePayload { get; }

        /// <summary>Creates a frame whose body is the opaque payload the writer produces.</summary>
        /// <param name="correlationId">The call's correlation id.</param>
        /// <param name="kind">The exchange the frame belongs to.</param>
        /// <param name="writePayload">The seam that writes the payload bytes.</param>
        /// <returns>The frame.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="writePayload"/> is <see langword="null"/>.</exception>
        internal static OutboundFrame ForPayload(ulong correlationId, byte kind, WriteFramePayloadDelegate writePayload)
        {
            ArgumentNullException.ThrowIfNull(writePayload);

            return new OutboundFrame(correlationId, kind, PayloadBody, UnspecifiedFault, writePayload);
        }

        /// <summary>Creates a frame that carries nothing after its header — a call with no argument, or an answer whose result is the absence of a record.</summary>
        /// <param name="correlationId">The call's correlation id.</param>
        /// <param name="kind">The exchange the frame belongs to.</param>
        /// <returns>The frame.</returns>
        internal static OutboundFrame ForAbsent(ulong correlationId, byte kind)
        {
            return new OutboundFrame(correlationId, kind, AbsentBody, UnspecifiedFault, null);
        }

        /// <summary>Creates a frame reporting that the serving side could not answer the call, naming the fault's class and nothing else.</summary>
        /// <param name="correlationId">The call's correlation id.</param>
        /// <param name="kind">The exchange the frame belongs to.</param>
        /// <param name="faultCode">The fault's class.</param>
        /// <returns>The frame.</returns>
        internal static OutboundFrame ForFault(ulong correlationId, byte kind, byte faultCode)
        {
            return new OutboundFrame(correlationId, kind, FaultBody, faultCode, null);
        }
    }

    /// <summary>
    /// One frame that was read, OWNING the pooled memory its opaque payload was copied into. The consumer
    /// disposes it exactly once; a frame with an absent or fault body holds no rental, so disposing it is a
    /// no-op.
    /// </summary>
    internal sealed class InboundFrame: IDisposable
    {
        /// <summary>Creates a read frame over its header fields and, for a payload body, the rental holding its bytes.</summary>
        /// <param name="correlationId">The call's correlation id.</param>
        /// <param name="kind">The exchange the frame belongs to.</param>
        /// <param name="body">The body discriminator.</param>
        /// <param name="faultCode">The fault code; meaningful only for a fault body.</param>
        /// <param name="owner">The rental holding the payload bytes, or <see langword="null"/> when the body carries none; this frame disposes it.</param>
        /// <param name="length">The number of payload bytes at the start of <paramref name="owner"/>'s memory.</param>
        internal InboundFrame(ulong correlationId, byte kind, byte body, byte faultCode, IMemoryOwner<byte>? owner, int length)
        {
            CorrelationId = correlationId;
            Kind = kind;
            Body = body;
            FaultCode = faultCode;
            Owner = owner;
            Length = length;
        }

        /// <summary>The call's correlation id.</summary>
        internal ulong CorrelationId { get; }

        /// <summary>The exchange the frame belongs to.</summary>
        internal byte Kind { get; }

        /// <summary>The body discriminator naming what followed the header.</summary>
        internal byte Body { get; }

        /// <summary>The fault code; meaningful only when <see cref="IsFault"/> is <see langword="true"/>.</summary>
        internal byte FaultCode { get; }

        /// <summary>Whether the frame reports that the serving side could not answer.</summary>
        internal bool IsFault => Body == FaultBody;

        /// <summary>Whether the frame carries opaque payload bytes.</summary>
        internal bool HasPayload => Body == PayloadBody;

        /// <summary>The opaque payload bytes, or an empty sequence when the body carries none; valid until this frame is disposed.</summary>
        internal ReadOnlySequence<byte> Payload => Owner is null ? ReadOnlySequence<byte>.Empty : new ReadOnlySequence<byte>(Owner.Memory[..Length]);

        /// <summary>The rental holding the payload bytes, or <see langword="null"/> when the body carries none.</summary>
        private IMemoryOwner<byte>? Owner { get; }

        /// <summary>The number of payload bytes at the start of the rental.</summary>
        private int Length { get; }

        /// <summary>Returns the payload rental to its pool; a frame that holds none disposes as a no-op.</summary>
        public void Dispose()
        {
            Owner?.Dispose();
        }
    }
}
