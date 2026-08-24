using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Cbor.Internal;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Re-emits a CBOR data item in RFC 8949 §4.2 CBOR Deterministic Encoding
/// (CDE) form. Reads arbitrary input under
/// <see cref="CborConformanceMode.Lax"/> and writes through a writer
/// configured for <see cref="CborConformanceMode.Cde"/>, which sorts map
/// keys length-first-lexically, collapses indefinite-length items to
/// definite-length, picks the shortest integer/float width that
/// preserves the value, and normalises NaN to a canonical form.
/// </summary>
/// <remarks>
/// <para>
/// Intended for normalising CBOR received from external sources before
/// hashing or signing. For consumers that already produce their own CBOR
/// through this library's <see cref="CborWriter"/> in
/// <see cref="CborConformanceMode.Cde"/> mode, output is already
/// canonical and no canonicalisation pass is required.
/// </para>
/// <para>
/// The implementation builds an internal tree representation of the
/// input first and then re-emits it; map-key sorting happens inside the
/// canonical <see cref="CborWriter"/> automatically. Indefinite-length
/// items are buffered transparently. The tree representation is
/// internal and not part of the public surface.
/// </para>
/// <para>
/// Integer range: the canonicaliser preserves the full CBOR integer
/// range — unsigned values up to <see cref="ulong.MaxValue"/> via
/// <see cref="CborReader.ReadUInt64"/>, and negative values down to
/// <c>-(1 + <see cref="ulong.MaxValue"/>)</c> via
/// <see cref="CborReader.ReadCborNegativeIntegerRepresentation"/>. The
/// underlying CBOR encoding fits any data item's integer in either a
/// <see cref="ulong"/> or a raw representation argument; no
/// <c>BigInteger</c> is required to round-trip.
/// </para>
/// <para>
/// Limitations: CBOR simple values other than <c>true</c>, <c>false</c>,
/// <c>null</c>, and <c>undefined</c> are not preserved (see
/// <see cref="CborReaderState.SimpleValue"/>); the canonicaliser raises
/// <see cref="InvalidOperationException"/> on encountering one.
/// </para>
/// <para>
/// See <see href="https://www.rfc-editor.org/rfc/rfc8949#section-4.2">RFC 8949 §4.2</see>.
/// </para>
/// </remarks>
public static class CborCanonicalizer
{
    /// <summary>
    /// Reads one CBOR data item from <paramref name="source"/> and writes
    /// its canonical encoding to <paramref name="destination"/>. Returns
    /// the number of input bytes consumed.
    /// </summary>
    /// <param name="source">The CBOR bytes to canonicalise.</param>
    /// <param name="destination">The buffer writer that receives the canonical bytes.</param>
    /// <param name="pool">Optional memory pool for the reader.</param>
    /// <returns>The byte count consumed from <paramref name="source"/>; equals <c>source.Length</c> for a single top-level item.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <c>null</c>.</exception>
    /// <exception cref="FormatException">The input is not valid CBOR.</exception>
    /// <exception cref="InvalidOperationException">The input contains a CBOR feature not supported by the canonicaliser (see remarks).</exception>
    public static long Canonicalize(
        ReadOnlyMemory<byte> source,
        IBufferWriter<byte> destination,
        Core.Memory.VeritasMemoryPool<byte>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        CborReader reader = new(source, CborSerializerOptions.Default(CborConformanceMode.Lax), pool);
        CborWriter writer = new(destination, CborSerializerOptions.Default(CborConformanceMode.Cde));

        CborValue tree = ReadValue(reader);
        CanonicalEmitter.Emit(tree, writer);
        return reader.BytesConsumedLong;
    }

