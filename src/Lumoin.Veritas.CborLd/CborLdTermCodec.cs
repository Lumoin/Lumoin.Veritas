using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// A mapping from a user-defined Linked Data term to its compressed CBOR
/// integer identifier. Per the W3C CBOR-LD specification, user-term IDs
/// occupy even integers starting at 100 (100, 102, 104, ...); odd
/// identifiers in that range are reserved for value-codec selection.
/// </summary>
/// <param name="Term">The compact term as defined in the active context.</param>
/// <param name="CborId">The compressed integer identifier used on the wire.</param>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public readonly record struct CborLdTermCodec(string Term, int CborId)
{
    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{Term} -> {CborId}");
}
