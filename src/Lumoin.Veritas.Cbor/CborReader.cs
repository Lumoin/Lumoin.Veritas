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
/// <see cref="ReadOnlySequence{T}"/>. Single-segment sequences are
/// dispatched into the contiguous fast path; multi-segment sequences are
/// materialised into a contiguous buffer once at construction. Streaming
/// over genuinely discontiguous input without materialisation is a future
/// optimisation.
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
    private readonly ReadOnlyMemory<byte> source;
    private readonly CborSerializerOptions options;
    private readonly Stack<ContainerFrame> stack = new();
    private int position;
    private int pendingTagDepth;

    /// <summary>
    /// Initialises a new reader over the contiguous byte buffer
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="options">The conformance options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public CborReader(ReadOnlyMemory<byte> source, CborSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.source = source;
        this.options = options;
    }

    /// <summary>
    /// Initialises a new reader over the byte sequence
    /// <paramref name="source"/>. Single-segment sequences are kept as-is;
    /// multi-segment sequences are concatenated once into a contiguous
    /// buffer.
    /// </summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="options">The conformance options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    public CborReader(ReadOnlySequence<byte> source, CborSerializerOptions options)
        : this(source.IsSingleSegment ? source.First : source.ToArray(), options)
    {
    }

    /// <summary>Gets the active conformance options.</summary>
    public CborSerializerOptions Options => options;

    /// <summary>Gets the number of bytes consumed so far from the source.</summary>
    public int BytesConsumed => position;

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

        if(position >= source.Length)
        {
            if(stack.Count != 0)
            {
                throw new FormatException("CBOR source is exhausted while containers are still open.");
            }
            return CborReaderState.Finished;
        }

        byte initial = source.Span[position];
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
                    string.Create(CultureInfo.InvariantCulture, $"Unrecognised CBOR major type {(byte)major} at position {position}."));
            }
        }
    }

    /// <summary>Reads an unsigned 64-bit integer (major type 0).</summary>
    public ulong ReadUInt64()
    {
        ExpectMajor(CborMajorType.UnsignedInteger);
        byte ai = (byte)(source.Span[position] & 0x1F);
        position++;
        ulong value = ReadArgument(ai);
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
        byte initial = source.Span[position];
        CborMajorType major = (CborMajorType)(initial >> 5);
        if(major != CborMajorType.UnsignedInteger && major != CborMajorType.NegativeInteger)
        {
            ThrowMajorMismatch("integer", major);
        }
        byte ai = (byte)(initial & 0x1F);
        position++;
        ulong argument = ReadArgument(ai);
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
        OnDataItemCompleted();
        return value;
    }

    /// <summary>
    /// Reads a byte string and returns its content as a freshly allocated
    /// array. Definite-length byte strings are read directly. Indefinite-
    /// length byte strings are consumed chunk-by-chunk and concatenated;
    /// the break byte terminates the data item.
    /// </summary>
    public byte[] ReadByteString()
    {
        ExpectMajor(CborMajorType.ByteString);
        byte ai = (byte)(source.Span[position] & 0x1F);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            position++;
            return ReadIndefiniteByteStringChunks();
        }
        position++;
        ulong length = ReadArgument(ai);
        if(length > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(length));
        }
        int len = (int)length;
        byte[] result = new byte[len];
        source.Span.Slice(position, len).CopyTo(result);
        position += len;
        OnDataItemCompleted();
        return result;
    }

    private byte[] ReadIndefiniteByteStringChunks()
    {
        List<byte[]> chunks = [];
        int totalLength = 0;
        while(position < source.Length && source.Span[position] != CborHeader.BreakStop)
        {
            byte chunkInitial = source.Span[position];
            if((CborMajorType)(chunkInitial >> 5) != CborMajorType.ByteString)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Indefinite byte string chunk at position {position} is not a byte string (major type {chunkInitial >> 5})."));
            }
            byte chunkAi = (byte)(chunkInitial & 0x1F);
            if(chunkAi == CborHeader.AdditionalInfoIndefinite)
            {
                throw new FormatException("Indefinite byte string chunks must each be definite length; nested indefinite strings are forbidden by RFC 8949 §3.2.3.");
            }
            position++;
            ulong chunkLength = ReadArgument(chunkAi);
            if(chunkLength > int.MaxValue)
            {
                CborThrowHelper.ThrowLengthExceedsInt32(nameof(chunkLength));
            }
            int len = (int)chunkLength;
            byte[] chunk = new byte[len];
            source.Span.Slice(position, len).CopyTo(chunk);
            position += len;
            chunks.Add(chunk);
            totalLength += len;
        }
        if(position >= source.Length)
        {
            throw new FormatException("Indefinite-length byte string is missing its break byte.");
        }
        position++;

        byte[] assembled = new byte[totalLength];
        int offset = 0;
        foreach(byte[] chunk in chunks)
        {
            chunk.CopyTo(assembled, offset);
            offset += chunk.Length;
        }
        OnDataItemCompleted();
        return assembled;
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
        ExpectMajor(CborMajorType.TextString);
        byte ai = (byte)(source.Span[position] & 0x1F);
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            position++;
            return ReadIndefiniteTextStringChunks();
        }
        position++;
        ulong length = ReadArgument(ai);
        if(length > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(length));
        }
        int len = (int)length;
        ReadOnlySpan<byte> bytes = source.Span.Slice(position, len);
        string value = DecodeUtf8(bytes);
        position += len;
        OnDataItemCompleted();
        return value;
    }

    private string ReadIndefiniteTextStringChunks()
    {
        List<byte[]> chunks = [];
        int totalLength = 0;
        while(position < source.Length && source.Span[position] != CborHeader.BreakStop)
        {
            byte chunkInitial = source.Span[position];
            if((CborMajorType)(chunkInitial >> 5) != CborMajorType.TextString)
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Indefinite text string chunk at position {position} is not a text string (major type {chunkInitial >> 5})."));
            }
            byte chunkAi = (byte)(chunkInitial & 0x1F);
            if(chunkAi == CborHeader.AdditionalInfoIndefinite)
            {
                throw new FormatException("Indefinite text string chunks must each be definite length; nested indefinite strings are forbidden by RFC 8949 §3.2.3.");
            }
            position++;
            ulong chunkLength = ReadArgument(chunkAi);
            if(chunkLength > int.MaxValue)
            {
                CborThrowHelper.ThrowLengthExceedsInt32(nameof(chunkLength));
            }
            int len = (int)chunkLength;
            byte[] chunk = new byte[len];
            source.Span.Slice(position, len).CopyTo(chunk);
            position += len;
            chunks.Add(chunk);
            totalLength += len;
        }
        if(position >= source.Length)
        {
            throw new FormatException("Indefinite-length text string is missing its break byte.");
        }
        position++;

        byte[] assembled = new byte[totalLength];
        int offset = 0;
        foreach(byte[] chunk in chunks)
        {
            chunk.CopyTo(assembled, offset);
            offset += chunk.Length;
        }
        string value = DecodeUtf8(assembled);
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
        ExpectMajor(CborMajorType.Array);
        byte ai = (byte)(source.Span[position] & 0x1F);
        position++;
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            stack.Push(new ContainerFrame(CborMajorType.Array, isDefinite: false, expectedCount: -1));
            return null;
        }
        ulong count = ReadArgument(ai);
        if(count > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(count));
        }
        int countInt = (int)count;
        stack.Push(new ContainerFrame(CborMajorType.Array, isDefinite: true, expectedCount: countInt));
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
            CborThrowHelper.ThrowInvalidWriterState("ReadEndArray", DescribeTopState());
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
        ExpectMajor(CborMajorType.Map);
        byte ai = (byte)(source.Span[position] & 0x1F);
        position++;
        if(ai == CborHeader.AdditionalInfoIndefinite)
        {
            EnsureIndefiniteAllowed();
            stack.Push(new ContainerFrame(CborMajorType.Map, isDefinite: false, expectedCount: -1));
            return null;
        }
        ulong count = ReadArgument(ai);
        if(count > int.MaxValue)
        {
            CborThrowHelper.ThrowLengthExceedsInt32(nameof(count));
        }
        int countInt = (int)count;
        stack.Push(new ContainerFrame(CborMajorType.Map, isDefinite: true, expectedCount: countInt));
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
            CborThrowHelper.ThrowInvalidWriterState("ReadEndMap", DescribeTopState());
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
        byte initial = source.Span[position];
        if((CborMajorType)(initial >> 5) != CborMajorType.SimpleAndFloat)
        {
            ThrowMajorMismatch("boolean", (CborMajorType)(initial >> 5));
        }
        byte ai = (byte)(initial & 0x1F);
        if(ai != 20 && ai != 21)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR boolean (simple value 20 or 21) at position {position}; got {ai}."));
        }
        position++;
        bool value = ai == 21;
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads the CBOR null simple value.</summary>
    public void ReadNull()
    {
        ExpectInitialByte((byte)(((byte)CborMajorType.SimpleAndFloat << 5) | (byte)CborSimpleValue.Null), "null");
        position++;
        OnDataItemCompleted();
    }

    /// <summary>Reads the CBOR undefined simple value.</summary>
    public void ReadUndefined()
    {
        ExpectInitialByte((byte)(((byte)CborMajorType.SimpleAndFloat << 5) | (byte)CborSimpleValue.Undefined), "undefined");
        position++;
        OnDataItemCompleted();
    }

    /// <summary>Reads a non-standardised simple value identifier.</summary>
    public byte ReadSimpleValue()
    {
        byte initial = source.Span[position];
        if((CborMajorType)(initial >> 5) != CborMajorType.SimpleAndFloat)
        {
            ThrowMajorMismatch("simple value", (CborMajorType)(initial >> 5));
        }
        byte ai = (byte)(initial & 0x1F);
        position++;
        byte value;
        if(ai <= CborHeader.ImmediateMax)
        {
            value = ai;
        }
        else if(ai == CborHeader.AdditionalInfoOneByte)
        {
            value = source.Span[position];
            position++;
        }
        else
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected a CBOR simple value at position {position}; got additional info {ai}."));
        }
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads a CBOR tag. The next data item is the tagged content.</summary>
    public CborTag ReadTag()
    {
        ExpectMajor(CborMajorType.Tag);
        byte ai = (byte)(source.Span[position] & 0x1F);
        position++;
        ulong tag = ReadArgument(ai);
        pendingTagDepth++;
        return new CborTag(tag);
    }

    /// <summary>Reads a half-precision (binary16) floating-point value.</summary>
    public Half ReadHalf()
    {
        ExpectInitialByte((byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoTwoByte), "half-precision float");
        position++;
        ushort bits = BinaryPrimitives.ReadUInt16BigEndian(source.Span.Slice(position, 2));
        position += 2;
        Half value = BitConverter.UInt16BitsToHalf(bits);
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads a single-precision (binary32) floating-point value.</summary>
    public float ReadSingle()
    {
        ExpectInitialByte((byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoFourByte), "single-precision float");
        position++;
        float value = CborFloatConversions.ReadBinary32(source.Span.Slice(position, 4));
        position += 4;
        OnDataItemCompleted();
        return value;
    }

    /// <summary>Reads a double-precision (binary64) floating-point value.</summary>
    public double ReadDouble()
    {
        ExpectInitialByte((byte)(((byte)CborMajorType.SimpleAndFloat << 5) | CborHeader.AdditionalInfoEightByte), "double-precision float");
        position++;
        double value = CborFloatConversions.ReadBinary64(source.Span.Slice(position, 8));
        position += 8;
        OnDataItemCompleted();
        return value;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private ulong ReadArgument(byte additionalInfo)
    {
        if(additionalInfo <= CborHeader.ImmediateMax)
        {
            return additionalInfo;
        }
        switch(additionalInfo)
        {
            case CborHeader.AdditionalInfoOneByte:
            {
                ulong v = source.Span[position];
                position += 1;
                return v;
            }
            case CborHeader.AdditionalInfoTwoByte:
            {
                ulong v = BinaryPrimitives.ReadUInt16BigEndian(source.Span.Slice(position, 2));
                position += 2;
                return v;
            }
            case CborHeader.AdditionalInfoFourByte:
            {
                ulong v = BinaryPrimitives.ReadUInt32BigEndian(source.Span.Slice(position, 4));
                position += 4;
                return v;
            }
            case CborHeader.AdditionalInfoEightByte:
            {
                ulong v = BinaryPrimitives.ReadUInt64BigEndian(source.Span.Slice(position, 8));
                position += 8;
                return v;
            }
            default:
            {
                throw new FormatException(
                    string.Create(CultureInfo.InvariantCulture, $"Invalid CBOR additional information value {additionalInfo} at position {position - 1}."));
            }
        }
    }

    private void ExpectMajor(CborMajorType expected)
    {
        if(position >= source.Length)
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        CborMajorType actual = (CborMajorType)(source.Span[position] >> 5);
        if(actual != expected)
        {
            ThrowMajorMismatch(expected.ToString(), actual);
        }
    }

    private void ExpectInitialByte(byte expected, string label)
    {
        if(position >= source.Length)
        {
            throw new FormatException("CBOR source is exhausted.");
        }
        if(source.Span[position] != expected)
        {
            throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected CBOR {label} initial byte 0x{expected:X2} at position {position}; got 0x{source.Span[position]:X2}."));
        }
    }

    private void ThrowMajorMismatch(string expected, CborMajorType actual)
    {
        throw new FormatException(
            string.Create(CultureInfo.InvariantCulture, $"Expected CBOR {expected} at position {position}; got major type {(byte)actual}."));
    }

    private bool PositionAtBreak()
    {
        return position < source.Length && source.Span[position] == CborHeader.BreakStop;
    }

    private void ConsumeBreakByte()
    {
        if(!PositionAtBreak())
        {
            throw new FormatException("CBOR indefinite container missing break byte.");
        }
        position++;
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
}
