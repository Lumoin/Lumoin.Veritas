using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Cbor.Internal;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Reads CBOR data items from a byte source. Mirrors <see cref="CborWriter"/>
/// in shape: a small state machine over a stack of open containers, with
/// tag-content pairing and conformance-mode enforcement.
/// </summary>
/// <remarks>
/// <para>
/// <b>Source.</b> The reader accepts either a contiguous
/// <see cref="ReadOnlyMemory{T}"/> or a possibly-discontiguous
/// <see cref="ReadOnlySequence{T}"/>. Multi-segment sequences are walked
/// in place via <see cref="SequenceReader{T}"/>; the reader does not
/// materialise multi-segment input into a contiguous buffer at
/// construction. Construction allocates a small bounded amount
/// regardless of the input sequence's total size.
/// </para>
/// <para>
/// <b>State.</b> Use <see cref="PeekState"/> to look at the next data
/// item without consuming, then call the matching <c>Read*</c> method to
/// advance. Container-bounded loops in caller code drive iteration:
/// <c>ReadStartArray</c> returns the definite count or <c>null</c> for
/// indefinite, after which the caller consumes children until
/// <see cref="PeekState"/> returns <see cref="CborReaderState.EndArray"/>.
/// </para>
/// </remarks>
public sealed class CborReader
{
    private ReadOnlySequence<byte> source;
    private readonly CborSerializerOptions options;
    private readonly MemoryPool<byte>? pool;
    private readonly CborStringInternPool? stringInternPool;
    private readonly Stack<ContainerFrame> stack = new();
    private long consumed;
    private int pendingTagDepth;

    /// <summary>
    /// Initialises a new reader over the contiguous byte buffer
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="options">The conformance options.</param>
    /// <param name="pool">An optional pool used by
    /// <see cref="ReadByteStringPooled"/> for the indefinite-length and
    /// cross-segment cases; when <c>null</c> the reader falls back to
    /// <see cref="MemoryPool{T}.Shared"/>. Any <see cref="MemoryPool{T}"/>
    /// is accepted, including a consumer's own pool; buffers a pool returns
    /// larger than the requested size are trimmed to an exact-length view.</param>
    /// <param name="stringInternPool">An optional intern pool consulted by
    /// <see cref="ReadTextString"/>; repeated reads of the same UTF-8 bytes
    /// return the same <see cref="string"/> instance, skipping both the
    /// UTF-8 → UTF-16 decode and the allocation. Useful for workloads that
    /// decode many similar documents (CAR / firehose, JSON-LD contexts).</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public CborReader(ReadOnlyMemory<byte> source, CborSerializerOptions options, MemoryPool<byte>? pool = null, CborStringInternPool? stringInternPool = null)
        : this(new ReadOnlySequence<byte>(source), options, pool, stringInternPool)
    {
    }

