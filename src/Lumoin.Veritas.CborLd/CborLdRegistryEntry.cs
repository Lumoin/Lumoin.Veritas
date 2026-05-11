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
/// </remarks>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public sealed class CborLdRegistryEntry
{
    /// <summary>
    /// Initialises a new registry entry with the given identifier and
    /// (possibly empty) codec dictionaries.
    /// </summary>
    /// <param name="registryEntryId">The integer identifier this entry is registered under.</param>
    /// <param name="keywords">Keyword-to-id codec dictionary.</param>
    /// <param name="terms">Term-to-id codec dictionary.</param>
    public CborLdRegistryEntry(
        int registryEntryId,
        IReadOnlyDictionary<string, CborLdKeywordCodec> keywords,
        IReadOnlyDictionary<string, CborLdTermCodec> terms)
    {
        RegistryEntryId = registryEntryId;
        Keywords = keywords;
        Terms = terms;
    }

    /// <summary>Gets the identifier this entry is registered under.</summary>
    public int RegistryEntryId { get; }

    /// <summary>Gets the keyword-to-id codec dictionary.</summary>
    public IReadOnlyDictionary<string, CborLdKeywordCodec> Keywords { get; }

    /// <summary>Gets the term-to-id codec dictionary.</summary>
    public IReadOnlyDictionary<string, CborLdTermCodec> Terms { get; }

    /// <summary>
    /// Gets the passthrough registry entry (id <c>0</c>, no compression).
    /// Single shared instance suitable for sharing across consumers.
    /// </summary>
    public static CborLdRegistryEntry Passthrough { get; } = new(
        registryEntryId: 0,
        keywords: new Dictionary<string, CborLdKeywordCodec>(),
        terms: new Dictionary<string, CborLdTermCodec>());

    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"CborLdRegistryEntry #{RegistryEntryId} keywords={Keywords.Count} terms={Terms.Count}");
}
