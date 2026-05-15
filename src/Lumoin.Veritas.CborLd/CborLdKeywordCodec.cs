using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// A mapping from a JSON-LD keyword (e.g. <c>@id</c>, <c>@type</c>) to its
/// compressed CBOR integer identifier. Per W3C CBOR-LD 1.0 §5.4.1 step 3,
/// keywords occupy the 28 even IDs in the range 0..54; the odd identifier
/// <c>CborId + 1</c> denotes the plural (array-valued) form of the same
/// keyword, so each keyword pairs on consecutive even/odd integers.
/// </summary>
/// <param name="Keyword">The JSON-LD keyword (begins with <c>'@'</c>).</param>
/// <param name="CborId">The compressed integer identifier used on the wire (the even, singular form).</param>
/// <seealso href="https://www.w3.org/TR/cbor-ld-10/#convert-map"/>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborLdKeywordCodec(string Keyword, int CborId)
{
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{Keyword} -> {CborId}");
}
