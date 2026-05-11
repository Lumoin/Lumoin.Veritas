using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Decodes a CBOR-LD document. Reads the outer tag (<c>0xCB1D</c>),
/// extracts the registry entry id, resolves it through the supplied
/// <see cref="LoadCborLdRegistryEntryDelegate"/>, and decodes the payload
/// into a <see cref="CborLdInputNode"/> tree.
/// </summary>
/// <remarks>
/// <para>
/// The walker is iterative — a <see cref="Stack{T}"/> of frames per the
/// project's no-recursion rule. The frame stack matches the wire-form
/// container nesting depth; decoding is linear in the number of nodes.
/// </para>
/// <para>
/// Compression-driven registry entries (term-to-id, keyword-to-id) are a
/// follow-up; this decoder rejects non-passthrough entries.
/// </para>
/// </remarks>
public static class CborLdDecoder
{
    private const ulong CborLdOuterTag = 51997;

    /// <summary>
    /// Decodes <paramref name="bytes"/> as a CBOR-LD document, resolving
    /// the registry entry id via <paramref name="registryLoader"/>.
    /// </summary>
    /// <param name="bytes">The wire bytes to decode.</param>
    /// <param name="registryLoader">The registry resolver. Receives the integer id from the wire and returns the matching entry.</param>
    /// <param name="cancellationToken">A token to cancel the registry lookup.</param>
    /// <returns>The decoded registry-entry id and the document tree.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registryLoader"/> is <c>null</c>.</exception>
    /// <exception cref="CborLdProcessingException">The bytes are not CBOR-LD, the registry entry is unknown, or the entry requires unimplemented semantic decompression.</exception>
    public static async ValueTask<CborLdDecodeResult> DecodeAsync(
        ReadOnlyMemory<byte> bytes,
        LoadCborLdRegistryEntryDelegate registryLoader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registryLoader);

        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax));

        if(reader.PeekState() != CborReaderState.Tag)
        {
            throw new CborLdProcessingException("CBOR-LD input must begin with a CBOR tag.");
        }

        CborTag tag = reader.ReadTag();
        if(tag.Value != CborLdOuterTag)
        {
            throw new CborLdProcessingException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR-LD outer tag must be {CborLdOuterTag}; got {tag.Value}."));
        }

        int? arrayCount = reader.ReadStartArray();
        if(arrayCount != 2)
        {
            throw new CborLdProcessingException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR-LD payload must be a 2-element array; got count {arrayCount?.ToString(CultureInfo.InvariantCulture) ?? "indefinite"}."));
        }

        long registryEntryIdSigned = reader.ReadInt64();
        if(registryEntryIdSigned is < int.MinValue or > int.MaxValue)
        {
            throw new CborLdProcessingException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR-LD registry entry id {registryEntryIdSigned} does not fit in Int32."));
        }
        int registryEntryId = (int)registryEntryIdSigned;

        CborLdRegistryEntry? entry = await registryLoader(registryEntryId, cancellationToken).ConfigureAwait(false);
        if(entry is null)
        {
            throw new CborLdProcessingException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR-LD registry loader returned no entry for id {registryEntryId}."));
        }

        if(entry.Keywords.Count > 0 || entry.Terms.Count > 0)
        {
            throw new CborLdProcessingException(
                "Semantic decompression registry entries are not yet implemented; only passthrough entries (empty keyword/term codec tables) are supported.");
        }

        CborLdInputNode root = ReadPayload(reader);
        reader.ReadEndArray();

        if(reader.PeekState() != CborReaderState.Finished)
        {
            throw new CborLdProcessingException("CBOR-LD wire form contains trailing bytes after the outer array.");
        }

        return new CborLdDecodeResult(registryEntryId, root);
    }

    private static CborLdInputNode ReadPayload(CborReader reader)
    {
        Stack<DecodeFrame> frames = new();
        CborLdInputNode? rootResult = null;

        while(rootResult is null)
        {
            //Close any completed container frames before reading the next item.
            if(frames.Count > 0 && frames.Peek().IsComplete)
            {
                DecodeFrame completed = frames.Pop();
                CborLdInputNode container = completed.Build();
                if(completed.IsMap)
                {
                    reader.ReadEndMap();
                }
                else
                {
                    reader.ReadEndArray();
                }
                if(frames.Count == 0)
                {
                    rootResult = container;
                    break;
                }
                AttachToTop(frames.Peek(), container);
                continue;
            }

            CborReaderState state = reader.PeekState();
            switch(state)
            {
                case CborReaderState.UnsignedInteger:
                case CborReaderState.NegativeInteger:
                {
                    CborLdInputNode leaf = new CborLdInputInt(reader.ReadInt64());
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.TextString:
                {
                    string text = reader.ReadTextString();
                    if(frames.Count > 0 && frames.Peek().IsMap && frames.Peek().PendingKey is null)
                    {
                        frames.Peek().PendingKey = text;
                        break;
                    }
                    CborLdInputNode leaf = new CborLdInputString(text);
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.ByteString:
                {
                    //Passthrough form does not currently distinguish byte
                    //strings; surface them as strings so callers can detect
                    //and re-encode if needed. A richer model is a future
                    //extension.
                    byte[] bytes = reader.ReadByteString();
                    CborLdInputNode leaf = new CborLdInputString(Convert.ToHexString(bytes));
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.Null:
                {
                    reader.ReadNull();
                    rootResult = AttachLeafOrReturnRoot(frames, CborLdInputNull.Instance, rootResult);
                    break;
                }
                case CborReaderState.Boolean:
                {
                    CborLdInputNode leaf = new CborLdInputBool(reader.ReadBoolean());
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.DoublePrecisionFloat:
                {
                    CborLdInputNode leaf = new CborLdInputDouble(reader.ReadDouble());
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.SinglePrecisionFloat:
                {
                    CborLdInputNode leaf = new CborLdInputDouble(reader.ReadSingle());
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.HalfPrecisionFloat:
                {
                    CborLdInputNode leaf = new CborLdInputDouble((double)reader.ReadHalf());
                    rootResult = AttachLeafOrReturnRoot(frames, leaf, rootResult);
                    break;
                }
                case CborReaderState.StartArray:
                {
                    int? count = reader.ReadStartArray();
                    if(count is null)
                    {
                        throw new CborLdProcessingException("CBOR-LD passthrough payload does not allow indefinite-length arrays.");
                    }
                    frames.Push(new DecodeFrame(isMap: false, expectedCount: count.Value));
                    break;
                }
                case CborReaderState.StartMap:
                {
                    int? count = reader.ReadStartMap();
                    if(count is null)
                    {
                        throw new CborLdProcessingException("CBOR-LD passthrough payload does not allow indefinite-length maps.");
                    }
                    frames.Push(new DecodeFrame(isMap: true, expectedCount: count.Value));
                    break;
                }
                default:
                {
                    throw new CborLdProcessingException(
                        string.Create(CultureInfo.InvariantCulture, $"CBOR-LD passthrough payload does not handle reader state {state}."));
                }
            }
        }

        return rootResult;
    }

    private static CborLdInputNode? AttachLeafOrReturnRoot(
        Stack<DecodeFrame> frames,
        CborLdInputNode leaf,
        CborLdInputNode? currentRoot)
    {
        if(frames.Count == 0)
        {
            return leaf;
        }
        AttachToTop(frames.Peek(), leaf);
        return currentRoot;
    }

    private static void AttachToTop(DecodeFrame frame, CborLdInputNode child)
    {
        if(frame.IsMap)
        {
            string key = frame.PendingKey
                ?? throw new CborLdProcessingException("CBOR-LD map value without a preceding key; the wire form is corrupt.");
            frame.Entries.Add(new KeyValuePair<string, CborLdInputNode>(key, child));
            frame.PendingKey = null;
        }
        else
        {
            frame.Items.Add(child);
        }
    }

    private sealed class DecodeFrame
    {
        public DecodeFrame(bool isMap, int expectedCount)
        {
            IsMap = isMap;
            ExpectedCount = expectedCount;
            Items = isMap ? null! : new List<CborLdInputNode>(expectedCount);
            Entries = isMap ? new List<KeyValuePair<string, CborLdInputNode>>(expectedCount) : null!;
        }

        public bool IsMap { get; }

        public int ExpectedCount { get; }

        public List<CborLdInputNode> Items { get; }

        public List<KeyValuePair<string, CborLdInputNode>> Entries { get; }

        public string? PendingKey { get; set; }

        public bool IsComplete => IsMap
            ? Entries.Count == ExpectedCount && PendingKey is null
            : Items.Count == ExpectedCount;

        public CborLdInputNode Build()
        {
            return IsMap
                ? new CborLdInputMap(Entries)
                : new CborLdInputArray(Items);
        }
    }
}

/// <summary>
/// The output of <see cref="CborLdDecoder.DecodeAsync"/>: the resolved
/// registry-entry id and the decoded document tree.
/// </summary>
/// <param name="RegistryEntryId">The wire-form registry entry id.</param>
/// <param name="Root">The decoded document tree.</param>
public readonly record struct CborLdDecodeResult(int RegistryEntryId, CborLdInputNode Root);
