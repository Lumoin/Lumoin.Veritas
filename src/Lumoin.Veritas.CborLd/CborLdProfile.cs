namespace Lumoin.Veritas.Cbor.CborLd;

/// <summary>
/// Controls the byte-level guarantees of a CBOR-LD encoding. The
/// <see cref="Default"/> profile produces W3C-spec-compliant CBOR-LD with
/// no extra byte-level guarantees beyond what the spec requires. The
/// <see cref="Deterministic"/> profile pins all encoding-discretion
/// decisions so the output is byte-deterministic; the result is suitable
/// for direct signing or content-addressing.
/// </summary>
public enum CborLdProfile
{
    /// <summary>Spec-conformant encoding with no extra determinism guarantees.</summary>
    Default,

    /// <summary>
    /// Deterministic encoding. The inner CBOR layer runs under
    /// <see cref="CborConformanceMode.Cde"/>, integer encodings are length
    /// minimised, map keys are sorted, and indefinite-length items are
    /// rejected.
    /// </summary>
    Deterministic
}
