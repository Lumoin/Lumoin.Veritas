using System;
using System.Buffers;
using System.IO;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The frame codec of the shard-difference channel: the two connection headers under their kind bytes, and
/// every reconciliation-envelope kind through the composed <see cref="ReconciliationEnvelopeFraming{TElement}"/>
/// — so the shard and dotted channels share one envelope byte layout and differ only in their header pair. The
/// channel adds the length-prefix framing; every read validates counts and lengths against the remaining bytes
/// and refuses unknown kind bytes loudly.
/// </summary>
/// <typeparam name="TElement">The element type the envelope's elements messages carry.</typeparam>
internal sealed class ShardDifferenceFraming<TElement>
{
    /// <summary>The request header's kind byte; the channel headers sit below the envelope kinds.</summary>
    private const byte RequestHeaderKind = 1;

    /// <summary>The reply header's kind byte.</summary>
    private const byte ReplyHeaderKind = 2;

    /// <summary>The composed envelope framing every non-header frame routes through.</summary>
    private ReconciliationEnvelopeFraming<TElement> Envelopes { get; }

    /// <summary>Creates the framing bound to a contract and an optional element codec.</summary>
    /// <param name="contract">The reconciliation contract whose widths the fixed-width envelope legs are framed at.</param>
    /// <param name="writeElement">The element serializer, or <see langword="null"/> for an add-only binding that transfers no elements.</param>
    /// <param name="readElement">The element deserializer, or <see langword="null"/> for an add-only binding that transfers no elements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contract"/> is <see langword="null"/>.</exception>
    public ShardDifferenceFraming(ReconciliationContract contract, WriteReconciliationElementDelegate<TElement>? writeElement = null, ReadReconciliationElementDelegate<TElement>? readElement = null)
    {
        Envelopes = new ReconciliationEnvelopeFraming<TElement>(contract, writeElement, readElement);
    }

    /// <summary>Writes one frame: the kind byte, then the payload's fixed layout. A <see cref="SerializeMessageDelegate{TMessage}"/> the channel writer binds as a method group.</summary>
    /// <param name="frame">The frame to serialize.</param>
    /// <param name="output">The channel buffer to write into.</param>
    /// <exception cref="NotSupportedException">The frame carries an elements message and this binding injects no element serializer.</exception>
    /// <exception cref="InvalidOperationException">The frame carries no payload.</exception>
    public void WriteFrame(ShardDifferenceFrame<TElement> frame, IBufferWriter<byte> output)
    {
        if(frame.RequestHeader is { } request)
        {
            ReconciliationWireCodec.WriteByte(output, RequestHeaderKind);
            ReconciliationWireCodec.WriteInt(output, request.ShardIndex);
            WriteFingerprint(output, request.Fingerprint);
            ReconciliationWireCodec.WriteUlong(output, request.DictionaryEpoch);
            ReconciliationWireCodec.WriteInt(output, request.SymbolCap);

            return;
        }

        if(frame.ReplyHeader is { } reply)
        {
            ReconciliationWireCodec.WriteByte(output, ReplyHeaderKind);
            ReconciliationWireCodec.WriteByte(output, reply.Accepted ? (byte)1 : (byte)0);
            WriteFingerprint(output, reply.Fingerprint);
            ReconciliationWireCodec.WriteUlong(output, reply.DictionaryEpoch);

            return;
        }

        if(frame.Envelope is { } envelope)
        {
            Envelopes.WriteEnvelope(envelope, output);

            return;
        }

        throw new InvalidOperationException("A shard-difference frame must carry exactly one payload.");
    }

    /// <summary>Reads one frame from its payload bytes, refusing truncation and unknown kind bytes. A <see cref="DeserializeMessageDelegate{TMessage}"/> the channel reader binds as a method group; every read value owns its content, so the frame buffer may be released after the call.</summary>
    /// <param name="payload">The framed payload; valid only for the duration of the call.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated, carries an unknown kind byte, declares an out-of-range count or width, or carries an elements message this binding injects no element reader for.</exception>
    public ShardDifferenceFrame<TElement> ReadFrame(ReadOnlySequence<byte> payload)
    {
        SequenceReader<byte> reader = new(payload);
        byte kind = ReconciliationWireCodec.ReadByteOrThrow(ref reader);

        switch(kind)
        {
            case RequestHeaderKind:
            {
                int shardIndex = ReconciliationWireCodec.ReadInt(ref reader);
                ShardPolicyFingerprint fingerprint = ReadFingerprint(ref reader);
                ulong epoch = ReconciliationWireCodec.ReadUlong(ref reader);
                int symbolCap = ReconciliationWireCodec.ReadInt(ref reader);

                return ShardDifferenceFrame<TElement>.ForRequestHeader(new ShardDifferenceRequestHeader(shardIndex, fingerprint, epoch, symbolCap));
            }

            case ReplyHeaderKind:
            {
                byte accepted = ReconciliationWireCodec.ReadByteOrThrow(ref reader);
                if(accepted > 1)
                {
                    throw new InvalidDataException($"A shard-difference reply header carried an unknown accept byte {accepted}.");
                }

                ShardPolicyFingerprint fingerprint = ReadFingerprint(ref reader);
                ulong epoch = ReconciliationWireCodec.ReadUlong(ref reader);

                return ShardDifferenceFrame<TElement>.ForReplyHeader(new ShardDifferenceReplyHeader(accepted == 1, fingerprint, epoch));
            }

            default:
            {
                return ShardDifferenceFrame<TElement>.ForEnvelope(Envelopes.ReadEnvelope(kind, ref reader));
            }
        }
    }

    /// <summary>Writes a shard-policy fingerprint in its pinned encoding.</summary>
    /// <param name="output">The channel buffer to write into.</param>
    /// <param name="fingerprint">The fingerprint.</param>
    private static void WriteFingerprint(IBufferWriter<byte> output, ShardPolicyFingerprint fingerprint)
    {
        Span<byte> span = output.GetSpan(ShardPolicyFingerprint.EncodedByteLength);
        fingerprint.Write(span);
        output.Advance(ShardPolicyFingerprint.EncodedByteLength);
    }

    /// <summary>Reads a shard-policy fingerprint in its pinned encoding, refusing a structurally unreadable one.</summary>
    /// <param name="reader">The frame cursor, advanced past the fingerprint.</param>
    /// <returns>The declared fingerprint, foreign values carried as-is.</returns>
    /// <exception cref="InvalidDataException">The frame is truncated or the encoding version is unknown.</exception>
    private static ShardPolicyFingerprint ReadFingerprint(ref SequenceReader<byte> reader)
    {
        Span<byte> span = stackalloc byte[ShardPolicyFingerprint.EncodedByteLength];
        ReconciliationWireCodec.ReadExactly(ref reader, span);
        if(!ShardPolicyFingerprint.TryRead(span, out ShardPolicyFingerprint fingerprint))
        {
            throw new InvalidDataException("A shard-difference frame carried a structurally unreadable policy fingerprint.");
        }

        return fingerprint;
    }
}
