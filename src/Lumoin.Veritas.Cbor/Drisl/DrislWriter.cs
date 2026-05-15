using System;
using System.Buffers;
using System.Collections.Generic;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Cbor.Drisl;

/// <summary>
/// Writes CBOR data items under the DRISL discipline: deterministic CBOR
/// with sorted text-string-keyed maps, finite double-precision floats,
/// no indefinite-length items, no half- or single-precision floats, no
/// arbitrary tags (only Tag 42 via <see cref="WriteCid"/>), and no simple
/// values other than <c>true</c>, <c>false</c>, and <c>null</c>.
/// </summary>
/// <remarks>
/// <para>
/// The public surface is a deliberate subset of <see cref="CborWriter"/>:
/// methods that would produce non-DRISL output are absent. Inputs that
/// would produce non-DRISL output (NaN, infinities, integer-keyed maps)
/// throw before the underlying writer emits any bytes.
/// </para>
/// </remarks>
public sealed class DrislWriter
{
    private readonly CborWriter inner;
    private readonly Stack<FrameKind> frameStack = new();

    private static CidCborConverter SharedCidConverter { get; } = new();

    /// <summary>
    /// Initialises a new <see cref="DrislWriter"/> writing into
    /// <paramref name="destination"/> with DRISL-discipline options.
    /// </summary>
    /// <param name="destination">The destination buffer writer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <c>null</c>.</exception>
    public DrislWriter(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        inner = new CborWriter(destination, DrislDefaults.CreateOptions());
    }

    /// <summary>Gets the total number of bytes emitted to the destination.</summary>
    public int BytesWritten => inner.BytesWritten;

    /// <summary>Resets the writer's internal state so it can be reused.</summary>
    public void Reset()
    {
        inner.Reset();
        frameStack.Clear();
    }

    /// <summary>Writes an unsigned 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt64(ulong value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteUInt64(value);
        OnNonKeyValueWritten();
    }

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

    /// <summary>Writes a byte string.</summary>
    /// <param name="value">The bytes to write.</param>
    public void WriteByteString(ReadOnlySpan<byte> value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteByteString(value);
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
            throw new InvalidOperationException("DRISL writer is not currently inside an array.");
        }
        frameStack.Pop();
        inner.WriteEndArray();
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a definite-length map introducer. DRISL maps require text-string keys.</summary>
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
            throw new InvalidOperationException("DRISL writer is not currently inside a map.");
        }
        FrameKind top = frameStack.Peek();
        if(top is not FrameKind.MapExpectingKey and not FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DRISL writer is not currently inside a map.");
        }
        if(top == FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DRISL map closed with a key written but no corresponding value.");
        }
        frameStack.Pop();
        inner.WriteEndMap();
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a Boolean.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteBoolean(bool value)
    {
        EnsureNotInMapKeyPosition();
        inner.WriteBoolean(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes the CBOR null value.</summary>
    public void WriteNull()
    {
        EnsureNotInMapKeyPosition();
        inner.WriteNull();
        OnNonKeyValueWritten();
    }

    /// <summary>
    /// Writes a double-precision (binary64) floating-point value. DRISL
    /// rejects NaN and infinities; negative zero is permitted.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is NaN or infinity.</exception>
    public void WriteDouble(double value)
    {
        if(double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "DRISL rejects NaN and infinity float values.");
        }
        EnsureNotInMapKeyPosition();
        inner.WriteDouble(value);
        OnNonKeyValueWritten();
    }

    /// <summary>Writes a CID as a CBOR Tag 42 data item.</summary>
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
            throw new InvalidOperationException("DRISL maps require text-string keys; only WriteTextString is allowed in key position.");
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
            //Value just completed; next pair starts with another key.
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
