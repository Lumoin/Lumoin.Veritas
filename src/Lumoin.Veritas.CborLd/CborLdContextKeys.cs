namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Well-known string keys used by <see cref="CborLdMatcherContext"/>
/// to carry registry metadata into matcher delegates during compression
/// and decompression.
/// </summary>
public static class CborLdContextKeys
{
    /// <summary>Key for the registry entry's type tables
    /// (<see cref="System.Collections.Generic.IReadOnlyDictionary{TKey, TValue}"/>
    /// of name → table).</summary>
    public const string TypeTables = "type-tables";

    /// <summary>Key for the registry entry's reverse type tables.</summary>
    public const string ReverseTypeTables = "reverse-type-tables";

    /// <summary>Key for the registry entry's
    /// <see cref="System.Collections.Generic.IReadOnlySet{T}"/> of types
    /// encoded as bytes.</summary>
    public const string TypesEncodedAsBytes = "types-encoded-as-bytes";

    /// <summary>Key for the current <see cref="CborLdProfile"/>.</summary>
    public const string Profile = "profile";

    /// <summary>
    /// Sentinel key the compression encoder reads under
    /// <see cref="CborLdRegistryEntry.TypeTables"/> to determine which
    /// type names emit as CBOR byte strings rather than CBOR integers
    /// (W3C CBOR-LD 1.0 §5.2.1 step 4). The associated map's keys are
    /// the type names; the values are unused.
    /// </summary>
    public const string TypesEncodedAsBytesSentinel = "__types-encoded-as-bytes__";
}
