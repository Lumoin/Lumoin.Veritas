namespace Lumoin.Veritas.Core;

/// <summary>
/// The kind of randomness a caller is requesting at a given call site.
/// </summary>
/// <remarks>
/// The kind selects which field of the returned <see cref="RandomnessValue"/> is
/// meaningful, so a single delegate serves every context-dependent built-in that
/// consults entropy (SPARQL <c>RAND</c>, <c>UUID</c>, <c>STRUUID</c>, and raw
/// byte requests) without per-built-in delegate types.
/// </remarks>
public enum RandomnessKind
{
    /// <summary>An <c>xsd:double</c> in the half-open range [0.0, 1.0); for SPARQL <c>RAND()</c>.</summary>
    UniformDouble,

    /// <summary>A fresh UUID; for SPARQL <c>UUID()</c> and <c>STRUUID()</c>.</summary>
    Uuid,

    /// <summary>Raw entropy bytes; the requested length is in <see cref="RandomnessRequest.ByteCount"/>.</summary>
    Bytes
}
