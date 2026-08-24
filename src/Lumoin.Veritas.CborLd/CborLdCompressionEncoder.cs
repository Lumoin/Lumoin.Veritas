using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd.Internal;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Implements compression-side encoding for CBOR-LD. Substitutes keywords
/// and terms with their compressed integer ids per W3C CBOR-LD 1.0
/// §5.2.5.3, including the singular/plural distinction in which an
/// array-valued entry uses <c>id + 1</c> while a single-valued entry uses
/// <c>id</c>. Keys not present in the registry's keyword or term codec
/// dictionaries fall through to dynamic ids assigned by the active context
/// (W3C CBOR-LD 1.0 §5.3), then to text-string keys when no context-defined
/// term matches either (spec §5.2.5.3 step 3.4). Typed values (terms whose
/// <see cref="CborLdTermCodec.Type"/> is non-null) are dispatched through
/// <see cref="CborLdTypedValueCodecs.ResolveEncoder"/>.
/// </summary>
internal static class CborLdCompressionEncoder
{
    public static async ValueTask WritePayloadAsync(
        CborWriter writer,
        CborLdInputNode root,
        CborLdRegistryEntry registryEntry,
        CborLdProfile profile,
        MemoryPool<byte> pool,
        FetchRemoteResourceDelegate? fetcher = null,
        ParseRemoteContextDelegate? parser = null,
        ProbeContextCacheDelegate? cache = null,
        CborLdCallerProvidedTypeTables? callerTables = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, int> nameToId = BuildNameToId(registryEntry);
        CborLdMatcherContext matcherContext = BuildMatcherContext(registryEntry, profile);
        CborLdCallerProvidedTypeTables effectiveCallerTables = callerTables ?? CborLdCallerProvidedTypeTables.Empty;

        CborLdConversionState state = new()
        {
            NextTermId = ComputeNextDynamicId(registryEntry)
        };
        CborLdActiveContextScope scope = new(fetcher, parser, cache);

        Stack<EncodeFrame> stack = new();
        stack.Push(new EncodeFrame(
            root,
            parentTermType: null,
            activeContext: LinkedDataContext.Empty,
            preEmbedded: LinkedDataContext.Empty));

        while(stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EncodeFrame frame = stack.Peek();

            //If this frame carries a typed value, dispatch via the codec
            //registry once and treat the frame as a leaf.
            if(frame.ParentTermType is not null && !frame.HeaderEmitted)
            {
                EmitTypedValue(writer, frame.Node, frame.ParentTermType, registryEntry, matcherContext, effectiveCallerTables, pool);
                stack.Pop();
                continue;
            }

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
                case CborLdInputBytes by:
                {
                    writer.WriteByteString(by.Value.Span);
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
                        //Array items inherit the parent's active context; arrays
                        //themselves do not carry @context per spec.
                        stack.Push(new EncodeFrame(
                            a.Items[index],
                            parentTermType: null,
                            activeContext: frame.ActiveContext,
                            preEmbedded: frame.PreEmbedded));
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
                        //E3: discover @context and @type before emitting
                        //the map header so subsequent key-id lookups see the
                        //updated state.TermToId. Clarifications 1, 3, 5:
                        //embedded → type-scoped, eager id assignment, @context
                        //value encoded in pre-embedded. The decoder reads
                        //sequentially and can only resolve dynamic ids after
                        //it has seen the @context that defined them, so the
                        //wire form must emit @context first regardless of
                        //its position in the input. We compute an emission
                        //order that places @context first when present.
                        frame.PreEmbedded = frame.ActiveContext;
                        frame.ActiveContext = await ApplyMapScopesAsync(
                            m, frame.ActiveContext, scope, state, cancellationToken).ConfigureAwait(false);
                        frame.EmissionOrder = BuildEmissionOrder(m);

                        writer.WriteStartMap(m.Entries.Count);
                        frame.HeaderEmitted = true;
                    }
                    if(frame.NextIndex < frame.EmissionOrder.Length)
                    {
                        int actualIndex = frame.EmissionOrder[frame.NextIndex];
                        KeyValuePair<string, CborLdInputNode> entry = m.Entries[actualIndex];

                        //Key emission consults registry first then dynamic ids
                        //(clarification 1; AssignTermId already populated state.TermToId).
                        //The frame's ActiveContext gates dynamic-id eligibility: a
                        //term must be visible in this frame's context, not just in
                        //the shared state.TermToId, so @context-value frames don't
                        //compress definitions back to their freshly-allocated ids.
                        WriteMapKey(writer, entry.Key, entry.Value, nameToId, frame.ActiveContext, state.TermToId);
                        frame.NextIndex++;

                        //Per-child context selection:
                        // - @context value: encode in pre-embedded (clarification 5)
                        // - other values: descendant context (gated by Propagate, clarification 2)
                        //   plus property-scoped overlay when the term carries ScopedContextEntries
                        LinkedDataContext childActiveContext = await SelectChildContextAsync(
                            entry.Key, entry.Value, frame, scope, state, cancellationToken).ConfigureAwait(false);

                        //Typed-value propagation: registry term codec wins,
                        //then the active context's TermDefinition&lt;TNode&gt;.TypeMapping
                        //covers dynamically-defined typed terms (e.g.
                        //{"birthDate": {"@id": "...", "@type": "xsd:date"}}).
                        string? typeName = ResolveTypeName(entry.Key, frame.ActiveContext, registryEntry);

                        stack.Push(new EncodeFrame(
                            entry.Value,
                            parentTermType: typeName,
                            activeContext: childActiveContext,
                            preEmbedded: childActiveContext));
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

    /// <summary>
    /// Applies the embedded <c>@context</c> entry (if present) and any
    /// type-scoped contexts triggered by an <c>@type</c> entry in the map,
    /// in spec order. Returns the post-application context. Dynamic ids
    /// are eagerly assigned inside the scope helper.
    /// </summary>
    private static async ValueTask<LinkedDataContext> ApplyMapScopesAsync(
        CborLdInputMap map,
        LinkedDataContext current,
        CborLdActiveContextScope scope,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        LinkedDataContext running = current;

        //Step 1: embedded @context.
        CborLdInputNode? contextNode = TryFindEntry(map, "@context");
        if(contextNode is not null)
        {
            running = await scope.WithEmbeddedContextAsync(
                running, contextNode, baseUrl: null, state, cancellationToken)
                .ConfigureAwait(false);
        }

        //Step 2: type-scoped via @type (after embedded). The values may be
        //a single string or an array of strings; both shapes carry IRIs
        //(after expansion against `running` — here we assume the input
        //tree already carries fully-expanded IRIs at @type, which matches
        //the encoder's current shape contract).
        CborLdInputNode? typeNode = TryFindEntry(map, "@type");
        if(typeNode is not null)
        {
            string[] typeIris = ExtractTypeIris(typeNode);
            if(typeIris.Length > 0)
            {
                running = await scope.WithTypeScopedAsync(
                    running, typeIris, baseUrl: null, state, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return running;
    }

    /// <summary>
    /// Computes the active context that applies to a child value frame.
    /// Encodes the @context-value-in-pre-embedded rule (clarification 5)
    /// and the propagate gating (clarification 2). Property-scoped
    /// overlay applies on top.
    /// </summary>
    private static async ValueTask<LinkedDataContext> SelectChildContextAsync(
        string key,
        CborLdInputNode value,
        EncodeFrame parent,
        CborLdActiveContextScope scope,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        _ = value;

        //@context value is always encoded in the pre-embedded context.
        if(key == "@context")
        {
            return parent.PreEmbedded;
        }

        //Descendants of this map see the post-embedded context when it
        //propagates; otherwise they see the pre-embedded context.
        LinkedDataContext descendant = parent.ActiveContext.Propagate
            ? parent.ActiveContext
            : parent.PreEmbedded;

        //Property-scoped overlay: if this property's term carries
        //ScopedContextEntries, apply them on top of the descendant context.
        if(parent.ActiveContext.TryGetTerm(key, out TermDefinition? def)
            && def is { ScopedContextEntries: not null })
        {
            descendant = await scope.WithPropertyScopedAsync(
                descendant, def, baseUrl: null, state, cancellationToken)
                .ConfigureAwait(false);
        }

        return descendant;
    }

    private static CborLdInputNode? TryFindEntry(CborLdInputMap map, string key)
    {
        foreach(KeyValuePair<string, CborLdInputNode> entry in map.Entries)
        {
            if(entry.Key == key)
            {
                return entry.Value;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the typed-value type name for <paramref name="key"/>, preferring
    /// a registry term codec when one is registered and falling back to the
    /// active context's <see cref="TermDefinition.TypeMapping"/>. JSON-LD
    /// type-coercion keywords <c>@id</c> and <c>@vocab</c> are filtered out:
    /// those instruct expansion behaviour, not CBOR-LD typed-value codec
    /// dispatch. Returns <c>null</c> when the term is untyped (or not
    /// registered) so the caller emits the value as a plain CBOR primitive.
    /// </summary>
    private static string? ResolveTypeName(string key, LinkedDataContext active, CborLdRegistryEntry registry)
    {
        if(registry.Terms.TryGetValue(key, out CborLdTermCodec codec))
        {
            return codec.Type;
        }

        if(active.TryGetTerm(key, out TermDefinition? def)
            && def is { TypeMapping: { } typeMapping }
            && !IriUtils.IsKeyword(typeMapping))
        {
            return typeMapping;
        }

        return null;
    }

    /// <summary>
    /// Returns the indices of <paramref name="map"/>'s entries in wire
    /// emission order: <c>@context</c> first when present, then <c>@type</c>
    /// second when present, then all other entries in original document
    /// order. The decoder reads keys sequentially and only updates the
    /// dynamic-id table when it has read each scope-triggering value, so
    /// both must precede any key that might reference a term they define.
    /// </summary>
    private static int[] BuildEmissionOrder(CborLdInputMap map)
    {
        int count = map.Entries.Count;
        int contextIndex = -1;
        int typeIndex = -1;
        for(int i = 0; i < count; i++)
        {
            string key = map.Entries[i].Key;
            if(contextIndex < 0 && key == "@context")
            {
                contextIndex = i;
            }
            else if(typeIndex < 0 && key == "@type")
            {
                typeIndex = i;
            }
        }

        if(contextIndex < 0 && typeIndex < 0)
        {
            //No reorder needed.
            int[] natural = new int[count];
            for(int i = 0; i < count; i++)
            {
                natural[i] = i;
            }
            return natural;
        }

        int[] order = new int[count];
        int slot = 0;
        if(contextIndex >= 0)
        {
            order[slot++] = contextIndex;
        }
        if(typeIndex >= 0)
        {
            order[slot++] = typeIndex;
        }
        for(int i = 0; i < count; i++)
        {
            if(i == contextIndex || i == typeIndex)
            {
                continue;
            }
            order[slot++] = i;
        }
        return order;
    }

    private static string[] ExtractTypeIris(CborLdInputNode typeNode)
    {
        return typeNode switch
        {
            CborLdInputString single => [single.Value],
            CborLdInputArray array => ExtractFromArray(array),
            _ => []
        };

        static string[] ExtractFromArray(CborLdInputArray array)
        {
            List<string> iris = new(array.Items.Count);
            foreach(CborLdInputNode item in array.Items)
            {
                if(item is CborLdInputString s)
                {
                    iris.Add(s.Value);
                }
            }
            return iris.ToArray();
        }
    }

    private static int ComputeNextDynamicId(CborLdRegistryEntry entry)
    {
        //Dynamic ids start after the highest registry-assigned term id.
        //Registry singular ids are even; plural ids (id + 1) are odd. Both
        //occupy the integer namespace; start above their max.
        int max = 99;
        foreach(KeyValuePair<string, CborLdTermCodec> kv in entry.Terms)
        {
            int candidate = kv.Value.CborId;
            if(candidate > max)
            {
                max = candidate;
            }
        }
        //Round up to the next even number ≥ 100 to keep the singular/plural
        //convention consistent with the registry's id allocation pattern.
        int next = max + 1;
        if((next & 1) == 1)
        {
            next++;
        }
        return next < 100 ? 100 : next;
    }

    private static void EmitTypedValue(
        CborWriter writer,
        CborLdInputNode value,
        string typeName,
        CborLdRegistryEntry registryEntry,
        CborLdMatcherContext matcherContext,
        CborLdCallerProvidedTypeTables callerTables,
        MemoryPool<byte> pool)
    {
        //Two routing modes:
        //  1. Lookup-based (e.g. "url"): a type table maps the string value
        //     to an integer id; that id is passed to the codec which packs
        //     it as bytes. The table source may be registry-supplied or
        //     caller-supplied per the registry entry's type-table source
        //     marker (see CborLdTypeTableSource).
        //  2. Direct (e.g. xsd:date / xsd:dateTime): the codec parses the
        //     string itself; no type-table lookup.
        CborLdInputNode codecInput = value;
        IReadOnlyDictionary<string, int>? table = ResolveTypeTable(registryEntry, typeName, callerTables, encoding: true);
        if(table is not null
            && value is CborLdInputString s
            && table.TryGetValue(s.Value, out int id))
        {
            codecInput = new CborLdInputInt(id);
        }

        CborLdTypedValueEncodeDelegate encoder;
        try
        {
            encoder = CborLdTypedValueCodecs.ResolveEncoder(typeName, matcherContext);
        }
        catch(Exception ex) when(ex is not CborLdProcessingException)
        {
            throw new CborLdProcessingException(
                $"CBOR-LD typed-value encoder resolution failed for type '{typeName}'.", ex);
        }

        IMemoryOwner<byte> wireBytes;
        try
        {
            wireBytes = encoder(codecInput, pool);
        }
        catch(Exception ex) when(ex is not CborLdProcessingException)
        {
            throw new CborLdProcessingException(
                $"CBOR-LD typed-value encoder failed for type '{typeName}'.", ex);
        }

        using(wireBytes)
        {
            bool emitAsBytes = IsTypeEncodedAsBytes(registryEntry, typeName);
            if(emitAsBytes)
            {
                writer.WriteByteString(wireBytes.Memory.Span);
            }
            else
            {
                //Read the int back from the wire bytes and emit as a CBOR integer.
                long intValue = ReadBigEndian(wireBytes.Memory.Span);
                writer.WriteInt64(intValue);
            }
        }
    }

    private static bool IsTypeEncodedAsBytes(CborLdRegistryEntry entry, string typeName)
    {
        //Convention: if the registry entry's TypeTables has a "<types-encoded-as-bytes>"
        //pseudo-table listing type names whose wire form is a byte string, consult it.
        //Otherwise default to integer emission. Real registries supply this set explicitly
        //via the conversion-state TypesEncodedAsBytes; the registry-entry surface we have
        //here approximates it with a sentinel key. Callers needing the canonical W3C
        //CBOR-LD 1.0 §5.2.1 step 4 behaviour can wire it through.
        return entry.TypeTables.TryGetValue(CborLdContextKeys.TypesEncodedAsBytesSentinel, out CborLdTypeTableSource? sentinelSource)
            && sentinelSource.Mappings is { } sentinel
            && sentinel.ContainsKey(typeName);
    }

    /// <summary>
    /// Resolves the type-table mapping for <paramref name="typeName"/>,
    /// pattern-matching on the registry entry's type-table source. When
    /// the source is a caller-provided marker, the table content is
    /// looked up in <paramref name="callerTables"/>; if no such table is
    /// supplied, <see cref="CborLdProcessingException"/> is raised with
    /// the project-defined error code <c>"caller provided type table missing"</c>.
    /// </summary>
    /// <param name="entry">The registry entry.</param>
    /// <param name="typeName">The type name to resolve a table for.</param>
    /// <param name="callerTables">Caller-supplied tables.</param>
    /// <param name="encoding">When <c>true</c>, the exception message
    /// names encoding context; when <c>false</c>, decoding context.
    /// Affects only the human-readable message.</param>
    /// <returns>The resolved string-to-integer table, or <c>null</c>
    /// when no table is registered for the type (caller code falls
    /// through to direct codec dispatch or string emission).</returns>
    private static IReadOnlyDictionary<string, int>? ResolveTypeTable(
        CborLdRegistryEntry entry,
        string typeName,
        CborLdCallerProvidedTypeTables callerTables,
        bool encoding)
    {
        if(!entry.TypeTables.TryGetValue(typeName, out CborLdTypeTableSource? source))
        {
            return null;
        }

        switch(source)
        {
            case CborLdRegistryProvidedTypeTable r:
            {
                return r.Mappings;
            }
            case CborLdCallerProvidedTypeTableMarker:
            {
                if(callerTables.TryGet(typeName, out CborLdCallerProvidedTypeTable? table) && table is not null)
                {
                    return table.Mappings;
                }
                string direction = encoding ? "encode" : "decode";
                throw new CborLdProcessingException(
                    "caller provided type table missing",
                    $"Registry entry #{entry.RegistryEntryId} declares type '{typeName}' as caller-provided, but no caller-provided table for that type was supplied at {direction} time.");
            }
            default:
            {
                return null;
            }
        }
    }


    private static long ReadBigEndian(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            8 => (long)BinaryPrimitives.ReadUInt64BigEndian(bytes),
            _ => ReadGeneral(bytes)
        };
    }

    private static long ReadGeneral(ReadOnlySpan<byte> bytes)
    {
        long result = 0;
        foreach(byte b in bytes)
        {
            result = (result << 8) | b;
        }
        return result;
    }

    private static CborLdMatcherContext BuildMatcherContext(CborLdRegistryEntry entry, CborLdProfile profile)
    {
        Dictionary<string, object> map = new(4)
        {
            [CborLdContextKeys.TypeTables] = entry.TypeTables,
            [CborLdContextKeys.Profile] = profile
        };
        return new CborLdMatcherContext(map.ToFrozenDictionary());
    }

    private static Dictionary<string, int> BuildNameToId(CborLdRegistryEntry entry)
    {
        Dictionary<string, int> map = new(entry.Keywords.Count + entry.Terms.Count);
        foreach(KeyValuePair<string, CborLdKeywordCodec> kv in entry.Keywords)
        {
            map[kv.Key] = kv.Value.CborId;
        }
        foreach(KeyValuePair<string, CborLdTermCodec> kv in entry.Terms)
        {
            map[kv.Key] = kv.Value.CborId;
        }
        return map;
    }

    private static void WriteMapKey(
        CborWriter writer,
        string key,
        CborLdInputNode value,
        Dictionary<string, int> nameToId,
        LinkedDataContext activeContext,
        Dictionary<string, int> dynamicToId)
    {
        int id;
        if(nameToId.TryGetValue(key, out id))
        {
            //Registry keyword or term codec — unconditional id emission.
        }
        else if(activeContext.TryGetTerm(key, out _) && dynamicToId.TryGetValue(key, out id))
        {
            //Dynamic id, but only when the term is visible in this frame's
            //active context. Frames encoding an @context value have an empty
            //(or pre-embedded) active context, so their inner term definitions
            //do not match and emit as text strings as they should.
        }
        else
        {
            writer.WriteTextString(key);
            return;
        }

        int wireId = value is CborLdInputArray ? id + 1 : id;
        writer.WriteInt32(wireId);
    }

    private sealed class EncodeFrame
    {
        public EncodeFrame(
            CborLdInputNode node,
            string? parentTermType,
            LinkedDataContext activeContext,
            LinkedDataContext preEmbedded)
        {
            Node = node;
            ParentTermType = parentTermType;
            ActiveContext = activeContext;
            PreEmbedded = preEmbedded;
        }

        public CborLdInputNode Node { get; }

        public string? ParentTermType { get; }

        public bool HeaderEmitted { get; set; }

        public int NextIndex { get; set; }

        /// <summary>The context active for this frame's value emission.</summary>
        public LinkedDataContext ActiveContext { get; set; }

        /// <summary>The context active before this frame's own @context (if any) applied.</summary>
        public LinkedDataContext PreEmbedded { get; set; }

        /// <summary>
        /// Map-emission index permutation: <c>@context</c> first when present,
        /// then other entries in document order. Empty for non-map frames.
        /// </summary>
        public int[] EmissionOrder { get; set; } = [];
    }
}
