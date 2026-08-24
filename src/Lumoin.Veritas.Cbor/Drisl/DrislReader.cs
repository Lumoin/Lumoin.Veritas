using System;
using System.Buffers;
using System.Collections.Generic;
using CidValue = Lumoin.Veritas.Cid.Cid;

namespace Lumoin.Veritas.Cbor.Drisl;

/// <summary>
/// Reads CBOR data items under the DRISL discipline. The reader rejects
/// any input feature DRISL forbids: indefinite-length items, half- or
/// single-precision floats, NaN or infinity doubles, integer or non-text
/// map keys, the <c>undefined</c> simple value, and arbitrary simple
/// values. CID values (Tag 42) are read through <see cref="ReadCid"/>;
/// no other tag is exposed.
/// </summary>
public sealed class DrislReader
{
    private readonly CborReader inner;
    private readonly Stack<FrameKind> frameStack = new();

    private static CidCborConverter SharedCidConverter { get; } = new();

    /// <summary>Initialises a new <see cref="DrislReader"/> reading from <paramref name="source"/>.</summary>
    /// <param name="source">The contiguous bytes to read.</param>
    public DrislReader(ReadOnlyMemory<byte> source)
    {
        inner = new CborReader(source, DrislDefaults.CreateOptions());
    }

    /// <summary>Initialises a new <see cref="DrislReader"/> reading from <paramref name="source"/>.</summary>
    /// <param name="source">The byte sequence to read.</param>
    public DrislReader(ReadOnlySequence<byte> source)
    {
        inner = new CborReader(source, DrislDefaults.CreateOptions());
    }

    /// <summary>Gets the total number of bytes consumed so far.</summary>
    public int BytesConsumed => inner.BytesConsumed;

    /// <summary>Gets the number of currently-open containers.</summary>
    public int CurrentDepth => inner.CurrentDepth;

    /// <summary>
    /// Returns the categorical state of the next data item, validating
    /// that the state is permitted under DRISL.
    /// </summary>
    /// <returns>The next reader state.</returns>
    /// <exception cref="FormatException">The next data item is forbidden by DRISL.</exception>
    public CborReaderState PeekState()
    {
        CborReaderState state = inner.PeekState();
        return state switch
        {
            CborReaderState.HalfPrecisionFloat => throw new FormatException("DRISL forbids half-precision floats."),
            CborReaderState.SinglePrecisionFloat => throw new FormatException("DRISL forbids single-precision floats."),
            CborReaderState.Undefined => throw new FormatException("DRISL forbids the undefined simple value."),
            CborReaderState.SimpleValue => throw new FormatException("DRISL forbids non-standard simple values."),
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

    /// <summary>Reads a byte string.</summary>
    public byte[] ReadByteString()
    {
        EnsureNotInMapKeyPosition();
        byte[] value = inner.ReadByteString();
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
            throw new FormatException("DRISL forbids indefinite-length arrays.");
        }
        frameStack.Push(FrameKind.Array);
        return count.Value;
    }

    /// <summary>Closes the topmost array.</summary>
    public void ReadEndArray()
    {
        if(frameStack.Count == 0 || frameStack.Peek() != FrameKind.Array)
        {
            throw new InvalidOperationException("DRISL reader is not currently inside an array.");
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
            throw new FormatException("DRISL forbids indefinite-length maps.");
        }
        frameStack.Push(FrameKind.MapExpectingKey);
        return count.Value;
    }

    /// <summary>Closes the topmost map.</summary>
    public void ReadEndMap()
    {
        if(frameStack.Count == 0)
        {
            throw new InvalidOperationException("DRISL reader is not currently inside a map.");
        }
        FrameKind top = frameStack.Peek();
        if(top is not FrameKind.MapExpectingKey and not FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DRISL reader is not currently inside a map.");
        }
        if(top == FrameKind.MapExpectingValue)
        {
            throw new InvalidOperationException("DRISL map closed with a key consumed but no corresponding value.");
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

    /// <summary>Reads a double-precision floating-point value, rejecting NaN and infinities.</summary>
    public double ReadDouble()
    {
        EnsureNotInMapKeyPosition();
        double value = inner.ReadDouble();
        if(double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new FormatException("DRISL forbids NaN and infinity float values.");
        }
        OnNonKeyValueRead();
        return value;
    }

    /// <summary>Reads a CID from a CBOR Tag 42 data item.</summary>
    public CidValue ReadCid()
    {
        EnsureNotInMapKeyPosition();
        CidValue value = SharedCidConverter.Read(inner);
        OnNonKeyValueRead();
        return value;
    }

    private void EnsureNotInMapKeyPosition()
    {
        if(frameStack.Count > 0 && frameStack.Peek() == FrameKind.MapExpectingKey)
        {
            throw new FormatException("DRISL maps require text-string keys; only ReadTextString is allowed in key position.");
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
