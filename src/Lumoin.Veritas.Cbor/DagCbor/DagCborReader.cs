using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Cbor.Drisl;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Cbor.DagCbor;

/// <summary>
/// Reads CBOR data items under the IPLD DAG-CBOR discipline. Two modes:
/// <see cref="StrictMode"/> rejects any wire-form deviation from the
/// six §Strictness rules with a <see cref="DagCborConformanceException"/>
/// naming the violated rule. Relaxed mode permits the relaxations
/// listed in the IPLD spec's §Decode strictness section verbatim — non-
/// canonical integer / length / tag encodings, out-of-order map keys,
/// half- and single-precision floats — while still rejecting the
/// invariants that remain forbidden even in relaxed mode: any tag other
/// than 42, indefinite-length items, NaN, infinity, undefined, and
/// non-string map keys.
/// </summary>
/// <seealso href="https://ipld.io/specs/codecs/dag-cbor/spec/#strictness"/>
/// <seealso href="https://ipld.io/specs/codecs/dag-cbor/spec/#decode-strictness"/>
public sealed class DagCborReader
{
    private readonly CborReader inner;
    private readonly bool strict;
    private readonly Stack<FrameKind> frameStack = new();

    private static CidCborConverter SharedCidConverter { get; } = new();

    /// <summary>
    /// Initialises a new reader over the contiguous byte buffer
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="strict">When <see langword="true"/> (the default), rejects every wire-form deviation from the IPLD §Strictness rules.</param>
    /// <param name="options">Optional pre-configured options. When <c>null</c>, <see cref="DagCborDefaults.CreateOptions"/> is used.</param>
    /// <param name="pool">Optional memory pool (any <see cref="System.Buffers.MemoryPool{T}"/>, e.g. a consumer's own) for pooled byte-string reads.</param>
    /// <param name="stringInternPool">Optional intern pool for repeated map keys / text strings; recommended for firehose / CAR streams.</param>
    public DagCborReader(
        ReadOnlyMemory<byte> source,
        bool strict = true,
        CborSerializerOptions? options = null,
        MemoryPool<byte>? pool = null,
        CborStringInternPool? stringInternPool = null)
    {
        this.strict = strict;
        inner = new CborReader(source, options ?? DagCborDefaults.CreateOptions(), pool, stringInternPool);
    }

    /// <summary>
    /// Initialises a new reader over the byte sequence
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes to read.</param>
    /// <param name="strict">When <see langword="true"/> (the default), rejects every wire-form deviation from the IPLD §Strictness rules.</param>
    /// <param name="options">Optional pre-configured options. When <c>null</c>, <see cref="DagCborDefaults.CreateOptions"/> is used.</param>
    /// <param name="pool">Optional memory pool (any <see cref="System.Buffers.MemoryPool{T}"/>, e.g. a consumer's own) for pooled byte-string reads.</param>
    /// <param name="stringInternPool">Optional intern pool for repeated map keys / text strings; recommended for firehose / CAR streams.</param>
    public DagCborReader(
        ReadOnlySequence<byte> source,
        bool strict = true,
        CborSerializerOptions? options = null,
        MemoryPool<byte>? pool = null,
        CborStringInternPool? stringInternPool = null)
    {
        this.strict = strict;
        inner = new CborReader(source, options ?? DagCborDefaults.CreateOptions(), pool, stringInternPool);
    }

    /// <summary>
    /// Re-targets this reader at <paramref name="newSource"/> without
    /// allocating a new <see cref="DagCborReader"/>. Clears the frame
    /// stack and resets the inner <see cref="CborReader"/>.
    /// </summary>
    public void Reset(ReadOnlyMemory<byte> newSource)
    {
        inner.Reset(newSource);
        frameStack.Clear();
    }

    /// <summary>
    /// Re-targets this reader at <paramref name="newSource"/>; see
    /// <see cref="Reset(ReadOnlyMemory{byte})"/>.
    /// </summary>
    public void Reset(ReadOnlySequence<byte> newSource)
    {
        inner.Reset(newSource);
        frameStack.Clear();
    }

    /// <summary>Indicates whether the reader is in strict mode.</summary>
    public bool StrictMode => strict;

