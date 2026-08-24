using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Cbor.CborLd.Internal;
using Lumoin.Veritas.LinkedData;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Implements compression-side decoding for CBOR-LD. Reverses the
/// substitutions in <see cref="CborLdCompressionEncoder"/>: integer map
/// keys are resolved back to keyword or term strings via the registry's
/// codec dictionaries and the active-context dynamic-id table built up
/// as <c>@context</c> values are encountered. The singular/plural rule
/// (even = singular, odd <c>id - 1</c> = plural of <c>id - 1</c>) is
/// decoded. Text-string keys fall through unchanged.
/// </summary>
internal static class CborLdCompressionDecoder
{
    public static async ValueTask<CborLdInputNode> ReadPayloadAsync(
        CborReader reader,
        CborLdRegistryEntry registryEntry,
        CborLdProfile profile,
        FetchRemoteResourceDelegate? fetcher = null,
        ParseRemoteContextDelegate? parser = null,
        ProbeContextCacheDelegate? cache = null,
        CborLdCallerProvidedTypeTables? callerTables = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<int, string> idToName = BuildIdToName(registryEntry);
        Dictionary<string, IReadOnlyDictionary<int, string>> reverseTypeTables = BuildReverseTypeTables(registryEntry);
        CborLdMatcherContext matcherContext = BuildMatcherContext(registryEntry, profile);
        CborLdCallerProvidedTypeTables effectiveCallerTables = callerTables ?? CborLdCallerProvidedTypeTables.Empty;
        //Per-call cache: inverted caller-provided tables. Built lazily on
        //first lookup per type name; reused for subsequent occurrences in
        //the same decode call.
        Dictionary<string, IReadOnlyDictionary<int, string>> callerReverseCache = new(StringComparer.Ordinal);

        CborLdConversionState state = new()
        {
            NextTermId = ComputeNextDynamicId(registryEntry)
        };
        CborLdActiveContextScope scope = new(fetcher, parser, cache);

        Stack<DecodeFrame> frames = new();
        CborLdInputNode? rootResult = null;

        while(rootResult is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                await AttachToTopAsync(frames.Peek(), container, scope, state, cancellationToken).ConfigureAwait(false);
                continue;
            }

            CborReaderState readerState = reader.PeekState();
            switch(readerState)
            {
                case CborReaderState.UnsignedInteger:
                case CborReaderState.NegativeInteger:
                {
                    long intValue = reader.ReadInt64();
                    if(frames.Count > 0 && frames.Peek().IsMap && frames.Peek().PendingKey is null)
                    {
                        frames.Peek().PendingKey = ResolveIntegerKey(intValue, idToName, frames.Peek().ActiveContext, state.IdToTerm);
                        break;
                    }
                    CborLdInputNode leaf = MaybeDispatchTypedValue(
                        frames,
                        registryEntry,
                        reverseTypeTables,
                        effectiveCallerTables,
                        callerReverseCache,
                        matcherContext,
                        intValue: intValue,
                        bytesValue: null) ?? new CborLdInputInt(intValue);
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
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
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.ByteString:
                {
                    ReadOnlyMemory<byte> bytes = reader.ReadByteStringMemory();
                    CborLdInputNode leaf = MaybeDispatchTypedValue(
                        frames,
                        registryEntry,
                        reverseTypeTables,
                        effectiveCallerTables,
                        callerReverseCache,
                        matcherContext,
                        intValue: null,
                        bytesValue: bytes) ?? new CborLdInputBytes(bytes);
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.Null:
                {
                    reader.ReadNull();
                    rootResult = await AttachLeafOrReturnRootAsync(frames, CborLdInputNull.Instance, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.Boolean:
                {
                    CborLdInputNode leaf = new CborLdInputBool(reader.ReadBoolean());
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.DoublePrecisionFloat:
                {
                    CborLdInputNode leaf = new CborLdInputDouble(reader.ReadDouble());
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.SinglePrecisionFloat:
                {
                    CborLdInputNode leaf = new CborLdInputDouble(reader.ReadSingle());
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.HalfPrecisionFloat:
                {
                    CborLdInputNode leaf = new CborLdInputDouble((double)reader.ReadHalf());
                    rootResult = await AttachLeafOrReturnRootAsync(frames, leaf, rootResult, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.StartArray:
                {
                    int? count = reader.ReadStartArray();
                    if(count is null)
                    {
                        throw new CborLdProcessingException("CBOR-LD compression decoder does not accept indefinite-length arrays.");
                    }
                    await PushChildFrameAsync(frames, isMap: false, expectedCount: count.Value, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                case CborReaderState.StartMap:
                {
                    int? count = reader.ReadStartMap();
                    if(count is null)
                    {
                        throw new CborLdProcessingException("CBOR-LD compression decoder does not accept indefinite-length maps.");
                    }
                    await PushChildFrameAsync(frames, isMap: true, expectedCount: count.Value, scope, state, cancellationToken).ConfigureAwait(false);
                    break;
                }
                default:
                {
                    throw new CborLdProcessingException(
                        string.Create(CultureInfo.InvariantCulture, $"CBOR-LD compression decoder does not handle reader state {readerState}."));
                }
            }
        }

        return rootResult;
    }

    /// <summary>
    /// Pushes a child frame onto the stack, computing its active context
    /// and propagating the context-data flag. A child is context-data when
    /// its parent is context-data OR when its parent is currently decoding
    /// the value of an <c>@context</c> key — both cases describe a subtree
    /// of a context definition, where keys are definitions (text-only) and
    /// no further scope-helper applications fire on attach.
    /// </summary>
    private static async ValueTask PushChildFrameAsync(
        Stack<DecodeFrame> frames,
        bool isMap,
        int expectedCount,
        CborLdActiveContextScope scope,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        if(frames.Count == 0)
        {
            //Root frame: no parent to inherit context from.
            frames.Push(new DecodeFrame(
                isMap: isMap,
                expectedCount: expectedCount,
                activeContext: LinkedDataContext.Empty,
                preEmbedded: LinkedDataContext.Empty,
                isContextData: false));
            return;
        }

        DecodeFrame parent = frames.Peek();
        bool inheritedContextData = parent.IsContextData || parent.PendingKey == "@context";
        LinkedDataContext childContext = await SelectChildActiveContextAsync(parent, scope, state, cancellationToken).ConfigureAwait(false);
        frames.Push(new DecodeFrame(
            isMap: isMap,
            expectedCount: expectedCount,
            activeContext: childContext,
            preEmbedded: childContext,
            isContextData: inheritedContextData));
    }

    /// <summary>
    /// Computes the active context for a child container being pushed.
    /// Mirror of the encoder's <c>SelectChildContext</c>:
    /// <list type="bullet">
    /// <item><description>Child of a map whose pending key is <c>@context</c>: encode in pre-embedded.</description></item>
    /// <item><description>Otherwise: descendant context (propagate-gated) with property-scoped overlay where applicable.</description></item>
    /// </list>
    /// </summary>
    private static async ValueTask<LinkedDataContext> SelectChildActiveContextAsync(
        DecodeFrame parent,
        CborLdActiveContextScope scope,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        //Non-map parent: child inherits the parent's active context directly
        //(arrays don't carry @context).
        if(!parent.IsMap)
        {
            return parent.ActiveContext;
        }

        //If the parent is a map currently expecting a value, look at the
        //PendingKey to decide which context the value should be decoded in.
        if(parent.PendingKey is "@context")
        {
            return parent.PreEmbedded;
        }

        LinkedDataContext descendant = parent.ActiveContext.Propagate
            ? parent.ActiveContext
            : parent.PreEmbedded;

        if(parent.PendingKey is { } key
            && parent.ActiveContext.TryGetTerm(key, out TermDefinition? def)
            && def is { ScopedContextEntries: not null })
        {
            descendant = await scope.WithPropertyScopedAsync(
                descendant, def, baseUrl: null, state, cancellationToken)
                .ConfigureAwait(false);
        }

        return descendant;
    }

    private static string ResolveIntegerKey(
        long intValue,
        Dictionary<int, string> idToName,
        LinkedDataContext activeContext,
        Dictionary<int, string> dynamicIdToTerm)
    {
        if(intValue < int.MinValue || intValue > int.MaxValue)
        {
            throw new CborLdProcessingException(
                string.Create(CultureInfo.InvariantCulture, $"CBOR-LD integer key {intValue} does not fit in Int32."));
        }
        int id = (int)intValue;
        //Odd id encodes the plural form of id-1; even id encodes the singular form.
        int lookupId = (id & 1) == 1 ? id - 1 : id;

        if(idToName.TryGetValue(lookupId, out string? registryName))
        {
            return registryName;
        }

        //Dynamic terms must also be visible in the current active context.
        //If state.IdToTerm has the id but the term isn't yet in the active
        //context (e.g. defined inside an as-yet-unconsumed @context value),
        //resolution fails — that catches malformed wire orderings.
        if(dynamicIdToTerm.TryGetValue(lookupId, out string? dynamicTerm)
            && activeContext.TryGetTerm(dynamicTerm, out _))
        {
            return dynamicTerm;
        }

        throw new CborLdProcessingException(
            string.Create(CultureInfo.InvariantCulture, $"CBOR-LD integer key {id} is not present in the registry entry's keyword or term codec dictionaries, nor in the active context's dynamic term table."));
    }

    private static int ComputeNextDynamicId(CborLdRegistryEntry entry)
    {
        int max = 99;
        foreach(KeyValuePair<string, CborLdTermCodec> kv in entry.Terms)
        {
            int candidate = kv.Value.CborId;
            if(candidate > max)
            {
                max = candidate;
            }
        }
        int next = max + 1;
        if((next & 1) == 1)
        {
            next++;
        }
        return next < 100 ? 100 : next;
    }

    private static Dictionary<int, string> BuildIdToName(CborLdRegistryEntry entry)
    {
        Dictionary<int, string> map = new(entry.Keywords.Count + entry.Terms.Count);
        foreach(KeyValuePair<string, CborLdKeywordCodec> kv in entry.Keywords)
        {
            map[kv.Value.CborId] = kv.Key;
        }
        foreach(KeyValuePair<string, CborLdTermCodec> kv in entry.Terms)
        {
            map[kv.Value.CborId] = kv.Key;
        }
        return map;
    }

    private static Dictionary<string, IReadOnlyDictionary<int, string>> BuildReverseTypeTables(CborLdRegistryEntry entry)
    {
        //Only registry-provided sources contribute to the static reverse
        //map. Caller-provided tables are inverted lazily per call when
        //they are first referenced (see InvertMappings).
        Dictionary<string, IReadOnlyDictionary<int, string>> reverse = new(entry.TypeTables.Count);
        foreach(KeyValuePair<string, CborLdTypeTableSource> kv in entry.TypeTables)
        {
            if(kv.Value.Mappings is not { } mappings)
            {
                continue;
            }
            reverse[kv.Key] = InvertMappings(mappings);
        }
        return reverse;
    }

    private static Dictionary<int, string> InvertMappings(IReadOnlyDictionary<string, int> mappings)
    {
        Dictionary<int, string> inverted = new(mappings.Count);
        foreach(KeyValuePair<string, int> kv in mappings)
        {
            inverted[kv.Value] = kv.Key;
        }
        return inverted;
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

    private static CborLdInputNode? MaybeDispatchTypedValue(
        Stack<DecodeFrame> frames,
        CborLdRegistryEntry registryEntry,
        Dictionary<string, IReadOnlyDictionary<int, string>> reverseTypeTables,
        CborLdCallerProvidedTypeTables callerTables,
        Dictionary<string, IReadOnlyDictionary<int, string>> callerReverseCache,
        CborLdMatcherContext matcherContext,
        long? intValue,
        ReadOnlyMemory<byte>? bytesValue)
    {
        if(frames.Count == 0)
        {
            return null;
        }
        DecodeFrame top = frames.Peek();
        if(!top.IsMap || top.PendingKey is null)
        {
            return null;
        }

        //Resolve type name: registry term codec wins, falling back to the
        //active context's TermDefinition&lt;TNode&gt;.TypeMapping (mirror of the
        //encoder's ResolveTypeName). Returns null for untyped terms.
        string? typeName = ResolveTypeName(top.PendingKey, top.ActiveContext, registryEntry);
        if(typeName is null)
        {
            return null;
        }

        ReadOnlyMemory<byte> wireBytes;
        if(bytesValue is { } b)
        {
            wireBytes = b;
        }
        else if(intValue is { } i)
        {
            wireBytes = EncodeIntegerBigEndian(i);
        }
        else
        {
            return null;
        }

        CborLdTypedValueDecodeDelegate decoder;
        try
        {
            decoder = CborLdTypedValueCodecs.ResolveDecoder(typeName, matcherContext);
        }
        catch(Exception ex) when(ex is not CborLdProcessingException)
        {
            throw new CborLdProcessingException(
                $"CBOR-LD typed-value decoder resolution failed for type '{typeName}'.", ex);
        }

        CborLdInputNode decoded = decoder(wireBytes);
        //For lookup-based types (e.g. URL), the codec returns a CborLdInputInt
        //holding the registered id; recover the original string via the
        //appropriate reverse table. Source selection mirrors the encoder:
        //registry-provided tables are pre-inverted in reverseTypeTables;
        //caller-provided tables are inverted lazily and cached in
        //callerReverseCache. For direct types (e.g. xsd:date) the codec
        //returns the string directly; pass through unchanged. Dynamic
        //terms (no table at all) fall through to direct-codec semantics.
        if(decoded is CborLdInputInt intLeaf
            && intLeaf.Value >= int.MinValue && intLeaf.Value <= int.MaxValue)
        {
            IReadOnlyDictionary<int, string>? reverseTable =
                ResolveReverseTable(registryEntry, typeName, reverseTypeTables, callerTables, callerReverseCache);
            if(reverseTable is not null
                && reverseTable.TryGetValue((int)intLeaf.Value, out string? original))
            {
                return new CborLdInputString(original);
            }
        }
        return decoded;
    }

    /// <summary>
    /// Resolves the integer-to-string reverse lookup table for
    /// <paramref name="typeName"/>. Registry-provided sources are
    /// satisfied from the pre-built <paramref name="reverseTypeTables"/>;
    /// caller-provided sources are inverted on first use and cached in
    /// <paramref name="callerReverseCache"/>. Returns <c>null</c> when no
    /// table is registered for the type. When the registry declares the
    /// table caller-provided but the caller supplied none, throws
    /// <see cref="CborLdProcessingException"/> with the spec-aligned
    /// error code <c>"caller provided type table missing"</c>.
    /// </summary>
    private static IReadOnlyDictionary<int, string>? ResolveReverseTable(
        CborLdRegistryEntry registryEntry,
        string typeName,
        Dictionary<string, IReadOnlyDictionary<int, string>> reverseTypeTables,
        CborLdCallerProvidedTypeTables callerTables,
        Dictionary<string, IReadOnlyDictionary<int, string>> callerReverseCache)
    {
        if(reverseTypeTables.TryGetValue(typeName, out IReadOnlyDictionary<int, string>? registryReverse))
        {
            return registryReverse;
        }

        if(!registryEntry.TypeTables.TryGetValue(typeName, out CborLdTypeTableSource? source))
        {
            return null;
        }

        if(!source.IsCallerProvided)
        {
            return null;
        }

        if(callerReverseCache.TryGetValue(typeName, out IReadOnlyDictionary<int, string>? cached))
        {
            return cached;
        }

        if(!callerTables.TryGet(typeName, out CborLdCallerProvidedTypeTable? callerTable) || callerTable is null)
        {
            throw new CborLdProcessingException(
                "caller provided type table missing",
                $"Registry entry #{registryEntry.RegistryEntryId} declares type '{typeName}' as caller-provided, but no caller-provided table for that type was supplied at decode time.");
        }

        IReadOnlyDictionary<int, string> inverted = InvertMappings(callerTable.Mappings);
        callerReverseCache[typeName] = inverted;
        return inverted;
    }

    /// <summary>
    /// Mirror of <c>CborLdCompressionEncoder.ResolveTypeName</c>. Registry
    /// term codec wins; falls back to the active context's
    /// <see cref="TermDefinition.TypeMapping"/> (filtering out the
    /// type-coercion keywords <c>@id</c> and <c>@vocab</c>).
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

    private static byte[] EncodeIntegerBigEndian(long value)
    {
        //Use the minimum width that the encoder would produce.
        if(value >= 0 && value <= ushort.MaxValue)
        {
            byte[] two = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(two, (ushort)value);
            return two;
        }
        if(value >= 0 && value <= uint.MaxValue)
        {
            byte[] four = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(four, (uint)value);
            return four;
        }
        byte[] eight = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(eight, value);
        return eight;
    }

    private static async ValueTask<CborLdInputNode?> AttachLeafOrReturnRootAsync(
        Stack<DecodeFrame> frames,
        CborLdInputNode leaf,
        CborLdInputNode? currentRoot,
        CborLdActiveContextScope scope,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        if(frames.Count == 0)
        {
            return leaf;
        }
        await AttachToTopAsync(frames.Peek(), leaf, scope, state, cancellationToken).ConfigureAwait(false);

        return currentRoot;
    }

    private static async ValueTask AttachToTopAsync(
        DecodeFrame frame,
        CborLdInputNode child,
        CborLdActiveContextScope scope,
        CborLdConversionState state,
        CancellationToken cancellationToken)
    {
        if(frame.IsMap)
        {
            string key = frame.PendingKey
                ?? throw new CborLdProcessingException("CBOR-LD map value without a preceding key; the wire form is corrupt.");
            frame.Entries.Add(new KeyValuePair<string, CborLdInputNode>(key, child));
            frame.PendingKey = null;

            //E3: after attaching an @context or @type value, apply the
            //scoped context so subsequent keys of this map see the updated
            //dynamic-id table. Wire order guarantees both arrive before
            //any key that might reference their defined terms; see
            //CborLdCompressionEncoder.BuildEmissionOrder.
            //
            //Suppression for context-data frames: when this frame is itself
            //a subtree inside a parent's @context value (i.e. it is
            //decoding a term-definition map), an @context key here is part
            //of that term's ScopedContext data — not a document-level
            //scope trigger. ContextProcessing will pick the nested context
            //up through LinkedDataTermSource.ScopedContext when the OUTER
            //@context is applied.
            if(frame.IsContextData || child is null)
            {
                return;
            }

            if(key == "@context")
            {
                frame.PreEmbedded = frame.ActiveContext;
                frame.ActiveContext = await scope.WithEmbeddedContextAsync(
                    frame.ActiveContext, child, baseUrl: null, state, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if(key == "@type")
            {
                string[] typeIris = ExtractTypeIris(child);
                if(typeIris.Length > 0)
                {
                    frame.ActiveContext = await scope.WithTypeScopedAsync(
                        frame.ActiveContext, typeIris, baseUrl: null, state, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        else
        {
            frame.Items.Add(child);
        }
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

    private sealed class DecodeFrame
    {
        private const int SafePreallocationCap = 64;

        public DecodeFrame(
            bool isMap,
            int expectedCount,
            LinkedDataContext activeContext,
            LinkedDataContext preEmbedded,
            bool isContextData)
        {
            IsMap = isMap;
            ExpectedCount = expectedCount;
            ActiveContext = activeContext;
            PreEmbedded = preEmbedded;
            IsContextData = isContextData;
            int safeCapacity = expectedCount > SafePreallocationCap ? SafePreallocationCap : expectedCount;
            Items = isMap ? null! : new List<CborLdInputNode>(safeCapacity);
            Entries = isMap ? new List<KeyValuePair<string, CborLdInputNode>>(safeCapacity) : null!;
        }

        public bool IsMap { get; }

        public int ExpectedCount { get; }

        public List<CborLdInputNode> Items { get; }

        public List<KeyValuePair<string, CborLdInputNode>> Entries { get; }

        public string? PendingKey { get; set; }

        public LinkedDataContext ActiveContext { get; set; }

        public LinkedDataContext PreEmbedded { get; set; }

        /// <summary>
        /// True when this frame and all descendants are decoding a subtree
        /// nested inside some parent's <c>@context</c> value. Context-data
        /// frames suppress scope-helper application on attach because their
        /// keys are definition data, not document-level scope triggers.
        /// </summary>
        public bool IsContextData { get; }

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
