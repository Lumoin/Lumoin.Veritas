using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Cbor.Drisl;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Cbor.DagCbor;

/// <summary>
/// Writes CBOR data items under the IPLD DAG-CBOR discipline. The public
/// surface is a deliberate subset of <see cref="CborWriter"/>: methods
/// that would produce non-DAG-CBOR output are absent (no indefinite-length
/// starters, no half- or single-precision floats, no simple-value writers
/// other than <c>true</c>, <c>false</c>, <c>null</c>, no arbitrary tag
/// writer — only Tag 42 via <see cref="WriteCid"/>). Inputs that would
/// produce non-conforming output (NaN, infinities) throw
/// <see cref="DagCborConformanceException"/> naming the violated rule.
/// </summary>
/// <remarks>
/// <para>
/// The six DAG-CBOR §Strictness rules:
/// </para>
/// <list type="number">
///   <item><description>Only Tag 42 (CID) is permitted.</description></item>
///   <item><description>Deterministic encoding: length-first lexical map-key ordering, no indefinite length, no non-shortest encodings.</description></item>
///   <item><description>Only the simple values <c>true</c> (21), <c>false</c> (20), and <c>null</c> (22).</description></item>
///   <item><description>Floats are always 64-bit (binary64).</description></item>
///   <item><description>NaN, +Infinity, and -Infinity are not permitted.</description></item>
///   <item><description>A document is a single top-level data item.</description></item>
/// </list>
/// </remarks>
/// <seealso href="https://ipld.io/specs/codecs/dag-cbor/spec/#strictness"/>
public sealed class DagCborWriter
{
    private readonly CborWriter inner;
    private readonly Stack<FrameKind> frameStack = new();

    private static CidCborConverter SharedCidConverter { get; } = new();

    /// <summary>
    /// Initialises a new <see cref="DagCborWriter"/> writing into
    /// <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The destination buffer writer.</param>
    /// <param name="options">Optional pre-configured options. When <c>null</c>, <see cref="DagCborDefaults.CreateOptions"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <c>null</c>.</exception>
    public DagCborWriter(IBufferWriter<byte> destination, CborSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        inner = new CborWriter(destination, options ?? DagCborDefaults.CreateOptions());
    }

    /// <summary>Gets the total number of bytes emitted to the destination.</summary>
    public int BytesWritten => inner.BytesWritten;

    /// <summary>Resets the writer's internal state so it can be reused.</summary>
    public void Reset()
    {
        inner.Reset();
        frameStack.Clear();
    }

    /// <summary>Writes a definite-length array introducer.</summary>
    /// <param name="length">The exact item count.</param>
    public void WriteStartArray(int length)
    {
        EnsureNotInMapKeyPosition();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        inner.WriteStartArray(length);
        frameStack.Push(FrameKind.Array);
    }

    /// <summary>Closes the topmost array.</summary>
    public void WriteEndArray()
    {
        if(frameStack.Count == 0 || frameStack.Peek() != FrameKind.Array)
        {
            throw new InvalidOperationException("DAG-CBOR writer is not currently inside an array.");
        }
        frameStack.Pop();
        inner.WriteEndArray();
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a definite-length map introducer. DAG-CBOR maps require text-string keys.</summary>
    /// <param name="length">The exact key/value pair count.</param>
    public void WriteStartMap(int length)
    {
        EnsureNotInMapKeyPosition();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        inner.WriteStartMap(length);
        frameStack.Push(FrameKind.MapExpectingKey);
    }

    /// <summary>Closes the topmost map.</summary>
    public void WriteEndMap()
    {
        if(frameStack.Count == 0)
        {
            throw new InvalidOperationException("DAG-CBOR writer is not currently inside a map.");
        }
        FrameKind top = frameStack.Peek();
        if(top is not FrameKind.MapExpectingKey and not FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DAG-CBOR writer is not currently inside a map.");
        }
        if(top == FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DAG-CBOR map closed with a key written but no corresponding value.");
        }
        frameStack.Pop();
        inner.WriteEndMap();
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a UTF-8 text string. Permitted as both map key and value.</summary>
    /// <param name="value">The string to encode and write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public void WriteTextString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteTextString(value.AsSpan());
    }

    /// <summary>Writes a UTF-8 text string. Permitted as both map key and value.</summary>
    /// <param name="value">The characters to encode and write.</param>
    public void WriteTextString(ReadOnlySpan<char> value)
    {
        inner.WriteTextString(value);
        OnTextWritten();
    }

    /// <summary>Writes a byte string. Cannot be used as a map key (rule on map keys).</summary>
    /// <param name="value">The bytes to write.</param>
    public void WriteByteString(ReadOnlySpan<byte> value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteByteString(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes an unsigned 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt64(ulong value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteUInt64(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes an unsigned 32-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt32(uint value) => WriteUInt64(value);

    /// <summary>Writes a signed 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt64(long value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteInt64(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a signed 32-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt32(int value) => WriteInt64(value);

    /// <summary>Writes a Boolean. Rule 3: only <c>true</c>, <c>false</c> permitted as simple values.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteBoolean(bool value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteBoolean(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes the CBOR null value. Rule 3: <c>null</c> is permitted as a simple value.</summary>
    public void WriteNull()
    {
        EnsureNotInMapKeyPosition();
        inner.WriteNull();
        OnNonKeyValueWritten();
    }

    /// <summary>
    /// Writes a double-precision (binary64) floating-point value. Rule 4
    /// fixes float precision at 64 bits; rule 5 forbids NaN and infinities.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="DagCborConformanceException"><paramref name="value"/> is NaN, +Infinity, or -Infinity.</exception>
    public void WriteDouble(double value)
    {
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
        EnsureNotInMapKeyPosition();
        //The inner CborWriter under RfcCanonical mode emits 64-bit
        //double without precision reduction, satisfying rule 4.
        inner.WriteDouble(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a CID as a CBOR Tag 42 data item. Rule 1: only Tag 42 is permitted.</summary>
    /// <param name="value">The CID to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
    public void WriteCid(CidValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureNotInMapKeyPosition();
        SharedCidConverter.Write(inner, value);
        OnNonKeyValueWritten();
    }

    private void EnsureNotInMapKeyPosition()
    {
        if(frameStack.Count > 0 && frameStack.Peek() == FrameKind.MapExpectingKey)
        {
            throw new InvalidOperationException(
                "DAG-CBOR maps require text-string keys; only WriteTextString is allowed in key position.");
        }
    }

    private void OnNonKeyValueWritten()
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

    private void OnTextWritten()
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
