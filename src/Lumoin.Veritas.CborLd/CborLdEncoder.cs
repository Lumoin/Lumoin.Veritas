using System;
using System.Buffers;
using System.Collections.Generic;

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
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="CborLdProcessingException">A registry entry other than passthrough is used; semantic compression is not yet implemented.</exception>
    public static void Encode(
        CborLdInputNode root,
        CborLdRegistryEntry registryEntry,
        CborLdProfile profile,
        IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registryEntry);
        ArgumentNullException.ThrowIfNull(destination);

        if(registryEntry.Keywords.Count > 0 || registryEntry.Terms.Count > 0)
        {
            throw new CborLdProcessingException(
                "Semantic compression registry entries are not yet implemented; only passthrough entries (empty keyword/term codec tables) are supported. Use CborLdRegistryEntry.Passthrough.");
        }

        CborConformanceMode mode = profile == CborLdProfile.Deterministic
            ? CborConformanceMode.Cde
            : CborConformanceMode.Lax;
        CborWriter writer = new(destination, CborSerializerOptions.Default(mode));

        //Outer wrapper: tag(51997, [registryEntryId, payload])
        writer.WriteTag(CborLdOuterTag);
        writer.WriteStartArray(2);
        writer.WriteInt32(registryEntry.RegistryEntryId);
        WritePayload(writer, root);
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
