using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// A mapping from a JSON-LD keyword (e.g. <c>@id</c>, <c>@type</c>) to its
/// compressed CBOR integer identifier. Per the W3C CBOR-LD specification,
/// keyword IDs occupy the range 0..54.
/// </summary>
/// <param name="Keyword">The JSON-LD keyword (begins with <c>'@'</c>).</param>
/// <param name="CborId">The compressed integer identifier used on the wire.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborLdKeywordCodec(string Keyword, int CborId)
{
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{Keyword} -> {CborId}");
}
