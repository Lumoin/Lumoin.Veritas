using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd.Internal;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd;


/// <summary>
/// Encodes a CBOR-LD document. The wire form per the W3C CBOR-LD 1.0
/// specification is <c>tag(51997, [registryEntryId, payload])</c> where
/// the outer tag <c>0xCB1D</c> (decimal <c>51997</c>) identifies the
/// payload as CBOR-LD and the registry entry id selects a (possibly
/// compressed) interpretation of the payload bytes.
/// </summary>
/// <remarks>
/// <para>
/// The current implementation supports the passthrough mode: registry
/// entry <c>0</c> (or any other id that resolves to an entry with empty
/// codec tables) encodes the input as CBOR primitives unchanged.
/// Compression-driven registry entries (term-to-id, keyword-to-id) are a
/// follow-up.
/// </para>
/// <para>
/// The walker is iterative — a <see cref="Stack{T}"/> of frames per the
/// project's no-recursion rule. The frame stack matches the input tree's
/// nesting depth; encoding is linear in the number of nodes.
/// </para>
/// </remarks>
public static class CborLdEncoder
{
    private const ulong CborLdOuterTag = 51997;

    /// <summary>
    /// Encodes <paramref name="root"/> into <paramref name="destination"/>
    /// using the supplied <paramref name="registryEntry"/> and
    /// <paramref name="profile"/>.
    /// </summary>
    /// <param name="root">The input tree. Must not be <c>null</c>.</param>
    /// <param name="registryEntry">The registry entry whose id is emitted in the wire form. Passthrough uses <see cref="CborLdRegistryEntry.Passthrough"/>.</param>
    /// <param name="profile">The encoding profile.</param>
    /// <param name="destination">The output buffer writer.</param>
    /// <param name="pool">Optional pool used by typed-value codecs to rent
    /// transient wire buffers. Falls back to <see cref="MemoryPool{T}.Shared"/>
    /// when <c>null</c>.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
    /// <exception cref="CborLdProcessingException">An embedded inline <c>@context</c> failed validation against the active-context spec rules. Remote-context resolution is not performed in this overload; use
    /// <see cref="EncodeWithRemoteContextsAsync(CborLdInputNode, CborLdRegistryEntry, CborLdProfile, IBufferWriter{byte}, FetchRemoteResourceDelegate, ParseRemoteContextDelegate, ProbeContextCacheDelegate, CborLdCallerProvidedTypeTables, MemoryPool{byte}, CancellationToken)"/>
    /// when remote contexts must be resolved.</exception>
    /// <returns>A task that completes when the document has been written.</returns>
    public static async ValueTask EncodeAsync(
        CborLdInputNode root,
        CborLdRegistryEntry registryEntry,
        CborLdProfile profile,
        IBufferWriter<byte> destination,
        CborLdCallerProvidedTypeTables? callerTables = null,
        MemoryPool<byte>? pool = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registryEntry);
        ArgumentNullException.ThrowIfNull(destination);

        CborConformanceMode mode = profile == CborLdProfile.Deterministic
            ? CborConformanceMode.Cde
            : CborConformanceMode.Lax;
        CborWriter writer = new(destination, CborSerializerOptions.Default(mode));

        //Outer wrapper: tag(51997, [registryEntryId, payload])
        writer.WriteTag(CborLdOuterTag);
        writer.WriteStartArray(2);
        writer.WriteInt32(registryEntry.RegistryEntryId);

