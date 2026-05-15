using System;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;

namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// A typed-value compression table supplied by the caller rather than
/// drawn from a CBOR-LD registry entry. Used when a registry entry's
/// <c>typeTables</c> array contains the sentinel string
/// <c>"callerProvidedTable"</c> for one or more type names, indicating
/// that the table for that type is not globally defined and must be
/// provided by the caller at encode or decode time.
/// </summary>
/// <remarks>
/// <para>
/// The library reads the supplied <see cref="Mappings"/> during a
/// single encode or decode call and does not retain references after
/// the call returns. Callers can hold tables as long-lived state
/// without incurring copy overhead.
/// </para>
/// <para>
/// The reverse direction (integer-to-string, used by the decoder) is
/// derived lazily from <see cref="Mappings"/> per call; callers do
/// not supply it separately.
/// </para>
/// <para>
/// See <see href="https://www.w3.org/TR/cbor-ld-10/">W3C CBOR-LD 1.0</see>
/// — CBOR-LD Registry Entries.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerLabel,nq}")]
public sealed class CborLdCallerProvidedTypeTable
{
    /// <summary>
    /// Initialises a new caller-provided type table for the given
    /// type name with the supplied mappings.
    /// </summary>
    /// <param name="typeName">The type name this table applies to,
    /// e.g. <c>"url"</c>.</param>
    /// <param name="mappings">The string-to-integer mappings. Frozen
    /// for read-only access during encode/decode.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public CborLdCallerProvidedTypeTable(
        string typeName,
        FrozenDictionary<string, int> mappings)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(mappings);
        TypeName = typeName;
        Mappings = mappings;
    }

    /// <summary>Gets the type name this table applies to.</summary>
    public string TypeName { get; }

    /// <summary>Gets the string-to-integer mappings.</summary>
    public FrozenDictionary<string, int> Mappings { get; }

    private string DebuggerLabel
        => string.Create(CultureInfo.InvariantCulture, $"{TypeName}: {Mappings.Count} mappings");
}
