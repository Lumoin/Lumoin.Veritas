using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// One entry in a CBOR-LD registry. A registry entry specifies the term
/// and keyword codec tables that drive compression for documents using
/// this entry's identifier.
/// </summary>
/// <remarks>
/// <para>
/// Registry entry <c>0</c> is the passthrough entry: no compression. Its
/// codec dictionaries are empty; the encoder emits the document as-is in
/// CBOR primitives.
/// </para>
/// <para>
/// Non-zero registry entries carry compression tables: a keyword codec
/// for the JSON-LD keyword set (mapping <c>@id</c>, <c>@type</c>, etc. to
/// their wire integers) and a term codec for the user-defined terms in
/// the registry's pinned active context.
/// </para>
/// <para>
/// Per <see href="https://www.w3.org/TR/cbor-ld-10/">W3C CBOR-LD 1.0</see>
/// §4.1 a registry entry also carries a processing model selector, a
/// provisional flag, and an optional set of named type tables used for
/// typed-value compression. Each type table is either fully specified by
/// the registry entry (<see cref="CborLdRegistryProvidedTypeTable"/>) or
/// declared caller-provided
/// (<see cref="CborLdCallerProvidedTypeTableMarker"/>), in which case the
/// caller supplies the mappings at encode/decode time via
/// <see cref="CborLdCallerProvidedTypeTables"/>.
/// </para>
/// </remarks>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#registry"/>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public sealed class CborLdRegistryEntry
{
    private static IReadOnlyDictionary<string, CborLdTypeTableSource> EmptyTypeTables { get; } =
        new Dictionary<string, CborLdTypeTableSource>();

    /// <summary>
    /// Initialises a new registry entry with the given identifier and
    /// (possibly empty) codec dictionaries. Uses the <c>default</c>
    /// processing model and <c>provisional = false</c>.
    /// </summary>
    /// <param name="registryEntryId">The integer identifier this entry is registered under.</param>
    /// <param name="keywords">Keyword-to-id codec dictionary.</param>
    /// <param name="terms">Term-to-id codec dictionary.</param>
    public CborLdRegistryEntry(
        int registryEntryId,
        IReadOnlyDictionary<string, CborLdKeywordCodec> keywords,
        IReadOnlyDictionary<string, CborLdTermCodec> terms)
        : this(registryEntryId, keywords, terms, "default", provisional: false, typeTables: null)
    {
    }

    /// <summary>
    /// Initialises a new registry entry with the full set of W3C CBOR-LD
    /// 1.0 §4.1 fields.
    /// </summary>
    /// <param name="registryEntryId">The integer identifier this entry is registered under.</param>
    /// <param name="keywords">Keyword-to-id codec dictionary.</param>
    /// <param name="terms">Term-to-id codec dictionary.</param>
    /// <param name="processingModel">The processing model selector, e.g. <c>"default"</c>.</param>
    /// <param name="provisional">Whether this entry is provisional and subject to change.</param>
    /// <param name="typeTables">Named type-table sources; may be <c>null</c> for none. Construct values via
    /// <see cref="CborLdTypeTableSource.FromRegistry"/> for registry-supplied tables or
    /// <see cref="CborLdTypeTableSource.CallerProvided"/> for tables the caller supplies at encode/decode time.</param>
    public CborLdRegistryEntry(
        int registryEntryId,
        IReadOnlyDictionary<string, CborLdKeywordCodec> keywords,
        IReadOnlyDictionary<string, CborLdTermCodec> terms,
        string processingModel,
        bool provisional,
        IReadOnlyDictionary<string, CborLdTypeTableSource>? typeTables)
    {
        RegistryEntryId = registryEntryId;
        Keywords = keywords;
        Terms = terms;
        ProcessingModel = processingModel;
        Provisional = provisional;
        TypeTables = typeTables ?? EmptyTypeTables;
    }

    /// <summary>Gets the identifier this entry is registered under.</summary>
    public int RegistryEntryId { get; }

    /// <summary>Gets the keyword-to-id codec dictionary.</summary>
    public IReadOnlyDictionary<string, CborLdKeywordCodec> Keywords { get; }

    /// <summary>Gets the term-to-id codec dictionary.</summary>
    public IReadOnlyDictionary<string, CborLdTermCodec> Terms { get; }

    /// <summary>
    /// Gets the processing model selector. The only currently defined value
    /// in W3C CBOR-LD 1.0 is <c>"default"</c>.
    /// </summary>
    public string ProcessingModel { get; }

    /// <summary>
    /// Gets whether this entry is provisional. Provisional entries may
    /// change without preserving wire compatibility.
    /// </summary>
    public bool Provisional { get; }

    /// <summary>
    /// Gets the named type-table sources used by typed-value compression.
    /// Keyed by type name (e.g. <c>"url"</c>, <c>"xsd:date"</c>); each
    /// value is either a registry-supplied mapping or a caller-provided
    /// marker.
    /// </summary>
    public IReadOnlyDictionary<string, CborLdTypeTableSource> TypeTables { get; }

    /// <summary>
    /// Gets the passthrough registry entry (id <c>0</c>, no compression).
    /// Single shared instance suitable for sharing across consumers.
    /// </summary>
    public static CborLdRegistryEntry Passthrough { get; } = new(
        registryEntryId: 0,
        keywords: new Dictionary<string, CborLdKeywordCodec>(),
        terms: new Dictionary<string, CborLdTermCodec>(),
        processingModel: "default",
        provisional: false,
        typeTables: null);

    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"CborLdRegistryEntry #{RegistryEntryId} keywords={Keywords.Count} terms={Terms.Count} types={TypeTables.Count}");
}