    /// <summary>
    /// Reads exactly one CBOR data item from <paramref name="reader"/>.
    /// The walk is iterative; containers and tags are built on a frame
    /// stack rather than via method-call recursion.
    /// </summary>
    private static CborValue ReadValue(CborReader reader)
    {
        Stack<BuildFrame> stack = new();
        CborValue? rootResult = null;

        while(rootResult is null)
        {
            //If the top frame is fully populated, close it and attach the
            //resulting CborValue to its parent (or surface as the root).
            if(stack.Count > 0 && stack.Peek().IsComplete)
            {
                BuildFrame completed = stack.Pop();
                CborValue value = completed.Build();
                if(completed.IsContainer)
                {
                    if(completed.IsMap)
                    {
                        reader.ReadEndMap();
                    }
                    else if(completed.IsArray)
                    {
                        reader.ReadEndArray();
                    }
                }
                if(stack.Count == 0)
                {
                    rootResult = value;
                    break;
                }
                AttachToParent(stack.Peek(), value);
                continue;
            }

            CborReaderState state = reader.PeekState();
            switch(state)
            {
                case CborReaderState.UnsignedInteger:
                {
                    ulong raw = reader.ReadUInt64();
                    CborValue leaf = raw <= long.MaxValue
                        ? new CborSignedIntValue((long)raw)
                        : new CborUnsignedIntValue(raw);
                    rootResult = AttachOrReturn(stack, leaf, rootResult);
                    break;
                }
                case CborReaderState.NegativeInteger:
                {
                    //Most negatives fit in Int64; the lowest range
                    //(-2^64 .. -(2^63 + 1)) does not. When ReadInt64
                    //overflows, fall back to the raw representation.
                    CborValue negLeaf;
                    try
                    {
                        negLeaf = new CborSignedIntValue(reader.ReadInt64());
                    }
                    catch(OverflowException)
                    {
                        ulong rawArgument = reader.ReadCborNegativeIntegerRepresentation();
                        negLeaf = new CborLargeNegativeIntValue(rawArgument);
                    }
                    rootResult = AttachOrReturn(stack, negLeaf, rootResult);
                    break;
                }
                case CborReaderState.ByteString:
                {
                    byte[] bytes = reader.ReadByteString();
                    rootResult = AttachOrReturn(stack, new CborByteStringValue(bytes), rootResult);
                    break;
                }
                case CborReaderState.TextString:
                {
                    string text = reader.ReadTextString();
                    rootResult = AttachOrReturn(stack, new CborTextStringValue(text), rootResult);
                    break;
                }
                case CborReaderState.Boolean:
                {
                    bool b = reader.ReadBoolean();
                    rootResult = AttachOrReturn(stack, new CborBoolValue(b), rootResult);
                    break;
                }
                case CborReaderState.Null:
                {
                    reader.ReadNull();
                    rootResult = AttachOrReturn(stack, CborNullValue.Instance, rootResult);
                    break;
                }
                case CborReaderState.HalfPrecisionFloat:
                {
                    Half h = reader.ReadHalf();
                    rootResult = AttachOrReturn(stack, new CborDoubleValue((double)h), rootResult);
                    break;
                }
                case CborReaderState.SinglePrecisionFloat:
                {
                    float f = reader.ReadSingle();
                    rootResult = AttachOrReturn(stack, new CborDoubleValue(f), rootResult);
                    break;
                }
                case CborReaderState.DoublePrecisionFloat:
                {
                    double d = reader.ReadDouble();
                    rootResult = AttachOrReturn(stack, new CborDoubleValue(d), rootResult);
                    break;
                }
                case CborReaderState.StartArray:
                {
                    int? count = reader.ReadStartArray();
                    stack.Push(BuildFrame.Array(count));
                    break;
                }
                case CborReaderState.StartMap:
                {
                    int? count = reader.ReadStartMap();
                    stack.Push(BuildFrame.Map(count));
                    break;
                }
                case CborReaderState.Tag:
                {
                    ulong tagValue = reader.ReadTag().Value;
                    stack.Push(BuildFrame.Tag(tagValue));
                    break;
                }
                case CborReaderState.EndArray:
                case CborReaderState.EndMap:
                {
                    //Indefinite-length container terminator. Mark the top
                    //frame complete; the IsComplete check at the loop top
                    //will dispatch the close.
                    if(stack.Count == 0)
                    {
                        throw new FormatException("CBOR end marker encountered with no open container.");
                    }
                    stack.Peek().SignalEnd();
                    break;
                }
                default:
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture, $"CBOR reader state {state} is not supported by the canonicaliser."));
                }
            }
        }

        return rootResult;
    }

    private static CborValue? AttachOrReturn(Stack<BuildFrame> stack, CborValue value, CborValue? currentRoot)
    {
        if(stack.Count == 0)
        {
            return value;
        }
        AttachToParent(stack.Peek(), value);
        return currentRoot;
    }

    private static void AttachToParent(BuildFrame parent, CborValue child)
    {
        parent.Attach(child);
    }

    /// <summary>
    /// Visitor that walks a <see cref="CborValue"/> tree and emits it
    /// through a <see cref="CborWriter"/>. Implemented iteratively with
    /// an explicit work stack rather than via mutual method-call
    /// recursion, in keeping with the project convention.
    /// </summary>
    private sealed class CanonicalEmitter: ICborValueVisitor
    {
        private readonly CborWriter writer;
        private readonly Stack<EmitFrame> frames = new();

        private CanonicalEmitter(CborWriter writer)
        {
            this.writer = writer;
        }

        public static void Emit(CborValue root, CborWriter writer)
        {
            CanonicalEmitter emitter = new(writer);
            emitter.frames.Push(EmitFrame.Single(root));
            emitter.Run();
        }

        private void Run()
        {
            while(frames.Count > 0)
            {
                EmitFrame top = frames.Peek();
                if(top.HasNext(out CborValue? next) && next is not null)
                {
                    next.Accept(this);
                }
                else
                {
                    //Frame exhausted: close the container it opened.
                    top.Close(writer);
                    frames.Pop();
                }
            }
        }

        public void Visit(CborNullValue value) => writer.WriteNull();
        public void Visit(CborUndefinedValue value) => writer.WriteUndefined();
        public void Visit(CborBoolValue value) => writer.WriteBoolean(value.Value);
        public void Visit(CborSignedIntValue value) => writer.WriteInt64(value.Value);
        public void Visit(CborUnsignedIntValue value) => writer.WriteUInt64(value.Value);
        public void Visit(CborLargeNegativeIntValue value) => writer.WriteCborNegativeIntegerRepresentation(value.RawArgument);
        public void Visit(CborByteStringValue value) => writer.WriteByteString(value.Value);
        public void Visit(CborTextStringValue value) => writer.WriteTextString(value.Value);
        public void Visit(CborDoubleValue value) => writer.WriteDouble(value.Value);

        public void Visit(CborTaggedValue value)
        {
            writer.WriteTag(value.Tag);
            //Push a one-shot frame for the inner value; no close action.
            frames.Push(EmitFrame.Single(value.Inner));
        }

        public void Visit(CborArrayValue value)
        {
            writer.WriteStartArray(value.Items.Count);
            frames.Push(EmitFrame.Array(value));
        }

        public void Visit(CborMapValue value)
        {
            writer.WriteStartMap(value.Entries.Count);
            frames.Push(EmitFrame.Map(value));
        }
    }

    /// <summary>
    /// Work-stack frame for the iterative emitter. Tracks the cursor
    /// position inside a container; <see cref="HasNext"/> yields the
    /// next value to emit, <see cref="Close"/> closes the container.
    /// </summary>
    private sealed class EmitFrame
    {
        private readonly CborValue? singleValue;
        private readonly CborArrayValue? array;
        private readonly CborMapValue? map;
        private int cursor;
        private bool emittingSingle;

        private EmitFrame(CborValue? single, CborArrayValue? array, CborMapValue? map)
        {
            singleValue = single;
            this.array = array;
            this.map = map;
            cursor = 0;
            emittingSingle = single is not null;
        }

        public static EmitFrame Single(CborValue value) => new(value, null, null);
        public static EmitFrame Array(CborArrayValue array) => new(null, array, null);
        public static EmitFrame Map(CborMapValue map) => new(null, null, map);

        public bool HasNext(out CborValue? next)
        {
            if(emittingSingle)
            {
                emittingSingle = false;
                next = singleValue;
                return true;
            }
            if(array is not null)
            {
                if(cursor < array.Items.Count)
                {
                    next = array.Items[cursor++];
                    return true;
                }
                next = null;
                return false;
            }
            if(map is not null)
            {
                int half = cursor / 2;
                if(half < map.Entries.Count)
                {
                    KeyValuePair<CborValue, CborValue> entry = map.Entries[half];
                    next = (cursor & 1) == 0 ? entry.Key : entry.Value;
                    cursor++;
                    return true;
                }
                next = null;
                return false;
            }
            next = null;
            return false;
        }

        public void Close(CborWriter writer)
        {
            if(array is not null)
            {
                writer.WriteEndArray();
            }
            else if(map is not null)
            {
                writer.WriteEndMap();
            }
            //Single-value frames need no close.
        }
    }

    /// <summary>
    /// Work-stack frame for the iterative DOM builder. Holds the
    /// in-progress container; <see cref="IsComplete"/> reports when the
    /// expected number of children has been collected.
    /// </summary>
    private sealed class BuildFrame
    {
        private readonly int expectedCount;
        private readonly List<CborValue>? arrayItems;
        private readonly List<KeyValuePair<CborValue, CborValue>>? mapEntries;
        private readonly ulong tagValue;
        private readonly bool isTag;
        private CborValue? tagInner;
        private CborValue? pendingMapKey;
        private bool endSignaled;

        private BuildFrame(int expected, bool isArray, bool isMap, bool isTag, ulong tagValue)
        {
            expectedCount = expected;
            arrayItems = isArray ? new List<CborValue>(expected >= 0 ? expected : 0) : null;
            mapEntries = isMap ? new List<KeyValuePair<CborValue, CborValue>>(expected >= 0 ? expected : 0) : null;
            this.isTag = isTag;
            this.tagValue = tagValue;
        }

        public static BuildFrame Array(int? count) => new(count ?? -1, true, false, false, 0);
        public static BuildFrame Map(int? count) => new(count ?? -1, false, true, false, 0);
        public static BuildFrame Tag(ulong tag) => new(1, false, false, true, tag);

        public bool IsContainer => arrayItems is not null || mapEntries is not null;
        public bool IsArray => arrayItems is not null;
        public bool IsMap => mapEntries is not null;

        public bool IsComplete
        {
            get
            {
                if(isTag)
                {
                    return tagInner is not null;
                }
                if(arrayItems is not null)
                {
                    if(expectedCount < 0)
                    {
                        return endSignaled;
                    }
                    return arrayItems.Count == expectedCount;
                }
                if(mapEntries is not null)
                {
                    if(expectedCount < 0)
                    {
                        return endSignaled && pendingMapKey is null;
                    }
                    return mapEntries.Count == expectedCount && pendingMapKey is null;
                }
                return false;
            }
        }

        public void SignalEnd()
        {
            endSignaled = true;
        }

        public void Attach(CborValue value)
        {
            if(isTag)
            {
                tagInner = value;
                return;
            }
            if(arrayItems is not null)
            {
                arrayItems.Add(value);
                return;
            }
            if(mapEntries is not null)
            {
                if(pendingMapKey is null)
                {
                    pendingMapKey = value;
                }
                else
                {
                    mapEntries.Add(new KeyValuePair<CborValue, CborValue>(pendingMapKey, value));
                    pendingMapKey = null;
                }
                return;
            }
            throw new InvalidOperationException("BuildFrame.Attach called on a non-container frame.");
        }

        public CborValue Build()
        {
            if(isTag)
            {
                return new CborTaggedValue(tagValue, tagInner!);
            }
            if(arrayItems is not null)
            {
                return new CborArrayValue(arrayItems);
            }
            if(mapEntries is not null)
            {
                return new CborMapValue(mapEntries);
            }
            throw new InvalidOperationException("BuildFrame.Build called on an unrecognised frame.");
        }
    }
}
