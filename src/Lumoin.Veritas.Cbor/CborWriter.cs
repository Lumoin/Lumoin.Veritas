using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Cbor.Internal;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Writes CBOR data items into an <see cref="IBufferWriter{T}"/>. The writer
/// owns no buffer; it streams through the destination and tracks open
/// arrays, maps, pending tags, and (under canonical conformance modes)
/// per-map key/value buffers so that container-length, tag-content
/// pairing, and map-key ordering rules are enforced as bytes are emitted.
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine.</b> The writer is a small state machine over a stack of
/// open containers. Each <c>Write*</c> call emits the data item's
/// fixed-shape header through <see cref="CborHeader"/> and any payload it
/// needs, then advances the bookkeeping that tells the writer what may be
/// emitted next. Open containers are tracked so <see cref="WriteEndArray"/>
/// and <see cref="WriteEndMap"/> can validate that the declared item count
/// has been satisfied for definite-length containers, or emit the break
/// byte for indefinite-length containers.
/// </para>
/// <para>
/// <b>Tag pairing.</b> A tag does not constitute a complete data item by
/// itself: per RFC 8949 §3.4 it tags the next data item that follows.
/// The writer carries a pending-tag count that collapses onto the next
/// completed data item; the surrounding container is bumped exactly once
/// for the (possibly tagged, possibly multiply tagged) item.
/// </para>
/// <para>
/// <b>Map sorting under canonical modes.</b> The deterministic conformance
/// modes (<see cref="CborConformanceMode.RfcCanonical"/>,
/// <see cref="CborConformanceMode.Ctap2Canonical"/>,
/// <see cref="CborConformanceMode.Cde"/>) require map keys to be emitted
/// in sorted order. The writer enforces this by buffering each map's
/// key/value pairs into per-frame byte arrays during writing and emitting
/// them in sorted order on <see cref="WriteEndMap"/>. RFC canonical and
/// CTAP2 use length-first then bytewise comparison; CDE uses bytewise
/// comparison only.
/// </para>
/// <para>
/// <b>Length minimisation.</b> The integer-length encoding chosen for any
/// argument is always the shortest fitting form (see
/// <see cref="CborHeader.LengthFor"/>), so the writer's basic output
/// already satisfies the length-minimisation rule shared by all
/// deterministic modes. Indefinite-length items are rejected by the
/// deterministic modes via <see cref="CborSerializerOptions.AllowIndefiniteLength"/>.
/// </para>
/// </remarks>
public sealed class CborWriter
{
    private readonly IBufferWriter<byte> originalDestination;
    private readonly Stack<ContainerFrame> stack = new();
    private CborSerializerOptions options;
    private int pendingTagDepth;
    private int bytesWritten;
    private int sortFrameCount;

    /// <summary>
    /// Initialises a new <see cref="CborWriter"/> writing into
    /// <paramref name="destination"/> under <paramref name="options"/>.
    /// </summary>
    /// <param name="destination">The destination buffer writer. Must not be <c>null</c>.</param>
    /// <param name="options">The conformance options. Must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public CborWriter(IBufferWriter<byte> destination, CborSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);

