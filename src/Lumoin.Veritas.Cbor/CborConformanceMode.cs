namespace Lumoin.Veritas.Cbor;

/// <summary>
/// The set of CBOR conformance disciplines selectable on the writer and
/// reader. Conformance modes range from permissive (<see cref="Lax"/>),
/// through validation-only (<see cref="Strict"/>), to fully deterministic
/// (<see cref="RfcCanonical"/>, <see cref="Ctap2Canonical"/>,
/// <see cref="Cde"/>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Lax"/>: accept and produce any well-formed CBOR. No length
/// minimisation, no map-key sorting, no UTF-8 validation, indefinite-length
/// items allowed.
/// </para>
/// <para>
/// <see cref="Strict"/>: validates UTF-8 in text strings, rejects duplicate
/// map keys, rejects malformed structures. Does not impose
/// length-minimisation or map-key ordering.
/// </para>
/// <para>
/// <see cref="RfcCanonical"/>: the canonical encoding rules from RFC 7049 §3.9
/// and continued in RFC 8949 §4.1: definite-length only, integer length
/// minimisation, and map keys sorted by length-then-bytewise comparison of
/// their encoded form.
/// </para>
/// <para>
/// <see cref="Ctap2Canonical"/>: the FIDO CTAP2 canonical encoding profile,
/// which closely follows RFC 7049 canonical with CTAP2-specific additional
/// constraints.
/// </para>
/// <para>
/// <see cref="Cde"/>: the Common Deterministic Encoding profile from
/// RFC 8949 §4.2 / draft-ietf-cbor-cde. Definite-length only, integer length
/// minimisation, and map keys sorted by bytewise lexicographic comparison of
/// their encoded form (no length-first tie-break).
/// </para>
/// </remarks>
public enum CborConformanceMode
{
    /// <summary>Permissive mode. Accept and produce any well-formed CBOR.</summary>
    Lax,

    /// <summary>Validation-only mode. Enforce UTF-8 and structural integrity but no determinism.</summary>
    Strict,

    /// <summary>RFC 7049 / RFC 8949 §4.1 canonical encoding rules.</summary>
    RfcCanonical,

    /// <summary>FIDO CTAP2 canonical encoding profile.</summary>
    Ctap2Canonical,

    /// <summary>Common Deterministic Encoding from RFC 8949 §4.2.</summary>
    Cde
}