        bool isCompressed = registryEntry.Keywords.Count > 0 || registryEntry.Terms.Count > 0;
        if(isCompressed)
        {
            //E3 wires active-context state through the compression walk,
            //which is the canonical validation path: spec violations in any
            //embedded @context surface through ContextProcessing as
            //CborLdProcessingException during encoding. Passthrough mode
            //emits the document as opaque CBOR primitives per W3C CBOR-LD
            //1.0 registry-entry 0 semantics; no validation is performed.
            await CborLdCompressionEncoder.WritePayloadAsync(
                writer, root, registryEntry, profile, pool ?? MemoryPool<byte>.Shared,
                fetcher: null, parser: null, cache: null, callerTables: callerTables).ConfigureAwait(false);
        }
        else
        {
            WritePayload(writer, root);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Encodes <paramref name="root"/> with active-context validation
    /// that may resolve remote <c>@context</c> URLs via the supplied
    /// delegate trio. The delegates follow the same contract as the
    /// JsonLd shell: <paramref name="fetcher"/> retrieves raw bytes,
    /// <paramref name="parser"/> converts to the format-neutral dict
    /// shape, <paramref name="cache"/> (optional) short-circuits the
    /// fetcher.
    /// </summary>
    /// <param name="root">The input tree.</param>
    /// <param name="registryEntry">The registry entry whose id is emitted in the wire form.</param>
    /// <param name="profile">The encoding profile.</param>
    /// <param name="destination">The output buffer writer.</param>
    /// <param name="fetcher">Delegate that fetches remote-context resources.</param>
    /// <param name="parser">Delegate that parses fetched remote-context bytes.</param>
    /// <param name="cache">Optional cache probe consulted before <paramref name="fetcher"/>.</param>
    /// <param name="callerTables">Caller-provided type tables; required only when <paramref name="registryEntry"/> declares one or more type tables as <c>"callerProvidedTable"</c>.</param>
    /// <param name="pool">Optional pool used by typed-value codecs.</param>
    /// <param name="cancellationToken">A token to cancel remote-context I/O.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <c>null</c>.</exception>
    /// <exception cref="CborLdProcessingException">An embedded <c>@context</c> failed validation, remote-context resolution failed, or the registry entry declares a caller-provided type table for which no table was supplied.</exception>
    /// <returns>A task that completes when the document has been written.</returns>
    public static async ValueTask EncodeWithRemoteContextsAsync(
        CborLdInputNode root,
        CborLdRegistryEntry registryEntry,
        CborLdProfile profile,
        IBufferWriter<byte> destination,
        FetchRemoteResourceDelegate fetcher,
        ParseRemoteContextDelegate parser,
        ProbeContextCacheDelegate? cache,
        CborLdCallerProvidedTypeTables? callerTables,
        MemoryPool<byte>? pool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registryEntry);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(parser);

        CborConformanceMode mode = profile == CborLdProfile.Deterministic
            ? CborConformanceMode.Cde
            : CborConformanceMode.Lax;
        CborWriter writer = new(destination, CborSerializerOptions.Default(mode));

        writer.WriteTag(CborLdOuterTag);
        writer.WriteStartArray(2);
        writer.WriteInt32(registryEntry.RegistryEntryId);

        bool isCompressed = registryEntry.Keywords.Count > 0 || registryEntry.Terms.Count > 0;
        if(isCompressed)
        {
            await CborLdCompressionEncoder.WritePayloadAsync(
                writer, root, registryEntry, profile, pool ?? MemoryPool<byte>.Shared,
                fetcher, parser, cache, callerTables, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            WritePayload(writer, root);
        }

        writer.WriteEndArray();
    }

    private static void WritePayload(CborWriter writer, CborLdInputNode root)
    {
        Stack<EncodeFrame> stack = new();
        stack.Push(new EncodeFrame(root));

        while(stack.Count > 0)
        {
            EncodeFrame frame = stack.Peek();
            switch(frame.Node)
            {
                case CborLdInputNull:
                {
                    writer.WriteNull();
                    stack.Pop();
                    break;
                }
                case CborLdInputBool b:
                {
                    writer.WriteBoolean(b.Value);
                    stack.Pop();
                    break;
                }
                case CborLdInputInt i:
                {
                    writer.WriteInt64(i.Value);
                    stack.Pop();
                    break;
                }
                case CborLdInputDouble d:
                {
                    writer.WriteDouble(d.Value);
                    stack.Pop();
                    break;
                }
                case CborLdInputString s:
                {
                    writer.WriteTextString(s.Value);
                    stack.Pop();
                    break;
                }
                case CborLdInputBytes b:
                {
                    writer.WriteByteString(b.Value.Span);
                    stack.Pop();
                    break;
                }
                case CborLdInputArray a:
                {
                    if(!frame.HeaderEmitted)
                    {
                        writer.WriteStartArray(a.Items.Count);
                        frame.HeaderEmitted = true;
                    }
                    if(frame.NextIndex < a.Items.Count)
                    {
                        int index = frame.NextIndex++;
                        stack.Push(new EncodeFrame(a.Items[index]));
                    }
                    else
                    {
                        writer.WriteEndArray();
                        stack.Pop();
                    }
                    break;
                }
                case CborLdInputMap m:
                {
                    if(!frame.HeaderEmitted)
                    {
                        writer.WriteStartMap(m.Entries.Count);
                        frame.HeaderEmitted = true;
                    }
                    if(frame.NextIndex < m.Entries.Count)
                    {
                        KeyValuePair<string, CborLdInputNode> entry = m.Entries[frame.NextIndex];
                        writer.WriteTextString(entry.Key);
                        frame.NextIndex++;
                        stack.Push(new EncodeFrame(entry.Value));
                    }
                    else
                    {
                        writer.WriteEndMap();
                        stack.Pop();
                    }
                    break;
                }
                default:
                {
                    throw new CborLdProcessingException(
                        $"Unhandled CborLdInputNode subtype: {frame.Node.GetType().Name}");
                }
            }
        }
    }

    private sealed class EncodeFrame
    {
        public EncodeFrame(CborLdInputNode node)
        {
            Node = node;
        }

        public CborLdInputNode Node { get; }

        public bool HeaderEmitted { get; set; }

        public int NextIndex { get; set; }
    }
}
