using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd.Internal;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.LinkedData;

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
    public static ValueTask<CborLdDecodeResult> DecodeAsync(
        ReadOnlyMemory<byte> bytes,
        LoadCborLdRegistryEntryDelegate registryLoader,
        CborLdCallerProvidedTypeTables? callerTables = null,
        VeritasMemoryPool<byte>? pool = null,
        CancellationToken cancellationToken = default)
    {
        return DecodeCoreAsync(
            bytes, registryLoader, fetcher: null, parser: null, cache: null, callerTables, pool, cancellationToken);
    }

    private static async ValueTask<CborLdDecodeResult> DecodeCoreAsync(
        ReadOnlyMemory<byte> bytes,
        LoadCborLdRegistryEntryDelegate registryLoader,
        FetchRemoteResourceDelegate? fetcher,
        ParseRemoteContextDelegate? parser,
        ProbeContextCacheDelegate? cache,
        CborLdCallerProvidedTypeTables? callerTables,
        VeritasMemoryPool<byte>? pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registryLoader);

        CborReader reader = new(bytes, CborSerializerOptions.Default(CborConformanceMode.Lax), pool);

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

        bool isCompressed = entry.Keywords.Count > 0 || entry.Terms.Count > 0;
        //The decoder presently has no profile signal on the wire; pass
        //Default for the matcher context. A future enhancement can carry
        //profile metadata in the outer tag for §5.2.5.1 deterministic decode.
        CborLdInputNode root = isCompressed
            ? await CborLdCompressionDecoder.ReadPayloadAsync(reader, entry, CborLdProfile.Default, fetcher, parser, cache, callerTables, cancellationToken).ConfigureAwait(false)
            : ReadPayload(reader);
        reader.ReadEndArray();

        if(reader.PeekState() != CborReaderState.Finished)
        {
            throw new CborLdProcessingException("CBOR-LD wire form contains trailing bytes after the outer array.");
        }

        //E3: spec-level @context validation now happens inline during the
        //compressed-mode walk above (via ContextProcessing called from
        //CborLdActiveContextScope). Passthrough emits/reads the document as
        //opaque CBOR per W3C CBOR-LD 1.0 registry-entry 0 semantics, so no
        //post-pass validation is performed in that mode.
        return new CborLdDecodeResult(registryEntryId, root);
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> as a CBOR-LD document and runs
    /// active-context validation with remote-context resolution through
    /// the supplied delegate trio. Use this overload when the document
    /// may contain URL <c>@context</c> entries that must be validated
    /// against their dereferenced inline content.
    /// </summary>
    /// <param name="bytes">The wire bytes to decode.</param>
    /// <param name="registryLoader">The registry resolver.</param>
    /// <param name="fetcher">Delegate that fetches remote-context resources.</param>
    /// <param name="parser">Delegate that parses fetched bytes to the format-neutral dict shape.</param>
    /// <param name="cache">Optional cache probe.</param>
    /// <param name="callerTables">Caller-provided type tables; required only when the registry entry declares one or more type tables as <c>"callerProvidedTable"</c>.</param>
    /// <param name="pool">Optional memory pool for the inner CBOR reader.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decoded registry-entry id and document tree.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="CborLdProcessingException">Decoding or active-context validation failed, or the registry entry declares a caller-provided type table for which no table was supplied.</exception>
    public static ValueTask<CborLdDecodeResult> DecodeWithRemoteContextsAsync(
        ReadOnlyMemory<byte> bytes,
        LoadCborLdRegistryEntryDelegate registryLoader,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        CborLdCallerProvidedTypeTables? callerTables,
        VeritasMemoryPool<byte>? pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registryLoader);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(parser);

        //The fetcher/parser/cache delegates now flow through the inline
        //compression walk to CborLdActiveContextScope, which dereferences
        //URL @contexts on demand. No separate post-pass.
        return DecodeCoreAsync(
            bytes, registryLoader, fetcher, parser, cache, callerTables, pool, cancellationToken);
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
                    //Zero-copy slice of the source memory; the decoded
                    //CborLdInputBytes aliases the caller-supplied bytes
                    //and is valid for as long as the caller retains them.
                    ReadOnlyMemory<byte> bytes = reader.ReadByteStringMemory();
                    CborLdInputNode leaf = new CborLdInputBytes(bytes);
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
        //Bounded preallocation: do not trust the wire-declared count for
        //capacity. The list grows naturally; the CborReader's MaxArrayLength
        /// MaxMapEntryCount caps already bound the count itself.
        private const int SafePreallocationCap = 64;

        public DecodeFrame(bool isMap, int expectedCount)
        {
            IsMap = isMap;
            ExpectedCount = expectedCount;
            int safeCapacity = expectedCount > SafePreallocationCap ? SafePreallocationCap : expectedCount;
            Items = isMap ? null! : new List<CborLdInputNode>(safeCapacity);
            Entries = isMap ? new List<KeyValuePair<string, CborLdInputNode>>(safeCapacity) : null!;
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