    /// <summary>
    /// Initialises a new reader over the byte sequence
    /// <paramref name="source"/>. Multi-segment sequences are walked in
    /// place via <see cref="SequenceReader{T}"/>; construction does not
    /// materialise the sequence into a contiguous buffer.
    /// </summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="options">The conformance options.</param>
    /// <param name="pool">An optional pool used by
    /// <see cref="ReadByteStringPooled"/> for the indefinite-length and
    /// cross-segment cases; when <c>null</c> the reader falls back to
    /// <see cref="MemoryPool{T}.Shared"/>. Any <see cref="MemoryPool{T}"/>
    /// is accepted, including a consumer's own pool; buffers a pool returns
    /// larger than the requested size are trimmed to an exact-length view.</param>
    /// <param name="stringInternPool">An optional intern pool consulted by
    /// <see cref="ReadTextString"/>; repeated reads of the same UTF-8 bytes
    /// return the same <see cref="string"/> instance, skipping both the
    /// UTF-8 → UTF-16 decode and the allocation. Useful for workloads that
    /// decode many similar documents (CAR / firehose, JSON-LD contexts).</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public CborReader(ReadOnlySequence<byte> source, CborSerializerOptions options, MemoryPool<byte>? pool = null, CborStringInternPool? stringInternPool = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.source = source;
        this.options = options;
        this.pool = pool;
        this.stringInternPool = stringInternPool;
    }

    /// <summary>
    /// Re-targets this reader at <paramref name="newSource"/> without
    /// allocating a new <see cref="CborReader"/>. Clears the container
    /// stack and resets consumed-byte and pending-tag counters. Options,
    /// pool, and intern-pool references are preserved.
    /// </summary>
    /// <remarks>
    /// Designed for hot paths that decode many independent CBOR blocks
    /// in sequence (CARv1 sections, AT Protocol firehose frames). Each
    /// reset is O(1) plus stack-clear, vs allocating a fresh reader
    /// (~architecture-dependent but measurable at firehose rates).
    /// Callers must ensure no in-flight read against the previous source
    /// is observing zero-copy spans (e.g. <see cref="ReadByteStringSpan"/>),
    /// since <see cref="Reset(ReadOnlyMemory{byte})"/> invalidates them.
    /// </remarks>
    /// <param name="newSource">The new contiguous byte source.</param>
    public void Reset(ReadOnlyMemory<byte> newSource)
    {
        Reset(new ReadOnlySequence<byte>(newSource));
    }

    /// <summary>
    /// Re-targets this reader at <paramref name="newSource"/>; see
    /// <see cref="Reset(ReadOnlyMemory{byte})"/>.
    /// </summary>
    /// <param name="newSource">The new byte sequence.</param>
    public void Reset(ReadOnlySequence<byte> newSource)
    {
        source = newSource;
        stack.Clear();
        consumed = 0;
        pendingTagDepth = 0;
    }

    /// <summary>Gets the active conformance options.</summary>
    public CborSerializerOptions Options => options;

    /// <summary>
    /// Gets the number of bytes consumed so far from the source. Saturates
    /// at <see cref="int.MaxValue"/> for sources larger than 2 GiB; callers
    /// who need exact byte counts past that boundary should use
    /// <see cref="GetBytesConsumedLong"/>.
    /// </summary>
    public int BytesConsumed => consumed > int.MaxValue ? int.MaxValue : (int)consumed;

    /// <summary>
    /// Gets the number of bytes consumed so far from the source, as a
    /// 64-bit integer. Use when the source may exceed 2 GiB.
    /// </summary>
    public long BytesConsumedLong => consumed;

    /// <summary>Gets the number of currently-open containers (arrays and maps).</summary>
    public int CurrentDepth => stack.Count;

    /// <summary>
    /// Returns the categorical state of the data item the reader is
    /// positioned at, without consuming any bytes.
    /// </summary>
    public CborReaderState PeekState()
    {
        if(stack.Count > 0)
        {
            ContainerFrame top = stack.Peek();
            if(top.IsDefinite && top.ItemsRead >= top.ExpectedCount)
            {
                return top.Major == CborMajorType.Array ? CborReaderState.EndArray : CborReaderState.EndMap;
            }
            if(!top.IsDefinite && PositionAtBreak())
            {
                return top.Major == CborMajorType.Array ? CborReaderState.EndArray : CborReaderState.EndMap;
            }
        }

        SequenceReader<byte> reader = CreateReader();
        if(!reader.TryPeek(out byte initial))
        {
            if(stack.Count != 0)
            {
                throw new FormatException("CBOR source is exhausted while containers are still open.");
            }
            return CborReaderState.Finished;
        }

        CborMajorType major = (CborMajorType)(initial >> 5);
        byte ai = (byte)(initial & 0x1F);

        switch(major)
        {
            case CborMajorType.UnsignedInteger:
            {
                return CborReaderState.UnsignedInteger;
            }
            case CborMajorType.NegativeInteger:
            {
                return CborReaderState.NegativeInteger;
            }
            case CborMajorType.ByteString:
            {
                return CborReaderState.ByteString;
            }
            case CborMajorType.TextString:
            {
                return CborReaderState.TextString;
            }
            case CborMajorType.Array:
            {
                return CborReaderState.StartArray;
            }
            case CborMajorType.Map:
            {
                return CborReaderState.StartMap;
            }
            case CborMajorType.Tag:
            {
                return CborReaderState.Tag;
            }
            case CborMajorType.SimpleAndFloat:
            {
                return ai switch
                {
                    20 or 21 => CborReaderState.Boolean,
                    22 => CborReaderState.Null,
                    23 => CborReaderState.Undefined,
                    25 => CborReaderState.HalfPrecisionFloat,
                    26 => CborReaderState.SinglePrecisionFloat,
                    27 => CborReaderState.DoublePrecisionFloat,
                    _ => CborReaderState.SimpleValue
                };
            }
            default:
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Unrecognised CBOR major type {(byte)major} at position {consumed}."));
            }
        }
    }

    /// <summary>
    /// Reads a negative integer (major type 1) and returns its raw
    /// encoded argument. The actual mathematical value is
    /// <c>-(1 + result)</c>; the raw form is preserved so callers can
    /// round-trip values below <see cref="long.MinValue"/> that
    /// <see cref="ReadInt64"/> rejects with <see cref="OverflowException"/>.
    /// </summary>
    /// <returns>The raw <c>ulong</c> argument; the numeric value is <c>-(1 + result)</c>.</returns>
    /// <exception cref="FormatException">The next data item is not a negative integer.</exception>
    public ulong ReadCborNegativeIntegerRepresentation()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.NegativeInteger);
        reader.TryRead(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        ulong value = ReadArgument(ref reader, ai);
        SaveReaderState(ref reader);
        OnDataItemCompleted();

        return value;
    }

    /// <summary>Reads an unsigned 64-bit integer (major type 0).</summary>
    public ulong ReadUInt64()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.UnsignedInteger);
        reader.TryRead(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        ulong value = ReadArgument(ref reader, ai);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>
    /// Reads a signed 64-bit integer. Accepts both major type 0 and major
    /// type 1; the latter encodes negative integers as <c>-1 - argument</c>.
    /// </summary>
    /// <exception cref="OverflowException">A negative value is below <see cref="long.MinValue"/>.</exception>
    public long ReadInt64()
    {
        SequenceReader<byte> reader = CreateReader();
        if(!reader.TryPeek(out byte initial))
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        CborMajorType major = (CborMajorType)(initial >> 5);
        if(major != CborMajorType.UnsignedInteger && major != CborMajorType.NegativeInteger)
        {
            ThrowMajorMismatch("integer", major);
        }
        reader.Advance(1);
        byte ai = (byte)(initial & 0x1F);
        ulong argument = ReadArgument(ref reader, ai);
        long value;
        if(major == CborMajorType.UnsignedInteger)
        {
            if(argument > long.MaxValue)
            {
                throw new OverflowException("CBOR unsigned integer exceeds Int64.MaxValue; use ReadUInt64.");
            }
            value = (long)argument;
        }
        else
        {
            if(argument > (ulong)long.MaxValue)
            {
                throw new OverflowException("CBOR negative integer is below Int64.MinValue.");
            }
            value = -1L - (long)argument;
        }
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>
    /// Reads a definite-length byte string and returns its content as a
    /// zero-copy <see cref="ReadOnlySpan{T}"/> slice of the underlying
    /// source memory. The span is valid until the next read call; callers
    /// that need to retain the bytes past that point must copy explicitly
    /// (e.g. <c>span.ToArray()</c>) or use <see cref="ReadByteStringPooled"/>.
    /// Indefinite-length byte strings throw — use
    /// <see cref="ReadByteStringPooled"/> for those. Byte strings that span
    /// multiple sequence segments also throw — use
    /// <see cref="ReadByteStringPooled"/> or <see cref="ReadByteStringMemory"/>
    /// for those.
    /// </summary>
    /// <exception cref="InvalidOperationException">The byte string is indefinite-length or spans segments.</exception>
    public ReadOnlySpan<byte> ReadByteStringSpan()
    {
        SequenceReader<byte> reader = CreateReader();
        int len = ReadDefiniteByteStringHeader(ref reader, out _);

        if(reader.UnreadSpan.Length < len)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture,
                    $"CBOR byte string of length {len} spans multiple sequence segments. Use ReadByteStringPooled or ReadByteStringMemory."));
        }

        ReadOnlySpan<byte> result = reader.UnreadSpan[..len];
        reader.Advance(len);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return result;
    }

    /// <summary>
    /// Reads a definite-length byte string and returns its content as a
    /// <see cref="ReadOnlyMemory{T}"/>. Single-segment data is returned as
    /// a zero-copy slice of the underlying source; cross-segment data is
    /// copied into a fresh <see cref="T:byte[]"/> (allocated; the caller
    /// retains it without explicit disposal). Indefinite-length byte
    /// strings throw — use <see cref="ReadByteStringPooled"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The byte string is indefinite-length.</exception>
    public ReadOnlyMemory<byte> ReadByteStringMemory()
    {
        SequenceReader<byte> reader = CreateReader();
        int len = ReadDefiniteByteStringHeader(ref reader, out long headerConsumed);

        if(reader.UnreadSpan.Length >= len)
        {
            //Single-segment zero-copy. Recover the memory slice for this
            //segment by walking the source sequence to the current position.
            ReadOnlyMemory<byte> slice = SliceCurrentSegmentMemory(headerConsumed, len);
            reader.Advance(len);
            SaveReaderState(ref reader);
            OnDataItemCompleted();
            return slice;
        }

        //Cross-segment: allocate and copy. The caller retains the
        //ReadOnlyMemory<byte> independent of the pool; GC handles cleanup.
        byte[] buffer = new byte[len];
        if(!reader.TryCopyTo(buffer))
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture,
                    $"CBOR byte-string length {len} exceeds remaining source bytes."));
        }
        reader.Advance(len);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return buffer;
    }

    private int ReadDefiniteByteStringHeader(ref SequenceReader<byte> reader, out long headerEndOffset)
    {
        ExpectMajor(ref reader, CborMajorType.ByteString);
        reader.TryPeek(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            throw new InvalidOperationException(
                "CBOR byte string is indefinite-length and cannot be returned as a zero-copy slice. Use ReadByteStringPooled.");
        }
        reader.Advance(1);
        ulong length = ReadArgument(ref reader, ai);
        if(length > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(length));
        }
        int len = (int)length;
        if(len > options.MaxByteStringLength)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxByteStringLength), len, options.MaxByteStringLength);
        }
        if(len > reader.Remaining)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR byte-string length {len} exceeds remaining source bytes {reader.Remaining}."));
        }
        //SequenceReader.Consumed is absolute from the start of the sequence
        //the reader was created over (which is the full source); no addition
        //of the saved 'consumed' field needed here.
        headerEndOffset = reader.Consumed;
        return len;
    }

    /// <summary>
    /// Reads a byte string and returns its content as an owned pool-rented
    /// buffer. Works for both definite- and indefinite-length byte strings;
    /// for indefinite-length, chunks are assembled into a single rented slab
    /// without per-chunk intermediate allocations. The caller is responsible
    /// for disposing the returned <see cref="IMemoryOwner{T}"/>.
    /// </summary>
    public IMemoryOwner<byte> ReadByteStringPooled()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.ByteString);
        reader.TryPeek(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            reader.Advance(1);
            return ReadIndefiniteByteStringChunksPooled(ref reader);
        }
        reader.Advance(1);
        ulong length = ReadArgument(ref reader, ai);
        if(length > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(length));
        }
        int len = (int)length;
        if(len > options.MaxByteStringLength)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxByteStringLength), len, options.MaxByteStringLength);
        }
        if(len > reader.Remaining)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR byte-string length {len} exceeds remaining source bytes {reader.Remaining}."));
        }
        IMemoryOwner<byte> owner = RentExact(len);
        if(!reader.TryCopyTo(owner.Memory.Span))
        {
            owner.Dispose();
            throw new FormatException("CBOR byte-string read failed: source exhausted.");
        }
        reader.Advance(len);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return owner;
    }

    /// <summary>
    /// Reads a byte string and returns its content as a freshly allocated
    /// array. This is a convenience wrapper over
    /// <see cref="ReadByteStringMemory"/> (definite) or
    /// <see cref="ReadByteStringPooled"/> (indefinite). Use the span /
    /// memory / pooled methods directly when allocation matters.
    /// </summary>
    public byte[] ReadByteString()
    {
        SequenceReader<byte> peekReader = CreateReader();
        if(!peekReader.TryPeek(out byte initial))
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        byte ai = (byte)(initial & 0x1F);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            using IMemoryOwner<byte> owner = ReadByteStringPooled();
            return owner.Memory.ToArray();
        }
        return ReadByteStringMemory().ToArray();
    }

    private IMemoryOwner<byte> RentExact(int size)
    {
        if(size == 0)
        {
            return new EmptyMemoryOwner();
        }

        //An injected pool may be any MemoryPool<byte> — a consumer's own pool, not only VeritasMemoryPool — so it
        //can return a buffer larger than requested; trim oversized rentals to an exact-length view so callers never
        //observe trailing slack. A pool that already rents exact-size (VeritasMemoryPool) hits the fast path with
        //no wrapper allocated, since the length already matches.
        MemoryPool<byte> rentPool = pool ?? MemoryPool<byte>.Shared;
        IMemoryOwner<byte> owner = rentPool.Rent(size);
        return owner.Memory.Length == size ? owner : new TrimmedMemoryOwner(owner, size);
    }

    private IMemoryOwner<byte> ReadIndefiniteByteStringChunksPooled(ref SequenceReader<byte> reader)
    {
        //Two-pass approach: first pass walks chunk positions to compute
        //the assembled length; second pass copies into a single rented slab.
        //Reader state after this method points past the closing break byte.
        long startConsumed = reader.Consumed;
        int totalLength = 0;
        int chunkCount = 0;
        while(reader.TryPeek(out byte chunkInitial) && chunkInitial != CborHeader.BreakStop)
        {
            if((CborMajorType)(chunkInitial >> 5) != CborMajorType.ByteString)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Indefinite byte string chunk is not a byte string (major type {chunkInitial >> 5})."));
            }
            byte chunkAi = (byte)(chunkInitial & 0x1F);
            if(chunkAi == CborHeader.AdditionalInfoIndefinite)
            {
                throw new FormatException("Indefinite byte string chunks must each be definite length; nested indefinite strings are forbidden by RFC 8949 §3.2.3.");
            }
            reader.Advance(1);
            ulong chunkLength = ReadArgument(ref reader, chunkAi);
            if(chunkLength > int.MaxValue)
            {
                CborThrowHelper.ThrowLengthExceedsInt32(nameof(chunkLength));
            }
            int len = (int)chunkLength;
            if(len > reader.Remaining)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"CBOR indefinite chunk length {len} exceeds remaining source bytes {reader.Remaining}."));
            }
            reader.Advance(len);
            totalLength += len;
            chunkCount++;
            if(chunkCount > options.MaxIndefiniteStringChunks)
            {
                throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxIndefiniteStringChunks), chunkCount, options.MaxIndefiniteStringChunks);
            }
        }
        if(reader.End)
        {
            throw new FormatException("Indefinite-length byte string is missing its break byte.");
        }

        //Rewind for the second pass.
        long endConsumed = reader.Consumed;
        long rewindBy = endConsumed - startConsumed;
        reader.Rewind(rewindBy);

        IMemoryOwner<byte> owner = RentExact(totalLength);
        Span<byte> dest = owner.Memory.Span;
        int destOffset = 0;
        while(reader.TryPeek(out byte chunkInitial) && chunkInitial != CborHeader.BreakStop)
        {
            byte chunkAi = (byte)(chunkInitial & 0x1F);
            reader.Advance(1);
            ulong chunkLength = ReadArgument(ref reader, chunkAi);
            int len = (int)chunkLength;
            if(!reader.TryCopyTo(dest.Slice(destOffset, len)))
            {
                owner.Dispose();
                throw new FormatException("CBOR indefinite-chunk copy failed.");
            }
            reader.Advance(len);
            destOffset += len;
        }
        reader.Advance(1);   //consume break byte
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return owner;
    }

    /// <summary>
    /// Reads a text string and returns its decoded value. Definite-length
    /// text strings decode directly. Indefinite-length text strings are
    /// consumed chunk-by-chunk and concatenated as bytes before UTF-8
    /// decoding; the break byte terminates the data item. UTF-8 is
    /// validated when configured by the conformance options.
    /// </summary>
    public string ReadTextString()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.TextString);
        reader.TryPeek(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            reader.Advance(1);
            return ReadIndefiniteTextStringChunks(ref reader);
        }
        reader.Advance(1);
        ulong length = ReadArgument(ref reader, ai);
        if(length > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(length));
        }
        int len = (int)length;
        if(len > options.MaxTextStringLength)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxTextStringLength), len, options.MaxTextStringLength);
        }
        if(len > reader.Remaining)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR text-string length {len} exceeds remaining source bytes {reader.Remaining}."));
        }
        string value;
        if(reader.UnreadSpan.Length >= len)
        {
            //Single-segment fast path: decode directly from the segment.
            ReadOnlySpan<byte> bytes = reader.UnreadSpan[..len];
            value = stringInternPool is not null
                ? InternOrDecode(bytes)
                : DecodeUtf8(bytes);
        }
        else
        {
            //Cross-segment: assemble into a small stack/heap buffer first.
            byte[] assembled = new byte[len];
            reader.TryCopyTo(assembled);
            value = stringInternPool is not null
                ? InternOrDecode(assembled)
                : DecodeUtf8(assembled);
        }
        reader.Advance(len);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    private string InternOrDecode(ReadOnlySpan<byte> bytes)
    {
        //Length-based bypass: strings the pool will never store (post
        //text, URIs, base64 payloads) skip the hash + dictionary lookup
        //entirely. Without this, long unique content adds a hash cost
        //per read that the pool can't amortise.
        if(bytes.Length > stringInternPool!.MaxByteLength)
        {
            return DecodeUtf8(bytes);
        }

        //Fast path: lookup. The alternate lookup avoids any allocation
        //when the string is already cached. This is the steady-state
        //path for repeated map keys / repeated content addresses.
        string? hit = stringInternPool.TryGet(bytes);
        if(hit is not null)
        {
            return hit;
        }

        //First sighting: decode (validating if configured) once, then
        //insert the result. The pool quietly rejects when the total-
        //entry cap is reached.
        string decoded = DecodeUtf8(bytes);
        _ = stringInternPool.AddDecoded(bytes, decoded);

        return decoded;
    }

    private string ReadIndefiniteTextStringChunks(ref SequenceReader<byte> reader)
    {
        //Reuse the byte-string assembly machinery: collect all chunk bytes
        //into a pool-rented buffer, then UTF-8 decode the assembled bytes.
        long startConsumed = reader.Consumed;
        int totalLength = 0;
        int chunkCount = 0;
        while(reader.TryPeek(out byte chunkInitial) && chunkInitial != CborHeader.BreakStop)
        {
            if((CborMajorType)(chunkInitial >> 5) != CborMajorType.TextString)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Indefinite text string chunk is not a text string (major type {chunkInitial >> 5})."));
            }
            byte chunkAi = (byte)(chunkInitial & 0x1F);
            if(chunkAi == CborHeader.AdditionalInfoIndefinite)
            {
                throw new FormatException("Indefinite text string chunks must each be definite length; nested indefinite strings are forbidden by RFC 8949 §3.2.3.");
            }
            reader.Advance(1);
            ulong chunkLength = ReadArgument(ref reader, chunkAi);
            if(chunkLength > int.MaxValue)
            {
                CborThrowHelper.ThrowLengthExceedsInt32(nameof(chunkLength));
            }
            int len = (int)chunkLength;
            if(len > reader.Remaining)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"CBOR indefinite chunk length {len} exceeds remaining source bytes {reader.Remaining}."));
            }
            reader.Advance(len);
            totalLength += len;
            chunkCount++;
            if(chunkCount > options.MaxIndefiniteStringChunks)
            {
                throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxIndefiniteStringChunks), chunkCount, options.MaxIndefiniteStringChunks);
            }
        }
        if(reader.End)
        {
            throw new FormatException("Indefinite-length text string is missing its break byte.");
        }

        long endConsumed = reader.Consumed;
        reader.Rewind(endConsumed - startConsumed);

        byte[] assembled = new byte[totalLength];
        int offset = 0;
        while(reader.TryPeek(out byte chunkInitial) && chunkInitial != CborHeader.BreakStop)
        {
            byte chunkAi = (byte)(chunkInitial & 0x1F);
            reader.Advance(1);
            ulong chunkLength = ReadArgument(ref reader, chunkAi);
            int len = (int)chunkLength;
            reader.TryCopyTo(assembled.AsSpan(offset, len));
            reader.Advance(len);
            offset += len;
        }
        reader.Advance(1);   //break byte
        string value = stringInternPool is not null
            ? InternOrDecode(assembled)
            : DecodeUtf8(assembled);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    private string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        if(options.ValidateUtf8)
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch(DecoderFallbackException)
            {
                CborThrowHelper.ThrowInvalidUtf8();
                throw;
            }
        }
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Reads an array introducer and returns the definite item count, or
    /// <c>null</c> for an indefinite-length array.
    /// </summary>
    public int? ReadStartArray()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.Array);
        reader.TryPeek(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        reader.Advance(1);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            EnsureDepthAvailable();
            stack.Push(new ContainerFrame(CborMajorType.Array, isDefinite: false, expectedCount: -1));
            SaveReaderState(ref reader);
            return null;
        }
        ulong count = ReadArgument(ref reader, ai);
        if(count > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(count));
        }
        int countInt = (int)count;
        if(countInt > options.MaxArrayLength)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxArrayLength), countInt, options.MaxArrayLength);
        }
        EnsureDepthAvailable();
        stack.Push(new ContainerFrame(CborMajorType.Array, isDefinite: true, expectedCount: countInt));
        SaveReaderState(ref reader);
        return countInt;
    }

    /// <summary>
    /// Closes the topmost array. For indefinite-length arrays this consumes
    /// the break byte; for definite-length arrays it validates that the
    /// declared item count has been read.
    /// </summary>
    public void ReadEndArray()
    {
        if(stack.Count == 0 || stack.Peek().Major != CborMajorType.Array)
        {
            CborThrowHelper.ThrowInvalidState("ReadEndArray", DescribeTopState());
        }
        ContainerFrame frame = stack.Pop();
        if(frame.IsDefinite)
        {
            if(frame.ItemsRead != frame.ExpectedCount)
            {
                CborThrowHelper.ThrowContainerLengthMismatch("array", frame.ExpectedCount, frame.ItemsRead);
            }
        }
        else
        {
            ConsumeBreakByte();
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Reads a map introducer and returns the definite key/value pair
    /// count, or <c>null</c> for an indefinite-length map.
    /// </summary>
    public int? ReadStartMap()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.Map);
        reader.TryPeek(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        reader.Advance(1);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            EnsureDepthAvailable();
            stack.Push(new ContainerFrame(CborMajorType.Map, isDefinite: false, expectedCount: -1));
            SaveReaderState(ref reader);
            return null;
        }
        ulong count = ReadArgument(ref reader, ai);
        if(count > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(count));
        }
        int countInt = (int)count;
        if(countInt > options.MaxMapEntryCount)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxMapEntryCount), countInt, options.MaxMapEntryCount);
        }
        EnsureDepthAvailable();
        stack.Push(new ContainerFrame(CborMajorType.Map, isDefinite: true, expectedCount: countInt));
        SaveReaderState(ref reader);
        return countInt;
    }

    /// <summary>
    /// Closes the topmost map. For indefinite-length maps this consumes
    /// the break byte; for definite-length maps it validates the
    /// declared pair count.
    /// </summary>
    public void ReadEndMap()
    {
        if(stack.Count == 0 || stack.Peek().Major != CborMajorType.Map)
        {
            CborThrowHelper.ThrowInvalidState("ReadEndMap", DescribeTopState());
        }
        ContainerFrame frame = stack.Pop();
        if(frame.ExpectingMapValue)
        {
            throw new InvalidOperationException("CBOR map closed with a key consumed but no corresponding value.");
        }
        if(frame.IsDefinite)
        {
            if(frame.ItemsRead != frame.ExpectedCount)
            {
                CborThrowHelper.ThrowContainerLengthMismatch("map", frame.ExpectedCount, frame.ItemsRead);
            }
        }
        else
        {
            ConsumeBreakByte();
        }
        OnDataItemCompleted();
    }

    /// <summary>Reads a Boolean simple value.</summary>
    public bool ReadBoolean()
    {
        SequenceReader<byte> reader = CreateReader();
        if(!reader.TryPeek(out byte initial))
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        if((CborMajorType)(initial >> 5) != CborMajorType.SimpleAndFloat)
        {
            ThrowMajorMismatch("boolean", (CborMajorType)(initial >> 5));
        }
        byte ai = (byte)(initial & 0x1F);
        if(ai != 20 && ai != 21)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR boolean (simple value 20 or 21) at position {consumed}; got {ai}."));
        }
        reader.Advance(1);
        bool value = ai == 21;
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads the CBOR null simple value.</summary>
    public void ReadNull()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectInitialByte(ref reader, (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | (byte)CborSimpleValue.Null), "null");
        reader.Advance(1);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
    }

    /// <summary>Reads the CBOR undefined simple value.</summary>
    public void ReadUndefined()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectInitialByte(ref reader, (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | (byte)CborSimpleValue.Undefined), "undefined");
        reader.Advance(1);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
    }

    /// <summary>Reads a non-standardised simple value identifier.</summary>
    public byte ReadSimpleValue()
    {
        SequenceReader<byte> reader = CreateReader();
        if(!reader.TryPeek(out byte initial))
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        if((CborMajorType)(initial >> 5) != CborMajorType.SimpleAndFloat)
        {
            ThrowMajorMismatch("simple value", (CborMajorType)(initial >> 5));
        }
        byte ai = (byte)(initial & 0x1F);
        reader.Advance(1);
        byte value;
        if(ai <= CborHeader.ImmediateMax)
        {
            value = ai;
        }
        else if(ai == CborHeader.AdditionalInfoOneByte)
        {
            if(!reader.TryRead(out value))
            {
                throw new FormatException("CBOR source exhausted reading simple value byte.");
            }
        }
        else
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected a CBOR simple value at position {consumed}; got additional info {ai}."));
        }
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads a CBOR tag. The next data item is the tagged content.</summary>
    public CborTag ReadTag()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectMajor(ref reader, CborMajorType.Tag);
        reader.TryPeek(out byte initial);
        byte ai = (byte)(initial & 0x1F);
        reader.Advance(1);
        ulong tag = ReadArgument(ref reader, ai);
        pendingTagDepth++;
        if(pendingTagDepth > options.MaxTagDepth)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxTagDepth), pendingTagDepth, options.MaxTagDepth);
        }
        SaveReaderState(ref reader);
        return new CborTag(tag);
    }

    /// <summary>Reads a half-precision (binary16) floating-point value.</summary>
    public Half ReadHalf()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectInitialByte(ref reader, (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoTwoByte), "half-precision float");
        reader.Advance(1);
        if(!reader.TryReadBigEndian(out short signedBits))
        {
            throw new FormatException("CBOR source exhausted reading half-precision float bits.");
        }
        Half value = BitConverter.UInt16BitsToHalf((ushort)signedBits);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads a single-precision (binary32) floating-point value.</summary>
    public float ReadSingle()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectInitialByte(ref reader, (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoFourByte), "single-precision float");
        reader.Advance(1);
        Span<byte> bytes = stackalloc byte[4];
        if(!reader.TryCopyTo(bytes))
        {
            throw new FormatException("CBOR source exhausted reading single-precision float bits.");
        }
        reader.Advance(4);
        float value = CborFloatConversions.ReadBinary32(bytes);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads a double-precision (binary64) floating-point value.</summary>
    public double ReadDouble()
    {
        SequenceReader<byte> reader = CreateReader();
        ExpectInitialByte(ref reader, (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoEightByte), "double-precision float");
        reader.Advance(1);
        Span<byte> bytes = stackalloc byte[8];
        if(!reader.TryCopyTo(bytes))
        {
            throw new FormatException("CBOR source exhausted reading double-precision float bits.");
        }
        reader.Advance(8);
        double value = CborFloatConversions.ReadBinary64(bytes);
        SaveReaderState(ref reader);
        OnDataItemCompleted();
        return value;
    }

    private static UTF8Encoding StrictUtf8 { get; } = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private SequenceReader<byte> CreateReader()
    {
        SequenceReader<byte> reader = new(source);
        if(consumed > 0)
        {
            reader.Advance(consumed);
        }
        return reader;
    }

    private void SaveReaderState(ref SequenceReader<byte> reader)
    {
        consumed = reader.Consumed;
    }

    private static ulong ReadArgument(ref SequenceReader<byte> reader, byte additionalInfo)
    {
        if(additionalInfo <= CborHeader.ImmediateMax)
        {
            return additionalInfo;
        }
        switch(additionalInfo)
        {
            case CborHeader.AdditionalInfoOneByte:
            {
                if(!reader.TryRead(out byte b))
                {
                    throw new FormatException("CBOR source exhausted reading one-byte argument.");
                }
                return b;
            }
            case CborHeader.AdditionalInfoTwoByte:
            {
                if(!reader.TryReadBigEndian(out short signed))
                {
                    throw new FormatException("CBOR source exhausted reading two-byte argument.");
                }
                return (ushort)signed;
            }
            case CborHeader.AdditionalInfoFourByte:
            {
                if(!reader.TryReadBigEndian(out int signed))
                {
                    throw new FormatException("CBOR source exhausted reading four-byte argument.");
                }
                return (uint)signed;
            }
            case CborHeader.AdditionalInfoEightByte:
            {
                if(!reader.TryReadBigEndian(out long signed))
                {
                    throw new FormatException("CBOR source exhausted reading eight-byte argument.");
                }
                return (ulong)signed;
            }
            default:
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Invalid CBOR additional information value {additionalInfo}."));
            }
        }
    }

    private void ExpectMajor(ref SequenceReader<byte> reader, CborMajorType expected)
    {
        if(!reader.TryPeek(out byte initial))
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        CborMajorType actual = (CborMajorType)(initial >> 5);
        if(actual != expected)
        {
            ThrowMajorMismatch(expected.ToString(), actual);
        }
    }

    private void ExpectInitialByte(ref SequenceReader<byte> reader, byte expected, string label)
    {
        if(!reader.TryPeek(out byte actual))
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        if(actual != expected)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR {label} initial byte 0x{expected:X2} at position {consumed}; got 0x{actual:X2}."));
        }
    }

    private void ThrowMajorMismatch(string expected, CborMajorType actual)
    {
        throw new FormatException(
            string.Create(CultureInfo.InvariantCulture, $"Expected CBOR {expected} at position {consumed}; got major type {(byte)actual}."));
    }

    private bool PositionAtBreak()
    {
        SequenceReader<byte> reader = CreateReader();
        return reader.TryPeek(out byte b) && b == CborHeader.BreakStop;
    }

    private void ConsumeBreakByte()
    {
        SequenceReader<byte> reader = CreateReader();
        if(!reader.TryPeek(out byte b) || b != CborHeader.BreakStop)
        {
            throw new FormatException("CBOR indefinite container missing break byte.");
        }
        reader.Advance(1);
        SaveReaderState(ref reader);
    }

    private void EnsureIndefiniteAllowed()
    {
        if(!options.AllowIndefiniteLength)
        {
            CborThrowHelper.ThrowFeatureDisabledByConformanceMode("indefinite-length", options.ConformanceMode);
        }
    }

    private void EnsureDepthAvailable()
    {
        if(stack.Count >= options.MaxDepth)
        {
            throw new CborSizeLimitExceededException(nameof(CborSerializerOptions.MaxDepth), stack.Count + 1, options.MaxDepth);
        }
    }

    private void OnDataItemCompleted()
    {
        //All pending tags collapse onto the data item that just completed,
        //matching the writer's bookkeeping: a tag plus its content is one
        //logical item, and the parent container is bumped exactly once.
        pendingTagDepth = 0;
        if(stack.Count == 0)
        {
            return;
        }
        ContainerFrame frame = stack.Peek();
        if(frame.Major == CborMajorType.Map)
        {
            if(frame.ExpectingMapValue)
            {
                frame.ItemsRead++;
                frame.ExpectingMapValue = false;
            }
            else
            {
                frame.ExpectingMapValue = true;
            }
        }
        else
        {
            frame.ItemsRead++;
        }
    }

    private string DescribeTopState()
    {
        if(stack.Count == 0)
        {
            return "TopLevel";
        }
        ContainerFrame top = stack.Peek();
        return top.Major == CborMajorType.Array ? "Array" : "Map";
    }

    private ReadOnlyMemory<byte> SliceCurrentSegmentMemory(long startOffset, int length)
    {
        //Walk the source sequence to find the SequencePosition for
        //startOffset, then slice length bytes. This is the closest
        //zero-copy memory recovery available on ReadOnlySequence<byte>;
        //the caller has already verified the data fits in one segment
        //via SequenceReader.UnreadSpan length.
        ReadOnlySequence<byte> sliced = source.Slice(startOffset, length);
        if(sliced.IsSingleSegment)
        {
            return sliced.First;
        }
        //Fallback: should not happen in callers (they pre-check), but
        //a copy is safe.
        return sliced.ToArray();
    }

    private sealed class ContainerFrame
    {
        public ContainerFrame(CborMajorType major, bool isDefinite, int expectedCount)
        {
            Major = major;
            IsDefinite = isDefinite;
            ExpectedCount = expectedCount;
        }

        public CborMajorType Major { get; }

        public bool IsDefinite { get; }

        public int ExpectedCount { get; }

        public int ItemsRead { get; set; }

        public bool ExpectingMapValue { get; set; }
    }

    private sealed class TrimmedMemoryOwner: IMemoryOwner<byte>
    {
        private readonly IMemoryOwner<byte> inner;
        private readonly int length;

        public TrimmedMemoryOwner(IMemoryOwner<byte> inner, int length)
        {
            this.inner = inner;
            this.length = length;
        }

        public Memory<byte> Memory => inner.Memory[..length];

        public void Dispose() => inner.Dispose();
    }

    private sealed class EmptyMemoryOwner: IMemoryOwner<byte>
    {
        public Memory<byte> Memory => Memory<byte>.Empty;

        public void Dispose()
        {
        }
    }
}
