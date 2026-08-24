using System;
using System.Buffers;
using System.IO;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The frame codec of the dotted-difference channel: the two connection headers under their kind bytes, and
/// every reconciliation-envelope kind through the composed <see cref="ReconciliationEnvelopeFraming{TElement}"/>
/// — the shard channel's shape with the dotted header pair. The reply header's decline reason rides its own
/// version byte: the version discriminates the field's layout and an unknown version is refused as structurally
/// unreadable, while an unrecognized reason CODE under the known version parses leniently to the typed unknown
/// carrier and fails closed gracefully, never throws. The distinctness pin is enforced in both directions: an
/// accepted reply carries exactly the absent reason, and a decline never carries it, so absence and refusal
/// cannot be confused on the wire.
/// </summary>
/// <typeparam name="TElement">The element type the envelope's elements messages carry.</typeparam>
internal sealed class DottedDifferenceFraming<TElement>
{
    /// <summary>The request header's kind byte; the channel headers sit below the envelope kinds.</summary>
    private const byte RequestHeaderKind = 1;

    /// <summary>The reply header's kind byte.</summary>
    private const byte ReplyHeaderKind = 2;

    /// <summary>The decline-reason field's layout version this build writes and reads; a future layout bumps it, and a reader refuses a version it does not know rather than misreading the field.</summary>
    private const byte DeclineReasonVersion = 1;

    /// <summary>The composed envelope framing every non-header frame routes through.</summary>
    private ReconciliationEnvelopeFraming<TElement> Envelopes { get; }

    /// <summary>Creates the framing bound to a contract and the dotted element codec.</summary>
    /// <param name="contract">The reconciliation contract whose widths the fixed-width envelope legs are framed at.</param>
    /// <param name="writeElement">The element serializer for the dotted elements leg.</param>
    /// <param name="readElement">The element deserializer for the dotted elements leg.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/>, <paramref name="writeElement"/>, or <paramref name="readElement"/> is <see langword="null"/>.</exception>
    public DottedDifferenceFraming(ReconciliationContract contract, WriteReconciliationElementDelegate<TElement> writeElement, ReadReconciliationElementDelegate<TElement> readElement)
    {
        ArgumentNullException.ThrowIfNull(writeElement);
        ArgumentNullException.ThrowIfNull(readElement);

        Envelopes = new ReconciliationEnvelopeFraming<TElement>(contract, writeElement, readElement);
    }

    /// <summary>Writes one frame: the kind byte, then the payload's fixed layout. A <see cref="SerializeMessageDelegate{TMessage}"/> the channel writer binds as a method group.</summary>
    /// <param name="frame">The frame to serialize.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="InvalidOperationException">The frame carries no payload, or a reply header violates the distinctness pin (an accepted reply with a real reason, or a decline with the absent one).</exception>
    public void WriteFrame(DottedDifferenceFrame<TElement> frame, IBufferWriter<byte> output)
    {
        if(frame.RequestHeader is { } request)
        {
            ReconciliationWireCodec.WriteByte(output, RequestHeaderKind);
            ReconciliationWireCodec.WriteUlong(output, request.DictionaryEpoch);
            WriteDeclaration(output, request.Declaration);
            ReconciliationWireCodec.WriteInt(output, request.SymbolCap);

            return;
        }

        if(frame.ReplyHeader is { } reply)
        {
            if(reply.Accepted != (reply.DeclineReason == DottedDifferenceDeclineReason.None))
            {
                throw new InvalidOperationException("A dotted-difference reply header must carry the absent decline reason exactly when it accepts; a decline always carries a real reason.");
            }

            ReconciliationWireCodec.WriteByte(output, ReplyHeaderKind);
            ReconciliationWireCodec.WriteByte(output, reply.Accepted ? (byte)1 : (byte)0);
            ReconciliationWireCodec.WriteUlong(output, reply.DictionaryEpoch);
            WriteDeclaration(output, reply.Declaration);
            ReconciliationWireCodec.WriteByte(output, DeclineReasonVersion);
            ReconciliationWireCodec.WriteByte(output, reply.DeclineReason.Code);

            return;
        }

        if(frame.Envelope is { } envelope)
        {
            Envelopes.WriteEnvelope(envelope, output);

            return;
        }

        throw new InvalidOperationException("A dotted-difference frame must carry exactly one payload.");
    }

