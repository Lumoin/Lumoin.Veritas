using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The per-message serializers the sketch-fetch transport binds to a Verisync message channel. A request carries a
/// domain-and-epoch contract stamp before its symbol budget; a response carries the same stamp before the peer's
/// sketch-image bytes. The leading domain byte doubles as the frame's format discriminator, so an unknown value is
/// refused at the wire. The channel adds the length-prefix framing, so these only turn one message into bytes and
/// back; they are pure functions of their arguments — they capture nothing — so they bind as method groups without
/// a closure, and the sketch image is a self-describing, already-checksummed blob, so the response needs no encoding
/// beyond the raw bytes after its stamp.
/// </summary>
internal static class SketchChannelFraming
{
    /// <summary>The stamp both frames lead with: a one-byte domain discriminator followed by an eight-byte big-endian dictionary epoch.</summary>
    private const int StampByteLength = sizeof(byte) + sizeof(ulong);

    /// <summary>The fixed request length: the domain-and-epoch stamp followed by the four-byte big-endian symbol budget.</summary>
    private const int RequestByteLength = StampByteLength + sizeof(int);

    /// <summary>Writes a stamped symbol-budget request: the domain byte, the eight-byte big-endian epoch, then the four-byte big-endian budget.</summary>
    /// <param name="request">The stamped request to serialize.</param>
    /// <param name="output">The channel buffer to write into.</param>
    internal static void WriteRequest(SketchChannelRequest request, IBufferWriter<byte> output)
    {
        Span<byte> span = output.GetSpan(RequestByteLength);
        span[0] = (byte)request.Domain;
        BinaryPrimitives.WriteUInt64BigEndian(span[sizeof(byte)..], request.DictionaryEpoch);
        BinaryPrimitives.WriteInt32BigEndian(span[StampByteLength..], request.SymbolBudget);
        output.Advance(RequestByteLength);
    }

    /// <summary>Reads a stamped symbol-budget request from its fixed-length frame.</summary>
    /// <param name="payload">The framed request payload.</param>
    /// <returns>The stamped request.</returns>
    /// <exception cref="InvalidDataException">The frame is shorter than a stamped request, or its domain byte is not a known <see cref="SketchChannelDomain"/>.</exception>
    internal static SketchChannelRequest ReadRequest(ReadOnlySequence<byte> payload)
    {
        if(payload.Length < RequestByteLength)
        {
            throw new InvalidDataException("A sketch-fetch request frame is shorter than its domain-and-epoch stamp and symbol budget.");
        }

        Span<byte> span = stackalloc byte[RequestByteLength];
        payload.Slice(0, RequestByteLength).CopyTo(span);
        SketchChannelDomain domain = ReadDomain(span[0]);
        ulong epoch = BinaryPrimitives.ReadUInt64BigEndian(span[sizeof(byte)..]);
        int symbolBudget = BinaryPrimitives.ReadInt32BigEndian(span[StampByteLength..]);

        return new SketchChannelRequest(domain, epoch, symbolBudget);
    }

    /// <summary>Writes a stamped sketch-image response: the domain byte, the eight-byte big-endian epoch, then the image's raw, self-describing bytes (empty for a stamped decline). A serializer the response channel's <see cref="Lumoin.Verisync.Core.MessageChannelWriter{TMessage}"/> binds.</summary>
    /// <param name="response">The stamped response to serialize; its image is empty for a stamped decline.</param>
    /// <param name="output">The channel buffer to write into.</param>
    internal static void WriteStampedImage(SketchChannelResponse response, IBufferWriter<byte> output)
    {
        Span<byte> stamp = output.GetSpan(StampByteLength);
        stamp[0] = (byte)response.Domain;
        BinaryPrimitives.WriteUInt64BigEndian(stamp[sizeof(byte)..], response.DictionaryEpoch);
        output.Advance(StampByteLength);
        output.Write(response.Image.Span);
    }

    /// <summary>Reads a stamped sketch-image response into a pool-owning result the session loads and verifies: the stamp is parsed, then the remaining payload bytes are copied once into a pooled buffer the receiver owns and disposes. A stamped frame with no image bytes is a stamped decline — <see cref="SketchFetchResult.HasImage"/> is <see langword="false"/> yet it carries a domain and epoch — distinct from an absent frame. A <see cref="Lumoin.Verisync.Core.DeserializeOwnedMessageDelegate{TMessage}"/> the response channel binds.</summary>
    /// <param name="payload">The framed response payload; valid only for the duration of the call.</param>
    /// <param name="pool">The pool the owned image is rented from.</param>
    /// <returns>The peer's stamped sketch image as an owning <see cref="SketchFetchResult"/>.</returns>
    /// <exception cref="InvalidDataException">The frame is shorter than its domain-and-epoch stamp, or its domain byte is not a known <see cref="SketchChannelDomain"/>.</exception>
    internal static SketchFetchResult ReadOwnedImage(ReadOnlySequence<byte> payload, MemoryPool<byte> pool)
    {
        if(payload.Length < StampByteLength)
        {
            throw new InvalidDataException("A sketch-fetch response frame is shorter than its domain-and-epoch stamp.");
        }

        Span<byte> stamp = stackalloc byte[StampByteLength];
        payload.Slice(0, StampByteLength).CopyTo(stamp);
        SketchChannelDomain domain = ReadDomain(stamp[0]);
        ulong epoch = BinaryPrimitives.ReadUInt64BigEndian(stamp[sizeof(byte)..]);

        ReadOnlySequence<byte> image = payload.Slice(StampByteLength);
        int length = (int)image.Length;
        if(length == 0)
        {
            //A stamped frame with no image is the peer's stamped decline: it carries the contract stamp but no
            //sketch to load, so it holds no rental.
            return new SketchFetchResult(null, 0, domain, epoch);
        }

        IMemoryOwner<byte> owner = pool.Rent(length);

        //The rental is returned before any throw so a rejected frame leaks nothing, per the owned-deserializer contract.
        try
        {
            image.CopyTo(owner.Memory.Span[..length]);
        }
        catch
        {
            owner.Dispose();
            throw;
        }

        return new SketchFetchResult(owner, length, domain, epoch);
    }

    /// <summary>Maps a wire domain byte to its <see cref="SketchChannelDomain"/>, refusing an unknown value as the format discriminator the byte doubles as.</summary>
    /// <param name="value">The leading domain byte read from a frame.</param>
    /// <returns>The domain the byte names.</returns>
    /// <exception cref="InvalidDataException">The byte is not a known <see cref="SketchChannelDomain"/>.</exception>
    private static SketchChannelDomain ReadDomain(byte value)
    {
        SketchChannelDomain domain = (SketchChannelDomain)value;

        return domain switch
        {
            SketchChannelDomain.Structural or SketchChannelDomain.ContentHash => domain,
            _ => throw new InvalidDataException($"A sketch-channel frame carries an unknown domain byte '{value}'."),
        };
    }
}