        this.originalDestination = destination;
        this.options = options;
    }

    /// <summary>
    /// Gets the total number of bytes emitted to the original destination
    /// since construction or the most recent <see cref="Reset"/>. Bytes
    /// captured into per-map sort buffers are counted at the moment they
    /// are flushed to the destination on <see cref="WriteEndMap"/>.
    /// </summary>
    public int BytesWritten => bytesWritten;

    /// <summary>
    /// Gets the active conformance options. The reference is held by the
    /// writer; callers must not mutate it concurrently with writer
    /// activity.
    /// </summary>
    public CborSerializerOptions Options => options;

    /// <summary>
    /// Resets the writer's internal state so it can be reused. The
    /// underlying destination is not affected; the caller is responsible
    /// for advancing or detaching the destination as appropriate.
    /// </summary>
    public void Reset()
    {
        stack.Clear();
        pendingTagDepth = 0;
        bytesWritten = 0;
        sortFrameCount = 0;
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer as a CBOR major-type 0 data item.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt64(ulong value)
    {
        WriteHeader(CborMajorType.UnsignedInteger, value);
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a signed 64-bit integer. Non-negative values are emitted as
    /// major type 0; negative values are emitted as major type 1 with the
    /// argument set to <c>-1 - value</c>.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt64(long value)
    {
        if(value >= 0)
        {
            WriteHeader(CborMajorType.UnsignedInteger, (ulong)value);
        }
        else
        {
            WriteHeader(CborMajorType.NegativeInteger, (ulong)(-(value + 1)));
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a signed 32-bit integer. Convenience overload of
    /// <see cref="WriteInt64(long)"/>.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt32(int value) => WriteInt64(value);

    /// <summary>
    /// Writes a negative integer using its raw CBOR major-type 1
    /// representation argument. The actual mathematical value emitted is
    /// <c>-(1 + rawArgument)</c> per RFC 8949 §3.1, so the full CBOR
    /// negative range (<c>-1</c> down to <c>-(2^64)</c>) is reachable —
    /// including values below <see cref="long.MinValue"/> that
    /// <see cref="WriteInt64"/> cannot express.
    /// </summary>
    /// <param name="rawArgument">The encoded argument; the emitted
    /// numeric value is <c>-(1 + rawArgument)</c>.</param>
    public void WriteCborNegativeIntegerRepresentation(ulong rawArgument)
    {
        WriteHeader(CborMajorType.NegativeInteger, rawArgument);
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a byte string as a CBOR major-type 2 data item. When called
    /// while an indefinite-length byte string frame is open (after
    /// <see cref="WriteStartIndefiniteByteString"/>) the bytes become the
    /// next chunk; otherwise they become a standalone definite-length
    /// byte string.
    /// </summary>
    /// <param name="value">The bytes to write.</param>
    public void WriteByteString(ReadOnlySpan<byte> value)
    {
        WriteHeader(CborMajorType.ByteString, (ulong)value.Length);
        EmitRawBytes(value);
        //Chunks inside an indefinite byte-string frame do not bump anything;
        //the containing frame's break terminates the data item.
        if(InIndefiniteStringContainer(CborMajorType.ByteString))
        {
            return;
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes the introducer for an indefinite-length byte string. Each
    /// subsequent <see cref="WriteByteString(ReadOnlySpan{byte})"/> call
    /// emits a chunk; close with
    /// <see cref="WriteEndIndefiniteByteString"/>. Forbidden under the
    /// deterministic conformance modes.
    /// </summary>
    public void WriteStartIndefiniteByteString()
    {
        EnsureIndefiniteAllowed();
        WriteIndefiniteIntroducer(CborMajorType.ByteString);
        stack.Push(new ContainerFrame(CborMajorType.ByteString, isDefinite: false, expectedCount: -1));
    }

    /// <summary>
    /// Closes the topmost indefinite-length byte string frame: emits the
    /// break byte and counts the assembled data item against the parent
    /// container.
    /// </summary>
    public void WriteEndIndefiniteByteString()
    {
        if(stack.Count == 0 || stack.Peek().Major != CborMajorType.ByteString || stack.Peek().IsDefinite)
        {
            CborThrowHelper.ThrowInvalidState("WriteEndIndefiniteByteString", DescribeTopState());
        }
        stack.Pop();
        WriteBreakStop();
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a UTF-8 text string as a CBOR major-type 3 data item.
    /// </summary>
    /// <param name="value">The string to encode and write. Must not be <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public void WriteTextString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteTextString(value.AsSpan());
    }

    /// <summary>
    /// Writes a UTF-8 text string as a CBOR major-type 3 data item. When
    /// called while an indefinite-length text string frame is open (after
    /// <see cref="WriteStartIndefiniteTextString"/>) the bytes become the
    /// next chunk; otherwise they become a standalone definite-length
    /// text string.
    /// </summary>
    /// <param name="value">The characters to encode and write.</param>
    public void WriteTextString(ReadOnlySpan<char> value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteHeader(CborMajorType.TextString, (ulong)byteCount);
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value, span);
        sink.Advance(written);
        OnBytesEmitted(written);
        if(InIndefiniteStringContainer(CborMajorType.TextString))
        {
            return;
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes the introducer for an indefinite-length text string. Each
    /// subsequent <see cref="WriteTextString(string)"/> or
    /// <see cref="WriteTextString(ReadOnlySpan{char})"/> call emits a
    /// chunk; close with <see cref="WriteEndIndefiniteTextString"/>.
    /// Forbidden under the deterministic conformance modes.
    /// </summary>
    public void WriteStartIndefiniteTextString()
    {
        EnsureIndefiniteAllowed();
        WriteIndefiniteIntroducer(CborMajorType.TextString);
        stack.Push(new ContainerFrame(CborMajorType.TextString, isDefinite: false, expectedCount: -1));
    }

    /// <summary>
    /// Closes the topmost indefinite-length text string frame: emits the
    /// break byte and counts the assembled data item against the parent
    /// container.
    /// </summary>
    public void WriteEndIndefiniteTextString()
    {
        if(stack.Count == 0 || stack.Peek().Major != CborMajorType.TextString || stack.Peek().IsDefinite)
        {
            CborThrowHelper.ThrowInvalidState("WriteEndIndefiniteTextString", DescribeTopState());
        }
        stack.Pop();
        WriteBreakStop();
        OnDataItemCompleted();
    }

    private bool InIndefiniteStringContainer(CborMajorType major)
    {
        if(stack.Count == 0)
        {
            return false;
        }
        ContainerFrame top = stack.Peek();
        return top.Major == major && !top.IsDefinite;
    }

    /// <summary>
    /// Writes an array introducer (major type 4). When
    /// <paramref name="definiteLength"/> is supplied, the array carries an
    /// exact item count that <see cref="WriteEndArray"/> validates.
    /// Indefinite arrays must be closed with <see cref="WriteEndArray"/>
    /// after all items have been written; the writer emits the break byte
    /// at that point.
    /// </summary>
    /// <param name="definiteLength">The exact item count, or <c>null</c> for indefinite-length.</param>
    public void WriteStartArray(int? definiteLength = null)
    {
        if(definiteLength is null)
        {
            EnsureIndefiniteAllowed();
            WriteIndefiniteIntroducer(CborMajorType.Array);
            stack.Push(new ContainerFrame(CborMajorType.Array, isDefinite: false, expectedCount: -1));
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(definiteLength.Value);
        WriteHeader(CborMajorType.Array, (ulong)definiteLength.Value);
        stack.Push(new ContainerFrame(CborMajorType.Array, isDefinite: true, expectedCount: definiteLength.Value));
    }

    /// <summary>
    /// Closes the topmost array opened by <see cref="WriteStartArray"/>.
    /// For definite arrays, validates that the declared item count has
    /// been written. For indefinite arrays, emits the break byte.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current top of stack is not an array, or the item count is short.</exception>
    public void WriteEndArray()
    {
        if(stack.Count == 0 || stack.Peek().Major != CborMajorType.Array)
        {
            CborThrowHelper.ThrowInvalidState("WriteEndArray", DescribeTopState());
        }

        ContainerFrame frame = stack.Pop();
        if(frame.IsDefinite)
        {
            if(frame.ItemsWritten != frame.ExpectedCount)
            {
                CborThrowHelper.ThrowContainerLengthMismatch("array", frame.ExpectedCount, frame.ItemsWritten);
            }
        }
        else
        {
            WriteBreakStop();
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a map introducer (major type 5). When
    /// <paramref name="definiteLength"/> is supplied, the map carries an
    /// exact key/value pair count that <see cref="WriteEndMap"/> validates.
    /// Indefinite maps must be closed with <see cref="WriteEndMap"/> after
    /// all key/value pairs have been written. Under canonical conformance
    /// modes the writer buffers each pair internally and emits the map's
    /// header plus sorted pairs on <see cref="WriteEndMap"/>.
    /// </summary>
    /// <param name="definiteLength">The exact key/value pair count, or <c>null</c> for indefinite-length.</param>
    public void WriteStartMap(int? definiteLength = null)
    {
        if(definiteLength is null)
        {
            EnsureIndefiniteAllowed();
            WriteIndefiniteIntroducer(CborMajorType.Map);
            stack.Push(new ContainerFrame(CborMajorType.Map, isDefinite: false, expectedCount: -1));
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(definiteLength.Value);

        if(IsSortRequired)
        {
            //Defer the header until WriteEndMap; key/value pairs are buffered first.
            ContainerFrame sortFrame = new(CborMajorType.Map, isDefinite: true, expectedCount: definiteLength.Value)
            {
                SortBuffer = new ArrayBufferWriter<byte>(),
                SortEntries = []
            };
            stack.Push(sortFrame);
            sortFrameCount++;
            return;
        }

        WriteHeader(CborMajorType.Map, (ulong)definiteLength.Value);
        stack.Push(new ContainerFrame(CborMajorType.Map, isDefinite: true, expectedCount: definiteLength.Value));
    }

    /// <summary>
    /// Closes the topmost map opened by <see cref="WriteStartMap"/>. Under
    /// canonical conformance modes the buffered pairs are sorted by their
    /// encoded keys and the map header plus sorted pairs are emitted to
    /// the parent sink. For non-canonical maps the existing pair stream is
    /// either count-validated (definite) or break-byte-terminated
    /// (indefinite).
    /// </summary>
    /// <exception cref="InvalidOperationException">The current top of stack is not a map, the pair count is short, or a value is missing for the most recent key.</exception>
    public void WriteEndMap()
    {
        if(stack.Count == 0 || stack.Peek().Major != CborMajorType.Map)
        {
            CborThrowHelper.ThrowInvalidState("WriteEndMap", DescribeTopState());
        }

        ContainerFrame frame = stack.Pop();
        if(frame.ExpectingMapValue)
        {
            throw new InvalidOperationException("CBOR map closed with a key written but no corresponding value.");
        }

        if(frame.SortBuffer is not null)
        {
            //Canonical map: emit the header now, into the parent sink, and follow with the sorted pairs.
            sortFrameCount--;
            if(frame.SortEntries!.Count != frame.ExpectedCount)
            {
                CborThrowHelper.ThrowContainerLengthMismatch("map", frame.ExpectedCount, frame.SortEntries.Count);
            }
            SortEntries(frame.SortEntries, options.ConformanceMode);
            WriteHeader(CborMajorType.Map, (ulong)frame.SortEntries.Count);
            foreach((byte[] keyBytes, byte[] valueBytes) in frame.SortEntries)
            {
                EmitRawBytes(keyBytes);
                EmitRawBytes(valueBytes);
            }
        }
        else if(frame.IsDefinite)
        {
            if(frame.ItemsWritten != frame.ExpectedCount)
            {
                CborThrowHelper.ThrowContainerLengthMismatch("map", frame.ExpectedCount, frame.ItemsWritten);
            }
        }
        else
        {
            WriteBreakStop();
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a Boolean value as a major-type 7 simple value (false = 20,
    /// true = 21).
    /// </summary>
    public void WriteBoolean(bool value)
    {
        WriteSimpleValue(value ? (byte)CborSimpleValue.True : (byte)CborSimpleValue.False);
    }

    /// <summary>Writes the CBOR null value (major type 7, simple value 22).</summary>
    public void WriteNull()
    {
        WriteSimpleValue((byte)CborSimpleValue.Null);
    }

    /// <summary>Writes the CBOR undefined value (major type 7, simple value 23).</summary>
    public void WriteUndefined()
    {
        WriteSimpleValue((byte)CborSimpleValue.Undefined);
    }

    /// <summary>
    /// Writes a simple value with the supplied numeric identifier.
    /// Identifiers 24..31 are reserved for major-type 7 length encoding
    /// and are rejected. Identifiers 0..19 are emitted as a single
    /// initial byte; identifiers 32..255 are emitted as the major-type 7
    /// initial byte with additional information 24 followed by the
    /// identifier byte.
    /// </summary>
    /// <param name="value">The simple-value identifier.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is in the reserved 24..31 range.</exception>
    public void WriteSimpleValue(byte value)
    {
        if(value is >= 24 and <= 31)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "CBOR simple-value identifiers 24..31 are reserved by the spec.");
        }

        IBufferWriter<byte> sink = ActiveSink;
        if(value <= CborHeader.ImmediateMax)
        {
            Span<byte> span = sink.GetSpan(1);
            span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | value);
            sink.Advance(1);
            OnBytesEmitted(1);
        }
        else
        {
            Span<byte> span = sink.GetSpan(2);
            span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoOneByte);
            span[1] = value;
            sink.Advance(2);
            OnBytesEmitted(2);
        }
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes a CBOR tag (major type 6). The tag does not by itself
    /// constitute a complete data item: the next item written becomes the
    /// tagged content. Tags may be nested.
    /// </summary>
    /// <param name="tag">The tag identifier.</param>
    public void WriteTag(ulong tag)
    {
        WriteHeader(CborMajorType.Tag, tag);
        pendingTagDepth++;
    }

    /// <summary>Writes a CBOR tag (major type 6) using a typed <see cref="CborTag"/>.</summary>
    /// <param name="tag">The tag.</param>
    public void WriteTag(CborTag tag) => WriteTag(tag.Value);

    /// <summary>Writes an IEEE 754 half-precision (binary16) floating-point value as three bytes.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteHalf(Half value)
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(3);
        span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoTwoByte);
        BinaryPrimitives.WriteUInt16BigEndian(span[1..], BitConverter.HalfToUInt16Bits(value));
        sink.Advance(3);
        OnBytesEmitted(3);
        OnDataItemCompleted();
    }

    /// <summary>Writes an IEEE 754 single-precision (binary32) floating-point value as five bytes.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteSingle(float value)
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(5);
        span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoFourByte);
        CborFloatConversions.WriteBinary32(value, span[1..]);
        sink.Advance(5);
        OnBytesEmitted(5);
        OnDataItemCompleted();
    }

    /// <summary>
    /// Writes an IEEE 754 double-precision (binary64) floating-point value.
    /// Under non-canonical conformance modes the encoding is always nine
    /// bytes. Under canonical modes the writer emits the shortest IEEE 754
    /// form that round-trips losslessly per RFC 8949 §4.2.2: half-precision
    /// (three bytes) when possible, then single-precision (five bytes),
    /// else double-precision (nine bytes). NaN, positive infinity, and
    /// negative infinity are emitted as canonical half-precision values.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteDouble(double value)
    {
        //All deterministic conformance modes require the shortest IEEE 754
        //form that preserves the value, per RFC 8949 §4.1. RFC 7049
        //canonical inherited the same rule post-RFC-8949; CTAP2 canonical
        //and CDE both apply it. Non-deterministic modes emit binary64
        //unchanged. Note: this is a deliberate spec-faithful divergence
        //from the BCL CborWriter under CborConformanceMode.Canonical, which
        //applies its own float reduction discipline; see
        //CborCanonicalFloatDivergenceTests for a regression pin on the
        //specific values where the two diverge.
        if(IsSortRequired && !options.SuppressFloatReduction)
        {
            WriteCanonicalFloat(value);
        }
        else
        {
            EmitBinary64(value);
        }
        OnDataItemCompleted();
    }

    private void WriteCanonicalFloat(double value)
    {
        if(double.IsNaN(value))
        {
            EmitHalfPrecisionBits(0x7E00);
            return;
        }
        if(double.IsPositiveInfinity(value))
        {
            EmitHalfPrecisionBits(0x7C00);
            return;
        }
        if(double.IsNegativeInfinity(value))
        {
            EmitHalfPrecisionBits(0xFC00);
            return;
        }

        //Reduce to single-precision when the round-trip preserves bits exactly.
        float asFloat = (float)value;
        if((double)asFloat == value && BitConverter.DoubleToInt64Bits(value) == BitConverter.DoubleToInt64Bits((double)asFloat))
        {
            //Reduce further to half-precision when that also preserves bits exactly.
            Half asHalf = (Half)asFloat;
            if((float)asHalf == asFloat && BitConverter.SingleToInt32Bits(asFloat) == BitConverter.SingleToInt32Bits((float)asHalf))
            {
                ushort halfBits = BitConverter.HalfToUInt16Bits(asHalf);
                EmitHalfPrecisionBits(halfBits);
                return;
            }

            EmitBinary32(asFloat);
            return;
        }

        EmitBinary64(value);
    }

    private void EmitHalfPrecisionBits(ushort bits)
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(3);
        span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoTwoByte);
        BinaryPrimitives.WriteUInt16BigEndian(span[1..], bits);
        sink.Advance(3);
        OnBytesEmitted(3);
    }

    private void EmitBinary32(float value)
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(5);
        span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoFourByte);
        CborFloatConversions.WriteBinary32(value, span[1..]);
        sink.Advance(5);
        OnBytesEmitted(5);
    }

    private void EmitBinary64(double value)
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(9);
        span[0] = (byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoEightByte);
        CborFloatConversions.WriteBinary64(value, span[1..]);
        sink.Advance(9);
        OnBytesEmitted(9);
    }

    private bool IsSortRequired
        => options.ConformanceMode is CborConformanceMode.RfcCanonical
                                   or CborConformanceMode.Ctap2Canonical
                                   or CborConformanceMode.Cde;

    private IBufferWriter<byte> ActiveSink
    {
        get
        {
            if(sortFrameCount == 0)
            {
                return originalDestination;
            }
            //Find the topmost sort frame's buffer; deeper sort frames take precedence.
            foreach(ContainerFrame frame in stack)
            {
                if(frame.SortBuffer is not null)
                {
                    return frame.SortBuffer;
                }
            }
            return originalDestination;
        }
    }

    private void OnBytesEmitted(int count)
    {
        if(sortFrameCount == 0)
        {
            bytesWritten += count;
        }
    }

    private void EmitRawBytes(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length == 0)
        {
            return;
        }
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> destinationSpan = sink.GetSpan(bytes.Length);
        bytes.CopyTo(destinationSpan);
        sink.Advance(bytes.Length);
        OnBytesEmitted(bytes.Length);
    }

    private void WriteHeader(CborMajorType major, ulong argument)
    {
        int length = CborHeader.LengthFor(argument);
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(length);
        int written = CborHeader.Write(major, argument, span);
        sink.Advance(written);
        OnBytesEmitted(written);
    }

    private void WriteIndefiniteIntroducer(CborMajorType major)
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(1);
        CborHeader.WriteIndefiniteIntroducer(major, span);
        sink.Advance(1);
        OnBytesEmitted(1);
    }

    private void WriteBreakStop()
    {
        IBufferWriter<byte> sink = ActiveSink;
        Span<byte> span = sink.GetSpan(1);
        span[0] = CborHeader.BreakStop;
        sink.Advance(1);
        OnBytesEmitted(1);
    }

    private void EnsureIndefiniteAllowed()
    {
        if(!options.AllowIndefiniteLength)
        {
            CborThrowHelper.ThrowFeatureDisabledByConformanceMode("indefinite-length", options.ConformanceMode);
        }
    }

    private void OnDataItemCompleted()
    {
        //All pending tags collapse onto the data item that just completed,
        //since a tag plus its content is one logical item. The parent
        //container then receives a single bump for the (possibly tagged)
        //item that just finished.
        pendingTagDepth = 0;
        if(stack.Count == 0)
        {
            return;
        }

        ContainerFrame frame = stack.Peek();
        bool wasExpectingValue = frame.ExpectingMapValue;
        bool isSortFrame = frame.SortBuffer is not null;

        if(frame.Major == CborMajorType.Map)
        {
            if(frame.ExpectingMapValue)
            {
                frame.ItemsWritten++;
                frame.ExpectingMapValue = false;
            }
            else
            {
                frame.ExpectingMapValue = true;
            }
        }
        else
        {
            frame.ItemsWritten++;
        }

        if(isSortFrame)
        {
            SnapshotSortBufferEntry(frame, wasExpectingValue);
        }
    }

    private static void SnapshotSortBufferEntry(ContainerFrame frame, bool wasExpectingValue)
    {
        ArrayBufferWriter<byte> buffer = frame.SortBuffer!;
        byte[] bytes = buffer.WrittenSpan.ToArray();
        buffer.Clear();
        if(!wasExpectingValue)
        {
            //We just completed a key (the frame transitioned from key-side to value-side).
            frame.PendingKey = bytes;
        }
        else
        {
            //We just completed a value (the frame transitioned from value-side back to key-side).
            frame.SortEntries!.Add((frame.PendingKey!, bytes));
            frame.PendingKey = null;
        }
    }

    private static void SortEntries(List<(byte[] Key, byte[] Value)> entries, CborConformanceMode mode)
    {
        IComparer<(byte[] Key, byte[] Value)> comparer = mode switch
        {
            CborConformanceMode.Cde => CdeKeyComparer.Instance,
            _ => LengthFirstKeyComparer.Instance
        };
        entries.Sort(comparer);
    }

    /// <summary>Orders canonical-map entries by byte-lexicographic key comparison (the CDE order), carrying no state.</summary>
    private sealed class CdeKeyComparer : IComparer<(byte[] Key, byte[] Value)>
    {
        /// <summary>The shared stateless instance.</summary>
        public static CdeKeyComparer Instance { get; } = new();

        /// <summary>Compares two entries by byte-lexicographic key order.</summary>
        /// <param name="a">The first entry.</param>
        /// <param name="b">The second entry.</param>
        /// <returns>The sign of the byte-lexicographic key comparison.</returns>
        public int Compare((byte[] Key, byte[] Value) a, (byte[] Key, byte[] Value) b)
        {
            return a.Key.AsSpan().SequenceCompareTo(b.Key.AsSpan());
        }
    }

    /// <summary>Orders canonical-map entries by key length first, then byte-lexicographically (the non-CDE canonical order), carrying no state.</summary>
    private sealed class LengthFirstKeyComparer : IComparer<(byte[] Key, byte[] Value)>
    {
        /// <summary>The shared stateless instance.</summary>
        public static LengthFirstKeyComparer Instance { get; } = new();

        /// <summary>Compares two entries by key length, then byte-lexicographically.</summary>
        /// <param name="a">The first entry.</param>
        /// <param name="b">The second entry.</param>
        /// <returns>The sign of the length-then-byte key comparison.</returns>
        public int Compare((byte[] Key, byte[] Value) a, (byte[] Key, byte[] Value) b)
        {
            int lenCmp = a.Key.Length.CompareTo(b.Key.Length);
            return lenCmp != 0 ? lenCmp : a.Key.AsSpan().SequenceCompareTo(b.Key.AsSpan());
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

        public int ItemsWritten { get; set; }

        public bool ExpectingMapValue { get; set; }

        public ArrayBufferWriter<byte>? SortBuffer { get; set; }

        public List<(byte[] Key, byte[] Value)>? SortEntries { get; set; }

        public byte[]? PendingKey { get; set; }
    }
}
