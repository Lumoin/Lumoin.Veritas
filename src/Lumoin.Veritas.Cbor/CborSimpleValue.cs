using System.Diagnostics.CodeAnalysis;

namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Standard simple values defined by RFC 8949 §3.3. CBOR simple values are
/// drawn from major type 7 and identify a small set of named singletons:
/// <c>false</c>, <c>true</c>, <c>null</c>, and <c>undefined</c>.
/// </summary>
/// <remarks>
/// Other simple value identifiers (in the ranges 0..19 and 32..255) are
/// reserved by the IANA CBOR Simple Values registry and are written
/// numerically through the writer's simple-value path; this enum lists only
/// the four standardised names a writer or reader will produce or consume
/// without external interpretation.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "The simple-value identifiers are wire-format constants assigned by the IANA CBOR Simple Values registry. A synthetic 'None = 0' member would collide with the reserved 0..19 simple-value range and would not correspond to any standard name on the wire.")]
[SuppressMessage(
    "Design",
    "CA1028:Enum storage should be Int32",
    Justification = "Standard simple values occupy a single byte after the major-type 7 header on the wire; sizing the enum's underlying type as System.Byte preserves that wire-format contract.")]
public enum CborSimpleValue: byte
{
    /// <summary>The Boolean value <c>false</c>. Simple value 20.</summary>
    False = 20,

    /// <summary>The Boolean value <c>true</c>. Simple value 21.</summary>
    True = 21,

    /// <summary>The null value. Simple value 22.</summary>
    Null = 22,

    /// <summary>
    /// The undefined value. Simple value 23. Distinct from
    /// <see cref="Null"/>; rarely useful in modern data interchange but
    /// preserved here for completeness with the spec.
    /// </summary>
    Undefined = 23
}
