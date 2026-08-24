using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// A mapping from a user-defined Linked Data term to its compressed CBOR
/// integer identifier. Per W3C CBOR-LD 1.0 §5.2.5.3 and §5.2.6.3, user-term
/// IDs occupy even integers starting at 100 (100, 102, 104, ...). The odd
/// identifier <c>CborId + 1</c> denotes the plural (array-valued) form of
/// the same term; the singular and plural forms always pair on consecutive
/// even/odd integers. When <see cref="Type"/> is non-null the term carries
/// a typed value (W3C CBOR-LD 1.0 §5.5.2); the compression encoder/decoder
/// route the value through the typed-value codec registered for that type.
/// </summary>
/// <param name="Term">The compact term as defined in the active context.</param>
/// <param name="CborId">The compressed integer identifier used on the wire (the even, singular form).</param>
/// <param name="Type">Optional type identifier for typed values (e.g. <c>"url"</c>, <c>"http://www.w3.org/2001/XMLSchema#date"</c>); <c>null</c> for untyped terms.</param>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#convert-map"/>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborLdTermCodec(string Term, int CborId, string? Type = null)
{
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{Term} -> {CborId}{(Type is null ? string.Empty : " : " + Type)}");
}
