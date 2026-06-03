namespace Lumoin.Veritas.Cbor;

/// <summary>
/// Options for <see cref="CborDiagnosticNotation.ToDiagnosticNotation"/>.
/// </summary>
/// <param name="DecodeEmbeddedByteStrings">Whether a byte string whose content is itself a single, fully-consumed CBOR item is rendered as embedded CBOR (<c>&lt;&lt;…&gt;&gt;</c>) instead of hex.</param>
/// <param name="Mode">The conformance mode the underlying reader uses; <see cref="CborConformanceMode.Lax"/> by default so arbitrary encodings render.</param>
public sealed record CborDiagnosticOptions(bool DecodeEmbeddedByteStrings = false, CborConformanceMode Mode = CborConformanceMode.Lax)
{
    /// <summary>Gets the default options: hex byte strings and the lax reader.</summary>
    public static CborDiagnosticOptions Default { get; } = new();
}