    /// <summary>Reads one frame from its payload bytes, refusing truncation and unknown kind bytes. A <see cref="DeserializeMessageDelegate{TMessage}"/> the channel reader binds as a method group; every read value owns its content, so the frame buffer may be released after the call.</summary>
    /// <param name="payload">The framed payload; valid only for the duration of the call.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, carries an unknown kind byte or decline-reason layout version, declares an out-of-range count or width, or violates the decline reason's distinctness pin.</exception>
    public DottedDifferenceFrame<TElement> ReadFrame(ReadOnlySequence<byte> payload)
    {
        SequenceReader<byte> reader = new(payload);
        byte kind = ReconciliationWireCodec.ReadByteOrThrow(ref reader);

        switch(kind)
        {
            case RequestHeaderKind:
            {
                ulong epoch = ReconciliationWireCodec.ReadUlong(ref reader);
                ReconciliationOffer declaration = ReadDeclaration(ref reader);
                int symbolCap = ReconciliationWireCodec.ReadInt(ref reader);

                return DottedDifferenceFrame<TElement>.ForRequestHeader(new DottedDifferenceRequestHeader(epoch, declaration, symbolCap));
            }

            case ReplyHeaderKind:
            {
                byte accepted = ReconciliationWireCodec.ReadByteOrThrow(ref reader);
                if(accepted > 1)
                {
                    throw new InvalidDataException($"A dotted-difference reply header carried an unknown accept byte {accepted}.");
                }

                ulong epoch = ReconciliationWireCodec.ReadUlong(ref reader);
                ReconciliationOffer declaration = ReadDeclaration(ref reader);
                byte reasonVersion = ReconciliationWireCodec.ReadByteOrThrow(ref reader);
                if(reasonVersion != DeclineReasonVersion)
                {
                    throw new InvalidDataException($"A dotted-difference reply header carried decline-reason layout version {reasonVersion}; this build reads version {DeclineReasonVersion}.");
                }

                byte reasonCode = ReconciliationWireCodec.ReadByteOrThrow(ref reader);
                DottedDifferenceDeclineReason reason = DottedDifferenceDeclineReason.Create(reasonCode);
                if((accepted == 1) != (reason == DottedDifferenceDeclineReason.None))
                {
                    throw new InvalidDataException("A dotted-difference reply header violates the decline reason's distinctness pin: an accepted reply carries exactly the absent reason, and a decline never carries it.");
                }

                return DottedDifferenceFrame<TElement>.ForReplyHeader(new DottedDifferenceReplyHeader(accepted == 1, epoch, declaration, reason));
            }

            default:
            {
                return DottedDifferenceFrame<TElement>.ForEnvelope(Envelopes.ReadEnvelope(kind, ref reader));
            }
        }
    }

    /// <summary>Writes an offer-shaped contract declaration: the domain byte, the two widths, and the fixed eight key-check bytes — the offer envelope's own field layout, reused so the header and the session pin one encoding.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="declaration">The declaration to write.</param>
    private static void WriteDeclaration(IBufferWriter<byte> output, ReconciliationOffer declaration)
    {
        ReconciliationWireCodec.WriteByte(output, (byte)declaration.ItemDomain);
        ReconciliationWireCodec.WriteInt(output, declaration.ItemWidth);
        ReconciliationWireCodec.WriteInt(output, declaration.ChecksumWidth);
        output.Write(declaration.KeyCheck.Span);
    }

    /// <summary>Reads an offer-shaped contract declaration, refusing truncation and out-of-range widths before the offer type's own validation runs.</summary>
    /// <param name="reader">The frame cursor, advanced past the declaration.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="InvalidDataException">The declaration is truncated or declares an unknown domain or out-of-range widths.</exception>
    private static ReconciliationOffer ReadDeclaration(ref SequenceReader<byte> reader)
    {
        byte domain = ReconciliationWireCodec.ReadByteOrThrow(ref reader);
        if(domain is not ((byte)ReconciliationItemDomain.ContentHash or (byte)ReconciliationItemDomain.Structural))
        {
            throw new InvalidDataException($"A dotted-difference header carried an unknown item-domain byte {domain}.");
        }

        int itemWidth = ReconciliationWireCodec.ReadInt(ref reader);
        int checksumWidth = ReconciliationWireCodec.ReadInt(ref reader);
        if(itemWidth is < 1 or > ReconciliationEnvelopeFraming<TElement>.MaximumOfferItemWidth || checksumWidth is < 1 or > ReconciliationEnvelopeFraming<TElement>.MaximumOfferChecksumWidth)
        {
            throw new InvalidDataException("A dotted-difference header declared out-of-range contract widths.");
        }

        Span<byte> keyCheck = stackalloc byte[ReconciliationEnvelopeFraming<TElement>.KeyCheckByteLength];
        ReconciliationWireCodec.ReadExactly(ref reader, keyCheck);

        return new ReconciliationOffer((ReconciliationItemDomain)domain, itemWidth, checksumWidth, keyCheck.ToArray());
    }
}