    /// <summary>Gets the number of currently-open containers.</summary>
    public int CurrentDepth => inner.CurrentDepth;

    /// <summary>Gets the number of bytes consumed from the source.</summary>
    public int BytesConsumed => inner.BytesConsumed;

    /// <summary>
    /// Returns the categorical state of the next data item, validating
    /// that the state is permitted under DAG-CBOR.
    /// </summary>
    /// <returns>The next reader state.</returns>
    /// <exception cref="DagCborConformanceException">The next data item is forbidden under the current mode.</exception>
    public CborReaderState PeekState()
    {
        CborReaderState state = inner.PeekState();
        return state switch
        {
            CborReaderState.HalfPrecisionFloat => strict
                ? throw new DagCborConformanceException("FloatsAlways64Bit", "half-precision floats are not permitted in strict DAG-CBOR")
                : state,
            CborReaderState.SinglePrecisionFloat => strict
                ? throw new DagCborConformanceException("FloatsAlways64Bit", "single-precision floats are not permitted in strict DAG-CBOR")
                : state,
            CborReaderState.Undefined => throw new DagCborConformanceException(
                "AllowedSimpleValues",
                "the 'undefined' simple value is not permitted in DAG-CBOR"),
            CborReaderState.SimpleValue => throw new DagCborConformanceException(
                "AllowedSimpleValues",
                "non-standard simple values are not permitted in DAG-CBOR"),
            _ => state
        };
    }

    /// <summary>Reads an unsigned 64-bit integer.</summary>
    public ulong ReadUInt64()
    {
        EnsureNotInMapKeyPosition();
        ulong value = inner.ReadUInt64();
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads a signed 64-bit integer.</summary>
    public long ReadInt64()
    {
        EnsureNotInMapKeyPosition();
        long value = inner.ReadInt64();
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads a byte string as a zero-copy span (single-segment only).</summary>
    public ReadOnlySpan<byte> ReadByteStringSpan()
    {
        EnsureNotInMapKeyPosition();
        ReadOnlySpan<byte> value = inner.ReadByteStringSpan();
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads a byte string as a <see cref="ReadOnlyMemory{T}"/>.</summary>
    public ReadOnlyMemory<byte> ReadByteStringMemory()
    {
        EnsureNotInMapKeyPosition();
        ReadOnlyMemory<byte> value = inner.ReadByteStringMemory();
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads a UTF-8 text string.</summary>
    public string ReadTextString()
    {
        string value = inner.ReadTextString();
        OnTextRead();
        return value;
    }

    /// <summary>Reads a definite-length array introducer.</summary>
    /// <returns>The array's item count.</returns>
    public int ReadStartArray()
    {
        EnsureNotInMapKeyPosition();
        int? count = inner.ReadStartArray();
        if(count is null)
        {
            throw new DagCborConformanceException(
                "DeterministicEncoding",
                "indefinite-length arrays are not permitted in DAG-CBOR");
        }
        frameStack.Push(FrameKind.Array);
        return count.Value;
    }

    /// <summary>Closes the topmost array.</summary>
    public void ReadEndArray()
    {
        if(frameStack.Count == 0 || frameStack.Peek() != FrameKind.Array)
        {
            throw new InvalidOperationException("DAG-CBOR reader is not currently inside an array.");
        }
        frameStack.Pop();
        inner.ReadEndArray();
        OnNonKeyValueRead();
    }

    /// <summary>Reads a definite-length map introducer.</summary>
    /// <returns>The map's key/value pair count.</returns>
    public int ReadStartMap()
    {
        EnsureNotInMapKeyPosition();
        int? count = inner.ReadStartMap();
        if(count is null)
        {
            throw new DagCborConformanceException(
                "DeterministicEncoding",
                "indefinite-length maps are not permitted in DAG-CBOR");
        }
        frameStack.Push(FrameKind.MapExpectingKey);
        return count.Value;
    }

    /// <summary>Closes the topmost map.</summary>
    public void ReadEndMap()
    {
        if(frameStack.Count == 0)
        {
            throw new InvalidOperationException("DAG-CBOR reader is not currently inside a map.");
        }
        FrameKind top = frameStack.Peek();
        if(top is not FrameKind.MapExpectingKey and not FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DAG-CBOR reader is not currently inside a map.");
        }
        if(top == FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DAG-CBOR map closed with a key consumed but no corresponding value.");
        }
        frameStack.Pop();
        inner.ReadEndMap();
        OnNonKeyValueRead();
    }

    /// <summary>Reads a Boolean.</summary>
    public bool ReadBoolean()
    {
        EnsureNotInMapKeyPosition();
        bool value = inner.ReadBoolean();
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads the CBOR null value.</summary>
    public void ReadNull()
    {
        EnsureNotInMapKeyPosition();
        inner.ReadNull();
        OnNonKeyValueRead();
    }

    /// <summary>
    /// Reads a floating-point value. Always returns a <see cref="double"/>;
    /// in relaxed mode this transparently widens half- and single-precision
    /// floats. Rejects NaN and infinities regardless of mode (rule 5).
    /// </summary>
    public double ReadDouble()
    {
        EnsureNotInMapKeyPosition();
        CborReaderState state = inner.PeekState();
        double value = state switch
        {
            CborReaderState.DoublePrecisionFloat => inner.ReadDouble(),
            CborReaderState.SinglePrecisionFloat when !strict => inner.ReadSingle(),
            CborReaderState.HalfPrecisionFloat when !strict => (double)inner.ReadHalf(),
            CborReaderState.SinglePrecisionFloat => throw new DagCborConformanceException(
                "FloatsAlways64Bit", "single-precision floats are not permitted in strict DAG-CBOR"),
            CborReaderState.HalfPrecisionFloat => throw new DagCborConformanceException(
                "FloatsAlways64Bit", "half-precision floats are not permitted in strict DAG-CBOR"),
            _ => throw new FormatException(
                string.Create(CultureInfo.InvariantCulture, $"Expected DAG-CBOR float; reader is at state {state}."))
        };
        if(double.IsNaN(value))
        {
            throw new DagCborConformanceException("NoNanOrInfinity", "NaN values are not permitted in DAG-CBOR");
        }
        if(double.IsInfinity(value))
        {
            throw new DagCborConformanceException(
                "NoNanOrInfinity",
                value > 0
                    ? "positive infinity is not permitted in DAG-CBOR"
                    : "negative infinity is not permitted in DAG-CBOR");
        }
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads a CID from a CBOR Tag 42 data item. Other tags throw.</summary>
    public CidValue ReadCid()
    {
        EnsureNotInMapKeyPosition();
        if(inner.PeekState() != CborReaderState.Tag)
        {
            throw new FormatException("Expected CBOR Tag 42 (CID); reader is not positioned at a tag.");
        }
        CidValue value = SharedCidConverter.Read(inner);
        OnNonKeyValueRead();
        return value;
    }

    private void EnsureNotInMapKeyPosition()
    {
        if(frameStack.Count > 0 && frameStack.Peek() == FrameKind.MapExpectingKey)
        {
            throw new DagCborConformanceException(
                "DeterministicEncoding",
                "DAG-CBOR maps require text-string keys; only ReadTextString is allowed in key position");
        }
    }

    private void OnNonKeyValueRead()
    {
        if(frameStack.Count == 0)
        {
            return;
        }
        FrameKind top = frameStack.Peek();
        if(top == FrameKind.MapExpectingValue)
        {
            frameStack.Pop();
            frameStack.Push(FrameKind.MapExpectingKey);
        }
    }

    private void OnTextRead()
    {
        if(frameStack.Count == 0)
        {
            return;
        }
        FrameKind top = frameStack.Peek();
        if(top == FrameKind.MapExpectingKey)
        {
            frameStack.Pop();
            frameStack.Push(FrameKind.MapExpectingValue);
        }
        else if(top == FrameKind.MapExpectingValue)
        {
            frameStack.Pop();
            frameStack.Push(FrameKind.MapExpectingKey);
        }
    }

    private enum FrameKind
    {
        Array,
        MapExpectingKey,
        MapExpectingValue
    }
}
